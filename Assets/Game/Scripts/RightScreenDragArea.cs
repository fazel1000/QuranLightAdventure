using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class RightScreenDragArea : MonoBehaviour,
    IPointerDownHandler,
    IDragHandler,
    IPointerUpHandler,
    IInitializePotentialDragHandler
{
    private const int NoPointer = int.MinValue;

    private static Vector2 accumulatedDelta;
    private static int activePointerId = NoPointer;

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        eventData.useDragThreshold = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (activePointerId != NoPointer) return;

        activePointerId = eventData.pointerId;
        accumulatedDelta = Vector2.zero;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != activePointerId) return;
        accumulatedDelta += eventData.delta;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId != activePointerId) return;
        activePointerId = NoPointer;
    }

    private void OnDisable()
    {
        ClearInput();
    }

    public static Vector2 ConsumeDelta()
    {
        Vector2 result = accumulatedDelta;
        accumulatedDelta = Vector2.zero;
        return result;
    }

    public static void ClearInput()
    {
        activePointerId = NoPointer;
        accumulatedDelta = Vector2.zero;
    }
}
