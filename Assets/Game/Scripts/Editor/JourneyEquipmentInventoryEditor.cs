#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(JourneyEquipmentInventory))]
public sealed class JourneyEquipmentInventoryEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var inventory = (JourneyEquipmentInventory)target;
        if (!inventory.resetAllPurchasesInEditor) return;
        inventory.resetAllPurchasesInEditor = false;
        EditorUtility.SetDirty(inventory);
        JourneyCoinWallet.ResetAllPurchasesForEditor();
        foreach (var current in Object.FindObjectsByType<JourneyEquipmentInventory>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            current.ResetEquipmentSelectionsForEditor();
            if (!Application.isPlaying && current.gameObject.scene.IsValid())
            {
                foreach (var item in current.items) if (item != null)
                    foreach (var attachment in item.attachments) if (attachment != null && attachment.root != null)
                    { EditorUtility.SetDirty(attachment.root.gameObject); PrefabUtility.RecordPrefabInstancePropertyModifications(attachment.root.gameObject); }
                EditorSceneManager.MarkSceneDirty(current.gameObject.scene);
            }
        }
        serializedObject.Update(); Repaint();
        Debug.Log("Equipment purchases reset for Editor testing. Paid items are locked again; free items remain free. Current coins, language and puzzle progress were kept. The reset checkbox has cleared itself.", inventory);
    }
}
#endif
