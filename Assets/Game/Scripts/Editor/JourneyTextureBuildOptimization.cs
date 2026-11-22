using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
internal sealed class JourneyTextureBuildOptimization : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    // TerrainData serialized on Terrain - Journey - Main in the first-grade scene.
    private const string TerrainGuid = "aa400634e1f2a3640903e697800a12d7";
    private const string Backups = "Library/JourneyTextureBuildBackup";
    public int callbackOrder => 9000;

    static JourneyTextureBuildOptimization()
    {
        // PostprocessBuild is skipped on some failures. Also recover after editor restart.
        if (Directory.Exists(Backups)) WatchForInterruptedBuild();
    }

    private static void WatchForInterruptedBuild()
    {
        EditorApplication.update -= RecoverWhenIdle;
        EditorApplication.update += RecoverWhenIdle;
    }

    private static void RecoverWhenIdle()
    {
        if (!BuildPipeline.isBuildingPlayer && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
        {
            Restore();
            EditorApplication.update -= RecoverWhenIdle;
        }
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        Restore();
        WatchForInterruptedBuild();
        try
        {
            SetTerrainResolution(report.summary.platform);
            int processed = 0;
            foreach (string path in BuildDependencies())
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                if (path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    StripMaterial(path);
                else if (path.EndsWith(".terrainlayer", StringComparison.OrdinalIgnoreCase))
                    StripLayer(path);
                else continue;
                ReleaseAfterBatch(++processed);
            }
            if (processed > 0) EditorUtility.UnloadUnusedAssetsImmediate();

        }
        catch
        {
            Restore();
            throw;
        }
    }

    private static string[] BuildDependencies()
    {
        var roots = new HashSet<string>();
        foreach (var scene in EditorBuildSettings.scenes)
            if (scene.enabled) roots.Add(scene.path);
        foreach (var asset in PlayerSettings.GetPreloadedAssets())
            if (asset) roots.Add(AssetDatabase.GetAssetPath(asset));
        foreach (string path in AssetDatabase.GetAllAssetPaths())
            if (path.StartsWith("Assets/", StringComparison.Ordinal) &&
                path.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0 &&
                path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) < 0 &&
                !AssetDatabase.IsValidFolder(path)) roots.Add(path);
        return AssetDatabase.GetDependencies(new List<string>(roots).ToArray(), true);
    }

    private static void StripLayer(string path)
    {
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        if (!layer || (!layer.normalMapTexture && !layer.maskMapTexture)) return;
        SaveAndBackup(layer, path);
        layer.normalMapTexture = null;
        layer.maskMapTexture = null;
        Save(layer);
    }

    private static void ReleaseAfterBatch(int count)
    {
        if (count % 8 == 0) EditorUtility.UnloadUnusedAssetsImmediate();
    }

    private static void SetTerrainResolution(BuildTarget target)
    {
        string path = AssetDatabase.GUIDToAssetPath(JourneyTerrainBuildSettings.instance.ResolveGuid(TerrainGuid));
        var data = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        if (!data)
        {
            Debug.LogWarning("Journey: Main TerrainData is missing. Skipping the 2048 terrain override.");
            return;
        }

        string platform = BuildPipeline.GetBuildTargetGroup(target).ToString();
        if (platform == "iOS") platform = "iPhone";
        var visited = new HashSet<string>();
        foreach (TerrainLayer layer in data.terrainLayers)
        {
            if (!layer || !layer.diffuseTexture) continue;
            string texturePath = AssetDatabase.GetAssetPath(layer.diffuseTexture);
            if (!visited.Add(texturePath) || !texturePath.StartsWith("Assets/", StringComparison.Ordinal)) continue;
            if (!(AssetImporter.GetAtPath(texturePath) is TextureImporter importer)) continue;
            var settings = importer.GetPlatformTextureSettings(platform);
            if (settings.overridden && settings.maxTextureSize == 2048) continue;
            if (!settings.overridden)
            {
                settings = importer.GetDefaultPlatformTextureSettings();
                settings.name = platform;
                settings.overridden = true;
            }
            Backup(texturePath + ".meta");
            settings.maxTextureSize = 2048;
            importer.SetPlatformTextureSettings(settings);
            importer.SaveAndReimport();
            if (importer.GetPlatformTextureSettings(platform).maxTextureSize != 2048)
                throw new BuildFailedException("Another texture import rule reset the terrain size: " + texturePath);
        }
    }

    private static void StripMaterial(string path)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material || !material.shader) return;
        var serialized = new SerializedObject(material);
        var slots = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
        if (slots == null) return;
        bool changed = false;
        for (int i = 0; i < slots.arraySize; i++)
        {
            var entry = slots.GetArrayElementAtIndex(i);
            string slot = entry.FindPropertyRelative("first").stringValue;
            var texture = entry.FindPropertyRelative("second.m_Texture");
            if (!texture.objectReferenceValue || !IsSurfaceMap(material.shader, slot)) continue;
            if (!changed) SaveAndBackup(material, path);
            texture.objectReferenceValue = null;
            changed = true;
        }
        if (!changed) return;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        foreach (string keyword in SurfaceKeywords) material.DisableKeyword(keyword);
        Save(material);
    }

    private static readonly string[] SurfaceKeywords = {
        "_NORMALMAP", "_PARALLAXMAP", "_OCCLUSIONMAP", "_METALLICSPECGLOSSMAP",
        "_METALLICGLOSSMAP", "_SPECGLOSSMAP", "_DETAIL_MULX2", "_DETAIL_SCALED"
    };
    private static readonly string[] SurfaceNames = {
        "normal", "bump", "rough", "smooth", "metal", "occlusion", "ambientoccl",
        "specular", "specgloss", "parallax", "heightmap", "displacement", "emissi",
        "maskmap", "detailmask", "detailalbedo", "clearcoat", "glossmap"
    };

    private static bool IsSurfaceMap(Shader shader, string slot)
    {
        int index = shader.FindPropertyIndex(slot);
        if (index >= 0 && (shader.GetPropertyFlags(index) & ShaderPropertyFlags.MainTexture) != 0)
            return false;
        string name = slot.ToLowerInvariant();
        if (name == "_maintex" || name == "_basemap" || name == "_basecolormap" || name == "_basecolor")
            return false;
        if (shader.name == "Standard" || shader.name == "Standard (Specular setup)" ||
            shader.name == "Universal Render Pipeline/Lit" || shader.name == "Universal Render Pipeline/Simple Lit" ||
            shader.name == "Universal Render Pipeline/Complex Lit")
            return true;
        foreach (string part in SurfaceNames)
            if (name.Contains(part)) return true;
        return name == "_ao" || name == "_aomap" || name == "_orm" || name == "_rma";
    }

    private static void SaveAndBackup(UnityEngine.Object asset, string path)
    {
        AssetDatabase.SaveAssetIfDirty(asset);
        Backup(path);
    }

    private static void Backup(string path)
    {
        string copy = Path.Combine(Backups, path);
        if (File.Exists(copy)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(copy));
        File.Copy(path, copy + ".pending", true);
        File.Move(copy + ".pending", copy);
    }

    private static void Save(UnityEngine.Object asset)
    {
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssetIfDirty(asset);
    }

    public void OnPostprocessBuild(BuildReport report) => Restore();

    private static void Restore()
    {
        if (!Directory.Exists(Backups)) return;
        int restored = 0;
        foreach (string file in Directory.GetFiles(Backups, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".pending", StringComparison.Ordinal)) continue;
            string path = file.Substring(Backups.Length + 1).Replace('\\', '/');
            File.Copy(file, path, true);
            string assetPath = path.EndsWith(".meta", StringComparison.Ordinal) ? path.Substring(0, path.Length - 5) : path;
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            File.Delete(file);
            ReleaseAfterBatch(++restored);
        }
        Directory.Delete(Backups, true);
    }
}
