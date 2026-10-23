#if UNITY_EDITOR
using System;
using System.Linq;
using GameCreator.Runtime.Characters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class JourneyThrowableStoneSetup
{
    [MenuItem("Quran Kids/Stones/Make Selected Rocks Throwable")]
    private static void MakeSelected()
    {
        if (!CanEdit()) return;
        Scene scene = SceneManager.GetActiveScene();
        Transform root = FindStonesRoot(scene);
        if (root == null) return;
        var selected = Selection.gameObjects;
        int count = 0;
        foreach (var go in selected)
        {
            if (go == null || go.scene != scene || go.transform == root ||
                selected.Any(other => other != go && go.transform.IsChildOf(other.transform))) continue;
            if (go.GetComponentInChildren<Character>(true) != null || go.GetComponentInChildren<Terrain>(true) != null ||
                go.GetComponentInChildren<JourneyStoneTower>(true) != null)
            { Debug.LogWarning("Select individual rock objects, not Player, Terrain or the target statue.", go); continue; }
            Configure(go, root); count++;
        }
        if (count == 0) Debug.LogWarning("Select one or more individual rock objects in the active scene Hierarchy first.");
        else { EditorSceneManager.MarkSceneDirty(scene); Debug.Log(count + " rocks are throwable and linked under Stones Root. Save the scene."); }
    }
    [MenuItem("Quran Kids/Stones/Create Test Throwable Stone")]
    private static void CreateTest()
    {
        if (!CanEdit()) return;
        Scene scene = SceneManager.GetActiveScene(); Transform root = FindStonesRoot(scene);
        if (root == null) return;
        var player = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Character>(true)).FirstOrDefault(c => c.IsPlayer);
        var stone = GameObject.CreatePrimitive(PrimitiveType.Sphere); stone.name = "Test Throwable Stone";
        Undo.RegisterCreatedObjectUndo(stone, "Create test stone"); SceneManager.MoveGameObjectToScene(stone, scene);
        stone.transform.localScale = Vector3.one * 0.24f;
        Vector3 position = player != null ? player.transform.position + Vector3.forward * 1.5f : Vector3.up * 2f;
        if (Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore)) position = hit.point + Vector3.up * 0.14f;
        stone.transform.position = position; Configure(stone, root);
        EditorSceneManager.MarkSceneDirty(scene); Selection.activeGameObject = stone;
    }
    private static bool CanEdit()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode) return true;
        Debug.LogError("Exit Play Mode before setting up throwable stones."); return false;
    }
    private static Transform FindStonesRoot(Scene scene)
    {
        var controller = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<JourneyStoneThrowController>(true)).FirstOrDefault();
        if (controller == null) { Debug.LogError("The active scene needs its JourneyStoneThrowController first."); return null; }
        var data = new SerializedObject(controller); var field = data.FindProperty("stonesRoot");
        Transform root = field.objectReferenceValue as Transform;
        if (root == null)
        {
            var go = new GameObject("Throwable Stones"); Undo.RegisterCreatedObjectUndo(go, "Create stones root");
            SceneManager.MoveGameObjectToScene(go, scene); root = go.transform;
            field.objectReferenceValue = root; data.ApplyModifiedProperties(); JourneyWorldSetupUtility.Dirty(controller);
        }
        return root;
    }
    private static void Configure(GameObject go, Transform stonesRoot)
    {
        var collider = go.GetComponents<Collider>().FirstOrDefault(c => !c.isTrigger &&
            (c is SphereCollider || c is BoxCollider || c is CapsuleCollider || c is MeshCollider mesh && mesh.convex));
        if (collider == null)
        {
            var sphere = JourneyWorldSetupUtility.Add<SphereCollider>(go);
            var temporary = go.AddComponent<BoxCollider>();
            JourneyWorldSetupUtility.FitHull(temporary, go.transform);
            sphere.center = temporary.center; sphere.radius = Mathf.Max(0.01f, Mathf.Max(temporary.size.x, Mathf.Max(temporary.size.y, temporary.size.z)) * 0.5f);
            UnityEngine.Object.DestroyImmediate(temporary); collider = sphere;
        }
        foreach (var other in go.GetComponentsInChildren<Collider>(true))
        { Undo.RecordObject(other, "Use one stone collider"); other.enabled = other == collider; JourneyWorldSetupUtility.Dirty(other); }
        collider.isTrigger = false;
        var material = AssetDatabase.LoadMainAssetAtPath("Assets/Game/Scripts/StoneThrowing/Stone_PhysicsMaterial.physicMaterial");
        if (material != null)
        {
            var data = new SerializedObject(collider); data.FindProperty("m_Material").objectReferenceValue = material; data.ApplyModifiedProperties();
        }
        var body = JourneyWorldSetupUtility.Add<Rigidbody>(go);
        Undo.RecordObject(body, "Configure stone rigidbody"); body.mass = 2f; body.linearDamping = 0f; body.angularDamping = 1f;
        body.useGravity = true; body.isKinematic = false; body.constraints = RigidbodyConstraints.None;
        body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        JourneyWorldSetupUtility.Add<JourneyThrowableStone>(go);
        foreach (var child in go.GetComponentsInChildren<Transform>(true))
        { Undo.RecordObject(child.gameObject, "Allow a dynamic stone to move"); child.gameObject.isStatic = false; JourneyWorldSetupUtility.Dirty(child.gameObject); }
        if (!go.transform.IsChildOf(stonesRoot)) Undo.SetTransformParent(go.transform, stonesRoot, "Link stone to pickup controller");
        JourneyWorldSetupUtility.Dirty(body); JourneyWorldSetupUtility.Dirty(collider);
    }
}
#endif
