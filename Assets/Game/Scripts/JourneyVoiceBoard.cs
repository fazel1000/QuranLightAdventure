using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DefaultExecutionOrder(-3000)]
[DisallowMultipleComponent]
public sealed class JourneyVoiceBoard : MonoBehaviour
{
    [Header("Verse asset and shared scene services")]
    [SerializeField] private JourneySurahDefinition surah;
    [SerializeField, Min(1), Tooltip("The verse number in this surah asset / Quran JSON. Bismillah is 1 for Ikhlas.")]
    private int verseNumber = 1;
    [SerializeField, HideInInspector] private JourneyVerseDefinition verse; // Preserve old scene assignments.
    [SerializeField] private JourneyVerseVoicePanel voiceService;
    [SerializeField] private LightBrushPuzzle scoreReceiver;
    [SerializeField] private JourneyMenuController menu;
    [SerializeField] private Camera interactionCamera;
    [Tooltip("Optional crystal lock and verse arrows. Leave empty on the existing ButtonsBoard boards.")]
    [SerializeField] private JourneyHamdBoard hamdBoard;

    [Header("Separate Blender button objects")]
    [SerializeField] private Collider playCollider;
    [SerializeField] private Transform playVisual;
    [SerializeField] private Collider recordCollider;
    [SerializeField] private Transform recordVisual;

    [Header("Text above the buttons")]
    [SerializeField] private TMP_Text verseText;
    [SerializeField] private TMP_Text percentText;
    [SerializeField] private TMP_Text statusText;

    [Header("Translation and recording unlock")]
    [SerializeField] private TMP_Text translateText;
    [SerializeField] private bool requireWordPuzzle = true;
    [Tooltip("0 maps Ikhlas JSON entries 1-5 automatically; for other surahs assign the correct puzzle number.")]
    [SerializeField, Min(0)] private int requiredPuzzleNumber;
    [TextArea] [SerializeField] private string persianTranslationOverride;
    [TextArea] [SerializeField] private string englishTranslationOverride;
    public bool IsActivated => hamdBoard == null || hamdBoard.IsActivated;
    public bool CanRecord => IsActivated && (!requireWordPuzzle || (scoreReceiver != null && scoreReceiver.IsPuzzleSolved(RequiredPuzzle)));
    public string RecordingLockedMessage => !IsActivated ?
        (hamdBoard.State == JourneyHamdBoard.CrystalState.Carried ? JourneyHamdBoard.CrystalCarriedMessage : JourneyHamdBoard.CrystalLockedMessage) : LockedMessage;
    public int VerseNumber => verseNumber;
    public bool CanChangeVerse => IsActivated && (voiceService == null || voiceService.CurrentBoard != this ||
        !(voiceService.IsRecording || voiceService.IsRecordPending || voiceService.IsAssessing));
    public bool HasPreviousVerse => AdjacentVerse(-1) > 0;
    public bool HasNextVerse => AdjacentVerse(1) > 0;
    private int RequiredPuzzle => requiredPuzzleNumber > 0 ? requiredPuzzleNumber :
        surah != null && surah.surahNumber == 112 ? verseNumber :
        verse != null && verse.surahNumber == 112 ? verse.verseNumber : 0;
    public const string LockedMessage = "اول جعبه‌های کلمات این آیه را پیدا کن و درست به هم وصل کن";
    private float? boardScore;
    private readonly Dictionary<int, float> verseScores = new Dictionary<int, float>();
    private string renderedScore;
    [Header("3D button colors (assign only button surface renderers)")]
    [SerializeField] private Renderer[] playRenderers;
    [SerializeField] private Renderer[] recordRenderers;
    [SerializeField] private Color playingColor = new Color(0.15f, 0.85f, 0.25f);
    [SerializeField] private Color recordingColor = new Color(1f, 0.1f, 0.12f);
    [SerializeField] private Color pendingColor = new Color(1f, 0.65f, 0.05f);
    [SerializeField] private Color lockedColor = new Color(0.35f, 0.35f, 0.35f);
    private JourneyBoardTint playTint, recordTint;

    [Header("Button movement in parent-local coordinates")]
    [SerializeField] private Vector3 pressedOffset = new Vector3(0.1f, 0f, 0f);
    [SerializeField, Min(0.01f)] private float moveDuration = 0.18f;
    [SerializeField] private Ease moveEase = Ease.OutQuad;
    [SerializeField, Min(0.1f)] private float maxTouchDistance = 100f;
    [SerializeField] private LayerMask interactionLayers = ~0;
    [SerializeField] private UnityEvent<int> onPointsAdded = new UnityEvent<int>();

