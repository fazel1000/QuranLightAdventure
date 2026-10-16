// Unity 6 Editor: selection-scoped conversion, backup, Undo and restore.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace DreamscapeURPTestPatch
{
    public sealed class DreamscapeURPMaterials : EditorWindow
    {
        private const string Title = "Dreamscape URP Materials";
        private const string BackupRoot = "Assets/DreamscapeURP17/Backups";
        private List<string> candidates = new List<string>();
        private string scanReport = "Select a DREAMSCAPE material folder or individual materials in Project, then Scan Selection.";
        private Vector2 scroll;
        [SerializeField] private string lastManifest;
        private static readonly Dictionary<string, string> Mapping = new Dictionary<string, string>
        {
            { "Polyart/Dreamscape Foliage", "Polyart/Dreamscape URP/Foliage" },
            { "Polyart/Dreamscape Foliage Billboard", "Polyart/Dreamscape URP/Foliage Billboard" },
            { "Polyart/Dreamscape Grass", "Polyart/Dreamscape URP/Grass" },
            { "Polyart/Dreamscape Masked Lit", "Polyart/Dreamscape URP/Masked Lit" },
            { "Polyart/Dreamscape Simple Foliage", "Polyart/Dreamscape URP/Simple Foliage" },
            { "Polyart/Dreamscape Surface", "Polyart/Dreamscape URP/Surface" },
            { "Polyart/Dreamscape Surface Coverage", "Polyart/Dreamscape URP/Surface Coverage" },
            { "Polyart/Dreamscape Surface World Aligned", "Polyart/Dreamscape URP/Surface World Aligned" },
            { "Polyart/Dreamscape Water", "Polyart/Dreamscape URP/Water" },
            { "Polyart/Dreamscape Water River", "Polyart/Dreamscape URP/Water River" },
            { "Polyart/Dreamscape Waterfall", "Polyart/Dreamscape URP/Waterfall" },
            { "Polyart/Dreamscape Waterfall Foam", "Polyart/Dreamscape URP/Waterfall Foam" },
            { "Polyart/Dreamscape Waterfall Foam Volumetric", "Polyart/Dreamscape URP/Waterfall Foam Volumetric" },
            { "Dreamscape_WorldAlignedBlend", "Polyart/Dreamscape URP/World Aligned Blend" },
            { "Polyart/Utilities/Grid", "Polyart/Dreamscape URP/Utilities/Grid" },
        };
        [Serializable] private sealed class Entry { public string originalPath; public string originalGuid; public string backupPath; public string convertedShader; }
        [Serializable] private sealed class Manifest { public List<Entry> entries = new List<Entry>(); }

        [MenuItem("Tools/Dreamscape/URP Material Converter")]
        private static void Open() { GetWindow<DreamscapeURPMaterials>(Title).minSize = new Vector2(510, 400); }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("DREAMSCAPE Built-in -> URP 17 test port", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Keeps texture slots and original property names. Only scanned materials using the 15 original " +
                "Polyart/Dreamscape shaders are changed. Terrain, render-pipeline settings, other shaders, scenes and prefabs are not edited.", MessageType.Info);
            EditorGUILayout.HelpBox("TEST PATCH: validate in your Unity project and on Android. " +
                "Lighting can look different from Built-in. Water/river require Opaque + Depth Texture; waterfall requires Depth Texture. " +
                "This is not the publisher's official URP package.", MessageType.Warning);
            if (GUILayout.Button("1. Scan Selection", GUILayout.Height(30))) Scan();
            EditorGUILayout.HelpBox(scanReport, MessageType.Info);
            using (new EditorGUI.DisabledScope(candidates.Count == 0 || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("2. Back Up And Convert Scanned Materials", GUILayout.Height(36))) Convert();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Restore the most recent batch", EditorStyles.boldLabel);
            lastManifest = EditorGUILayout.TextField("Backup manifest", lastManifest ?? "");
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(lastManifest) || !File.Exists(lastManifest)))
                if (GUILayout.Button("Restore Last Conversion")) Restore();
            EditorGUILayout.HelpBox("The operation creates backup copies before changing materials. " +
                "Original material GUIDs remain the same, so existing models and prefabs keep their references. " +
                "If you changed materials after conversion, restore will replace those later property edits too.", MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void Scan()
        {
            candidates.Clear();
            var paths = new HashSet<string>();
            foreach (UnityEngine.Object item in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(item);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                if (path.StartsWith(BackupRoot + "/", StringComparison.Ordinal)) continue;
                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { path }))
                        paths.Add(AssetDatabase.GUIDToAssetPath(guid));
                }
                else if (item is Material) paths.Add(path);
            }
            int already = 0, unknown = 0;
            foreach (string path in paths)
            {
                if (path.StartsWith(BackupRoot + "/", StringComparison.Ordinal)) continue;
                Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m || AssetDatabase.IsSubAsset(m)) { unknown++; continue; }
                string shader = m.shader ? m.shader.name : "";
                if (Mapping.ContainsKey(shader)) candidates.Add(path);
                else if (shader.StartsWith("Polyart/Dreamscape URP/", StringComparison.Ordinal)) already++;
                else unknown++;
            }
            candidates.Sort(StringComparer.Ordinal);
            scanReport = "Ready: " + candidates.Count + ". Already converted: " + already + ". Other/unsupported: " + unknown +
                ".\nMissing/error shaders cannot be identified safely and are skipped.\n" +
                "Use the intended DREAMSCAPE Materials folder, rather than the entire Assets folder.";
        }

        private void Convert()
        {
            Manifest manifest = new Manifest();
            string folder = null;
            int undoGroup = -1;
            bool water = false;
            bool needsDepth = false;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
                RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
                if (!pipeline || pipeline.GetType().Name != "UniversalRenderPipelineAsset")
                    throw new InvalidOperationException("The active pipeline is not URP. Check Graphics / Quality settings. No settings were changed.");
                // Check everything before creating backups or touching materials.
                foreach (string path in candidates)
                {
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!material || !material.shader || !Mapping.ContainsKey(material.shader.name))
                        throw new InvalidOperationException("The selection changed or a material was edited. Scan again.");
                    Shader shader = Shader.Find(Mapping[material.shader.name]);
                    if (!shader) throw new InvalidOperationException("Import the entire Assets/DreamscapeURP17 folder first. Missing shader: " + Mapping[material.shader.name]);
                    if (ShaderUtil.ShaderHasError(shader) || !shader.isSupported)
                        throw new InvalidOperationException("The new shader has an error or is unsupported on the active platform: " + shader.name + ". Send the first shader error from Console.");
                }
                if (candidates.Count == 0) return;
                Directory.CreateDirectory(BackupRoot);
                AssetDatabase.Refresh();
                folder = AssetDatabase.GenerateUniqueAssetPath(BackupRoot + "/Batch_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                AssetDatabase.CreateFolder(BackupRoot, Path.GetFileName(folder));
                foreach (string path in candidates)
                {
                    Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    string backup = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Path.GetFileName(path));
                    if (!AssetDatabase.CopyAsset(path, backup)) throw new IOException("Could not back up " + path);
                    manifest.entries.Add(new Entry { originalPath = path, originalGuid = AssetDatabase.AssetPathToGUID(path),
                        backupPath = backup, convertedShader = Mapping[m.shader.name] });
                }
                lastManifest = folder + "/conversion-backup.json";
                File.WriteAllText(lastManifest, JsonUtility.ToJson(manifest, true));
                AssetDatabase.Refresh();
                Undo.IncrementCurrentGroup();
                undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Convert selected Dreamscape materials to URP");
                foreach (Entry entry in manifest.entries)
                {
                    Material m = AssetDatabase.LoadAssetAtPath<Material>(entry.originalPath);
                    Undo.RecordObject(m, "Convert Dreamscape material");
                    SerializedObject serialized = new SerializedObject(m);
                    SerializedProperty queueProperty = serialized.FindProperty("m_CustomRenderQueue");
                    int queue = queueProperty != null ? queueProperty.intValue : -1;
                    m.shader = Shader.Find(entry.convertedShader);
                    m.renderQueue = queue;
                    SyncKeywords(m, entry.convertedShader);
                    if (entry.convertedShader.EndsWith("/Water", StringComparison.Ordinal) || entry.convertedShader.EndsWith("/Water River", StringComparison.Ordinal)) water = true;
                    if (water || entry.convertedShader.EndsWith("/Waterfall", StringComparison.Ordinal)) needsDepth = true;
                    EditorUtility.SetDirty(m);
                }
                AssetDatabase.SaveAssets();
                Undo.CollapseUndoOperations(undoGroup);
                Debug.Log("Converted " + manifest.entries.Count + " DREAMSCAPE materials. Backup: " + folder);
                EditorUtility.DisplayDialog(Title, "Converted " + manifest.entries.Count + " materials.\nBackup: " + folder +
                    (needsDepth ? "\n\nEnable Depth Texture on the active URP Asset; camera overrides must not disable it." : "") +
                    (water ? "\nWater/river: also enable Opaque Texture. Original planar reflection scripts are not converted." : "") +
                    "\n\nInspect the results and test on your Android device.", "OK");
                candidates.Clear();
                scanReport = "Conversion complete. Check the scene and Console. Restore is available below.";
            }
            catch (Exception e)
            {
                if (undoGroup >= 0)
                {
                    Undo.RevertAllDownToGroup(undoGroup);
                    AssetDatabase.SaveAssets();
                }
                Debug.LogException(e);
                EditorUtility.DisplayDialog(Title, e.Message + "\nNo completed conversion was retained; any created backups remain available.", "OK");
            }
        }

        private static void Toggle(Material m, string property, string keyword)
        {
            if (!m.HasProperty(property)) return;
            if (m.GetFloat(property) > 0.5f) m.EnableKeyword(keyword); else m.DisableKeyword(keyword);
        }

        private static void EnumKeywords(Material m, string property, params string[] keywords)
        {
            if (!m.HasProperty(property)) return;
            int selected = Mathf.Clamp(Mathf.RoundToInt(m.GetFloat(property)), 0, keywords.Length - 1);
            for (int i = 0; i < keywords.Length; i++)
                if (i == selected) m.EnableKeyword(keywords[i]); else m.DisableKeyword(keywords[i]);
        }

        private static void SyncKeywords(Material m, string shader)
        {
            Toggle(m, "_GroundCoverON", "_GROUNDCOVERON_ON");
            Toggle(m, "_BlendnormalsON", "_BLENDNORMALSON_ON");
            Toggle(m, "_WorldtoObjectPosition", "_WORLDTOOBJECTPOSITION_ON");
        }

        private void Restore()
        {
            try
            {
                Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(lastManifest));
                if (manifest == null || manifest.entries == null) throw new InvalidOperationException("Invalid backup manifest.");
                // Resolve all original GUIDs first; do not silently restore to a different asset.
                foreach (Entry entry in manifest.entries)
                {
                    string path = AssetDatabase.GUIDToAssetPath(entry.originalGuid);
                    Material original = AssetDatabase.LoadAssetAtPath<Material>(path);
                    Material backup = AssetDatabase.LoadAssetAtPath<Material>(entry.backupPath);
                    if (!original || !backup) throw new InvalidOperationException("Original material or backup missing: " + entry.originalPath);
                }
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Restore Dreamscape materials");
                foreach (Entry entry in manifest.entries)
                {
                    Material original = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(entry.originalGuid));
                    Material backup = AssetDatabase.LoadAssetAtPath<Material>(entry.backupPath);
                    Undo.RecordObject(original, "Restore Dreamscape material");
                    string name = original.name;
                    EditorUtility.CopySerialized(backup, original);
                    original.name = name;
                    EditorUtility.SetDirty(original);
                }
                AssetDatabase.SaveAssets();
                Undo.CollapseUndoOperations(group);
                Debug.Log("Restored " + manifest.entries.Count + " original DREAMSCAPE materials.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog(Title, e.Message, "OK");
            }
        }
    }
}
#endif
