using System.Collections.Generic;
using UnityEngine;

// Arcade game movement only: no real-world aerodynamic or guidance model.
[DisallowMultipleComponent]
public sealed class JourneyDroneLauncher : MonoBehaviour
{
    public Transform droneRoot, boosterRoot, thrownRailRoot, holderBase;
    [Tooltip("World destination of Drone Root's pivot at the end of rail travel. Place ahead along the selected local X direction of Drone Root.")]
    public Transform railEnd;
    [Tooltip("On: model nose points along Drone Root local +X. Off: local -X. Set before launching.")]
    public bool launchAlongPositiveX = true;
    public Rigidbody droneBody, boosterBody;
    public GameObject boosterFlame, engineSmoke;
    public Transform engineSmokePoint;
    public GameObject engineSmokePrefab;
    public AudioSource audioSource;
    public AudioClip boosterSound, engineLoop, impactSound;
    [Header("Impact audio - separate source")]
    public AudioSource impactAudioSource;
    public bool impactAudio2D = true;
    [Header("Visual propeller")]
    public Transform propeller;
    [Tooltip("Visual rotation around the propeller's local Y, in degrees per second. Negative reverses it.")]
    public float propellerDegreesPerSecond = 1800f;
    public GameObject impactExplosionPrefab, targetSmokePrefab;
    [Header("Arcade timing and movement")]
    [Min(0.05f)] public float railDuration = 0.45f;
    [Min(0.1f)] public float boosterDuration = 2f;
    [Min(0f)] public float cameraDelay = 4f;
    [Min(0.1f)] public float flightSpeed = 12f;
    [Min(1f)] public float yawSpeed = 65f, pitchSpeed = 45f;
    [Range(5f, 80f)] public float maxPitch = 65f;
    [Min(5f)] public float maximumFlightSeconds = 90f;
    public LayerMask impactLayers = ~0;
    [Min(0.1f)] public float cameraDistance = 2f, cameraHeight = 0.7f;
    [Min(1f)] public float debrisLifetime = 12f, explosionLifetime = 8f;
    public static readonly HashSet<JourneyDroneLauncher> Available = new HashSet<JourneyDroneLauncher>();
    public bool Launched { get; private set; }
    public bool Finished { get; private set; }
    public float Elapsed { get; private set; }
    public bool Ready => droneRoot != null && boosterRoot != null && thrownRailRoot != null &&
        railEnd != null && droneBody != null && boosterBody != null && audioSource != null &&
        droneRoot.GetComponentInChildren<Collider>() != null;
    public Vector3 Forward => droneRoot != null ? (launchAlongPositiveX ? droneRoot.right : -droneRoot.right) : transform.forward;
    private Vector3 droneStart, boosterStart, railStart, railDelta, boosterOffset;
    private Quaternion boosterRotation, modelOffset;
    private float yaw, pitch;
    private bool separated, paused;
    private Vector3 debrisVelocity, debrisAngularVelocity;
    private JourneyDronePilot pilot;
    private Collider[] droneColliders, ownColliders;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Available.Clear(); }
    private void OnEnable() { Available.Add(this); }
    private void Awake()
    {
        if (propeller == null && droneRoot != null)
            foreach (Transform child in droneRoot.GetComponentsInChildren<Transform>(true))
                if (child.name == "Drone propeller") { propeller = child; break; }
        if (boosterFlame != null) boosterFlame.SetActive(false);
        if (engineSmoke != null) engineSmoke.SetActive(false);
        if (audioSource != null) { audioSource.playOnAwake = false; audioSource.Stop(); }
    }
    public bool Launch(JourneyDronePilot owner)
    {
        if (!Ready || Launched || owner == null) return false;
        if (audioSource.transform == droneRoot || !audioSource.transform.IsChildOf(droneRoot))
        { Debug.LogError("Drone audio must be on its own child under Drone Root.", this); return false; }
        if (Vector3.Dot(railEnd.position - droneRoot.position, Forward) <= 0.01f)
        { Debug.LogError("RailEnd must be ahead along Drone Root local " + (launchAlongPositiveX ? "+X" : "-X") + ". Move RailEnd toward the model nose; do not reverse the model to satisfy this check.", this); return false; }
        pilot = owner; Launched = true; Elapsed = 0f;
        droneStart = droneRoot.position; boosterStart = boosterRoot.position; railStart = thrownRailRoot.position;
        // All three roots share one displacement, preserving their assembled alignment.
        Vector3 direction = Forward.normalized;
        float distance = Vector3.Dot(railEnd.position - droneStart, direction);
        railDelta = direction * Mathf.Max(0.05f, distance);
        droneBody.isKinematic = true; droneBody.useGravity = false;
        boosterBody.isKinematic = true; boosterBody.useGravity = false;
        droneColliders = droneRoot.GetComponentsInChildren<Collider>(true);
        ownColliders = GetComponentsInChildren<Collider>(true);
        foreach (Collider a in droneColliders) foreach (Collider b in ownColliders)
            if (a != b && a.enabled && b.enabled) Physics.IgnoreCollision(a, b, true);
        if (owner.player != null)
            foreach (Collider a in droneColliders) foreach (Collider b in owner.player.GetComponentsInChildren<Collider>())
                Physics.IgnoreCollision(a, b, true);
        boosterOffset = droneRoot.InverseTransformPoint(boosterRoot.position);
        boosterRotation = Quaternion.Inverse(droneRoot.rotation) * boosterRoot.rotation;
        pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
        yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        Quaternion heading = Quaternion.LookRotation(direction, Vector3.up);
        modelOffset = Quaternion.Inverse(heading) * droneRoot.rotation;
        if (engineSmoke == null && engineSmokePrefab != null)
        {
            engineSmoke = Instantiate(engineSmokePrefab, engineSmokePoint != null ? engineSmokePoint : droneRoot, false);
            engineSmoke.SetActive(false);
        }
        if (boosterFlame != null) { boosterFlame.SetActive(true); PlayParticles(boosterFlame); }
        if (boosterSound != null) { audioSource.clip = boosterSound; audioSource.loop = true; audioSource.Play(); }
        return true;
    }
    public void Tick(float dt, Vector2 input, bool canSteer)
    {
        if (!Launched || Finished || paused) return;
        if (droneRoot == null) { Finish(false, null, transform.position); return; }
        float previous = Elapsed; Elapsed += dt;
        float railTime = Mathf.Max(0.05f, railDuration);
        if (previous < railTime)
        {
            float u = Mathf.Clamp01(Elapsed / railTime);
            // Ease-in launcher animation; Holder Base is never moved.
            Vector3 delta = railDelta * u * u;
            droneRoot.position = droneStart + delta;
            thrownRailRoot.position = railStart + delta;
            if (!separated && boosterRoot != null) boosterRoot.position = boosterStart + delta;
            Physics.SyncTransforms();
        }
        float flightDt = Mathf.Max(0f, Elapsed - Mathf.Max(previous, railTime));
        if (flightDt > 0f)
        {
            if (canSteer)
            {
                yaw += input.x * yawSpeed * flightDt;
                pitch = Mathf.Clamp(pitch + input.y * pitchSpeed * flightDt, -maxPitch, maxPitch);
            }
            Quaternion rotation = Quaternion.Euler(-pitch, yaw, 0f);
            Vector3 direction = rotation * Vector3.forward;
            Vector3 delta = direction * flightSpeed * flightDt;
            droneRoot.rotation = rotation * modelOffset;
            Physics.SyncTransforms();
            // Resolve initial overlaps too: sweeps alone may miss a collider already intersecting after a turn.
            foreach (var own in droneColliders)
            {
                if (own == null || !own.enabled || !own.gameObject.activeInHierarchy) continue;
                Bounds bounds = own.bounds;
                foreach (var other in Physics.OverlapBox(bounds.center, bounds.extents, Quaternion.identity, impactLayers, QueryTriggerInteraction.Ignore))
                {
                    if (Ignore(other)) continue;
                    if (Physics.ComputePenetration(own, own.transform.position, own.transform.rotation,
                        other, other.transform.position, other.transform.rotation, out Vector3 separation, out float depth))
                    { Finish(true, other, other.ClosestPoint(bounds.center)); return; }
                }
            }
            // Sweep the actual convex child collider(s), so fast motion cannot simply skip a target.
            RaycastHit[] contacts = droneBody.SweepTestAll(direction, delta.magnitude, QueryTriggerInteraction.Ignore);
            RaycastHit nearest = default; bool found = false; float nearestDistance = float.PositiveInfinity;
            foreach (var hit in contacts)
            {
                if (Ignore(hit.collider) || hit.distance >= nearestDistance) continue;
                found = true; nearest = hit; nearestDistance = hit.distance;
            }
            if (found) { Finish(true, nearest.collider, nearest.point); return; }
            droneBody.MovePosition(droneBody.position + delta);
            if (!separated && boosterRoot != null)
            {
                boosterBody.MovePosition(droneRoot.TransformPoint(boosterOffset) + delta);
                boosterBody.MoveRotation(droneRoot.rotation * boosterRotation);
            }
        }
        if (!separated && Elapsed >= Mathf.Max(boosterDuration, railTime)) SeparateBooster();
        if (Elapsed >= maximumFlightSeconds || droneRoot.position.y < -1000f)
            Finish(false, null, droneRoot.position);
    }
    private bool Ignore(Collider collider)
    {
        return collider == null || (impactLayers.value & (1 << collider.gameObject.layer)) == 0 ||
            collider.transform.IsChildOf(transform) ||
            (boosterRoot != null && collider.transform.IsChildOf(boosterRoot)) ||
            (pilot != null && pilot.player != null && collider.transform.IsChildOf(pilot.player.transform));
    }
    private void SeparateBooster()
    {
        separated = true;
        if (boosterFlame != null) boosterFlame.SetActive(false);
        audioSource.Stop();
        if (engineLoop != null) { audioSource.clip = engineLoop; audioSource.loop = true; audioSource.Play(); }
        if (engineSmoke != null) { engineSmoke.SetActive(true); PlayParticles(engineSmoke); }
        if (boosterRoot == null) return;
        boosterRoot.SetParent(null, true);
        boosterBody.isKinematic = false; boosterBody.useGravity = true;
        boosterBody.linearVelocity = Forward.normalized * flightSpeed;
        Destroy(boosterRoot.gameObject, debrisLifetime);
    }
    public void SetPaused(bool value)
    {
        if (paused == value) return;
        paused = value;
        if (audioSource != null) { if (value) audioSource.Pause(); else audioSource.UnPause(); }
        foreach (GameObject effect in new[] { boosterFlame, engineSmoke })
            if (effect != null && effect.activeInHierarchy)
                foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>())
                { if (value) ps.Pause(); else ps.Play(); }
        if (separated && boosterBody != null)
        {
            if (value)
            {
                debrisVelocity = boosterBody.linearVelocity; debrisAngularVelocity = boosterBody.angularVelocity;
                boosterBody.isKinematic = true;
            }
            else
            {
                boosterBody.isKinematic = false;
                boosterBody.linearVelocity = debrisVelocity; boosterBody.angularVelocity = debrisAngularVelocity;
            }
        }
    }
    public void Abort() { if (Launched && !Finished) Finish(false, null, droneRoot != null ? droneRoot.position : transform.position); }
    private void Finish(bool impact, Collider hit, Vector3 point)
    {
        if (Finished) return;
        Finished = true; // One projectile awards only once, even with multiple target colliders.
        if (impact && hit != null)
        {
            var target = hit.GetComponentInParent<JourneyDroneTarget>();
            if (target != null) target.Hit(pilot != null ? pilot.coinReceiver : null, point, targetSmokePrefab);
            if (impactExplosionPrefab != null)
            {
                var explosion = Instantiate(impactExplosionPrefab, point, Quaternion.identity);
                explosion.SetActive(true); PlayParticles(explosion); Destroy(explosion, explosionLifetime);
            }
        }
        if (audioSource != null) audioSource.Stop();
        if (impact && impactSound != null) PlayImpactAudio(point);
        if (!separated && boosterRoot != null) Destroy(boosterRoot.gameObject);
        if (droneRoot != null) Destroy(droneRoot.gameObject);
    }
    private void PlayImpactAudio(Vector3 point)
    {
        var source = impactAudioSource;
        // An engine source must never be reused for this separate, audible impact channel.
        bool temporary = source == null || source == audioSource ||
            source.transform == droneRoot || source.transform == boosterRoot;
        if (temporary)
        {
            var go = new GameObject("Drone Impact Audio");
            go.transform.position = point;
            source = go.AddComponent<AudioSource>();
            source.volume = 1f;
        }
        else if ((droneRoot != null && source.transform.IsChildOf(droneRoot)) ||
            (!separated && boosterRoot != null && source.transform.IsChildOf(boosterRoot)))
        {
            source.transform.SetParent(null, true);
            temporary = true;
        }
        source.playOnAwake = false; source.loop = false;
        if (impactAudio2D) source.spatialBlend = 0f;
        // PlayOneShot allows a shared scene source to finish older explosions too.
        source.PlayOneShot(impactSound);
        if (temporary) Destroy(source.gameObject, impactSound.length / Mathf.Max(0.01f, Mathf.Abs(source.pitch)) + 0.5f);
    }
    private void Update()
    {
        if (Launched && !Finished && !paused && propeller != null)
            propeller.Rotate(Vector3.up, propellerDegreesPerSecond * Time.deltaTime, Space.Self);
    }
    private void OnDrawGizmosSelected()
    {
        if (droneRoot == null || railEnd == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(droneRoot.position, railEnd.position);
        Gizmos.DrawWireSphere(railEnd.position, 0.15f);
    }
    private static void PlayParticles(GameObject root)
    { foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true)) ps.Play(true); }
    private void OnDisable() { Available.Remove(this); Abort(); }
}
