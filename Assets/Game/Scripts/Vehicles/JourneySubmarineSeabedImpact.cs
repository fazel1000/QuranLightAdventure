using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(JourneyAirWaterVehicle))]
public sealed class JourneySubmarineSeabedImpact : MonoBehaviour
{
    [Header("Seabed collision sound and sand")]
    public AudioClip seabedImpactClip;
    public AudioSource impactAudioSource;
    [Tooltip("A ParticleSystem prefab. Its colors, texture and shape are preserved.")]
    public GameObject sandImpactPrefab;
    public LayerMask seabedLayers = ~0;
    [Tooltip("An upward contact normal identifies the floor; vertical walls do not emit sand.")]
    [Range(0f, 1f)] public float minimumFloorNormalY = 0.35f;
    [Min(0.01f)] public float minimumImpactSpeed = 0.5f;
    [Min(0.1f)] public float fullIntensitySpeed = 8f;
    [Min(0.05f)] public float impactCooldown = 0.6f;
    [Header("Intensity: gentle contact to hard impact")]
    [Range(0f, 1f)] public float minimumVolume = 0.15f;
    [Range(0f, 1f)] public float maximumVolume = 1f;
    [Min(0f)] public float minimumSandAmount = 0.35f, maximumSandAmount = 2f;
    [Min(0.01f)] public float minimumSandScale = 0.6f, maximumSandScale = 1.4f;
    [Min(0.1f)] public float effectLifetime = 8f;
    [Min(1)] public int maximumParticlesPerSystem = 400;
    private JourneyAirWaterVehicle vehicle;
    private float nextImpactAt;
    private bool warnedPrefab, audioPaused;

    private void Awake()
    {
        vehicle = GetComponent<JourneyAirWaterVehicle>();
        // Keep collision audio separate from startup, engine and Nitro sounds.
        if (impactAudioSource == null || impactAudioSource == vehicle.engineSource || impactAudioSource == vehicle.effectsSource)
        {
            var source = new GameObject("Seabed Impact Audio"); source.transform.SetParent(transform, false);
            impactAudioSource = source.AddComponent<AudioSource>();
        }
        impactAudioSource.playOnAwake = false; impactAudioSource.loop = false;
        impactAudioSource.spatialBlend = 1f; impactAudioSource.volume = 1f; impactAudioSource.pitch = 1f;
        impactAudioSource.minDistance = 5f; impactAudioSource.maxDistance = 100f; impactAudioSource.dopplerLevel = 0f;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!isActiveAndEnabled || vehicle == null || vehicle.kind != JourneyAirWaterVehicle.VehicleKind.Submarine ||
            !vehicle.EffectsActive || Time.time < nextImpactAt) return;
        float speed = 0f; Vector3 point = default, normal = Vector3.up;
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            Collider other = contact.otherCollider;
            if (other == null || (seabedLayers.value & (1 << other.gameObject.layer)) == 0 ||
                contact.normal.y < minimumFloorNormalY || contact.point.y > vehicle.WaterY + 0.1f) continue;
            Rigidbody otherBody = other.attachedRigidbody;
            if (otherBody != null && !otherBody.isKinematic) continue;
            // Sliding along the bottom should not sound like a head-on impact.
            float closingSpeed = Mathf.Max(0f, -Vector3.Dot(collision.relativeVelocity, contact.normal));
            if (closingSpeed <= speed) continue;
            speed = closingSpeed; point = contact.point; normal = contact.normal;
        }
        if (speed < Mathf.Max(0.01f, minimumImpactSpeed)) return;
        nextImpactAt = Time.time + impactCooldown;
        float intensity = Mathf.InverseLerp(minimumImpactSpeed, Mathf.Max(minimumImpactSpeed + 0.01f, fullIntensitySpeed), speed);
        if (seabedImpactClip != null && impactAudioSource != null)
            impactAudioSource.PlayOneShot(seabedImpactClip, Mathf.Lerp(minimumVolume, Mathf.Max(minimumVolume, maximumVolume), intensity));
        if (sandImpactPrefab != null) SpawnSand(point, normal, intensity);
    }

    private void Update()
    {
        if (vehicle == null || impactAudioSource == null) return;
        bool stopped = !vehicle.EffectsActive;
        if (audioPaused == stopped) return;
        audioPaused = stopped;
        if (stopped) impactAudioSource.Pause(); else impactAudioSource.UnPause();
    }

    private void SpawnSand(Vector3 point, Vector3 normal, float intensity)
    {
        var instance = Instantiate(sandImpactPrefab, point + normal * 0.04f, Quaternion.FromToRotation(Vector3.up, normal));
        var particles = instance.GetComponentsInChildren<ParticleSystem>(true);
        if (particles.Length == 0)
        {
            if (!warnedPrefab) { warnedPrefab = true; Debug.LogWarning("Sand Impact Prefab needs a ParticleSystem.", this); }
            Destroy(instance); return;
        }
        float amount = Mathf.Lerp(minimumSandAmount, Mathf.Max(minimumSandAmount, maximumSandAmount), intensity);
        instance.transform.localScale *= Mathf.Lerp(minimumSandScale, Mathf.Max(minimumSandScale, maximumSandScale), intensity);
        foreach (var system in particles)
        {
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main; main.loop = false; main.playOnAwake = false;
            main.stopAction = ParticleSystemStopAction.None; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy; main.maxParticles = Mathf.Min(main.maxParticles, Mathf.Max(1, maximumParticlesPerSystem));
            var emission = system.emission;
            emission.rateOverTimeMultiplier *= amount; emission.rateOverDistanceMultiplier *= amount;
            for (int b = 0; b < emission.burstCount; b++)
            {
                var burst = emission.GetBurst(b); burst.count = ScaleCurve(burst.count, amount); emission.SetBurst(b, burst);
            }
        }
        // Leave each impact in world space; later impacts cannot replace it.
        instance.SetActive(true);
        foreach (var system in particles) if (system.gameObject.activeInHierarchy) system.Play(false);
        Destroy(instance, Mathf.Max(0.1f, effectLifetime));
    }

    private static ParticleSystem.MinMaxCurve ScaleCurve(ParticleSystem.MinMaxCurve curve, float multiplier)
    {
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.TwoConstants:
                return new ParticleSystem.MinMaxCurve(curve.constantMin * multiplier, curve.constantMax * multiplier);
            case ParticleSystemCurveMode.Curve:
                return new ParticleSystem.MinMaxCurve(curve.curveMultiplier * multiplier, curve.curve);
            case ParticleSystemCurveMode.TwoCurves:
                return new ParticleSystem.MinMaxCurve(curve.curveMultiplier * multiplier, curve.curveMin, curve.curveMax);
            default:
                return new ParticleSystem.MinMaxCurve(curve.constant * multiplier);
        }
    }

    private void OnDisable() { audioPaused = false; if (impactAudioSource != null) impactAudioSource.Stop(); }
}
