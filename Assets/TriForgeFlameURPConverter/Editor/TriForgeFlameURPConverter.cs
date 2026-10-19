using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class TriForgeFlameURPConverter : EditorWindow
{
    private const string TargetShaderName = "TriForge/Flame URP";

    private static readonly HashSet<string> TargetMaterialNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "M_Flame_Particle",
        "M_Flame_Particle_B"
    };

    private readonly List<Material> materials = new List<Material>();
    private Vector2 scrollPosition;

    [MenuItem("Tools/TriForge/Flame URP Converter")]
    private static void OpenWindow()
    {
        TriForgeFlameURPConverter window = GetWindow<TriForgeFlameURPConverter>();
        window.titleContent = new GUIContent("Flame URP Converter");
        window.minSize = new Vector2(480f, 300f);
        window.RefreshMaterials();
        window.Show();
    }

    private void OnEnable()
    {
        RefreshMaterials();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("TriForge Flame Materials → URP", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "این ابزار فقط متریال‌هایی با نام دقیق M_Flame_Particle و M_Flame_Particle_B را پیدا می‌کند. " +
            "قبل از تبدیل، از پروژه بکاپ بگیر. ابزار شیدر URP اختصاصی Flame را به متریال‌ها اختصاص می‌دهد " +
            "و تکسچر، Tint HDR و پارامترهای افکت را تا حد امکان نگه می‌دارد.",
            MessageType.Info);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Find Flame Materials", GUILayout.Height(28f)))
                RefreshMaterials();

            GUI.enabled = materials.Count > 0;
            if (GUILayout.Button("Convert Found Materials", GUILayout.Height(28f)))
                ConvertFoundMaterials();
            GUI.enabled = true;
        }

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Found materials", EditorStyles.boldLabel);

        if (materials.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "متریالی با این نام‌ها پیدا نشد. مطمئن شو فایل‌های متریال داخل پروژه Import شده‌اند، سپس دوباره جست‌وجو کن.",
                MessageType.Warning);
            return;
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        foreach (Material material in materials)
        {
            if (material == null)
                continue;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.ObjectField("Material", material, typeof(Material), false);
                EditorGUILayout.LabelField("Current shader", material.shader != null ? material.shader.name : "(None)");
                EditorGUILayout.LabelField(
                    "Status",
                    material.shader != null && material.shader.name == TargetShaderName
                        ? "Already using Flame URP"
                        : "Needs conversion");
            }
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(4f);
        EditorGUILayout.HelpBox(
            "اگر بعد از تبدیل محوشدن لبه‌ها با عمق صحنه درست کار نکرد، گزینه Depth Texture را در URP Asset " +
            "یا تنظیمات Camera فعال کن.",
            MessageType.None);
    }

    private void RefreshMaterials()
    {
        materials.Clear();

        string[] guids = AssetDatabase.FindAssets("t:Material");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material != null && TargetMaterialNames.Contains(material.name) && !materials.Contains(material))
                materials.Add(material);
        }

        materials.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
        Repaint();
    }

    private void ConvertFoundMaterials()
    {
        Shader targetShader = Shader.Find(TargetShaderName);
        if (targetShader == null)
        {
            EditorUtility.DisplayDialog(
                "Flame URP shader not found",
                "شیدر '" + TargetShaderName + "' پیدا نشد یا هنوز Compile نشده است. " +
                "مطمئن شو فایل Flame_URP.shader داخل پوشه Assets قرار دارد و خطای کامپایل ندارد.",
                "OK");
            return;
        }

        int converted = 0;
        int alreadyConverted = 0;

        foreach (Material material in materials)
        {
            if (material == null)
                continue;

            if (material.shader == targetShader)
            {
                alreadyConverted++;
                continue;
            }

            Undo.RecordObject(material, "Convert TriForge Flame Material to URP");

            // Cache the original properties before changing the shader.
            bool hasMainTex = material.HasProperty("_MainTex");
            Texture mainTex = hasMainTex ? material.GetTexture("_MainTex") : null;
            Vector2 mainTexScale = hasMainTex ? material.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 mainTexOffset = hasMainTex ? material.GetTextureOffset("_MainTex") : Vector2.zero;

            bool hasTint = material.HasProperty("_TintColor");
            Color tint = hasTint ? material.GetColor("_TintColor") : Color.white;

            float distortionStrength = GetFloatOrDefault(material, "_DistortionStrength", 0.191f);
            float dissolveStrength = GetFloatOrDefault(material, "_DissolveStrength", 1f);
            float dissolveScale = GetFloatOrDefault(material, "_DissolveScale", 2f);
            float distortionScale = GetFloatOrDefault(material, "_DistortionScale", 2f);
            float effectSpeed = GetFloatOrDefault(material, "_EffectSpeed", 1f);
            bool enableInstancing = material.enableInstancing;

            material.shader = targetShader;

            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", mainTex);
                material.SetTextureScale("_MainTex", mainTexScale);
                material.SetTextureOffset("_MainTex", mainTexOffset);
            }

            if (material.HasProperty("_TintColor"))
                material.SetColor("_TintColor", tint);

            SetFloatIfAvailable(material, "_DistortionStrength", distortionStrength);
            SetFloatIfAvailable(material, "_DissolveStrength", dissolveStrength);
            SetFloatIfAvailable(material, "_DissolveScale", dissolveScale);
            SetFloatIfAvailable(material, "_DistortionScale", distortionScale);
            SetFloatIfAvailable(material, "_EffectSpeed", effectSpeed);

            material.enableInstancing = enableInstancing;
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;

            EditorUtility.SetDirty(material);
            converted++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Flame URP conversion",
            "تبدیل تمام شد.\n\nConverted: " + converted + "\nAlready converted: " + alreadyConverted +
            "\n\nنتیجه را در Scene و Game view بررسی کن؛ ظاهر افکت ممکن است نسبت به Built-in کمی تفاوت داشته باشد.",
            "OK");

        RefreshMaterials();
    }

    private static float GetFloatOrDefault(Material material, string propertyName, float defaultValue)
    {
        return material.HasProperty(propertyName) ? material.GetFloat(propertyName) : defaultValue;
    }

    private static void SetFloatIfAvailable(Material material, string propertyName, float value)
    {
        if (material.HasProperty(propertyName))
            material.SetFloat(propertyName, value);
    }
}
