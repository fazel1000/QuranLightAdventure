using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class JourneyRainCycle : MonoBehaviour
{
    public JourneyMenuController menu;
    public GameObject rainEffectPrefab;
    public Transform followTarget;
    public Vector3 followOffset = new Vector3(0f, 25f, 0f);
    [Header("Seconds of active gameplay")]
    [Min(1f)] public float minimumDrySeconds = 300f, maximumDrySeconds = 600f;
    [Min(1f)] public float minimumRainSeconds = 120f, maximumRainSeconds = 180f;
    public Transform waterSurface;
    public bool hideUnderwater = true;
    public AudioSource rainSource;
    public AudioClip rainLoop;
    [Range(0f, 1f)] public float rainVolume = 0.4f;
    public bool IsRaining { get; private set; }
    public float SecondsRemaining => remaining;
    private GameObject instance;
    private ParticleSystem[] systems = Array.Empty<ParticleSystem>();
    private float remaining;
    private bool focused = true, appPaused, paused, submerged, draining;
    private bool Gameplay => focused && !appPaused && Time.timeScale > 0f && (menu == null || menu.IsGameplayActive);
    private void Start()
    {
        if (menu == null) menu = FindFirstObjectByType<JourneyMenuController>(FindObjectsInactive.Include);
        if (followTarget == null && Camera.main != null) followTarget = Camera.main.transform;
        ScheduleDry();
    }
    private float RandomDuration(float min, float max) => UnityEngine.Random.Range(Mathf.Max(1f, min), Mathf.Max(Mathf.Max(1f, min), max));
    private void ScheduleDry() { remaining = RandomDuration(minimumDrySeconds, maximumDrySeconds); }
    [ContextMenu("Start Rain Now (Play Mode)")]
    public void StartRainNow()
    {
        if (!Application.isPlaying) return;
        if (instance == null)
        {
            if (rainEffectPrefab == null) { Debug.LogWarning("Assign Rain Effect Prefab on JourneyRainCycle.", this); ScheduleDry(); return; }
            instance = Instantiate(rainEffectPrefab); instance.name = "Journey Rain (runtime)";
            systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0)
            {
                Debug.LogWarning("Rain Effect Prefab must contain a ParticleSystem.", this);
                Destroy(instance); instance = null; ScheduleDry(); return;
            }
            foreach (var particles in systems)
            {
                particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main; main.loop = true; main.stopAction = ParticleSystemStopAction.None;
            }
        }
        IsRaining = true; draining = false; remaining = RandomDuration(minimumRainSeconds, maximumRainSeconds);
        instance.SetActive(true); Follow(); submerged = IsUnderwater();
        foreach (var particles in systems) { var emission = particles.emission; emission.enabled = !submerged; particles.Play(false); }
        if (rainLoop != null)
        {
            if (rainSource == null) rainSource = gameObject.AddComponent<AudioSource>();
            rainSource.playOnAwake = false; rainSource.spatialBlend = 0f; rainSource.loop = true; rainSource.clip = rainLoop;
            rainSource.volume = submerged ? 0f : rainVolume; rainSource.Play();
        }
        paused = false; Pause(!Gameplay);
    }
    [ContextMenu("Stop Rain Now (Play Mode)")]
    public void StopRainNow()
    {
        if (!Application.isPlaying) return;
        IsRaining = false; draining = instance != null; ScheduleDry();
        foreach (var particles in systems)
        {
            if (particles == null) continue;
            // Keep draining systems from starting emission again when a pause
            // ends and Play resumes their remaining particles.
            var emission = particles.emission; emission.enabled = false;
            particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        }
        if (rainSource != null) rainSource.Stop();
    }
    private bool IsUnderwater() => hideUnderwater && waterSurface != null && followTarget != null && followTarget.position.y < waterSurface.position.y - 0.2f;
    private void Update()
    {
        Pause(!Gameplay);
        if (!Gameplay) return;
        remaining -= Time.deltaTime;
        if (remaining <= 0f) { if (IsRaining) StopRainNow(); else StartRainNow(); }
        if (IsRaining)
        {
            bool under = IsUnderwater();
            if (under != submerged)
            {
                submerged = under;
                foreach (var particles in systems)
                {
                    if (particles == null) continue;
                    if (under) particles.Clear(false);
                    var emission = particles.emission; emission.enabled = !under;
                }
            }
            if (rainSource != null) rainSource.volume = submerged ? 0f : rainVolume;
        }
        if (draining && instance != null)
        {
            bool alive = false; foreach (var particles in systems) if (particles != null && particles.IsAlive(false)) { alive = true; break; }
            if (!alive) { instance.SetActive(false); draining = false; }
        }
    }
    private void Pause(bool value)
    {
        if (paused == value) return;
        paused = value;
        foreach (var particles in systems)
            if (particles != null && particles.gameObject.activeInHierarchy)
            { if (value) particles.Pause(false); else if (particles.isPaused) particles.Play(false); }
        if (rainSource != null) { if (value) rainSource.Pause(); else rainSource.UnPause(); }
    }
    private void Follow() { if (instance != null && followTarget != null) instance.transform.position = followTarget.position + followOffset; }
    private void LateUpdate() { if (IsRaining && Gameplay) Follow(); }
    private void OnApplicationFocus(bool value) { focused = value; Pause(!Gameplay); }
    private void OnApplicationPause(bool value) { appPaused = value; Pause(!Gameplay); }
    private void OnDisable() { Pause(true); }
    private void OnDestroy() { if (instance != null) Destroy(instance); }
}
