using UnityEngine;
using UnityEngine.UI;

// Put this on the settings panel. Do not also wire the same events in OnClick.
[DisallowMultipleComponent]
public sealed class JourneyMusicSettingsUI : MonoBehaviour
{
    public JourneyShuffleMusic music;
    public Slider volumeSlider;
    public Button previousButton;
    public Button nextButton;
    private void OnEnable()
    {
        if (music == null) music = FindFirstObjectByType<JourneyShuffleMusic>(FindObjectsInactive.Include);
        if (music == null) { Debug.LogWarning("Assign Journey Music to Music Settings UI.", this); return; }
        if (volumeSlider != null)
        {
            volumeSlider.minValue = 0f; volumeSlider.maxValue = 1f; volumeSlider.wholeNumbers = false;
            volumeSlider.SetValueWithoutNotify(music.volume);
            volumeSlider.onValueChanged.AddListener(ChangeVolume);
        }
        if (previousButton != null) previousButton.onClick.AddListener(Previous);
        if (nextButton != null) nextButton.onClick.AddListener(Next);
    }
    private void ChangeVolume(float value) { if (music != null) music.SetVolume(value); }
    private void Previous() { if (music != null) music.PreviousTrack(); }
    private void Next() { if (music != null) music.NextTrack(); }
    private void OnDisable()
    {
        if (volumeSlider != null) volumeSlider.onValueChanged.RemoveListener(ChangeVolume);
        if (previousButton != null) previousButton.onClick.RemoveListener(Previous);
        if (nextButton != null) nextButton.onClick.RemoveListener(Next);
        if (music != null) music.SaveVolume();
    }
}
