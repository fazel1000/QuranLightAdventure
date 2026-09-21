using System;
using System.Collections;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed class JourneyVerseVoicePanel : MonoBehaviour
{
    [Header("Shared services (assigned by setup menu)")]
    [SerializeField] private QuranAssessmentEngine assessmentEngine;
    [SerializeField] private QuranJsonRepository repository;
    [SerializeField] private JourneyVoiceRecorder recorder;
    [SerializeField] private AudioSource verseAudioSource;

    [Header("Verse - JSON numbering includes Bismillah")]
    [SerializeField, Range(1, 114)] private int surahNumber = 112;
    [Tooltip("For Ikhlas: 1 = Bismillah, 2 = Qul huwa Allahu ahad, ... 5 = last verse.")]
    [SerializeField, Min(1)] private int verseNumber = 2;
    [SerializeField] private AudioClip verseAudio;

    [Header("UI - assign two Buttons and an RTL TMP text")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button recordButton;
    [SerializeField] private TMP_Text percentText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text verseText;
    [SerializeField] private Image recordIcon;
    [SerializeField] private Sprite idleRecordSprite;
    [SerializeField] private Sprite stopRecordSprite;
    [SerializeField] private bool usePersianDigits = true;

    [Header("Percent Text font sizes")]
    [SerializeField, Min(1f)] private float statusFontSize = 24f;
    [SerializeField, Min(1f)] private float percentFontSize = 48f;

    [Header("Optional background music to pause during listening / recording")]
    [SerializeField] private AudioSource backgroundMusic;

    [Header("Result - only fired after successful assessment")]
    [SerializeField] private UnityEvent<float> onScoreReady = new UnityEvent<float>();
    public float LastScore { get; private set; }
    private bool preparing, assessing, requestingPermission, ready, musicPaused;
    private int generation;
    private Task<string> preparation;

    public JourneyVoiceBoard CurrentBoard { get; private set; }
    public bool IsPlaying => verseAudioSource != null && verseAudioSource.isPlaying;
    public bool IsRecording => recorder != null && recorder.IsRecording;
    public bool IsRecordPending => requestingPermission;
    public bool IsAssessing => assessing;
    public bool IsReady => ready && !preparing;
    public event Action<float> ScoreEvaluated;
    private float recordingPressDelay;
    private Coroutine recordingRequest;

    // Preserve compatibility with the earlier one-verse assets.
    public bool SelectBoard(JourneyVoiceBoard board, JourneyVerseDefinition definition,
        TMP_Text verseLabel, TMP_Text percentLabel, TMP_Text statusLabel, float pressDelay)
    {
        return definition != null && SelectBoard(board, definition.surahNumber,
            definition.verseNumber, definition.verseAudio, definition.displayText,
            verseLabel, percentLabel, statusLabel, pressDelay);
    }

    public bool SelectBoard(JourneyVoiceBoard board, int selectedSurah, int selectedVerse,
        AudioClip selectedAudio, string displayOverride, TMP_Text verseLabel,
        TMP_Text percentLabel, TMP_Text statusLabel, float pressDelay)
    {
        if (!isActiveAndEnabled || board == null) return false;
        if (assessing || requestingPermission || IsRecording) return CurrentBoard == board;
        bool changed = CurrentBoard != board || surahNumber != selectedSurah || verseNumber != selectedVerse;
        if (changed && verseAudioSource != null) verseAudioSource.Stop();
        CurrentBoard = board;
        surahNumber = selectedSurah;
        verseNumber = selectedVerse;
        verseAudio = selectedAudio;
        verseText = verseLabel;
        percentText = percentLabel;
        statusText = statusLabel;
        recordingPressDelay = Mathf.Max(0f, pressDelay);
        if (verseText != null) verseText.text = GetDisplayText(selectedSurah, selectedVerse, displayOverride);
        if (changed) SetPercent(null);
        return true;
    }

    public string GetDisplayText(JourneyVerseDefinition definition)
    {
        return definition == null ? string.Empty : GetDisplayText(definition.surahNumber,
            definition.verseNumber, definition.displayText);
    }

    public string GetDisplayText(int selectedSurah, int selectedVerse, string displayOverride)
    {
        if (!string.IsNullOrEmpty(displayOverride)) return displayOverride;
        return repository != null && repository.IsReady
            ? repository.GetVerseDisplayText(selectedSurah, selectedVerse) : string.Empty;
    }

    public void CancelBoard(JourneyVoiceBoard board)
    {
        if (CurrentBoard != board) return;
        if (recordingRequest != null) StopCoroutine(recordingRequest);
        recordingRequest = null;
        requestingPermission = false;
        if (recorder != null) recorder.Cancel();
        if (verseAudioSource != null) verseAudioSource.Stop();
        // Invalidate an assessment result without interrupting model preparation.
        if (assessing) generation++;
        ResumeMusic();
        SetStatus("ضبط لغو شد");
    }

    private void OnEnable()
    {
        ready = false;
        preparing = true;
        if (playButton != null) playButton.onClick.AddListener(PlayVerse);
        if (recordButton != null) recordButton.onClick.AddListener(ToggleRecording);
        if (recorder != null) recorder.RecordingFinished += HandleRecording;
        SetPercent(null);
        RefreshButtons();
        StartCoroutine(PrepareAfterAwake());
    }

    private IEnumerator PrepareAfterAwake()
    {
        yield return null;
        Prepare();
    }

    private async void Prepare()
    {
        int version = generation;
        preparing = true;
        SetStatus("در حال آماده شدن…");
        try
        {
            if (assessmentEngine == null || repository == null || recorder == null || verseAudioSource == null)
                throw new InvalidOperationException("Assign the shared voice services using the setup menu.");
            if (preparation == null) preparation = assessmentEngine.EnsureReadyAsync();
            string error = await preparation;
            if (this == null || version != generation || !isActiveAndEnabled) return;
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            QuranVerseJsonData verse;
            if (!repository.TryGetVerse(surahNumber, verseNumber, out verse))
                throw new InvalidOperationException("Selected verse is missing from Quran JSON.");
            if (CurrentBoard == null && verseText != null) verseText.text = repository.GetVerseDisplayText(surahNumber, verseNumber);
            ready = true;
            SetStatus("آمادهٔ ضبط");
        }
        catch (Exception ex)
        {
            if (this != null && version == generation)
            {
                ready = false;
                SetStatus("آماده‌سازی انجام نشد؛ دوباره امتحان کن");
                Debug.LogException(ex, this);
                preparation = null;
            }
        }
        finally
        {
            if (this != null && version == generation) { preparing = false; RefreshButtons(); }
        }
    }

    public void RetryPreparation() { if (!preparing && !assessing) Prepare(); }

    public void PlayVerse()
    {
        if (!isActiveAndEnabled || assessing || requestingPermission ||
            (recorder != null && recorder.IsRecording) || verseAudioSource == null || verseAudio == null) return;
        PauseMusic();
        verseAudioSource.Stop();
        verseAudioSource.clip = verseAudio;
        verseAudioSource.Play();
        SetStatus("گوش کن و سپس بخوان");
    }

    public void ToggleRecording()
    {
        if (!isActiveAndEnabled || !ready || preparing || assessing || requestingPermission || recorder == null) return;
        if (recorder.IsRecording) { recorder.Finish(); return; }
        recordingRequest = StartCoroutine(RequestAndRecord());
    }

    private IEnumerator RequestAndRecord()
    {
        requestingPermission = true;
        RefreshButtons();
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            bool answered = false;
            callbacks.PermissionGranted += _ => answered = true;
            callbacks.PermissionDenied += _ => answered = true;
            callbacks.PermissionDeniedAndDontAskAgain += _ => answered = true;
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
            float deadline = Time.realtimeSinceStartup + 60f;
            while (!answered && Time.realtimeSinceStartup < deadline) yield return null;
        }
        bool permitted = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#elif UNITY_IOS || UNITY_WEBGL
        yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        bool permitted = Application.HasUserAuthorization(UserAuthorization.Microphone);
#else
        bool permitted = true;
        yield return null;
#endif
        if (!permitted) requestingPermission = false;
        if (!permitted) { SetStatus("اجازهٔ دسترسی به میکروفون لازم است"); RefreshButtons(); yield break; }
        if (verseAudioSource != null) verseAudioSource.Stop();
        PauseMusic();
        if (recordingPressDelay > 0f) yield return new WaitForSecondsRealtime(recordingPressDelay);
        requestingPermission = false;
        recordingRequest = null;
        try
        {
            if (recorder.Begin()) { SetPercent(null); SetStatus("در حال ضبط… برای پایان دوباره بزن"); }
            else { SetStatus("میکروفون در دسترس نیست"); ResumeMusic(); }
        }
        catch (Exception ex) { SetStatus("ضبط شروع نشد"); ResumeMusic(); Debug.LogException(ex, this); }
        RefreshButtons();
    }

    private async void HandleRecording(AudioClip clip)
    {
        int version = generation;
        if (!isActiveAndEnabled || assessing) { if (clip != null) Destroy(clip); return; }
        if (clip == null) { SetStatus("صدایی ضبط نشد؛ دوباره تلاش کن"); RefreshButtons(); ResumeMusic(); return; }
        int selectedSurah = surahNumber, selectedVerse = verseNumber;
        assessing = true;
        SetStatus("در حال بررسی…");
        RefreshButtons();
        try
        {
            QuranAssessmentResult result = await assessmentEngine.AssessAsync(selectedSurah, selectedVerse, clip);
            if (this == null || version != generation || !isActiveAndEnabled) return;
            if (!result.success) throw new InvalidOperationException(result.error);
            LastScore = result.score;
            SetPercent(LastScore);
            SetStatus("بررسی انجام شد");
            ScoreEvaluated?.Invoke(LastScore);
            onScoreReady.Invoke(LastScore);
        }
        catch (Exception ex)
        {
            if (this != null && version == generation)
            {
                SetPercent(null);
                SetStatus("بررسی انجام نشد؛ دوباره تلاش کن");
                Debug.LogException(ex, this);
            }
        }
        finally
        {
            if (clip != null) Destroy(clip);
            if (this != null)
            {
                assessing = false;
                if (isActiveAndEnabled) RefreshButtons();
                ResumeMusic();
            }
        }
    }

    private void Update()
    {
        RefreshButtons();
        if (!assessing && !requestingPermission && (recorder == null || !recorder.IsRecording) &&
            (verseAudioSource == null || !verseAudioSource.isPlaying)) ResumeMusic();
    }

    private void RefreshButtons()
    {
        bool recording = recorder != null && recorder.IsRecording;
        if (playButton != null) playButton.interactable = !assessing && !requestingPermission && !recording && verseAudio != null;
        if (recordButton != null) recordButton.interactable = ready && !preparing && !assessing && !requestingPermission;
        if (recordIcon != null)
        {
            Sprite icon = recording ? stopRecordSprite : idleRecordSprite;
            if (icon != null) recordIcon.sprite = icon;
        }
    }
    private void SetStatus(string text)
    {
        if (statusText != null && statusText != percentText) statusText.text = text;
        else if (percentText != null && text != "بررسی انجام شد")
        {
            ApplyPercentTextSize(statusFontSize);
            percentText.text = text;
        }
    }
    private void SetPercent(float? score)
    {
        if (percentText == null) return;
        string value = score.HasValue ? Mathf.FloorToInt(Mathf.Clamp(score.Value, 0, 100)).ToString() + "٪" : "—";
        if (usePersianDigits)
            for (int i = 0; i < 10; i++) value = value.Replace((char)('0' + i), (char)('۰' + i));
        ApplyPercentTextSize(score.HasValue ? percentFontSize : statusFontSize);
        percentText.text = value;
    }
    private void ApplyPercentTextSize(float size)
    {
        if (percentText == null) return;
        percentText.enableAutoSizing = false;
        percentText.fontSize = Mathf.Max(1f, size);
    }
    private void PauseMusic()
    {
        if (backgroundMusic != null && backgroundMusic.isPlaying) { backgroundMusic.Pause(); musicPaused = true; }
    }
    private void ResumeMusic()
    {
        if (musicPaused && backgroundMusic != null) backgroundMusic.UnPause();
        musicPaused = false;
    }
    private void OnApplicationPause(bool paused)
    {
        if (paused && recorder != null && recorder.IsRecording)
        { recorder.Cancel(); SetStatus("ضبط لغو شد"); RefreshButtons(); }
    }
    private void OnDisable()
    {
        generation++;
        StopAllCoroutines();
        requestingPermission = preparing = false;
        if (playButton != null) playButton.onClick.RemoveListener(PlayVerse);
        if (recordButton != null) recordButton.onClick.RemoveListener(ToggleRecording);
        if (recorder != null) { recorder.RecordingFinished -= HandleRecording; recorder.Cancel(); }
        if (verseAudioSource != null) verseAudioSource.Stop();
        ResumeMusic();
    }
}
