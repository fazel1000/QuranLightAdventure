#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using GameCreator.Runtime.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class JourneyRiderGuardSetup
{
    private static string Source(bool zombie) => zombie ? "Assets/Zombie Sodier/Rider zombie animated character/Prefab/Rider_zombie.prefab" : "Assets/Horse Soldier/Rider knight animated character/Prefab/Rider_knight.prefab";
    private static string Model(bool zombie) => zombie ? "Assets/Zombie Sodier/Rider zombie animated character/Model/Rider_zombie.fbx" : "Assets/Horse Soldier/Rider knight animated character/Model/Rider_knight.fbx";
    [MenuItem("Quran Kids/Guards/Configure Selected Riders")]
    private static void ConfigureSelected()
    {
        if (EditorApplication.isPlaying) return;
        var result = new HashSet<JourneyRiderGuard>();
        foreach (var selected in Selection.gameObjects)
        {
            if (!selected.scene.IsValid()) continue;
            var guard = selected.GetComponent<JourneyRiderGuard>() ?? selected.GetComponentInParent<JourneyRiderGuard>() ?? selected.GetComponentInChildren<JourneyRiderGuard>(true);
            if (guard != null) { Upgrade(guard); result.Add(guard); continue; }
            var model = PrefabUtility.GetOutermostPrefabInstanceRoot(selected) ?? selected;
            string path = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(model));
            if (!path.Contains("Horse Soldier") && !path.Contains("Zombie Sodier")) continue;
            var configured = Configure(model, path.Contains("Zombie Sodier")); if (configured != null) result.Add(configured);
        }
        if (result.Count == 0) Debug.LogWarning("Select Rider_knight / Rider_zombie instances or their patrol groups in Hierarchy.");
        else { Selection.objects = result.Select(item => (Object)item.gameObject).ToArray(); AssetDatabase.SaveAssets(); }
    }
    [MenuItem("Quran Kids/Guards/Upgrade Guards In Active Scene")]
    public static void UpgradeAll()
    {
        if (EditorApplication.isPlaying) return;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene(); int count = 0;
        foreach (var guard in Object.FindObjectsByType<JourneyRiderGuard>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (guard.gameObject.scene == scene) { Upgrade(guard); count++; }
        Debug.Log("Updated " + count + " guards. Each patrol has its own point list; running scale is 0.8 and distance sleeping is enabled.");
    }
    [MenuItem("Quran Kids/Guards/Create Knight Patrol")]
    private static void CreateKnight() { Create(false); }
    [MenuItem("Quran Kids/Guards/Create Zombie Patrol")]
    private static void CreateZombie() { Create(true); }
    [MenuItem("Quran Kids/Create Rider Knight Guard Test")]
    private static void LegacyCreate() { Create(false); }
    private static void Create(bool zombie)
    {
        if (EditorApplication.isPlaying) return;
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source(zombie));
        if (source == null) { Debug.LogError("Rider prefab missing: " + Source(zombie)); return; }
        Vector3 position = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
        if (JourneyGuardGround.TryTerrain(position, 35f, out var ground, out _)) position = ground;
        else
        {
            var player = Object.FindObjectsByType<Character>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(item => item.IsPlayer);
            if (player != null && JourneyGuardGround.TryTerrain(player.transform.position, 35f, out ground, out _)) position = ground;
            else
            {
                var terrain = Terrain.activeTerrains.FirstOrDefault(item => item != null && item.terrainData != null);
                if (terrain != null && JourneyGuardGround.TryTerrain(terrain.transform.position + terrain.terrainData.size * 0.5f, 35f, out ground, out _)) position = ground;
            }
        }
        var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
        Undo.RegisterCreatedObjectUndo(model, "Create rider"); model.transform.position = position;
        var guard = Configure(model, zombie);
        if (guard != null)
        {
            Selection.activeGameObject = guard.gameObject;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(new Bounds(guard.transform.position, new Vector3(12, 8, 12)), false);
        }
    }
    private static JourneyRiderGuard Configure(GameObject model, bool zombie)
    {
        var controller = Controller(zombie, out var roar);
        if (controller == null) return null;
        Vector3 home = model.transform.position;
        if (JourneyGuardGround.TryTerrain(home, 60f, out var grounded, out _)) home = grounded;
        var group = new GameObject(zombie ? "Zombie Patrol" : "Knight Patrol");
        Undo.RegisterCreatedObjectUndo(group, "Create independent patrol"); group.transform.SetPositionAndRotation(home, Quaternion.Euler(0f, model.transform.eulerAngles.y, 0f));
        var actor = new GameObject(zombie ? "Zombie Guard" : "Knight Guard"); actor.transform.SetParent(group.transform, false);
        Undo.SetTransformParent(model.transform, actor.transform, "Wrap rider model");
        Undo.RecordObject(model.transform, "Center rider model"); model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
        var animator = model.GetComponentInChildren<Animator>(true);
        if (animator == null) { Debug.LogError("Rider Animator is missing.", model); return null; }
        Undo.RecordObject(animator, "Assign patrol controller"); animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.CullCompletely;
        PrefabUtility.RecordPrefabInstancePropertyModifications(animator); PrefabUtility.RecordPrefabInstancePropertyModifications(model.transform);
        var guard = Undo.AddComponent<JourneyRiderGuard>(actor);
        guard.animator = animator; guard.roarAnimation = roar;
        var points = new GameObject("Patrol Points"); points.transform.SetParent(group.transform, false); guard.routeRoot = points.transform;
        var homeObject = new GameObject("Point 01 Home"); homeObject.transform.SetParent(points.transform, false);
        var lookout = new GameObject("Point 02"); lookout.transform.SetParent(points.transform, false);
        Vector3 destination = home + model.transform.forward * 6f;
        if (JourneyGuardGround.TryTerrain(destination, guard.maximumSlope, out var target, out _) && JourneyGuardGround.SafeSegment(home, target, guard.maximumSlope)) destination = target;
        lookout.transform.position = destination;
        guard.patrolPoints.Add(new JourneyRiderGuard.PatrolPoint { point = homeObject.transform, waitSeconds = 10f, roarHere = false, runToPoint = true });
        guard.patrolPoints.Add(new JourneyRiderGuard.PatrolPoint { point = lookout.transform, waitSeconds = 2f, roarHere = true });
        BindScene(guard); EditorUtility.SetDirty(guard); EditorSceneManager.MarkSceneDirty(group.scene);
        return guard;
    }
    private static void BindScene(JourneyRiderGuard guard)
    {
        guard.menu = Object.FindObjectsByType<JourneyMenuController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(item => item.gameObject.scene == guard.gameObject.scene);
        if (guard.distanceTarget == null) guard.distanceTarget = Object.FindObjectsByType<Character>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(item => item.IsPlayer && item.gameObject.scene == guard.gameObject.scene)?.transform;
    }
    private static void Upgrade(JourneyRiderGuard guard)
    {
        Undo.RecordObject(guard, "Upgrade patrol route"); guard.MigrateLegacyRoute();
        guard.runSpeedScale = 0.8f; guard.sleepWhenDistant = true; BindScene(guard);
        if (guard.animator != null)
        { Undo.RecordObject(guard.animator, "Cull distant animation"); guard.animator.cullingMode = AnimatorCullingMode.CullCompletely; PrefabUtility.RecordPrefabInstancePropertyModifications(guard.animator); }
        // Normalize the original test group so a prefab instance spawns at its own origin.
        var parent = guard.transform.parent;
        if (parent != null && parent.name == "Rider Knight Patrol Test")
        {
            var children = parent.Cast<Transform>().ToArray();
            var positions = children.Select(item => item.position).ToArray();
            Undo.RecordObject(parent, "Center patrol prefab root");
            foreach (var child in children) Undo.RecordObject(child, "Preserve patrol positions");
            parent.position = guard.patrolPoints.Count > 0 && guard.patrolPoints[0].point != null ? guard.patrolPoints[0].point.position : guard.transform.position;
            for (int i = 0; i < children.Length; i++) children[i].position = positions[i];
            Undo.RecordObject(parent.gameObject, "Rename patrol group");
            parent.name = "Knight Patrol";
        }
        EditorUtility.SetDirty(guard); PrefabUtility.RecordPrefabInstancePropertyModifications(guard); EditorSceneManager.MarkSceneDirty(guard.gameObject.scene);
    }
    public static void AddPoint(JourneyRiderGuard guard)
    {
        if (EditorApplication.isPlaying || EditorUtility.IsPersistent(guard)) return;
        Undo.RecordObject(guard, "Add patrol point"); guard.MigrateLegacyRoute();
        if (guard.routeRoot == null || guard.routeRoot.IsChildOf(guard.transform))
        {
            var root = new GameObject("Patrol Points"); Undo.RegisterCreatedObjectUndo(root, "Create patrol points");
            root.transform.SetParent(guard.transform.parent, false); guard.routeRoot = root.transform;
        }
        Vector3 position = guard.patrolPoints.Count > 0 && guard.patrolPoints[guard.patrolPoints.Count - 1].point != null ? guard.patrolPoints[guard.patrolPoints.Count - 1].point.position : guard.transform.position;
        position += guard.transform.forward * 5f;
        if (JourneyGuardGround.TryTerrain(position, guard.maximumSlope, out var ground, out _)) position = ground;
        var point = new GameObject("Point " + (guard.patrolPoints.Count + 1).ToString("00")); Undo.RegisterCreatedObjectUndo(point, "Create patrol point");
        point.transform.SetParent(guard.routeRoot, true); point.transform.position = position;
        guard.patrolPoints.Add(new JourneyRiderGuard.PatrolPoint { point = point.transform });
        EditorUtility.SetDirty(guard); PrefabUtility.RecordPrefabInstancePropertyModifications(guard); EditorSceneManager.MarkSceneDirty(guard.gameObject.scene);
        Selection.activeGameObject = guard.gameObject; SceneView.RepaintAll();
    }
    public static void MakeRouteUnique(JourneyRiderGuard guard)
    {
        if (EditorApplication.isPlaying || EditorUtility.IsPersistent(guard)) return;
        Undo.RecordObject(guard, "Make patrol route unique"); guard.MigrateLegacyRoute();
        var root = new GameObject("Patrol Points Unique"); Undo.RegisterCreatedObjectUndo(root, "Create unique route"); root.transform.SetParent(guard.transform.parent, false);
        var entries = new List<JourneyRiderGuard.PatrolPoint>();
        foreach (var item in guard.patrolPoints)
        {
            if (item == null || item.point == null) continue;
            var point = new GameObject("Point " + (entries.Count + 1).ToString("00")); point.transform.SetParent(root.transform, false); point.transform.position = item.point.position;
            entries.Add(new JourneyRiderGuard.PatrolPoint { point = point.transform, waitSeconds = item.waitSeconds, roarHere = item.roarHere, afterRoarSeconds = item.afterRoarSeconds, runToPoint = item.runToPoint });
        }
        guard.routeRoot = root.transform; guard.patrolPoints = entries;
        EditorUtility.SetDirty(guard); PrefabUtility.RecordPrefabInstancePropertyModifications(guard); EditorSceneManager.MarkSceneDirty(guard.gameObject.scene);
    }
    private static AnimatorController Controller(bool zombie, out AnimationClip roar)
    {
        string folder = zombie ? "Assets/Game/Animations/ZombieGuard" : "Assets/Game/Animations/RiderGuard";
        EnsureFolder(folder);
        var clips = AssetDatabase.LoadAllAssetsAtPath(Model(zombie)).OfType<AnimationClip>().Where(item => !item.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
        AnimationClip Find(string suffix) => clips.FirstOrDefault(item => item.name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        var idle = Find("_Idle"); var walk = Find("_Walk"); var run = Find("_Run"); var victory = Find("_Victory");
        roar = null;
        if (idle == null || walk == null || run == null || victory == null) { Debug.LogError("Rider animation clips are missing: " + Model(zombie)); return null; }
        var motions = new[] { ("Idle", idle, true), ("Rest", Find("_Idle_long") ?? idle, true), ("Walk", walk, true), ("Run", run, true), ("Roar", victory, false) };
        string path = folder + (zombie ? "/JourneyZombieGuard.controller" : "/JourneyRiderGuard.controller");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path) ?? AnimatorController.CreateAnimatorControllerAtPath(path);
        var machine = controller.layers[0].stateMachine;
        foreach (var item in motions)
        {
            string clipPath = folder + "/" + item.Item1 + ".anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null)
            {
                clip = Object.Instantiate(item.Item2); clip.name = item.Item1; clip.hideFlags = HideFlags.None;
                var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = item.Item3; AnimationUtility.SetAnimationClipSettings(clip, settings);
                AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>()); AssetDatabase.CreateAsset(clip, clipPath);
            }
            var state = machine.states.Select(entry => entry.state).FirstOrDefault(entry => entry.name == item.Item1) ?? machine.AddState(item.Item1);
            if (state.motion == null) state.motion = clip;
            if (item.Item1 == "Idle") machine.defaultState = state;
            if (item.Item1 == "Roar") roar = clip;
        }
        EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets(); return controller;
    }
    private static void EnsureFolder(string path)
    {
        string current = "Assets";
        foreach (string part in path.Split('/').Skip(1)) { if (!AssetDatabase.IsValidFolder(current + "/" + part)) AssetDatabase.CreateFolder(current, part); current += "/" + part; }
    }
}
#endif
