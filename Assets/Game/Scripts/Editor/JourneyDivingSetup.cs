#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using GameCreator.Runtime.Characters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class JourneyDivingSetup
{
    private static IEnumerable<T> All<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));
    private static T Need<T>(T value, string message) where T : Object
    { if (value == null) throw new InvalidOperationException(message); return value; }
    private static GameObject UI(Scene scene, string name) => All<RectTransform>(scene)
        .Where(t => t.name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)).Select(t => t.gameObject).FirstOrDefault();
    private static Image Fill(GameObject root) => root.GetComponentsInChildren<Image>(true)
        .FirstOrDefault(image => image.name.Trim().Equals("HealthFill", StringComparison.OrdinalIgnoreCase));
    private static void Dirty(Object value)
    { if (value == null) return; EditorUtility.SetDirty(value); PrefabUtility.RecordPrefabInstancePropertyModifications(value); }
    private static void ConfigureBar(GameObject root, Image image)
    {
        Undo.RecordObject(image, "Configure diving reserve fill");
        image.type = Image.Type.Filled; image.fillMethod = Image.FillMethod.Horizontal;
        image.fillOrigin = (int)Image.OriginHorizontal.Left; image.fillAmount = 1f;
        foreach (var graphic in root.GetComponentsInChildren<Graphic>(true))
        { Undo.RecordObject(graphic, "Let touch input pass through diving HUD"); graphic.raycastTarget = false; Dirty(graphic); }
        foreach (var slider in root.GetComponentsInChildren<Slider>(true))
        { Undo.RecordObject(slider, "Make diving slider display-only"); slider.interactable = false; Dirty(slider); }
        Dirty(image);
    }
    private static Bounds GraphicBounds(Transform parent, GameObject ui)
    {
        Bounds result = new Bounds(); bool found = false;
        var corners = new Vector3[4];
        foreach (var graphic in ui.GetComponentsInChildren<Graphic>(true))
        {
            graphic.rectTransform.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                Vector3 point = parent.InverseTransformPoint(corner);
                if (!found) { result = new Bounds(point, Vector3.zero); found = true; }
                else result.Encapsulate(point);
            }
        }
        return result;
    }
    private static void SeparateOverlappingHUD(GameObject ui, GameObject mainHealth)
    {
        var rect = ui.transform as RectTransform; var main = mainHealth.transform as RectTransform;
        if (rect == null || main == null || rect.parent != main.parent ||
            (rect.anchoredPosition - main.anchoredPosition).sqrMagnitude > 0.1f) return;
        Bounds health = GraphicBounds(rect.parent, mainHealth), air = GraphicBounds(rect.parent, ui);
        float gap = Mathf.Max(8f, health.size.y * 0.12f);
        if (air.max.y < health.min.y || air.min.y > health.max.y || air.max.x < health.min.x || air.min.x > health.max.x) return;
        Undo.RecordObject(rect, "Place air reserve below main health");
        rect.anchoredPosition += Vector2.up * (health.min.y - gap - air.max.y);
        Dirty(rect);
    }

    [MenuItem("Quran Kids/Setup Diving Oxygen and Equipment Toggles")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("Exit Play Mode before diving setup."); return; }
        int group = -1;
        try
        {
            Scene scene = SceneManager.GetActiveScene();
            var player = Need(All<Character>(scene).FirstOrDefault(c => c.IsPlayer), "GameCreator Player is missing.");
            var swimming = Need(player.GetComponent<JourneyPlayerSwimming>(), "Run Setup Swimming and Stone Aiming first.");
            var inventory = Need(player.GetComponent<JourneyEquipmentInventory>(), "Run Setup Equipment Wardrobe and Camera first.");
            var combat = Need(player.GetComponent<JourneyPlayerCombat>(), "Run Setup Journey Combat first.");
            var menu = Need(All<JourneyMenuController>(scene).FirstOrDefault(), "JourneyMenuController is missing.");
            var radial = Need(All<JourneyRadialMenu>(scene).FirstOrDefault(), "RadialMenu Button needs JourneyRadialMenu.");
            var breathUI = Need(UI(scene, "Breath HealthUI"), "Add Breath HealthUI under Canvas.");
            var scubaUI = Need(UI(scene, "Scuba gear HealthUI"), "Add Scuba gear HealthUI under Canvas.");
            var breath = Need(Fill(breathUI), "Breath HealthUI needs a child Image named HealthFill.");
            var scuba = Need(Fill(scubaUI), "Scuba gear HealthUI needs a child Image named HealthFill.");
            Need(combat.startPoint, "Assign the existing combat Start Point so a dry-land diving respawn point can be created.");
            if (breath == scuba || combat.healthFill == breath || combat.healthFill == scuba)
                throw new InvalidOperationException("Main health, breath and scuba must use three separate HealthFill Images.");

            Undo.IncrementCurrentGroup(); group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Setup diving oxygen and equipment toggles");
            var breathing = player.GetComponent<JourneyPlayerBreathing>();
            if (breathing == null) breathing = Undo.AddComponent<JourneyPlayerBreathing>(player.gameObject);
            Undo.RecordObject(breathing, "Connect diving reserves and health HUD");
            breathing.swimming = swimming; breathing.inventory = inventory; breathing.combat = combat; breathing.menu = menu;
            breathing.breathHealthUI = breathUI; breathing.scubaGearHealthUI = scubaUI;
            breathing.breathFill = breath; breathing.scubaGearFill = scuba;
            breathing.breathSlider = breathUI.GetComponentInChildren<Slider>(true);
            breathing.scubaGearSlider = scubaUI.GetComponentInChildren<Slider>(true);
            if (breathing.drowningRespawnPoint == null)
            {
                var point = All<Transform>(scene).FirstOrDefault(t => t.name == "Diving Respawn Point");
                if (point == null)
                {
                    var go = new GameObject("Diving Respawn Point");
                    Undo.RegisterCreatedObjectUndo(go, "Create diving respawn point");
                    SceneManager.MoveGameObjectToScene(go, scene); point = go.transform;
                    point.SetPositionAndRotation(combat.startPoint.position, combat.startPoint.rotation);
                }
                breathing.drowningRespawnPoint = point;
            }
            Undo.RecordObject(inventory, "Connect underwater equipment visibility");
            inventory.swimming = swimming; inventory.hideWeaponsAndArmorUnderwater = true;
            Undo.RecordObject(radial, "Allow equipping and removing radial items");
            radial.equipmentInventory = inventory; radial.toggleEquippedItems = true;
            foreach (var item in inventory.items)
                if (item != null && !string.IsNullOrEmpty(item.id) && !radial.items.Any(slot => slot != null && slot.equipmentId == item.id))
                    radial.items.Add(new JourneyRadialMenu.Item { name = item.displayName, equipmentId = item.id });
            ConfigureBar(breathUI, breath); ConfigureBar(scubaUI, scuba);
            var health = UI(scene, "PlayerHealthUI");
            if (health != null) { SeparateOverlappingHUD(breathUI, health); SeparateOverlappingHUD(scubaUI, health); }
            Dirty(breathing); Dirty(inventory); Dirty(radial);
            EditorSceneManager.MarkSceneDirty(scene); Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = player.gameObject;
            Debug.Log("OK: Diving oxygen and equipment toggles. Breath: " + breathing.breathSeconds + "s; scuba: " + breathing.scubaSeconds +
                "s. Set Drowning Respawn Point on Player > JourneyPlayerBreathing, then save the scene. Equipment attachment poses were preserved.", breathing);
        }
        catch (Exception error)
        {
            if (group >= 0) Undo.RevertAllDownToGroup(group);
            Debug.LogError("Diving setup FAILED: " + error.Message);
        }
    }

    [MenuItem("Quran Kids/Validate Diving Oxygen and Equipment Toggles")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Exit Play Mode before validating diving setup."); return; }
        Scene scene = SceneManager.GetActiveScene();
        var components = All<JourneyPlayerBreathing>(scene).ToArray();
        if (components.Length == 0) { Debug.LogError("Run Setup Diving Oxygen and Equipment Toggles first."); return; }
        foreach (var component in components)
        {
            var errors = new List<string>();
            if (component.swimming == null || component.inventory == null || component.combat == null || component.menu == null) errors.Add("Player service references are incomplete.");
            if (component.breathHealthUI == null || component.scubaGearHealthUI == null || component.breathFill == null || component.scubaGearFill == null) errors.Add("Breath / Scuba UI references are incomplete.");
            if (component.breathHealthUI != null && component.breathHealthUI == component.scubaGearHealthUI) errors.Add("Assign the two separate HealthUI parents, not Canvas.");
            if (component.breathFill != null && component.breathFill == component.scubaGearFill) errors.Add("The two reserves must use different fill Images.");
            if (component.combat != null && (component.combat.healthFill == component.breathFill || component.combat.healthFill == component.scubaGearFill)) errors.Add("Main health cannot share a fill Image with oxygen.");
            if (component.drowningRespawnPoint == null) errors.Add("Assign Drowning Respawn Point on dry land.");
            if (component.swimming != null && (component.swimming.preferredWater == null || component.swimming.mouth == null)) errors.Add("Assign swimming Water and Mouth references.");
            if (component.inventory != null && component.inventory.swimming != component.swimming) errors.Add("Connect Swimming on JourneyEquipmentInventory.");
            if (component.inventory != null && !component.inventory.items.Any(item => item != null && item.category == JourneyEquipmentInventory.Category.Scuba &&
                item.attachments.Any(attachment => attachment != null && attachment.root != null &&
                    (attachment.bone == HumanBodyBones.Chest || attachment.bone == HumanBodyBones.UpperChest || attachment.bone == HumanBodyBones.Spine))))
                errors.Add("Scuba catalog needs a tank attachment bound to Chest, UpperChest or Spine.");
            var radial = All<JourneyRadialMenu>(scene).FirstOrDefault();
            if (radial == null || !radial.toggleEquippedItems || radial.equipmentInventory != component.inventory) errors.Add("Connect radial equipment inventory and enable Toggle Equipped Items.");
            if (errors.Count > 0) Debug.LogError("Diving validation FAILED:\n- " + string.Join("\n- ", errors), component);
            else Debug.Log("Diving references OK. Play and device tests for waterline, oxygen, toggles and respawn are still required.", component);
        }
    }
}
#endif
