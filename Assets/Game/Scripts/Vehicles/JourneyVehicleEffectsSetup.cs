#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class JourneyVehicleEffectsSetup
{
    [MenuItem("Quran Kids/Add Journey Vehicle Effects")]
    private static void Setup()
    {
        var selected = Selection.activeGameObject;
        var vehicle = selected != null ? selected.GetComponentInParent<JourneyVehicle>() : null;
        if (vehicle == null) { EditorUtility.DisplayDialog("Vehicle Effects", "Select the car with JourneyVehicle first.", "OK"); return; }
        Shader shader = Shader.Find("Journey/Vehicle Effects");
        if (shader == null) { Debug.LogError("Import JourneyVehicleEffects.shader first."); return; }
        var effects = vehicle.GetComponent<JourneyVehicleEffects>();
        if (effects == null) effects = Undo.AddComponent<JourneyVehicleEffects>(vehicle.gameObject);
        Undo.RecordObject(effects, "Connect vehicle effects materials");
        const string folder = "Assets/Game/VehicleEffects";
        EnsureFolder(folder);
        if (effects.skidMaterial == null) effects.skidMaterial = GetMaterial(folder + "/Skid.mat", shader);
        if (effects.smokeMaterial == null)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/Smoke.asset");
            if (texture == null)
            {
                texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "Soft Smoke", wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color[1024];
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                {
                    float radius = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
                    pixels[y * 32 + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - radius), 2f));
                }
                texture.SetPixels(pixels); texture.Apply();
                AssetDatabase.CreateAsset(texture, folder + "/Smoke.asset");
            }
            var material = GetMaterial(folder + "/Smoke.mat", shader);
            material.mainTexture = texture;
            EditorUtility.SetDirty(material);
            effects.smokeMaterial = material;
        }
        EditorUtility.SetDirty(effects);
        PrefabUtility.RecordPrefabInstancePropertyModifications(effects);
        EditorSceneManager.MarkSceneDirty(vehicle.gameObject.scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = vehicle.gameObject;
        Debug.Log("Vehicle effects added. Assign Brake Loop and Exhaust Points, then save the scene. No wheel or button setup was changed.", effects);
    }
    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string parent = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = parent + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                string guid = AssetDatabase.CreateFolder(parent, parts[i]);
                if (string.IsNullOrEmpty(guid))
                    throw new System.InvalidOperationException("Could not create asset folder: " + next);
            }
            parent = next;
        }
    }
    private static Material GetMaterial(string path, Shader shader)
    {
        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(shader);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
#endif
