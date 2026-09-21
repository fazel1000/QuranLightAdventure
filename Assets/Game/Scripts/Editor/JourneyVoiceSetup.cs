#if UNITY_EDITOR
using Eitan.Sherpa.Onnx.Unity.Mono.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class JourneyVoiceSetup
{
    [MenuItem("Quran Kids/Setup Journey Voice")]
    private static void Setup()
    {
        if (Application.isPlaying) { Debug.LogWarning("Exit Play Mode before setup."); return; }
        var existing = Object.FindFirstObjectByType<JourneyVerseVoicePanel>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing);
            return;
        }
        var json = Resources.Load<TextAsset>("QuranData/ordered_quran_phonemes (1)");
        var tokens = Resources.Load<TextAsset>("QuranData/tokens");
        if (json == null || tokens == null)
        {
            Debug.LogError("Copy the original Resources/QuranData folder, including ordered_quran_phonemes (1).json and tokens.txt, then run setup again.");
            return;
        }
        var root = new GameObject("Journey Quran Voice");
        Undo.RegisterCreatedObjectUndo(root, "Setup Journey Voice");
        var repo = Undo.AddComponent<QuranJsonRepository>(root);
        var recognizer = Undo.AddComponent<RealtimeSpeechRecognizerComponent>(root);
        var engine = Undo.AddComponent<QuranAssessmentEngine>(root);
        var recorder = Undo.AddComponent<JourneyVoiceRecorder>(root);
        var audio = Undo.AddComponent<AudioSource>(root);
        audio.playOnAwake = false; audio.loop = false; audio.spatialBlend = 0f;
        var panel = Undo.AddComponent<JourneyVerseVoicePanel>(root);
        SetObject(repo, "orderedQuranPhonemesJson", json);
        SetObject(repo, "tokensText", tokens);
        SetObject(engine, "quranRepository", repo);
        SetObject(engine, "realtimeSpeechRecognizer", recognizer);
        var config = new SerializedObject(recognizer);
        config.FindProperty("modelId").stringValue = "quran-streaming-zipformer2-ctc";
        config.FindProperty("sampleRate").intValue = 16000;
        config.FindProperty("loadOnAwake").boolValue = false;
        config.FindProperty("autoBindInput").boolValue = false;
        config.FindProperty("startCaptureWhenReady").boolValue = false;
        config.FindProperty("startModuleImmediately").boolValue = true;
        config.FindProperty("processChunksInBackground").boolValue = false;
        config.ApplyModifiedProperties();
        SetObject(panel, "assessmentEngine", engine);
        SetObject(panel, "repository", repo);
        SetObject(panel, "recorder", recorder);
        SetObject(panel, "verseAudioSource", audio);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);
        Debug.Log("Journey voice ready for wiring: assign Play Button, Record Button, Percent Text and Verse Audio on JourneyVerseVoicePanel, then save the scene.", root);
    }

    private static void SetObject(Object target, string field, Object value)
    {
        var serialized = new SerializedObject(target);
        var property = serialized.FindProperty(field);
        if (property == null) throw new System.InvalidOperationException("Missing field: " + field);
        property.objectReferenceValue = value;
        serialized.ApplyModifiedProperties();
    }
}
#endif
