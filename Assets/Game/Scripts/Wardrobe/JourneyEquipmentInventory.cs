using System;
using System.Collections.Generic;
using GameCreator.Runtime.Characters;
using UnityEngine;

[DisallowMultipleComponent, DefaultExecutionOrder(-400)]
public sealed class JourneyEquipmentInventory : MonoBehaviour
{
    public enum Category { Sword, Shield, Armor, Scuba }
    [Serializable] public sealed class Attachment
    {
        public Transform root;
        public HumanBodyBones bone = HumanBodyBones.RightHand;
        public Vector3 localPosition, localEulerAngles;
        public Vector3 localScale = Vector3.one;
    }
    [Serializable] public sealed class Item
    {
        [Tooltip("Keep this ID unchanged after players have bought the item.")]
        public string id;
        public string displayName;
        public string englishName, arabicName;
        public Category category;
        [Min(0)] public int price;
        public Sprite icon;
        [Tooltip("Optional 3D source for the shop preview; never activated as gameplay equipment.")]
        public GameObject previewModel;
        public Vector3 previewEulerAngles;
        public bool equippedByDefault;
        public List<Attachment> attachments = new List<Attachment>();
        public BoxCollider shieldCollider;
        public string Name => JourneyLocalization.Choose(displayName, englishName, arabicName);
    }
    public Character player;
    public JourneyPlayerCombat combat;
    public JourneyWardrobeRoom wardrobe;
    [Header("Carrying and throwing stones")]
    public JourneyStoneThrowController stoneController;
    public bool hideSwordWhileUsingStone = true;
    [Tooltip("Use the local position, rotation and scale authored on each equipment root under its Humanoid bone when play starts. Disable to use the offsets stored in Attachments instead.")]
    public bool readAttachmentPosesFromScene = true;
    public List<Item> items = new List<Item>();
    public event Action Changed;
    private readonly Dictionary<Category, string> equipped = new Dictionary<Category, string>();
    private Animator bound;
    private bool initialized, scenePosesCaptured, swordHiddenForStone;
    private const string SelectedKey = "JourneyOfLight.Equipment.Selected.";
    public bool HasShieldItems => items.Exists(item => item != null && item.category == Category.Shield);
    public BoxCollider EquippedShieldCollider
    {
        get { Initialize(); var item = items.Find(candidate => candidate != null && candidate.category == Category.Shield && IsEquipped(candidate) && Owns(candidate)); return item != null ? item.shieldCollider : null; }
    }
    private void Awake()
    {
        if (player == null) player = GetComponent<Character>();
        if (combat == null) combat = GetComponent<JourneyPlayerCombat>();
        if (stoneController == null) stoneController = FindFirstObjectByType<JourneyStoneThrowController>(FindObjectsInactive.Include);
        CaptureSceneAttachmentPoses();
        if (player != null)
        { player.EventBeforeChangeModel += BeforeModelChange; player.EventAfterChangeModel += RefreshBindings; }
    }
    private void Start() { Initialize(); RefreshBindings(); }
    private void Initialize()
    {
        if (initialized) return;
        initialized = true;
        foreach (Category category in Enum.GetValues(typeof(Category)))
        {
            string id = PlayerPrefs.GetString(SelectedKey + category, "");
            Item selected = Find(id);
            // An explicitly saved empty choice means the player removed that equipment.
            if (!PlayerPrefs.HasKey(SelectedKey + category) || !string.IsNullOrEmpty(id) && (selected == null || !Owns(selected)))
                selected = items.Find(item => item != null && item.category == category && item.equippedByDefault && Owns(item));
            if (selected != null && (selected.category != category || !Owns(selected))) selected = null;
            equipped[category] = selected != null ? selected.id : "";
        }
    }
    public Item Find(string id) => string.IsNullOrEmpty(id) ? null : items.Find(item => item != null && item.id == id);
    public bool Owns(Item item) => item != null && JourneyCoinWallet.IsOwned(item.id, item.price);
    public bool IsEquipped(Item item)
    { Initialize(); return item != null && equipped.TryGetValue(item.category, out string id) && id == item.id; }
    public bool TryPurchaseAndEquip(string id)
    {
        Item item = Find(id);
        if (item == null || !JourneyCoinWallet.TryPurchase(item.id, item.price)) return false;
        return TryEquip(id);
    }
    public bool TryEquip(string id)
    {
        Initialize(); Item item = Find(id);
        if (!Owns(item)) return false;
        equipped[item.category] = item.id; SaveSelection(item.category);
        ApplySelections(); Changed?.Invoke(); return true;
    }
    public void Unequip(Category category)
    {
        Initialize(); equipped[category] = ""; SaveSelection(category);
        ApplySelections(); Changed?.Invoke();
    }
    private void SaveSelection(Category category)
    { PlayerPrefs.SetString(SelectedKey + category, equipped[category]); PlayerPrefs.Save(); }
    private void CaptureSceneAttachmentPoses()
    {
        if (scenePosesCaptured || !readAttachmentPosesFromScene || player == null) return;
        Animator animator = player.Animim.Animator;
        if (animator == null || !animator.isHuman) return;
        scenePosesCaptured = true;
        foreach (var item in items) if (item != null)
            foreach (var attachment in item.attachments)
            {
                if (attachment == null || attachment.root == null) continue;
                Transform bone = animator.GetBoneTransform(attachment.bone);
                // Read the authored scene pose before any binding or model swap.
                // Detached props must retain their captured offsets for the new model.
                if (bone == null || attachment.root.parent != bone) continue;
                attachment.localPosition = attachment.root.localPosition;
                attachment.localEulerAngles = attachment.root.localEulerAngles;
                attachment.localScale = attachment.root.localScale;
            }
    }
    private void BeforeModelChange()
    {
        if (player == null) return;
        CaptureSceneAttachmentPoses();
        foreach (var item in items) if (item != null)
            foreach (var attachment in item.attachments)
                if (attachment != null && attachment.root != null) attachment.root.SetParent(player.transform, true);
        bound = null;
    }
    public void RefreshBindings()
    {
        if (player == null || player.Animim.Animator == null || !player.Animim.Animator.isHuman) return;
        CaptureSceneAttachmentPoses();
        Initialize(); bound = player.Animim.Animator;
        foreach (var item in items) if (item != null)
            foreach (var attachment in item.attachments)
            {
                if (attachment == null || attachment.root == null) continue;
                Transform bone = bound.GetBoneTransform(attachment.bone);
                if (bone == null) continue;
                attachment.root.SetParent(bone, false);
                attachment.root.SetLocalPositionAndRotation(attachment.localPosition, Quaternion.Euler(attachment.localEulerAngles));
                attachment.root.localScale = attachment.localScale;
                // A dynamic Rigidbody on a held prop must not fight the animated hand.
                foreach (var body in attachment.root.GetComponentsInChildren<Rigidbody>(true))
                { body.isKinematic = true; body.useGravity = false; body.detectCollisions = true; }
            }
        ApplySelections();
    }
    private void ApplySelections()
    {
        ApplyVisibility();
        Item shield = items.Find(item => item != null && item.category == Category.Shield && IsEquipped(item));
        if (combat != null)
        {
            combat.shieldHitbox = shield != null ? shield.shieldCollider : null;
            combat.RefreshEquipmentReferences();
        }
        var binder = player != null ? player.GetComponent<JourneyPlayerModelBindings>() : null;
        if (binder != null) binder.shieldHitbox = combat != null ? combat.shieldHitbox : null;
    }
    private void ApplyVisibility()
    {
        swordHiddenForStone = hideSwordWhileUsingStone && stoneController != null &&
            stoneController.isActiveAndEnabled && stoneController.IsUsingStone;
        foreach (var item in items) if (item != null)
        {
            bool active = Owns(item) && equipped.TryGetValue(item.category, out string id) && id == item.id &&
                !(swordHiddenForStone && item.category == Category.Sword);
            foreach (var attachment in item.attachments)
                if (attachment != null && attachment.root != null) attachment.root.gameObject.SetActive(active);
        }
    }
    private void LateUpdate()
    {
        if (!initialized) return;
        if (player != null && player.Animim.Animator != bound) RefreshBindings();
        bool hide = hideSwordWhileUsingStone && stoneController != null &&
            stoneController.isActiveAndEnabled && stoneController.IsUsingStone;
        if (hide != swordHiddenForStone) ApplyVisibility();
    }
    private void OnDestroy()
    {
        if (player == null) return;
        player.EventBeforeChangeModel -= BeforeModelChange; player.EventAfterChangeModel -= RefreshBindings;
    }
}
