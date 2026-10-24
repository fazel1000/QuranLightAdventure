#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using GameCreator.Runtime.Characters;
using GameCreator.Runtime.Cameras;
using RTLTMPro;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class JourneyEquipmentWardrobeSetup
{
    private const string ScubaPath = "Assets/3d Objects/Scuba Gear/Scuba Gear .fbx";
    private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<T>(true));
    private static T Add<T>(GameObject go) where T : Component => JourneyWorldSetupUtility.Add<T>(go);
    private static void Dirty(Object item) { JourneyWorldSetupUtility.Dirty(item); }

    [MenuItem("Quran Kids/Setup Equipment Wardrobe and Camera")]
    private static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("Exit Play Mode before equipment setup."); return; }
        Scene scene = SceneManager.GetActiveScene();
        var player = All<Character>(scene).FirstOrDefault(c => c.IsPlayer);
        var room = All<JourneyWardrobeRoom>(scene).FirstOrDefault();
        if (player == null || room == null || room.wardrobePanel == null || room.preview == null)
        { Debug.LogError("The active scene needs its Player and existing Hodaart Wardrobe Room."); return; }
        Animator animator = player.Animim.Animator;
        if (animator == null || !animator.isHuman) { Debug.LogError("Player requires its current Humanoid Animator."); return; }
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Set up equipment wardrobe and camera");
        var inventory = Add<JourneyEquipmentInventory>(player.gameObject);
        Undo.RecordObject(inventory, "Connect equipment catalog"); inventory.player = player; inventory.combat = player.GetComponent<JourneyPlayerCombat>(); inventory.wardrobe = room;
        Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand), left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (right == null || left == null) { Debug.LogError("Both Humanoid hand bones are required."); return; }
        var swords = right.Cast<Transform>().Where(t => t.name.IndexOf("Sword", StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(t => t.name).ToArray();
        int lockedPrice = 100;
        foreach (var sword in swords)
        {
            bool fresh; var item = Item(inventory, sword.gameObject, JourneyEquipmentInventory.Category.Sword, out fresh);
            if (fresh) { item.equippedByDefault = sword.gameObject.activeSelf; item.price = item.equippedByDefault ? 0 : lockedPrice; if (!item.equippedByDefault) lockedPrice += 100; }
            PrepareHeldModel(sword.gameObject, player.gameObject.layer, null);
            if (fresh) item.attachments.Add(Capture(sword, HumanBodyBones.RightHand));
        }
        if (swords.Length > 0 && !inventory.items.Any(i => i != null && i.category == JourneyEquipmentInventory.Category.Sword && i.equippedByDefault))
        { var first = inventory.items.First(i => i != null && i.category == JourneyEquipmentInventory.Category.Sword); first.equippedByDefault = true; first.price = 0; }
        Transform shield = left.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.Trim().Equals("Shield 1", StringComparison.OrdinalIgnoreCase));
        if (shield != null)
        {
            bool fresh; var item = Item(inventory, shield.gameObject, JourneyEquipmentInventory.Category.Shield, out fresh);
            bool newBox = shield.GetComponent<BoxCollider>() == null; var box = Add<BoxCollider>(shield.gameObject);
            Undo.RecordObject(box, "Fit the held shield trigger");
            if (newBox) JourneyWorldSetupUtility.FitHull(box, shield);
            box.isTrigger = true; box.enabled = true; item.shieldCollider = box;
            PrepareHeldModel(shield.gameObject, player.gameObject.layer, box);
            if (fresh) { item.price = 0; item.equippedByDefault = true; item.attachments.Add(Capture(shield, HumanBodyBones.LeftHand)); }
            var combat = inventory.combat;
            if (combat != null)
            {
                Undo.RecordObject(combat, "Use Shield 1 for defense");
                if (combat.shieldHitbox != null && combat.shieldHitbox != box)
                { Undo.RecordObject(combat.shieldHitbox, "Disable obsolete shield hitbox"); combat.shieldHitbox.enabled = false; Dirty(combat.shieldHitbox); }
                combat.shieldHitbox = box;
                if (combat.defendButton != null)
                {
                    var input = Add<JourneyShieldHoldInput>(combat.defendButton.gameObject); Undo.RecordObject(input, "Connect held defense"); input.combat = combat; Dirty(input);
                    for (int i = combat.defendButton.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                        if (combat.defendButton.onClick.GetPersistentTarget(i) == combat && combat.defendButton.onClick.GetPersistentMethodName(i) == "TryDefend")
                            UnityEventTools.RemovePersistentListener(combat.defendButton.onClick, i);
                    Dirty(combat.defendButton);
                }
                Dirty(combat);
            }
            var binder = player.GetComponent<JourneyPlayerModelBindings>();
            if (binder != null) { Undo.RecordObject(binder, "Bind the visible shield to the current hand"); binder.shieldHitbox = box; Dirty(binder); }
            Dirty(box);
        }
        else Debug.LogWarning("Shield 1 was not found under the current player's LeftHand. Existing shield references were retained.");
        SetupScuba(inventory, animator);
        SetupUI(scene, room, inventory);
        SetupCamera(scene, room);
        var radial = All<JourneyRadialMenu>(scene).FirstOrDefault();
        if (radial != null) { Undo.RecordObject(radial, "Connect radial equipment ownership"); radial.equipmentInventory = inventory; Dirty(radial); }
        Dirty(inventory); Undo.CollapseUndoOperations(group); EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets();
        Selection.activeGameObject = inventory.gameObject;
        Debug.Log("Equipment setup COMPLETE: " + swords.Length + " swords, Shield 1, Scuba Gear, held defense, equipment shop and camera presets. Save scene. Set each item's Price/Icon/attachment pose on Player > JourneyEquipmentInventory.", inventory);
    }
    private static JourneyEquipmentInventory.Item Item(JourneyEquipmentInventory inventory, GameObject model,
        JourneyEquipmentInventory.Category category, out bool fresh)
    {
        var item = inventory.items.FirstOrDefault(i => i != null && i.category == category && (i.previewModel == model || i.attachments.Any(a => a != null && a.root == model.transform)));
        fresh = item == null;
        if (fresh)
        {
            item = new JourneyEquipmentInventory.Item { id = category.ToString().ToLowerInvariant() + "-" + Hash128.Compute(GlobalObjectId.GetGlobalObjectIdSlow(model).ToString()),
                displayName = model.name.Trim(), englishName = model.name.Trim(), arabicName = model.name.Trim(), category = category, previewModel = model };
            item.icon = FindIcon(model.name); inventory.items.Add(item);
        }
        return item;
    }
    private static Sprite FindIcon(string name)
    {
        string[] folders = { "Assets/Swords Pack Cute Series", "Assets/3d Objects/Scuba Gear" };
        foreach (string folder in folders.Where(AssetDatabase.IsValidFolder))
            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string candidate = System.IO.Path.GetFileNameWithoutExtension(path).Replace(" ", "").Replace("_", "");
                if (candidate.Equals(name.Replace(" ", "").Replace("_", ""), StringComparison.OrdinalIgnoreCase)) return AssetDatabase.LoadAssetAtPath<Sprite>(path);
            }
        return null;
    }
    private static JourneyEquipmentInventory.Attachment Capture(Transform root, HumanBodyBones bone)
    { return new JourneyEquipmentInventory.Attachment { root = root, bone = bone, localPosition = root.localPosition, localEulerAngles = root.localEulerAngles, localScale = root.localScale }; }
    private static void PrepareHeldModel(GameObject root, int layer, Collider shield)
    {
        foreach (var part in root.GetComponentsInChildren<Transform>(true))
        {
            Undo.RecordObject(part.gameObject, "Make held equipment movable");
            GameObjectUtility.SetStaticEditorFlags(part.gameObject, 0); part.gameObject.layer = layer; Dirty(part.gameObject);
        }
        foreach (var collider in root.GetComponentsInChildren<Collider>(true))
        { Undo.RecordObject(collider, "Use only the shield defense trigger"); collider.enabled = collider == shield; if (collider == shield) collider.isTrigger = true; Dirty(collider); }
        foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
        { Undo.RecordObject(body, "Attach rigid equipment to animation"); body.isKinematic = true; body.useGravity = false; Dirty(body); }
    }
    private static void SetupScuba(JourneyEquipmentInventory inventory, Animator animator)
    {
        if (inventory.items.Any(i => i != null && i.id == "scuba-b7da6653f81372b4585928374d705620")) return;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ScubaPath);
        if (model == null) { Debug.LogWarning("Scuba Gear model was not found at " + ScubaPath); return; }
        var item = new JourneyEquipmentInventory.Item { id = "scuba-b7da6653f81372b4585928374d705620", category = JourneyEquipmentInventory.Category.Scuba,
            displayName = "تجهیزات غواصی", englishName = "Scuba gear", arabicName = "معدات الغوص", price = 500, previewModel = model };
        var authored = All<Transform>(inventory.gameObject.scene).FirstOrDefault(t => t.name.Trim() == "Scuba Gear");
        GameObject source = authored != null ? authored.gameObject : model;
        var mask = MountScubaPart(source, animator, HumanBodyBones.Head, "Scuba Mask", new[] { "Diving Mask", "Diving Mask Glass" }, 0.24f, new Vector3(0f, -0.02f, 0.12f));
        var tank = MountScubaPart(source, animator, HumanBodyBones.Chest, "Scuba Tank", new[] { "Tank Capsule" }, 0.55f, new Vector3(0f, -0.03f, -0.2f));
        var clock = MountScubaPart(source, animator, HumanBodyBones.LeftHand, "Scuba Wrist Clock", new[] { "Clock" }, 0.075f, new Vector3(0f, 0.04f, -0.07f));
        if (mask != null) item.attachments.Add(mask); if (tank != null) item.attachments.Add(tank); if (clock != null) item.attachments.Add(clock);
        if (mask == null || tank == null || clock == null) Debug.LogWarning("Some Scuba parts could not be mounted. Check the authored Scuba Gear child names and the player's Head/Chest/LeftHand bones.");
        inventory.items.Add(item);
    }
    private static JourneyEquipmentInventory.Attachment MountScubaPart(GameObject model, Animator animator, HumanBodyBones boneType,
        string name, string[] partNames, float size, Vector3 offset)
    {
        Transform bone = animator.GetBoneTransform(boneType); if (bone == null) return null;
        var wrapper = new GameObject(name); Undo.RegisterCreatedObjectUndo(wrapper, "Create wearable " + name); wrapper.transform.SetParent(bone, false);
        bool found = false;
        foreach (var part in model.GetComponentsInChildren<Transform>(true))
        {
            if (!partNames.Contains(part.name.Trim())) continue;
            var copy = Object.Instantiate(part.gameObject, wrapper.transform, false); copy.name = part.name;
            Undo.RegisterCreatedObjectUndo(copy, "Copy Scuba Gear part"); found = true;
        }
        if (!found) { Undo.DestroyObjectImmediate(wrapper); return null; }
        var temporary = wrapper.AddComponent<BoxCollider>(); JourneyWorldSetupUtility.FitHull(temporary, wrapper.transform);
        Vector3 center = temporary.center, dimensions = temporary.size; Object.DestroyImmediate(temporary);
        foreach (Transform child in wrapper.transform) child.localPosition -= center;
        float length = Mathf.Max(0.01f, Mathf.Max(dimensions.x, Mathf.Max(dimensions.y, dimensions.z)));
        float parentScale = Mathf.Max(0.01f, Mathf.Max(Mathf.Abs(bone.lossyScale.x), Mathf.Max(Mathf.Abs(bone.lossyScale.y), Mathf.Abs(bone.lossyScale.z))));
        Transform facing = animator.transform;
        wrapper.transform.localRotation = Quaternion.Inverse(bone.rotation) * facing.rotation;
        wrapper.transform.localPosition = bone.InverseTransformVector(facing.TransformDirection(offset));
        wrapper.transform.localScale = Vector3.one * (size / length / parentScale);
        PrepareHeldModel(wrapper, animator.gameObject.layer, null); var attachment = Capture(wrapper.transform, boneType); wrapper.SetActive(false); return attachment;
    }
    private static void SetupUI(Scene scene, JourneyWardrobeRoom room, JourneyEquipmentInventory inventory)
    {
        var ui = Add<JourneyEquipmentWardrobe>(room.gameObject); Undo.RecordObject(ui, "Connect equipment wardrobe UI"); ui.room = room; ui.inventory = inventory;
        Transform panel = room.wardrobePanel.transform;
        var choices = panel.Find("Choices");
        if (choices == null) throw new InvalidOperationException("The existing wardrobe Choices panel is missing.");
        ui.clothingRoot = choices.gameObject;
        TMP_FontAsset font = panel.GetComponentsInChildren<TMP_Text>(true).Select(t => t.font).FirstOrDefault(f => f != null);
        if (font == null) font = TMP_Settings.defaultFontAsset;
        var back = choices.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "Back");
        if (back != null)
        {
            Undo.SetTransformParent(back.transform, panel, "Keep one wardrobe Back button");
            var rect = (RectTransform)back.transform; Undo.RecordObject(rect, "Place wardrobe Back button");
            rect.anchorMin = rect.anchorMax = new Vector2(0.19f, 0.065f); rect.anchoredPosition = Vector2.zero; rect.sizeDelta = new Vector2(200f, 55f); Dirty(rect);
        }
        var choicesRect = (RectTransform)choices; Undo.RecordObject(choicesRect, "Reserve wardrobe mode tabs"); choicesRect.anchorMax = new Vector2(0.38f, 0.93f); Dirty(choicesRect);
        ui.clothesTab = MakeButton(panel, "Clothes Tab", new Vector2(0.01f, 0.94f), new Vector2(0.185f, 0.995f), "لباس‌ها", "Clothes", "الملابس", font);
        ui.equipmentTab = MakeButton(panel, "Equipment Tab", new Vector2(0.195f, 0.94f), new Vector2(0.37f, 0.995f), "تجهیزات", "Equipment", "المعدات", font);
        var body = UI(panel, "Equipment Shop Panel", typeof(Image)); Anchors(body, new Vector2(0.005f, 0.13f), new Vector2(0.375f, 0.93f));
        var background = body.GetComponent<Image>(); background.color = new Color(0.04f, 0.08f, 0.13f, 0.94f); background.raycastTarget = true;
        ui.equipmentRoot = body; ui.selectedNameText = MakeText(body.transform, "Selected Name", new Vector2(0.03f, 0.92f), new Vector2(0.97f, 1f), font, 25f);
        ui.balanceText = MakeText(body.transform, "Shop Coin Balance", new Vector2(0.03f, 0.85f), new Vector2(0.97f, 0.92f), font, 22f);
        string[] fa = { "شمشیر", "سپر", "زره", "غواصی" }, en = { "Swords", "Shields", "Armor", "Scuba" }, ar = { "السيوف", "الدروع", "الدرع", "الغوص" };
        ui.categoryButtons = new Button[4];
        for (int i = 0; i < 4; i++) ui.categoryButtons[i] = MakeButton(body.transform, "Category " + i, new Vector2(0.02f + i * 0.245f, 0.78f), new Vector2(0.255f + i * 0.245f, 0.85f), fa[i], en[i], ar[i], font);
        var frame = UI(body.transform, "Equipment Preview Frame"); Anchors(frame, new Vector2(0.05f, 0.36f), new Vector2(0.95f, 0.78f));
        var surface = UI(frame.transform, "Equipment 3D Preview", typeof(RawImage), typeof(JourneyWardrobeDrag), typeof(AspectRatioFitter)); Anchors(surface, Vector2.zero, Vector2.one);
        var aspect = surface.GetComponent<AspectRatioFitter>(); aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent; aspect.aspectRatio = 1f;
        var image = surface.GetComponent<RawImage>(); image.color = Color.white; image.raycastTarget = true;
        ui.statusText = MakeText(body.transform, "Equipment Status", new Vector2(0.03f, 0.29f), new Vector2(0.97f, 0.36f), font, 21f);
        var list = UI(body.transform, "Equipment List", typeof(Image), typeof(ScrollRect)); Anchors(list, new Vector2(0.02f, 0.1f), new Vector2(0.98f, 0.29f)); list.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.2f);
        var viewport = UI(list.transform, "Viewport", typeof(Image), typeof(RectMask2D)); Anchors(viewport, Vector2.zero, Vector2.one); viewport.GetComponent<Image>().color = Color.clear;
        var content = UI(viewport.transform, "Content", typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
        var contentRect = (RectTransform)content.transform; contentRect.anchorMin = Vector2.zero; contentRect.anchorMax = new Vector2(0f, 1f); contentRect.pivot = new Vector2(0f, 0.5f); contentRect.anchoredPosition = Vector2.zero; contentRect.sizeDelta = Vector2.zero;
        var layout = content.GetComponent<HorizontalLayoutGroup>(); layout.spacing = 10f; layout.padding = new RectOffset(6, 6, 4, 4); layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = false; layout.childForceExpandHeight = true;
        content.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = list.GetComponent<ScrollRect>(); scroll.viewport = (RectTransform)viewport.transform; scroll.content = contentRect; scroll.horizontal = true; scroll.vertical = false; scroll.movementType = ScrollRect.MovementType.Clamped;
        ui.itemList = contentRect;
        var template = UI(content.transform, "Equipment Card Template", typeof(Image), typeof(UnityEngine.UI.Button), typeof(LayoutElement));
        template.GetComponent<LayoutElement>().preferredWidth = 155f; template.GetComponent<Image>().color = ui.normalColor;
        var icon = UI(template.transform, "Icon", typeof(Image)); Anchors(icon, new Vector2(0.2f, 0.48f), new Vector2(0.8f, 0.98f)); icon.GetComponent<Image>().preserveAspect = true; icon.GetComponent<Image>().raycastTarget = false;
        MakeText(template.transform, "Name", new Vector2(0.02f, 0.23f), new Vector2(0.98f, 0.48f), font, 18f);
        MakeText(template.transform, "Price", new Vector2(0.02f, 0f), new Vector2(0.98f, 0.23f), font, 16f);
        ui.cardTemplate = template; template.SetActive(false);
        ui.actionButton = MakeButton(body.transform, "Buy Equip Button", new Vector2(0.04f, 0.015f), new Vector2(0.96f, 0.095f), "انتخاب", "Equip", "تجهيز", font);
        ui.actionText = ui.actionButton.GetComponentInChildren<TMP_Text>(true);
        // This label is dynamic; the shop writes its localized price/state.
        var fixedLabel = ui.actionText.GetComponent<JourneyLocalizedText>(); if (fixedLabel != null) Undo.DestroyObjectImmediate(fixedLabel);
        var preview = Add<JourneyEquipmentPreview>(room.gameObject); Undo.RecordObject(preview, "Connect equipment preview");
        ui.equipmentPreview = preview; preview.image = image; preview.drag = surface.GetComponent<JourneyWardrobeDrag>();
        ConfigurePreviewStage(scene, preview);
        body.SetActive(false); choices.gameObject.SetActive(true);
        Dirty(preview); Dirty(ui); Dirty(scroll); Dirty(layout); Dirty(contentRect); Dirty(body.GetComponent<Image>());
    }
    private static void ConfigurePreviewStage(Scene scene, JourneyEquipmentPreview preview)
    {
        int layer = EnsureLayer("Journey Equipment Preview"); preview.previewLayer = layer;
        if (preview.stage == null)
        {
            var stage = new GameObject("Journey Equipment Preview Stage"); Undo.RegisterCreatedObjectUndo(stage, "Create equipment preview stage");
            SceneManager.MoveGameObjectToScene(stage, scene); stage.transform.position = new Vector3(13000f, 3000f, 13000f); preview.stage = stage.transform;
        }
        if (preview.previewCamera == null)
        { var camera = JourneyWorldSetupUtility.Child(preview.stage, "Equipment Preview Camera", typeof(Camera)); preview.previewCamera = Add<Camera>(camera); }
        var view = preview.previewCamera; Undo.RecordObject(view, "Configure equipment render camera"); view.enabled = false; view.cullingMask = 1 << layer;
        view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = Color.clear; view.nearClipPlane = 0.03f; view.farClipPlane = 15f; view.fieldOfView = 45f;
        var extra = Add<UniversalAdditionalCameraData>(view.gameObject); extra.renderPostProcessing = false; extra.renderShadows = false; extra.volumeLayerMask = 0;
        var lightObject = JourneyWorldSetupUtility.Child(preview.stage, "Equipment Preview Light", typeof(Light)); var light = Add<Light>(lightObject);
        Undo.RecordObject(light, "Light the equipment preview"); light.type = LightType.Directional; light.intensity = 1.5f; light.cullingMask = 1 << layer; light.shadows = LightShadows.None;
        light.transform.localRotation = Quaternion.Euler(40f, -35f, 0f); preview.stage.gameObject.SetActive(false);
        Dirty(view); Dirty(extra); Dirty(light); Dirty(preview.stage);
    }
    private static int EnsureLayer(string name)
    {
        int existing = LayerMask.NameToLayer(name); if (existing >= 0) return existing;
        var tags = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tags.FindProperty("layers");
        for (int i = 30; i >= 8; i--)
            if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
            { layers.GetArrayElementAtIndex(i).stringValue = name; tags.ApplyModifiedProperties(); return i; }
        throw new InvalidOperationException("Create a free layer named 'Journey Equipment Preview', then rerun Setup.");
    }
    private static void SetupCamera(Scene scene, JourneyWardrobeRoom room)
    {
        var settings = Add<JourneyCameraPresets>(room.gameObject); Undo.RecordObject(settings, "Connect camera preset buttons");
        var shot = All<ShotCamera>(scene).FirstOrDefault(s => s.IsMainShot && s.ShotType is ShotTypeThirdPerson) ?? All<ShotCamera>(scene).FirstOrDefault(s => s.ShotType is ShotTypeThirdPerson);
        if (shot == null) throw new InvalidOperationException("The player's third-person Shot Camera was not found.");
        settings.thirdPersonShot = shot;
        Transform panel = All<Transform>(scene).FirstOrDefault(t => t.name.Trim() == "Camera Panel");
        if (panel == null) throw new InvalidOperationException("Camera Panel was not found in the active scene.");
        settings.nearButton = panel.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name.Trim() == "Near");
        settings.averageButton = panel.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name.Trim() == "Average");
        settings.awayButton = panel.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name.Trim() == "Away");
        var data = new SerializedObject(shot); var type = data.FindProperty("m_ShotType");
        if (type != null)
        {
            settings.averageShoulder = Decimal(type, "m_ThirdPerson.m_Shoulder.m_Property.m_Value", settings.averageShoulder);
            settings.averageLift = Decimal(type, "m_ThirdPerson.m_Lift.m_Property.m_Value", settings.averageLift);
            settings.averageBaseRadius = Decimal(type, "m_ThirdPerson.m_Radius.m_Property.m_Value", settings.averageBaseRadius);
            settings.averageZoom = Decimal(type, "m_Zoom.m_TargetZoom", settings.averageZoom);
            settings.minimumZoomDistance = Decimal(type, "m_Zoom.m_MinDistance", settings.minimumZoomDistance);
        }
        LocalizeExisting(settings.nearButton, "نزدیک", "Near", "قريب"); LocalizeExisting(settings.averageButton, "متوسط", "Average", "متوسط"); LocalizeExisting(settings.awayButton, "دور", "Away", "بعيد");
        Dirty(settings);
    }
    private static float Decimal(SerializedProperty root, string path, float fallback)
    { var property = root.FindPropertyRelative(path); return property != null && property.propertyType == SerializedPropertyType.Float ? (float)property.doubleValue : fallback; }
    private static GameObject UI(Transform parent, string name, params Type[] components)
    {
        var types = new List<Type> { typeof(RectTransform) }; types.AddRange(components);
        GameObject go = JourneyWorldSetupUtility.Child(parent, name, types.ToArray());
        if (!(go.transform is RectTransform)) throw new InvalidOperationException(name + " must be a UI RectTransform.");
        foreach (Type component in components) if (go.GetComponent(component) == null) Undo.AddComponent(go, component);
        return go;
    }
    private static void Anchors(GameObject go, Vector2 min, Vector2 max)
    { var rect = (RectTransform)go.transform; Undo.RecordObject(rect, "Layout equipment UI"); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one; Dirty(rect); }
    private static TMP_Text MakeText(Transform parent, string name, Vector2 min, Vector2 max, TMP_FontAsset font, float size)
    {
        var go = UI(parent, name, typeof(RTLTextMeshPro)); Anchors(go, min, max); var text = go.GetComponent<TMP_Text>();
        Undo.RecordObject(text, "Configure equipment label"); text.font = font; text.fontSize = size; text.color = Color.white; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        var style = Add<JourneyLanguageTextStyle>(go); style.target = text; Dirty(text); Dirty(style); return text;
    }
    private static Button MakeButton(Transform parent, string name, Vector2 min, Vector2 max, string fa, string en, string ar, TMP_FontAsset font)
    {
        var go = UI(parent, name, typeof(Image), typeof(UnityEngine.UI.Button)); Anchors(go, min, max); var button = go.GetComponent<UnityEngine.UI.Button>();
        button.targetGraphic = go.GetComponent<Image>(); go.GetComponent<Image>().color = Color.white;
        var text = MakeText(go.transform, "Label", new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.96f), font, 21f);
        Localize(text, fa, en, ar); Dirty(button); return button;
    }
    private static void LocalizeExisting(Button button, string fa, string en, string ar)
    { if (button != null) Localize(button.GetComponentInChildren<TMP_Text>(true), fa, en, ar); }
    private static void Localize(TMP_Text text, string fa, string en, string ar)
    {
        if (text == null) return; var label = Add<JourneyLocalizedText>(text.gameObject); Undo.RecordObject(label, "Translate equipment UI");
        label.target = text; label.persian = fa; label.english = en; label.arabic = ar; label.Refresh(); Dirty(label);
        var style = Add<JourneyLanguageTextStyle>(text.gameObject); style.target = text; Dirty(style);
    }
    [MenuItem("Quran Kids/Equipment/Fill Radial Menu From Equipment Catalog")]
    private static void FillRadial()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene scene = SceneManager.GetActiveScene(); var inventory = All<JourneyEquipmentInventory>(scene).FirstOrDefault();
        var radial = All<JourneyRadialMenu>(scene).FirstOrDefault();
        if (inventory == null || radial == null) { Debug.LogError("Run Setup Equipment Wardrobe and Camera and the world-system Setup first."); return; }
        Undo.RecordObject(radial, "Fill radial equipment catalog"); radial.equipmentInventory = inventory;
        radial.items.RemoveAll(item => item != null && string.IsNullOrEmpty(item.equipmentId) && item.equipmentObject == null && item.icon == null &&
            item.onSelected.GetPersistentEventCount() == 0 && item.name != null && item.name.StartsWith("Item ", StringComparison.Ordinal));
        foreach (var item in inventory.items)
            if (item != null && !radial.items.Any(slot => slot != null && slot.equipmentId == item.id))
                radial.items.Add(new JourneyRadialMenu.Item { name = item.displayName, equipmentId = item.id });
        Dirty(radial); EditorSceneManager.MarkSceneDirty(scene); Selection.activeGameObject = radial.gameObject;
        Debug.Log("Radial items connected to the catalog. Assign Icon on each catalog item; owned items become selectable automatically. Custom radial entries were retained.", radial);
    }
    [MenuItem("Quran Kids/Equipment/Capture Equipment Attachment Poses")]
    private static void CapturePoses()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Scene scene = SceneManager.GetActiveScene(); var inventory = All<JourneyEquipmentInventory>(scene).FirstOrDefault();
        Animator animator = inventory != null && inventory.player != null ? inventory.player.Animim.Animator : null;
        if (inventory == null || animator == null || !animator.isHuman) { Debug.LogError("Run equipment Setup first."); return; }
        Undo.RecordObject(inventory, "Capture authored equipment attachment poses");
        foreach (var item in inventory.items)
        {
            if (item == null) continue;
            if (string.IsNullOrEmpty(item.id)) item.id = item.category.ToString().ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N");
            foreach (var attachment in item.attachments)
            {
                if (attachment == null || attachment.root == null) continue;
                Transform bone = animator.GetBoneTransform(attachment.bone); if (bone == null) continue;
                attachment.localPosition = bone.InverseTransformPoint(attachment.root.position);
                attachment.localEulerAngles = (Quaternion.Inverse(bone.rotation) * attachment.root.rotation).eulerAngles;
                Vector3 a = attachment.root.lossyScale, b = bone.lossyScale;
                attachment.localScale = new Vector3(a.x / Mathf.Max(0.0001f, Mathf.Abs(b.x)), a.y / Mathf.Max(0.0001f, Mathf.Abs(b.y)), a.z / Mathf.Max(0.0001f, Mathf.Abs(b.z)));
                if (item.previewModel == null) item.previewModel = attachment.root.gameObject;
                if (item.category == JourneyEquipmentInventory.Category.Shield && item.shieldCollider == null)
                { item.shieldCollider = Add<BoxCollider>(attachment.root.gameObject); JourneyWorldSetupUtility.FitHull(item.shieldCollider, attachment.root); item.shieldCollider.isTrigger = true; Dirty(item.shieldCollider); }
                PrepareHeldModel(attachment.root.gameObject, inventory.player.gameObject.layer, item.shieldCollider);
            }
        }
        Dirty(inventory); EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("Equipment positions, rotations and scales captured relative to their assigned Humanoid bones. Save scene.", inventory);
    }
}
#endif