    private static readonly HashSet<JourneyVoiceBoard> pointerOwners = new HashSet<JourneyVoiceBoard>();
    private readonly HashSet<int> capturedTouches = new HashSet<int>();
    private readonly HashSet<int> heldTouches = new HashSet<int>();
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private bool mouseCaptured, initialized, playPressed, recordPressed, menuWasOpen;
    private string lastVerseDisplay;
    private Vector3 playHome, recordHome;
    private Tween playTween, recordTween;
    public static bool IsPointerCaptured => pointerOwners.Count != 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { pointerOwners.Clear(); }

    private void Awake()
    {
        if (hamdBoard == null) hamdBoard = GetComponent<JourneyHamdBoard>();
        if (voiceService == null) voiceService = FindFirstObjectByType<JourneyVerseVoicePanel>(FindObjectsInactive.Include);
        if (scoreReceiver == null) scoreReceiver = FindFirstObjectByType<LightBrushPuzzle>(FindObjectsInactive.Include);
        if (menu == null) menu = FindFirstObjectByType<JourneyMenuController>(FindObjectsInactive.Include);
        if (interactionCamera == null) interactionCamera = Camera.main;
        if (playVisual == null && playCollider != null) playVisual = playCollider.transform;
        if (recordVisual == null && recordCollider != null) recordVisual = recordCollider.transform;
        if (playVisual != null) playHome = playVisual.localPosition;
        if (recordVisual != null) recordHome = recordVisual.localPosition;
        if (verseText != null) verseText.raycastTarget = false;
        if (percentText != null) { percentText.raycastTarget = false; percentText.text = "—"; }
        if (statusText != null) statusText.raycastTarget = false;
        if (translateText == null)
            foreach (var label in GetComponentsInChildren<TMP_Text>(true))
                if (label.name == "Translate Text") { translateText = label; break; }
        if (translateText != null) translateText.raycastTarget = false;
        playTint = new JourneyBoardTint(playRenderers);
        recordTint = new JourneyBoardTint(recordRenderers);
        initialized = true;
        RefreshTranslation();
    }

    private void OnEnable()
    {
        JourneyLocalization.Changed += RefreshStatusLanguage;
        RefreshStatusLanguage();
        JourneyLocalization.Changed += RefreshTranslation;
        RefreshTranslation();
        if (voiceService != null) voiceService.ScoreEvaluated += HandleScore;
    }

    private void Update()
    {
        if (voiceService == null) return;
        if (verseText != null)
        {
            string text = TryGetSelection(out int number, out int selectedVerse, out AudioClip audio, out string display)
                ? voiceService.GetDisplayText(number, selectedVerse, display) : string.Empty;
            if (!string.IsNullOrEmpty(text) && lastVerseDisplay != text) { JourneyLocalizedText.Write(verseText, text, true); lastVerseDisplay = text; }
        }
        if ((menu != null && menu.IsMenuOpen) || Time.timeScale <= 0f ||
            (hamdBoard != null && !hamdBoard.CanInteract))
        {
            if (!menuWasOpen) voiceService.CancelBoard(this);
            menuWasOpen = true;
            ReleasePointers();
        }
        else { menuWasOpen = false; ReadPresses(); }
        bool selected = voiceService.CurrentBoard == this;
        playTint?.Set(!IsActivated ? lockedColor : selected && voiceService.IsPlaying ? playingColor : (Color?)null);
        recordTint?.Set(!CanRecord ? lockedColor : selected && voiceService.IsRecording ? recordingColor :
            selected && (voiceService.IsRecordPending || voiceService.IsAssessing) ? pendingColor : (Color?)null);
        AnimateButton(true, selected && voiceService.IsPlaying);
        AnimateButton(false, selected && (voiceService.IsRecording || voiceService.IsRecordPending));
    }

    private void ReadPresses()
    {
        heldTouches.Clear();
        bool touchActive = false;
        if (Touchscreen.current != null)
        {
            foreach (var touch in Touchscreen.current.touches)
            {
                if (!touch.press.isPressed) continue;
                touchActive = true;
                int id = touch.touchId.ReadValue();
                heldTouches.Add(id);
                if (touch.press.wasPressedThisFrame && TryPress(touch.position.ReadValue())) capturedTouches.Add(id);
            }
        }
        capturedTouches.IntersectWith(heldTouches);
        if (touchActive || Mouse.current == null || !Mouse.current.leftButton.isPressed) mouseCaptured = false;
        else if (Mouse.current.leftButton.wasPressedThisFrame) mouseCaptured = TryPress(Mouse.current.position.ReadValue());
        bool blocked = mouseCaptured || capturedTouches.Count != 0;
        bool changed = blocked ? pointerOwners.Add(this) : pointerOwners.Remove(this);
        if (changed)
        {
            RightScreenDragArea.ClearInput();
            JourneyMovementSettings.RefreshPuzzleInput();
        }
    }

