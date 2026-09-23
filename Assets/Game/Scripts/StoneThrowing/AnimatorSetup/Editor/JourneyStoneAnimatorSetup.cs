using System;
using System.Linq;
using System.Collections.Generic;
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
    private AnimatorController locomotionController;
    private int remappedPaths;
    private const string LayerName = "Journey Stone Actions";

    [MenuItem("Quran Kids/Setup Stone Animator")]
    private static void Open() { GetWindow<JourneyStoneAnimatorSetup>("Stone Animator"); }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Creates a COPY of the player's current controller and adds a stone-action layer. Do not assign the standalone Stone_Thowing template over locomotion.", MessageType.Info);
        playerAnimator = (Animator)EditorGUILayout.ObjectField("Player Animator", playerAnimator, typeof(Animator), true);
        locomotionController = (AnimatorController)EditorGUILayout.ObjectField("Locomotion Controller", locomotionController, typeof(AnimatorController), false);
        pickup = (AnimationClip)EditorGUILayout.ObjectField("Pick Up Clip", pickup, typeof(AnimationClip), false);
        throwing = (AnimationClip)EditorGUILayout.ObjectField("Throw Clip", throwing, typeof(AnimationClip), false);
        throwController = (JourneyStoneThrowController)EditorGUILayout.ObjectField("Throw Controller", throwController, typeof(JourneyStoneThrowController), true);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || !playerAnimator || !pickup || !throwing || !throwController))
            if (GUILayout.Button("Create and Connect"))
                try { Build(); } catch (Exception ex) { result = ex.Message; Debug.LogException(ex); }
        if (!string.IsNullOrEmpty(result)) EditorGUILayout.HelpBox(result, MessageType.Info);
    }

    private string ResolvePath(string sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath)) return sourcePath;
        if (playerAnimator.transform.Find(sourcePath) != null) return sourcePath;
        string normalized = NormalizePath(sourcePath);
        string[] matches = playerAnimator.GetComponentsInChildren<Transform>(true)
            .Select(t => AnimationUtility.CalculateTransformPath(t, playerAnimator.transform))
            .Where(p => NormalizePath(p) == normalized || NormalizePath(p).EndsWith("/" + normalized, StringComparison.Ordinal))
            .Distinct().ToArray();
        if (matches.Length == 1) return matches[0];
        throw new InvalidOperationException(matches.Length == 0
            ? "No matching skeleton hierarchy for " + sourcePath + ". Nothing has been renamed or ignored."
            : "Ambiguous skeleton path: " + sourcePath + ". Select the Animator of one model only.");
    }

    private static string NormalizePath(string path)
    {
        return string.Join("/", path.Split('/').Select(part =>
            part.StartsWith("mixamorig:", StringComparison.Ordinal) ? part.Substring(10) : part));
    }

    private void CheckClip(AnimationClip clip)
    {
        if (clip.legacy) throw new InvalidOperationException("The clip must not be Legacy: " + clip.name);
        if (clip.isHumanMotion)
        {
            if (!playerAnimator.isHuman) throw new InvalidOperationException("Humanoid clip needs a valid Humanoid Avatar.");
            return;
        }
        var keys = new HashSet<string>();
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            string path = ResolvePath(binding.path);
            string key = path + "|" + binding.type.FullName + "|" + binding.propertyName;
            if (!keys.Add(key)) throw new InvalidOperationException("Two curves would overwrite the same target: " + key);
        }
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip)) ResolvePath(binding.path);
    }

    private void Build()
    {
        AnimatorController original = locomotionController;
        if (original == null)
        {
            original = playerAnimator.runtimeAnimatorController as AnimatorController;
            if (original != null && (original.name == "Stone_Thowing" || original.name == "Stone_Throwing"))
            {
                // Original Character 05 controller GUID observed in this project's scene.
                string originalControllerPath = AssetDatabase.GUIDToAssetPath("ecba992c2f1394f9eb808f518164f8ea");
                original = AssetDatabase.LoadAssetAtPath<AnimatorController>(originalControllerPath);
            }
        }
        if (original == null || original.name == "Stone_Thowing" || original.name == "Stone_Throwing")
            throw new InvalidOperationException("Assign the original movement controller in Locomotion Controller. The standalone Stone_Thowing has no walking states.");
        remappedPaths = 0;
        if (!playerAnimator.gameObject.scene.IsValid() || !throwController.gameObject.scene.IsValid())
            throw new InvalidOperationException("Assign scene instances of Player Animator and Throw Controller.");
        if (original.layers.Any(x => x.name == LayerName))
            throw new InvalidOperationException("Stone layer already exists. Setup has not been duplicated.");
        CheckClip(pickup);
        CheckClip(throwing);
        foreach (string name in new[] { "Pickup", "Throw", "PickupSpeed" })
            if (original.parameters.Any(p => p.name == name))
                throw new InvalidOperationException("Existing parameter conflicts with " + name + ". Original controller was not changed.");

        string folder = "Assets/Game/StoneAnimations";
        EnsureFolder(folder);
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/Player_With_Stone.controller");
        if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original), path))
            throw new InvalidOperationException("Could not copy the current controller.");
        var copy = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        copy.AddParameter(new AnimatorControllerParameter
        { name = "PickupSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 0.5f });
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
        result = "Created " + path + ". Remapped " + remappedPaths + " curve bindings to the actual skeleton. Original model and clips preserved. Check limb poses in Unity; path remapping is not full Humanoid retargeting.";
    }

    private static void AddAction(AnimatorStateMachine sm, AnimatorState idle, string trigger, AnimationClip clip, Vector3 position)
    {
        var state = sm.AddState(trigger, position);
        state.motion = clip;
        if (trigger == "Pickup")
        {
            state.speedParameterActive = true;
            state.speedParameter = "PickupSpeed";
        }
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

    private AnimationClip CopyClip(AnimationClip source, string folder)
    {
        var clip = Instantiate(source);
        clip.name = source.name;
        if (!source.isHumanMotion)
        {
            var bindings = AnimationUtility.GetCurveBindings(source);
            var curves = bindings.Select(b => AnimationUtility.GetEditorCurve(source, b)).ToArray();
            var mapped = bindings.ToArray();
            for (int i = 0; i < mapped.Length; i++)
            {
                mapped[i].path = ResolvePath(bindings[i].path);
                if (mapped[i].path != bindings[i].path) remappedPaths++;
            }
            // Remove source bindings first, then write target bindings with identical curve data.
            foreach (var binding in bindings) AnimationUtility.SetEditorCurve(clip, binding, null);
            for (int i = 0; i < mapped.Length; i++) AnimationUtility.SetEditorCurve(clip, mapped[i], curves[i]);
            var objects = AnimationUtility.GetObjectReferenceCurveBindings(source);
            var objectCurves = objects.Select(b => AnimationUtility.GetObjectReferenceCurve(source, b)).ToArray();
            foreach (var binding in objects) AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
            for (int i = 0; i < objects.Length; i++)
            {
                var binding = objects[i];
                binding.path = ResolvePath(binding.path);
                AnimationUtility.SetObjectReferenceCurve(clip, binding, objectCurves[i]);
            }
        }
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
