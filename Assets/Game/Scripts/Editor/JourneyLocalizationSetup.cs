#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class JourneyLocalizationSetup
{
    [MenuItem("Quran Kids/Add Translations To Known Labels")]
    private static void Setup()
    {
        if (EditorApplication.isPlaying) return;
        // Exclude every live verse, score and status reference, even if its current
        // contents happen to match a dictionary entry.
        var excluded = new HashSet<Object>();
        foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!(component is JourneyVoiceBoard) && !(component is JourneyVerseVoicePanel)) continue;
            var data = new SerializedObject(component);
            foreach (string field in new[] { "verseText", "percentText", "statusText", "translateText" })
            { var property = data.FindProperty(field); if (property != null) excluded.Add(property.objectReferenceValue); }
        }
        foreach (var menu in Object.FindObjectsByType<JourneyMenuController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var data = new SerializedObject(menu);
            string[] fields = { "startButton", "settingButton", "exitButton", "settingBackButton", "gameplayBackButton" };
            string[] labels = { "شروع بازی", "تنظیمات", "خروج", "بازگشت", "بازگشت" };
            for (int i = 0; i < fields.Length; i++)
            {
                var button = data.FindProperty(fields[i]).objectReferenceValue as UnityEngine.UI.Button;
                if (button == null) continue;
                var label = button.GetComponentInChildren<TMP_Text>(true);
                if (label == null || excluded.Contains(label)) continue;
                var component = label.GetComponent<JourneyLocalizedText>();
                if (component != null) continue;
                component = Undo.AddComponent<JourneyLocalizedText>(label.gameObject);
                component.target = label;
                JourneyLocalization.TryGet(labels[i], out component.persian, out component.english, out component.arabic);
                EditorUtility.SetDirty(component);
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                EditorSceneManager.MarkSceneDirty(label.gameObject.scene);
            }
        }
        int count = 0;
        foreach (var label in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!label.gameObject.scene.IsValid() || excluded.Contains(label) || label.GetComponent<JourneyLocalizedText>() != null) continue;
            if (!JourneyLocalization.TryGet(JourneyLocalizedText.Original(label), out var fa, out var en, out var ar)) continue;
            var localization = Undo.AddComponent<JourneyLocalizedText>(label.gameObject);
            localization.target = label; localization.persian = fa; localization.english = en; localization.arabic = ar;
            EditorUtility.SetDirty(localization);
            PrefabUtility.RecordPrefabInstancePropertyModifications(localization);
            EditorSceneManager.MarkSceneDirty(label.gameObject.scene);
            count++;
        }
        Debug.Log("Added translation components to " + count + " known labels. For other static labels add JourneyLocalizedText and fill Persian, English, Arabic. Live verses/scores/status labels are excluded. Save scene.");
    }
}
#endif
