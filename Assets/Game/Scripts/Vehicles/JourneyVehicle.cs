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
    [Header("Nitro")]
    [Min(1f)] public float nitroMultiplier = 1.6f;
    [Min(0.1f)] public float nitroSeconds = 3f;
    [Min(0f)] public float nitroRechargePerSecond = 0.6f;
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
    private bool nitroHeld, nitroExhausted, frozen, wasKinematic;
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
            wheel.rotationOffset = Quaternion.Inverse(wheel.collider.transform.rotation) * wheel.visual.rotation;
            wheel.collider.ConfigureVehicleSubsteps(5f, 5, 8);
        }
    }
    public void SetOccupied(bool occupied)
    {
        IsOccupied = occupied;
        SetInput(0f, 0f, 1f, false);
        if (engineSource == null) return;
        if (occupied && engineLoop != null)
        { engineSource.clip = engineLoop; engineSource.loop = true; engineSource.Play(); }
        else engineSource.Stop();
    }
    public void SetInput(float steer, float throttle, float braking, bool nitro)
    { steering = Mathf.Clamp(steer, -1f, 1f); gas = Mathf.Clamp01(throttle); brake = Mathf.Clamp01(braking); nitroHeld = nitro; }
    public void Freeze(bool value)
    {
        if (Body == null || frozen == value) return;
        frozen = value;
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
        if (!Ready || frozen || Body.isKinematic) return;
        if (!nitroHeld) nitroExhausted = false;
        bool boost = IsOccupied && gas > 0f && brake < 0.01f && nitroHeld && !nitroExhausted && nitroRemaining > 0f;
        if (boost)
        {
            nitroRemaining = Mathf.Max(0f, nitroRemaining - Time.fixedDeltaTime);
            if (nitroRemaining <= 0f) nitroExhausted = true;
        }
        else if (!nitroHeld) nitroRemaining = Mathf.Min(nitroSeconds, nitroRemaining + nitroRechargePerSecond * Time.fixedDeltaTime);
        float speed = SpeedKph;
        float limit = maxSpeedKph * (boost ? nitroMultiplier : 1f);
        float desiredTurn = IsOccupied ? steering * steeringAngle * Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(speed / maxSpeedKph)) : 0f;
        turn = Mathf.MoveTowards(turn, desiredTurn, steeringResponse * Time.fixedDeltaTime);
        foreach (Wheel wheel in wheels)
        {
            wheel.collider.steerAngle = wheel.steering ? turn : 0f;
            wheel.collider.motorTorque = IsOccupied && brake < 0.01f && speed < limit
                ? motorTorque * gas * (boost ? nitroMultiplier : 1f) / 4f : 0f;
            wheel.collider.brakeTorque = !IsOccupied ? brakeTorque : brakeTorque * brake;
        }
        if (engineSource != null && IsOccupied) engineSource.pitch = Mathf.Lerp(0.8f, 1.8f, Mathf.Clamp01(speed / maxSpeedKph));
    }
    private void LateUpdate()
    {
        if (!Ready) return;
        foreach (Wheel wheel in wheels)
        {
            wheel.collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
            wheel.visual.SetPositionAndRotation(position, rotation * wheel.rotationOffset);
        }
    }
    private void OnDisable()
    {
        Available.Remove(this);
        if (engineSource != null) engineSource.Stop();
        if (wheels != null) foreach (Wheel wheel in wheels)
            if (wheel != null && wheel.collider != null) { wheel.collider.motorTorque = 0f; wheel.collider.brakeTorque = brakeTorque; }
    }
}
