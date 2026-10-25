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
    private readonly HashSet<UnityEngine.Object> pauseOwners = new HashSet<UnityEngine.Object>();
    private readonly Dictionary<UnityEngine.Object, float> volumeMultipliers = new Dictionary<UnityEngine.Object, float>();
    private int currentIndex = -1;
    private const string VolumeKey = "JourneyOfLight.MusicVolume";
    private bool playlistBuilt, started, pausedByUs, appPaused;
    private bool focus = true;
    private void Awake()
    {
        if (musicSource == null) musicSource = GetComponent<AudioSource>();
        musicSource.playOnAwake = false; musicSource.loop = false; musicSource.spatialBlend = 0f;
        volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, volume));
        ApplyOutputVolume(); musicSource.Stop();
        if (voiceService == null) voiceService = FindFirstObjectByType<JourneyVerseVoicePanel>(FindObjectsInactive.Include);
        if (menu == null) menu = FindFirstObjectByType<JourneyMenuController>(FindObjectsInactive.Include);
        Refill();
        if (bag.Count > 0) { currentIndex = 0; musicSource.clip = bag[0]; }
        Changed?.Invoke();
    }
    private bool HasExternalPause
    {
        get { foreach (var owner in pauseOwners) if (owner != null) return true; return false; }
    }
    private bool IsBlocked => UserPaused || HasExternalPause || !focus || appPaused || (voiceService != null && voiceService.IsAudioBusy) ||
        (pauseInMenus && menu != null && !menu.IsGameplayActive && !(playInSettings && menu.IsSettingsOpen));
    private void Update()
    {
        if (musicSource == null) return;
        ApplyOutputVolume();
        if (IsBlocked)
        { if (!pausedByUs || musicSource.isPlaying) { musicSource.Pause(); pausedByUs = true; } return; }
        if (pausedByUs) { musicSource.UnPause(); pausedByUs = false; }
        if (!started) PlayCurrent();
        else if (!musicSource.isPlaying) ChangeTrack(1);
    }
    public void SetVolume(float value)
    {
        volume = Mathf.Clamp01(value);
        ApplyOutputVolume();
        PlayerPrefs.SetFloat(VolumeKey, volume);
    }
    public void SaveVolume() => PlayerPrefs.Save();
    public void SetExternalPause(UnityEngine.Object owner, bool paused)
    {
        if (ReferenceEquals(owner, null)) return;
        if (paused && owner != null)
        {
            pauseOwners.Add(owner);
            if (musicSource != null) { musicSource.Pause(); pausedByUs = true; }
        }
        else pauseOwners.Remove(owner);
    }
    public void SetVolumeMultiplier(UnityEngine.Object owner, float multiplier)
    {
        if (owner == null) return;
        multiplier = Mathf.Clamp01(multiplier);
        if (multiplier >= 1f) volumeMultipliers.Remove(owner);
        else volumeMultipliers[owner] = multiplier;
        ApplyOutputVolume();
    }
    public void ClearVolumeMultiplier(UnityEngine.Object owner)
    {
        if (ReferenceEquals(owner, null)) return;
        volumeMultipliers.Remove(owner); ApplyOutputVolume();
    }
    private void ApplyOutputVolume()
    {
        if (musicSource == null) return;
        float multiplier = 1f;
        foreach (var request in volumeMultipliers)
            if (request.Key != null) multiplier = Mathf.Min(multiplier, request.Value);
        musicSource.volume = volume * multiplier;
    }
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
        musicSource.Stop(); musicSource.clip = bag[currentIndex]; ApplyOutputVolume();
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
