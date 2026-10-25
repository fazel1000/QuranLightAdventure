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
    [Tooltip("Destroy any unheld stone below this WORLD Y, including scored stones.")]
    [SerializeField] private float destroyBelowY = -100f;
    private bool destroying;
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
    private Vector3 previousPhysicsCenter;
    private Vector3 PhysicsCenter => hitCollider is SphereCollider sphere
        ? sphere.transform.TransformPoint(sphere.center) : hitCollider.bounds.center;
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
                return sphere.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
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
        previousPhysicsCenter = PhysicsCenter;
        // Collision filtering is configured in Project Settings > Physics.
        // collisionLayers remains the controller's trajectory-preview mask.
    }

    private void FixedUpdate()
    {
        if (!initialized || held) return;
        if (thrown)
        {
            flightAge += Time.fixedDeltaTime;
            if (!spent && owner != null && owner.CanContinueFlight && owner.Target != null &&
                owner.Target.TrySweepBonusSphere(previousPhysicsCenter, PhysicsCenter, Radius, out Vector3 bonusPoint))
                TryScoreHit(owner.Target.BonusHitSphere, bonusPoint);
        }
        previousPhysicsCenter = PhysicsCenter;
        if (body.position.y < destroyBelowY)
        {
            destroying = true;
            Destroy(gameObject);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        Vector3 hit = collision.contactCount > 0 ? collision.GetContact(0).point : body.position;
        TryScoreHit(collision.collider, hit);
        // Keep the rigidbody's natural bounce, roll and gravity after scoring.
    }
    private void OnTriggerEnter(Collider other)
    {
        if (owner == null || owner.Target == null || !owner.Target.IsBonusCollider(other)) return;
        TryScoreHit(other, other.ClosestPoint(PhysicsCenter));
    }
    private void TryScoreHit(Collider other, Vector3 hit)
    {
        if (!thrown || spent || held || owner == null || !owner.CanContinueFlight) return;
        JourneyStoneTower tower = other != null ? other.GetComponentInParent<JourneyStoneTower>() : null;
        if (tower == null || tower != owner.Target) return;
        // Give the bonus priority when trigger and solid contact callbacks
        // arrive in the opposite order during the same simulation step.
        if (!tower.IsBonusCollider(other) && !tower.ContainsBonusPoint(hit) &&
            tower.TrySweepBonusSphere(previousPhysicsCenter, PhysicsCenter, Radius, out Vector3 bonusPoint))
        { other = tower.BonusHitSphere; hit = bonusPoint; }
        // Mark before invoking events: multiple contacts can never award twice.
        spent = true;
        if (!tower.TryAcceptHit(this, hit, owner, other, out _)) spent = false;
    }

    public void ResetStone()
    {
        if (!initialized || destroying) return;
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
