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
    [Header("Distance bonus - horizontal launch-to-hit distance")]
    [SerializeField, Min(0.1f)] private float distancePerBonus = 5f;
    [SerializeField, Min(0)] private int bonusPointsPerStep = 5;
    [SerializeField, Min(0)] private int maximumDistanceBonus = 100;
    public float LastHitDistance { get; private set; }
    public int LastAwardedPoints { get; private set; }
    [Header("Optional effects")]
    [SerializeField] private AudioSource effectsSource;
    [SerializeField] private AudioClip hitSound;
    [SerializeField] private ParticleSystem hitParticles;
    [SerializeField] private bool lightHaptic = true;
    [Tooltip("Notification only; do not add points again if Score Receiver is assigned.")]
    [SerializeField] private UnityEvent<int> onHit = new UnityEvent<int>();
    public Vector3 AimPosition => aimPoint != null ? aimPoint.position : transform.position;

    public bool TryAcceptHit(JourneyThrowableStone stone, Vector3 hit,
        JourneyStoneThrowController owner, out Vector3 destination)
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
        LastAwardedPoints = (int)System.Math.Min(int.MaxValue, (double)Mathf.Max(0, pointsPerHit) + bonus);
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
