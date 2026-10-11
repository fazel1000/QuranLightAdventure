using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class JourneyVehiclePedal : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    private readonly HashSet<int> pointers = new HashSet<int>();
    private Selectable selectable;
    private bool pressQueued;
    public bool ConsumePress()
    {
        bool pressed = pressQueued && isActiveAndEnabled && gameObject.activeInHierarchy &&
            (selectable == null || selectable.IsInteractable());
        pressQueued = false;
        return pressed;
    }
    public bool Held => isActiveAndEnabled && gameObject.activeInHierarchy &&
        (selectable == null || selectable.IsInteractable()) && pointers.Count > 0;
    private void Awake() { selectable = GetComponent<Selectable>(); }
    public void OnPointerDown(PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left ||
            (selectable != null && !selectable.IsInteractable())) return;
        if (pointers.Add(data.pointerId)) pressQueued = true;
    }
    public void OnPointerUp(PointerEventData data) { pointers.Remove(data.pointerId); }
    public void OnPointerExit(PointerEventData data) { pointers.Remove(data.pointerId); }
    public void ResetInput() { pointers.Clear(); pressQueued = false; }
    private void OnDisable() { ResetInput(); }
    private void OnApplicationFocus(bool focus) { if (!focus) ResetInput(); }
    private void OnApplicationPause(bool pause) { if (pause) ResetInput(); }
}
