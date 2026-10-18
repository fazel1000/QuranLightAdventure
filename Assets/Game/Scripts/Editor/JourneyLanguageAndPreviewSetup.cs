#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using GameCreator.Runtime.Characters;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class JourneyLanguageAndPreviewSetup
{
    [MenuItem("Quran Kids/Setup Language Fonts, Jump and Wardrobe Preview")]
    private static void Setup()
    {
        if (EditorApplication.isPlaying) return;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Configure language fonts, jump and wardrobe");
        var verseLabels = new HashSet<TMP_Text>();
        foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (component == null || component.gameObject.scene != scene || (!(component is JourneyVoiceBoard) && !(component is JourneyVerseVoicePanel))) continue;
            var data = new SerializedObject(component);
            var label = data.FindProperty("verseText")?.objectReferenceValue as TMP_Text;
            if (label != null) verseLabels.Add(label);
        }
        int styled = 0;
        foreach (var label in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (label.gameObject.scene != scene) continue;
            var style = label.GetComponent<JourneyLanguageTextStyle>() ?? Undo.AddComponent<JourneyLanguageTextStyle>(label.gameObject);
            Undo.RecordObject(style, "Assign language typography");
            style.target = label;
            if (verseLabels.Contains(label)) style.alwaysArabic = true;
            EditorUtility.SetDirty(style); PrefabUtility.RecordPrefabInstancePropertyModifications(style); styled++;
        }
        var menu = Object.FindObjectsByType<JourneyMenuController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(item => item.gameObject.scene == scene);
        Canvas canvas = null;
        if (menu != null)
        {
            var menuData = new SerializedObject(menu);
            var mainPanel = menuData.FindProperty("mainMenuPanel")?.objectReferenceValue as GameObject;
            if (mainPanel != null) canvas = mainPanel.GetComponentInParent<Canvas>();
        }
        if (canvas == null) canvas = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(item => item.gameObject.scene == scene && item.isRootCanvas);
        if (canvas != null && menu != null) SetupJump(canvas, menu);
        else Debug.LogWarning("No scene Canvas/Menu found; jump button was not created.");
        var room = Object.FindObjectsByType<JourneyWardrobeRoom>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(item => item.gameObject.scene == scene);
        if (room != null && room.wardrobePanel != null && room.previewCamera != null && room.preview != null) SetupPreview(room);
        else Debug.LogWarning("No configured wardrobe room found; preview surface was not created.");
        EditorSceneManager.MarkSceneDirty(scene); Undo.CollapseUndoOperations(group);
        Debug.Log("Font/size components ready on " + styled + " texts. Configure Persian/English/Arabic on JourneyLanguageTextStyle. JumpButton and wardrobe preview connected where available. Save scene.");
    }
    private static void SetupJump(Canvas canvas, JourneyMenuController menu)
    {
        var jump = canvas.GetComponentsInChildren<JourneyChargedJump>(true).FirstOrDefault();
        GameObject button = jump != null ? jump.gameObject : canvas.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name.Replace(" ", "").Equals("JumpButton", System.StringComparison.OrdinalIgnoreCase))?.gameObject;
        if (button == null)
        {
            var font = canvas.GetComponentsInChildren<TMP_Text>(true).Select(item => item.font).FirstOrDefault(item => item != null);
            var created = JourneyProgressSetup.MakeButton("JumpButton", canvas.transform, new Vector2(-140, 300), new Vector2(115, 100), "پرش", font);
            button = created.gameObject;
            var rect = (RectTransform)button.transform; rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-140, 300);
        }
        if (jump == null) jump = button.GetComponent<JourneyChargedJump>() ?? Undo.AddComponent<JourneyChargedJump>(button);
        Undo.RecordObject(jump, "Connect jump controls"); jump.menu = menu;
        if (jump.player == null) jump.player = Object.FindObjectsByType<Character>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(item => item.IsPlayer && item.gameObject.scene == menu.gameObject.scene);
        if (jump.vehicle == null) jump.vehicle = Object.FindObjectsByType<JourneyVehicleDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(item => item.gameObject.scene == menu.gameObject.scene);
        if (jump.drone == null) jump.drone = Object.FindObjectsByType<JourneyDronePilot>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(item => item.gameObject.scene == menu.gameObject.scene);
        var label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            var localized = label.GetComponent<JourneyLocalizedText>() ?? Undo.AddComponent<JourneyLocalizedText>(label.gameObject);
            Undo.RecordObject(localized, "Translate Jump label"); localized.target = label;
            localized.persian = "پرش"; localized.english = "Jump"; localized.arabic = "قفز";
            var style = label.GetComponent<JourneyLanguageTextStyle>() ?? Undo.AddComponent<JourneyLanguageTextStyle>(label.gameObject); style.target = label;
            EditorUtility.SetDirty(localized); EditorUtility.SetDirty(style);
            PrefabUtility.RecordPrefabInstancePropertyModifications(localized); PrefabUtility.RecordPrefabInstancePropertyModifications(style);
        }
        EditorUtility.SetDirty(jump); PrefabUtility.RecordPrefabInstancePropertyModifications(jump);
    }
    private static void SetupPreview(JourneyWardrobeRoom room)
    {
        var surface = room.GetComponent<JourneyWardrobePreviewSurface>() ?? Undo.AddComponent<JourneyWardrobePreviewSurface>(room.gameObject);
        Undo.RecordObject(surface, "Connect wardrobe preview image");
        RawImage image = surface.previewImage;
        if (image == null)
        {
            var existing = room.wardrobePanel.transform.Find("Wardrobe Preview Surface");
            if (existing != null) image = existing.GetComponent<RawImage>();
        }
        if (image == null)
        {
            var go = new GameObject("Wardrobe Preview Surface", typeof(RectTransform), typeof(RawImage));
            Undo.RegisterCreatedObjectUndo(go, "Create preview image"); go.transform.SetParent(room.wardrobePanel.transform, false);
            image = go.GetComponent<RawImage>();
            var rect = image.rectTransform;
            Rect viewport = room.previewCamera.rect;
            rect.anchorMin = new Vector2(viewport.xMin, viewport.yMin); rect.anchorMax = new Vector2(viewport.xMax, viewport.yMax);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        Undo.RecordObject(image, "Enable preview raycasts"); image.color = Color.white; image.raycastTarget = true;
        // Parent Image is the background; draw the preview over it and keep controls on top.
        image.transform.SetAsLastSibling();
        foreach (Transform child in room.wardrobePanel.transform.Cast<Transform>().ToArray())
            if (child != image.transform && (child.GetComponentInChildren<Selectable>(true) != null || child.GetComponentInChildren<TMP_Text>(true) != null)) child.SetAsLastSibling();
        surface.previewImage = image;
        var drag = room.wardrobePanel.GetComponent<JourneyWardrobeDrag>() ?? Undo.AddComponent<JourneyWardrobeDrag>(room.wardrobePanel);
        Undo.RecordObject(drag, "Connect drag viewport"); drag.previewArea = image.rectTransform; drag.target = room.preview.transform; drag.previewCamera = room.previewCamera;
        // URP post-processing may discard alpha; the isolated preview does not require it.
        var additional = room.previewCamera.GetComponents<MonoBehaviour>().FirstOrDefault(item => item != null && item.GetType().Name == "UniversalAdditionalCameraData");
        if (additional != null)
        {
            var data = new SerializedObject(additional);
            var postProcessing = data.FindProperty("m_RenderPostProcessing");
            if (postProcessing != null) { postProcessing.boolValue = false; data.ApplyModifiedProperties(); }
        }
        EditorUtility.SetDirty(surface); EditorUtility.SetDirty(drag);
        PrefabUtility.RecordPrefabInstancePropertyModifications(surface); PrefabUtility.RecordPrefabInstancePropertyModifications(drag);
    }
}
#endif
