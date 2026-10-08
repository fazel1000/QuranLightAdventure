#if UNITY_EDITOR
using System;
using System.Linq;
using GameCreator.Runtime.Characters;
using RTLTMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

public sealed class JourneyCombatSetup : EditorWindow
{
    private Character player;
    private Animator playerAnimator;
    private AnimationClip swordAttack;
    private JourneyMenuController menu;
    private JourneyStoneThrowController stoneController;
    private Transform startPoint;
    private Button attackButton;
    private Image healthFill;
    private RTLTextMeshPro healthPercentText;
    private GameObject skeletonEnemy;
    private BoxCollider patrolArea;
    private Vector2 scroll;
    private string result;
    private const string Folder = "Assets/Game/Animations/Combat/Generated";
    private const string SkeletonModel = "Assets/Packages/Fantasy animated characters pack/Skeleton animated character/Model/Skeleton.fbx";

    [MenuItem("Quran Kids/Setup Journey Combat")]
    private static void Open() { GetWindow<JourneyCombatSetup>("Journey Combat"); }
    private void OnEnable() { FindSceneReferences(); }

    private void FindSceneReferences()
    {
        player = FindObjectsByType<Character>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(x => x.IsPlayer);
        if (player != null) playerAnimator = player.GetComponentsInChildren<Animator>(true).FirstOrDefault(x => x.avatar != null && x.avatar.isHuman);
        menu = FindFirstObjectByType<JourneyMenuController>(FindObjectsInactive.Include);
        stoneController = FindFirstObjectByType<JourneyStoneThrowController>(FindObjectsInactive.Include);
        if (menu != null) startPoint = new SerializedObject(menu).FindProperty("startPoint")?.objectReferenceValue as Transform;
        var sceneObjects = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform skeleton = sceneObjects.FirstOrDefault(x => x.name == "SkeletonEnemy");
        if (skeleton != null) skeletonEnemy = skeleton.gameObject;
        patrolArea = sceneObjects.FirstOrDefault(x => x.name == "SkeletonPatrolArea")?.GetComponent<BoxCollider>();
        attackButton = sceneObjects.FirstOrDefault(x => x.name == "AttackButton")?.GetComponent<Button>();
        healthFill = sceneObjects.FirstOrDefault(x => x.name == "HealthFill")?.GetComponent<Image>();
        healthPercentText = sceneObjects.FirstOrDefault(x => x.name == "HealthPercentText")?.GetComponent<RTLTextMeshPro>();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Connects the selected scene instances. Copies the player controller, preserves locomotion/stone layers and creates a Skeleton controller. Use outside Play Mode.", MessageType.Info);
        if (GUILayout.Button("Find Scene References")) FindSceneReferences();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        player = (Character)EditorGUILayout.ObjectField("Player", player, typeof(Character), true);
        playerAnimator = (Animator)EditorGUILayout.ObjectField("Player Animator", playerAnimator, typeof(Animator), true);
        swordAttack = (AnimationClip)EditorGUILayout.ObjectField("Sword Attack Clip", swordAttack, typeof(AnimationClip), false);
        menu = (JourneyMenuController)EditorGUILayout.ObjectField("Menu", menu, typeof(JourneyMenuController), true);
        startPoint = (Transform)EditorGUILayout.ObjectField("Start Point", startPoint, typeof(Transform), true);
        stoneController = (JourneyStoneThrowController)EditorGUILayout.ObjectField("Stone Controller", stoneController, typeof(JourneyStoneThrowController), true);
        attackButton = (Button)EditorGUILayout.ObjectField("Attack Button", attackButton, typeof(Button), true);
        healthFill = (Image)EditorGUILayout.ObjectField("Health Fill", healthFill, typeof(Image), true);
        healthPercentText = (RTLTextMeshPro)EditorGUILayout.ObjectField("Health Percent Text", healthPercentText, typeof(RTLTextMeshPro), true);
        skeletonEnemy = (GameObject)EditorGUILayout.ObjectField("Skeleton Enemy", skeletonEnemy, typeof(GameObject), true);
        patrolArea = (BoxCollider)EditorGUILayout.ObjectField("Patrol Area", patrolArea, typeof(BoxCollider), true);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            if (GUILayout.Button("Create and Connect"))
                try { Build(); } catch (Exception error) { result = error.Message; Debug.LogException(error); }
        if (!string.IsNullOrEmpty(result)) EditorGUILayout.HelpBox(result, MessageType.Info);
        EditorGUILayout.EndScrollView();
    }

