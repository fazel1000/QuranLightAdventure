#if UNITY_EDITOR
using System;
using System.Linq;
using GameCreator.Runtime.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class JourneySwimmingSetup
{
    [MenuItem("Quran Kids/Setup Swimming and Stone Aiming")]
    private static void SetupActive()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { Debug.LogError("Exit Play Mode before running Setup Swimming and Stone Aiming."); return; }
        Scene scene = SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var player = roots.SelectMany(go => go.GetComponentsInChildren<Character>(true)).FirstOrDefault(c => c.IsPlayer);
        var menu = roots.SelectMany(go => go.GetComponentsInChildren<JourneyMenuController>(true)).FirstOrDefault();
        var canvas = roots.SelectMany(go => go.GetComponentsInChildren<Canvas>(true)).FirstOrDefault(c => c.isRootCanvas && c.renderMode != RenderMode.WorldSpace);
        if (player == null || menu == null || canvas == null)
        { Debug.LogError("The active scene needs its GC Player, JourneyMenuController and screen-space Canvas."); return; }
        Configure(scene, player, menu, canvas);
        EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets();
        Debug.Log("Swimming and stone aiming connected. Save the scene; use the existing joystick to swim at the water surface.", player);
    }

    internal static void Configure(Scene scene, Character player, JourneyMenuController menu, Canvas canvas)
    {
        var binder = player.GetComponent<JourneyPlayerModelBindings>();
        var controller = binder != null ? binder.gameplayController as AnimatorController : null;
        if (controller == null && player.Animim.Animator != null) controller = player.Animim.Animator.runtimeAnimatorController as AnimatorController;
        if (controller == null) throw new InvalidOperationException("Run Setup Air Water and World Systems to restore a gameplay AnimatorController first.");
        AnimationClip swim = PrepareClip("Assets/Swimming/Swimming.fbx", true);
        AnimationClip tread = PrepareClip("Assets/Swimming/Treading Water.fbx", true);
        AnimationClip edge = PrepareClip("Assets/Swimming/Swimming To Edge.fbx", false);
        AddLayer(controller, tread, swim, edge);
        var component = JourneyWorldSetupUtility.Add<JourneyPlayerSwimming>(player.gameObject);
        Undo.RecordObject(component, "Connect water and swimming animations");
        component.player = player; component.menu = menu;
        if (component.viewCamera == null)
        {
            var pilot = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<JourneyAirWaterPilot>(true)).FirstOrDefault();
            component.viewCamera = pilot != null && pilot.viewCamera != null ? pilot.viewCamera : Camera.main;
        }
        component.preferredWater = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<JourneyWaterVolume>(true)).FirstOrDefault();
        if (component.mouth == null && player.Animim.Animator != null && player.Animim.Animator.isHuman)
            component.mouth = player.Animim.Animator.GetBoneTransform(HumanBodyBones.Head);
        component.swimmingClip = swim; component.treadingClip = tread; component.approachEdgeClip = edge;
        JourneyWorldSetupUtility.Dirty(component); JourneyWorldSetupUtility.Dirty(controller);
        ConfigureAim(scene, canvas);
    }
    internal static void ConfigureAim(Scene scene, Canvas canvas)
    {
        foreach (var stone in scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<JourneyStoneThrowController>(true)))
        {
            var serialized = new SerializedObject(stone);
            var reference = serialized.FindProperty("aimReticle");
            if (reference.objectReferenceValue == null)
            {
                var go = JourneyWorldSetupUtility.Child(canvas.rootCanvas.transform, "Stone Aim Reticle", typeof(RectTransform), typeof(Image));
                var rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f; rect.sizeDelta = Vector2.one * 48f;
                var image = JourneyWorldSetupUtility.Add<Image>(go); image.sprite = JourneyWorldSetupUtility.Crosshair();
                image.color = new Color(1f, 0.9f, 0.2f, 1f); image.raycastTarget = false;
                reference.objectReferenceValue = rect; go.SetActive(false);
                serialized.ApplyModifiedProperties(); JourneyWorldSetupUtility.Dirty(image); JourneyWorldSetupUtility.Dirty(rect);
            }
            JourneyWorldSetupUtility.Dirty(stone);
        }
    }
    private static AnimationClip PrepareClip(string path, bool loop)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Missing swimming FBX: " + path);
        bool changed = !importer.importAnimation || importer.animationType != ModelImporterAnimationType.Human;
        importer.importAnimation = true; importer.animationType = ModelImporterAnimationType.Human;
        var clips = importer.clipAnimations;
        if (clips.Length == 0) { clips = importer.defaultClipAnimations; changed = true; }
        foreach (var clip in clips)
        {
            changed |= clip.loopTime != loop || clip.loopPose != loop || !clip.lockRootRotation ||
                !clip.lockRootHeightY || clip.lockRootPositionXZ || !clip.keepOriginalOrientation ||
                !clip.keepOriginalPositionY || !clip.keepOriginalPositionXZ;
            clip.loopTime = loop; clip.loopPose = loop;
            // Extract authored forward travel; GC's kinematic driver supplies movement.
            clip.lockRootRotation = true; clip.lockRootHeightY = true; clip.lockRootPositionXZ = false;
            clip.keepOriginalOrientation = true; clip.keepOriginalPositionY = true; clip.keepOriginalPositionXZ = true;
        }
        if (changed) { importer.clipAnimations = clips; importer.SaveAndReimport(); }
        var animation = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
        if (animation == null || !animation.humanMotion)
            throw new InvalidOperationException("Swimming clip needs a valid Humanoid rig/avatar: " + path);
        return animation;
    }
    private static void AddLayer(AnimatorController controller, AnimationClip tread, AnimationClip swim, AnimationClip edge)
    {
        Undo.RecordObject(controller, "Add swimming animation layer");
        if (!controller.parameters.Any(p => p.name == JourneyPlayerSwimming.SpeedParameter))
            controller.AddParameter(JourneyPlayerSwimming.SpeedParameter, AnimatorControllerParameterType.Float);
        if (!controller.parameters.Any(p => p.name == JourneyPlayerSwimming.PlaybackSpeedParameter))
            controller.AddParameter(new AnimatorControllerParameter { name = JourneyPlayerSwimming.PlaybackSpeedParameter,
                type = AnimatorControllerParameterType.Float, defaultFloat = 2f });
        var layers = controller.layers;
        int index = Array.FindIndex(layers, l => l.name == JourneyPlayerSwimming.LayerName);
        if (index < 0)
        {
            controller.AddLayer(JourneyPlayerSwimming.LayerName);
            layers = controller.layers; index = layers.Length - 1;
        }
        var layer = layers[index]; layer.defaultWeight = 0f; layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layer.avatarMask = null; var machine = layer.stateMachine;
        var surface = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Surface");
        if (surface == null) surface = machine.AddState("Surface", new Vector3(230f, 80f));
        var tree = surface.motion as BlendTree;
        if (tree == null)
        {
            tree = new BlendTree { name = "Surface Swimming", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, controller); surface.motion = tree;
        }
        tree.blendType = BlendTreeType.Simple1D; tree.blendParameter = JourneyPlayerSwimming.SpeedParameter; tree.useAutomaticThresholds = false;
        tree.children = new[] { new ChildMotion { motion = tread, threshold = 0f, timeScale = 1f }, new ChildMotion { motion = swim, threshold = 1f, timeScale = 1f } };
        surface.writeDefaultValues = false; surface.iKOnFeet = false; machine.defaultState = surface;
        surface.speed = 1f; surface.speedParameter = JourneyPlayerSwimming.PlaybackSpeedParameter; surface.speedParameterActive = true;
        var approach = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Approach Edge");
        if (approach == null) approach = machine.AddState("Approach Edge", new Vector3(230f, 170f));
        approach.motion = edge; approach.writeDefaultValues = false; approach.iKOnFeet = false;
        approach.speed = 1f; approach.speedParameter = JourneyPlayerSwimming.PlaybackSpeedParameter; approach.speedParameterActive = true;
        layers[index] = layer; controller.layers = layers;
        EditorUtility.SetDirty(tree); EditorUtility.SetDirty(surface); EditorUtility.SetDirty(approach); EditorUtility.SetDirty(machine);
    }
}
#endif
