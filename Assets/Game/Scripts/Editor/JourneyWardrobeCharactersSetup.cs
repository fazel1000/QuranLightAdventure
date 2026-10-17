#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Replaces the two-character setup command with restoration of the existing Hodaart wardrobe.
public static class JourneyWardrobeCharactersSetup
{
    [MenuItem("Quran Kids/Restore Hodaart Wardrobe")]
    private static void Restore()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("Exit Play Mode first."); return; }
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var room = Object.FindObjectsByType<JourneyWardrobeRoom>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(item => item.gameObject.scene == scene);
        if (room == null || room.preview == null || room.stageRoot == null || room.wardrobePanel == null || room.previewCamera == null)
        { Debug.LogError("Open the gameplay scene and assign the existing Hodaart wardrobe references first."); return; }
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Restore Hodaart wardrobe");
        // Resolve the old component by name so this tool also compiles without its script installed.
        foreach (var component in room.GetComponents<MonoBehaviour>())
        {
            if (component == null || component.GetType().Name != "JourneyWardrobeCharacters") continue;
            var data = new SerializedObject(component);
            var property = data.FindProperty("cartoonPreview");
            var cartoon = property != null ? property.objectReferenceValue as Transform : null;
            RemoveCartoonPreview(cartoon, room);
            Undo.DestroyObjectImmediate(component);
        }
        RemoveCartoonPreview(room.stageRoot.transform.Find("Cartoon Kids Preview"), room);
        foreach (string name in new[] { "Select Hodaart", "Select Cartoon Kids", "Character Selection Status" })
        {
            Transform addedUI = room.wardrobePanel.transform.Find(name);
            if (addedUI != null) Undo.DestroyObjectImmediate(addedUI.gameObject);
        }
        Undo.RecordObject(room, "Restore Hodaart model");
        room.useHodaartForPlayer = true;
        Undo.RecordObject(room.preview.transform, "Center Hodaart preview");
        room.preview.transform.localPosition = Vector3.zero;
        room.preview.transform.localRotation = Quaternion.identity;
        Undo.RecordObject(room.preview.gameObject, "Show Hodaart preview");
        room.preview.gameObject.SetActive(true);
        PrefabUtility.RecordPrefabInstancePropertyModifications(room.preview.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(room.preview.gameObject);
        foreach (var choice in room.wardrobePanel.GetComponentsInChildren<JourneyWardrobeChoice>(true))
        {
            Undo.RecordObject(choice, "Restore outfit controls");
            Undo.RecordObject(choice.gameObject, "Show outfit controls");
            choice.appearance = room.preview;
            choice.gameObject.SetActive(true);
        }
        Undo.RecordObject(room.previewCamera, "Restore wardrobe viewport");
        room.previewCamera.rect = new Rect(0.38f, 0f, 0.62f, 1f);
        var drag = room.wardrobePanel.GetComponent<JourneyWardrobeDrag>() ?? Undo.AddComponent<JourneyWardrobeDrag>(room.wardrobePanel);
        Undo.RecordObject(drag, "Enable camera orbit");
        drag.target = room.preview.transform;
        drag.previewCamera = room.previewCamera;
        drag.degreesPerScreenWidth = 360f;
        drag.degreesPerScreenHeight = 180f;
        drag.limitVerticalRotation = false;
        EditorUtility.SetDirty(room);
        EditorUtility.SetDirty(drag);
        EditorUtility.SetDirty(room.previewCamera);
        EditorSceneManager.MarkSceneDirty(scene);
        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = room.wardrobePanel;
        Debug.Log("Hodaart-only wardrobe restored. Cartoon preview, selector component and selection UI removed. Save the scene. Drag horizontally or vertically over the preview to orbit the camera.", room);
    }
    private static void RemoveCartoonPreview(Transform candidate, JourneyWardrobeRoom room)
    {
        if (candidate == null || candidate == room.preview.transform || candidate == room.stageRoot.transform ||
            room.preview.transform.IsChildOf(candidate) || !candidate.IsChildOf(room.stageRoot.transform)) return;
        Undo.DestroyObjectImmediate(candidate.gameObject);
    }
}
#endif
