#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using RTLTMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class JourneyProgressSetup
{
    private static TMP_FontAsset Font(Transform root) => root.GetComponentsInChildren<TMP_Text>(true).Select(t => t.font).FirstOrDefault(f => f != null);
    private static GameObject Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)?.gameObject;
    private static void Set(SerializedObject data, string name, Object value) { var property = data.FindProperty(name); if (property != null) property.objectReferenceValue = value; }
    private static GameObject Get(SerializedObject data, string name) => data.FindProperty(name)?.objectReferenceValue as GameObject;
    private static GameObject Panel(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); Undo.RegisterCreatedObjectUndo(go, "Create Journey UI");
        go.transform.SetParent(parent, false); var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = color; return go;
    }
    internal static Button MakeButton(string name, Transform parent, Vector2 position, Vector2 size, string label, TMP_FontAsset font)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); Undo.RegisterCreatedObjectUndo(go, "Create Journey button");
        go.transform.SetParent(parent, false); var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.anchoredPosition = position; rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0.18f, 0.28f, 0.38f);
        var text = MakeLabel("Label", go.transform, Vector2.zero, size - new Vector2(12, 8), label, font);
        text.fontSize = 28; return go.GetComponent<Button>();
    }
    internal static TMP_Text MakeLabel(string name, Transform parent, Vector2 position, Vector2 size, string text, TMP_FontAsset font)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(RTLTextMeshPro)); go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.anchoredPosition = position; rt.sizeDelta = size;
        var label = go.GetComponent<RTLTextMeshPro>(); if (font != null) label.font = font;
        label.text = text; label.fontSize = 26; label.alignment = TextAlignmentOptions.Center; label.raycastTarget = false;
        return label;
    }
    private static Button LanguageButton(Transform parent, string language)
    {
        return parent.GetComponentsInChildren<Button>(true).FirstOrDefault(b =>
            b.name.IndexOf(language, StringComparison.OrdinalIgnoreCase) >= 0 ||
            b.GetComponentsInChildren<TMP_Text>(true).Any(t =>
                JourneyLocalizedText.Original(t).Contains(language == "Persian" ? "فارسی" : language == "Arabic" ? "العربية" : "English")));
    }
    [MenuItem("Quran Kids/Setup Journey Progress UI")]
    private static void Setup()
    {
        if (EditorApplication.isPlaying) return;
        var menu = Object.FindFirstObjectByType<JourneyMenuController>(FindObjectsInactive.Include);
        if (menu == null) { Debug.LogError("Open Journey of Light and assign JourneyMenuController first."); return; }
        var data = new SerializedObject(menu);
        var main = Get(data, "mainMenuPanel"); var settings = Get(data, "settingPanel");
        if (main == null || settings == null) { Debug.LogError("Assign Main Menu Panel and Setting Panel first."); return; }
        Transform canvas = main.transform.parent; var font = Font(canvas);
        var first = Get(data, "firstPanel") ?? Find(canvas, "First Panel") ?? Panel("First Panel", canvas, new Color(0.07f, 0.1f, 0.16f, 1f));
        Set(data, "firstPanel", first);
        string[] languages = { "Persian", "English", "Arabic" };
        string[] captions = { "فارسی", "English", "العربية" };
        var normal = new Button[3]; var initial = new Button[3];
        for (int i = 0; i < 3; i++)
        {
            string field = languages[i].ToLowerInvariant() + "Button";
            normal[i] = data.FindProperty(field).objectReferenceValue as Button;
            if (normal[i] == null) normal[i] = LanguageButton(settings.transform, languages[i]);
            if (normal[i] == null) Debug.LogWarning("Assign " + field + " on JourneyMenuController; the settings button was not found.", menu);
            Set(data, field, normal[i]);
            string firstField = "first" + languages[i] + "Button";
            initial[i] = data.FindProperty(firstField).objectReferenceValue as Button;
            if (initial[i] == null) initial[i] = LanguageButton(first.transform, languages[i]);
            if (initial[i] == null)
                initial[i] = MakeButton(languages[i] + " Button", first.transform, new Vector2(0, 90 - i * 90), new Vector2(280, 68), captions[i], font);
            Set(data, firstField, initial[i]);
        }
        AddHighlight(settings, normal); AddHighlight(first, initial);
        data.ApplyModifiedProperties(); PrefabUtility.RecordPrefabInstancePropertyModifications(menu);
        first.SetActive(false);
        var puzzle = Object.FindFirstObjectByType<LightBrushPuzzle>(FindObjectsInactive.Include);
        foreach (var board in Object.FindObjectsByType<JourneyVoiceBoard>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var boardData = new SerializedObject(board);
            if (boardData.FindProperty("scoreReceiver").objectReferenceValue == null) Set(boardData, "scoreReceiver", puzzle);
            var translation = Find(board.transform, "Translate Text");
            if (translation != null) Set(boardData, "translateText", translation.GetComponent<TMP_Text>());
            BindTint(boardData, "playRenderers", "playVisual"); BindTint(boardData, "recordRenderers", "recordVisual");
            boardData.ApplyModifiedProperties(); PrefabUtility.RecordPrefabInstancePropertyModifications(board);
        }
        var jump = Find(canvas, "JumpButton");
        if (jump != null && jump.GetComponent<JourneyChargedJump>() == null) Undo.AddComponent<JourneyChargedJump>(jump);
        EditorSceneManager.MarkSceneDirty(menu.gameObject.scene);
        Selection.activeGameObject = menu.gameObject;
        Debug.Log("First language panel and board references connected. Check first/normal language buttons, tint renderers, Translate Text and JumpButton. Save scene. Set translations on the Surah assets.", menu);
    }
    private static void AddHighlight(GameObject root, Button[] buttons)
    {
        var highlight = root.GetComponent<JourneyLanguageHighlight>() ?? Undo.AddComponent<JourneyLanguageHighlight>(root);
        Undo.RecordObject(highlight, "Language highlights"); highlight.persian = buttons[0]; highlight.english = buttons[1]; highlight.arabic = buttons[2]; EditorUtility.SetDirty(highlight);
    }
    private static void BindTint(SerializedObject data, string arrayField, string visualField)
    {
        var array = data.FindProperty(arrayField); if (array.arraySize != 0) return;
        var visual = data.FindProperty(visualField).objectReferenceValue as Transform; if (visual == null) return;
        var renderers = visual.GetComponentsInChildren<Renderer>(true).Where(r => r.GetComponent<TMP_Text>() == null).ToArray();
        array.arraySize = renderers.Length;
        for (int i = 0; i < renderers.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
    }
    [MenuItem("Quran Kids/Fill Missing Ikhlas Translations")]
    private static void FillTranslations()
    {
        // These short translations are editable, and never replace an existing authored translation.
        string[] fa = { "به نام خداوند بخشندهٔ مهربان", "بگو: او خداوند یگانه است.", "خداوند بی‌نیاز است.", "نه زاده است و نه زاده شده است.", "و هیچ‌کس همتای او نیست." };
        string[] en = { "In the name of Allah, the Most Merciful, the Ever Merciful.", "Say: He is Allah, the One.", "Allah is free of need; all depend on Him.", "He neither begets nor was born.", "And none is equal to Him." };
        int count = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:JourneySurahDefinition"))
        {
            var surah = AssetDatabase.LoadAssetAtPath<JourneySurahDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (surah == null || surah.surahNumber != 112 || surah.verses == null) continue;
            Undo.RecordObject(surah, "Fill missing Ikhlas translations");
            foreach (var entry in surah.verses)
            {
                if (entry == null || entry.verseNumber < 1 || entry.verseNumber > 5) continue;
                if (string.IsNullOrWhiteSpace(entry.persianTranslation)) entry.persianTranslation = fa[entry.verseNumber - 1];
                if (string.IsNullOrWhiteSpace(entry.englishTranslation)) entry.englishTranslation = en[entry.verseNumber - 1];
            }
            EditorUtility.SetDirty(surah); count++;
        }
        AssetDatabase.SaveAssets(); Debug.Log("Filled missing translations on " + count + " Ikhlas assets. Numbering: 1 = Bismillah, 2-5 = the four verses. Review wording in Inspector.");
    }
}
#endif
