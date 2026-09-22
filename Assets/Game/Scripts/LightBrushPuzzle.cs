
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

[DefaultExecutionOrder(-2000)]
[RequireComponent(typeof(LineRenderer))]
public class LightBrushPuzzle : MonoBehaviour
{
    [Header("Puzzle")]
    [SerializeField] private Camera puzzleCamera;
    [SerializeField] private LayerMask letterLayer;

    [Header("Surah Al-Ikhlas")]
    [Tooltip("All assigned puzzles are visible and can be solved in any order. Off uses only Bismillah.")]
    [FormerlySerializedAs("enableIkhlasSequence")]
    [SerializeField] private bool enableIkhlasPuzzles;

    [Header("Bismillah - 4 words")]
    [FormerlySerializedAs("orderedLetters")]
    [SerializeField] private Transform[] bismillahWords;

    [Header("Ayah 1 - Qul Huwa Allahu Ahad - 4 words")]
    [SerializeField] private Transform[] ayah1Words = new Transform[4];
    [Header("Ayah 2 - Allahu As-Samad - 2 words")]
    [SerializeField] private Transform[] ayah2Words = new Transform[2];
    [Header("Ayah 3 - Lam Yalid Wa Lam Yulad - 4 words")]
    [SerializeField] private Transform[] ayah3Words = new Transform[4];
    [Header("Ayah 4 - Wa Lam Yakun Lahu Kufuwan Ahad - 5 words")]
    [SerializeField] private Transform[] ayah4Words = new Transform[5];

    [Header("Score Per Puzzle")]
    [SerializeField, Min(0)] private int bismillahScore = 10;
    [SerializeField, Min(0)] private int ayah1Score = 10;
    [SerializeField, Min(0)] private int ayah2Score = 10;
    [SerializeField, Min(0)] private int ayah3Score = 10;
    [SerializeField, Min(0)] private int ayah4Score = 10;

    [Header("Score UI")]
    [SerializeField] private global::RTLTMPro.RTLTextMeshPro scoreText;
    [SerializeField] private string scorePrefix = "امتیاز: ";
    [SerializeField] private bool usePersianDigits = true;
    [SerializeField] private UnityEvent<int> onScoreChanged = new UnityEvent<int>();

    [global::System.Serializable]
    public sealed class PuzzleFinishedEvent : UnityEvent<int> { }
    [Header("Each Puzzle Finished (1 = Bismillah, 5 = Ayah 4)")]
    [SerializeField] private PuzzleFinishedEvent onPuzzleCompleted = new PuzzleFinishedEvent();

    [Header("Light Line")]
    [SerializeField] private Color glowColor = new Color(0.05f, 0.85f, 1f, 0.42f);
    [SerializeField] private Color coreColor = new Color(0.8f, 1f, 1f, 1f);
    [SerializeField, Min(0.01f)] private float glowWidth = 0.18f;
    [SerializeField, Min(0.005f)] private float coreWidth = 0.055f;
    [SerializeField, Min(0.05f)] private float travelDuration = 0.32f;
    [SerializeField, Min(0f)] private float pulseSpeed = 4f;
    [SerializeField, Min(0f)] private float connectionInset = 0.18f;

    [Header("Audio - Optional Overrides")]
    [SerializeField] private AudioClip lightMoveSound;
    [SerializeField] private AudioClip successSound;
    [SerializeField, Range(0f, 1f)] private float moveVolume = 0.65f;
    [SerializeField, Range(0f, 1f)] private float successVolume = 0.85f;

    [Header("Success")]
    [SerializeField] private ParticleSystem successEffectPrefab;
    [SerializeField, Min(0f)] private float successEffectHeight = 0.8f;
    [Tooltip("Invoked once when all configured puzzles have been solved, in any order.")]
    [SerializeField] private UnityEvent onCompleted;

    private sealed class LightSegment
    {
        public GameObject Root;
        public int PuzzleIndex;
        public LineRenderer Glow;
        public LineRenderer Core;
    }

    private LineRenderer lineTemplate;
    private readonly List<LightSegment> lightSegments =
        new List<LightSegment>();
    private AudioSource audioSource;
    private Material glowMaterial;
    private Material coreMaterial;
    private Material energyMaterial;
    private Material particleMaterial;

