using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class JourneyTreeLodSetup
{
    private const string Folder = "Assets/Game/JourneyTreeLODs";

    [MenuItem("Tools/Journey/Graphics/Register Selected Tree LODs")]
    private static void Register()
    {
        var scene = SceneManager.GetActiveScene();
        JourneyGraphicsSettings settings = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            settings = root.GetComponentInChildren<JourneyGraphicsSettings>(true);
            if (settings) break;
        }
        if (!settings || Application.isPlaying)
        {
            Debug.LogWarning("Open the gameplay scene outside Play Mode first.");
            return;
        }
        var sources = new HashSet<GameObject>();
        foreach (var selected in Selection.gameObjects)
        {
            string path = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrEmpty(path))
                path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(selected);
            if (string.IsNullOrEmpty(path) || path.StartsWith(Folder + "/")) continue;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var group = source ? source.GetComponent<LODGroup>() : null;
            if (group && group.lodCount >= 2) sources.Add(source);
        }
        if (sources.Count == 0)
        {
            Debug.LogWarning("Select a tree prefab with an LODGroup on the root and at least two levels.");
            return;
        }
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Game", "JourneyTreeLODs");
        var quality = settings.GetComponent<JourneyTreeQuality>();
        if (!quality) quality = Undo.AddComponent<JourneyTreeQuality>(settings.gameObject);
        Undo.RecordObject(quality, "Register tree LODs");
        foreach (var source in sources)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            var high = Bake(source, 1, Folder + "/" + guid + "_High.prefab");
            var low = source.GetComponent<LODGroup>().lodCount > 2
                ? Bake(source, 2, Folder + "/" + guid + "_Low.prefab") : high;
            var tree = quality.trees.Find(t => t.original == source);
            if (tree == null) { tree = new JourneyTreeQuality.Tree(); quality.trees.Add(tree); }
            tree.original = source;
            tree.high = high;
            tree.low = low;
        }
        EditorUtility.SetDirty(quality);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Registered {sources.Count} tree prefab(s). Save the scene.");
    }

    private static GameObject Bake(GameObject source, int first, string path)
    {
        var copy = UnityEngine.Object.Instantiate(source);
        try
        {
            copy.name = source.name;
            var group = copy.GetComponent<LODGroup>();
            var lods = group.GetLODs();
            var reduced = new LOD[lods.Length - first];
            Array.Copy(lods, first, reduced, 0, reduced.Length);
            var keep = new HashSet<Renderer>();
            foreach (var lod in reduced) keep.UnionWith(lod.renderers);
            group.SetLODs(reduced);
            for (int i = 0; i < first; i++)
                foreach (var renderer in lods[i].renderers)
                {
                    if (!renderer || keep.Contains(renderer)) continue;
                    var filter = renderer.GetComponent<MeshFilter>();
                    UnityEngine.Object.DestroyImmediate(renderer);
                    if (filter && !filter.GetComponent<Renderer>()) UnityEngine.Object.DestroyImmediate(filter);
                }
            var result = PrefabUtility.SaveAsPrefabAsset(copy, path);
            if (!result) throw new InvalidOperationException("Failed to create tree prefab: " + path);
            return result;
        }
        finally { UnityEngine.Object.DestroyImmediate(copy); }
    }
}
