#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class JourneyPatrolSettingsUpdate
{
    [MenuItem("Quran Kids/Setup Patrols and Settings Update")]
    private static void Setup()
    {
        if (EditorApplication.isPlaying) return;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var menu = Object.FindObjectsByType<JourneyMenuController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(item => item.gameObject.scene == scene);
        if (menu == null) { Debug.LogError("Open the gameplay scene with JourneyMenuController first."); return; }
        var data = new SerializedObject(menu);
        var first = data.FindProperty("settingPanel").objectReferenceValue as GameObject;
        var second = scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<Transform>(true)).FirstOrDefault(item => item.name.Trim() == "Setting Panel (2)")?.gameObject;
        if (first == null || second == null) { Debug.LogError("Setting Panel and Setting Panel (2) must both exist in the active scene."); return; }
        if (first.transform.parent != second.transform.parent) { Debug.LogError("Both settings pages must have the same parent Canvas."); return; }
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Connect settings pages and music");
        var pages = menu.GetComponent<JourneySettingsPages>() ?? Undo.AddComponent<JourneySettingsPages>(menu.gameObject);
        Undo.RecordObject(pages, "Connect settings pages"); pages.menu = menu;
        pages.firstPage = first.GetComponent<RectTransform>(); pages.secondPage = second.GetComponent<RectTransform>();
        pages.rightArrow = Button(first.transform, "Right Arrow"); pages.leftArrow = Button(second.transform, "Left Arrow"); pages.secondBackButton = Button(second.transform, "Back");
        if (pages.rightArrow == null || pages.leftArrow == null) Debug.LogWarning("Assign Right Arrow and Left Arrow on JourneySettingsPages.", pages);
        var music = Object.FindObjectsByType<JourneyShuffleMusic>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(item => item.gameObject.scene == scene);
        var musicPanel = Find(first.transform, "Gameplay Musics Panel");
        if (musicPanel != null)
        {
            var ui = first.GetComponent<JourneyMusicSettingsUI>() ?? Undo.AddComponent<JourneyMusicSettingsUI>(first);
            Undo.RecordObject(ui, "Connect music pause and name"); ui.music = music;
            if (ui.volumeSlider == null) ui.volumeSlider = musicPanel.GetComponentInChildren<Slider>(true);
            ui.previousButton = Button(musicPanel, "Previous Button"); ui.nextButton = Button(musicPanel, "Next Button");
            ui.pauseButton = Button(musicPanel, "Pause Button");
            if (ui.pauseButton == null) ui.pauseButton = CreatePauseButton(musicPanel, ui.previousButton, ui.nextButton);
            ui.pauseImage = ui.pauseButton != null ? ui.pauseButton.targetGraphic as Image ?? ui.pauseButton.GetComponentInChildren<Image>(true) : null;
            if (ui.playingSprite == null && ui.pauseImage != null) ui.playingSprite = ui.pauseImage.sprite;
            if (ui.pausedSprite == null) ui.pausedSprite = MusicIcon("pause");
            var title = Find(musicPanel, "Music Gameplay Name"); ui.trackNameText = title != null ? title.GetComponent<TMP_Text>() : null;
            if (ui.trackNameText != null)
            {
                var localized = ui.trackNameText.GetComponent<JourneyLocalizedText>();
                if (localized != null) { Undo.RecordObject(localized, "Keep song title dynamic"); localized.enabled = false; }
                var style = ui.trackNameText.GetComponent<JourneyLanguageTextStyle>() ?? Undo.AddComponent<JourneyLanguageTextStyle>(ui.trackNameText.gameObject);
                Undo.RecordObject(style, "Style song name"); style.target = ui.trackNameText;
                EditorUtility.SetDirty(style); PrefabUtility.RecordPrefabInstancePropertyModifications(style);
            }
            EditorUtility.SetDirty(ui); PrefabUtility.RecordPrefabInstancePropertyModifications(ui);
            Selection.activeGameObject = first;
        }
        var room = Object.FindObjectsByType<JourneyWardrobeRoom>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(item => item.gameObject.scene == scene);
        var customize = Button(second.transform, "Customize Button");
        if (room != null)
        {
            Undo.RecordObject(room, "Preserve settings return page"); room.menu = menu; room.settingsPanel = first;
            if (customize != null)
            {
                bool connected = false;
                for (int i = 0; i < customize.onClick.GetPersistentEventCount(); i++)
                    if (customize.onClick.GetPersistentTarget(i) == room && customize.onClick.GetPersistentMethodName(i) == "Open") connected = true;
                if (!connected) { Undo.RecordObject(customize, "Open wardrobe from page two"); UnityEventTools.AddPersistentListener(customize.onClick, room.Open); }
            }
            if (room.wardrobePanel != null)
            {
                var drag = room.wardrobePanel.GetComponent<JourneyWardrobeDrag>();
                if (drag != null) { Undo.RecordObject(drag, "Correct wardrobe drag direction"); drag.invertHorizontal = false; drag.invertVertical = false; EditorUtility.SetDirty(drag); }
            }
            EditorUtility.SetDirty(room);
        }
        Undo.RecordObject(second, "Hide second settings page at startup"); second.SetActive(false);
        EditorUtility.SetDirty(pages); PrefabUtility.RecordPrefabInstancePropertyModifications(pages);
        EditorSceneManager.MarkSceneDirty(scene); Undo.CollapseUndoOperations(group);
        JourneyRiderGuardSetup.UpgradeAll(); AssetDatabase.SaveAssets();
        Debug.Log("Settings arrows, page-two Back/Customize, pause sprite fields and Music Gameplay Name connected. Assign Paused Sprite on the first page's JourneyMusicSettingsUI, then save the scene.", menu);
    }
    private static Sprite MusicIcon(string name)
    {
        const string folder = "Assets/GUI/GUI Pro-FantasyRPG/ResourcesData/Sprites/Component/Icon_PictoIcons/128/";
        return AssetDatabase.LoadAllAssetsAtPath(folder + "function_icon_player_" + name + ".png").OfType<Sprite>().FirstOrDefault();
    }
    private static UnityEngine.UI.Button CreatePauseButton(Transform panel, UnityEngine.UI.Button previous, UnityEngine.UI.Button next)
    {
        var gameObject = new GameObject("Pause Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UnityEngine.UI.Button));
        Undo.RegisterCreatedObjectUndo(gameObject, "Create music pause button"); gameObject.transform.SetParent(panel, false);
        var rect = (RectTransform)gameObject.transform;
        var image = gameObject.GetComponent<Image>(); var button = gameObject.GetComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image; image.sprite = MusicIcon("play"); image.preserveAspect = true;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.sizeDelta = new Vector2(100f, 100f);
        if (previous != null && next != null)
        {
            var before = (RectTransform)previous.transform; var after = (RectTransform)next.transform;
            if (before.anchorMin == after.anchorMin && before.anchorMax == after.anchorMax)
            {
                Undo.RecordObjects(new Object[] { before, after }, "Space three music buttons");
                rect.anchorMin = before.anchorMin; rect.anchorMax = before.anchorMax; rect.pivot = before.pivot;
                rect.sizeDelta = before.sizeDelta; rect.localScale = before.localScale;
                Vector2 center = (before.anchoredPosition + after.anchoredPosition) * 0.5f;
                float spacing = Mathf.Max(Mathf.Abs(before.anchoredPosition.x - after.anchoredPosition.x) * 0.5f, rect.sizeDelta.x * 1.3f);
                rect.anchoredPosition = center; before.anchoredPosition = center + Vector2.left * spacing; after.anchoredPosition = center + Vector2.right * spacing;
            }
            if (next.targetGraphic is Image template) image.color = template.color;
            button.colors = next.colors;
        }
        Debug.Log("Pause Button was missing from the saved scene; created it using the project's Play icon. You can replace both sprites on JourneyMusicSettingsUI.", gameObject);
        return button;
    }
    private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => string.Equals(item.name.Trim(), name, StringComparison.OrdinalIgnoreCase));
    private static UnityEngine.UI.Button Button(Transform root, string name) => root.GetComponentsInChildren<UnityEngine.UI.Button>(true).FirstOrDefault(item => string.Equals(item.name.Trim(), name, StringComparison.OrdinalIgnoreCase));
}
#endif
