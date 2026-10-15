using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class JourneyWardrobeDrag : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    public Transform target;
    public Camera previewCamera;
    [Min(1f)] public float degreesPerScreenWidth = 360f;
    private int pointer = int.MinValue;
    public void OnPointerDown(PointerEventData e)
    {
        if (pointer != int.MinValue || e.button != PointerEventData.InputButton.Left) return;
        GameObject hit = e.pointerCurrentRaycast.gameObject;
        if (hit != null && hit.GetComponentInParent<Selectable>() != null) return;
        if (previewCamera != null && !previewCamera.pixelRect.Contains(e.position)) return;
        pointer = e.pointerId;
    }
    public void OnDrag(PointerEventData e)
    {
        if (e.pointerId != pointer || target == null) return;
        float width = previewCamera != null ? previewCamera.pixelRect.width : Screen.width;
        // No angle limit: repeated swipes can rotate through any number of full turns.
        target.Rotate(0f, -e.delta.x * degreesPerScreenWidth / Mathf.Max(1f, width), 0f, Space.World);
    }
    public void OnPointerUp(PointerEventData e) { if (pointer == e.pointerId) pointer = int.MinValue; }
    private void OnDisable() { pointer = int.MinValue; }
}
