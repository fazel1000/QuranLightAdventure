using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
public sealed class JourneyShuffleMusic : MonoBehaviour
{
    [Header("Ten tracks - all other tracks play before a repeat")]
    public AudioClip track01, track02, track03, track04, track05;
    public AudioClip track06, track07, track08, track09, track10;
    public AudioSource musicSource;
    public JourneyVerseVoicePanel voiceService;
    public JourneyMenuController menu;
    public bool pauseInMenus = true;
    public bool playInSettings = true;
    [Range(0f, 1f)] public float volume = 0.35f;
    public event Action Changed;
    public bool UserPaused { get; private set; }
    public AudioClip CurrentTrack => musicSource != null ? musicSource.clip : null;
    public string CurrentTrackName => CurrentTrack != null ? CurrentTrack.name : "—";
    private readonly List<AudioClip> bag = new List<AudioClip>();
    private int currentIndex = -1;
    private const string VolumeKey = "JourneyOfLight.MusicVolume";
    private bool playlistBuilt, started, pausedByUs, appPaused;
    private bool focus = true;
    private void Awake()
    {
        if (musicSource == null) musicSource = GetComponent<AudioSource>();
        musicSource.playOnAwake = false; musicSource.loop = false; musicSource.spatialBlend = 0f;
        volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, volume));
        musicSource.volume = volume; musicSource.Stop();
        if (voiceService == null) voiceService = FindFirstObjectByType<JourneyVerseVoicePanel>(FindObjectsInactive.Include);
        if (menu == null) menu = FindFirstObjectByType<JourneyMenuController>(FindObjectsInactive.Include);
        Refill();
        if (bag.Count > 0) { currentIndex = 0; musicSource.clip = bag[0]; }
        Changed?.Invoke();
    }
    private bool IsBlocked => UserPaused || !focus || appPaused || (voiceService != null && voiceService.IsAudioBusy) ||
        (pauseInMenus && menu != null && !menu.IsGameplayActive && !(playInSettings && menu.IsSettingsOpen));
    private void Update()
    {
        if (musicSource == null) return;
        if (IsBlocked)
        { if (!pausedByUs || musicSource.isPlaying) { musicSource.Pause(); pausedByUs = true; } return; }
        if (pausedByUs) { musicSource.UnPause(); pausedByUs = false; }
        if (!started) PlayCurrent();
        else if (!musicSource.isPlaying) ChangeTrack(1);
    }
    public void SetVolume(float value)
    {
        volume = Mathf.Clamp01(value);
        if (musicSource != null) musicSource.volume = volume;
        PlayerPrefs.SetFloat(VolumeKey, volume);
    }
    public void SaveVolume() => PlayerPrefs.Save();
    public void TogglePause() => SetUserPaused(!UserPaused);
    public void SetUserPaused(bool value)
    {
        if (UserPaused == value) return;
        UserPaused = value;
        if (value && musicSource != null) { musicSource.Pause(); pausedByUs = true; }
        Changed?.Invoke();
    }
    public void NextTrack() => ChangeTrack(1);
    public void PreviousTrack() => ChangeTrack(-1);
    private void ChangeTrack(int direction)
    {
        if (!playlistBuilt) Refill();
        if (bag.Count == 0 || musicSource == null) return;
        currentIndex = currentIndex < 0 ? (direction < 0 ? bag.Count - 1 : 0) : (currentIndex + direction + bag.Count) % bag.Count;
        PlayCurrent();
    }
    private void PlayCurrent()
    {
        if (musicSource == null || bag.Count == 0) return;
        if (currentIndex < 0) currentIndex = 0;
        musicSource.Stop(); musicSource.clip = bag[currentIndex]; musicSource.volume = volume;
        musicSource.Play(); started = true; pausedByUs = IsBlocked;
        if (pausedByUs) musicSource.Pause();
        Changed?.Invoke();
    }
    private void Refill()
    {
        bag.Clear(); currentIndex = -1;
        foreach (var clip in new[] { track01, track02, track03, track04, track05, track06, track07, track08, track09, track10 })
            if (clip != null && !bag.Contains(clip)) bag.Add(clip);
        for (int i = bag.Count - 1; i > 0; i--)
        { int j = UnityEngine.Random.Range(0, i + 1); var temp = bag[i]; bag[i] = bag[j]; bag[j] = temp; }
        playlistBuilt = true;
    }
    private void OnApplicationFocus(bool value)
    { focus = value; if (!value && musicSource != null) { musicSource.Pause(); pausedByUs = true; } }
    private void OnApplicationPause(bool value)
    { appPaused = value; if (value && musicSource != null) { musicSource.Pause(); pausedByUs = true; } }
    private void OnDisable() { SaveVolume(); if (musicSource != null) { musicSource.Pause(); pausedByUs = true; } }
}
