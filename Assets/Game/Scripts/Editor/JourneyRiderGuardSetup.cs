#if UNITY_EDITOR
using System;
using System.Linq;
using GameCreator.Runtime.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class JourneyRiderGuardSetup
{
    private const string Source = "Assets/Horse Soldier/Rider knight animated character/Prefab/Rider_knight.prefab";
    private const string Model = "Assets/Horse Soldier/Rider knight animated character/Model/Rider_knight.fbx";
    private const string Folder = "Assets/Game/Animations/RiderGuard";
    [MenuItem("Quran Kids/Create Rider Knight Guard Test")]
    private static void Setup()
    {
        if (EditorApplication.isPlaying) return;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var previous = Object.FindObjectsByType<JourneyRiderGuard>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(item => item.gameObject.scene == scene);
        if (previous != null) { Selection.activeGameObject = previous.gameObject; Debug.Log("The guard test already exists. Move its Home and Lookout route points.", previous); return; }
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        var clips = AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Where(clip => !clip.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
        AnimationClip Find(string suffix) => clips.FirstOrDefault(clip => clip.name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        var idle = Find("_Idle"); var walk = Find("_Walk"); var run = Find("_Run"); var roar = Find("_Victory"); var rest = Find("_Idle_long") ?? idle;
        if (source == null || idle == null || walk == null || run == null || roar == null)
        { Debug.LogError("Horse Soldier source prefab or Idle/Walk/Run/Victory clips are missing."); return; }
        var player = Object.FindObjectsByType<Character>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(item => item.IsPlayer && item.gameObject.scene == scene);
        Vector3 origin = player != null ? player.transform.position : SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        var menu = Object.FindObjectsByType<JourneyMenuController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(item => item.gameObject.scene == scene);
        if (menu != null)
        {
            var data = new SerializedObject(menu);
            var start = data.FindProperty("startPoint")?.objectReferenceValue as Transform;
            if (start != null) origin = start.position;
        }
        var existingModel = Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(item => item.gameObject.scene == scene)
            .Select(item => PrefabUtility.GetOutermostPrefabInstanceRoot(item.gameObject))
            .Where(item => item != null).Distinct()
            .FirstOrDefault(item => AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(item)) == Source);
        Vector3 home = Vector3.zero, lookout = Vector3.zero;
        bool routeFound = existingModel != null && JourneyGuardGround.TryTerrain(existingModel.transform.position, 35f, out home, out _)
            && FindLookout(home, out lookout);
        // Existing rider position is preferred; otherwise find a nearby walkable hill.
        if (!routeFound && !FindRoute(origin, out home, out lookout))
        { Debug.LogError("No short walkable hill route found. Put the rider or player near an active Terrain and run this command again."); return; }
        EnsureFolder(Folder);
        AnimationClip idleCopy = Copy(idle, "Idle", true), restCopy = Copy(rest, "Rest", true);
        AnimationClip walkCopy = Copy(walk, "Walk", true), runCopy = Copy(run, "Run", true), roarCopy = Copy(roar, "Roar", false);
        string controllerPath = Folder + "/JourneyRiderGuard.controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var item in new[] { ("Idle", idleCopy), ("Rest", restCopy), ("Walk", walkCopy), ("Run", runCopy), ("Roar", roarCopy) })
            {
                var state = machine.AddState(item.Item1); state.motion = item.Item2;
                if (item.Item1 == "Idle") machine.defaultState = state;
            }
            EditorUtility.SetDirty(controller);
        }
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Create rider guard test");
        var root = new GameObject("Rider Knight Patrol Test"); Undo.RegisterCreatedObjectUndo(root, "Create patrol test");
        var homeObject = new GameObject("Home"); homeObject.transform.SetParent(root.transform); homeObject.transform.position = home;
        var lookoutObject = new GameObject("Lookout"); lookoutObject.transform.SetParent(root.transform); lookoutObject.transform.position = lookout;
        var actor = new GameObject("Rider Knight Guard"); actor.transform.SetParent(root.transform); actor.transform.position = home;
        actor.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(lookout - home, Vector3.up));
        GameObject model;
        if (existingModel != null)
        {
            model = existingModel;
            Undo.SetTransformParent(model.transform, actor.transform, "Use existing rider for patrol");
            Undo.RecordObject(model.transform, "Center rider visual");
            Undo.RecordObject(model, "Enable rider test"); model.SetActive(true);
        }
        else model = (GameObject)PrefabUtility.InstantiatePrefab(source, actor.transform);
        model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
        PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(model);
        var animator = model.GetComponentInChildren<Animator>(true);
        Undo.RecordObject(animator, "Assign patrol controller");
        animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
        var guard = actor.AddComponent<JourneyRiderGuard>(); guard.animator = animator;
        guard.homePoint = homeObject.transform; guard.lookoutPoint = lookoutObject.transform;
        guard.roarAnimation = roarCopy;
        guard.menu = menu;
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
        Undo.CollapseUndoOperations(group); Selection.activeGameObject = actor;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(new Bounds(home, new Vector3(12, 8, 12)), false);
        Debug.Log("Rider guard placed on a nearby walkable hill. Save scene. On gameplay: walk to Lookout, pause, Victory with roar audio, run home, idle 10 seconds, repeat. Assign sound clips on JourneyRiderGuard.", guard);
    }
    private static bool FindRoute(Vector3 origin, out Vector3 home, out Vector3 lookout)
    {
        home = lookout = Vector3.zero; float best = float.NegativeInfinity; bool found = false;
        var terrains = Terrain.activeTerrains;
        if (terrains.Length == 0) return false;
        if (!JourneyGuardGround.TryTerrain(origin, 89f, out _, out _))
            origin = terrains[0].transform.position + terrains[0].terrainData.size * 0.5f;
        for (int ring = 1; ring <= 3; ring++)
        for (int angle = 0; angle < 16; angle++)
        {
            float radians = angle * Mathf.PI * 2f / 16f;
            Vector3 candidate = origin + new Vector3(Mathf.Cos(radians), 0, Mathf.Sin(radians)) * (12f * ring);
            if (!JourneyGuardGround.TryTerrain(candidate, 35f, out var ground, out _) || !FindLookout(ground, out var destination)) continue;
            float score = ground.y - Vector3.Distance(origin, ground) * 0.02f;
            if (score <= best) continue;
            best = score; home = ground; lookout = destination; found = true;
        }
        return found;
    }
    private static bool FindLookout(Vector3 home, out Vector3 destination)
    {
        destination = home; float highest = float.NegativeInfinity; bool found = false;
        for (int i = 0; i < 12; i++)
        {
            float angle = i * Mathf.PI * 2f / 12f;
            Vector3 point = home + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 8f;
            if (!JourneyGuardGround.TryTerrain(point, 35f, out var ground, out _) || ground.y <= highest || !JourneyGuardGround.SafeSegment(home, ground, 35f)) continue;
            highest = ground.y; destination = ground; found = true;
        }
        return found;
    }
    private static AnimationClip Copy(AnimationClip source, string name, bool loop)
    {
        string path = Folder + "/" + name + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip != null) return clip;
        clip = Object.Instantiate(source); clip.name = name; clip.hideFlags = HideFlags.None;
        var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
        AssetDatabase.CreateAsset(clip, path); return clip;
    }
    private static void EnsureFolder(string path)
    {
        string current = "Assets";
        foreach (string part in path.Split('/').Skip(1))
        { if (!AssetDatabase.IsValidFolder(current + "/" + part)) AssetDatabase.CreateFolder(current, part); current += "/" + part; }
    }
}
#endif
