#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FantasyForestTools
{
    public sealed class TerrainTreesToGameObjects : EditorWindow
    {
        [SerializeField] private Terrain terrain;
        [SerializeField] private int prototypeIndex = -1;
        [SerializeField] private int maximumPerBatch = 1000;
        [SerializeField] private bool removeConvertedTrees = true;
        [SerializeField] private bool preserveTint = true;
        [MenuItem("Tools/Fantasy Forest/Extract Terrain Trees to GameObjects")]
        private static void Open() { GetWindow<TerrainTreesToGameObjects>("Extract Terrain Trees"); }
        [MenuItem("CONTEXT/Terrain/Extract Trees to GameObjects")]
        private static void OpenContext(MenuCommand command)
        { var window = GetWindow<TerrainTreesToGameObjects>("Extract Terrain Trees"); window.terrain = command.context as Terrain; }
        private void OnEnable()
        { if (terrain == null && Selection.activeGameObject != null) terrain = Selection.activeGameObject.GetComponent<Terrain>(); }
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Creates separate prefab instances at the painted trees' positions, rotations and sizes. Grass/details are not trees.\nRemoving converted trees uses a NEW TerrainData asset; the original data is retained.", MessageType.Info);
            terrain = (Terrain)EditorGUILayout.ObjectField("Terrain", terrain, typeof(Terrain), true);
            prototypeIndex = EditorGUILayout.IntField("Prototype (-1 = All)", prototypeIndex);
            maximumPerBatch = Mathf.Clamp(EditorGUILayout.IntField("Maximum Per Batch", maximumPerBatch), 1, 10000);
            removeConvertedTrees = EditorGUILayout.Toggle("Remove Converted Trees", removeConvertedTrees);
            preserveTint = EditorGUILayout.Toggle("Preserve Tree Tint", preserveTint);
            if (terrain != null && terrain.terrainData != null)
                EditorGUILayout.LabelField("Painted tree count", terrain.terrainData.treeInstanceCount.ToString());
            using (new EditorGUI.DisabledScope(terrain == null || terrain.terrainData == null || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Extract Trees", GUILayout.Height(34f))) Extract();
        }
        private void Extract()
        {
            if (terrain == null || terrain.terrainData == null || !terrain.gameObject.scene.IsValid() || AssetDatabase.Contains(terrain.gameObject)) return;
            if (terrain.transform.rotation != Quaternion.identity || (terrain.transform.lossyScale - Vector3.one).sqrMagnitude > 0.00001f)
            { Debug.LogError("Terrain extraction expects the usual Terrain rotation (0,0,0) and scale (1,1,1). Terrain position may be anywhere.", terrain); return; }
            TerrainData original = terrain.terrainData; TreeInstance[] trees = original.treeInstances; TreePrototype[] prototypes = original.treePrototypes;
            if (trees.Length == 0) { Debug.Log("This Terrain has no painted tree instances.", terrain); return; }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Extract painted terrain trees");
            var parent = new GameObject(terrain.name + " Extracted Trees"); Undo.RegisterCreatedObjectUndo(parent, "Create extracted-tree group");
            SceneManager.MoveGameObjectToScene(parent, terrain.gameObject.scene); parent.transform.position = terrain.transform.position;
            var remaining = new List<TreeInstance>(trees.Length); int extracted = 0;
            try
            {
                for (int i = 0; i < trees.Length; i++)
                {
                    TreeInstance tree = trees[i];
                    if (extracted >= maximumPerBatch || prototypeIndex >= 0 && tree.prototypeIndex != prototypeIndex ||
                        tree.prototypeIndex < 0 || tree.prototypeIndex >= prototypes.Length || prototypes[tree.prototypeIndex].prefab == null)
                    { remaining.Add(tree); continue; }
                    if (i % 50 == 0 && EditorUtility.DisplayCancelableProgressBar("Extract Terrain Trees", extracted + " objects created", (float)i / trees.Length))
                    { for (int j = i; j < trees.Length; j++) remaining.Add(trees[j]); break; }
                    GameObject prefab = prototypes[tree.prototypeIndex].prefab;
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, terrain.gameObject.scene);
                    Undo.RegisterCreatedObjectUndo(instance, "Extract painted tree"); instance.name = prefab.name + " Tree " + (i + 1);
                    instance.transform.SetParent(parent.transform, false);
                    instance.transform.position = terrain.transform.position + Vector3.Scale(tree.position, original.size);
                    instance.transform.rotation = Quaternion.AngleAxis(tree.rotation * Mathf.Rad2Deg, Vector3.up) * prefab.transform.rotation;
                    instance.transform.localScale = Vector3.Scale(prefab.transform.localScale, new Vector3(tree.widthScale, tree.heightScale, tree.widthScale));
                    if (preserveTint) ApplyTint(instance, tree.color);
                    extracted++; if (!removeConvertedTrees) remaining.Add(tree);
                }
                if (extracted == 0) { Undo.DestroyObjectImmediate(parent); Debug.Log("No trees matched the selected prototype.", terrain); return; }
                if (removeConvertedTrees)
                {
                    const string folder = "Assets/Game/WorldSystems/ExtractedTrees"; JourneyWorldSetupUtility.EnsureFolder(folder);
                    var copy = Instantiate(original); copy.name = original.name + " Extracted Trees"; copy.SetTreeInstances(remaining.ToArray(), false);
                    string name = string.Concat(terrain.name.Split(System.IO.Path.GetInvalidFileNameChars()));
                    AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + "_TreesExtracted.asset"));
                    Undo.RecordObject(terrain, "Use extracted-tree TerrainData"); terrain.terrainData = copy;
                    var collider = terrain.GetComponent<TerrainCollider>();
                    if (collider != null && collider.terrainData == original)
                    { Undo.RecordObject(collider, "Keep terrain height collision data aligned"); collider.terrainData = copy; EditorUtility.SetDirty(collider); }
                    terrain.Flush(); EditorUtility.SetDirty(terrain); AssetDatabase.SaveAssets();
                }
                EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene); Selection.activeGameObject = parent;
                Debug.Log("Extracted " + extracted + " trees into '" + parent.name + "'. Original TerrainData retained. Save scene; repeat for the next batch if needed.", parent);
            }
            finally { EditorUtility.ClearProgressBar(); Undo.CollapseUndoOperations(group); }
        }
        private static void ApplyTint(GameObject instance, Color tint)
        {
            if (tint == Color.white) return;
            var color = Undo.AddComponent<JourneyExtractedTreeTint>(instance); color.tint = tint; color.Apply();
            EditorUtility.SetDirty(color); PrefabUtility.RecordPrefabInstancePropertyModifications(color);
        }
    }
}
#endif
