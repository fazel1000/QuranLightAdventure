using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class JourneyStoneAnimatorSetup : EditorWindow
{
    private Animator playerAnimator;
    private AnimationClip pickup, throwing;
    private JourneyStoneThrowController throwController;
    private string result;
    private const string LayerName = "Journey Stone Actions";

    [MenuItem("Quran Kids/Setup Stone Animator")]
    private static void Open() { GetWindow<JourneyStoneAnimatorSetup>("Stone Animator"); }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Creates a COPY of the player's current controller and adds a stone-action layer. Do not assign the standalone Stone_Thowing template over locomotion.", MessageType.Info);
        playerAnimator = (Animator)EditorGUILayout.ObjectField("Player Animator", playerAnimator, typeof(Animator), true);
        pickup = (AnimationClip)EditorGUILayout.ObjectField("Pick Up Clip", pickup, typeof(AnimationClip), false);
        throwing = (AnimationClip)EditorGUILayout.ObjectField("Throw Clip", throwing, typeof(AnimationClip), false);
        throwController = (JourneyStoneThrowController)EditorGUILayout.ObjectField("Throw Controller", throwController, typeof(JourneyStoneThrowController), true);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || !playerAnimator || !pickup || !throwing || !throwController))
            if (GUILayout.Button("Create and Connect"))
                try { Build(); } catch (Exception ex) { result = ex.Message; Debug.LogException(ex); }
        if (!string.IsNullOrEmpty(result)) EditorGUILayout.HelpBox(result, MessageType.Info);
    }

    private void CheckClip(AnimationClip clip)
    {
        if (clip.legacy) throw new InvalidOperationException("The clip must not be Legacy: " + clip.name);
        if (clip.isHumanMotion && playerAnimator.isHuman) return;
        // Uploaded clips contain explicit mixamorig transform tracks. Do not assume retargeting.
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            if (binding.type == typeof(Transform) && !string.IsNullOrEmpty(binding.path) &&
                playerAnimator.transform.Find(binding.path) == null)
                throw new InvalidOperationException("Clip skeleton path is missing below this Animator: " + binding.path +
                    ". Use the matching model Animator, or reimport the original FBX as Humanoid; renaming a controller cannot retarget transform curves.");
    }

    private void Build()
    {
        if (!(playerAnimator.runtimeAnimatorController is AnimatorController original))
            throw new InvalidOperationException("Player Animator needs its existing AnimatorController. If Game Creator supplies an OverrideController or a controller at runtime, keep it unchanged and inspect that setup before integrating.");
        if (!playerAnimator.gameObject.scene.IsValid() || !throwController.gameObject.scene.IsValid())
            throw new InvalidOperationException("Assign scene instances of Player Animator and Throw Controller.");
        if (original.layers.Any(x => x.name == LayerName))
            throw new InvalidOperationException("Stone layer already exists. Setup has not been duplicated.");
        CheckClip(pickup);
        CheckClip(throwing);
        foreach (string name in new[] { "Pickup", "Throw" })
            if (original.parameters.Any(p => p.name == name))
                throw new InvalidOperationException("Existing parameter conflicts with " + name + ". Original controller was not changed.");

        string folder = "Assets/Game/StoneAnimations";
        EnsureFolder(folder);
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/Player_With_Stone.controller");
        if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original), path))
            throw new InvalidOperationException("Could not copy the current controller.");
        var copy = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        copy.AddParameter("Pickup", AnimatorControllerParameterType.Trigger);
        copy.AddParameter("Throw", AnimatorControllerParameterType.Trigger);
        copy.AddLayer(LayerName);
        var layers = copy.layers;
        var layer = layers[layers.Length - 1];
        layer.defaultWeight = 0f;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        copy.layers = layers;
        var sm = layer.stateMachine;
        var idle = sm.AddState("Locomotion Pass Through", new Vector3(250, 40));
        idle.writeDefaultValues = false;
        idle.AddStateMachineBehaviour<JourneyStoneAnimationLayer>().active = false;
        sm.defaultState = idle;
        AddAction(sm, idle, "Pickup", CopyClip(pickup, folder), new Vector3(250, 160));
        AddAction(sm, idle, "Throw", CopyClip(throwing, folder), new Vector3(500, 160));
        EditorUtility.SetDirty(copy);
        AssetDatabase.SaveAssets();

        Undo.RecordObject(playerAnimator, "Connect stone animator copy");
        playerAnimator.runtimeAnimatorController = copy;
        PrefabUtility.RecordPrefabInstancePropertyModifications(playerAnimator);
        var fields = new SerializedObject(throwController);
        fields.FindProperty("animator").objectReferenceValue = playerAnimator;
        fields.FindProperty("pickupTrigger").stringValue = "Pickup";
        fields.FindProperty("throwTrigger").stringValue = "Throw";
        fields.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(throwController);
        EditorSceneManager.MarkSceneDirty(playerAnimator.gameObject.scene);
        EditorSceneManager.MarkSceneDirty(throwController.gameObject.scene);
        Selection.activeObject = copy;
        result = "Created " + path + ". Original controller preserved. Test movement and both animations. Release Delay still needs visual tuning.";
    }

    private static void AddAction(AnimatorStateMachine sm, AnimatorState idle, string trigger, AnimationClip clip, Vector3 position)
    {
        var state = sm.AddState(trigger, position);
        state.motion = clip;
        state.writeDefaultValues = false;
        state.AddStateMachineBehaviour<JourneyStoneAnimationLayer>().active = true;
        var enter = sm.AddAnyStateTransition(state);
        enter.hasExitTime = false;
        enter.hasFixedDuration = true;
        enter.duration = 0.12f;
        enter.canTransitionToSelf = false;
        enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        var exit = state.AddTransition(idle);
        exit.hasExitTime = true;
        exit.exitTime = 1f;
        exit.hasFixedDuration = true;
        exit.duration = 0.15f;
    }

    private static AnimationClip CopyClip(AnimationClip source, string folder)
    {
        var clip = Instantiate(source);
        clip.name = source.name;
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + source.name.Replace(' ', '_') + ".anim"));
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