    private bool drawing;
    private bool completed;
    private int currentLetter;
    private Transform[][] wordGroups;
    private int puzzleIndex;
    private bool configurationValid;
    private int runVersion;
    private bool[] solvedPuzzles;
    private bool[] playablePuzzles;
    public int TotalScore { get; private set; }
    public int SolvedPuzzleCount { get; private set; }
    public bool IsAllPuzzlesCompleted => IsSequenceCompleted;
    private readonly List<GameObject> transientEffects = new List<GameObject>();
    public int CurrentPuzzleNumber => puzzleIndex + 1;
    public bool IsSequenceCompleted { get; private set; }
    private Transform[] ActiveWords => wordGroups[puzzleIndex];

    private static readonly HashSet<LightBrushPuzzle> movementBlockers =
        new HashSet<LightBrushPuzzle>();
    private readonly HashSet<int> puzzleTouches = new HashSet<int>();
    private readonly HashSet<int> heldTouches = new HashSet<int>();
    private bool puzzleMouseHeld;
    private bool inputFocused = true;
    private bool inputPaused;

    public static bool IsMovementBlocked => movementBlockers.Count > 0 || JourneyVoiceBoard.IsPointerCaptured || JourneyStoneThrowController.IsMovementBlocked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetMovementBlockers()
    {
        movementBlockers.Clear();
    }

    private void UpdateMovementBlock()
    {
        if (!configurationValid || !inputFocused || inputPaused)
        {
            ReleaseMovementBlock();
            return;
        }

        heldTouches.Clear();
        bool anyTouchHeld = false;
        if (Touchscreen.current != null)
        {
            foreach (var touch in Touchscreen.current.touches)
            {
                if (!touch.press.isPressed) continue;
                anyTouchHeld = true;
                int id = touch.touchId.ReadValue();
                heldTouches.Add(id);
                if (touch.press.wasPressedThisFrame && HitLetter(touch.position.ReadValue()) != null)
                    puzzleTouches.Add(id);
            }
        }
        puzzleTouches.IntersectWith(heldTouches);

        var mouse = Mouse.current;
        if (anyTouchHeld || mouse == null || !mouse.leftButton.isPressed)
            puzzleMouseHeld = false;
        else if (mouse.leftButton.wasPressedThisFrame)
            puzzleMouseHeld = HitLetter(mouse.position.ReadValue()) != null;

        SetMovementBlock(puzzleMouseHeld || puzzleTouches.Count > 0);
    }

    private void SetMovementBlock(bool blocked)
    {
        bool changed = blocked ? movementBlockers.Add(this) : movementBlockers.Remove(this);
        if (!changed) return;
        RightScreenDragArea.ClearInput();
        JourneyMovementSettings.RefreshPuzzleInput();
    }

    private void ReleaseMovementBlock()
    {
        puzzleTouches.Clear();
        heldTouches.Clear();
        puzzleMouseHeld = false;
        SetMovementBlock(false);
    }

    private void OnApplicationFocus(bool focused)
    {
        inputFocused = focused;
        if (!focused)
        {
            ReleaseMovementBlock();
            if (drawing) ResetBrush();
        }
    }

    private void OnApplicationPause(bool paused)
    {
        inputPaused = paused;
        if (paused)
        {
            ReleaseMovementBlock();
            if (drawing) ResetBrush();
        }
    }

    private void Awake()
    {
        if (puzzleCamera == null)
        {
            puzzleCamera = Camera.main;
        }

        PrepareSceneSuccessEffect();

        lineTemplate = GetComponent<LineRenderer>();
        glowMaterial = CreateEffectMaterial(lineTemplate.sharedMaterial, Color.white);
        coreMaterial = CreateEffectMaterial(lineTemplate.sharedMaterial, Color.white);
        energyMaterial = CreateEffectMaterial(lineTemplate.sharedMaterial, coreColor * 3f);
        particleMaterial = CreateEffectMaterial(lineTemplate.sharedMaterial, Color.white);

        lineTemplate.positionCount = 0;
        lineTemplate.enabled = false;

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;

        if (lightMoveSound == null)
        {
            lightMoveSound = CreateMoveSound();
        }

        if (successSound == null)
        {
            successSound = CreateSuccessSound();
        }

        RestartAllPuzzles();
    }

    private void PrepareSceneSuccessEffect()
    {
        if (successEffectPrefab == null)
        {
            return;
        }

        GameObject sourceObject = successEffectPrefab.gameObject;

        // If a scene object was assigned instead of a Project prefab,
        // keep the original hidden until the puzzle is completed.
        if (sourceObject.scene.IsValid())
        {
            sourceObject.SetActive(false);
        }
    }

