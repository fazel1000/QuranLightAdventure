using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class StylizedToonyURPConverter
{
    private const string TargetShaderName = "Custom/URP Stylized Toony";
    private const string LegacyShaderName = "Custom/StylizedToonyShader";
    private const string BackupFolder = "Assets/_ShaderFixBackups/StylizedToony";

    [MenuItem("Tools/URP/Stylized Toony/Convert Selected Materials")]
    private static void ConvertSelectedMaterials()
    {
        List<Material> materials = Selection.objects
            .OfType<Material>()
            .Where(m => m != null)
            .Distinct()
            .ToList();

        if (materials.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Stylized Toony URP Converter",
                "در پنجره Project یک یا چند فایل Material را انتخاب کن و دوباره این گزینه را اجرا کن.",
                "OK");
            return;
        }

        ConvertMaterials(materials);
    }

    [MenuItem("Tools/URP/Stylized Toony/Convert All Legacy Materials")]
    private static void ConvertAllLegacyMaterials()
    {
        string[] guids = AssetDatabase.FindAssets("t:Material");
        List<Material> materials = new List<Material>();

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // Never include our own rollback copies in project-wide conversion.
            if (path.Replace('\\', '/').StartsWith(BackupFolder + "/", StringComparison.OrdinalIgnoreCase))
                continue;

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null && material.shader != null &&
                material.shader.name == LegacyShaderName)
            {
                materials.Add(material);
            }
        }

        if (materials.Count == 0)
        {
            EditorUtility.DisplayDialog(
                "Stylized Toony URP Converter",
                "هیچ متریالی با شیدر " + LegacyShaderName + " پیدا نشد. می‌توانی متریال‌ها را در Project انتخاب کنی و از گزینه Convert Selected Materials استفاده کنی.",
                "OK");
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "Convert all legacy materials?",
                materials.Count + " متریال با شیدر قدیمی پیدا شد.\n\nاز هر متریال یک نسخه‌ی پشتیبان ساخته می‌شود و سپس شیدر آن به نسخه‌ی سازگار با URP تغییر می‌کند.",
                "Convert All",
                "Cancel"))
            return;

        ConvertMaterials(materials);
    }

    private static void ConvertMaterials(List<Material> materials)
    {
        Shader targetShader = Shader.Find(TargetShaderName);
        if (targetShader == null)
        {
            EditorUtility.DisplayDialog(
                "URP shader not found",
                "شیدر مقصد پیدا نشد: " + TargetShaderName + "\n\nابتدا فایل StylizedToonyURP.shader را داخل Assets کپی کن و صبر کن Unity آن را Import/Compile کند.",
                "OK");
            return;
        }

        EnsureFolder(BackupFolder);

        int converted = 0;
        int skipped = 0;
        int backupFailed = 0;

        try
        {
            for (int i = 0; i < materials.Count; i++)
            {
                Material material = materials[i];
                if (material == null || material.shader == targetShader)
                {
                    skipped++;
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(material);
                if (string.IsNullOrEmpty(path))
                {
                    Debug.LogWarning("Skipped non-asset material: " + material.name);
                    skipped++;
                    continue;
                }

                EditorUtility.DisplayProgressBar(
                    "Stylized Toony → URP",
                    material.name,
                    (float)(i + 1) / materials.Count);

                string backupPath = AssetDatabase.GenerateUniqueAssetPath(
                    BackupFolder + "/" + Path.GetFileNameWithoutExtension(path) + "_BeforeURP.mat");

                if (!AssetDatabase.CopyAsset(path, backupPath))
                {
                    backupFailed++;
                    if (!EditorUtility.DisplayDialog(
                            "Backup could not be created",
                            "برای متریال " + material.name + " نسخه‌ی پشتیبان ساخته نشد.\n\nمی‌خواهی بدون بکاپ ادامه بدهی؟",
                            "Continue",
                            "Stop"))
                    {
                        break;
                    }
                }

                Undo.RecordObject(material, "Convert Stylized Toony Material to URP");

                // The replacement shader intentionally uses the same property names
                // (_MainTex, _Color, _DiffuseColor, _Mask, _AO, _Emission, etc.)
                // so Unity can retain compatible serialized material values.
                material.shader = targetShader;
                EditorUtility.SetDirty(material);
                converted++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        EditorUtility.DisplayDialog(
            "Stylized Toony URP Converter",
            "تبدیل تمام شد.\n\nConverted: " + converted +
            "\nSkipped: " + skipped +
            "\nBackup failures: " + backupFailed +
            "\n\nنسخه‌های پشتیبان در این پوشه قرار دارند:\n" + BackupFolder +
            "\n\nحالا Scene را بررسی کن و اگر ظاهر متریال‌ها نیاز به تنظیم داشت، مقدارها را اصلاح کن.",
            "OK");
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
