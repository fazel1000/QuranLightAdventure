using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class JourneyThrowableStone : MonoBehaviour
{
    [Header("Natural physics - starting values, tune for your scale")]
    [SerializeField, Min(0.01f)] private float mass = 2f;
    [SerializeField, Min(0f)] private float linearDamping = 0f;
    [SerializeField, Min(0f)] private float angularDamping = 1f;
    [SerializeField, Min(1f)] private float resetBelowStart = 40f;
    [SerializeField, Min(0.01f)] private float pickupSpeedLimit = 0.3f;
    private Rigidbody body;
    private Collider hitCollider;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private Transform originalParent;
    private Coroutine motion;
    private JourneyStoneThrowController owner;
    private bool held, spent, thrown, initialized;
    private float flightAge;
    public Vector3 LaunchPosition { get; private set; }
    public bool WasLaunchedBy(JourneyStoneThrowController controller) => thrown && owner == controller;
    public bool CanPickUp => initialized && isActiveAndEnabled && !held && !spent &&
        (!thrown || (flightAge > 0.5f && body.linearVelocity.sqrMagnitude < pickupSpeedLimit * pickupSpeedLimit));
    public float Radius
    {
        get
        {
            if (hitCollider is SphereCollider sphere)
            {
                Vector3 s = transform.lossyScale;
                return sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            }
            // Conservative preview radius for box/convex stone colliders.
            return hitCollider != null && hitCollider.enabled ? hitCollider.bounds.extents.magnitude : cachedRadius;
        }
    }
    private float cachedRadius = 0.12f;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        hitCollider = GetComponent<Collider>();
        if (hitCollider == null || hitCollider.isTrigger ||
            (hitCollider is MeshCollider mesh && !mesh.convex))
        {
            Debug.LogError("JourneyThrowableStone: add one non-trigger Sphere/Box Collider or a convex Mesh Collider on the root.", this);
            enabled = false;
            return;
        }
        originalPosition = transform.position;
        originalRotation = transform.rotation;
        originalParent = transform.parent;
        cachedRadius = Radius;
        body.mass = Mathf.Max(0.01f, mass);
        body.linearDamping = Mathf.Max(0f, linearDamping);
        body.angularDamping = Mathf.Max(0f, angularDamping);
        body.useGravity = true;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        initialized = true;
    }

    public bool PickUp(Transform hand)
    {
        if (!CanPickUp || hand == null) return false;
        cachedRadius = Radius;
        held = true;
        thrown = false;
        owner = null;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        // While carried, animation owns the pose. Rigidbody interpolation would
        // otherwise apply a delayed world pose while the hand continues moving.
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.isKinematic = true;
        hitCollider.enabled = false;
        transform.SetParent(hand, true);
        motion = StartCoroutine(MoveIntoHand());
        return true;
    }

    private IEnumerator MoveIntoHand()
    {
        Vector3 start = transform.localPosition;
        Quaternion rotation = transform.localRotation;
        float elapsed = 0f;
        while (elapsed < 0.2f)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / 0.2f);
            transform.localPosition = Vector3.Lerp(start, Vector3.zero, t);
            transform.localRotation = Quaternion.Slerp(rotation, Quaternion.identity, t);
            yield return null;
        }
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        motion = null;
    }

    private void LateUpdate()
    {
        if (!held || motion != null) return;
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    public void Launch(Vector3 position, Vector3 velocity, LayerMask collisionLayers,
        JourneyStoneThrowController controller)
    {
        if (!held || !initialized) return;
        if (motion != null) StopCoroutine(motion);
        motion = null;
        transform.SetParent(originalParent, true);
        transform.position = position;
        body.position = position;
        held = false;
        thrown = true;
        flightAge = 0f;
        LaunchPosition = position;
        owner = controller;
        hitCollider.enabled = true;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = velocity;
        body.angularVelocity = Random.onUnitSphere * 4f;
        body.WakeUp();
        // Collision filtering is configured in Project Settings > Physics.
        // collisionLayers remains the controller's trajectory-preview mask.
    }

    private void FixedUpdate()
    {
        if (!initialized || held) return;
        if (thrown) flightAge += Time.fixedDeltaTime;
        // Only recover unscored misses that fall out of the environment.
        if (!spent && body.position.y < originalPosition.y - resetBelowStart) ResetStone();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!thrown || spent || held || owner == null || !owner.CanContinueFlight) return;
        JourneyStoneTower tower = collision.collider.GetComponentInParent<JourneyStoneTower>();
        if (tower == null || tower != owner.Target) return;
        Vector3 hit = collision.contactCount > 0 ? collision.GetContact(0).point : body.position;
        // Mark before invoking events: multiple contacts can never award twice.
        spent = true;
        if (!tower.TryAcceptHit(this, hit, owner, out _)) spent = false;
        // Deliberately do not change position, velocity, damping or gravity on impact.
        // The rigidbody falls, bounces, rolls and sleeps naturally with its material.
    }

    public void ResetStone()
    {
        if (!initialized) return;
        if (motion != null) StopCoroutine(motion);
        motion = null;
        held = spent = thrown = false;
        owner = null;
        transform.SetParent(originalParent, true);
        body.interpolation = RigidbodyInterpolation.None;
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.isKinematic = true;
        transform.SetPositionAndRotation(originalPosition, originalRotation);
        body.position = originalPosition;
        body.rotation = originalRotation;
        hitCollider.enabled = true;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.WakeUp();
    }

    private void OnDisable() { ResetStone(); }
}