    private bool TryPress(Vector2 screenPosition)
    {
        if (interactionCamera == null || IsOverSelectableUI(screenPosition)) return false;
        Ray ray = interactionCamera.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxTouchDistance, interactionLayers, QueryTriggerInteraction.Collide)) return false;
        if (hamdBoard != null && hamdBoard.TryPress(hit.collider)) return true;
        bool play = hit.collider == playCollider;
        if (!play && hit.collider != recordCollider) return false;
        if (play) Play(); else Record();
        return true;
    }

    // Transparent drag surfaces must not intercept the physical board. Real UI
    // buttons/toggles/sliders retain priority; menus are gated separately above.
    private bool IsOverSelectableUI(Vector2 position)
    {
        if (EventSystem.current == null) return false;
        uiHits.Clear();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
        foreach (RaycastResult hit in uiHits)
            if (hit.module is GraphicRaycaster && hit.gameObject.GetComponentInParent<Selectable>() != null) return true;
        return false;
    }

    private bool TryGetSelection(out int selectedSurah, out int selectedVerse,
        out AudioClip audio, out string display)
    {
        selectedSurah = 0; selectedVerse = 0; audio = null; display = null;
        if (surah != null)
        {
            if (!surah.TryGetVerse(verseNumber, out JourneySurahDefinition.VerseEntry entry)) return false;
            selectedSurah = surah.surahNumber;
            selectedVerse = entry.verseNumber;
            audio = entry.verseAudio;
            display = entry.displayText;
            return true;
        }
        if (verse == null) return false;
        selectedSurah = verse.surahNumber;
        selectedVerse = verse.verseNumber;
        audio = verse.verseAudio;
        display = verse.displayText;
        return true;
    }

    private bool Select()
    {
        if (menu != null && menu.IsMenuOpen) return false;
        if (hamdBoard != null && !hamdBoard.InBoardRange) return false;
        if (!IsActivated) { ShowStatus(RecordingLockedMessage); return false; }
        if (!TryGetSelection(out int number, out int selectedVerse, out AudioClip audio, out string display))
        {
            ShowStatus("آیهٔ تابلو در فهرست سوره نیست یا شمارهٔ تکراری دارد");
            return false;
        }
        return voiceService != null && voiceService.SelectBoard(this, number, selectedVerse,
            audio, display, verseText, percentText, statusText, moveDuration);
    }
    public void Play()
    {
        if (!Select()) return;
        if (!TryGetSelection(out _, out _, out AudioClip audio, out _) || audio == null)
        { ShowStatus("صدای این آیه را در فهرست سوره قرار بده"); return; }
        voiceService.PlayVerse();
    }
    public void Record()
    {
        if (!Select()) return;
        if (!CanRecord) { ShowStatus(RecordingLockedMessage); return; }
        if (!voiceService.IsReady)
        {
            string status = voiceService.PreparationStatus;
            if (!voiceService.IsPreparing) voiceService.RetryPreparation();
            ShowStatus(status);
            return;
        }
        voiceService.ToggleRecording();
    }

    private void HandleScore(float percentage)
    {
        if (voiceService == null || voiceService.CurrentBoard != this) return;
        if (float.IsNaN(percentage) || float.IsInfinity(percentage)) return;
        lastStatusMessage = null;
        boardScore = percentage;
        verseScores[verseNumber] = percentage;
        RenderBoardScore();
        int points = Mathf.FloorToInt(Mathf.Clamp(percentage, 0f, 100f) / 10f);
        if (scoreReceiver != null) scoreReceiver.AddVoicePoints(points);
        else Debug.LogError("JourneyVoiceBoard: assign Score Receiver to LightBrushPuzzle to add game points.", this);
        // Notification only. Do not connect this back to AddVoicePoints (double award).
        onPointsAdded.Invoke(points);
    }
    private void RenderBoardScore()
    {
        if (!boardScore.HasValue || percentText == null) return;
        JourneyLocalization.WritePercent(percentText, boardScore.Value);
        renderedScore = JourneyLocalizedText.Original(percentText);
    }
    private void RefreshTranslation()
    {
        if (translateText != null)
        {
            string fa = persianTranslationOverride, en = englishTranslationOverride;
            if (surah != null && surah.TryGetVerse(verseNumber, out var entry))
            { if (string.IsNullOrEmpty(fa)) fa = entry.persianTranslation; if (string.IsNullOrEmpty(en)) en = entry.englishTranslation; }
            string value = JourneyLocalization.Language == 2 ? "" : JourneyLocalization.Language == 1 ? en : fa;
            JourneyLocalizedText.Write(translateText, value ?? "");
        }
        if (boardScore.HasValue && percentText != null && JourneyLocalizedText.Original(percentText) == renderedScore) RenderBoardScore();
    }
    private string lastStatusMessage;
    private string lastRenderedStatus;
    private void RefreshStatusLanguage()
    {
        if (!IsActivated) { ShowStatus(RecordingLockedMessage); return; }
        var target = statusText != null ? statusText : percentText;
        if (target != null && JourneyLocalizedText.Original(target) == lastRenderedStatus && !string.IsNullOrEmpty(lastStatusMessage))
            ShowStatus(lastStatusMessage);
    }
    private int AdjacentVerse(int direction)
    {
        if (surah == null || surah.verses == null || direction == 0) return 0;
        int best = 0;
        foreach (var entry in surah.verses)
        {
            if (entry == null || entry.verseNumber < 1 || !surah.TryGetVerse(entry.verseNumber, out _)) continue;
            int candidate = entry.verseNumber;
            if (direction > 0 && candidate > verseNumber && (best == 0 || candidate < best) ||
                direction < 0 && candidate < verseNumber && (best == 0 || candidate > best)) best = candidate;
        }
        return best;
    }
    public bool ChangeVerse(int direction)
    {
        if (!CanChangeVerse || (menu != null && menu.IsMenuOpen) ||
            (hamdBoard != null && !hamdBoard.InBoardRange)) return false;
        int next = AdjacentVerse(direction);
        if (next == 0) return false;
        if (voiceService != null) voiceService.DeselectBoard(this);
        verseNumber = next;
        boardScore = verseScores.TryGetValue(next, out float previousScore) ? previousScore : (float?)null;
        lastVerseDisplay = null;
        RefreshAvailability();
        RefreshTranslation();
        return true;
    }
    public void RefreshAvailability()
    {
        lastStatusMessage = null; lastRenderedStatus = null;
        if (!IsActivated) { ShowStatus(RecordingLockedMessage); return; }
        if (percentText != null) JourneyLocalizedText.Write(percentText, "—");
        if (boardScore.HasValue) RenderBoardScore();
    }
    private void ShowStatus(string message)
    {
        lastStatusMessage = message;
        message = JourneyLocalization.Translate(message);
        lastRenderedStatus = message;
        if (statusText != null) JourneyLocalizedText.Write(statusText, message);
        else if (percentText != null) JourneyLocalizedText.Write(percentText, message);
    }

    private void AnimateButton(bool play, bool pressed)
    {
        Transform visual = play ? playVisual : recordVisual;
        if (visual == null || (play ? playPressed : recordPressed) == pressed) return;
        Vector3 home = play ? playHome : recordHome;
        if (play)
        {
            playPressed = pressed;
            playTween?.Kill();
            playTween = visual.DOLocalMove(home + (pressed ? pressedOffset : Vector3.zero), moveDuration).SetEase(moveEase).SetUpdate(true);
        }
        else
        {
            recordPressed = pressed;
            recordTween?.Kill();
            recordTween = visual.DOLocalMove(home + (pressed ? pressedOffset : Vector3.zero), moveDuration).SetEase(moveEase).SetUpdate(true);
        }
    }
    private void ReleasePointers()
    {
        mouseCaptured = false; capturedTouches.Clear(); heldTouches.Clear();
        if (pointerOwners.Remove(this))
        {
            RightScreenDragArea.ClearInput();
            JourneyMovementSettings.RefreshPuzzleInput();
        }
    }
    private void OnApplicationPause(bool paused)
    {
        if (!paused) return;
        ReleasePointers();
        // Android's permission dialog also pauses the app: let its request finish.
        if (voiceService != null && !voiceService.IsRecordPending) voiceService.CancelBoard(this);
    }
    private void OnDisable()
    {
        JourneyLocalization.Changed -= RefreshStatusLanguage;
        JourneyLocalization.Changed -= RefreshTranslation;
        playTint?.Restore(); recordTint?.Restore();
        ReleasePointers();
        if (voiceService != null)
        {
            voiceService.ScoreEvaluated -= HandleScore;
            voiceService.CancelBoard(this);
        }
        playTween?.Kill(); recordTween?.Kill();
        if (initialized)
        {
            if (playVisual != null) playVisual.localPosition = playHome;
            if (recordVisual != null) recordVisual.localPosition = recordHome;
        }
        playPressed = recordPressed = false;
    }
}