    private void Update()
    {
        UpdateMovementBlock();
        AnimateLinePulse();
        if (JourneyVoiceBoard.IsPointerCaptured || JourneyStoneThrowController.IsMovementBlocked)
        {
            if (drawing) ResetBrush();
            return;
        }

        if (!inputFocused || inputPaused || !configurationValid || completed || !ReadPointer(
                out Vector2 position,
                out bool pressed,
                out bool held,
                out bool released))
        {
            return;
        }

        if (pressed)
        {
            BeginDrawing(position);
        }

        if (drawing && held)
        {
            ContinueDrawing(position);
        }

        if (drawing && released)
        {
            ResetBrush();
        }
    }

    private void BeginDrawing(Vector2 screenPosition)
    {
        Transform letter = HitLetter(screenPosition);
        if (letter == null) return;

        int selected = -1;
        for (int index = 0; index < wordGroups.Length; index++)
        {
            if (playablePuzzles[index] && !solvedPuzzles[index] && wordGroups[index][0] == letter)
            {
                selected = index;
                break;
            }
        }
        if (selected < 0) return;

        ClearUnfinishedSegments();
        puzzleIndex = selected;
        drawing = true;
        currentLetter = 0;
        PlayMoveSound(0.45f);
    }

    private void ContinueDrawing(Vector2 screenPosition)
    {
        if (currentLetter + 1 >= ActiveWords.Length)
        {
            return;
        }

        Transform letter = HitLetter(screenPosition);
        if (letter != ActiveWords[currentLetter + 1])
        {
            return;
        }

        Transform previousLetter = ActiveWords[currentLetter];
        currentLetter++;

        GetConnectionPoints(previousLetter, letter, out Vector3 from, out Vector3 to);
        CreateLightSegment(from, to);
        StartCoroutine(TravelLight(from, to));
        PlayMoveSound(1f);

        if (currentLetter == ActiveWords.Length - 1)
        {
            completed = true;
            drawing = false;
            StartCoroutine(CompletePuzzleAfterTravel());
        }
    }

    private System.Collections.IEnumerator CompletePuzzleAfterTravel()
    {
        int version = runVersion;
        int finishedPuzzle = puzzleIndex;
        yield return WaitForGameplaySeconds(travelDuration);
        if (version != runVersion || solvedPuzzles[finishedPuzzle]) yield break;

        Vector3 effectPosition = CalculateSuccessPosition();
        PlaySuccessEffect(effectPosition);
        if (successSound != null) audioSource.PlayOneShot(successSound, successVolume);

        // Commit the result before invoking any external event: no duplicate awards.
        solvedPuzzles[finishedPuzzle] = true;
        SolvedPuzzleCount++;
        TotalScore = (int)global::System.Math.Min(int.MaxValue,
            (long)TotalScore + GetPuzzleScore(finishedPuzzle));
        IsSequenceCompleted = SolvedPuzzleCount == wordGroups.Length;
        completed = IsSequenceCompleted;
        currentLetter = 0;
        RefreshScoreDisplay();

        onScoreChanged?.Invoke(TotalScore);
        if (version != runVersion) yield break;
        onPuzzleCompleted?.Invoke(finishedPuzzle + 1);
        if (version != runVersion) yield break;
        if (IsSequenceCompleted)
        {
            onCompleted?.Invoke();
            Debug.Log("LightBrushPuzzle: all configured puzzles completed.", this);
        }
    }

    private int GetPuzzleScore(int index)
    {
        int points;
        switch (index)
        {
            case 0: points = bismillahScore; break;
            case 1: points = ayah1Score; break;
            case 2: points = ayah2Score; break;
            case 3: points = ayah3Score; break;
            case 4: points = ayah4Score; break;
            default: points = 0; break;
        }
        return Mathf.Max(0, points);
    }

    public bool IsPuzzleSolved(int puzzleNumber)
    {
        return solvedPuzzles != null && puzzleNumber >= 1 &&
            puzzleNumber <= solvedPuzzles.Length && solvedPuzzles[puzzleNumber - 1];
    }

    // Award once per successfully assessed recording. Examples: 55% -> 5 points.
    public void AddVoicePoints(int points)
    {
        if (points <= 0) return;
        TotalScore = (int)global::System.Math.Min(int.MaxValue, (long)TotalScore + points);
        RefreshScoreDisplay();
        onScoreChanged?.Invoke(TotalScore);
    }

