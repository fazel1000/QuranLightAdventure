#if UNITY_EDITOR
using System.Linq;
using GameCreator.Runtime.Characters;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class JourneyWardrobeSetup
{
    private const string Source = "Assets/Hodaart/ModularBoysCharacters/Prefabs/Modular Boys Avatar Kit.prefab";
    private const string Variant = "Assets/Game/Characters/JourneyModularBoy.prefab";
    [MenuItem("Quran Kids/Create Hodaart Wardrobe Room")]
    private static void Setup()
    {
        if (EditorApplication.isPlaying) return;
        if (Object.FindFirstObjectByType<JourneyWardrobeRoom>(FindObjectsInactive.Include) != null)
        { Debug.LogWarning("A wardrobe room already exists. Edit that room instead of creating duplicates."); return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        var menu = Object.FindFirstObjectByType<JourneyMenuController>(FindObjectsInactive.Include);
        if (prefab == null || menu == null) { Debug.LogError("Hodaart modular prefab or JourneyMenuController is missing."); return; }
        var data = new SerializedObject(menu);
        var settings = data.FindProperty("settingPanel").objectReferenceValue as GameObject;
        var main = data.FindProperty("mainMenuPanel").objectReferenceValue as GameObject;
        if (settings == null || main == null) { Debug.LogError("Assign the menu panels first."); return; }
        int layer = LayerMask.NameToLayer("JourneyWardrobe");
        if (layer < 0)
        {
            var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tags.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++) if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
            { layers.GetArrayElementAtIndex(i).stringValue = "JourneyWardrobe"; tags.ApplyModifiedProperties(); layer = i; break; }
        }
        if (layer < 0) { Debug.LogError("Create a free layer named JourneyWardrobe."); return; }
        Transform canvas = main.transform.parent;
        var font = canvas.GetComponentsInChildren<TMP_Text>(true).Select(t => t.font).FirstOrDefault(f => f != null);
        var stage = new GameObject("Journey Wardrobe Stage"); Undo.RegisterCreatedObjectUndo(stage, "Create wardrobe stage"); stage.transform.position = new Vector3(1000f, 1000f, 1000f);
        var variant = AssetDatabase.LoadAssetAtPath<GameObject>(Variant);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(variant != null ? variant : prefab, stage.transform);
        var appearance = model.GetComponent<JourneyWardrobeAppearance>() ?? Undo.AddComponent<JourneyWardrobeAppearance>(model);
        if (variant == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Game/Characters")) AssetDatabase.CreateFolder("Assets/Game", "Characters");
            variant = PrefabUtility.SaveAsPrefabAssetAndConnect(model, Variant, UnityEditor.InteractionMode.AutomatedAction);
        }
        var cameraObject = new GameObject("Wardrobe Camera", typeof(Camera)); cameraObject.transform.SetParent(stage.transform, false);
        var camera = cameraObject.GetComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.08f, 0.11f, 0.16f); camera.fieldOfView = 35f;
        camera.nearClipPlane = 0.05f; camera.farClipPlane = 50f; camera.cullingMask = 1 << layer; camera.depth = 50;
        camera.rect = new Rect(0.38f, 0f, 0.62f, 1f); cameraObject.transform.localPosition = new Vector3(0, 1, 4);
        cameraObject.transform.LookAt(model.transform.position + Vector3.up);
        var lightObject = new GameObject("Wardrobe Light", typeof(Light)); lightObject.transform.SetParent(stage.transform, false);
        lightObject.transform.localPosition = new Vector3(0, 3, 3); var light = lightObject.GetComponent<Light>();
        light.type = LightType.Point; light.range = 12f; light.intensity = 3f; light.cullingMask = 1 << layer;
        foreach (var t in stage.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        var panel = new GameObject("Wardrobe Panel", typeof(RectTransform), typeof(Image)); Undo.RegisterCreatedObjectUndo(panel, "Create wardrobe panel");
        panel.transform.SetParent(canvas, false); var panelRect = (RectTransform)panel.transform;
        panelRect.anchorMin = Vector2.zero; panelRect.anchorMax = Vector2.one; panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = Color.clear;
        var drag = Undo.AddComponent<JourneyWardrobeDrag>(panel); drag.target = model.transform;
        var sidebar = new GameObject("Choices", typeof(RectTransform), typeof(Image)); sidebar.transform.SetParent(panel.transform, false);
        var sidebarRect = (RectTransform)sidebar.transform; sidebarRect.anchorMin = new Vector2(0f, 0f); sidebarRect.anchorMax = new Vector2(0.38f, 1f); sidebarRect.offsetMin = sidebarRect.offsetMax = Vector2.zero;
        sidebar.GetComponent<Image>().color = new Color(0.06f, 0.09f, 0.13f, 1f);
        for (int i = 0; i < 8; i++)
        {
            var row = new GameObject("Choice " + i, typeof(RectTransform), typeof(JourneyWardrobeChoice)); row.transform.SetParent(sidebar.transform, false);
            var rt = (RectTransform)row.transform; rt.anchorMin = new Vector2(0.03f, 0.17f + (7 - i) * 0.098f); rt.anchorMax = new Vector2(0.97f, 0.17f + (8 - i) * 0.098f); rt.offsetMin = rt.offsetMax = Vector2.zero;
            var choice = row.GetComponent<JourneyWardrobeChoice>(); choice.appearance = appearance; choice.category = i;
            choice.previous = JourneyProgressSetup.MakeButton("Previous", row.transform, Vector2.zero, new Vector2(42, 42), "<", font);
            choice.next = JourneyProgressSetup.MakeButton("Next", row.transform, Vector2.zero, new Vector2(42, 42), ">", font);
            var prev = (RectTransform)choice.previous.transform; prev.anchorMin = prev.anchorMax = new Vector2(0.07f, 0.5f);
            var next = (RectTransform)choice.next.transform; next.anchorMin = next.anchorMax = new Vector2(0.93f, 0.5f);
            choice.label = JourneyProgressSetup.MakeLabel("Category", row.transform, Vector2.zero, Vector2.zero, "", font);
            var labelRect = (RectTransform)choice.label.transform; labelRect.anchorMin = new Vector2(0.18f, 0f); labelRect.anchorMax = new Vector2(0.82f, 1f); labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            choice.label.enableAutoSizing = true; choice.label.fontSizeMin = 14; choice.label.fontSizeMax = 26;
        }
        var manager = new GameObject("Journey Wardrobe Manager", typeof(JourneyWardrobeRoom)); Undo.RegisterCreatedObjectUndo(manager, "Create wardrobe manager");
        var room = manager.GetComponent<JourneyWardrobeRoom>(); room.menu = menu; room.settingsPanel = settings;
        room.wardrobePanel = panel; room.stageRoot = stage; room.previewCamera = camera; room.gameplayCamera = Camera.main;
        room.preview = appearance; room.gameplayModelPrefab = variant;
        room.player = Object.FindObjectsByType<Character>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(c => c.IsPlayer);
        var open = JourneyProgressSetup.MakeButton("Customize Button", settings.transform, new Vector2(0, -280), new Vector2(300, 65), "شخصی‌سازی", font);
        var openLabel = open.GetComponentInChildren<TMP_Text>(); var localized = openLabel.gameObject.AddComponent<JourneyLocalizedText>(); localized.target = openLabel; localized.persian = "شخصی‌سازی"; localized.english = "Customize"; localized.arabic = "تخصيص الشخصية";
        UnityEventTools.AddPersistentListener(open.onClick, room.Open);
        var back = JourneyProgressSetup.MakeButton("Back", sidebar.transform, Vector2.zero, new Vector2(200, 55), "بازگشت", font);
        var backRect = (RectTransform)back.transform; backRect.anchorMin = backRect.anchorMax = new Vector2(0.5f, 0.07f);
        var backLabel = back.GetComponentInChildren<TMP_Text>(); var backText = backLabel.gameObject.AddComponent<JourneyLocalizedText>(); backText.target = backLabel; backText.persian = "بازگشت"; backText.english = "Back"; backText.arabic = "رجوع";
        UnityEventTools.AddPersistentListener(back.onClick, room.Close);
        camera.enabled = false; stage.SetActive(false); panel.SetActive(false);
        EditorSceneManager.MarkSceneDirty(menu.gameObject.scene); Selection.activeGameObject = manager;
        Debug.Log("Wardrobe stage, prefab variant and UI created. Save scene. Inspect the Customize Button position, preview framing, Player and Gameplay Model Prefab. Existing gameplay animator controller is retained when the model changes.", manager);
    }
}
#endif
