// NatureManufacture Fire & Smoke -> URP material converter
// Unity 6 / URP. Place this file in Assets/Editor/.
// It backs up each material before changing its shader.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class NatureManufactureParticlesURPConverter
{
    private const string BackupRoot = "Assets/URP_Conversion_Backups/NatureManufactureParticles";
    private const string GeneratedTextureRoot = "Assets/URP_ConvertedTextures/NatureManufactureParticles";

    private enum TextureBuildMode
    {
        FireRgbToAlpha,
        AlphaOnly,
        RedMaskAndAlpha
    }

    private static readonly Dictionary<string, Texture2D> GeneratedTextureCache =
        new Dictionary<string, Texture2D>();

    [MenuItem("Tools/NatureManufacture/Convert Fire & Smoke Materials to URP")]
    private static void ConvertMaterials()
    {
        if (!EditorUtility.DisplayDialog(
                "Convert NatureManufacture Fire & Smoke Materials",
                "This will find materials using the five NatureManufacture particle shaders and switch them to URP Particles/Lit or Particles/Unlit.\n\n" +
                "A backup copy of each material will be saved under:\n" + BackupRoot +
                "\n\nThis is a compatibility conversion, not a pixel-perfect shader port. Custom six-way lighting, wind deformation, emission-over-time logic, and some intersection effects will not be identical. Test the result on a few particle systems before building.",
                "Convert with Backups", "Cancel"))
            return;

        Shader urpUnlit = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Shader urpLit = Shader.Find("Universal Render Pipeline/Particles/Lit");

        if (urpUnlit == null || urpLit == null)
        {
            EditorUtility.DisplayDialog(
                "URP particle shaders not found",
                "Unity could not find one or both URP particle shaders.\n\n" +
                "Check that the Universal Render Pipeline package is installed and that the project is using a URP Render Pipeline Asset, then try again.",
                "OK");
            return;
        }

        GeneratedTextureCache.Clear();
        string[] materialGuids = AssetDatabase.FindAssets("t:Material");
        int converted = 0;
        int skipped = 0;
        int backupFailures = 0;
        List<string> convertedNames = new List<string>();

        try
        {
            for (int i = 0; i < materialGuids.Length; i++)
            {
                EditorUtility.DisplayProgressBar(
                    "Converting NatureManufacture particle materials",
                    "Checking material " + (i + 1) + " / " + materialGuids.Length,
                    materialGuids.Length == 0 ? 1f : (float)i / materialGuids.Length);

                string path = AssetDatabase.GUIDToAssetPath(materialGuids[i]);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader == null)
                    continue;

                string oldShaderName = material.shader.name;
                bool isFire = oldShaderName == "NatureManufacture/Particles/Fire Unlit";
                bool isLightmapLit = oldShaderName == "NatureManufacture/Particles/Smoke Lightmap Lit";
                bool isLightmapUnlit = oldShaderName == "NatureManufacture/Particles/Smoke Lightmap Unlit";
                bool isNormalLit = oldShaderName == "NatureManufacture/Particles/Smoke Normalmap Lit";
                bool isNormalUnlit = oldShaderName == "NatureManufacture/Particles/Smoke Normalmap Unlit";

                if (!(isFire || isLightmapLit || isLightmapUnlit || isNormalLit || isNormalUnlit))
                    continue;

                if (!CreateBackup(path))
                {
                    backupFailures++;
                    Debug.LogError("[NatureManufacture URP Converter] Backup failed; material was not changed: " + path, material);
                    continue;
                }

                Shader targetShader = (isLightmapLit || isNormalLit) ? urpLit : urpUnlit;

                Texture2D fireTexture = GetTexture(material, "_Emission_Flipbook");
                Texture2D lightmapTransparency = GetTexture(material, "_Lightmap_Front_R_Back_G_Emission_B_Transparency_A");
                Texture2D normalColorMask = GetTexture(material, "_Color_Mask_R_Emission_B_Transparency_A");
                Texture2D normalMap = GetTexture(material, "_Normal_Map");

                Color emissionColor = GetColor(material, "_Emission_Color", Color.black);
                Color lightColor = GetColor(material, "_Light_Color", Color.white);
                Color particleColor = GetColor(material, "_Particle_Color_RGB_Alpha_A", Color.white);
                float alphaMultiplier = Mathf.Clamp01(GetFloat(material, "_Alpha_Multiplier", 0.5f));
                float emissionIntensity = Mathf.Max(0f, GetFloat(material, "_Emission_Intensity", 1f));
                float normalStrength = GetFloat(material, "_Normal_Map_Strength",
                    GetFloat(material, "_Normal_Strength", 0.5f));
                float metallic = Mathf.Clamp01(GetFloat(material, "_Metallic", 0f));
                float smoothness = Mathf.Clamp01(GetFloat(material, "_Smoothness", 0.1f));
                bool useIntersection = GetFloat(material, "USE_TRANSPARENCY_INTERSECTION", 0f) > 0.5f;

                Texture2D replacementBaseMap = null;
                Vector2 textureScale = Vector2.one;
                Vector2 textureOffset = Vector2.zero;

                if (isFire && fireTexture != null)
                {
                    replacementBaseMap = BuildTexture(fireTexture, TextureBuildMode.FireRgbToAlpha, alphaMultiplier);
                    textureScale = GetTextureScaleSafe(material, "_Emission_Flipbook");
                    textureOffset = GetTextureOffsetSafe(material, "_Emission_Flipbook");
                }
                else if ((isLightmapLit || isLightmapUnlit) && lightmapTransparency != null)
                {
                    // The source texture packs directional lighting/emission in RGB and transparency in A.
                    // A standard URP particle shader cannot reproduce that packed lighting, so preserve its alpha silhouette.
                    replacementBaseMap = BuildTexture(lightmapTransparency, TextureBuildMode.AlphaOnly, 1f);
                    textureScale = GetTextureScaleSafe(material, "_Lightmap_Front_R_Back_G_Emission_B_Transparency_A");
                    textureOffset = GetTextureOffsetSafe(material, "_Lightmap_Front_R_Back_G_Emission_B_Transparency_A");
                }
                else if ((isNormalLit || isNormalUnlit) && normalColorMask != null)
                {
                    // Preserve the source R color mask and A transparency channel.
                    replacementBaseMap = BuildTexture(normalColorMask, TextureBuildMode.RedMaskAndAlpha, 1f);
                    textureScale = GetTextureScaleSafe(material, "_Color_Mask_R_Emission_B_Transparency_A");
                    textureOffset = GetTextureOffsetSafe(material, "_Color_Mask_R_Emission_B_Transparency_A");
                }

                Undo.RecordObject(material, "Convert NatureManufacture particle material to URP");
                material.shader = targetShader;

                if (replacementBaseMap != null && material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", replacementBaseMap);
                    material.SetTextureScale("_BaseMap", textureScale);
                    material.SetTextureOffset("_BaseMap", textureOffset);
                }

                Color tint;
                if (isFire)
                {
                    tint = emissionColor * emissionIntensity;
                    tint.a = 1f; // Alpha is carried by the generated fire texture.
                }
                else if (isNormalLit || isNormalUnlit)
                {
                    tint = particleColor;
                    tint.a = Mathf.Clamp01((particleColor.a <= 0.001f ? 1f : particleColor.a) * alphaMultiplier);
                }
                else
                {
                    tint = lightColor;
                    tint.a = Mathf.Clamp01((lightColor.a <= 0.001f ? 1f : lightColor.a) * alphaMultiplier);
                }

                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", tint);

                // Match the source shaders' transparent, double-sided rendering as closely as possible.
                ConfigureTransparentParticleMaterial(material);

                if ((isNormalLit || isNormalUnlit) && normalMap != null && material.HasProperty("_BumpMap"))
                {
                    material.SetTexture("_BumpMap", normalMap);
                    if (material.HasProperty("_BumpScale"))
                        material.SetFloat("_BumpScale", Mathf.Clamp01(Mathf.Abs(normalStrength)));
                }

                if (material.HasProperty("_Metallic"))
                    material.SetFloat("_Metallic", metallic);
                if (material.HasProperty("_Smoothness"))
                    material.SetFloat("_Smoothness", smoothness);

                // URP Soft Particles are the closest standard replacement for the source intersection fade.
                if (material.HasProperty("_SoftParticlesEnabled"))
                {
                    material.SetFloat("_SoftParticlesEnabled", useIntersection ? 1f : 0f);
                    if (useIntersection)
                        material.EnableKeyword("_SOFTPARTICLES_ON");
                    else
                        material.DisableKeyword("_SOFTPARTICLES_ON");
                }

                // Preserve the old material name and asset GUID; only its shader and compatible properties change.
                EditorUtility.SetDirty(material);
                converted++;
                convertedNames.Add(material.name + "  (" + oldShaderName + ")");
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Conversion stopped", "An unexpected error occurred. See the Console for details.\n\nAlready converted materials have backups.", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        string report =
            "Converted materials: " + converted +
            "\nSkipped because backup failed: " + backupFailures +
            "\n\nBackups:\n" + BackupRoot +
            "\n\nGenerated textures:\n" + GeneratedTextureRoot +
            "\n\nCheck the Console for details. Test each effect in Scene/Game view before building.";

        EditorUtility.DisplayDialog("NatureManufacture URP conversion finished", report, "OK");
        Debug.Log("[NatureManufacture URP Converter] Finished. Converted: " + converted +
                  ", backup failures: " + backupFailures + ". Converted materials: " +
                  string.Join(", ", convertedNames.ToArray()));
    }

    private static void ConfigureTransparentParticleMaterial(Material material)
    {
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f); // Transparent
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f); // Alpha blend
        if (material.HasProperty("_Cull"))
            material.SetFloat("_Cull", (float)CullMode.Off);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);

        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_SrcBlendAlpha"))
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        if (material.HasProperty("_DstBlendAlpha"))
            material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);

        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHAMODULATE_ON");
        material.DisableKeyword("_ALPHATEST_ON");
    }

    private static bool CreateBackup(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath) || !sourcePath.StartsWith("Assets/", StringComparison.Ordinal))
            return false;

        string guid = AssetDatabase.AssetPathToGUID(sourcePath);
        string fileName = Path.GetFileNameWithoutExtension(sourcePath);
        string backupPath = BackupRoot + "/" + guid + "_" + fileName + ".mat";
        string folder = Path.GetDirectoryName(backupPath);

        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        if (File.Exists(backupPath))
            return true; // Keep the first/original backup; never overwrite it on a later run.

        return AssetDatabase.CopyAsset(sourcePath, backupPath);
    }

    private static Texture2D BuildTexture(Texture2D source, TextureBuildMode mode, float alphaMultiplier)
    {
        if (source == null)
            return null;

        string sourcePath = AssetDatabase.GetAssetPath(source);
        string cacheKey = sourcePath + "|" + mode + "|" + alphaMultiplier.ToString("F3");
        if (GeneratedTextureCache.TryGetValue(cacheKey, out Texture2D cached) && cached != null)
            return cached;

        if (string.IsNullOrEmpty(sourcePath))
            return source;

        string guid = AssetDatabase.AssetPathToGUID(sourcePath);
        if (string.IsNullOrEmpty(guid))
            guid = Math.Abs(sourcePath.GetHashCode()).ToString("X8");

        string suffix = mode.ToString();
        string outputPath = GeneratedTextureRoot + "/" +
                            Path.GetFileNameWithoutExtension(sourcePath) + "_" +
                            guid.Substring(0, Math.Min(8, guid.Length)) + "_" + suffix + ".png";

        try
        {
            TextureImporter sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            bool restoreReadable = sourceImporter != null && !sourceImporter.isReadable;

            if (restoreReadable)
            {
                sourceImporter.isReadable = true;
                sourceImporter.SaveAndReimport();
                source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            }

            Color32[] sourcePixels;
            try
            {
                sourcePixels = source.GetPixels32();
            }
            finally
            {
                if (restoreReadable)
                {
                    TextureImporter importerToRestore = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
                    if (importerToRestore != null)
                    {
                        importerToRestore.isReadable = false;
                        importerToRestore.SaveAndReimport();
                    }
                }
            }

            Color32[] outputPixels = new Color32[sourcePixels.Length];
            for (int i = 0; i < sourcePixels.Length; i++)
            {
                Color32 p = sourcePixels[i];

                if (mode == TextureBuildMode.FireRgbToAlpha)
                {
                    byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(((p.r + p.g + p.b) / 3f) * alphaMultiplier), 0, 255);
                    outputPixels[i] = new Color32(p.r, p.g, p.b, alpha);
                }
                else if (mode == TextureBuildMode.AlphaOnly)
                {
                    outputPixels[i] = new Color32(255, 255, 255, p.a);
                }
                else // RedMaskAndAlpha
                {
                    outputPixels[i] = new Color32(p.r, p.r, p.r, p.a);
                }
            }

            Directory.CreateDirectory(Path.GetFullPath(GeneratedTextureRoot));
            Texture2D outputTexture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, mode != TextureBuildMode.FireRgbToAlpha);
            outputTexture.SetPixels32(outputPixels);
            outputTexture.Apply(true, false);
            byte[] png = outputTexture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(outputTexture);

            File.WriteAllBytes(Path.GetFullPath(outputPath), png);
            AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);

            TextureImporter generatedImporter = AssetImporter.GetAtPath(outputPath) as TextureImporter;
            if (generatedImporter != null)
            {
                generatedImporter.textureType = TextureImporterType.Default;
                generatedImporter.alphaSource = TextureImporterAlphaSource.FromInput;
                generatedImporter.alphaIsTransparency = true;
                generatedImporter.sRGBTexture = mode == TextureBuildMode.FireRgbToAlpha;
                generatedImporter.mipmapEnabled = true;
                generatedImporter.isReadable = false;
                generatedImporter.SaveAndReimport();
            }

            Texture2D generated = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
            GeneratedTextureCache[cacheKey] = generated;
            return generated;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[NatureManufacture URP Converter] Could not generate a helper texture from " +
                             sourcePath + ". The original texture will be used instead. " + ex.Message);
            return source;
        }
    }

    private static Texture2D GetTexture(Material material, string property)
    {
        return material != null && material.HasProperty(property) ? material.GetTexture(property) as Texture2D : null;
    }

    private static Color GetColor(Material material, string property, Color fallback)
    {
        return material != null && material.HasProperty(property) ? material.GetColor(property) : fallback;
    }

    private static float GetFloat(Material material, string property, float fallback)
    {
        return material != null && material.HasProperty(property) ? material.GetFloat(property) : fallback;
    }

    private static Vector2 GetTextureScaleSafe(Material material, string property)
    {
        try { return material.GetTextureScale(property); }
        catch { return Vector2.one; }
    }

    private static Vector2 GetTextureOffsetSafe(Material material, string property)
    {
        try { return material.GetTextureOffset(property); }
        catch { return Vector2.zero; }
    }
}
