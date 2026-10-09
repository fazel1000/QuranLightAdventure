using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class JourneyVehiclePedal : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    private readonly HashSet<int> pointers = new HashSet<int>();
    private Selectable selectable;
    public bool Held => isActiveAndEnabled && gameObject.activeInHierarchy &&
        (selectable == null || selectable.IsInteractable()) && pointers.Count > 0;
    private void Awake() { selectable = GetComponent<Selectable>(); }
    public void OnPointerDown(PointerEventData data)
    { if (data.button == PointerEventData.InputButton.Left) pointers.Add(data.pointerId); }
    public void OnPointerUp(PointerEventData data) { pointers.Remove(data.pointerId); }
    public void OnPointerExit(PointerEventData data) { pointers.Remove(data.pointerId); }
    public void ResetInput() { pointers.Clear(); }
    private void OnDisable() { ResetInput(); }
    private void OnApplicationFocus(bool focus) { if (!focus) ResetInput(); }
    private void OnApplicationPause(bool pause) { if (pause) ResetInput(); }
}
