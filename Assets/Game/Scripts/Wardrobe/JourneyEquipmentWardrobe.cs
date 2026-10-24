using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class JourneyEquipmentWardrobe : MonoBehaviour
{
    public JourneyWardrobeRoom room;
    public JourneyEquipmentInventory inventory;
    public GameObject clothingRoot, equipmentRoot;
    public Button clothesTab, equipmentTab;
    public Button[] categoryButtons = new Button[4];
    public RectTransform itemList;
    public GameObject cardTemplate;
    public TMP_Text selectedNameText, balanceText, statusText, actionText;
    public Button actionButton;
    public JourneyEquipmentPreview equipmentPreview;
    public Color selectedColor = new Color(0.95f, 0.72f, 0.2f, 1f);
    public Color normalColor = new Color(0.13f, 0.22f, 0.3f, 1f);
    private sealed class Card
    { public JourneyEquipmentInventory.Item item; public GameObject root; public Image background, icon; public TMP_Text name, price; }
    private readonly List<Card> cards = new List<Card>();
    private readonly List<GameObject> characterEquipment = new List<GameObject>();
    private readonly List<Mesh> previewMeshes = new List<Mesh>();
    private JourneyEquipmentInventory.Category category;
    private JourneyEquipmentInventory.Item selected;
    private bool equipmentMode, wasOpen;
    private void OnEnable()
    {
        Listen(clothesTab, ShowClothes, true); Listen(equipmentTab, ShowEquipment, true);
        Listen(actionButton, PurchaseOrEquip, true);
        if (categoryButtons.Length > 0) Listen(categoryButtons[0], ShowSwords, true);
        if (categoryButtons.Length > 1) Listen(categoryButtons[1], ShowShields, true);
        if (categoryButtons.Length > 2) Listen(categoryButtons[2], ShowArmor, true);
        if (categoryButtons.Length > 3) Listen(categoryButtons[3], ShowScuba, true);
        JourneyCoinWallet.Changed += Refresh; JourneyLocalization.Changed += Refresh;
        if (inventory != null) inventory.Changed += EquipmentChanged;
    }
    private static void Listen(Button button, UnityEngine.Events.UnityAction action, bool add)
    { if (button == null) return; button.onClick.RemoveListener(action); if (add) button.onClick.AddListener(action); }
    private void Update()
    {
        bool open = room != null && room.IsOpen;
        if (open == wasOpen) return;
        wasOpen = open;
        if (open) { ShowClothes(); RefreshCharacterEquipment(); }
        else { if (equipmentPreview != null) equipmentPreview.Hide(); ClearCharacterEquipment(); }
    }
    public void ShowClothes()
    {
        equipmentMode = false;
        if (clothingRoot != null) clothingRoot.SetActive(true);
        if (equipmentRoot != null) equipmentRoot.SetActive(false);
        if (equipmentPreview != null) equipmentPreview.Hide();
        Refresh();
    }
    public void ShowEquipment()
    {
        equipmentMode = true;
        if (clothingRoot != null) clothingRoot.SetActive(false);
        if (equipmentRoot != null) equipmentRoot.SetActive(true);
        ShowCategory(category);
    }
    private void ShowSwords() { ShowCategory(JourneyEquipmentInventory.Category.Sword); }
    private void ShowShields() { ShowCategory(JourneyEquipmentInventory.Category.Shield); }
    private void ShowArmor() { ShowCategory(JourneyEquipmentInventory.Category.Armor); }
    private void ShowScuba() { ShowCategory(JourneyEquipmentInventory.Category.Scuba); }
    private void ShowCategory(JourneyEquipmentInventory.Category value)
    {
        category = value;
        foreach (var card in cards) if (card.root != null) { card.root.SetActive(false); Destroy(card.root); }
        cards.Clear(); selected = null;
        if (inventory == null || itemList == null || cardTemplate == null) { Refresh(); return; }
        foreach (var item in inventory.items)
        {
            if (item == null || item.category != category) continue;
            var go = Instantiate(cardTemplate, itemList); go.name = "Equipment " + item.id; go.SetActive(true);
            var button = go.GetComponent<Button>(); string id = item.id;
            if (button != null) button.onClick.AddListener(() => SelectItem(id));
            cards.Add(new Card { item = item, root = go, background = go.GetComponent<Image>(),
                icon = go.transform.Find("Icon")?.GetComponent<Image>(), name = go.transform.Find("Name")?.GetComponent<TMP_Text>(),
                price = go.transform.Find("Price")?.GetComponent<TMP_Text>() });
            if (selected == null || inventory.IsEquipped(item)) selected = item;
        }
        Refresh();
        if (equipmentPreview != null) equipmentPreview.Show(selected);
    }
    private void SelectItem(string id)
    {
        selected = inventory != null ? inventory.Find(id) : null; Refresh();
        if (equipmentPreview != null) equipmentPreview.Show(selected);
    }
    private void PurchaseOrEquip()
    {
        if (inventory == null || selected == null) return;
        if (inventory.Owns(selected) && inventory.IsEquipped(selected) && selected.category != JourneyEquipmentInventory.Category.Sword)
            inventory.Unequip(selected.category);
        else inventory.TryPurchaseAndEquip(selected.id);
        Refresh();
    }
    private void EquipmentChanged() { Refresh(); if (room != null && room.IsOpen) RefreshCharacterEquipment(); }
    private void Refresh()
    {
        Write(balanceText, Text("سکه: ", "Coins: ", "العملات: ") + JourneyCoinWallet.Format(JourneyCoinWallet.Balance));
        Paint(clothesTab, !equipmentMode); Paint(equipmentTab, equipmentMode);
        for (int i = 0; i < categoryButtons.Length; i++)
        {
            if (categoryButtons[i] == null) continue;
            bool exists = inventory != null && inventory.items.Exists(item => item != null && (int)item.category == i);
            categoryButtons[i].gameObject.SetActive(exists); Paint(categoryButtons[i], (int)category == i);
        }
        foreach (var card in cards)
        {
            bool owned = inventory.Owns(card.item);
            if (card.background != null) card.background.color = card.item == selected ? selectedColor : normalColor;
            if (card.icon != null)
            { card.icon.sprite = card.item.icon; card.icon.color = owned ? Color.white : new Color(0.7f, 0.7f, 0.7f, 0.65f); card.icon.gameObject.SetActive(card.item.icon != null); }
            Write(card.name, card.item.Name);
            Write(card.price, owned ? inventory.IsEquipped(card.item) ? Text("انتخاب‌شده", "Equipped", "مُجهّز") : Text("خریداری‌شده", "Owned", "مملوك") :
                Text("قفل • ", "Locked • ", "مقفل • ") + Price(card.item.price));
        }
        Write(selectedNameText, selected != null ? selected.Name : Text("تجهیزات", "Equipment", "المعدات"));
        if (selected == null)
        {
            Write(statusText, Text("هنوز آیتمی در این بخش نیست", "No items in this category yet", "لا توجد عناصر في هذا القسم بعد"));
            Write(actionText, ""); if (actionButton != null) actionButton.interactable = false; return;
        }
        bool has = inventory.Owns(selected), equipped = inventory.IsEquipped(selected);
        Write(statusText, has ? equipped ? Text("در حال استفاده", "Currently equipped", "قيد الاستخدام") : Text("آمادهٔ انتخاب", "Ready to equip", "جاهز للتجهيز") :
            Text("قیمت: ", "Price: ", "السعر: ") + Price(selected.price));
        Write(actionText, !has ? Text("خرید و انتخاب — ", "Buy & equip — ", "شراء وتجهيز — ") + Price(selected.price) :
            equipped ? selected.category == JourneyEquipmentInventory.Category.Sword ? Text("انتخاب‌شده", "Equipped", "مُجهّز") : Text("برداشتن", "Unequip", "إزالة") : Text("انتخاب", "Equip", "تجهيز"));
        if (actionButton != null) actionButton.interactable = has ? !equipped || selected.category != JourneyEquipmentInventory.Category.Sword : JourneyCoinWallet.Balance >= selected.price;
    }
    private static string Price(int price) => JourneyCoinWallet.Format(price) + Text(" سکه", " coins", " عملة");
    private static string Text(string fa, string en, string ar) => JourneyLocalization.Choose(fa, en, ar);
    private static void Write(TMP_Text label, string value) { JourneyLocalizedText.Write(label, value); }
    private void Paint(Button button, bool selectedState)
    {
        if (button == null) return;
        var colors = button.colors; colors.normalColor = selectedState ? selectedColor : normalColor;
        colors.selectedColor = colors.normalColor; button.colors = colors;
    }
    private void RefreshCharacterEquipment()
    {
        ClearCharacterEquipment();
        if (inventory == null || room == null || room.preview == null) return;
        var animator = room.preview.GetComponentInChildren<Animator>(true);
        if (animator == null || !animator.isHuman) return;
        foreach (var item in inventory.items)
        {
            if (item == null || !inventory.IsEquipped(item) || !inventory.Owns(item)) continue;
            foreach (var attachment in item.attachments)
            {
                if (attachment == null || attachment.root == null) continue;
                Transform bone = animator.GetBoneTransform(attachment.bone); if (bone == null) continue;
                var copy = JourneyEquipmentVisuals.Copy(attachment.root.gameObject, bone, room.preview.gameObject.layer, previewMeshes);
                copy.transform.SetLocalPositionAndRotation(attachment.localPosition, Quaternion.Euler(attachment.localEulerAngles));
                copy.transform.localScale = attachment.localScale; characterEquipment.Add(copy);
            }
        }
        room.RefitPreviewCameraPreserveAngle();
    }
    private void ClearCharacterEquipment()
    {
        foreach (var item in characterEquipment) if (item != null) { item.SetActive(false); Destroy(item); }
        characterEquipment.Clear(); JourneyEquipmentVisuals.ReleaseMeshes(previewMeshes);
    }
    private void OnDisable()
    {
        Listen(clothesTab, ShowClothes, false); Listen(equipmentTab, ShowEquipment, false); Listen(actionButton, PurchaseOrEquip, false);
        if (categoryButtons.Length > 0) Listen(categoryButtons[0], ShowSwords, false);
        if (categoryButtons.Length > 1) Listen(categoryButtons[1], ShowShields, false);
        if (categoryButtons.Length > 2) Listen(categoryButtons[2], ShowArmor, false);
        if (categoryButtons.Length > 3) Listen(categoryButtons[3], ShowScuba, false);
        JourneyCoinWallet.Changed -= Refresh; JourneyLocalization.Changed -= Refresh;
        if (inventory != null) inventory.Changed -= EquipmentChanged;
        if (equipmentPreview != null) equipmentPreview.Hide(); ClearCharacterEquipment(); wasOpen = false;
    }
}
