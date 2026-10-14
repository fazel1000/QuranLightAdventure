using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
public sealed class JourneyShuffleMusic : MonoBehaviour
{
    [Header("Ten tracks - shuffled once, then cycled so all other tracks play before a repeat")]
    public AudioClip track01, track02, track03, track04, track05;
    public AudioClip track06, track07, track08, track09, track10;
    public AudioSource musicSource;
    [Tooltip("Shared voice service: music pauses during verse playback and microphone recording.")]
    public JourneyVerseVoicePanel voiceService;
    public JourneyMenuController menu;
    public bool pauseInMenus = true;
    [Range(0f, 1f)] public float volume = 0.35f;
    private readonly List<AudioClip> bag = new List<AudioClip>();
    private int currentIndex = -1;
    private const string VolumeKey = "JourneyOfLight.MusicVolume";
    private bool playlistBuilt;
    private bool started, pausedByUs, focus = true, appPaused;
    private void Awake()
    {
        if (musicSource == null) musicSource = GetComponent<AudioSource>();
        musicSource.playOnAwake = false; musicSource.loop = false; musicSource.spatialBlend = 0f;
        volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, volume));
        musicSource.volume = volume; musicSource.Stop();
        if (voiceService == null) voiceService = FindFirstObjectByType<JourneyVerseVoicePanel>(FindObjectsInactive.Include);
        if (menu == null) menu = FindFirstObjectByType<JourneyMenuController>(FindObjectsInactive.Include);
    }
    private void Update()
    {
        if (musicSource == null) return;
        bool blocked = IsBlocked;
        if (blocked)
        {
            if (!pausedByUs) { musicSource.Pause(); pausedByUs = true; }
            return;
        }
        if (pausedByUs) { musicSource.UnPause(); pausedByUs = false; }
        if (!started || !musicSource.isPlaying) PlayNext();
    }
    private bool IsBlocked => !focus || appPaused || (voiceService != null && voiceService.IsAudioBusy) ||
        (pauseInMenus && menu != null && !menu.IsGameplayActive);
    public void SetVolume(float value)
    {
        volume = Mathf.Clamp01(value);
        if (musicSource != null) musicSource.volume = volume;
        PlayerPrefs.SetFloat(VolumeKey, volume);
    }
    public void SaveVolume() => PlayerPrefs.Save();
    public void NextTrack() => ChangeTrack(1);
    public void PreviousTrack() => ChangeTrack(-1);
    private void PlayNext() => ChangeTrack(1);
    private void ChangeTrack(int direction)
    {
        if (!playlistBuilt) Refill();
        if (bag.Count == 0 || musicSource == null) return;
        currentIndex = currentIndex < 0 ? (direction < 0 ? bag.Count - 1 : 0)
            : (currentIndex + direction + bag.Count) % bag.Count;
        musicSource.Stop();
        musicSource.clip = bag[currentIndex];
        musicSource.volume = volume;
        musicSource.Play();
        started = true;
        pausedByUs = IsBlocked;
        if (pausedByUs) musicSource.Pause();
    }
    private void Refill()
    {
        bag.Clear(); currentIndex = -1;
        foreach (var clip in new[] { track01, track02, track03, track04, track05, track06, track07, track08, track09, track10 })
            if (clip != null && !bag.Contains(clip)) bag.Add(clip);
        for (int i = bag.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            AudioClip temp = bag[i]; bag[i] = bag[j]; bag[j] = temp;
        }
        // Keep this permutation across rounds: re-shuffling every round would
        // allow a song to repeat before all nine others have played again.
        playlistBuilt = true;
    }
    private void OnApplicationFocus(bool value)
    { focus = value; if (!value && musicSource != null) { musicSource.Pause(); pausedByUs = true; } }
    private void OnApplicationPause(bool value)
    { appPaused = value; if (value && musicSource != null) { musicSource.Pause(); pausedByUs = true; } }
    private void OnDisable()
    {
        SaveVolume(); if (musicSource != null) { musicSource.Pause(); pausedByUs = true; } }
}