    private void Build()
    {
        if (player == null || playerAnimator == null || swordAttack == null || menu == null ||
            startPoint == null || attackButton == null || healthFill == null || healthPercentText == null ||
            skeletonEnemy == null || patrolArea == null)
            throw new InvalidOperationException("Fill all fields except the optional Stone Controller.");
        if (!player.gameObject.scene.IsValid() || !skeletonEnemy.scene.IsValid() ||
            !playerAnimator.transform.IsChildOf(player.transform))
            throw new InvalidOperationException("Use the scene Player and its Character 05 Animator, plus the scene SkeletonEnemy parent.");
        if (!swordAttack.isHumanMotion || swordAttack.legacy || playerAnimator.avatar == null || !playerAnimator.avatar.isValid || !playerAnimator.avatar.isHuman)
            throw new InvalidOperationException("Import Sword Attack and Character 05 as Humanoid with valid Avatars. Generic transform curves are not retargeted by renaming bones.");
        if (patrolArea.transform.IsChildOf(skeletonEnemy.transform) || Vector3.Dot(patrolArea.transform.up, Vector3.up) < 0.999f)
            throw new InvalidOperationException("Patrol Area must be separate from the enemy; rotate it around Y only.");
        var original = playerAnimator.runtimeAnimatorController as AnimatorController;
        if (original == null) throw new InvalidOperationException("Player needs its current locomotion AnimatorController, including the existing stone layer.");
        Animator skeletonAnimator = skeletonEnemy.GetComponentInChildren<Animator>(true);
        if (skeletonAnimator == null) throw new InvalidOperationException("SkeletonEnemy must contain the Skeleton model and Animator.");
        AnimationClip[] embedded = AssetDatabase.LoadAllAssetsAtPath(SkeletonModel).OfType<AnimationClip>().ToArray();
        AnimationClip idle = FindClip(embedded, "Skeleton_Idle");
        AnimationClip run = FindClip(embedded, "Skeleton_Run");
        AnimationClip attack = FindClip(embedded, "Skeleton_Attak");
        AnimationClip death = FindClip(embedded, "Skeleton_Death");
        EnsureFolder(Folder);
        string playerPath = AssetDatabase.GenerateUniqueAssetPath(Folder + "/Player_With_Combat.controller");
        if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original), playerPath))
            throw new InvalidOperationException("Could not copy the player's controller.");
        var copy = AssetDatabase.LoadAssetAtPath<AnimatorController>(playerPath);
        int previousLayer = Array.FindIndex(copy.layers, x => x.name == JourneyPlayerCombat.LayerName);
        if (previousLayer >= 0) copy.RemoveLayer(previousLayer);
        foreach (AnimatorControllerParameter parameter in copy.parameters)
            if (parameter.name == "JourneySwordSpeed") copy.RemoveParameter(parameter);
        copy.AddParameter(new AnimatorControllerParameter { name = "JourneySwordSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        copy.AddLayer(JourneyPlayerCombat.LayerName);
        var layers = copy.layers;
        var swordLayer = layers[layers.Length - 1];
        swordLayer.defaultWeight = 0f;
        swordLayer.blendingMode = AnimatorLayerBlendingMode.Override;
        copy.layers = layers;
        AnimatorState idleState = swordLayer.stateMachine.AddState("Idle");
        idleState.writeDefaultValues = false;
        swordLayer.stateMachine.defaultState = idleState;
        AnimatorState swordState = swordLayer.stateMachine.AddState("SwordAttack");
        swordState.motion = CopyClip(swordAttack, "SwordAttack", false);
        swordState.writeDefaultValues = false;
        swordState.speedParameter = "JourneySwordSpeed";
        swordState.speedParameterActive = true;

        string enemyPath = AssetDatabase.GenerateUniqueAssetPath(Folder + "/Skeleton_Combat.controller");
        var enemyController = AnimatorController.CreateAnimatorControllerAtPath(enemyPath);
        AnimatorStateMachine sm = enemyController.layers[0].stateMachine;
        sm.defaultState = AddEnemyState(sm, "Idle", idle, true);
        AddEnemyState(sm, "Run", run, true);
        AddEnemyState(sm, "Attack", attack, false);
        AddEnemyState(sm, "Death", death, false);
        EditorUtility.SetDirty(copy);
        EditorUtility.SetDirty(enemyController);
        AssetDatabase.SaveAssets();

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Connect Journey Combat");
        var combat = GetOrAdd<JourneyPlayerCombat>(player.gameObject);
        Undo.RecordObject(combat, "Configure player combat");
        combat.player = player;
        combat.playerAnimator = playerAnimator;
        combat.menu = menu;
        combat.startPoint = startPoint;
        combat.stoneController = stoneController;
        combat.attackButton = attackButton;
        combat.healthFill = healthFill;
        combat.healthPercentText = healthPercentText;
        Undo.RecordObject(playerAnimator, "Connect sword animation layer");
        playerAnimator.runtimeAnimatorController = copy;
        Undo.RecordObject(healthFill, "Configure health fill");
        healthFill.type = Image.Type.Filled;
        healthFill.fillMethod = Image.FillMethod.Horizontal;
        healthFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        healthFill.fillAmount = 1f;
        healthFill.raycastTarget = false;
        Undo.RecordObject(healthPercentText, "Configure health text");
        healthPercentText.text = "100%";
        healthPercentText.raycastTarget = false;
        var agent = GetOrAdd<NavMeshAgent>(skeletonEnemy);
        Undo.RecordObject(agent, "Configure skeleton navigation");
        agent.stoppingDistance = 0.1f;
        agent.speed = 1.5f;
        agent.acceleration = 8f;
        agent.angularSpeed = 360f;
        var body = GetOrAdd<CapsuleCollider>(skeletonEnemy);
        Undo.RecordObject(body, "Configure skeleton body");
        body.isTrigger = false;
        body.radius = agent.radius;
        body.height = agent.height;
        body.center = Vector3.up * (agent.height * 0.5f + agent.baseOffset);
        var rigidbody = GetOrAdd<Rigidbody>(skeletonEnemy);
        Undo.RecordObject(rigidbody, "Configure navigation-driven body");
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;
        var enemy = GetOrAdd<JourneySkeletonEnemy>(skeletonEnemy);
        Undo.RecordObject(enemy, "Configure skeleton combat");
        enemy.playerCombat = combat;
        enemy.patrolArea = patrolArea;
        enemy.modelAnimator = skeletonAnimator;
        enemy.corpseVisibleSeconds = Mathf.Max(5f, death.length + 0.25f);
        Undo.RecordObject(patrolArea, "Configure patrol volume");
        patrolArea.isTrigger = true;
        Undo.RecordObject(skeletonAnimator, "Connect skeleton animation");
        skeletonAnimator.runtimeAnimatorController = enemyController;
        skeletonAnimator.applyRootMotion = false;
        foreach (UnityEngine.Object changed in new UnityEngine.Object[] { combat, enemy, agent, body, rigidbody, patrolArea, playerAnimator, skeletonAnimator, healthFill, healthPercentText })
        {
            EditorUtility.SetDirty(changed);
            PrefabUtility.RecordPrefabInstancePropertyModifications(changed);
        }
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        EditorSceneManager.MarkSceneDirty(skeletonEnemy.scene);
        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = player.gameObject;
        result = "Connected. Save the scene. Attack Button On Click needs no manual entry. Test one enemy before duplicating. Set a Source Image on HealthFill if empty. New assets: " + playerPath + " and " + enemyPath;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    { return target.GetComponent<T>() ?? Undo.AddComponent<T>(target); }
    private static AnimationClip FindClip(AnimationClip[] clips, string name)
    {
        AnimationClip clip = clips.FirstOrDefault(x => x.name == name);
        if (clip == null) throw new InvalidOperationException("Missing embedded clip " + name + " in " + SkeletonModel);
        return clip;
    }
    private static AnimatorState AddEnemyState(AnimatorStateMachine sm, string name, AnimationClip source, bool loop)
    {
        AnimatorState state = sm.AddState(name);
        state.motion = CopyClip(source, "Skeleton_" + name, loop);
        state.writeDefaultValues = false;
        return state;
    }
    private static AnimationClip CopyClip(AnimationClip source, string name, bool loop)
    {
        AnimationClip clip = Instantiate(source);
        clip.name = name;
        // The scripts own hit timing, so imported events cannot produce duplicate hits.
        AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        if (source.isHumanMotion)
        {
            settings.keepOriginalPositionXZ = true;
            settings.keepOriginalPositionY = true;
            settings.keepOriginalOrientation = true;
            settings.loopBlendPositionXZ = true;
            settings.loopBlendPositionY = true;
            settings.loopBlendOrientation = true;
        }
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + name + ".anim"));
        return clip;
    }
    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}

#endif
