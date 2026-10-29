using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class CardBehaviour : MonoBehaviour
{
    [Tooltip("Give the two cards showing the same picture the same ID. This board uses IDs 1 to 16.")]
    [Min(1)] public int pairId = 1;
    public bool isFlipped;
    [SerializeField, Min(0.01f)] private float flipDuration = 0.3f;
    [SerializeField] private Vector3 localFlipAxis = Vector3.up;
    public bool IsLocked { get; private set; }
    public bool IsAnimating { get; private set; }
    private Quaternion closedRotation;
    private bool poseCaptured;

    private void Awake() { CapturePose(); }
    private void CapturePose()
    {
        if (poseCaptured) return;
        closedRotation = transform.localRotation; poseCaptured = true;
    }
    private Quaternion TargetRotation(bool open)
    {
        Vector3 axis = localFlipAxis.sqrMagnitude > 0.0001f ? localFlipAxis.normalized : Vector3.up;
        return closedRotation * (open ? Quaternion.AngleAxis(180f, axis) : Quaternion.identity);
    }
    public IEnumerator Flip(bool open)
    {
        if (IsLocked || IsAnimating || isFlipped == open) yield break;
        CapturePose(); IsAnimating = true; isFlipped = open;
        Quaternion start = transform.localRotation, target = TargetRotation(open);
        float elapsed = 0f, duration = Mathf.Max(0.01f, flipDuration);
        while (elapsed < duration)
        {
            if (!isActiveAndEnabled) { CancelFlip(); yield break; }
            elapsed += Time.deltaTime;
            transform.localRotation = Quaternion.Slerp(start, target, Mathf.SmoothStep(0f, 1f, elapsed / duration));
            yield return null;
        }
        transform.localRotation = target; IsAnimating = false;
    }
    public void Lock() { IsLocked = true; }
    public void Unlock() { IsLocked = false; }
    public void ResetCard() { IsLocked = false; CancelFlip(); }
    public void CancelFlip()
    {
        CapturePose(); IsAnimating = false; isFlipped = IsLocked;
        transform.localRotation = TargetRotation(IsLocked);
    }
    private void OnDisable() { if (poseCaptured) CancelFlip(); }
}
