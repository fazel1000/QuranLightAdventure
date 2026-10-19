using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class JourneyMusicSettingsUI : MonoBehaviour
{
    public JourneyShuffleMusic music;
    public Slider volumeSlider;
    public Button previousButton, nextButton;
    [Header("Pause and track name")]
    public Button pauseButton;
    public Image pauseImage;
    public Sprite playingSprite, pausedSprite;
    public TMP_Text trackNameText;
    private void OnEnable()
    {
        if (music == null) music = FindFirstObjectByType<JourneyShuffleMusic>(FindObjectsInactive.Include);
        if (music == null) { Debug.LogWarning("Assign Journey Music to Music Settings UI.", this); return; }
        if (volumeSlider != null)
        {
            volumeSlider.minValue = 0f; volumeSlider.maxValue = 1f; volumeSlider.wholeNumbers = false;
            volumeSlider.SetValueWithoutNotify(music.volume); volumeSlider.onValueChanged.AddListener(ChangeVolume);
        }
        if (previousButton != null) previousButton.onClick.AddListener(Previous);
        if (nextButton != null) nextButton.onClick.AddListener(Next);
        if (pauseButton != null) pauseButton.onClick.AddListener(Pause);
        if (pauseImage == null && pauseButton != null) pauseImage = pauseButton.targetGraphic as Image ?? pauseButton.GetComponentInChildren<Image>(true);
        if (playingSprite == null && pauseImage != null) playingSprite = pauseImage.sprite;
        music.Changed += Refresh;
        JourneyLocalization.Changed += Refresh;
        Refresh();
    }
    private void Refresh()
    {
        if (music == null) return;
        if (pauseImage != null)
        {
            Sprite sprite = music.UserPaused && pausedSprite != null ? pausedSprite : playingSprite;
            if (sprite != null) pauseImage.sprite = sprite;
        }
        JourneyLocalizedText.Write(trackNameText, music.CurrentTrackName);
    }
    private void ChangeVolume(float value) { if (music != null) music.SetVolume(value); }
    private void Previous() { if (music != null) music.PreviousTrack(); }
    private void Next() { if (music != null) music.NextTrack(); }
    private void Pause() { if (music != null) music.TogglePause(); }
    private void OnDisable()
    {
        if (volumeSlider != null) volumeSlider.onValueChanged.RemoveListener(ChangeVolume);
        if (previousButton != null) previousButton.onClick.RemoveListener(Previous);
        if (nextButton != null) nextButton.onClick.RemoveListener(Next);
        if (pauseButton != null) pauseButton.onClick.RemoveListener(Pause);
        JourneyLocalization.Changed -= Refresh;
        if (music != null) { music.Changed -= Refresh; music.SaveVolume(); }
    }
}
