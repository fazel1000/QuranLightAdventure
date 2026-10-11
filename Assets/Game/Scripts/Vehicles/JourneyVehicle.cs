using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
public sealed class JourneyVehicle : MonoBehaviour
{
    [Serializable] public sealed class Wheel
    {
        public WheelCollider collider;
        public Transform visual;
        public bool steering;
        [NonSerialized] public Quaternion rotationOffset;
        [NonSerialized] public Vector3 pivotFromCenter;
    }
    public Wheel[] wheels = new Wheel[4];
    public Transform driverSeat, exitPoint, cameraTarget;
    public BoxCollider bodyCollider;
    [Header("Driving")]
    [Min(1f)] public float motorTorque = 1200f;
    [Min(1f)] public float brakeTorque = 3000f;
    [Min(1f)] public float maxSpeedKph = 60f;
    [Range(5f, 45f)] public float steeringAngle = 28f;
    [Min(1f)] public float steeringResponse = 90f;
    public Vector3 centerOfMass = new Vector3(0f, 0.4f, 0f);
    [Header("Reverse - release brake after stopping, then press again")]
    [Min(1f)] public float reverseMaxSpeedKph = 15f;
    [Range(0.1f, 1f)] public float reverseTorqueMultiplier = 0.6f;
    [Range(0.05f, 1f)] public float reverseEngageSpeedKph = 0.5f;
    private bool reversePedal;
    private float effectiveThrottle, effectiveBrake;
    public bool IsReversing => reversePedal && IsOccupied && !frozen;
    [Header("Coasting - torque per wheel")]
    [Min(0f)] public float coastBrakeTorque = 85f;
    [Min(0f)] public float coastBrakeResponse = 180f;
    public float Throttle01 => IsOccupied && !frozen ? effectiveThrottle : 0f;
    public float Brake01 => IsOccupied && !frozen ? effectiveBrake : 0f;
    public bool EffectsActive => Ready && IsOccupied && !frozen && isActiveAndEnabled && Time.timeScale > 0f;
    public bool IsBoosting { get; private set; }
    private float coastingTorque;
    [Header("Nitro")]
    [Min(1f)] public float nitroMultiplier = 1.6f;
    [Min(0.1f)] public float nitroSeconds = 3f;
    [Min(0f)] public float nitroRechargePerSecond = 0.6f;
    [Min(0.1f)] public float nitroBurstDuration = 1.2f;
    [Min(0f)] public float nitroAcceleration = 12f;
    [Min(0f)] public float nitroCooldown = 3f;
    private float nitroBurstRemaining, nextNitroAt;
    private bool nitroRequested;
    public void RequestNitro()
    {
        if (EffectsActive && Body != null && !Body.isKinematic) nitroRequested = true;
    }
    private void CancelNitro()
    {
        nitroRequested = false; nitroBurstRemaining = 0f; IsBoosting = false;
    }
    [Header("Camera")]
    [Min(1f)] public float cameraDistance = 5f;
    [Min(0.1f)] public float cameraHeight = 2f;
    [Header("Optional sound")]
    public AudioSource engineSource;
    public AudioClip engineLoop;
    public float SpeedKph => Body != null ? Body.linearVelocity.magnitude * 3.6f : 0f;
    public float Nitro01 => nitroRemaining / Mathf.Max(0.1f, nitroSeconds);
    public bool IsOccupied { get; private set; }
    public bool Ready { get; private set; }
    public Rigidbody Body { get; private set; }
    public static readonly HashSet<JourneyVehicle> Available = new HashSet<JourneyVehicle>();
    private float steering, gas, brake, turn, nitroRemaining;
    private bool nitroHeld, frozen, wasKinematic;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { Available.Clear(); }
    private void OnEnable() { Available.Add(this); }
    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
        Ready = driverSeat != null && exitPoint != null && cameraTarget != null && bodyCollider != null && wheels != null && wheels.Length == 4;
        if (Ready) foreach (Wheel wheel in wheels)
            if (wheel == null || wheel.collider == null || wheel.visual == null) Ready = false;
        if (!Ready) { Debug.LogError("JourneyVehicle: run Setup Journey Vehicle and assign all four wheels and markers.", this); return; }
        Body.centerOfMass = centerOfMass;
        nitroRemaining = nitroSeconds;
        foreach (Wheel wheel in wheels)
        {
            MeshFilter filter = wheel.visual.GetComponent<MeshFilter>();
            Vector3 center = filter != null && filter.sharedMesh != null
                ? wheel.visual.TransformPoint(filter.sharedMesh.bounds.center) : wheel.visual.position;
            wheel.pivotFromCenter = Quaternion.Inverse(wheel.visual.rotation) * (wheel.visual.position - center);
            wheel.rotationOffset = Quaternion.Inverse(wheel.collider.transform.rotation) * wheel.visual.rotation;
            wheel.collider.ConfigureVehicleSubsteps(5f, 5, 8);
        }
    }
    public void SetOccupied(bool occupied)
    {
        IsOccupied = occupied;
        CancelNitro();
        reversePedal = false; effectiveThrottle = 0f; effectiveBrake = 0f;
        brake = 1f; // Synthetic parking input must not count as a reverse-button press.
        SetInput(0f, 0f, 1f, false);
        if (engineSource == null) return;
        if (occupied && engineLoop != null)
        { engineSource.clip = engineLoop; engineSource.loop = true; engineSource.Play(); }
        else engineSource.Stop();
    }
    public void SetInput(float steer, float throttle, float braking, bool nitro)
    {
        float nextBrake = Mathf.Clamp01(braking);
        float nextGas = Mathf.Clamp01(throttle);
        // Latch reverse only on a fresh press near standstill. Holding the brake
        // throughout a forward stop must never automatically select reverse.
        if (nextBrake < 0.01f || nextGas > 0.01f || !IsOccupied || frozen)
            reversePedal = false;
        else if (brake < 0.01f && nextBrake >= 0.01f)
            reversePedal = SpeedKph <= reverseEngageSpeedKph;
        steering = Mathf.Clamp(steer, -1f, 1f);
        gas = nextGas; brake = nextBrake;
        if (nitro && !nitroHeld) RequestNitro();
        nitroHeld = nitro;
        if (brake >= 0.01f) CancelNitro();
    }
    public void Freeze(bool value)
    {
        if (Body == null || frozen == value) return;
        frozen = value;
        CancelNitro();
        reversePedal = false; effectiveThrottle = effectiveBrake = 0f;
        if (value)
        {
            wasKinematic = Body.isKinematic;
            if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            Body.isKinematic = true;
            if (engineSource != null) engineSource.Pause();
        }
        else
        {
            Body.isKinematic = wasKinematic;
            if (engineSource != null && IsOccupied) engineSource.UnPause();
        }
    }
    private void FixedUpdate()
    {
        if (!Ready || frozen || Body.isKinematic) { CancelNitro(); return; }
        float signedSpeed = Vector3.Dot(Body.linearVelocity, transform.forward) * 3.6f;
        bool grounded = false;
        foreach (Wheel wheel in wheels) if (wheel.collider.isGrounded) { grounded = true; break; }
        if (nitroRequested)
        {
            nitroRequested = false;
            float duration = Mathf.Max(0.1f, nitroBurstDuration);
            if (IsOccupied && grounded && brake < 0.01f && !reversePedal &&
                signedSpeed >= -reverseEngageSpeedKph && Time.time >= nextNitroAt &&
                nitroRemaining >= Mathf.Min(duration, Mathf.Max(0.1f, nitroSeconds)))
            {
                nitroBurstRemaining = Mathf.Min(duration, nitroRemaining);
                nextNitroAt = Time.time + nitroBurstRemaining + nitroCooldown;
            }
        }
        if (!IsOccupied || brake >= 0.01f || reversePedal || signedSpeed < -reverseEngageSpeedKph || !grounded)
            CancelNitro();
        bool boost = nitroBurstRemaining > 0f && nitroRemaining > 0f;
        float drive = 0f;
        effectiveBrake = 0f;
        if (IsOccupied)
        {
            if (brake >= 0.01f)
            {
                if (reversePedal && gas < 0.01f)
                {
                    if (signedSpeed > reverseEngageSpeedKph) effectiveBrake = brake;
                    else drive = -brake;
                }
                else effectiveBrake = brake;
            }
            else if (gas > 0f)
            {
                if (signedSpeed < -reverseEngageSpeedKph) effectiveBrake = gas;
                else drive = gas;
            }
        }
        if (boost)
        {
            drive = 1f; // One tap supplies forward throttle for the short burst.
            nitroBurstRemaining = Mathf.Max(0f, nitroBurstRemaining - Time.fixedDeltaTime);
            nitroRemaining = Mathf.Max(0f, nitroRemaining - Time.fixedDeltaTime);
        }
        else nitroRemaining = Mathf.Min(nitroSeconds, nitroRemaining + nitroRechargePerSecond * Time.fixedDeltaTime);
        effectiveThrottle = Mathf.Abs(drive);
        IsBoosting = boost;
        coastingTorque = Mathf.MoveTowards(coastingTorque,
            IsOccupied && Mathf.Abs(drive) < 0.01f && effectiveBrake < 0.01f ? coastBrakeTorque : 0f,
            coastBrakeResponse * Time.fixedDeltaTime);
        float speed = SpeedKph;
        float limit = drive < 0f ? reverseMaxSpeedKph : maxSpeedKph * (boost ? nitroMultiplier : 1f);
        float desiredTurn = IsOccupied ? steering * steeringAngle * Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(speed / maxSpeedKph)) : 0f;
        turn = Mathf.MoveTowards(turn, desiredTurn, steeringResponse * Time.fixedDeltaTime);
        foreach (Wheel wheel in wheels)
        {
            wheel.collider.steerAngle = wheel.steering ? turn : 0f;
            wheel.collider.motorTorque = IsOccupied && effectiveBrake < 0.01f && speed < limit
                ? motorTorque * drive * (drive < 0f ? reverseTorqueMultiplier : boost ? nitroMultiplier : 1f) / 4f : 0f;
            wheel.collider.brakeTorque = !IsOccupied ? brakeTorque : Mathf.Max(brakeTorque * effectiveBrake, Mathf.Abs(drive) < 0.01f ? coastingTorque : 0f);
        }
        if (boost && speed < limit)
        {
            Vector3 direction = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            float acceleration = Mathf.Min(nitroAcceleration,
                Mathf.Max(0f, limit / 3.6f - Body.linearVelocity.magnitude) / Time.fixedDeltaTime);
            Body.AddForce(direction * acceleration, ForceMode.Acceleration);
        }
        if (engineSource != null && IsOccupied) engineSource.pitch = Mathf.Lerp(0.8f, 1.8f, Mathf.Clamp01(speed / maxSpeedKph));
    }
    private void LateUpdate()
    {
        if (!Ready) return;
        foreach (Wheel wheel in wheels)
        {
            wheel.collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
            Quaternion visualRotation = rotation * wheel.rotationOffset;
            wheel.visual.SetPositionAndRotation(position + visualRotation * wheel.pivotFromCenter, visualRotation);
        }
    }
    private void OnDisable()
    {
        Available.Remove(this);
        CancelNitro();
        reversePedal = false; effectiveThrottle = effectiveBrake = 0f;
        if (engineSource != null) engineSource.Stop();
        if (wheels != null) foreach (Wheel wheel in wheels)
            if (wheel != null && wheel.collider != null) { wheel.collider.motorTorque = 0f; wheel.collider.brakeTorque = brakeTorque; }
    }
}
