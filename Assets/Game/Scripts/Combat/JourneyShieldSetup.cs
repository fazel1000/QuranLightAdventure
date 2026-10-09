#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public sealed class JourneyShieldSetup : EditorWindow
{
    private JourneyPlayerCombat combat;
    private AnimationClip blockClip;
    private Button defendButton;
    private BoxCollider shieldHitbox;
    private Collider bodyHitbox;
    private string result;

    [MenuItem("Quran Kids/Setup Journey Shield")]
    private static void Open() { GetWindow<JourneyShieldSetup>("Journey Shield"); }
    private void OnEnable()
    {
        combat = FindFirstObjectByType<JourneyPlayerCombat>(FindObjectsInactive.Include);
        if (combat != null)
        {
            defendButton = combat.defendButton;
            shieldHitbox = combat.shieldHitbox;
            bodyHitbox = combat.bodyHitbox != null ? combat.bodyHitbox : combat.player?.GetComponent<Collider>();
            if (shieldHitbox == null && combat.player != null)
                shieldHitbox = combat.player.GetComponentsInChildren<BoxCollider>(true).FirstOrDefault(x => x.name == "ShieldHitbox");
        }
        if (defendButton == null)
            defendButton = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(x => x.name == "DefendButton");
    }
    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Adds ShieldBlock to a COPY of the current player controller. Does not rebuild enemies or replace the attack clip. Assign the shield trigger. Body Hitbox can be left empty to create a dedicated capsule.", MessageType.Info);
        combat = (JourneyPlayerCombat)EditorGUILayout.ObjectField("Player Combat", combat, typeof(JourneyPlayerCombat), true);
        blockClip = (AnimationClip)EditorGUILayout.ObjectField("Shield Block Clip", blockClip, typeof(AnimationClip), false);
        defendButton = (Button)EditorGUILayout.ObjectField("Defend Button", defendButton, typeof(Button), true);
        shieldHitbox = (BoxCollider)EditorGUILayout.ObjectField("Shield Hitbox", shieldHitbox, typeof(BoxCollider), true);
        bodyHitbox = (Collider)EditorGUILayout.ObjectField("Body Hitbox (optional)", bodyHitbox, typeof(Collider), true);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            if (GUILayout.Button("Create and Connect"))
                try { Build(); } catch (Exception ex) { result = ex.Message; Debug.LogException(ex); }
        if (!string.IsNullOrEmpty(result)) EditorGUILayout.HelpBox(result, MessageType.Info);
    }

    private void Build()
    {
        if (combat == null || blockClip == null || defendButton == null || shieldHitbox == null)
            throw new InvalidOperationException("Assign Player Combat, Shield Block Clip, Defend Button and Shield Hitbox.");
        if (!combat.gameObject.scene.IsValid() || combat.player == null || combat.playerAnimator == null)
            throw new InvalidOperationException("Select the configured scene Player Combat.");
        if (!blockClip.isHumanMotion || blockClip.legacy || combat.playerAnimator.avatar == null ||
            !combat.playerAnimator.avatar.isHuman || !combat.playerAnimator.avatar.isValid)
            throw new InvalidOperationException("Import Shield Block FBX as Humanoid / Create From This Model, Apply, then assign the clip inside it.");
        if (!shieldHitbox.transform.IsChildOf(combat.player.transform) || (bodyHitbox != null && (!bodyHitbox.transform.IsChildOf(combat.player.transform) || shieldHitbox == bodyHitbox)))
            throw new InvalidOperationException("Both colliders must belong to this Player; body and shield must be different colliders.");

        Animator animator = combat.playerAnimator;
        var original = animator.runtimeAnimatorController as AnimatorController;
        if (original == null || !original.layers.Any(x => x.name == JourneyPlayerCombat.LayerName))
            throw new InvalidOperationException("Use the player's current controller containing Journey Sword Actions.");
        if (original.parameters.Any(p => p.name == "JourneyShieldSpeed" && p.type != AnimatorControllerParameterType.Float))
            throw new InvalidOperationException("JourneyShieldSpeed already exists with a conflicting type.");
        string folder = "Assets/Game/Animations/Combat/Generated";
        if (!AssetDatabase.IsValidFolder(folder)) throw new InvalidOperationException("The combat Generated folder is missing; restore the existing combat setup first.");
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/Player_With_Shield.controller");
        if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(original), path)) throw new InvalidOperationException("Controller copy failed.");
        var copy = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (!copy.parameters.Any(p => p.name == "JourneyShieldSpeed"))
            copy.AddParameter(new AnimatorControllerParameter { name = "JourneyShieldSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 0.65f });
        var sm = copy.layers.First(x => x.name == JourneyPlayerCombat.LayerName).stateMachine;
        var state = sm.states.Select(x => x.state).FirstOrDefault(x => x.name == "ShieldBlock");
        if (state == null) state = sm.AddState("ShieldBlock");
        var clip = Instantiate(blockClip);
        clip.name = "ShieldBlock";
        AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = false;
        settings.loopBlendOrientation = true;
        settings.loopBlendPositionY = true;
        settings.loopBlendPositionXZ = true;
        // Preserve the imported orientation/offset so existing rig corrections remain intact.
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AssetDatabase.CreateAsset(clip, AssetDatabase.GenerateUniqueAssetPath(folder + "/ShieldBlock.anim"));
        state.motion = clip;
        state.writeDefaultValues = false;
        state.speedParameter = "JourneyShieldSpeed";
        state.speedParameterActive = true;
        EditorUtility.SetDirty(state);
        EditorUtility.SetDirty(copy);
        AssetDatabase.SaveAssets();
        if (bodyHitbox == null)
        {
            Transform existing = combat.player.transform.Find("CombatBodyHitbox");
            bodyHitbox = existing != null ? existing.GetComponent<Collider>() : null;
            if (bodyHitbox == null)
            {
                var bodyObject = new GameObject("CombatBodyHitbox");
                Undo.RegisterCreatedObjectUndo(bodyObject, "Create combat body hitbox");
                bodyObject.transform.SetParent(combat.player.transform, false);
                bodyObject.layer = combat.player.gameObject.layer;
                var capsule = Undo.AddComponent<CapsuleCollider>(bodyObject);
                capsule.radius = 0.3f;
                capsule.height = 1.6f;
                capsule.center = Vector3.zero;
                capsule.isTrigger = true;
                bodyHitbox = capsule;
            }
        }
        Undo.RecordObjects(new UnityEngine.Object[] { combat, animator, shieldHitbox }, "Connect shield defense");
        animator.runtimeAnimatorController = copy;
        combat.defendButton = defendButton;
        combat.shieldHitbox = shieldHitbox;
        combat.bodyHitbox = bodyHitbox;
        shieldHitbox.isTrigger = true;
        foreach (UnityEngine.Object changed in new UnityEngine.Object[] { combat, animator, shieldHitbox })
        {
            EditorUtility.SetDirty(changed);
            PrefabUtility.RecordPrefabInstancePropertyModifications(changed);
        }
        EditorSceneManager.MarkSceneDirty(combat.gameObject.scene);
        result = "Connected. Save scene. Defend Button On Click stays empty. Assign Blade Base/Tip on each enemy. Created " + path;
    }
}
#endif
