// Editor-only converter, scoped to the ten supplied Fantasy Forest materials.
// No original shader, texture, material GUID, prefab or scene is replaced.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TDFF.URP17.Editor
{
    public sealed class TDFFMaterialConverter : EditorWindow
    {
        const string Prefix = "TDFF URP17/";
        const string BackupRoot = "Assets/TDFF_URP17/Editor/Backups";
        const string LastBatchKey = "TDFF.URP17.LastBatch.";
        static readonly Dictionary<string, string> Expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"M_tdFF_billboard_lod", "0458f785296697c419fd98b1b3a3b69c"},
            {"M_tdFF_Bush_Set", "5a3186dabc008924e96df2d587c7df96"},
            {"M_tdFF_Grass_Tall", "ea001a2ead151434690a5ea05c3eaceb"},
            {"M_tdFF_Spruce_Branch", "ea001a2ead151434690a5ea05c3eaceb"},
            {"M_tdFF_Spruce_Trunk", "ea001a2ead151434690a5ea05c3eaceb"},
            {"M_tdFF_Water", "31dd6c77fc616bb4787b719b84f38316"},
            {"M_tdFF_WaterFountain", "31dd6c77fc616bb4787b719b84f38316"},
            {"M_tdFF_WaterFountain_2", "31dd6c77fc616bb4787b719b84f38316"},
            {"M_tdFF_WaterFountain_3", "31dd6c77fc616bb4787b719b84f38316"},
            {"M_tdFF_WaterFountain_4", "31dd6c77fc616bb4787b719b84f38316"}
        };

        sealed class Candidate
        {
            public Material material;
            public string path, sourceGuid, target, baseProperty;
            public bool selected = true;
            public bool Compatibility => target == Prefix + "Vegetation Compatibility";
        }

        [Serializable] sealed class BackupEntry
        {
            public string originalGuid, originalPath, backupPath, targetShader;
        }
        [Serializable] sealed class Batch
        {
            public string unityVersion, createdUtc, status;
            public List<BackupEntry> entries = new List<BackupEntry>();
        }
        struct TextureValue
        {
            public Texture texture;
            public Vector2 scale, offset;
        }
        // Read saved values directly: Material.HasProperty does not expose stale or missing-shader values.
        sealed class Snapshot
        {
            public readonly Dictionary<string, TextureValue> textures = new Dictionary<string, TextureValue>();
            public readonly Dictionary<string, float> floats = new Dictionary<string, float>();
            public readonly Dictionary<string, Color> colors = new Dictionary<string, Color>();
            public readonly bool instancing, doubleSidedGI;
            public readonly MaterialGlobalIlluminationFlags giFlags;
            public Snapshot(Material material)
            {
                instancing = material.enableInstancing;
                doubleSidedGI = material.doubleSidedGI;
                giFlags = material.globalIlluminationFlags;
                var saved = new SerializedObject(material).FindProperty("m_SavedProperties");
                if (saved == null) throw new InvalidOperationException("Cannot read saved material properties: " + material.name);
                var tex = saved.FindPropertyRelative("m_TexEnvs");
                for (int i = 0; i < tex.arraySize; ++i)
                {
                    var pair = tex.GetArrayElementAtIndex(i);
                    var value = pair.FindPropertyRelative("second");
                    textures[pair.FindPropertyRelative("first").stringValue] = new TextureValue
                    {
                        texture = value.FindPropertyRelative("m_Texture").objectReferenceValue as Texture,
                        scale = value.FindPropertyRelative("m_Scale").vector2Value,
                        offset = value.FindPropertyRelative("m_Offset").vector2Value
                    };
                }
                var fs = saved.FindPropertyRelative("m_Floats");
                for (int i = 0; i < fs.arraySize; ++i)
                {
                    var pair = fs.GetArrayElementAtIndex(i);
                    floats[pair.FindPropertyRelative("first").stringValue] = pair.FindPropertyRelative("second").floatValue;
                }
                var cs = saved.FindPropertyRelative("m_Colors");
                for (int i = 0; i < cs.arraySize; ++i)
                {
                    var pair = cs.GetArrayElementAtIndex(i);
                    colors[pair.FindPropertyRelative("first").stringValue] = pair.FindPropertyRelative("second").colorValue;
                }
            }
            public float Float(string key, float fallback) => floats.TryGetValue(key, out var f) ? f : fallback;
            public bool HasTexture(string key) => textures.TryGetValue(key, out var t) && t.texture != null;
            public void TextureTo(Material material, string source, string destination)
            {
                if (!material.HasProperty(destination) || !textures.TryGetValue(source, out var value)) return;
                material.SetTexture(destination, value.texture);
                material.SetTextureScale(destination, value.scale);
                material.SetTextureOffset(destination, value.offset);
            }
            public void ApplyMatching(Material material)
            {
                // Query property types on the new shader. Some old files store _Metallic as BOTH texture and float.
                Shader shader = material.shader;
                for (int i = 0; i < shader.GetPropertyCount(); ++i)
                {
                    string key = shader.GetPropertyName(i);
                    switch (shader.GetPropertyType(i))
                    {
                        case ShaderPropertyType.Texture: TextureTo(material, key, key); break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range:
                            if (floats.TryGetValue(key, out var f)) material.SetFloat(key, f);
                            break;
                        case ShaderPropertyType.Color:
                            if (colors.TryGetValue(key, out var c)) material.SetColor(key, c);
                            break;
                        case ShaderPropertyType.Vector:
                            if (colors.TryGetValue(key, out var v)) material.SetVector(key, new Vector4(v.r,v.g,v.b,v.a));
                            break;
                    }
                }
                material.enableInstancing = instancing;
                material.doubleSidedGI = doubleSidedGI;
                material.globalIlluminationFlags = giFlags;
            }
        }

        [SerializeField] DefaultAsset folder;
        [SerializeField] bool includeCompatibility = true;
        [SerializeField] bool approximateWind;
        readonly List<Candidate> candidates = new List<Candidate>();
        Vector2 scroll;
        string summary = "Select the package folder in Project, then click Scan selected folder/materials.";
        string ProjectKey => LastBatchKey + Application.dataPath;

        [MenuItem("Tools/Top Down Fantasy Forest/URP 17 Material Converter")]
        public static void Open()
        {
            var w = GetWindow<TDFFMaterialConverter>("Fantasy Forest URP");
            w.minSize = new Vector2(610,480);
            w.Show();
        }
        void OnEnable()
        {
            string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (AssetDatabase.IsValidFolder(selectedPath)) folder = Selection.activeObject as DefaultAsset;
        }
        void OnGUI()
        {
            EditorGUILayout.LabelField("Top Down - Fantasy Forest / Unity 6 + URP 17", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Only the ten supplied material names AND their original shader GUIDs are eligible. Existing converted materials are skipped. Each row shows the proposed shader before conversion.", MessageType.Info);
            folder = (DefaultAsset)EditorGUILayout.ObjectField("Package folder", folder, typeof(DefaultAsset), false);
            if (GUILayout.Button("1. Scan selected folder/materials")) Scan();
            EditorGUILayout.HelpBox("Vegetation Compatibility is an approximate replacement for missing source shaders. It preserves texture references and available settings, but gradient, mask packing and wind are not an exact port. Wind is off by default.", MessageType.Warning);
            includeCompatibility = EditorGUILayout.ToggleLeft("Include vegetation compatibility conversions",includeCompatibility);
            using (new EditorGUI.DisabledScope(!includeCompatibility))
                approximateWind = EditorGUILayout.ToggleLeft("Enable approximate wind on compatibility materials",approximateWind);
            EditorGUILayout.LabelField(summary, EditorStyles.wordWrappedLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var row in candidates)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                row.selected = EditorGUILayout.ToggleLeft(row.material.name + "  →  " + row.target.Substring(Prefix.Length),row.selected);
                EditorGUILayout.LabelField(row.path,EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Source: " + (row.material.shader ? row.material.shader.name : "Missing shader"),EditorStyles.miniLabel);
                if (row.material.name == "M_tdFF_Bush_Set" && row.Compatibility)
                {
                    int choice = row.baseProperty == "_MainTex" ? 1 : 0;
                    choice = EditorGUILayout.Popup("Bush albedo property",choice,new[]{"_BaseColor (default)","_MainTex (alternative)"});
                    row.baseProperty = choice == 0 ? "_BaseColor" : "_MainTex";
                }
                if (!row.material.name.Contains("Water"))
                {
                    var s = new Snapshot(row.material);
                    if (s.textures.TryGetValue(row.baseProperty,out var tex))
                        EditorGUILayout.ObjectField("Albedo to keep",tex.texture,typeof(Texture),false);
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
            using (new EditorGUI.DisabledScope(!candidates.Any(IsChosen) || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("2. Backup + Convert checked materials",GUILayout.Height(32))) ConvertChecked();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Check target shader errors")) CheckShaderErrors();
            using (new EditorGUI.DisabledScope(!EditorPrefs.HasKey(ProjectKey)))
                if (GUILayout.Button("Restore last batch")) RestoreLast();
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Enable Depth + Opaque Texture for water (active URP asset)")) EnableWaterTextures();
        }
        bool IsChosen(Candidate c) => c.selected && (includeCompatibility || !c.Compatibility);

        static string ShaderGuid(Material material, string path)
        {
            if (material.shader && material.shader.name != "Hidden/InternalErrorShader" &&
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material.shader,out string guid,out long _) &&
                !String.IsNullOrEmpty(guid) && guid.Any(ch=>ch!='0')) return guid;
            // Missing shader reference: recover its GUID without changing the material or serialisation mode.
            if (File.Exists(path))
            {
                var match = Regex.Match(File.ReadAllText(path),@"m_Shader:\s*\{[^}]*guid:\s*([a-fA-F0-9]{32})");
                if (match.Success) return match.Groups[1].Value.ToLowerInvariant();
            }
            return "";
        }
        void Scan()
        {
            candidates.Clear();
            var paths = new HashSet<string>(StringComparer.Ordinal);
            string chosenFolder = AssetDatabase.GetAssetPath(folder);
            if (AssetDatabase.IsValidFolder(chosenFolder))
            {
                if (chosenFolder != "Assets" && !chosenFolder.StartsWith("Assets/",StringComparison.Ordinal))
                { summary = "Choose a folder inside Assets."; return; }
                foreach (string guid in AssetDatabase.FindAssets("t:Material",new[]{chosenFolder})) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            else
            {
                foreach (var obj in Selection.objects)
                {
                    string path = AssetDatabase.GetAssetPath(obj);
                    if (AssetDatabase.IsValidFolder(path) && (path == "Assets" || path.StartsWith("Assets/",StringComparison.Ordinal)))
                        foreach (string guid in AssetDatabase.FindAssets("t:Material",new[]{path})) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
                    else if (obj is Material) paths.Add(path);
                }
            }
            int alreadyConverted = 0, mismatched = 0;
            foreach (string path in paths.OrderBy(p=>p,StringComparer.Ordinal))
            {
                if (!path.StartsWith("Assets/",StringComparison.Ordinal) || path.Contains("/TDFF_URP17/") || !path.EndsWith(".mat",StringComparison.OrdinalIgnoreCase)) continue;
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material || !Expected.TryGetValue(material.name,out string expected)) continue;
                if (material.shader && (material.shader.name.StartsWith(Prefix,StringComparison.Ordinal) || material.shader.name.StartsWith("Universal Render Pipeline/",StringComparison.Ordinal)))
                { ++alreadyConverted; continue; }
                string guid = ShaderGuid(material,path);
                if (!String.Equals(guid,expected,StringComparison.OrdinalIgnoreCase)) { ++mismatched; continue; }
                string target = material.name.Contains("Water") ? "Simple Water" : material.name == "M_tdFF_billboard_lod" ? "Tree LOD" : "Vegetation Compatibility";
                if (material.name == "M_tdFF_Bush_Set" && material.shader && material.shader.name == "TriForge/StardardCullOff") target = "Standard Cull Off";
                var s = new Snapshot(material);
                string baseProperty = target == "Tree LOD" ? "_MainTex1" : "_MainTex";
                if (material.name == "M_tdFF_Bush_Set")
                {
                    // When the original shader exists, inspect its actual active property before considering stale saved maps.
                    bool baseActive = material.HasProperty("_BaseColor") && s.HasTexture("_BaseColor");
                    bool mainActive = material.HasProperty("_MainTex") && s.HasTexture("_MainTex");
                    baseProperty = baseActive ? "_BaseColor" : mainActive ? "_MainTex" : s.HasTexture("_BaseColor") ? "_BaseColor" : "_MainTex";
                }
                candidates.Add(new Candidate{material=material,path=path,sourceGuid=guid,target=Prefix+target,baseProperty=baseProperty});
            }
            summary = $"Ready: {candidates.Count} | Already converted: {alreadyConverted} | Name matched but shader GUID differed: {mismatched}.";
            if (candidates.Count == 0) summary += " Select the original package folder. See Console if shader import failed.";
        }

        static Shader ValidShader(string name)
        {
            var shader = Shader.Find(name);
            if (!shader) throw new InvalidOperationException("Target shader not found: " + name + ". Import the full TDFF_URP17 folder.");
            if (ShaderUtil.ShaderHasError(shader))
            {
                foreach (var message in ShaderUtil.GetShaderMessages(shader)) Debug.LogError(name+": "+message.message+" ("+message.file+":"+message.line+")");
                throw new InvalidOperationException("Target shader has compile errors: " + name + ". No materials were converted; see Console.");
            }
            if (!shader.isSupported) throw new InvalidOperationException("Target shader unsupported on the current editor graphics backend: " + name);
            return shader;
        }
        void CheckShaderErrors()
        {
            try
            {
                foreach (string name in new[]{"Tree LOD","Standard Cull Off","Simple Water","Vegetation Compatibility"}) ValidShader(Prefix+name);
                summary = "No currently reported target shader errors. Check Scene/Game and an Android device; this does not compile every build variant.";
            }
            catch (Exception ex) { summary=ex.Message; Debug.LogException(ex); }
        }
        static void EnsureAssetFolder(string path)
        {
            string parent = Path.GetDirectoryName(path).Replace('\\','/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureAssetFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
        }
        static void Keyword(Material m, string name, bool enabled)
        { if (enabled) m.EnableKeyword(name); else m.DisableKeyword(name); }
        static void Configure(Candidate c, Snapshot saved, Shader shader, bool wind)
        {
            var m = c.material;
            m.shader = shader;
            m.shaderKeywords = Array.Empty<string>();
            saved.ApplyMatching(m);
            m.SetShaderPassEnabled("ShadowCaster",true);
            m.SetShaderPassEnabled("DepthOnly",true);
            m.SetShaderPassEnabled("DepthNormals",true);
            m.renderQueue = -1; // use the new shader queue; don't retain stale Standard conversion queues.
            if (c.Compatibility)
            {
                saved.TextureTo(m,c.baseProperty,"_MainTex");
                if (!saved.HasTexture("_Normal") && saved.HasTexture("_BumpMap")) saved.TextureTo(m,"_BumpMap","_Normal");
                string mask = saved.HasTexture("_MaskMap") ? "_MaskMap" : saved.HasTexture("_MetallicRSmoothnessA") ? "_MetallicRSmoothnessA" : saved.HasTexture("_Metallic") ? "_Metallic" : "_MetallicGlossMap";
                saved.TextureTo(m,mask,"_MaskMap");
                bool trunk = m.name == "M_tdFF_Spruce_Trunk";
                m.SetFloat("_TDAlphaClip",trunk ? 0 : 1);
                m.SetFloat("_TDCull",trunk ? saved.Float("_CullMode",2) : 0);
                m.SetFloat("_TDUseMask",0); // unknown source packing must not be guessed.
                m.SetFloat("_TDWind",wind ? 1 : 0);
                m.SetFloat("_UseColorVariance",saved.HasTexture("_ColorVariationMask") ? saved.Float("_UseColorVariance",0) : 0);
                m.SetFloat("_Metallic",Mathf.Clamp01(saved.Float("_Metallic",0)));
                m.doubleSidedGI = !trunk;
                if (trunk) { m.renderQueue=(int)RenderQueue.Geometry; m.SetOverrideTag("RenderType","Opaque"); }
                else m.SetOverrideTag("RenderType","TransparentCutout");
            }
            else if (c.target.EndsWith("Simple Water",StringComparison.Ordinal))
            {
                m.SetOverrideTag("RenderType","Transparent");
                Keyword(m,"_SPECULARHIGHLIGHTS_OFF",saved.Float("_SpecularHighlights",1)<.5f);
                Keyword(m,"_ENVIRONMENTREFLECTIONS_OFF",saved.Float("_GlossyReflections",1)<.5f);
            }
            else { m.SetOverrideTag("RenderType","TransparentCutout"); m.doubleSidedGI=true; }
            EditorUtility.SetDirty(m);
        }
        void ConvertChecked()
        {
            var chosen = candidates.Where(IsChosen).ToList();
            if (chosen.Count == 0) return;
            Batch batch = null;
            string manifest = null;
            int undoGroup = -1;
            bool mutationStarted = false;
            try
            {
                if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) throw new InvalidOperationException("The current quality level must use Universal Render Pipeline.");
                var targets = chosen.Select(c=>c.target).Distinct().ToDictionary(s=>s,s=>ValidShader(s));
                var snapshots = new Dictionary<Candidate,Snapshot>();
                foreach (var c in chosen)
                {
                    if (!c.material || AssetDatabase.GetAssetPath(c.material) != c.path || ShaderGuid(c.material,c.path) != c.sourceGuid)
                        throw new InvalidOperationException("A material changed after Scan. Scan again.");
                    if (!AssetDatabase.IsOpenForEdit(c.path) || (File.GetAttributes(c.path) & FileAttributes.ReadOnly) != 0)
                        throw new InvalidOperationException("Material is read-only. Check it out first: " + c.path);
                    snapshots[c] = new Snapshot(c.material);
                }
                AssetDatabase.SaveAssets();
                string batchPath = BackupRoot+"/Batch_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff")+"_"+Guid.NewGuid().ToString("N").Substring(0,6);
                EnsureAssetFolder(batchPath);
                batch = new Batch{unityVersion=Application.unityVersion,createdUtc=DateTime.UtcNow.ToString("O"),status="backup-complete"};
                for (int i=0;i<chosen.Count;++i)
                {
                    var c=chosen[i];
                    string backup=batchPath+"/"+i.ToString("00")+"_"+c.material.name+".mat";
                    if (!AssetDatabase.CopyAsset(c.path,backup) || !AssetDatabase.LoadAssetAtPath<Material>(backup))
                        throw new IOException("Could not back up " + c.path + ". No conversion started.");
                    batch.entries.Add(new BackupEntry{originalGuid=AssetDatabase.AssetPathToGUID(c.path),originalPath=c.path,backupPath=backup,targetShader=c.target});
                }
                manifest=batchPath+"/manifest.json";
                File.WriteAllText(manifest,JsonUtility.ToJson(batch,true));
                AssetDatabase.ImportAsset(manifest);
                EditorPrefs.SetString(ProjectKey,manifest);
                Undo.IncrementCurrentGroup(); undoGroup=Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Convert Fantasy Forest materials to URP17");
                mutationStarted=true;
                for (int i=0;i<chosen.Count;++i)
                {
                    var c=chosen[i];
                    EditorUtility.DisplayProgressBar("Fantasy Forest URP conversion",c.material.name,(float)i/chosen.Count);
                    Undo.RegisterCompleteObjectUndo(c.material,"Convert Fantasy Forest material");
                    Configure(c,snapshots[c],targets[c.target],approximateWind);
                    if (ShaderUtil.ShaderHasError(c.material.shader)) throw new InvalidOperationException("Shader error after assignment: " + c.target);
                }
                AssetDatabase.SaveAssets();
                batch.status="converted";
                File.WriteAllText(manifest,JsonUtility.ToJson(batch,true));
                AssetDatabase.ImportAsset(manifest);
                Undo.CollapseUndoOperations(undoGroup);
                int count=chosen.Count;
                candidates.Clear();
                summary=$"Converted {count}. Backup: {manifest}. For water, enable Depth + Opaque Texture below. Check Scene/Game before Android build.";
                Debug.Log("TDFF URP17: "+summary);
            }
            catch (Exception ex)
            {
                string rollback="";
                if (mutationStarted && batch != null)
                {
                    try
                    {
                        if (undoGroup >= 0) Undo.RevertAllDownToGroup(undoGroup);
                        RestoreBatch(batch,false);
                        batch.status="rolled-back";
                        File.WriteAllText(manifest,JsonUtility.ToJson(batch,true));
                        AssetDatabase.ImportAsset(manifest);
                        rollback=" Changes rolled back.";
                    }
                    catch (Exception restoreError) { rollback=" Automatic rollback failed; use Restore last batch. "+restoreError.Message; Debug.LogException(restoreError); }
                }
                summary=ex.Message+rollback;
                Debug.LogException(ex);
            }
            finally { EditorUtility.ClearProgressBar(); Repaint(); }
        }
        static void RestoreBatch(Batch batch,bool recordUndo)
        {
            // Preflight every original and backup before changing any material. Resolve originals by GUID, allowing moves.
            var pairs=new List<KeyValuePair<Material,Material>>();
            foreach (var entry in batch.entries)
            {
                var original=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(entry.originalGuid));
                var backup=AssetDatabase.LoadAssetAtPath<Material>(entry.backupPath);
                if (!original || !backup) throw new IOException("Original or backup missing: "+entry.originalPath);
                string path=AssetDatabase.GetAssetPath(original);
                if (!AssetDatabase.IsOpenForEdit(path) || (File.GetAttributes(path)&FileAttributes.ReadOnly)!=0) throw new IOException("Read-only: "+path);
                pairs.Add(new KeyValuePair<Material,Material>(original,backup));
            }
            foreach (var pair in pairs)
            {
                if(recordUndo) Undo.RegisterCompleteObjectUndo(pair.Key,"Restore Fantasy Forest material");
                string name=pair.Key.name;
                EditorUtility.CopySerialized(pair.Value,pair.Key);
                pair.Key.name=name;
                EditorUtility.SetDirty(pair.Key);
            }
            AssetDatabase.SaveAssets();
        }
        void RestoreLast()
        {
            try
            {
                string path=EditorPrefs.GetString(ProjectKey,"");
                if (!File.Exists(path)) throw new IOException("Backup manifest not found: "+path);
                var batch=JsonUtility.FromJson<Batch>(File.ReadAllText(path));
                if (batch == null || batch.entries == null || batch.entries.Count == 0) throw new IOException("Empty backup manifest.");
                Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup();
                RestoreBatch(batch,true);
                Undo.CollapseUndoOperations(group);
                summary=$"Restored {batch.entries.Count} materials from {path}.";
                candidates.Clear();
            }
            catch(Exception ex) { summary=ex.Message; Debug.LogException(ex); }
        }
        void EnableWaterTextures()
        {
            var asset=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!asset) { summary="No active URP asset for the current quality level."; return; }
            Undo.RecordObject(asset,"Enable water depth and opaque textures");
            asset.supportsCameraDepthTexture=true;
            asset.supportsCameraOpaqueTexture=true;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            summary="Enabled Depth + Opaque Texture on "+asset.name+". Ensure the camera does not override them Off, and repeat for other quality-level URP assets if needed.";
        }
    }
}