    public void RefreshScoreDisplay()
    {
        if (scoreText == null) return;
        string number = TotalScore.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
        if (usePersianDigits)
        {
            char[] digits = number.ToCharArray();
            for (int i = 0; i < digits.Length; i++)
                if (digits[i] >= '0' && digits[i] <= '9')
                    digits[i] = (char)('۰' + digits[i] - '0');
            number = new string(digits);
        }
        scoreText.text = (scorePrefix ?? string.Empty) + number;
    }

    private void OnEnable()
    {
        RefreshScoreDisplay();
    }

    private System.Collections.IEnumerator WaitForGameplaySeconds(float seconds)
    {
        float elapsed = 0f;
        // JourneyMenuController disables this component while a menu is open.
        // Disabled MonoBehaviours still run coroutines, so gate the delay explicitly.
        while (!isActiveAndEnabled || elapsed < Mathf.Max(0f, seconds))
        {
            yield return null;
            if (isActiveAndEnabled) elapsed += Time.deltaTime;
        }
    }

    public void RestartAllPuzzles()
    {
        if (!Application.isPlaying || lineTemplate == null) return;
        ReleaseMovementBlock();
        runVersion++;
        StopAllCoroutines();
        ClearSegments();
        ClearTransientEffects();
        if (audioSource != null) audioSource.Stop();
        puzzleIndex = 0;
        currentLetter = 0;
        drawing = false;
        completed = false;
        IsSequenceCompleted = false;
        wordGroups = enableIkhlasPuzzles
            ? new[] { bismillahWords, ayah1Words, ayah2Words, ayah3Words, ayah4Words }
            : new[] { bismillahWords };
        solvedPuzzles = new bool[wordGroups.Length];
        SolvedPuzzleCount = 0;
        TotalScore = 0;
        configurationValid = ValidateWords();
        if (configurationValid) ShowAllWords();
        RefreshScoreDisplay();
        onScoreChanged?.Invoke(TotalScore);
    }

    private bool ValidateWords()
    {
        playablePuzzles = new bool[wordGroups.Length];
        var assigned = new List<Transform>();
        int playableCount = 0;
        for (int group = 0; group < wordGroups.Length; group++)
        {
            string problem = CheckWordGroup(group, assigned);
            if (problem != null)
            {
                Debug.LogWarning("LightBrushPuzzle: puzzle " + (group + 1) +
                    " is skipped for this run: " + problem, this);
                continue;
            }
            playablePuzzles[group] = true;
            playableCount++;
            assigned.AddRange(wordGroups[group]);
        }

        return playableCount > 0 || ConfigurationError(
            "No playable puzzle. Fully assign at least one word list with valid Colliders and Letter Layer.");
    }

    private string CheckWordGroup(int group, List<Transform> assigned)
    {
        int[] requiredCounts = { 4, 4, 2, 4, 5 };
        Transform[] words = wordGroups[group];
        if (words == null || words.Length < 2 ||
            (enableIkhlasPuzzles && words.Length != requiredCounts[group]))
            return "Expected " + (enableIkhlasPuzzles ? requiredCounts[group].ToString() : "at least 2") + " words.";

        // Validate locally before reserving any references for this group.
        var candidates = new List<Transform>(assigned);
        foreach (Transform word in words)
        {
            if (word == null) return "One or more word fields are empty.";
            if (transform.IsChildOf(word)) return "A word cannot contain the LightBrush controller.";
            foreach (Transform previous in candidates)
                if (word.IsChildOf(previous) || previous.IsChildOf(word))
                    return "Use separate, non-nested word objects for each slot and puzzle.";
            candidates.Add(word);
            bool hittable = false;
            foreach (Collider collider in word.GetComponentsInChildren<Collider>(true))
                if (collider.enabled && (letterLayer.value & (1 << collider.gameObject.layer)) != 0)
                    hittable = true;
            if (!hittable)
                return word.name + " needs an enabled Collider on a layer in Letter Layer.";
        }
        return null;
    }

    private bool ConfigurationError(string message)
    {
        Debug.LogError("LightBrushPuzzle: " + message, this);
        return false;
    }

    private void ShowAllWords()
    {
        foreach (Transform[] words in wordGroups)
        {
            if (words == null) continue;
            foreach (Transform word in words)
                if (word != null) word.gameObject.SetActive(true);
        }
    }

