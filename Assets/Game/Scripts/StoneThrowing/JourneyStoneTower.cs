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
        if (!isActiveAndEnabled || stone == null || owner == null || owner.Target != this) return false;
        if (scoreReceiver != null) scoreReceiver.AddVoicePoints(pointsPerHit);
        else Debug.LogWarning("JourneyStoneTower: assign Score Receiver to display points in the existing score UI.", this);
        if (effectsSource != null && hitSound != null) effectsSource.PlayOneShot(hitSound);
        if (hitParticles != null) { hitParticles.transform.position = hit; hitParticles.Play(); }
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        if (lightHaptic) CandyCoded.HapticFeedback.HapticFeedback.LightFeedback();
#endif
        onHit.Invoke(pointsPerHit);
        return true;
    }

}
