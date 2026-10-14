#if UNITY_EDITOR
using GameCreator.Runtime.Cameras;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class JourneyCameraCollisionSetup
{
    [MenuItem("Quran Kids/Enable Terrain Only Camera Collision")]
    private static void Enable()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("Run camera setup outside Play Mode."); return; }
        TCamera target = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<TCamera>() : null;
        if (target == null && Camera.main != null) target = Camera.main.GetComponent<TCamera>();
        if (target == null) { Debug.LogError("Select the actual Main Camera with the Game Creator camera component."); return; }
        var data = new SerializedObject(target);
        var clip = data.FindProperty("m_Clip");
        if (clip == null) { Debug.LogError("Camera clipping field not found in this Game Creator version."); return; }
        int terrainLayer = LayerMask.NameToLayer("TerrainCamera");
        if (terrainLayer < 0)
        {
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                { layers.GetArrayElementAtIndex(i).stringValue = "TerrainCamera"; tags.ApplyModifiedProperties(); terrainLayer = i; break; }
        }
        if (terrainLayer < 0) { Debug.LogError("Create a free layer named TerrainCamera first."); return; }
        foreach (var terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (terrain.gameObject.scene != target.gameObject.scene) continue;
            Undo.RecordObject(terrain.gameObject, "Terrain camera layer");
            terrain.gameObject.layer = terrainLayer;
            PrefabUtility.RecordPrefabInstancePropertyModifications(terrain.gameObject);
            var collider = terrain.GetComponent<TerrainCollider>();
            if (collider == null || !collider.enabled) Debug.LogWarning("Enable a TerrainCollider on this terrain.", terrain);
        }
        Undo.RecordObject(target, "Enable camera collision");
        if (!(clip.managedReferenceValue is CameraClipZoom))
            clip.managedReferenceValue = new CameraClipZoom { LayerMask = Physics.DefaultRaycastLayers, Radius = 0.4f, MinDistance = 0f };
        ((CameraClipZoom)clip.managedReferenceValue).LayerMask = 1 << terrainLayer;
        data.ApplyModifiedProperties();
        // Only the walking shot is changed. Puzzle shots retain their authored settings.
        foreach (var shot in Object.FindObjectsByType<ShotCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (shot.name != "Camera Shot" || shot.gameObject.scene != target.gameObject.scene) continue;
            var shotData = new SerializedObject(shot);
            var clipping = shotData.FindProperty("m_Clipping");
            if (clipping != null) { clipping.enumValueIndex = 0; shotData.ApplyModifiedProperties(); }
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
        Selection.activeGameObject = target.gameObject;
        Debug.Log("Walking camera now collides only with TerrainCamera. Terrain objects were assigned this layer; children were not changed. Check player ground masks include it, then save scene.", target);
    }
}
#endif
