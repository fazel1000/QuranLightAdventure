using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(JourneyAirWaterVehicle))]
public sealed class JourneySubmarineBubbles : MonoBehaviour
{
    public Transform[] exhaustPoints = new Transform[0];
    public GameObject bubblePrefab;
    public Material bubbleMaterial;
    [Min(0f)] public float idleRate = 2f, fullSpeedRate = 45f;
    [Tooltip("Applied once to both emission rates. 5 gives five times the existing bubbles.")]
    [Min(0f)] public float emissionMultiplier = 5f;
    [Min(1)] public int maximumParticlesPerSystem = 600;
    [Min(0f)] public float minimumSpeed = 0.4f, maximumSpeed = 3f;
    private JourneyAirWaterVehicle vehicle;
    private readonly List<ParticleSystem> systems = new List<ParticleSystem>();
    private bool paused;
    private void Start()
    {
        vehicle = GetComponent<JourneyAirWaterVehicle>();
        foreach (var point in exhaustPoints)
        {
            if (point == null) continue;
            GameObject instance = bubblePrefab != null ? Instantiate(bubblePrefab, point) : new GameObject("Engine Bubbles", typeof(ParticleSystem));
            instance.transform.SetParent(point, false);
            instance.transform.localPosition = Vector3.zero;
            // Particle cones point along +Z. This makes them emit along the point's local -Z.
            instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var particles = instance.GetComponentInChildren<ParticleSystem>(true);
            if (particles == null) { Debug.LogWarning("Bubble Prefab needs a ParticleSystem.", this); Destroy(instance); continue; }
            if (bubblePrefab == null)
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main; main.playOnAwake = false; main.loop = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.09f);
                main.startColor = new Color(0.8f, 0.95f, 1f, 0.6f);
                var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 9f; shape.radius = 0.05f;
                var rise = particles.velocityOverLifetime; rise.enabled = true; rise.space = ParticleSystemSimulationSpace.World;
                rise.x = new ParticleSystem.MinMaxCurve(0f); rise.y = new ParticleSystem.MinMaxCurve(0.25f); rise.z = new ParticleSystem.MinMaxCurve(0f);
                if (bubbleMaterial != null) particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = bubbleMaterial;
            }
            foreach (var system in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main; main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = Mathf.Max(1, maximumParticlesPerSystem);
                main.stopAction = ParticleSystemStopAction.None; main.loop = true; main.playOnAwake = false;
                systems.Add(system); system.Play(false);
            }
        }
    }
    private void Update()
    {
        if (vehicle == null) return;
        bool stopped = !vehicle.EffectsActive;
        if (stopped != paused)
        {
            paused = stopped;
            foreach (var particles in systems) if (particles != null) { if (paused) particles.Pause(false); else particles.Play(false); }
        }
        if (paused) return;
        bool underwater = !vehicle.constrainToWater || transform.position.y < vehicle.WaterY;
        float speed = vehicle.IsOccupied ? vehicle.Speed01 : 0f;
        foreach (var particles in systems)
        {
            if (particles == null) continue;
            var main = particles.main; main.startSpeed = Mathf.Lerp(minimumSpeed, maximumSpeed, speed);
            var emission = particles.emission;
            emission.rateOverTime = underwater ? Mathf.Lerp(idleRate, fullSpeedRate, speed) * Mathf.Max(0f, emissionMultiplier) : 0f;
        }
    }
    private void OnEnable() { paused = false; foreach (var particles in systems) if (particles != null) particles.Play(false); }
    private void OnDisable() { foreach (var particles in systems) if (particles != null) particles.Stop(false, ParticleSystemStopBehavior.StopEmitting); }
}