    private void ClearTransientEffects()
    {
        foreach (GameObject effect in transientEffects)
            if (effect != null) Destroy(effect);
        transientEffects.Clear();
    }

    private void OnDisable()
    {
        ReleaseMovementBlock();
        // Cancel a partial gesture when opening a menu; keep completed puzzle progress.
        if (!drawing) return;
        StopAllCoroutines();
        ClearTransientEffects();
        ResetBrush();
    }

    private Transform HitLetter(Vector2 screenPosition)
    {
        if (puzzleCamera == null)
        {
            return null;
        }

        if (!IsValidScreenPosition(screenPosition))
        {
            return null;
        }

        Ray ray = puzzleCamera.ScreenPointToRay(screenPosition);

        if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                500f,
                letterLayer,
                QueryTriggerInteraction.Collide))
        {
            return null;
        }

        for (int group = 0; group < wordGroups.Length; group++)
        {
            if (!playablePuzzles[group]) continue;
            foreach (Transform letter in wordGroups[group])
            {
                if (letter != null &&
                    (hit.transform == letter || hit.transform.IsChildOf(letter)))
                    return letter;
            }
        }

        return null;
    }

    private bool IsValidScreenPosition(Vector2 screenPosition)
    {
        if (float.IsNaN(screenPosition.x) ||
            float.IsNaN(screenPosition.y) ||
            float.IsInfinity(screenPosition.x) ||
            float.IsInfinity(screenPosition.y))
        {
            return false;
        }

        return puzzleCamera.pixelRect.Contains(screenPosition);
    }

    private void GetConnectionPoints(
        Transform fromLetter,
        Transform toLetter,
        out Vector3 from,
        out Vector3 to)
    {
        Collider fromCollider = fromLetter.GetComponentInChildren<Collider>();
        Collider toCollider = toLetter.GetComponentInChildren<Collider>();

        Vector3 fromCenter = fromCollider != null
            ? fromCollider.bounds.center
            : fromLetter.position;

        Vector3 toCenter = toCollider != null
            ? toCollider.bounds.center
            : toLetter.position;

        Vector3 fromSurface = fromCollider != null
            ? fromCollider.ClosestPoint(toCenter)
            : fromCenter;

        Vector3 toSurface = toCollider != null
            ? toCollider.ClosestPoint(fromCenter)
            : toCenter;

        from = Vector3.MoveTowards(fromSurface, fromCenter, connectionInset);
        to = Vector3.MoveTowards(toSurface, toCenter, connectionInset);
    }

    private void CreateLightSegment(Vector3 from, Vector3 to)
    {
        GameObject root = new GameObject(
            $"Light Segment {lightSegments.Count + 1}"
        );
        root.transform.SetParent(transform, false);

        LineRenderer glow = CreateSegmentLine(
            root.transform,
            "Glow",
            glowWidth,
            CreateGlowGradient(),
            glowMaterial,
            0
        );

        LineRenderer core = CreateSegmentLine(
            root.transform,
            "Core",
            coreWidth,
            CreateCoreGradient(),
            coreMaterial,
            1
        );

        glow.positionCount = 2;
        core.positionCount = 2;

        glow.SetPosition(0, from);
        glow.SetPosition(1, to);
        core.SetPosition(0, from);
        core.SetPosition(1, to);

        lightSegments.Add(new LightSegment
        {
            Root = root,
            PuzzleIndex = puzzleIndex,
            Glow = glow,
            Core = core
        });
    }

    private LineRenderer CreateSegmentLine(
        Transform parent,
        string objectName,
        float width,
        Gradient gradient,
        Material material,
        int sortingOrder)
    {
        GameObject lineObject = new GameObject(objectName);
        lineObject.transform.SetParent(parent, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        ConfigureLine(line, width, gradient, material, sortingOrder);
        return line;
    }

    private void ConfigureLine(
        LineRenderer line,
        float width,
        Gradient gradient,
        Material material,
        int sortingOrder)
    {
        line.useWorldSpace = true;
        line.loop = false;
        line.positionCount = 0;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Tile;
        line.numCornerVertices = 12;
        line.numCapVertices = 12;
        line.widthCurve = AnimationCurve.EaseInOut(0f, 0.82f, 1f, 1f);
        line.widthMultiplier = width;
        line.colorGradient = gradient;
        line.sharedMaterial = material;
        line.sortingOrder = sortingOrder;
    }

    private void AnimateLinePulse()
    {
        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * 0.14f;

        foreach (LightSegment segment in lightSegments)
        {
            if (segment.Glow == null || segment.Core == null)
            {
                continue;
            }

            segment.Glow.widthMultiplier = glowWidth * pulse;
            segment.Core.widthMultiplier =
                coreWidth * (2f - pulse * 0.82f);
        }
    }

    private System.Collections.IEnumerator TravelLight(Vector3 from, Vector3 to)
    {
        GameObject energy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        energy.name = "Traveling Light";
        transientEffects.Add(energy);

        Collider generatedCollider = energy.GetComponent<Collider>();
        if (generatedCollider != null)
        {
            Destroy(generatedCollider);
        }

        MeshRenderer meshRenderer = energy.GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = energyMaterial;

        TrailRenderer trail = energy.AddComponent<TrailRenderer>();
        trail.time = 0.2f;
        trail.minVertexDistance = 0.015f;
        trail.alignment = LineAlignment.View;
        trail.widthCurve = AnimationCurve.EaseInOut(0f, coreWidth, 1f, 0f);
        trail.colorGradient = CreateCoreGradient();
        trail.sharedMaterial = coreMaterial;
        trail.numCornerVertices = 8;
        trail.numCapVertices = 8;

        float elapsed = 0f;

        while (elapsed < travelDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / travelDuration);
            progress = 1f - Mathf.Pow(1f - progress, 3f);

            energy.transform.position = Vector3.Lerp(from, to, progress);

            float scale = 0.09f + Mathf.Sin(progress * Mathf.PI) * 0.055f;
            energy.transform.localScale = Vector3.one * scale;

            yield return null;
        }

        energy.transform.position = to;
        meshRenderer.enabled = false;
        Destroy(energy, trail.time + 0.05f);
    }

    private void PlaySuccessEffect(Vector3 position)
    {
        if (successEffectPrefab != null)
        {
            ParticleSystem effect = Instantiate(
                successEffectPrefab,
                position,
                Quaternion.identity
            );

            GameObject effectObject = effect.gameObject;
            transientEffects.Add(effectObject);
            effectObject.SetActive(true);

            ParticleSystem[] particleSystems =
                effectObject.GetComponentsInChildren<ParticleSystem>(true);

            foreach (ParticleSystem particleSystem in particleSystems)
            {
                particleSystem.gameObject.SetActive(true);
                particleSystem.Clear(true);
                particleSystem.Play(true);
            }

            Destroy(effectObject, 5f);
            return;
        }

        ParticleSystem particles = CreateSuccessParticles(position);
        StartCoroutine(CreateSuccessRing(position, 0f, glowColor));
        StartCoroutine(CreateSuccessRing(position, 0.18f, new Color(1f, 0.72f, 0.15f, 1f)));
        StartCoroutine(SuccessLightPulse(position));

        particles.Play();
        Destroy(particles.gameObject, 3.5f);
    }

    private ParticleSystem CreateSuccessParticles(Vector3 position)
    {
        GameObject effectObject = new GameObject("Light Brush Success Effect");
        transientEffects.Add(effectObject);
        effectObject.transform.position = position;

        ParticleSystem particles = effectObject.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.duration = 1.25f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.65f, 1.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.045f, 0.14f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(0.1f, 0.9f, 1f, 1f),
            new Color(1f, 0.68f, 0.12f, 1f)
        );
        main.gravityModifier = -0.08f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 180;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 80),
            new ParticleSystem.Burst(0.18f, 45)
        });

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f;

        ParticleSystem.ColorOverLifetimeModule colorLifetime =
            particles.colorOverLifetime;
        colorLifetime.enabled = true;
        colorLifetime.color = CreateParticleFadeGradient();

        ParticleSystem.SizeOverLifetimeModule sizeLifetime =
            particles.sizeOverLifetime;
        sizeLifetime.enabled = true;
        sizeLifetime.size = new ParticleSystem.MinMaxCurve(
            1f,
            AnimationCurve.EaseInOut(0f, 0.25f, 0.35f, 1f)
        );

        ParticleSystem.TrailModule trails = particles.trails;
        trails.enabled = true;
        trails.ratio = 0.38f;
        trails.lifetime = 0.22f;
        trails.dieWithParticles = true;

        ParticleSystemRenderer renderer =
            particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = particleMaterial;
        renderer.trailMaterial = coreMaterial;
        renderer.sortingOrder = 20;

        return particles;
    }

    private System.Collections.IEnumerator CreateSuccessRing(
        Vector3 position,
        float delay,
        Color color)
    {
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        GameObject ringObject = new GameObject("Success Light Ring");
        transientEffects.Add(ringObject);
        ringObject.transform.position = position;
        ringObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        LineRenderer ring = ringObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = 64;
        ring.alignment = LineAlignment.View;
        ring.numCornerVertices = 6;
        ring.widthMultiplier = 0.055f;
        ring.sharedMaterial = particleMaterial;
        ring.sortingOrder = 19;

        for (int i = 0; i < ring.positionCount; i++)
        {
            float angle = i / (float)ring.positionCount * Mathf.PI * 2f;
            ring.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f));
        }

        float duration = 0.85f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float radius = Mathf.Lerp(0.12f, 2.15f, 1f - Mathf.Pow(1f - progress, 2f));

            ringObject.transform.localScale = Vector3.one * radius;
            ring.colorGradient = CreateFadingGradient(color, 1f - progress);

            yield return null;
        }

        Destroy(ringObject);
    }

    private System.Collections.IEnumerator SuccessLightPulse(Vector3 position)
    {
        GameObject lightObject = new GameObject("Success Light");
        transientEffects.Add(lightObject);
        lightObject.transform.position = position;

        Light successLight = lightObject.AddComponent<Light>();
        successLight.type = LightType.Point;
        successLight.color = new Color(0.15f, 0.9f, 1f);
        successLight.range = 5f;

        float duration = 1.1f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            successLight.intensity = Mathf.Sin(progress * Mathf.PI) * 7f;

            yield return null;
        }

        Destroy(lightObject);
    }

    private Vector3 CalculateSuccessPosition()
    {
        bool hasBounds = false;
        Bounds combinedBounds = new Bounds();

        foreach (Transform letter in ActiveWords)
        {
            if (letter == null)
            {
                continue;
            }

            Collider letterCollider = letter.GetComponentInChildren<Collider>();
            Bounds currentBounds = letterCollider != null
                ? letterCollider.bounds
                : new Bounds(letter.position, Vector3.one * 0.2f);

            if (!hasBounds)
            {
                combinedBounds = currentBounds;
                hasBounds = true;
            }
            else
            {
                combinedBounds.Encapsulate(currentBounds);
            }
        }

        if (!hasBounds)
        {
            return transform.position + Vector3.up * successEffectHeight;
        }

        return new Vector3(
            combinedBounds.center.x,
            combinedBounds.max.y + successEffectHeight,
            combinedBounds.center.z
        );
    }

    private void PlayMoveSound(float volumeMultiplier)
    {
        if (lightMoveSound != null)
        {
            audioSource.PlayOneShot(
                lightMoveSound,
                moveVolume * volumeMultiplier
            );
        }
    }

    private void ResetBrush()
    {
        drawing = false;
        currentLetter = 0;
        ClearUnfinishedSegments();
    }

    private void ClearUnfinishedSegments()
    {
        for (int i = lightSegments.Count - 1; i >= 0; i--)
        {
            LightSegment segment = lightSegments[i];
            if (solvedPuzzles != null && solvedPuzzles[segment.PuzzleIndex]) continue;
            if (segment.Root != null) Destroy(segment.Root);
            lightSegments.RemoveAt(i);
        }
    }

    private void ClearSegments()
    {
        foreach (LightSegment segment in lightSegments)
        {
            if (segment.Root != null)
            {
                Destroy(segment.Root);
            }
        }

        lightSegments.Clear();
    }

    private Gradient CreateGlowGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(glowColor, 0f),
                new GradientColorKey(new Color(0.2f, 0.95f, 1f), 0.5f),
                new GradientColorKey(glowColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.25f, 0f),
                new GradientAlphaKey(0.82f, 0.5f),
                new GradientAlphaKey(0.25f, 1f)
            }
        );

        return gradient;
    }

    private Gradient CreateCoreGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(coreColor, 0f),
                new GradientColorKey(Color.white, 0.5f),
                new GradientColorKey(coreColor, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.78f, 0f),
                new GradientAlphaKey(1f, 0.5f),
                new GradientAlphaKey(0.78f, 1f)
            }
        );

        return gradient;
    }

    private Gradient CreateParticleFadeGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(0.3f, 0.95f, 1f), 0.55f),
                new GradientColorKey(new Color(1f, 0.65f, 0.1f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.12f),
                new GradientAlphaKey(0.85f, 0.65f),
                new GradientAlphaKey(0f, 1f)
            }
        );

        return gradient;
    }

    private Gradient CreateFadingGradient(Color color, float alpha)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(color, 0f),
                new GradientColorKey(Color.white, 0.5f),
                new GradientColorKey(color, 1f)
            },
            new[]
            {
                new GradientAlphaKey(alpha * 0.15f, 0f),
                new GradientAlphaKey(alpha, 0.5f),
                new GradientAlphaKey(alpha * 0.15f, 1f)
            }
        );

        return gradient;
    }

    private Material CreateEffectMaterial(Material source, Color color)
    {
        Material material;

        if (source != null)
        {
            material = new Material(source);
        }
        else
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            material = new Material(shader);
        }

        material.name = "Runtime Light Brush Material";

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 2f);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 2f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)BlendMode.One);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        if (material.HasProperty("_ZTest"))
        {
            material.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        material.renderQueue = 3000;
        return material;
    }

    private AudioClip CreateMoveSound()
    {
        const int sampleRate = 44100;
        const float duration = 0.42f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float time = i / (float)sampleRate;
            float progress = time / duration;
            float frequency = Mathf.Lerp(430f, 1180f, progress * progress);
            float phase = Mathf.PI * 2f * frequency * time;
            float envelope = Mathf.Sin(progress * Mathf.PI);
            float shimmer = Mathf.Sin(phase * 2.01f) * 0.22f;

            samples[i] = (Mathf.Sin(phase) + shimmer)
                         * envelope * 0.22f;
        }

        AudioClip clip = AudioClip.Create(
            "Generated Light Move",
            sampleCount,
            1,
            sampleRate,
            false
        );

        clip.SetData(samples, 0);
        return clip;
    }

    private AudioClip CreateSuccessSound()
    {
        const int sampleRate = 44100;
        const float duration = 1.45f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };

        for (int i = 0; i < sampleCount; i++)
        {
            float time = i / (float)sampleRate;
            float value = 0f;

            for (int noteIndex = 0; noteIndex < notes.Length; noteIndex++)
            {
                float noteStart = noteIndex * 0.16f;
                if (time < noteStart)
                {
                    continue;
                }

                float localTime = time - noteStart;
                float decay = Mathf.Exp(-3.8f * localTime);
                float fundamental = Mathf.Sin(
                    Mathf.PI * 2f * notes[noteIndex] * localTime
                );
                float bell = Mathf.Sin(
                    Mathf.PI * 2f * notes[noteIndex] * 2.01f * localTime
                ) * 0.32f;

                value += (fundamental + bell) * decay;
            }

            float masterFade = Mathf.Clamp01((duration - time) / 0.35f);
            samples[i] = Mathf.Clamp(value * 0.13f * masterFade, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create(
            "Generated Light Success",
            sampleCount,
            1,
            sampleRate,
            false
        );

        clip.SetData(samples, 0);
        return clip;
    }

    private bool ReadPointer(
        out Vector2 position,
        out bool pressed,
        out bool held,
        out bool released)
    {
        if (Touchscreen.current != null)
        {
            var touch = Touchscreen.current.primaryTouch;

            if (touch.press.isPressed ||
                touch.press.wasPressedThisFrame ||
                touch.press.wasReleasedThisFrame)
            {
                position = touch.position.ReadValue();
                pressed = touch.press.wasPressedThisFrame;
                held = touch.press.isPressed;
                released = touch.press.wasReleasedThisFrame;
                return true;
            }
        }

        if (Mouse.current != null)
        {
            position = Mouse.current.position.ReadValue();
            pressed = Mouse.current.leftButton.wasPressedThisFrame;
            held = Mouse.current.leftButton.isPressed;
            released = Mouse.current.leftButton.wasReleasedThisFrame;
            return pressed || held || released;
        }

        position = default;
        pressed = false;
        held = false;
        released = false;
        return false;
    }

    private void OnDestroy()
    {
        ReleaseMovementBlock();
        ClearTransientEffects();
        Destroy(glowMaterial);
        Destroy(coreMaterial);
        Destroy(energyMaterial);
        Destroy(particleMaterial);
    }
}