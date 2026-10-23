using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
public sealed class JourneyAirWaterVehicle : MonoBehaviour
{
    public enum VehicleKind { Helicopter, Submarine }
    public VehicleKind kind;
    public JourneyMenuController menu;
    public Transform driverSeat, exitPoint, cameraTarget, visualRoot;
    public BoxCollider hull;
    [Tooltip("Helicopter Police faces local -Z; this submarine faces local +Z.")]
    public Vector3 localForward = Vector3.forward;
    [Header("Movement")]
    [Min(0.1f)] public float horizontalSpeed = 16f;
    [Min(0.1f)] public float verticalSpeed = 5f;
    [Tooltip("Applied to horizontal and vertical movement only for submarines. 2 doubles the existing speed.")]
    [Min(0.1f)] public float submarineSpeedMultiplier = 2f;
    [Tooltip("Applied to Vertical Speed only for helicopters. 1.5 is 50 percent faster ascent/descent.")]
    [Min(0.1f)] public float helicopterVerticalSpeedMultiplier = 1.5f;
    [Min(0.1f)] public float acceleration = 5f;
    [Min(0.1f)] public float velocityResponse = 2f;
    [Min(1f)] public float turnDegreesPerSecond = 70f;
    [Range(0f, 25f)] public float helicopterPitch = 12f, helicopterRoll = 8f;
    [Tooltip("Applied to the forward/backward body tilt. Does not tilt the submarine.")]
    [Min(0f)] public float helicopterPitchMultiplier = 1.5f;
    [Min(0f)] public float hoverDriftSpeed = 0.12f;
    [Min(0f)] public float parkedWaveAmplitude = 0.08f;
    [Range(0f, 5f)] public float parkedWaveYaw = 0.8f;
    [Header("Helicopter startup")]
    public Transform mainRotor, tailRotor;
    public float mainRotorRpm = 420f, tailRotorRpm = 1600f;
    [Min(0.1f)] public float rotorSpinupSeconds = 6f;
    [Min(0f)] public float movementLockSeconds = 4f;
    [Header("Submarine depth - water plane is optional")]
    public Transform waterSurface;
    [Tooltip("When assigned, the complete submarine hull stays inside this Underwater Volume.")]
    public JourneyWaterVolume waterVolume;
    public bool constrainToWater = true;
    public float waterLevel = 39.1f;
    [Min(1f)] public float maximumDepth = 80f;
    [Min(0f)] public float surfaceClearance = 0.15f;
    [Header("Submarine nitro: ramp, hold, return")]
    [Min(0.1f)] public float boostRampSeconds = 3f;
    [Min(0f)] public float boostHoldSeconds = 5f;
    [Min(0.1f)] public float boostReturnSeconds = 3f;
    [Min(1f)] public float boostMultiplier = 1.5f;
    [Min(0f)] public float boostCooldown = 2f;
    [Header("Camera")]
    [Min(1f)] public float cameraDistance = 10f;
    [Min(0f)] public float cameraHeight = 3f;
    [Header("3D sounds")]
    public AudioSource engineSource, effectsSource;
    public AudioClip startupClip, engineLoop, ambientLoop, boostClip;
    [Range(0f, 1f)] public float idleVolume = 0.3f, movingVolume = 0.8f, effectsVolume = 1f;
    [Range(0.1f, 3f)] public float idlePitch = 0.85f, movingPitch = 1.25f;
    public static readonly HashSet<JourneyAirWaterVehicle> Available = new HashSet<JourneyAirWaterVehicle>();
    public Rigidbody Body { get; private set; }
    public bool IsOccupied { get; private set; }
    public bool Ready => Body != null && hull != null && driverSeat != null && exitPoint != null;
    public bool InputUnlocked => kind != VehicleKind.Helicopter || engineAge >= movementLockSeconds;
    public bool EffectsActive => Ready && Gameplay && !externallyFrozen;
    public float Speed => Body != null && !Body.isKinematic ? Body.linearVelocity.magnitude : 0f;
    public float EffectiveHorizontalSpeed => Mathf.Max(0.1f, horizontalSpeed * (kind == VehicleKind.Submarine ? submarineSpeedMultiplier : 1f));
    public float EffectiveVerticalSpeed => Mathf.Max(0.1f, verticalSpeed * (kind == VehicleKind.Helicopter ? helicopterVerticalSpeedMultiplier : submarineSpeedMultiplier));
    public float Speed01 => Mathf.Clamp01(Speed / Mathf.Max(0.1f, EffectiveHorizontalSpeed * boostMultiplier));
    public float Boost01 => boostActive ? BoostEnvelope(boostAge, boostRampSeconds, boostHoldSeconds, boostReturnSeconds) : 0f;
    public bool IsBoosting => boostActive;
    public bool CanBoost => kind == VehicleKind.Submarine && IsOccupied && EffectsActive && !boostActive && simulationAge >= nextBoostAt;
    public float WaterY => waterVolume != null ? waterVolume.SurfaceY : waterSurface != null ? waterSurface.position.y : waterLevel;
    public Vector3 Forward => transform.TransformDirection(FlatForward);
    private Vector3 FlatForward => Vector3.ProjectOnPlane(localForward, Vector3.up).sqrMagnitude > 0.01f ? Vector3.ProjectOnPlane(localForward, Vector3.up).normalized : Vector3.forward;
    private bool Gameplay => focused && !appPaused && Time.timeScale > 0f && (menu == null || menu.IsGameplayActive);
    private Vector3 desiredDirection, parkedPosition;
    private Quaternion parkedRotation, visualRest, mainRest, tailRest;
    private float verticalInput, simulationAge, parkedAt, engineAge, rotorSpool, mainAngle, tailAngle, boostAge, nextBoostAt;
    private bool boostActive, externallyFrozen, focused = true, appPaused, audioPaused, warnedVolume;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() { Available.Clear(); }
    private void OnEnable() { Available.Add(this); if (Body != null) { audioPaused = false; SetEngineClip(); } }
    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
        Body.interpolation = RigidbodyInterpolation.Interpolate;
        Body.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        parkedPosition = Body.position; parkedRotation = Body.rotation;
        if (visualRoot != null) visualRest = visualRoot.localRotation;
        if (mainRotor != null) mainRest = mainRotor.localRotation;
        if (tailRotor != null) tailRest = tailRotor.localRotation;
        if (menu == null) menu = FindFirstObjectByType<JourneyMenuController>(FindObjectsInactive.Include);
        if (engineSource == null) engineSource = MakeSource();
        if (effectsSource == null || effectsSource == engineSource) effectsSource = MakeSource();
        ConfigureSource(engineSource); ConfigureSource(effectsSource);
        engineSource.loop = true; effectsSource.loop = false;
        SetEngineClip();
        if (!Ready) Debug.LogError("Air/water vehicle: assign Hull, Driver Seat and Exit Point using Setup Air Water and World Systems.", this);
    }
    private AudioSource MakeSource() { return gameObject.AddComponent<AudioSource>(); }
    private static void ConfigureSource(AudioSource source)
    { source.playOnAwake = false; source.spatialBlend = 1f; source.minDistance = 5f; source.maxDistance = 90f; source.dopplerLevel = 0.2f; }
    private void SetEngineClip()
    {
        if (engineSource == null) return;
        engineSource.Stop(); engineSource.clip = IsOccupied ? engineLoop != null ? engineLoop : ambientLoop : ambientLoop;
        if (engineSource.clip != null && isActiveAndEnabled) engineSource.Play();
        if (audioPaused || !Gameplay) engineSource.Pause();
    }
    public void SetOccupied(bool value)
    {
        if (IsOccupied == value || !Ready) return;
        IsOccupied = value; desiredDirection = Vector3.zero; verticalInput = 0f;
        boostActive = false; boostAge = 0f; engineAge = 0f;
        effectsSource.Stop();
        if (value)
        {
            if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            if (startupClip != null) { effectsSource.clip = startupClip; effectsSource.pitch = 1f; effectsSource.volume = effectsVolume; effectsSource.Play(); }
        }
        else { parkedPosition = Body.position; parkedAt = simulationAge; parkedRotation = Quaternion.Euler(0f, Body.rotation.eulerAngles.y, 0f); }
        SetEngineClip();
    }
    public void SetInput(Vector3 worldDirection, float vertical)
    { desiredDirection = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(worldDirection, Vector3.up), 1f); verticalInput = Mathf.Clamp(vertical, -1f, 1f); }
    public void Freeze(bool value) { externallyFrozen = value; if (value) SetInput(Vector3.zero, 0f); }
    public void RequestBoost()
    {
        if (!CanBoost) return;
        boostActive = true; boostAge = 0f;
        if (boostClip != null) { effectsSource.Stop(); effectsSource.clip = boostClip; effectsSource.volume = 0f; effectsSource.Play(); }
    }
    public static float BoostEnvelope(float age, float ramp, float hold, float release)
    {
        ramp = Mathf.Max(0.1f, ramp); hold = Mathf.Max(0f, hold); release = Mathf.Max(0.1f, release);
        if (age < 0f) return 0f;
        if (age < ramp) return Mathf.SmoothStep(0f, 1f, age / ramp);
        if (age < ramp + hold) return 1f;
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - ramp - hold) / release));
    }
    private void Update()
    {
        if (!Ready) return;
        bool stopped = !EffectsActive;
        if (audioPaused != stopped)
        {
            audioPaused = stopped;
            if (stopped) { engineSource.Pause(); effectsSource.Pause(); }
            else { engineSource.UnPause(); effectsSource.UnPause(); }
        }
        if (stopped) return;
        simulationAge += Time.deltaTime;
        if (IsOccupied) engineAge += Time.deltaTime;
        rotorSpool = Mathf.MoveTowards(rotorSpool, IsOccupied ? 1f : 0f, Time.deltaTime / Mathf.Max(0.1f, rotorSpinupSeconds));
        float rpmScale = Mathf.SmoothStep(0f, 1f, rotorSpool);
        mainAngle = Mathf.Repeat(mainAngle + mainRotorRpm * 6f * rpmScale * Time.deltaTime, 360f);
        tailAngle = Mathf.Repeat(tailAngle + tailRotorRpm * 6f * rpmScale * Time.deltaTime, 360f);
        if (mainRotor != null) mainRotor.localRotation = mainRest * Quaternion.AngleAxis(mainAngle, Vector3.up);
        if (tailRotor != null) tailRotor.localRotation = tailRest * Quaternion.AngleAxis(tailAngle, Vector3.right);
        if (boostActive)
        {
            boostAge += Time.deltaTime;
            if (boostAge >= boostRampSeconds + boostHoldSeconds + boostReturnSeconds)
            { boostActive = false; nextBoostAt = simulationAge + boostCooldown; if (effectsSource.clip == boostClip) effectsSource.Stop(); }
        }
        engineSource.volume = Mathf.Lerp(idleVolume, movingVolume, Speed01) * (kind == VehicleKind.Helicopter && IsOccupied ? Mathf.Lerp(0.15f, 1f, rpmScale) : 1f);
        engineSource.pitch = Mathf.Lerp(idlePitch, movingPitch, Speed01);
        if (boostActive && boostClip != null)
        {
            float fade = boostAge >= boostRampSeconds + boostHoldSeconds ? Boost01 : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(boostAge / boostRampSeconds));
            effectsSource.volume = effectsVolume * fade; effectsSource.pitch = Mathf.Lerp(0.85f, 1.1f, Boost01);
        }
        if (kind == VehicleKind.Helicopter && visualRoot != null)
        {
            Vector3 velocity = transform.InverseTransformDirection(Body.isKinematic ? Vector3.zero : Body.linearVelocity);
            Vector3 right = Vector3.Cross(Vector3.up, FlatForward);
            float pitch = Mathf.Clamp(Vector3.Dot(velocity, FlatForward) / EffectiveHorizontalSpeed, -1f, 1f) * helicopterPitch * helicopterPitchMultiplier;
            float roll = -Mathf.Clamp(Vector3.Dot(velocity, right) / EffectiveHorizontalSpeed, -1f, 1f) * helicopterRoll;
            Quaternion tilt = Quaternion.AngleAxis(pitch, right) * Quaternion.AngleAxis(roll, FlatForward);
            visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation, visualRest * tilt, 1f - Mathf.Exp(-3f * Time.deltaTime));
        }
    }
    private void FixedUpdate()
    {
        if (!Ready) return;
        bool locked = !EffectsActive || IsOccupied && !InputUnlocked;
        bool parkedSub = kind == VehicleKind.Submarine && !IsOccupied;
        bool kinematic = locked || parkedSub;
        if (Body.isKinematic != kinematic)
        {
            if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            Body.isKinematic = kinematic;
        }
        Body.useGravity = kind == VehicleKind.Helicopter && !IsOccupied;
        if (locked) return;
        if (parkedSub)
        {
            float age = simulationAge - parkedAt;
            Vector3 wave = new Vector3(Mathf.Sin(age * 0.31f) * 0.5f, Mathf.Sin(age * 0.9f), (Mathf.Cos(age * 0.27f) - 1f) * 0.25f) * parkedWaveAmplitude;
            Quaternion rotation = parkedRotation * Quaternion.Euler(0f, Mathf.Sin(age * 0.4f) * parkedWaveYaw, 0f);
            Vector3 target = ConstrainSubPosition(parkedPosition + wave, rotation);
            Body.MovePosition(target); Body.MoveRotation(rotation); return;
        }
        if (!IsOccupied) return;
        float multiplier = Mathf.Lerp(1f, boostMultiplier, Boost01);
        Vector3 targetVelocity = desiredDirection * (EffectiveHorizontalSpeed * multiplier) + Vector3.up * (verticalInput * EffectiveVerticalSpeed);
        if (desiredDirection.sqrMagnitude < 0.001f && Mathf.Abs(verticalInput) < 0.01f)
            targetVelocity = new Vector3(Mathf.Sin(simulationAge * 0.7f), Mathf.Cos(simulationAge * 0.9f) * 0.5f, Mathf.Sin(simulationAge * 0.5f)) * hoverDriftSpeed;
        Quaternion nextRotation = Body.rotation;
        if (desiredDirection.sqrMagnitude > 0.01f)
        {
            Quaternion heading = Quaternion.LookRotation(desiredDirection, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(FlatForward, Vector3.up));
            nextRotation = Quaternion.RotateTowards(Body.rotation, heading, turnDegreesPerSecond * Time.fixedDeltaTime);
        }
        Vector3 force = Vector3.ClampMagnitude((targetVelocity - Body.linearVelocity) * velocityResponse, acceleration);
        if (kind == VehicleKind.Submarine && (constrainToWater || waterVolume != null))
        {
            Vector3 position = ConstrainSubPosition(Body.position, nextRotation);
            if ((position - Body.position).sqrMagnitude > 0.000001f) Body.position = position;
            Vector3 velocity = Body.linearVelocity + force * Time.fixedDeltaTime;
            Vector3 next = ConstrainSubPosition(position + velocity * Time.fixedDeltaTime, nextRotation);
            Body.linearVelocity = (next - position) / Time.fixedDeltaTime;
        }
        else Body.AddForce(force, ForceMode.Acceleration);
        Body.MoveRotation(nextRotation);
    }
    private Vector3 ConstrainSubPosition(Vector3 desired, Quaternion rotation)
    {
        if (constrainToWater) desired.y = ClampDepth(desired.y);
        if (waterVolume == null) return desired;
        if (waterVolume.TryConstrainHull(transform, hull, desired, rotation, surfaceClearance, out Vector3 result)) return result;
        if (!warnedVolume)
        { warnedVolume = true; Debug.LogError("Submarine Water Volume must be active, upright, and large enough to contain its Hull. Movement is stopped until corrected.", this); }
        return Body.position;
    }
    private float ClampDepth(float y)
    {
        if (kind != VehicleKind.Submarine || !constrainToWater) return y;
        float top = hull != null ? (hull.center.y + hull.size.y * 0.5f) * Mathf.Abs(transform.lossyScale.y) : 1f;
        return Mathf.Clamp(y, WaterY - maximumDepth, WaterY - top - surfaceClearance);
    }
    private void OnApplicationFocus(bool value) { focused = value; if (!value) SetInput(Vector3.zero, 0f); }
    private void OnApplicationPause(bool value) { appPaused = value; if (value) SetInput(Vector3.zero, 0f); }
    private void OnDisable()
    {
        Available.Remove(this);
        if (engineSource != null) engineSource.Stop(); if (effectsSource != null) effectsSource.Stop();
        if (Body != null)
        {
            if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            Body.isKinematic = true;
        }
    }
}
