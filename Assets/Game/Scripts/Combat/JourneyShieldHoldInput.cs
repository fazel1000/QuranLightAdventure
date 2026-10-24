using UnityEngine;
using UnityEngine.EventSystems;

// The original shield Button retains its appearance and sound events.
[DisallowMultipleComponent]
public sealed class JourneyShieldHoldInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
    IDragHandler, IInitializePotentialDragHandler
{
    public JourneyPlayerCombat combat;
    private int pointer = int.MinValue;
    public void OnInitializePotentialDrag(PointerEventData data) { data.useDragThreshold = false; }
    public void OnPointerDown(PointerEventData data)
    {
        if (pointer != int.MinValue || data.button != PointerEventData.InputButton.Left || combat == null) return;
        if (combat.BeginDefenseHold()) pointer = data.pointerId;
    }
    public void OnDrag(PointerEventData data) { }
    public void OnPointerUp(PointerEventData data) { if (pointer == data.pointerId) Release(); }
    private void Release() { pointer = int.MinValue; if (combat != null) combat.ReleaseDefenseHold(); }
    private void OnDisable() { Release(); }
    private void OnApplicationFocus(bool value) { if (!value) Release(); }
    private void OnApplicationPause(bool value) { if (value) Release(); }
}
