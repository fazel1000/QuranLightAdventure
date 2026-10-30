#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using GameCreator.Runtime.Characters;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class JourneyHamdBoardSetup
{
    private const string SurahPath = "Assets/Resources/QuranData/Hamd_001.asset";
    private static IEnumerable<T> All<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static Transform Child(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(child => child.name == name);
    private static T Need<T>(T value, string message) where T : Object
    { if (value == null) throw new InvalidOperationException(message); return value; }
    private static Transform FindRoot(Scene scene)
    {
        Transform selected = Selection.activeTransform;
        if (selected != null && !EditorUtility.IsPersistent(selected) && selected.gameObject.scene == scene)
            for (Transform current = selected; current != null; current = current.parent)
                if (current.GetComponent<JourneyHamdBoard>() != null || current.name.StartsWith("Hamd Board", StringComparison.OrdinalIgnoreCase)) return current;
        var candidates = All<Transform>(scene).Where(t => t.name.StartsWith("Hamd Board", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length != 1) throw new InvalidOperationException("Select one Hamd Board root in the active scene's Hierarchy.");
        return candidates[0];
    }
    private static void Assign(Object target, string field, Object value)
    {
        var data = new SerializedObject(target);
        var property = data.FindProperty(field);
        if (property == null) throw new InvalidOperationException("Missing serialized field: " + field);
        property.objectReferenceValue = value; data.ApplyModifiedProperties();
    }
    private static BoxCollider ClickBox(Transform target, Mesh shape = null)
    {
        var box = target.GetComponent<BoxCollider>();
        bool created = box == null;
        if (created) box = Undo.AddComponent<BoxCollider>(target.gameObject);
        Undo.RecordObject(box, "Configure Hamd click collider");
        if (created)
        {
            var filter = target.GetComponent<MeshFilter>();
            Mesh mesh = shape != null ? shape : filter != null ? filter.sharedMesh : null;
            Bounds bounds = mesh != null ? mesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.4f);
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, Vector3.one * 0.025f);
        }
        box.enabled = true; box.isTrigger = true;
        Undo.RecordObject(target.gameObject, "Make Hamd part movable");
        GameObjectUtility.SetStaticEditorFlags(target.gameObject, 0);
        return box;
    }
    private static TMP_Text Label(Transform root, string name, bool firstSetup)
    {
        var label = Need(Need(Child(root, name), "Hamd Board needs " + name).GetComponent<TMP_Text>(), name + " needs an RTL TMP component.");
        Undo.RecordObject(label, "Configure Hamd text"); label.raycastTarget = false;
        var style = label.GetComponent<JourneyLanguageTextStyle>() ?? Undo.AddComponent<JourneyLanguageTextStyle>(label.gameObject);
        Undo.RecordObject(style, "Configure Hamd language fonts");
        style.target = label; style.alwaysArabic = name == "VerseText";
        var translation = Child(root, "Translate Text");
        var translationStyle = translation != null ? translation.GetComponent<JourneyLanguageTextStyle>() : null;
        if (!style.alwaysArabic && style.english.font == null && translationStyle != null)
            style.english.font = translationStyle.english.font;
        if (firstSetup)
        {
            Undo.RecordObject(label.rectTransform, "Separate Hamd verse, translation and result");
            Vector2 position = label.rectTransform.anchoredPosition;
            position.y = name == "VerseText" ? 365f : name == "Translate Text" ? -20f : -365f;
            label.rectTransform.anchoredPosition = position;
            label.rectTransform.sizeDelta = new Vector2(label.rectTransform.sizeDelta.x, name == "VerseText" ? 400f : 300f);
            if (name == "PercentText")
            {
                // This label also carries the longer crystal instructions.
                if (style.persian.fontSize <= 0f) style.persian.fontSize = Mathf.Min(80f, label.fontSize);
                if (style.english.fontSize <= 0f) style.english.fontSize = Mathf.Min(80f, label.fontSize);
                if (style.arabic.fontSize <= 0f) style.arabic.fontSize = Mathf.Min(80f, label.fontSize);
            }
            else
            {
                label.enableAutoSizing = true;
                label.fontSizeMax = label.fontSize; label.fontSizeMin = Mathf.Min(48f, label.fontSize);
            }
        }
        // Static localization must not overwrite this dynamic verse / result / translation.
        var staticText = label.GetComponent<JourneyLocalizedText>();
        if (staticText != null) Undo.DestroyObjectImmediate(staticText);
        return label;
    }

    [MenuItem("Quran Kids/Setup Hamd Board and Crystal")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("Exit Play Mode before Hamd setup."); return; }
        int group = -1;
        try
        {
            Scene scene = SceneManager.GetActiveScene();
            Transform root = FindRoot(scene);
            JourneyHamdBoard existing = root.GetComponent<JourneyHamdBoard>();
            var crystal = existing != null ? existing.crystal : null;
            if (crystal == null) crystal = Child(root, "Crystal");
            if (crystal == null)
            {
                var loose = All<Transform>(scene).Where(t => t.name == "Crystal" && t.GetComponentInParent<JourneyHamdBoard>() == null).ToArray();
                if (loose.Length == 1) crystal = loose[0];
            }
            Need(crystal, "Keep Crystal under Hamd Board while setting up, or assign Crystal on JourneyHamdBoard.");
            var socket = Need(Child(root, "Crystal Position"), "Add Crystal Position under Hamd Board.");
            var up = Need(Child(root, "Up Button"), "Up Button is missing.");
            var down = Need(Child(root, "Down Button"), "Down Button is missing.");
            var play = Need(Child(root, "Play Button"), "Play Button is missing.");
            var record = Need(Child(root, "Record Button"), "Record Button is missing.");
            var mesh = Need(Need(crystal.GetComponent<MeshFilter>(), "Crystal needs its MeshFilter.").sharedMesh, "Crystal mesh is missing.");
            var sourceRenderer = Need(crystal.GetComponent<MeshRenderer>(), "Crystal needs its MeshRenderer.");
            var voiceService = Need(All<JourneyVerseVoicePanel>(scene).FirstOrDefault(), "Run Quran Kids > Setup Journey Voice first.");
            var menu = Need(All<JourneyMenuController>(scene).FirstOrDefault(), "JourneyMenuController is missing.");
            var player = Need(All<Character>(scene).FirstOrDefault(c => c.IsPlayer), "GameCreator Player is missing.");
            var radial = Need(All<JourneyRadialMenu>(scene).FirstOrDefault(), "Run Setup Air Water and World Systems first; RadialMenu is missing.");
            var surah = Need(AssetDatabase.LoadAssetAtPath<JourneySurahDefinition>(SurahPath), "Import Hamd_001.asset from this update first.");
            foreach (string name in new[] { "VerseText", "PercentText", "Translate Text" })
                Need(Need(Child(root, name), name + " is missing.").GetComponent<TMP_Text>(), name + " needs TMP text.");

            Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Setup Hamd Board and Crystal");
            var voice = root.GetComponent<JourneyVoiceBoard>();
            bool newVoice = voice == null;
            if (newVoice) voice = Undo.AddComponent<JourneyVoiceBoard>(root.gameObject);
            var hamd = existing != null ? existing : Undo.AddComponent<JourneyHamdBoard>(root.gameObject);
            Undo.RecordObject(hamd, "Assign Hamd Board references");
            hamd.voiceBoard = voice; hamd.player = player.transform; hamd.menu = menu; hamd.radialMenu = radial;
            hamd.stoneController = All<JourneyStoneThrowController>(scene).FirstOrDefault();
            hamd.crystal = crystal; hamd.crystalPosition = socket;
            ClickBox(crystal); hamd.crystalColliders = crystal.GetComponentsInChildren<Collider>(true);
            foreach (var body in crystal.GetComponentsInChildren<Rigidbody>(true))
            { Undo.RecordObject(body, "Make crystal stationary"); body.isKinematic = true; body.useGravity = false; }
            hamd.socketCollider = ClickBox(socket, mesh);
            hamd.upCollider = ClickBox(up); hamd.downCollider = ClickBox(down);
            hamd.upRenderers = up.GetComponentsInChildren<Renderer>(true); hamd.downRenderers = down.GetComponentsInChildren<Renderer>(true);

            if (hamd.socketVisual == null)
            {
                var ghost = new GameObject("Crystal Socket Visual", typeof(MeshFilter), typeof(MeshRenderer));
                Undo.RegisterCreatedObjectUndo(ghost, "Create visible crystal socket");
                ghost.transform.SetParent(socket, false); hamd.socketVisual = ghost;
            }
            var ghostFilter = hamd.socketVisual.GetComponent<MeshFilter>() ?? Undo.AddComponent<MeshFilter>(hamd.socketVisual);
            var ghostRenderer = hamd.socketVisual.GetComponent<MeshRenderer>() ?? Undo.AddComponent<MeshRenderer>(hamd.socketVisual);
            Undo.RecordObjects(new Object[] { ghostFilter, ghostRenderer, hamd.socketVisual.transform }, "Configure crystal socket visual");
            ghostFilter.sharedMesh = mesh; ghostRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            hamd.socketVisual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(hamd.placedLocalEulerAngles));
            hamd.socketVisual.transform.localScale = hamd.placedLocalScale;
            if (hamd.effectsSource == null)
            {
                hamd.effectsSource = root.GetComponent<AudioSource>() ?? Undo.AddComponent<AudioSource>(root.gameObject);
                Undo.RecordObject(hamd.effectsSource, "Configure crystal audio");
                hamd.effectsSource.playOnAwake = false; hamd.effectsSource.loop = false; hamd.effectsSource.spatialBlend = 0f;
            }

            Assign(voice, "hamdBoard", hamd); Assign(voice, "surah", surah); Assign(voice, "voiceService", voiceService);
            Assign(voice, "scoreReceiver", All<LightBrushPuzzle>(scene).FirstOrDefault()); Assign(voice, "menu", menu);
            Assign(voice, "interactionCamera", All<Camera>(scene).FirstOrDefault(c => c.CompareTag("MainCamera")));
            Assign(voice, "playCollider", ClickBox(play)); Assign(voice, "recordCollider", ClickBox(record));
            Assign(voice, "playVisual", play); Assign(voice, "recordVisual", record);
            Assign(voice, "verseText", Label(root, "VerseText", newVoice)); Assign(voice, "percentText", Label(root, "PercentText", newVoice));
            Assign(voice, "translateText", Label(root, "Translate Text", newVoice)); Assign(voice, "statusText", null);
            var data = new SerializedObject(voice);
            data.FindProperty("requireWordPuzzle").boolValue = false;
            if (newVoice)
            {
                data.FindProperty("verseNumber").intValue = 1;
                data.FindProperty("pressedOffset").vector3Value = new Vector3(0f, 0f, 0.04f);
            }
            foreach (var pair in new[] { ("playRenderers", play), ("recordRenderers", record) })
            {
                var renderers = pair.Item2.GetComponentsInChildren<Renderer>(true);
                var array = data.FindProperty(pair.Item1); array.arraySize = renderers.Length;
                for (int i = 0; i < renderers.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
            }
            data.ApplyModifiedProperties();
            // Accurate body occlusion, without a box covering the buttons or the crystal socket.
            var boardBody = Child(root, "Board");
            if (boardBody != null && boardBody.GetComponent<MeshFilter>() != null && boardBody.GetComponent<Collider>() == null)
            {
                var bodyCollider = Undo.AddComponent<MeshCollider>(boardBody.gameObject);
                bodyCollider.sharedMesh = boardBody.GetComponent<MeshFilter>().sharedMesh; bodyCollider.convex = false;
            }
            foreach (var inventory in All<JourneyEquipmentInventory>(scene))
            {
                Undo.RecordObject(inventory, "Hide equipped sword while using stone");
                inventory.stoneController = hamd.stoneController; inventory.hideSwordWhileUsingStone = true;
                EditorUtility.SetDirty(inventory); PrefabUtility.RecordPrefabInstancePropertyModifications(inventory);
            }
            foreach (var component in root.GetComponentsInChildren<Component>(true))
                if (component != null) { EditorUtility.SetDirty(component); PrefabUtility.RecordPrefabInstancePropertyModifications(component); }
            EditorUtility.SetDirty(crystal); PrefabUtility.RecordPrefabInstancePropertyModifications(crystal);
            EditorSceneManager.MarkSceneDirty(scene); Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = root.gameObject;
            Debug.Log("OK: Hamd Board, 7 verses, crystal pickup/socket, radial inventory and temporary sword hiding. Assign Crystal Icon, Installation Sound and verse audio clips, move Crystal to its hiding place, then save the scene.", hamd);
            if (Vector3.Distance(crystal.position, socket.position) < 0.2f)
                Debug.LogWarning("Crystal is still at Crystal Position. Move only Crystal to its hiding place in the map; keep Crystal Position fixed on the board.", crystal);
        }
        catch (Exception error)
        {
            if (group >= 0) Undo.RevertAllDownToGroup(group);
            Debug.LogError("Hamd setup FAILED: " + error.Message);
        }
    }

    [MenuItem("Quran Kids/Validate Hamd Board and Crystal")]
    public static void Validate()
    {
        Scene scene = SceneManager.GetActiveScene(); var boards = All<JourneyHamdBoard>(scene).ToArray();
        if (boards.Length == 0) { Debug.LogError("Run Quran Kids > Setup Hamd Board and Crystal first."); return; }
        foreach (var board in boards)
        {
            var errors = new List<string>(); var reminders = new List<string>();
            if (board.voiceBoard == null || board.player == null || board.menu == null || board.radialMenu == null || board.radialMenu.menuRoot == null) errors.Add("Voice Board / Player / Menu / Radial references are incomplete.");
            if (board.crystal == null || board.crystalPosition == null || board.socketCollider == null || board.socketVisual == null ||
                board.crystalColliders == null || board.crystalColliders.Length == 0) errors.Add("Crystal / socket / colliders are incomplete.");
            if (board.upCollider == null || board.downCollider == null) errors.Add("Verse arrow colliders are missing.");
            if (board.crystalIcon == null) reminders.Add("Assign Crystal Icon on JourneyHamdBoard.");
            if (board.installationSound == null) reminders.Add("Assign Installation Sound on JourneyHamdBoard.");
            if (board.voiceBoard != null)
            {
                var data = new SerializedObject(board.voiceBoard);
                foreach (string field in new[] { "surah", "hamdBoard", "voiceService", "interactionCamera", "playCollider", "recordCollider", "verseText", "percentText", "translateText" })
                    if (data.FindProperty(field).objectReferenceValue == null) errors.Add("Assign " + field + " on JourneyVoiceBoard.");
                var surah = data.FindProperty("surah").objectReferenceValue as JourneySurahDefinition;
                if (surah != null)
                    for (int number = 1; number <= 7; number++)
                        if (!surah.TryGetVerse(number, out var entry)) errors.Add("Missing or duplicate Hamd verse " + number + ".");
                        else if (entry.verseAudio == null) reminders.Add("Assign Hamd verse " + number + " audio in " + SurahPath + ".");
            }
            if (errors.Count > 0) Debug.LogError(board.name + " validation FAILED:\n- " + string.Join("\n- ", errors), board);
            else Debug.Log(board.name + ": references OK. Play and phone interaction/audio tests are still required.", board);
            if (reminders.Count > 0) Debug.LogWarning(string.Join("\n", reminders), board);
        }
    }
}
#endif
