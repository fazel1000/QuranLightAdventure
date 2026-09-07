
using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

// Keep this component on the always-active MenuManager, outside all panels.
[DisallowMultipleComponent]
public sealed class JourneyUIFeedback : MonoBehaviour
{
    public enum ButtonSound { Start, Settings, Exit, Level, Back }

    [Header("Two Shared Audio Sources")]
    [SerializeField] private AudioSource buttonAudioSource;
    [SerializeField] private AudioSource musicAudioSource;

    [Header("Button Sounds")]
    [SerializeField] private AudioClip startSound;
    [SerializeField] private AudioClip settingSound;
    [SerializeField] private AudioClip exitSound;
    [SerializeField] private AudioClip levelSound;
    [SerializeField] private AudioClip backSound;
    [SerializeField, Range(0f, 1f)] private float buttonVolume = 0.8f;

    [Header("Background Music")]
    [SerializeField] private AudioClip mainMenuAndSettingMusic;
    [SerializeField] private AudioClip levelsMusic;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 0.4f;
    [SerializeField, Min(0.01f)] private float musicFadeDuration = 0.25f;

    [Header("Button Animation and Haptics")]
    [SerializeField] private bool enableHaptics = true;
    [SerializeField, Range(0.8f, 1f)] private float pressedScale = 0.95f;
    [SerializeField, Min(0.01f)] private float pressDuration = 0.07f;
    [SerializeField, Min(0.01f)] private float releaseDuration = 0.12f;

    public bool IsBusy { get; private set; }
    private Tween buttonTween;
    private Tween musicTween;
    private Transform animatedTransform;
    private Vector3 restingScale;
    private AudioClip requestedMusic;
    private bool musicRequested;

    private void Awake()
    {
        PrepareSources();
    }

    private void PrepareSources()
    {
        // Assign two existing sources, or create exactly the missing sources.
        if (buttonAudioSource == null) buttonAudioSource = gameObject.AddComponent<AudioSource>();
        if (musicAudioSource == null || musicAudioSource == buttonAudioSource)
            musicAudioSource = gameObject.AddComponent<AudioSource>();

        buttonAudioSource.playOnAwake = false;
        buttonAudioSource.loop = false;
        buttonAudioSource.spatialBlend = 0f;
        buttonAudioSource.ignoreListenerPause = true;
        musicAudioSource.playOnAwake = false;
        musicAudioSource.loop = true;
        musicAudioSource.spatialBlend = 0f;
        musicAudioSource.ignoreListenerPause = true;
    }

    // Returns false while another accepted click is finishing.
    // Uses only unscaled timing so menus also work while gameplay is paused.
    public bool PlayButton(Button button, ButtonSound sound, Action onFinished)
    {
        if (IsBusy || !isActiveAndEnabled || button == null ||
            !button.isActiveAndEnabled || !button.IsInteractable()) return false;

        IsBusy = true;
        StartCoroutine(AnimateClick(button, sound, onFinished));
        return true;
    }

    private IEnumerator AnimateClick(Button button, ButtonSound sound, Action onFinished)
    {
        AudioClip clip = GetSound(sound);
        if (clip != null && buttonAudioSource != null)
            buttonAudioSource.PlayOneShot(clip, Mathf.Clamp01(buttonVolume));

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        if (enableHaptics)
            CandyCoded.HapticFeedback.HapticFeedback.LightFeedback();
#endif

        animatedTransform = button.transform;
        restingScale = animatedTransform.localScale;
        float down = Mathf.Max(0.01f, pressDuration);
        float up = Mathf.Max(0.01f, releaseDuration);

        buttonTween = animatedTransform.DOScale(restingScale * Mathf.Clamp(pressedScale, 0.8f, 1f), down)
            .SetEase(Ease.OutQuad).SetUpdate(true);
        yield return new WaitForSecondsRealtime(down);

        if (button == null || !button.isActiveAndEnabled)
        {
            CancelButtonAnimation();
            yield break;
        }

        if (buttonTween != null) buttonTween.Kill();
        buttonTween = animatedTransform.DOScale(restingScale, up)
            .SetEase(Ease.OutCubic).SetUpdate(true);
        yield return new WaitForSecondsRealtime(up);

        if (buttonTween != null) buttonTween.Kill();
        buttonTween = null;
        if (animatedTransform != null) animatedTransform.localScale = restingScale;
        animatedTransform = null;

        // Let the exit sound finish before quitting the application.
        if (sound == ButtonSound.Exit && clip != null && buttonAudioSource != null)
        {
            float duration = clip.length / Mathf.Max(0.01f, Mathf.Abs(buttonAudioSource.pitch));
            float remaining = duration - down - up;
            if (remaining > 0f) yield return new WaitForSecondsRealtime(remaining);
        }

        IsBusy = false;
        if (button != null && button.isActiveAndEnabled && button.IsInteractable())
            onFinished?.Invoke();
    }

    private AudioClip GetSound(ButtonSound sound)
    {
        switch (sound)
        {
            case ButtonSound.Start: return startSound;
            case ButtonSound.Settings: return settingSound;
            case ButtonSound.Exit: return exitSound;
            case ButtonSound.Level: return levelSound;
            case ButtonSound.Back: return backSound;
            default: return null;
        }
    }

    public void ShowMenuMusic(bool isLevelsPanel)
    {
        SwitchMusic(isLevelsPanel ? levelsMusic : mainMenuAndSettingMusic);
    }

    public void StopMenuMusic()
    {
        SwitchMusic(null);
    }

    private void SwitchMusic(AudioClip next)
    {
        if (musicAudioSource == null) return;
        // Main Menu and Settings share one uninterrupted track.
        if (musicRequested && requestedMusic == next) return;
        musicRequested = true;
        requestedMusic = next;
        if (musicTween != null) musicTween.Kill();

        float fade = Mathf.Max(0.01f, musicFadeDuration);
        Sequence transition = DOTween.Sequence().SetUpdate(true);
        if (musicAudioSource.isPlaying)
            transition.Append(DOTween.To(() => musicAudioSource.volume,
                value => musicAudioSource.volume = value, 0f, fade));

        transition.AppendCallback(() =>
        {
            if (musicAudioSource == null) return;
            musicAudioSource.Stop();
            musicAudioSource.clip = next;
            musicAudioSource.volume = 0f;
            if (next != null) musicAudioSource.Play();
        });

        if (next != null)
            transition.Append(DOTween.To(() => musicAudioSource.volume,
                value => musicAudioSource.volume = value, Mathf.Clamp01(musicVolume), fade));

        musicTween = transition;
    }

    private void CancelButtonAnimation()
    {
        if (buttonTween != null) buttonTween.Kill();
        buttonTween = null;
        if (animatedTransform != null) animatedTransform.localScale = restingScale;
        animatedTransform = null;
        IsBusy = false;
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        CancelButtonAnimation();
        if (musicTween != null) musicTween.Kill();
        musicTween = null;
        musicRequested = false;
        if (musicAudioSource != null) musicAudioSource.Stop();
    }
}