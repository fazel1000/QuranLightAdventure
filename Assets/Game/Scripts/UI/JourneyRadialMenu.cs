using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent, DefaultExecutionOrder(-300)]
public sealed class JourneyRadialMenu : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
{
    [Serializable] public sealed class Item
    {
        public string name;
        public Sprite icon;
        public bool available = true;
        [Tooltip("Optional catalog ID. Only owned items can be selected; equips the item's own category without removing other categories.")]
        public string equipmentId;
        [Tooltip("Legacy Equipment Object entries only switch objects in the same group.")]
        public string equipmentGroup = "Held Item";
        [Tooltip("Optional held weapon/tool to activate; other assigned equipment objects are deactivated.")]
        public GameObject equipmentObject;
        public UnityEvent onSelected = new UnityEvent();
    }
    public JourneyMenuController menu;
    public JourneyEquipmentInventory equipmentInventory;
    public CanvasGroup menuRoot;
    public Sprite slotBackground;
    public List<Item> items = new List<Item>();
    [Tooltip("Allow holding and dragging the left mouse button in the Unity Editor and desktop builds. Touch input remains enabled.")]
    public bool enableDesktopMouseInput = true;
    [Min(10f)] public float ringRadius = 140f, itemSize = 72f, deadZone = 45f;
    [Min(0.01f)] public float fadeSeconds = 0.12f;
    public Color normalColor = new Color(0.3f, 0.65f, 0.8f, 1f), selectedColor = new Color(1f, 0.8f, 0.2f, 1f);
    public UnityEvent<int> onSelection = new UnityEvent<int>();
    public int SelectedIndex { get; private set; } = -1;
    public static bool IsOpen => opened != null;
    private static JourneyRadialMenu opened;
    private readonly List<Image> backgrounds = new List<Image>();
    private readonly List<Image> icons = new List<Image>();
    private readonly List<RaycastResult> mouseHits = new List<RaycastResult>();
    private PointerEventData mousePointer;
    private bool mouseFallbackHeld;
    private const int MouseFallbackPointer = int.MinValue + 1;
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
        if (built && backgrounds.Count != items.Count)
        {
            foreach (var image in backgrounds) if (image != null) { image.gameObject.SetActive(false); Destroy(image.gameObject); }
            backgrounds.Clear(); icons.Clear(); built = false;
        }
        if (built || menuRoot == null) return;
        for (int i = 0; i < items.Count; i++)
        {
            var go = new GameObject("Item " + (i + 1), typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform; rect.SetParent(menuRoot.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            float angle = i * Mathf.PI * 2f / Mathf.Max(1, items.Count);
            rect.anchoredPosition = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * ringRadius; rect.sizeDelta = Vector2.one * itemSize;
            var image = go.GetComponent<Image>(); image.sprite = slotBackground; image.raycastTarget = false; backgrounds.Add(image);
            if (items[i] != null)
            {
                var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)); var iconRect = (RectTransform)icon.transform;
                iconRect.SetParent(rect, false); iconRect.anchorMin = Vector2.zero; iconRect.anchorMax = Vector2.one;
                iconRect.offsetMin = Vector2.one * 10f; iconRect.offsetMax = Vector2.one * -10f;
                var graphic = icon.GetComponent<Image>(); graphic.sprite = IconFor(items[i]); graphic.preserveAspect = true; graphic.raycastTarget = false;
                graphic.gameObject.SetActive(graphic.sprite != null); icons.Add(graphic);
            }
            else icons.Add(null);
        }
        built = true; Highlight(-1);
    }
    private bool Available(Item item)
    {
        if (item == null || !item.available) return false;
        var catalogItem = CatalogItem(item);
        if (catalogItem != null) return equipmentInventory.Owns(catalogItem);
        return string.IsNullOrEmpty(item.equipmentId);
    }
    private JourneyEquipmentInventory.Item CatalogItem(Item item)
    {
        if (item == null || equipmentInventory == null) return null;
        if (!string.IsNullOrEmpty(item.equipmentId)) return equipmentInventory.Find(item.equipmentId);
        if (item.equipmentObject == null) return null;
        return equipmentInventory.items.Find(entry => entry != null && entry.attachments.Exists(attachment =>
            attachment != null && attachment.root != null && (attachment.root.gameObject == item.equipmentObject || item.equipmentObject.transform.IsChildOf(attachment.root))));
    }
    private Sprite IconFor(Item item)
    {
        if (item == null) return null;
        if (item.icon != null) return item.icon;
        var catalogItem = CatalogItem(item);
        return catalogItem != null ? catalogItem.icon : null;
    }
    public void OnInitializePotentialDrag(PointerEventData data) { data.useDragThreshold = false; }
    public void OnPointerDown(PointerEventData data)
    {
        if (!Allowed || menuRoot == null || items.Count == 0 || pointer != int.MinValue || IsOpen || data.button != PointerEventData.InputButton.Left) return;
        Build(); pointer = data.pointerId; opened = this; hovering = -1;
        for (int i = 0; i < icons.Count; i++) if (icons[i] != null)
        { icons[i].sprite = IconFor(items[i]); icons[i].gameObject.SetActive(icons[i].sprite != null); }
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
        Highlight(Available(items[index]) ? index : -1);
    }
    private void Highlight(int index)
    {
        hovering = index;
        for (int i = 0; i < backgrounds.Count; i++)
        {
            Color color = i == index ? selectedColor : normalColor;
            if (!Available(items[i])) color.a *= 0.25f;
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
        if (selection < 0 || selection >= items.Count || !Available(items[selection])) return;
        Item chosen = items[selection];
        var catalogItem = CatalogItem(chosen);
        if (catalogItem != null && !equipmentInventory.TryEquip(catalogItem.id)) return;
        SelectedIndex = selection;
        if (catalogItem == null && string.IsNullOrEmpty(chosen.equipmentId) && chosen.equipmentObject != null)
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null && string.IsNullOrEmpty(items[i].equipmentId) && items[i].equipmentObject != null && items[i].equipmentGroup == chosen.equipmentGroup)
                    items[i].equipmentObject.SetActive(i == selection);
        onSelection.Invoke(selection); items[selection].onSelected.Invoke();
    }
    private void Update()
    {
        ReadDesktopMouse();
        bool allowed = Allowed;
        if (opened == this && !allowed) Close(false);
        if (buttonVisibility != null)
        { buttonVisibility.alpha = allowed ? originalAlpha : 0f; buttonVisibility.blocksRaycasts = allowed && originalBlocks; buttonVisibility.interactable = allowed && originalInteractable; }
        if (menuRoot == null || !menuRoot.gameObject.activeSelf) return;
        menuRoot.alpha = Mathf.MoveTowards(menuRoot.alpha, opened == this ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeSeconds));
        if (opened != this && menuRoot.alpha <= 0f) menuRoot.gameObject.SetActive(false);
    }
    private void ReadDesktopMouse()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        var mouse = Mouse.current;
        if (!enableDesktopMouseInput || mouse == null || !Application.isFocused)
        { if (mouseFallbackHeld) Close(true); return; }
        if (mouseFallbackHeld)
        {
            mousePointer.position = mouse.position.ReadValue();
            if (mouse.leftButton.isPressed) OnDrag(mousePointer);
            else OnPointerUp(mousePointer);
            return;
        }
        if (!mouse.leftButton.wasPressedThisFrame || pointer != int.MinValue || IsOpen || !Allowed || EventSystem.current == null) return;
        // Use the actual UI hit order, so another panel covering the button
        // cannot open the radial menu. Both input paths share the same handlers.
        mousePointer = new PointerEventData(EventSystem.current)
        {
            pointerId = MouseFallbackPointer,
            button = PointerEventData.InputButton.Left,
            position = mouse.position.ReadValue(),
            pressPosition = mouse.position.ReadValue()
        };
        mouseHits.Clear(); EventSystem.current.RaycastAll(mousePointer, mouseHits);
        foreach (var hit in mouseHits)
        {
            if (!(hit.module is GraphicRaycaster)) continue;
            if (hit.gameObject != null && hit.gameObject.transform.IsChildOf(transform))
            {
                mousePointer.pointerPressRaycast = hit;
                mousePointer.pointerCurrentRaycast = hit;
                OnPointerDown(mousePointer);
                mouseFallbackHeld = opened == this && pointer == MouseFallbackPointer;
            }
            break;
        }
#endif
    }
    private void Close(bool immediate)
    {
        pointer = int.MinValue; hovering = -1; mouseFallbackHeld = false;
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
