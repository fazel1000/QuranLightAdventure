using GameCreator.Runtime.Characters;
using System.Collections.Generic;
using UnityEngine;

// The visual model's demo controller is independent from the gameplay controller.
[DefaultExecutionOrder(-500), DisallowMultipleComponent]
public sealed class JourneyPlayerModelBindings : MonoBehaviour
{
    public Character player;
    public RuntimeAnimatorController gameplayController;
    public JourneyPlayerCombat combat;
    public JourneyStoneThrowController stone;
    public Transform handSocket, throwOrigin;
    public BoxCollider shieldHitbox;
    private Animator bound;
    private bool refreshPending;
    private readonly Dictionary<Transform, Pose> socketPoses = new Dictionary<Transform, Pose>();
    private void Awake()
    {
        if (player == null) player = GetComponent<Character>();
        if (combat == null) combat = GetComponent<JourneyPlayerCombat>();
        if (player != null)
        {
            player.EventBeforeChangeModel += BeforeModelChange;
            player.EventAfterChangeModel += Bind;
        }
        // Run before Character.Awake builds its PlayableGraph from this controller.
        if (player != null && player.Animim.Animator != null && gameplayController != null)
            player.Animim.Animator.runtimeAnimatorController = gameplayController;
    }
    private void Start() { Bind(); }
    private void LateUpdate()
    {
        if (!refreshPending || combat == null || player == null) return;
        refreshPending = false; combat.RebindCharacterAnimator(player.Animim.Animator);
    }
    private void BeforeModelChange()
    {
        if (stone != null) stone.CancelForRespawn();
        Detach(throwOrigin, HumanBodyBones.RightHand); Detach(handSocket, HumanBodyBones.RightHand);
        if (shieldHitbox != null) Detach(shieldHitbox.transform, HumanBodyBones.LeftHand);
    }
    private void Detach(Transform item, HumanBodyBones bone)
    {
        if (item == null || bound == null || !item.IsChildOf(bound.transform)) return;
        Transform parent = bound.GetBoneTransform(bone);
        if (parent != null) socketPoses[item] = new Pose(parent.InverseTransformPoint(item.position), Quaternion.Inverse(parent.rotation) * item.rotation);
        item.SetParent(player.transform, true);
    }
    public void Bind()
    {
        if (player == null) return;
        Animator animator = player.Animim.Animator;
        if (animator == null || !animator.isHuman) return;
        if (gameplayController != null && animator.runtimeAnimatorController != gameplayController) animator.runtimeAnimatorController = gameplayController;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand), left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (right != null)
        {
            if (handSocket == null) handSocket = NewSocket("HandSocket", right, Vector3.zero);
            else if (!handSocket.IsChildOf(animator.transform)) AttachPreserved(handSocket, right, Vector3.zero);
            if (throwOrigin == null) throwOrigin = NewSocket("ThrowOrigin", right, new Vector3(0f, 0f, 0.15f));
            else if (socketPoses.ContainsKey(throwOrigin)) AttachPreserved(throwOrigin, right, new Vector3(0f, 0f, 0.15f));
        }
        if (left != null)
        {
            if (shieldHitbox == null)
            {
                Transform point = NewSocket("ShieldHitbox", left, new Vector3(0f, 0f, 0.08f));
                shieldHitbox = point.gameObject.AddComponent<BoxCollider>(); shieldHitbox.size = new Vector3(0.5f, 0.65f, 0.12f); shieldHitbox.isTrigger = true;
            }
            else if (!shieldHitbox.transform.IsChildOf(animator.transform)) AttachPreserved(shieldHitbox.transform, left, new Vector3(0f, 0f, 0.08f));
        }
        if (combat != null) { combat.player = player; combat.shieldHitbox = shieldHitbox; refreshPending = !combat.RebindCharacterAnimator(animator); }
        if (stone != null) stone.BindCharacterSockets(animator, handSocket, throwOrigin);
        bound = animator; socketPoses.Clear();
    }
    private void AttachPreserved(Transform item, Transform parent, Vector3 fallback)
    {
        Attach(item, parent, fallback);
        if (socketPoses.TryGetValue(item, out Pose pose)) { item.localPosition = pose.position; item.localRotation = pose.rotation; }
    }
    private Transform NewSocket(string name, Transform parent, Vector3 position)
    { var item = new GameObject(name).transform; item.gameObject.layer = player.gameObject.layer; Attach(item, parent, position); return item; }
    private static void Attach(Transform item, Transform parent, Vector3 position)
    { item.SetParent(parent, false); item.localPosition = position; item.localRotation = Quaternion.identity; }
    private void OnDestroy()
    {
        if (player == null) return;
        player.EventBeforeChangeModel -= BeforeModelChange; player.EventAfterChangeModel -= Bind;
    }
}
