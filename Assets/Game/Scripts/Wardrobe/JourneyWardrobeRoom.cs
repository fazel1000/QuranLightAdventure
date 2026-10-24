using System;
using System.Collections;
using System.Collections.Generic;
using GameCreator.Runtime.Characters;
using UnityEngine;

// Keep on an always-active manager, outside Wardrobe Panel and stageRoot.
[DisallowMultipleComponent]
public sealed class JourneyWardrobeRoom : MonoBehaviour
{
    public JourneyMenuController menu;
    public GameObject settingsPanel, wardrobePanel, stageRoot;
    public Camera gameplayCamera, previewCamera;
    public JourneyWardrobeAppearance preview;
    public Character player;
    public GameObject gameplayModelPrefab;
    public bool useHodaartForPlayer = true;
    public Vector3 modelOffset;
    public RuntimeAnimatorController gameplayAnimatorController;
    [Header("Keep existing equipment when replacing the player model")]
    public JourneyPlayerCombat combat;
    public JourneyStoneThrowController stoneController;
    [Tooltip("Optional extra equipment roots attached to the old Humanoid bones. Stone sockets and shield are collected automatically.")]
    public Transform[] extraAttachments = Array.Empty<Transform>();
    [Header("Wardrobe UI visibility")]
    public bool hideOtherCanvasUI = true;
    [Tooltip("Optional UI roots outside the wardrobe Canvas, such as a separate HUD Canvas.")]
    public GameObject[] extraUIToHide = Array.Empty<GameObject>();
    private sealed class HiddenUI
    {
        public CanvasGroup group;
        public float alpha;
        public bool interactable, blocksRaycasts, ignoreParentGroups;
    }
    private readonly List<HiddenUI> hiddenUI = new List<HiddenUI>();
    private sealed class Attachment
    { public Transform item; public HumanBodyBones bone; public Vector3 position, scale; public Quaternion rotation; }
    private readonly List<Attachment> attachments = new List<Attachment>();
    private bool open, oldCameraEnabled;
    private int returnSettingsPage;
    public bool IsOpen => open;
    private IEnumerator Start()
    {
        if (wardrobePanel != null) wardrobePanel.SetActive(false);
        if (stageRoot != null) stageRoot.SetActive(false);
        if (previewCamera != null) previewCamera.enabled = false;
        yield return null; // Game Creator kernel must finish startup before ChangeModel.
        if (useHodaartForPlayer) TryChangeGameplayModel(gameplayModelPrefab, true);
    }
    // Replace the visual model while retaining gameplay animation and equipment.
    public bool TryChangeGameplayModel(GameObject prefab, bool editableOutfit)
    {
        if (player == null || prefab == null) return false;
        Animator model = prefab.GetComponentInChildren<Animator>(true);
        if (model == null || model.avatar == null || !model.avatar.isValid || !model.avatar.isHuman)
        { Debug.LogError("Gameplay model requires a valid Humanoid Avatar. Existing player model was kept.", this); return false; }
        Animator oldAnimator = player.Animim.Animator;
        bool gameplayRootMotion = oldAnimator != null && oldAnimator.applyRootMotion;
        RuntimeAnimatorController controller = gameplayAnimatorController != null ? gameplayAnimatorController :
            oldAnimator != null ? oldAnimator.runtimeAnimatorController : null;
        if (controller == null) { Debug.LogError("Assign the Game Creator gameplay animator controller; existing model was kept.", this); return false; }
        if (combat == null) combat = player.GetComponent<JourneyPlayerCombat>();
        if (stoneController == null) stoneController = FindFirstObjectByType<JourneyStoneThrowController>(FindObjectsInactive.Include);
        if (stoneController != null) stoneController.CancelForRespawn();
        KeepAttachments(oldAnimator);
        GameObject instance = player.ChangeModel(prefab, new Character.ChangeOptions { controller = controller, offset = modelOffset });
        if (instance == null) { RestoreAttachments(oldAnimator); return false; }
        foreach (var child in instance.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = player.gameObject.layer;
        if (editableOutfit && instance.GetComponent<JourneyWardrobeAppearance>() == null)
            instance.AddComponent<JourneyWardrobeAppearance>();
        if (!editableOutfit)
            foreach (var appearance in instance.GetComponentsInChildren<JourneyWardrobeAppearance>(true)) appearance.enabled = false;
        Animator newAnimator = player.Animim.Animator;
        if (newAnimator != null)
        {
            newAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            newAnimator.applyRootMotion = gameplayRootMotion;
            RestoreAttachments(newAnimator);
            if (combat != null) combat.RebindCharacterAnimator(newAnimator);
            if (stoneController != null) stoneController.RebindCharacterAnimator(newAnimator);
        }
        return true;
    }
    private void RestoreAttachments(Animator animator)
    {
        if (animator == null || !animator.isHuman) return;
        foreach (var attachment in attachments)
        {
            if (attachment.item == null) continue;
            Transform bone = animator.GetBoneTransform(attachment.bone);
            if (bone == null) { Debug.LogWarning("Missing equipment bone: " + attachment.bone + "; item retained under Player.", this); continue; }
            attachment.item.SetParent(bone, false);
            attachment.item.localPosition = attachment.position;
            attachment.item.localRotation = attachment.rotation;
            attachment.item.localScale = attachment.scale;
        }
        attachments.Clear();
    }
    private void KeepAttachments(Animator animator)
    {
        attachments.Clear();
        if (animator == null || !animator.isHuman) return;
        var candidates = new List<Transform>();
        if (stoneController != null) { candidates.Add(stoneController.HandSocket); candidates.Add(stoneController.ThrowOrigin); }
        if (combat != null && combat.shieldHitbox != null) candidates.Add(combat.shieldHitbox.transform);
        if (extraAttachments != null) candidates.AddRange(extraAttachments);
        // Preserve rigid equipment by its bone attachment, not its asset name.
        // Imported skinned body/clothes are intentionally not candidates.
        var humanBones = new HashSet<Transform>();
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
            if (bone != null) humanBones.Add(bone);
        }
        foreach (var mesh in animator.GetComponentsInChildren<MeshRenderer>(true))
        {
            Transform item = mesh.transform;
            if (humanBones.Contains(item)) continue;
            Transform ancestor = item.parent;
            while (ancestor != null && ancestor != animator.transform && !humanBones.Contains(ancestor))
            { item = ancestor; ancestor = ancestor.parent; }
            if (ancestor != null && humanBones.Contains(ancestor)) candidates.Add(item);
        }
        foreach (var item in candidates)
        {
            if (item == null || humanBones.Contains(item) || !item.IsChildOf(animator.transform)) continue;
            bool covered = false;
            foreach (var other in candidates) if (other != null && other != item && item.IsChildOf(other)) { covered = true; break; }
            if (covered || attachments.Exists(a => a.item == item)) continue;
            Transform nearest = null; HumanBodyBones selected = HumanBodyBones.LastBone;
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone == null || item == bone || !item.IsChildOf(bone)) continue;
                if (nearest == null || bone.IsChildOf(nearest)) { nearest = bone; selected = (HumanBodyBones)i; }
            }
            if (nearest == null) { Debug.LogWarning("Assign equipment below a Humanoid bone before changing model: " + item.name, item); continue; }
            attachments.Add(new Attachment { item = item, bone = selected, position = nearest.InverseTransformPoint(item.position), rotation = Quaternion.Inverse(nearest.rotation) * item.rotation, scale = new Vector3(item.lossyScale.x / Mathf.Max(0.0001f, nearest.lossyScale.x), item.lossyScale.y / Mathf.Max(0.0001f, nearest.lossyScale.y), item.lossyScale.z / Mathf.Max(0.0001f, nearest.lossyScale.z)) });
        }
        foreach (var attachment in attachments) attachment.item.SetParent(player.transform, true);
    }
    public void Open()
    {
        if (open || menu == null || menu.IsFirstLanguageOpen || wardrobePanel == null || previewCamera == null || preview == null) return;
        var settingsPages = menu.GetComponent<JourneySettingsPages>();
        returnSettingsPage = settingsPages != null ? settingsPages.CurrentPage : 0;
        menu.ShowSettings();
        if (settingsPages != null) settingsPages.HideAll();
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        oldCameraEnabled = gameplayCamera != null && gameplayCamera.enabled;
        if (gameplayCamera != null) gameplayCamera.enabled = false;
        if (stageRoot != null) stageRoot.SetActive(true);
        var surface = GetComponent<JourneyWardrobePreviewSurface>();
        if (surface != null) surface.Begin();
        // Keep an onscreen camera for Screen Space Camera canvases; the model camera now renders offscreen.
        if (surface != null && surface.IsRendering && gameplayCamera != null) gameplayCamera.enabled = oldCameraEnabled;
        previewCamera.enabled = true;
        FitPreviewCamera();
        if (settingsPanel != null) settingsPanel.SetActive(false);
        wardrobePanel.SetActive(true);
        wardrobePanel.transform.SetAsLastSibling();
        var drag = wardrobePanel.GetComponent<JourneyWardrobeDrag>();
        if (drag != null)
        {
            drag.target = preview.transform;
            drag.previewCamera = previewCamera;
        }
        HideOtherUI();
        open = true;
    }
    private void HideOtherUI()
    {
        RestoreOtherUI();
        if (hideOtherCanvasUI)
        {
            Canvas canvas = wardrobePanel.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                Transform root = canvas.rootCanvas.transform;
                Transform branch = wardrobePanel.transform;
                while (branch != root && branch.parent != null)
                {
                    foreach (Transform sibling in branch.parent)
                        if (sibling != branch) HideUIRoot(sibling.gameObject);
                    branch = branch.parent;
                }
            }
        }
        if (extraUIToHide != null)
            foreach (var item in extraUIToHide) HideUIRoot(item);
    }
    private void HideUIRoot(GameObject item)
    {
        if (item == null || item == wardrobePanel ||
            wardrobePanel.transform.IsChildOf(item.transform) ||
            item.transform.IsChildOf(wardrobePanel.transform)) return;
        var group = item.GetComponent<CanvasGroup>();
        if (group == null) group = item.AddComponent<CanvasGroup>();
        // Include nested groups which might otherwise ignore the hidden parent.
        foreach (var nested in item.GetComponentsInChildren<CanvasGroup>(true))
        {
            if (hiddenUI.Exists(state => state.group == nested)) continue;
            hiddenUI.Add(new HiddenUI { group = nested, alpha = nested.alpha,
                interactable = nested.interactable, blocksRaycasts = nested.blocksRaycasts,
                ignoreParentGroups = nested.ignoreParentGroups });
            nested.alpha = 0f;
            nested.interactable = false;
            nested.blocksRaycasts = false;
            nested.ignoreParentGroups = false;
        }
    }
    private void RestoreOtherUI()
    {
        foreach (var state in hiddenUI)
        {
            if (state.group == null) continue;
            state.group.alpha = state.alpha;
            state.group.interactable = state.interactable;
            state.group.blocksRaycasts = state.blocksRaycasts;
            state.group.ignoreParentGroups = state.ignoreParentGroups;
        }
        hiddenUI.Clear();
    }
    public void FitPreviewCamera()
    { FitPreviewCamera(false); }
    public void RefitPreviewCameraPreserveAngle()
    { FitPreviewCamera(true); }
    private void FitPreviewCamera(bool preserveAngle)
    {
        Bounds bounds = new Bounds(preview.transform.position + Vector3.up, Vector3.zero);
        bool found = false;
        foreach (var renderer in preview.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled) continue;
            if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
        }
        if (!found) return;
        float aspect = previewCamera.targetTexture != null ? Mathf.Max(0.1f, previewCamera.aspect) :
            Mathf.Max(0.1f, (Screen.width * previewCamera.rect.width) / Mathf.Max(1f, Screen.height * previewCamera.rect.height));
        float tangent = Mathf.Tan(previewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float radius = Mathf.Sqrt(bounds.extents.x * bounds.extents.x + bounds.extents.z * bounds.extents.z);
        float distance = Mathf.Max(bounds.extents.y / tangent, radius / (tangent * aspect)) * 1.2f + radius;
        if (preserveAngle) previewCamera.transform.position = bounds.center - previewCamera.transform.forward * distance;
        else
        { previewCamera.transform.position = bounds.center + Vector3.forward * distance; previewCamera.transform.LookAt(bounds.center); }
    }
    public void Close()
    {
        if (!open) return;
        Restore();
        if (menu != null && menu.isActiveAndEnabled)
        {
            menu.ShowSettings();
            menu.GetComponent<JourneySettingsPages>()?.ShowPageImmediate(returnSettingsPage);
        }
    }
    private void Restore()
    {
        RestoreOtherUI();
        var surface = GetComponent<JourneyWardrobePreviewSurface>();
        if (surface != null) surface.End();
        if (previewCamera != null) previewCamera.enabled = false;
        if (stageRoot != null) stageRoot.SetActive(false);
        if (wardrobePanel != null) wardrobePanel.SetActive(false);
        if (gameplayCamera != null) gameplayCamera.enabled = oldCameraEnabled;
        open = false;
    }
    private void OnDisable() { if (open) Restore(); }
}
