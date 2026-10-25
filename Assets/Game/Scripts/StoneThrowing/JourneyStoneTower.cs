using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class JourneyStoneTower : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform aimPoint;
    [Header("Score")]
    [SerializeField, Min(0)] private int pointsPerHit = 5;
    [SerializeField] private LightBrushPuzzle scoreReceiver;
    [Header("Special hit zone")]
    [SerializeField] private SphereCollider bonusHitSphere;
    [SerializeField, Min(1)] private int bonusHitMultiplier = 3;
    [Header("Distance bonus - horizontal launch-to-hit distance")]
    [SerializeField, Min(0.1f)] private float distancePerBonus = 5f;
    [SerializeField, Min(0)] private int bonusPointsPerStep = 5;
    [SerializeField, Min(0)] private int maximumDistanceBonus = 100;
    public float LastHitDistance { get; private set; }
    public int LastAwardedPoints { get; private set; }
    public int LastHitMultiplier { get; private set; } = 1;
    public SphereCollider BonusHitSphere => bonusHitSphere;
    [Header("Optional effects")]
    [SerializeField] private AudioSource effectsSource;
    [SerializeField] private AudioClip hitSound;
    [SerializeField] private ParticleSystem hitParticles;
    [SerializeField] private bool lightHaptic = true;
    [Tooltip("Notification only; do not add points again if Score Receiver is assigned.")]
    [SerializeField] private UnityEvent<int> onHit = new UnityEvent<int>();
    public Vector3 AimPosition => aimPoint != null ? aimPoint.position : transform.position;

    private void Awake()
    {
        if (bonusHitSphere == null) bonusHitSphere = GetComponent<SphereCollider>();
    }
    public bool IsBonusCollider(Collider other) => other != null && other == bonusHitSphere;
    public bool ContainsBonusPoint(Vector3 point)
    {
        if (!BonusSphereActive) return false;
        return (bonusHitSphere.ClosestPoint(point) - point).sqrMagnitude <= 0.000001f;
    }
    private bool BonusSphereActive => bonusHitSphere != null && bonusHitSphere.enabled && bonusHitSphere.gameObject.activeInHierarchy;
    // Triggers can be crossed between physics steps by a fast stone. Test the
    // travelled segment against this exact sphere without changing its physics.
    public bool TrySweepBonusSphere(Vector3 start, Vector3 end, float stoneRadius, out Vector3 point)
    {
        point = end;
        if (!isActiveAndEnabled || !BonusSphereActive) return false;
        Vector3 center = bonusHitSphere.transform.TransformPoint(bonusHitSphere.center);
        Vector3 scale = bonusHitSphere.transform.lossyScale;
        float radius = bonusHitSphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        float expanded = radius + Mathf.Max(0f, stoneRadius);
        Vector3 offset = start - center, travel = end - start;
        float c = offset.sqrMagnitude - expanded * expanded;
        float fraction = 0f;
        if (c > 0f)
        {
            float a = travel.sqrMagnitude;
            if (a <= 0.000001f) return false;
            float b = Vector3.Dot(offset, travel);
            float discriminant = b * b - a * c;
            if (discriminant < 0f) return false;
            fraction = (-b - Mathf.Sqrt(discriminant)) / a;
            if (fraction < 0f || fraction > 1f) return false;
        }
        Vector3 contact = start + travel * fraction - center;
        point = center + (contact.sqrMagnitude > 0.000001f ? contact.normalized : Vector3.up) * radius;
        return true;
    }

    public bool TryAcceptHit(JourneyThrowableStone stone, Vector3 hit,
        JourneyStoneThrowController owner, out Vector3 destination)
        => TryAcceptHit(stone, hit, owner, null, out destination);

    public bool TryAcceptHit(JourneyThrowableStone stone, Vector3 hit,
        JourneyStoneThrowController owner, Collider struckCollider, out Vector3 destination)
    {
        destination = hit;
        if (!isActiveAndEnabled || stone == null || owner == null || owner.Target != this || !stone.WasLaunchedBy(owner)) return false;
        Vector3 displacement = hit - stone.LaunchPosition;
        Vector3 horizontal = Physics.gravity.sqrMagnitude > 0.0001f
            ? Vector3.ProjectOnPlane(displacement, Physics.gravity.normalized)
            : Vector3.ProjectOnPlane(displacement, Vector3.up);
        LastHitDistance = horizontal.magnitude;
        double steps = System.Math.Floor(LastHitDistance / Mathf.Max(0.1f, distancePerBonus));
        double bonus = System.Math.Min(Mathf.Max(0, maximumDistanceBonus), steps * Mathf.Max(0, bonusPointsPerStep));
        LastHitMultiplier = IsBonusCollider(struckCollider) || ContainsBonusPoint(hit) ? Mathf.Max(1, bonusHitMultiplier) : 1;
        LastAwardedPoints = (int)System.Math.Min(int.MaxValue, ((double)Mathf.Max(0, pointsPerHit) + bonus) * LastHitMultiplier);
        if (scoreReceiver != null) scoreReceiver.AddVoicePoints(LastAwardedPoints);
        else Debug.LogWarning("JourneyStoneTower: assign Score Receiver to display points in the existing score UI.", this);
        if (effectsSource != null && hitSound != null) effectsSource.PlayOneShot(hitSound);
        if (hitParticles != null) { hitParticles.transform.position = hit; hitParticles.Play(); }
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        if (lightHaptic) CandyCoded.HapticFeedback.HapticFeedback.LightFeedback();
#endif
        onHit.Invoke(LastAwardedPoints);
        return true;
    }

}
