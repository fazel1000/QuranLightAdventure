using GameCreator.Runtime.Characters;
using UnityEngine;

// Uses the GC driver for collision and camera-relative three-dimensional swimming.
[DefaultExecutionOrder(-50), DisallowMultipleComponent, RequireComponent(typeof(Character))]
public sealed class JourneyPlayerSwimming : MonoBehaviour
{
    public const string LayerName = "Journey Swimming";
    public const string SpeedParameter = "JourneySwimSpeed";
    public const string PlaybackSpeedParameter = "JourneySwimPlaybackSpeed";
    public Character player;
    public JourneyMenuController menu;
    public JourneyWaterVolume preferredWater;
    public Camera viewCamera;
    [Tooltip("Head bone is used automatically. An Empty at the mouth gives more exact water entry.")]
    public Transform mouth;
    public float mouthHeightOffset = 0.08f;
    [Min(0.1f)] public float swimSpeed = 2.5f;
    [Min(0.1f)] public float swimSpeedMultiplier = 2f;
    [Min(0.1f)] public float animationSpeed = 2f;
    [Header("Dive in the camera direction")]
    public bool diveWithCamera = true;
    [Tooltip("Small camera tilts keep swimming horizontal. Look farther down/up to dive/surface.")]
    [Range(0f, 80f)] public float minimumDiveLookAngle = 15f;
    [Min(0f)] public float bottomClearance = 0.1f;
    [Min(0f)] public float enterSubmersion = 0.04f;
    [Min(0f)] public float shoreExitMargin = 0.12f;
    [Min(0f)] public float breathingClearance = 0.06f;
    [Min(0.1f)] public float buoyancyResponse = 4f, maximumRiseSpeed = 1.5f;
    [Min(0.05f)] public float animationBlendSeconds = 0.25f;
    [Header("Animations - connected by Setup Swimming and Stone Aiming")]
    public AnimationClip swimmingClip, treadingClip, approachEdgeClip;
    [Tooltip("Swimming To Edge approaches a ledge; this option does not implement ledge climbing.")]
    public bool useApproachEdgeAnimation;
    public LayerMask groundLayers = ~0;
    public bool IsSwimmingNow { get; private set; }
    public float EffectiveSwimSpeed => Mathf.Max(0.1f, swimSpeed * swimSpeedMultiplier);
    public static bool IsSwimming => activeSwimmer != null && activeSwimmer.IsSwimmingNow;
    private static JourneyPlayerSwimming activeSwimmer;
    private JourneyWaterVolume water;
    private Animator animator;
    private RuntimeAnimatorController controller;
    private int layer = -1;
    private float previousSpeed, standingMouthHeight, speedBlend, layerWeight, targetMouthY;
    private bool previousCanJump, inEdgeState, initialized, focused = true, paused, warnedAnimation, hasPlaybackParameter;
    private readonly RaycastHit[] groundHits = new RaycastHit[32];
    private int GravityKey => GetInstanceID();
    private bool Gameplay => focused && !paused && Time.timeScale > 0f && menu != null && menu.IsGameplayActive;
    private Vector3 MouthPoint => mouth != null ? mouth.position + Vector3.up * mouthHeightOffset :
        transform.position + Vector3.up * Mathf.Max(0.2f, player.Motion.Height * 0.35f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { activeSwimmer = null; }
    private void Awake()
    {
        if (player == null) player = GetComponent<Character>();
        if (menu == null) menu = FindFirstObjectByType<JourneyMenuController>(FindObjectsInactive.Include);
        if (viewCamera == null) viewCamera = Camera.main;
    }
    private void Start()
    {
        RefreshAnimator();
        if (player == null || player.Motion == null || player.Driver == null) return;
        standingMouthHeight = Mathf.Clamp(MouthPoint.y - (transform.position.y - player.Motion.Height * 0.5f), 1f, player.Motion.Height + 0.2f);
        initialized = true;
    }
    private void RefreshAnimator()
    {
        Animator next = player != null ? player.Animim.Animator : null;
        if (next == animator && next != null && next.runtimeAnimatorController == controller)
        {
            if (mouth == null && next.isHuman) mouth = next.GetBoneTransform(HumanBodyBones.Head);
            return;
        }
        bool replaceMouth = mouth == null || animator != null && mouth.IsChildOf(animator.transform);
        if (animator != null && animator.isActiveAndEnabled && player.isActiveAndEnabled && animator.runtimeAnimatorController == controller && layer >= 0) animator.SetLayerWeight(layer, 0f);
        animator = next; controller = next != null ? next.runtimeAnimatorController : null;
        layer = animator != null ? animator.GetLayerIndex(LayerName) : -1; layerWeight = 0f; inEdgeState = false;
        hasPlaybackParameter = false;
        if (animator != null && controller != null)
            foreach (var parameter in animator.parameters)
                if (parameter.name == PlaybackSpeedParameter && parameter.type == AnimatorControllerParameterType.Float)
                { hasPlaybackParameter = true; break; }
        if (replaceMouth && animator != null && animator.isHuman) mouth = animator.GetBoneTransform(HumanBodyBones.Head);
        if (layer < 0 && !warnedAnimation)
        { warnedAnimation = true; Debug.LogWarning("Swimming animation layer missing. Run Quran Kids > Setup Swimming and Stone Aiming.", this); }
    }
    private void Update()
    {
        if (!initialized || player == null) return;
        RefreshAnimator();
        if (!player.isActiveAndEnabled || player.IsDead || JourneyRideSession.IsOccupied)
        { LeaveWater(); BlendAnimation(false, 0f, false); return; }
        if (!Gameplay)
        {
            // Release the higher-priority swim command while menus/app pause block input.
            if (IsSwimmingNow) player.Motion.StopToDirection(1);
            return;
        }
        if (!IsSwimmingNow)
        {
            JourneyWaterVolume candidate = preferredWater != null && preferredWater.ContainsHorizontal(MouthPoint)
                ? preferredWater : JourneyWaterVolume.FindAt(MouthPoint, gameObject.scene);
            if (candidate != null && candidate.ContainsWaterAt(MouthPoint - Vector3.up * enterSubmersion) &&
                MouthPoint.y < candidate.SurfaceY - enterSubmersion)
                EnterWater(candidate);
        }
        if (!IsSwimmingNow) { BlendAnimation(false, 0f, false); return; }
        if (water == null || !water.IsUsable || !water.ContainsHorizontal(transform.position))
        { LeaveWater(); BlendAnimation(false, 0f, false); return; }

        bool nearSurface = MouthPoint.y >= water.SurfaceY - 0.4f;
        bool shore = nearSurface && WalkableShoreAt(transform.position, out _);
        if (shore)
        { LeaveWater(); BlendAnimation(false, 0f, false); return; }

        player.Driver.SetGravityInfluence(GravityKey, 0f);
        player.Driver.ResetVerticalVelocity();
        player.Motion.LinearSpeed = EffectiveSwimSpeed;
        Vector2 input = JourneyMovementSettings.ReadSwimmingSteering();
        Transform look = viewCamera != null ? viewCamera.transform : player.Animim.Mannequin != null ? player.Animim.Mannequin : transform;
        Vector3 forward = look.forward;
        if (!diveWithCamera || Mathf.Abs(forward.y) < Mathf.Sin(minimumDiveLookAngle * Mathf.Deg2Rad))
            forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(look.right, Vector3.up).normalized;
        Vector3 velocity = Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f) * EffectiveSwimSpeed;
        float mouthY = MouthPoint.y;
        float maximumMouthY = water.SurfaceY + breathingClearance;
        float minimumMouthY = Mathf.Min(maximumMouthY, water.Region.bounds.min.y + player.Motion.Height * 0.5f +
            (mouthY - transform.position.y) + bottomClearance);
        float deltaTime = Mathf.Max(0.0001f, Time.deltaTime);
        if (Mathf.Abs(velocity.y) > 0.01f)
        {
            targetMouthY = Mathf.Clamp(mouthY + velocity.y * deltaTime, minimumMouthY, maximumMouthY);
            velocity.y = (targetMouthY - mouthY) / deltaTime;
        }
        else
        {
            if (!diveWithCamera) targetMouthY = maximumMouthY;
            targetMouthY = Mathf.Clamp(targetMouthY, minimumMouthY, maximumMouthY);
            velocity.y = Mathf.Clamp((targetMouthY - mouthY) * buoyancyResponse, -maximumRiseSpeed, maximumRiseSpeed);
        }
        // Priority 1 replaces the walking provider's flat, priority-0 movement.
        // Both horizontal and vertical travel still pass through CharacterController collisions.
        player.Motion.MoveToDirection(Vector3.ClampMagnitude(velocity, EffectiveSwimSpeed), Space.World, 1);
        float motion = input.magnitude;
        Vector3 facing = player.Animim.Mannequin != null ? player.Animim.Mannequin.forward : transform.forward;
        bool nearEdge = useApproachEdgeAnimation && nearSurface && motion > 0.1f && WalkableShoreAt(transform.position + facing * 0.8f, out _);
        BlendAnimation(true, Mathf.Clamp01(motion), nearEdge);
    }
    private bool WalkableShoreAt(Vector3 point, out float floorY)
    {
        floorY = float.NegativeInfinity;
        if (water == null) return false;
        Vector3 start = new Vector3(point.x, water.SurfaceY + 0.3f, point.z);
        int count = Physics.RaycastNonAlloc(start, Vector3.down, groundHits, standingMouthHeight + 1f, groundLayers, QueryTriggerInteraction.Ignore);
        float closest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = groundHits[i];
            if (hit.transform.IsChildOf(transform) || hit.normal.y < 0.71f || hit.distance >= closest) continue;
            closest = hit.distance; floorY = hit.point.y;
        }
        return floorY + standingMouthHeight > water.SurfaceY + shoreExitMargin;
    }
    private void EnterWater(JourneyWaterVolume volume)
    {
        if (IsSwimmingNow) return;
        previousSpeed = player.Motion.LinearSpeed; previousCanJump = player.Motion.CanJump;
        player.Motion.LinearSpeed = EffectiveSwimSpeed; player.Motion.CanJump = false;
        player.Driver.SetGravityInfluence(GravityKey, 0f); player.Driver.ResetVerticalVelocity();
        water = volume; IsSwimmingNow = true; activeSwimmer = this;
        targetMouthY = volume.SurfaceY + breathingClearance;
        if (animator != null && layer >= 0) animator.CrossFadeInFixedTime(LayerName + ".Surface", animationBlendSeconds, layer);
    }
    private void LeaveWater()
    {
        if (!IsSwimmingNow) return;
        if (player != null && player.Motion != null && player.Driver != null)
        {
            player.Motion.LinearSpeed = previousSpeed; player.Motion.CanJump = previousCanJump;
            player.Motion.StopToDirection(1);
            player.Driver.RemoveGravityInfluence(GravityKey); player.Driver.ResetVerticalVelocity();
        }
        IsSwimmingNow = false; water = null;
        if (activeSwimmer == this) activeSwimmer = null;
    }
    private void BlendAnimation(bool swimming, float speed, bool edge)
    {
        if (animator == null || layer < 0 || controller != animator.runtimeAnimatorController) return;
        if (!player.isActiveAndEnabled || !animator.isActiveAndEnabled) { layerWeight = 0f; return; }
        float step = Time.deltaTime / Mathf.Max(0.05f, animationBlendSeconds);
        layerWeight = Mathf.MoveTowards(layerWeight, swimming ? 1f : 0f, step);
        speedBlend = Mathf.MoveTowards(speedBlend, speed, Time.deltaTime * 3f);
        animator.SetLayerWeight(layer, layerWeight); animator.SetFloat(SpeedParameter, speedBlend);
        if (hasPlaybackParameter) animator.SetFloat(PlaybackSpeedParameter, Mathf.Max(0.1f, animationSpeed));
        if (swimming && edge != inEdgeState)
        {
            inEdgeState = edge;
            animator.CrossFadeInFixedTime(LayerName + (edge && approachEdgeClip != null ? ".Approach Edge" : ".Surface"), animationBlendSeconds, layer);
        }
    }
    private void OnApplicationFocus(bool value) { focused = value; }
    private void OnApplicationPause(bool value) { paused = value; }
    private void OnDisable()
    {
        LeaveWater(); layerWeight = 0f;
        if (animator != null && animator.isActiveAndEnabled && player != null && player.isActiveAndEnabled && layer >= 0 && controller == animator.runtimeAnimatorController) animator.SetLayerWeight(layer, 0f);
    }
}
