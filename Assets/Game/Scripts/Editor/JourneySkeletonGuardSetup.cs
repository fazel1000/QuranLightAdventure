#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class JourneySkeletonGuardSetup
{
    private const string PrefabPath = "Assets/Packages/Fantasy animated characters pack/Skeleton animated character/Prefab/Skeleton.prefab";
    [MenuItem("Quran Kids/Skeletons/Create Complete Skeleton Guard")]
    private static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("Exit Play Mode before creating a guard."); return; }
        Scene scene = SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var combat = roots.SelectMany(go => go.GetComponentsInChildren<JourneyPlayerCombat>(true)).FirstOrDefault();
        var template = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<JourneySkeletonEnemy>() : null;
        if (template == null) template = roots.SelectMany(go => go.GetComponentsInChildren<JourneySkeletonEnemy>(true))
            .FirstOrDefault(enemy => enemy.modelAnimator != null && enemy.bladeBase != null && enemy.bladeTip != null);
        GameObject asset = template == null ? AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) : null;
        if (combat == null || template == null && asset == null)
        { Debug.LogError("A configured Player Combat and the Skeleton model/template are required in this scene."); return; }
        Undo.IncrementCurrentGroup(); int groupIndex = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Create skeleton guard and independent route");
        var group = new GameObject("Skeleton Guard Group"); Undo.RegisterCreatedObjectUndo(group, "Create skeleton group"); SceneManager.MoveGameObjectToScene(group, scene);
        GameObject model = template != null ? Object.Instantiate(template.gameObject) : (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
        model.name = "Skeleton Guard"; Undo.RegisterCreatedObjectUndo(model, "Create complete skeleton"); model.transform.SetParent(group.transform, false);
        var enemy = JourneyWorldSetupUtility.Add<JourneySkeletonEnemy>(model); var agent = JourneyWorldSetupUtility.Add<NavMeshAgent>(model);
        Undo.RecordObjects(new Object[] { enemy, agent }, "Connect skeleton patrol and combat");
        enemy.playerCombat = combat; enemy.patrolArea = null; enemy.useDistanceDetection = true;
        if (enemy.modelAnimator == null) enemy.modelAnimator = model.GetComponentInChildren<Animator>(true);
        if (enemy.modelAnimator == null) enemy.modelAnimator = JourneyWorldSetupUtility.Add<Animator>(model);
        if (enemy.modelAnimator.runtimeAnimatorController == null)
        {
            string path = "Assets/Game/Animations/Combat/Generated/Skeleton_Combat.controller";
            enemy.modelAnimator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        }
        enemy.modelAnimator.applyRootMotion = false; enemy.modelAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        agent.radius = 0.3f; agent.height = 1.8f; agent.acceleration = 8f; agent.angularSpeed = 360f;
        agent.stoppingDistance = 0.1f; agent.autoBraking = true;
        var body = JourneyWorldSetupUtility.Add<CapsuleCollider>(model); body.radius = agent.radius; body.height = agent.height;
        body.center = Vector3.up * (agent.height * 0.5f + agent.baseOffset); body.isTrigger = false;
        var rigidbody = JourneyWorldSetupUtility.Add<Rigidbody>(model); rigidbody.isKinematic = true; rigidbody.useGravity = false;
        foreach (var part in model.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(part.gameObject, 0);
        if (enemy.effectsSource == null) enemy.effectsSource = JourneyWorldSetupUtility.Add<AudioSource>(model);
        enemy.effectsSource.playOnAwake = false; enemy.effectsSource.spatialBlend = 1f; enemy.effectsSource.maxDistance = 35f;
        Vector3 desired = Selection.activeTransform != null && Selection.activeTransform.gameObject.scene == scene ? Selection.activeTransform.position :
            SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : combat.startPoint.position;
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        bool onMesh = NavMesh.SamplePosition(desired, out NavMeshHit hit, 8f, filter);
        if (!onMesh && template != null) onMesh = NavMesh.SamplePosition(template.transform.position, out hit, 3f, filter);
        if (!onMesh) onMesh = NavMesh.SamplePosition(combat.startPoint.position, out hit, 8f, filter);
        group.transform.position = onMesh ? hit.position : desired; model.transform.localPosition = Vector3.zero;
        var route = new GameObject("Patrol Route"); Undo.RegisterCreatedObjectUndo(route, "Create independent patrol route"); route.transform.SetParent(group.transform, false);
        enemy.patrolPoints = new Transform[3]; Vector3[] offsets = { Vector3.zero, Vector3.forward * 3f, Vector3.right * 3f };
        for (int i = 0; i < offsets.Length; i++)
        {
            var point = new GameObject("Point " + (i + 1)); Undo.RegisterCreatedObjectUndo(point, "Create patrol point"); point.transform.SetParent(route.transform, false);
            Vector3 position = group.transform.position + offsets[i];
            point.transform.position = NavMesh.SamplePosition(position, out NavMeshHit sample, 3f, filter) ? sample.position : position;
            enemy.patrolPoints[i] = point.transform;
        }
        if (enemy.bladeBase == null || enemy.bladeTip == null)
            Debug.LogWarning("This fallback model needs Blade Base and Blade Tip on its animated weapon. Select an existing configured SkeletonEnemy before Create to copy its complete blade setup.", enemy);
        foreach (Object changed in new Object[] { enemy, agent, body, rigidbody, enemy.effectsSource, enemy.modelAnimator }) JourneyWorldSetupUtility.Dirty(changed);
        model.SetActive(true); EditorSceneManager.MarkSceneDirty(scene); Undo.CollapseUndoOperations(groupIndex); Selection.activeGameObject = group;
        if (onMesh) Debug.Log("Skeleton guard created with three independent patrol points. Move/add/reorder Patrol Points, set Detection Distance, then save scene. Duplicate the entire Skeleton Guard Group for independent routes.", enemy);
        else Debug.LogWarning("Skeleton created, but no baked NavMesh was found near the placement. Bake a NavMeshSurface or move the group onto an existing NavMesh before Play.", enemy);
    }
    public static void AddPoint(JourneySkeletonEnemy enemy)
    {
        if (enemy == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        Transform parent = enemy.patrolPoints.FirstOrDefault(point => point != null)?.parent;
        if (parent == null)
        {
            var route = new GameObject(enemy.name + " Patrol Route"); Undo.RegisterCreatedObjectUndo(route, "Create independent route");
            SceneManager.MoveGameObjectToScene(route, enemy.gameObject.scene); route.transform.SetParent(enemy.transform.parent, true); parent = route.transform;
        }
        var go = new GameObject("Point " + (enemy.patrolPoints.Length + 1)); Undo.RegisterCreatedObjectUndo(go, "Add patrol point"); go.transform.SetParent(parent, false);
        go.transform.position = enemy.patrolPoints.LastOrDefault(point => point != null) != null ? enemy.patrolPoints.Last(point => point != null).position + Vector3.forward * 2f : enemy.transform.position;
        Undo.RecordObject(enemy, "Add patrol waypoint"); var points = enemy.patrolPoints.ToList(); points.Add(go.transform); enemy.patrolPoints = points.ToArray();
        JourneyWorldSetupUtility.Dirty(enemy); EditorSceneManager.MarkSceneDirty(enemy.gameObject.scene); Selection.activeGameObject = go;
    }
}

[CustomEditor(typeof(JourneySkeletonEnemy)), CanEditMultipleObjects]
public sealed class JourneySkeletonEnemyEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("Add Independent Patrol Point")) JourneySkeletonGuardSetup.AddPoint((JourneySkeletonEnemy)target);
            if (GUILayout.Button("Snap Enemy and Patrol Points to NavMesh"))
            {
                foreach (var selected in targets)
                {
                    var enemy = (JourneySkeletonEnemy)selected;
                    foreach (var point in enemy.patrolPoints.Concat(new[] { enemy.transform }).Where(point => point != null))
                    {
                        if (!NavMesh.SamplePosition(point.position, out NavMeshHit hit, 5f, NavMesh.AllAreas)) { Debug.LogWarning("No baked NavMesh near " + point.name, point); continue; }
                        Undo.RecordObject(point, "Snap patrol placement"); point.position = hit.position; JourneyWorldSetupUtility.Dirty(point);
                    }
                    EditorSceneManager.MarkSceneDirty(enemy.gameObject.scene);
                }
            }
        }
    }
}
#endif
