#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using GameCreator.Runtime.Cameras;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class JourneyEquipmentValidation
{
    [MenuItem("Quran Kids/Validate Equipment Wardrobe and Camera")]
    private static void Validate()
    {
        Scene scene = SceneManager.GetActiveScene(); var roots = scene.GetRootGameObjects();
        var errors = new List<string>(); var reminders = new List<string>();
        void Check(bool ok, string message) { if (!ok) errors.Add(message); }
        var inventory = roots.SelectMany(go => go.GetComponentsInChildren<JourneyEquipmentInventory>(true)).FirstOrDefault();
        Check(inventory != null, "Player equipment inventory is missing. Run Setup Equipment Wardrobe and Camera.");
        if (inventory != null)
        {
            Check(inventory.player != null && inventory.combat != null, "Inventory Player/Combat references are missing.");
            var ids = new HashSet<string>();
            foreach (var item in inventory.items)
            {
                if (item == null) { errors.Add("The catalog contains an empty item."); continue; }
                Check(!string.IsNullOrEmpty(item.id) && ids.Add(item.id), "Missing/duplicate equipment ID: " + item.displayName);
                Check(item.price >= 0, "Negative equipment price: " + item.displayName);
                Check(item.previewModel != null, "Assign the 3D Preview Model for " + item.displayName);
                Check(item.attachments.Count > 0 && item.attachments.All(a => a != null && a.root != null), "Equipment attachments are incomplete: " + item.displayName);
                foreach (var attachment in item.attachments)
                {
                    if (attachment == null || attachment.root == null) continue;
                    foreach (var part in attachment.root.GetComponentsInChildren<Transform>(true))
                        Check(GameObjectUtility.GetStaticEditorFlags(part.gameObject) == 0, "Held equipment must not be Static: " + part.name);
                }
                if (item.category == JourneyEquipmentInventory.Category.Shield)
                    Check(item.shieldCollider != null && item.shieldCollider.enabled && item.shieldCollider.isTrigger, "Shield needs its enabled trigger BoxCollider: " + item.displayName);
                if (item.icon == null) reminders.Add("Assign Icon on Player > JourneyEquipmentInventory > " + item.displayName);
            }
            var combat = inventory.combat;
            if (combat != null)
            {
                var input = combat.defendButton != null ? combat.defendButton.GetComponent<JourneyShieldHoldInput>() : null;
                Check(input != null && input.combat == combat, "Defend Button needs JourneyShieldHoldInput connected to the Player Combat.");
            }
        }
        var shop = roots.SelectMany(go => go.GetComponentsInChildren<JourneyEquipmentWardrobe>(true)).FirstOrDefault();
        Check(shop != null && shop.room != null && shop.inventory == inventory && shop.equipmentRoot != null && shop.cardTemplate != null &&
            shop.itemList != null && shop.actionButton != null && shop.equipmentPreview != null, "Equipment wardrobe UI is incomplete.");
        if (shop != null && shop.equipmentPreview != null)
            Check(shop.equipmentPreview.image != null && shop.equipmentPreview.previewCamera != null && shop.equipmentPreview.stage != null && shop.equipmentPreview.drag != null,
                "Equipment 3D preview camera/surface/drag references are incomplete.");
        var camera = roots.SelectMany(go => go.GetComponentsInChildren<JourneyCameraPresets>(true)).FirstOrDefault();
        Check(camera != null && camera.thirdPersonShot != null && camera.thirdPersonShot.ShotType is ShotTypeThirdPerson && camera.nearButton != null &&
            camera.averageButton != null && camera.awayButton != null, "Near/Average/Away camera preset references are incomplete.");
        foreach (var radial in roots.SelectMany(go => go.GetComponentsInChildren<JourneyRadialMenu>(true)))
            foreach (var item in radial.items)
                if (item != null && !string.IsNullOrEmpty(item.equipmentId)) Check(radial.equipmentInventory != null && radial.equipmentInventory.Find(item.equipmentId) != null,
                    "Radial catalog ID not found: " + item.name);
        foreach (var enemy in roots.SelectMany(go => go.GetComponentsInChildren<JourneySkeletonEnemy>(true)))
        {
            if (enemy.patrolPoints == null || enemy.patrolPoints.Length == 0) continue;
            Check(enemy.patrolPoints.All(point => point != null && !point.IsChildOf(enemy.transform)), enemy.name + ": patrol points must be assigned outside the moving enemy.");
            Check(enemy.modelAnimator != null && enemy.playerCombat != null && enemy.bladeBase != null && enemy.bladeTip != null,
                enemy.name + ": animator/player/blade references are incomplete.");
            if (!NavMesh.SamplePosition(enemy.transform.position, out _, 1.5f, NavMesh.AllAreas))
                reminders.Add(enemy.name + ": place on baked NavMesh / use Snap Enemy and Patrol Points to NavMesh.");
        }
        if (errors.Count > 0) Debug.LogError("Equipment validation FAILED [" + scene.name + "]:\n- " + string.Join("\n- ", errors));
        else Debug.Log("Equipment validation [" + scene.name + "]: 0 errors. Test purchases, equipment fitting, held defense, camera modes and patrols in Play/device.");
        foreach (string reminder in reminders.Distinct()) Debug.LogWarning(reminder);
    }
}
#endif
