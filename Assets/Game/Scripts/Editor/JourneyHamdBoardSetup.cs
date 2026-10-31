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
    private sealed class BoardProfile
    {
        public readonly string rootName, assetName, persianCrystal, englishCrystal, arabicCrystal;
        public readonly int surahNumber, verseCount;
        public string AssetPath => "Assets/Resources/QuranData/" + assetName + ".asset";
        public BoardProfile(string rootName, string assetName, int surahNumber, int verseCount,
            string persianCrystal, string englishCrystal, string arabicCrystal)
        {
            this.rootName = rootName; this.assetName = assetName; this.surahNumber = surahNumber; this.verseCount = verseCount;
            this.persianCrystal = persianCrystal; this.englishCrystal = englishCrystal; this.arabicCrystal = arabicCrystal;
        }
        public bool Matches(Transform target)
        {
            string name = target.name.Trim();
            return name.Equals(rootName, StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(rootName + " (", StringComparison.OrdinalIgnoreCase);
        }
    }
    private static readonly BoardProfile Hamd = new BoardProfile("Hamd Board", "Hamd_001", 1, 7,
        "کریستال سورهٔ حمد", "Al-Fatiha crystal", "بلورة سورة الفاتحة");
    private static readonly BoardProfile Falagh = new BoardProfile("Falagh Board", "Falagh_113", 113, 6,
        "کریستال سورهٔ فلق", "Al-Falaq crystal", "بلورة سورة الفلق");
    private static readonly BoardProfile Naas = new BoardProfile("Naas Board", "Naas_114", 114, 7,
        "کریستال سورهٔ ناس", "An-Nas crystal", "بلورة سورة الناس");
    private static readonly BoardProfile[] Profiles = { Hamd, Falagh, Naas };
    private static readonly string[] TextNames = { "VerseText", "PercentText", "Translate Text" };
    private static IEnumerable<T> All<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static Transform Child(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(child => child.name == name);
    private static T Need<T>(T value, string message) where T : Object
    { if (value == null) throw new InvalidOperationException(message); return value; }
    private static Transform FindRoot(Scene scene, BoardProfile profile)
    {
        Transform selected = Selection.activeTransform;
        if (selected != null && !EditorUtility.IsPersistent(selected) && selected.gameObject.scene == scene)
            for (Transform current = selected; current != null; current = current.parent)
            {
                if (profile.Matches(current)) return current;
                var other = Profiles.FirstOrDefault(candidate => candidate.Matches(current));
                if (other != null) throw new InvalidOperationException("Selected " + other.rootName + ". Select " + profile.rootName + " before running its setup.");
            }
        var candidates = All<Transform>(scene).Where(profile.Matches).ToArray();
        if (candidates.Length == 0) throw new InvalidOperationException("No " + profile.rootName + " exists in the active scene. Add its model to the scene, name its root '" + profile.rootName + "', select it and retry.");
        if (candidates.Length != 1) throw new InvalidOperationException("Select one " + profile.rootName + " root in the active scene's Hierarchy.");
        return candidates[0];
    }
    private static bool HasTexts(Transform root) => TextNames.All(name => Child(root, name)?.GetComponent<TMP_Text>() != null);
    private static Canvas TextTemplate(Scene scene, Transform root) => All<Canvas>(scene)
        .Where(canvas => !canvas.transform.IsChildOf(root) && HasTexts(canvas.transform))
        .OrderByDescending(canvas => canvas.GetComponentInParent<JourneyHamdBoard>() != null)
        .FirstOrDefault();
    private static void EnsureTexts(Transform root, Scene scene, BoardProfile profile)
    {
        if (HasTexts(root)) return;
        var template = Need(TextTemplate(scene, root), "This board needs VerseText, PercentText and Translate Text. Keep the configured Hamd Canvas in the scene so setup can copy it.");
        var canvas = root.GetComponentsInChildren<Canvas>(true).FirstOrDefault();
        if (canvas == null)
        {
            var copy = Object.Instantiate(template.gameObject, root, false);
            copy.name = profile.rootName + " Canvas"; copy.SetActive(true);
            Undo.RegisterCreatedObjectUndo(copy, "Create surah board text canvas");
        }
        else
            foreach (string name in TextNames)
                if (Child(root, name) == null)
                {
                    var copy = Object.Instantiate(Child(template.transform, name).gameObject, canvas.transform, false);
                    copy.name = name; copy.SetActive(true);
                    Undo.RegisterCreatedObjectUndo(copy, "Create missing surah board text");
                }
        foreach (string name in TextNames) Need(Child(root, name)?.GetComponent<TMP_Text>(), name + " needs TMP text.");
    }
    private static Transform EnsureSocket(Transform root, Transform crystal, Scene scene)
    {
        var socket = Child(root, "Crystal Position");
        if (socket != null) return socket;
        var marker = new GameObject("Crystal Position");
        Undo.RegisterCreatedObjectUndo(marker, "Create crystal socket");
        socket = marker.transform; socket.SetParent(root, false);
        var templateBoard = All<JourneyHamdBoard>(scene).FirstOrDefault(board => board.transform != root && board.crystalPosition != null &&
            board.crystalPosition.parent == board.transform);
        if (templateBoard != null)
        {
            socket.localPosition = templateBoard.crystalPosition.localPosition;
            socket.localRotation = templateBoard.crystalPosition.localRotation;
            socket.localScale = templateBoard.crystalPosition.localScale;
        }
        else
        {
            socket.SetPositionAndRotation(crystal.position, crystal.rotation);
            socket.localScale = crystal.localScale;
        }
        return socket;
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
        Undo.RecordObject(box, "Configure surah board click collider");
        if (created)
        {
            var filter = target.GetComponent<MeshFilter>();
            Mesh mesh = shape != null ? shape : filter != null ? filter.sharedMesh : null;
            Bounds bounds = mesh != null ? mesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.4f);
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, Vector3.one * 0.025f);
        }
        box.enabled = true; box.isTrigger = true;
        Undo.RecordObject(target.gameObject, "Make surah board part movable");
        GameObjectUtility.SetStaticEditorFlags(target.gameObject, 0);
        return box;
    }
    private static TMP_Text Label(Transform root, string name, bool firstSetup)
    {
        var label = Need(Need(Child(root, name), root.name + " needs " + name).GetComponent<TMP_Text>(), name + " needs an RTL TMP component.");
        Undo.RecordObject(label, "Configure surah board text"); label.raycastTarget = false;
        var style = label.GetComponent<JourneyLanguageTextStyle>() ?? Undo.AddComponent<JourneyLanguageTextStyle>(label.gameObject);
        Undo.RecordObject(style, "Configure surah board language fonts");
        style.target = label; style.alwaysArabic = name == "VerseText";
        var translation = Child(root, "Translate Text");
        var translationStyle = translation != null ? translation.GetComponent<JourneyLanguageTextStyle>() : null;
        if (!style.alwaysArabic && style.english.font == null && translationStyle != null)
            style.english.font = translationStyle.english.font;
        if (firstSetup)
        {
            Undo.RecordObject(label.rectTransform, "Separate verse, translation and result");
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
    public static void Setup() { Setup(Hamd); }
    [MenuItem("Quran Kids/Setup Falagh Board and Crystal")]
    public static void SetupFalagh() { Setup(Falagh); }
    [MenuItem("Quran Kids/Setup Naas Board and Crystal")]
    public static void SetupNaas() { Setup(Naas); }

    private static void Setup(BoardProfile profile)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("Exit Play Mode before surah board setup."); return; }
        int group = -1;
        try
        {
            Scene scene = SceneManager.GetActiveScene();
            Transform root = FindRoot(scene, profile);
            JourneyHamdBoard existing = root.GetComponent<JourneyHamdBoard>();
            var crystal = Child(root, "Crystal");
            if (crystal == null && existing != null) crystal = existing.crystal;
            if (crystal == null)
            {
                var loose = All<Transform>(scene).Where(t => t.name == "Crystal" && t.GetComponentInParent<JourneyHamdBoard>() == null &&
                    !Profiles.Any(candidate => AncestorMatches(t, candidate))).ToArray();
                if (loose.Length == 1) crystal = loose[0];
            }
            Need(crystal, "Keep Crystal under " + profile.rootName + " while setting up, or assign Crystal on JourneyHamdBoard.");
            if (All<JourneyHamdBoard>(scene).Any(board => board != existing && board.crystal == crystal))
                throw new InvalidOperationException("That Crystal belongs to another board. Assign this board's own Crystal.");
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
            var surah = Need(AssetDatabase.LoadAssetAtPath<JourneySurahDefinition>(profile.AssetPath), "Import " + profile.assetName + ".asset first.");
            Need(All<Camera>(scene).FirstOrDefault(c => c.CompareTag("MainCamera")), "MainCamera is missing.");
            if (surah.surahNumber != profile.surahNumber) throw new InvalidOperationException("Wrong Surah Number in " + profile.AssetPath);
            if (surah.verses == null || surah.verses.Length != profile.verseCount)
                throw new InvalidOperationException("Expected " + profile.verseCount + " entries including Bismillah in " + profile.AssetPath);
            for (int number = 1; number <= profile.verseCount; number++)
                if (!surah.TryGetVerse(number, out _)) throw new InvalidOperationException("Missing or duplicate verse " + number + " in " + profile.AssetPath);
            if (!HasTexts(root)) Need(TextTemplate(scene, root), "Keep the configured Hamd Canvas in the scene so the missing text UI can be created.");

            Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Setup " + profile.rootName + " and Crystal");
            EnsureTexts(root, scene, profile);
            var socket = EnsureSocket(root, crystal, scene);
            var voice = root.GetComponent<JourneyVoiceBoard>();
            bool newVoice = voice == null;
            if (newVoice) voice = Undo.AddComponent<JourneyVoiceBoard>(root.gameObject);
            var hamd = existing != null ? existing : Undo.AddComponent<JourneyHamdBoard>(root.gameObject);
            Undo.RecordObject(hamd, "Assign surah board references");
            hamd.voiceBoard = voice; hamd.player = player.transform; hamd.menu = menu; hamd.radialMenu = radial;
            hamd.stoneController = All<JourneyStoneThrowController>(scene).FirstOrDefault();
            hamd.crystal = crystal; hamd.crystalPosition = socket;
            hamd.persianCrystalName = profile.persianCrystal; hamd.englishCrystalName = profile.englishCrystal; hamd.arabicCrystalName = profile.arabicCrystal;
            ClickBox(crystal); hamd.crystalColliders = crystal.GetComponentsInChildren<Collider>(true);
            foreach (var body in crystal.GetComponentsInChildren<Rigidbody>(true))
            { Undo.RecordObject(body, "Make crystal stationary"); body.isKinematic = true; body.useGravity = false; }
            hamd.socketCollider = ClickBox(socket, mesh);
            hamd.upCollider = ClickBox(up); hamd.downCollider = ClickBox(down);
            hamd.upRenderers = up.GetComponentsInChildren<Renderer>(true); hamd.downRenderers = down.GetComponentsInChildren<Renderer>(true);

            if (hamd.socketVisual != null && !hamd.socketVisual.transform.IsChildOf(socket)) hamd.socketVisual = null;
            if (hamd.socketVisual == null)
            {
                var previous = Child(socket, "Crystal Socket Visual");
                if (previous != null) hamd.socketVisual = previous.gameObject;
                else
                {
                    var ghost = new GameObject("Crystal Socket Visual", typeof(MeshFilter), typeof(MeshRenderer));
                    Undo.RegisterCreatedObjectUndo(ghost, "Create visible crystal socket");
                    ghost.transform.SetParent(socket, false); hamd.socketVisual = ghost;
                }
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
            data.FindProperty("persianTranslationOverride").stringValue = "";
            data.FindProperty("englishTranslationOverride").stringValue = "";
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
            Debug.Log("OK: " + root.name + ", surah " + profile.surahNumber + ", " + profile.verseCount + " entries including Bismillah; crystal pickup/socket, radial inventory, playback/recording and translations. Assign Crystal Icon, Installation Sound and verse audio in " + profile.AssetPath + ", then save the scene.", hamd);
            if (Vector3.Distance(crystal.position, socket.position) < 0.2f)
                Debug.LogWarning("Crystal is still at Crystal Position. Move only Crystal to its hiding place in the map; keep Crystal Position fixed on the board.", crystal);
        }
        catch (Exception error)
        {
            if (group >= 0) Undo.RevertAllDownToGroup(group);
            Debug.LogError(profile.rootName + " setup FAILED: " + error.Message);
        }
    }
    private static bool AncestorMatches(Transform target, BoardProfile profile)
    {
        for (Transform current = target; current != null; current = current.parent)
            if (profile.Matches(current)) return true;
        return false;
    }

    [MenuItem("Quran Kids/Validate Hamd Board and Crystal")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Exit Play Mode before validating surah board setup."); return; }
        Scene scene = SceneManager.GetActiveScene(); var boards = All<JourneyHamdBoard>(scene).ToArray();
        foreach (var profile in Profiles)
            foreach (var root in All<Transform>(scene).Where(profile.Matches))
                if (root.GetComponent<JourneyHamdBoard>() == null)
                    Debug.LogError(root.name + " is not configured. Run Quran Kids > Setup " + profile.rootName + " and Crystal.", root);
        if (boards.Length == 0) { Debug.LogError("Run the corresponding Hamd / Falagh / Naas Board and Crystal setup first."); return; }
        foreach (var board in boards)
        {
            var errors = new List<string>(); var reminders = new List<string>();
            if (board.voiceBoard == null || board.player == null || board.menu == null || board.radialMenu == null || board.radialMenu.menuRoot == null) errors.Add("Voice Board / Player / Menu / Radial references are incomplete.");
            if (board.crystal == null || board.crystalPosition == null || board.socketCollider == null || board.socketVisual == null ||
                board.crystalColliders == null || board.crystalColliders.Length == 0) errors.Add("Crystal / socket / colliders are incomplete.");
            if (board.upCollider == null || board.downCollider == null) errors.Add("Verse arrow colliders are missing.");
            if (board.voiceBoard != null && board.voiceBoard.transform != board.transform) errors.Add("Voice Board must belong to this board root.");
            if (board.crystalPosition != null && !board.crystalPosition.IsChildOf(board.transform)) errors.Add("Crystal Position belongs to another board.");
            if (board.crystal != null && boards.Any(other => other != board && other.crystal == board.crystal)) errors.Add("Two boards share the same Crystal; each needs its own Crystal.");
            if (board.crystalIcon == null) reminders.Add("Assign Crystal Icon on JourneyHamdBoard.");
            if (board.installationSound == null) reminders.Add("Assign Installation Sound on JourneyHamdBoard.");
            if (board.voiceBoard != null)
            {
                var data = new SerializedObject(board.voiceBoard);
                foreach (string field in new[] { "surah", "hamdBoard", "voiceService", "interactionCamera", "playCollider", "recordCollider", "verseText", "percentText", "translateText" })
                    if (data.FindProperty(field).objectReferenceValue == null) errors.Add("Assign " + field + " on JourneyVoiceBoard.");
                var surah = data.FindProperty("surah").objectReferenceValue as JourneySurahDefinition;
                if (surah != null)
                {
                    var profile = Profiles.FirstOrDefault(candidate => candidate.Matches(board.transform)) ??
                        Profiles.FirstOrDefault(candidate => candidate.surahNumber == surah.surahNumber);
                    if (profile == null) errors.Add("Only Hamd, Falagh and Naas profiles are configured by this setup.");
                    else
                    {
                        if (surah.surahNumber != profile.surahNumber) errors.Add("This board is assigned the wrong surah.");
                        if (surah.verses == null || surah.verses.Length != profile.verseCount) errors.Add("Expected " + profile.verseCount + " entries including Bismillah.");
                        for (int number = 1; number <= profile.verseCount; number++)
                            if (!surah.TryGetVerse(number, out var entry)) errors.Add("Missing or duplicate " + profile.rootName + " verse " + number + ".");
                            else if (entry.verseAudio == null) reminders.Add("Assign verse " + number + " audio in " + AssetDatabase.GetAssetPath(surah) + ".");
                    }
                }
            }
            if (errors.Count > 0) Debug.LogError(board.name + " validation FAILED:\n- " + string.Join("\n- ", errors), board);
            else Debug.Log(board.name + ": references OK. Play and phone interaction/audio tests are still required.", board);
            if (reminders.Count > 0) Debug.LogWarning(string.Join("\n", reminders), board);
        }
    }
    [MenuItem("Quran Kids/Validate Surah Boards and Crystals")]
    public static void ValidateSurahBoards() { Validate(); }
}
#endif
