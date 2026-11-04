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
    [Tooltip("Selecting the currently equipped item again removes it. Selecting another owned item equips that item.")]
    public bool toggleEquippedItems = true;
    [Tooltip("Allow holding and dragging the left mouse button in the Unity Editor and desktop builds. Touch input remains enabled.")]
    public bool enableDesktopMouseInput = true;
    [Header("Background slots")]
    [Tooltip("Number of holes around the background. Empty or unowned slots keep their positions.")]
    [Min(1)] public int slotCount = 10;
    [Tooltip("Clockwise angle from the top hole, in degrees.")]
    public float slotAngleOffset;
    [Tooltip("Scale the ring with Menu Root. Ring Radius is measured on a 440 x 440 reference rectangle.")]
    public bool fitRingToMenu = true;
    [Min(10f)] public float ringRadius = 140f, itemSize = 72f, deadZone = 45f;
    [Min(0.01f)] public float fadeSeconds = 0.12f;
    public Color normalColor = new Color(0.3f, 0.65f, 0.8f, 1f), selectedColor = new Color(1f, 0.8f, 0.2f, 1f);
    public UnityEvent<int> onSelection = new UnityEvent<int>();
    public int SelectedIndex { get; private set; } = -1;
    public static bool IsOpen => opened != null;
    private static JourneyRadialMenu opened;
    private readonly List<Image> backgrounds = new List<Image>();
    private readonly List<Image> icons = new List<Image>();
    // Scene pickups own these entries; they never replace or equip catalog items.
    private readonly Dictionary<UnityEngine.Object, Item> runtimeItems = new Dictionary<UnityEngine.Object, Item>();
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
    public void SetRuntimeItem(UnityEngine.Object owner, string label, Sprite icon, bool visible, UnityAction selected)
    {
        if (owner == null) return;
        runtimeItems.TryGetValue(owner, out Item item);
        if (!visible)
        {
            if (item == null) return;
            if (opened == this) Close(true);
            items.Remove(item); runtimeItems.Remove(owner); SelectedIndex = -1;
            return;
        }
        if (item == null)
        {
            if (opened == this) Close(true);
            item = new Item(); runtimeItems.Add(owner, item); items.Add(item);
        }
        item.name = label; item.icon = icon; item.available = true;
        item.onSelected.RemoveAllListeners();
        if (selected != null) item.onSelected.AddListener(selected);
    }
    private int SlotCount => Mathf.Max(1, slotCount);
    private Vector2 RingRadii
    {
        get
        {
            if (!fitRingToMenu || menuRoot == null) return Vector2.one * Mathf.Max(10f, ringRadius);
            Vector2 size = ((RectTransform)menuRoot.transform).rect.size;
            return new Vector2(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y)) * (Mathf.Max(10f, ringRadius) / 440f);
        }
    }
    private void LayoutSlots()
    {
        Vector2 radii = RingRadii;
        for (int i = 0; i < backgrounds.Count; i++)
        {
            float angle = (slotAngleOffset + i * 360f / SlotCount) * Mathf.Deg2Rad;
            var rect = backgrounds[i].rectTransform;
            rect.anchoredPosition = new Vector2(Mathf.Sin(angle) * radii.x, Mathf.Cos(angle) * radii.y);
            rect.sizeDelta = Vector2.one * itemSize;
        }
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
            rect.sizeDelta = Vector2.one * itemSize;
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
        built = true; LayoutSlots(); Highlight(-1);
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
        Build(); LayoutSlots(); pointer = data.pointerId; opened = this; hovering = -1;
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
        // Use the same ellipse and fixed hole count for drawing and touch selection.
        Vector2 radii = RingRadii;
        Vector2 unit = new Vector2(delta.x / radii.x, delta.y / radii.y);
        float referenceDistance = unit.magnitude * Mathf.Max(10f, ringRadius);
        if (referenceDistance < deadZone || referenceDistance > ringRadius + itemSize * 1.5f)
        { Highlight(-1); return; }
        float degrees = Mathf.Repeat(Mathf.Atan2(unit.x, unit.y) * Mathf.Rad2Deg - slotAngleOffset, 360f);
        int index = Mathf.FloorToInt((degrees + 180f / SlotCount) / (360f / SlotCount)) % SlotCount;
        Highlight(index < items.Count && Available(items[index]) ? index : -1);
    }
    private void Highlight(int index)
    {
        hovering = index;
        for (int i = 0; i < backgrounds.Count; i++)
        {
            // Hide the complete slot, including its icon, until the item is owned.
            // Keep its index reserved so buying equipment never shifts other icons.
            bool visible = i < SlotCount && Available(items[i]);
            backgrounds[i].gameObject.SetActive(visible);
            Color color = i == index ? selectedColor : normalColor;
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
        if (catalogItem != null && !(toggleEquippedItems ? equipmentInventory.ToggleEquip(catalogItem.id) : equipmentInventory.TryEquip(catalogItem.id))) return;
        SelectedIndex = selection;
        if (catalogItem == null && string.IsNullOrEmpty(chosen.equipmentId) && chosen.equipmentObject != null)
        {
            bool remove = toggleEquippedItems && chosen.equipmentObject.activeSelf;
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null && string.IsNullOrEmpty(items[i].equipmentId) && items[i].equipmentObject != null && items[i].equipmentGroup == chosen.equipmentGroup)
                    items[i].equipmentObject.SetActive(i == selection && !remove);
        }
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
        if (opened == this)
        {
            LayoutSlots();
            Highlight(hovering >= 0 && hovering < items.Count && Available(items[hovering]) ? hovering : -1);
        }
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
