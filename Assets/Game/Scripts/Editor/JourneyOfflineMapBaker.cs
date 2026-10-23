#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameCreator.Runtime.Characters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class JourneyOfflineMapBaker
{
    [MenuItem("Quran Kids/Bake Offline Minimap")]
    private static void BakeActive()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { Debug.LogError("Exit Play Mode before baking the offline minimap."); return; }
        var map = Object.FindObjectsByType<JourneyMinimap>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(m => m.gameObject.scene == SceneManager.GetActiveScene());
        if (map == null)
        {
            Debug.LogError("No JourneyMinimap exists in active scene '" + SceneManager.GetActiveScene().name +
                "'. Run Quran Kids > Setup Air Water and World Systems outside Play Mode. Wait for OK: Offline minimap UI and live markers, then save the scene and retry Bake. Inspect the first FAILED setup step if setup stops.");
            return;
        }
        Bake(map);
    }
    public static void Bake(JourneyMinimap map)
    {
        if (EditorApplication.isPlaying || map == null) return;
        var scene = map.gameObject.scene;
        var terrains = Terrain.activeTerrains.Where(t => t.gameObject.scene == scene && t.terrainData != null).ToArray();
        if (terrains.Length == 0) { Debug.LogError("A loaded Terrain is needed to choose the map capture bounds.", map); return; }
        Bounds bounds = new Bounds(terrains[0].transform.position, Vector3.zero);
        foreach (var terrain in terrains)
        { bounds.Encapsulate(terrain.transform.position); bounds.Encapsulate(terrain.transform.position + terrain.terrainData.size); }
        var roots = scene.GetRootGameObjects();
        foreach (var marker in roots.SelectMany(go => go.GetComponentsInChildren<JourneyMinimapMarker>(true))) bounds.Encapsulate(marker.Target.position);
        float span = Mathf.Max(bounds.size.x, bounds.size.z) + 20f;
        Vector2 center = new Vector2(bounds.center.x, bounds.center.z);
        var rect = new Rect(center.x - span * 0.5f, center.y - span * 0.5f, span, span);
        int resolution = Mathf.Clamp(Mathf.NextPowerOfTwo(map.captureResolution), 256, 4096);
        var hidden = new Dictionary<Renderer, bool>(); var canvases = new Dictionary<Canvas, bool>();
        void Hide(Transform root)
        {
            if (root == null) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) if (!hidden.ContainsKey(renderer)) { hidden[renderer] = renderer.enabled; renderer.enabled = false; }
        }
        RenderTexture previous = RenderTexture.active; bool fog = RenderSettings.fog; Texture2D image = null;
        GameObject cameraObject = null; Camera camera = null; RenderTexture target = null;
        try
        {
            foreach (var root in roots)
            {
                foreach (var actor in root.GetComponentsInChildren<Character>(true)) Hide(actor.transform);
                foreach (var marker in root.GetComponentsInChildren<JourneyMinimapMarker>(true)) Hide(marker.transform);
                foreach (var guard in root.GetComponentsInChildren<JourneyRiderGuard>(true)) Hide(guard.transform);
                foreach (var skeleton in root.GetComponentsInChildren<JourneySkeletonEnemy>(true)) Hide(skeleton.transform);
                foreach (var drone in root.GetComponentsInChildren<JourneyDroneLauncher>(true)) Hide(drone.transform);
                foreach (var appearance in root.GetComponentsInChildren<JourneyWardrobeAppearance>(true)) Hide(appearance.transform);
                foreach (var particles in root.GetComponentsInChildren<ParticleSystemRenderer>(true)) Hide(particles.transform);
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true)) { canvases[canvas] = canvas.enabled; canvas.enabled = false; }
            }
            cameraObject = new GameObject("Offline Map Capture") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.orthographic = true; camera.orthographicSize = span * 0.5f; camera.aspect = 1f;
            camera.transform.SetPositionAndRotation(new Vector3(center.x, bounds.max.y + 100f, center.y), Quaternion.Euler(90f, 0f, 0f));
            camera.nearClipPlane = 0.1f; camera.farClipPlane = Mathf.Max(500f, bounds.size.y + 300f);
            camera.cullingMask = map.captureLayers; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.12f, 0.22f, 0.25f);
            camera.allowHDR = false; camera.allowMSAA = false;
            var data = cameraObject.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = false; data.volumeLayerMask = 0;
            target = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32); target.Create();
            RenderSettings.fog = false;
            if (GraphicsSettings.currentRenderPipeline == null) { camera.targetTexture = target; camera.Render(); }
            else
            {
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                if (!RenderPipeline.SupportsRenderRequest(camera, request)) throw new System.InvalidOperationException("The active renderer does not support a URP SingleCameraRequest. Use the scene's URP renderer, allow Scene View to render, then retry.");
                RenderPipeline.SubmitRenderRequest(camera, request);
            }
            RenderTexture.active = target; image = new Texture2D(resolution, resolution, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, resolution, resolution), 0, 0); image.Apply();
            JourneyWorldSetupUtility.EnsureFolder(JourneyWorldSetupUtility.Folder + "/Maps");
            string path = JourneyWorldSetupUtility.Folder + "/Maps/" + scene.name.Replace(' ', '_') + ".png";
            File.WriteAllBytes(path, image.EncodeToPNG()); AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.textureType = TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Clamp; importer.mipmapEnabled = false; importer.maxTextureSize = resolution; importer.SaveAndReimport();
            Undo.RecordObject(map, "Assign baked map and matching coordinates"); map.offlineMap = AssetDatabase.LoadAssetAtPath<Texture2D>(path); map.worldRect = rect;
            if (map.mapImage != null) { Undo.RecordObject(map.mapImage, "Show offline map"); map.mapImage.texture = map.offlineMap; JourneyWorldSetupUtility.Dirty(map.mapImage); }
            JourneyWorldSetupUtility.Dirty(map); EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets();
            Debug.Log("Offline minimap saved: " + path + ". The capture camera is removed; only 2D icons update during gameplay.", map);
        }
        finally
        {
            RenderSettings.fog = fog; RenderTexture.active = previous;
            foreach (var item in hidden) if (item.Key != null) item.Key.enabled = item.Value;
            foreach (var item in canvases) if (item.Key != null) item.Key.enabled = item.Value;
            if (camera != null) camera.targetTexture = null;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            if (image != null) Object.DestroyImmediate(image);
        }
    }
}

[CustomEditor(typeof(JourneyMinimap))]
public sealed class JourneyMinimapEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            if (GUILayout.Button("Bake Offline Map From Loaded Terrain")) JourneyOfflineMapBaker.Bake((JourneyMinimap)target);
    }
}
#endif
