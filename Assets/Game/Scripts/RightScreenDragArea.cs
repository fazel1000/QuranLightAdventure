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
    private static Object reservedReader;
    private static RightScreenDragArea activeArea;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { reservedReader = null; ClearInput(); }

    // A disabled GC camera can still have an input provider updating elsewhere.
    // Reserve the delta so only the occupied vehicle consumes that drag.
    public static void Capture(Object reader)
    {
        if (reader == null || reservedReader == reader) return;
        reservedReader = reader; ClearInput();
    }

    public static void Release(Object reader)
    {
        if (reservedReader != reader) return;
        reservedReader = null; ClearInput();
    }

    public void OnInitializePotentialDrag(PointerEventData eventData)
    {
        eventData.useDragThreshold = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (LightBrushPuzzle.IsMovementBlocked || JourneyRadialMenu.IsOpen)
        {
            ClearInput();
            return;
        }

        if (activePointerId != NoPointer) return;

        activePointerId = eventData.pointerId;
        activeArea = this;
        accumulatedDelta = Vector2.zero;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (LightBrushPuzzle.IsMovementBlocked || JourneyRadialMenu.IsOpen)
        {
            ClearInput();
            return;
        }

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
        if (activeArea == this) ClearInput();
    }

    public static Vector2 ConsumeDelta(Object reader = null)
    {
        if (reservedReader != null && reservedReader != reader) return Vector2.zero;
        if (LightBrushPuzzle.IsMovementBlocked || JourneyRadialMenu.IsOpen)
        {
            ClearInput();
            return Vector2.zero;
        }

        Vector2 result = accumulatedDelta;
        accumulatedDelta = Vector2.zero;
        return result;
    }

    public static void ClearInput()
    {
        activePointerId = NoPointer;
        activeArea = null;
        accumulatedDelta = Vector2.zero;
    }
}
