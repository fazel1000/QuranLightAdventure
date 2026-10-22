using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class JourneyRadialMenu : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
{
    [Serializable] public sealed class Item
    {
        public string name;
        public Sprite icon;
        public bool available = true;
        [Tooltip("Optional held weapon/tool to activate; other assigned equipment objects are deactivated.")]
        public GameObject equipmentObject;
        public UnityEvent onSelected = new UnityEvent();
    }
    public JourneyMenuController menu;
    public CanvasGroup menuRoot;
    public Sprite slotBackground;
    public List<Item> items = new List<Item>();
    [Min(10f)] public float ringRadius = 140f, itemSize = 72f, deadZone = 45f;
    [Min(0.01f)] public float fadeSeconds = 0.12f;
    public Color normalColor = new Color(0.3f, 0.65f, 0.8f, 1f), selectedColor = new Color(1f, 0.8f, 0.2f, 1f);
    public UnityEvent<int> onSelection = new UnityEvent<int>();
    public int SelectedIndex { get; private set; } = -1;
    public static bool IsOpen => opened != null;
    private static JourneyRadialMenu opened;
    private readonly List<Image> backgrounds = new List<Image>();
    private int pointer = int.MinValue, hovering = -1;
    private bool built;
    private CanvasGroup buttonVisibility;
    private float originalAlpha = 1f;
    private bool originalBlocks = true, originalInteractable = true;
    private bool Allowed => menu != null && menu.IsGameplayActive && Time.timeScale > 0f && !JourneyRideSession.IsOccupied &&
        !JourneyStoneThrowController.IsMovementBlocked && !LightBrushPuzzle.IsMovementBlocked && !JourneyPlayerCombat.IsMovementBlocked && !JourneyVoiceBoard.IsPointerCaptured;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { opened = null; }
    private void Awake()
    {
        if (menu == null) menu = FindFirstObjectByType<JourneyMenuController>(FindObjectsInactive.Include);
        buttonVisibility = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        originalAlpha = buttonVisibility.alpha; originalBlocks = buttonVisibility.blocksRaycasts; originalInteractable = buttonVisibility.interactable;
        if (menuRoot != null) { menuRoot.alpha = 0f; menuRoot.blocksRaycasts = false; menuRoot.gameObject.SetActive(false); }
    }
    private void Build()
    {
        if (built || menuRoot == null) return;
        for (int i = 0; i < items.Count; i++)
        {
            var go = new GameObject("Item " + (i + 1), typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform; rect.SetParent(menuRoot.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            float angle = i * Mathf.PI * 2f / Mathf.Max(1, items.Count);
            rect.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * ringRadius; rect.sizeDelta = Vector2.one * itemSize;
            var image = go.GetComponent<Image>(); image.sprite = slotBackground; image.raycastTarget = false; backgrounds.Add(image);
            if (items[i] != null && items[i].icon != null)
            {
                var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)); var iconRect = (RectTransform)icon.transform;
                iconRect.SetParent(rect, false); iconRect.anchorMin = Vector2.zero; iconRect.anchorMax = Vector2.one;
                iconRect.offsetMin = Vector2.one * 10f; iconRect.offsetMax = Vector2.one * -10f;
                var graphic = icon.GetComponent<Image>(); graphic.sprite = items[i].icon; graphic.preserveAspect = true; graphic.raycastTarget = false;
            }
        }
        built = true; Highlight(-1);
    }
    public void OnInitializePotentialDrag(PointerEventData data) { data.useDragThreshold = false; }
    public void OnPointerDown(PointerEventData data)
    {
        if (!Allowed || menuRoot == null || items.Count == 0 || pointer != int.MinValue || IsOpen || data.button != PointerEventData.InputButton.Left) return;
        Build(); pointer = data.pointerId; opened = this; hovering = -1;
        menuRoot.gameObject.SetActive(true); menuRoot.transform.SetAsLastSibling(); menuRoot.blocksRaycasts = true;
        RightScreenDragArea.ClearInput(); JourneyMovementSettings.RefreshPuzzleInput(); Highlight(-1);
    }
    public void OnDrag(PointerEventData data)
    {
        if (pointer != data.pointerId || opened != this || menuRoot == null) return;
        var rect = (RectTransform)menuRoot.transform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.pressEventCamera, out Vector2 delta)) return;
        float magnitude = delta.magnitude;
        if (magnitude < deadZone || magnitude > ringRadius + itemSize * 1.5f) { Highlight(-1); return; }
        float degrees = Mathf.Repeat(Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg, 360f);
        int index = Mathf.FloorToInt((degrees + 180f / items.Count) / (360f / items.Count)) % items.Count;
        Highlight(items[index] != null && items[index].available ? index : -1);
    }
    private void Highlight(int index)
    {
        hovering = index;
        for (int i = 0; i < backgrounds.Count; i++)
        {
            Color color = i == index ? selectedColor : normalColor;
            if (items[i] == null || !items[i].available) color.a *= 0.25f;
            backgrounds[i].color = color;
            backgrounds[i].rectTransform.localScale = Vector3.one * (i == index ? 1.15f : 1f);
        }
    }
    public void OnPointerUp(PointerEventData data)
    {
        if (pointer != data.pointerId) return;
        // Evaluate the release position as well: the last drag event may belong
        // to the previous frame, when the finger was still over another slot.
        OnDrag(data);
        int selection = Allowed ? hovering : -1;
        Close(false);
        if (selection < 0 || selection >= items.Count || items[selection] == null || !items[selection].available) return;
        SelectedIndex = selection;
        if (items[selection].equipmentObject != null)
            for (int i = 0; i < items.Count; i++) if (items[i] != null && items[i].equipmentObject != null) items[i].equipmentObject.SetActive(i == selection);
        onSelection.Invoke(selection); items[selection].onSelected.Invoke();
    }
    private void Update()
    {
        bool allowed = Allowed;
        if (opened == this && !allowed) Close(false);
        if (buttonVisibility != null)
        { buttonVisibility.alpha = allowed ? originalAlpha : 0f; buttonVisibility.blocksRaycasts = allowed && originalBlocks; buttonVisibility.interactable = allowed && originalInteractable; }
        if (menuRoot == null || !menuRoot.gameObject.activeSelf) return;
        menuRoot.alpha = Mathf.MoveTowards(menuRoot.alpha, opened == this ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
        if (opened != this && menuRoot.alpha <= 0f) menuRoot.gameObject.SetActive(false);
    }
    private void Close(bool immediate)
    {
        pointer = int.MinValue; hovering = -1;
        if (opened == this) { opened = null; RightScreenDragArea.ClearInput(); JourneyMovementSettings.RefreshPuzzleInput(); }
        if (menuRoot == null) return;
        menuRoot.blocksRaycasts = false;
        if (immediate) { menuRoot.alpha = 0f; menuRoot.gameObject.SetActive(false); }
    }
    private void OnApplicationFocus(bool value) { if (!value) Close(true); }
    private void OnApplicationPause(bool value) { if (value) Close(true); }
    private void OnDisable()
    {
        Close(true);
        if (buttonVisibility != null) { buttonVisibility.alpha = originalAlpha; buttonVisibility.blocksRaycasts = originalBlocks; buttonVisibility.interactable = originalInteractable; }
    }
}
