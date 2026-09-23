using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Serialization;

[DefaultExecutionOrder(-300)]
[DisallowMultipleComponent]
public sealed class JourneyStoneThrowController : MonoBehaviour
{
    [Header("Scene references")]
    [SerializeField] private JourneyLevelsController levels;
    [SerializeField] private JourneyMenuController menu;
    [SerializeField, Min(1)] private int activeLevel = 2;
    [SerializeField] private Transform player;
    [SerializeField] private Camera gameplayCamera;
    [SerializeField] private Transform handSocket;
    [Tooltip("An empty object near the hand, outside the Player collider.")]
    [SerializeField] private Transform throwOrigin;
    [SerializeField] private JourneyStoneTower target;
    [SerializeField] private Transform stonesRoot;
    [Header("Hint panel - drag the parent UI Image here")]
    [Tooltip("Image containing the instruction text. The whole panel fades together. Keep it separate from this controller and gameplay controls.")]
    [SerializeField] private Image hintImage;
    [SerializeField, Min(0f)] private float hintFadeInSeconds = 0.35f;
    [SerializeField, Min(0f)] private float hintVisibleSeconds = 3f;
    [SerializeField, Min(0f)] private float hintFadeOutSeconds = 0.5f;
    [Header("Touch Throw, drag to aim, release to throw")]
    [Tooltip("Assign the UI Throw button. Keep its On Click list empty.")]
    [SerializeField] private Button throwButton;
    [Tooltip("Minimum drag distance as a fraction of the shorter screen dimension. A tap does not throw.")]
    [SerializeField, Range(0.001f, 0.1f)] private float minimumDragDistance = 0.015f;
    [SerializeField] private LineRenderer trajectory;
    [Tooltip("Optional marker displayed above the nearest available stone.")]
    [SerializeField] private Transform pickupMarker;
    [Header("Pickup Range")]
    [SerializeField, Min(0.2f)] private float pickupDistance = 2.5f;

    [Header("THROW CHARGE SETTINGS")]
    [Tooltip("Seconds held from touching Throw until maximum launch power. Does not change animation speed.")]
    [InspectorName("Full Charge Seconds")]
    [SerializeField, Min(0.1f)] private float fullChargeSeconds = 7f;

    [Tooltip("Launch speed limit with a quick drag and release. Low charge can fall short of distant targets.")]
    [InspectorName("Minimum Throw Speed")]
    [SerializeField, Min(0.1f)] private float minimumThrowSpeed = 10f;

    [Tooltip("Launch speed limit at full charge. Nearby targets use a slower arc. Does not change animation speed.")]
    [InspectorName("Maximum Throw Speed")]
    [FormerlySerializedAs("maximumThrowSpeed")]
    [SerializeField, Min(0.1f)] private float throwSpeed = 40f;

    [Header("Natural Arc - longer distance takes more time")]
    [Tooltip("Minimum flight time in seconds, before the distance contribution.")]
    [SerializeField, Min(0.1f)] private float minimumFlightSeconds = 0.8f;
    [Tooltip("Extra flight seconds per Unity unit of target distance. Increase for slower, higher arcs.")]
    [SerializeField, Min(0f)] private float flightSecondsPerUnit = 0.03f;
    [Tooltip("Minimum arc lift above a straight line between launch and target.")]
    [SerializeField, Min(0f)] private float minimumArcHeight = 1.5f;

    [Header("Aiming and Trajectory")]
    [SerializeField, Min(1f)] private float aimRayDistance = 100f;
    [Tooltip("Include tower, walls and ground. EXCLUDE Player and pickup Stone layers.")]
    [SerializeField] private LayerMask flightCollisionLayers = ~0;
    [Tooltip("Include solid scenery and stones; the closest hit must be a stone.")]
    [SerializeField] private LayerMask pickupRayLayers = ~0;
    [SerializeField, Range(12, 80)] private int previewSegments = 40;
    [SerializeField, Min(0.02f)] private float previewStep = 0.08f;
    [Header("PICKUP - actual playback duration, not a start delay")]
    [SerializeField, Min(0.2f)] private float pickupDurationSeconds = 2f;
    [Tooltip("Fraction of the pickup animation where the hand touches the stone. 0.35 means 0.7 seconds in a two-second animation.")]
    [SerializeField, Range(0f, 0.95f)] private float pickupAttachFraction = 0.35f;
    [SerializeField] private string pickupLayerName = "Journey Stone Actions";
    [Tooltip("State name, or full state path including layer and sub-state machines.")]
    [SerializeField] private string pickupStateName = "Pickup";
    [SerializeField] private bool logPickupDiagnostics = true;
    [Header("Optional character animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string throwTrigger = "";
    [Header("THROW - stone release timing")]
    [Tooltip("Seconds between starting the throw animation and releasing the stone from the hand. Increase this if the stone leaves too early. Does not change animation speed.")]
    [InspectorName("Throw Release Delay Seconds")]
    [SerializeField, Min(0f)] private float releaseDelay = 0.2f;
    [Header("Optional sound")]
    [SerializeField] private AudioSource effectsSource;
    [SerializeField] private AudioClip pickupSound;
    [SerializeField] private AudioClip throwSound;

    private static readonly HashSet<JourneyStoneThrowController> blockers = new HashSet<JourneyStoneThrowController>();
    public static bool IsMovementBlocked => blockers.Count != 0;
    public JourneyStoneTower Target => target;
    public bool CanContinueFlight => initialized && hasFocus && !paused && isActiveAndEnabled && levels != null && levels.SelectedLevelNumber == activeLevel
        && (menu == null || !menu.IsMenuOpen);
    private JourneyThrowableStone held;
    private JourneyThrowableStone pickupCandidate;
    private Coroutine pickupRoutine;
    private bool pickingUp;
    private JourneyThrowableStone[] stones;
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private bool releasing, wasActive, initialized;
    private Vector3 releaseAimPoint;
    private int dragPointerId = int.MinValue;
    private Vector2 dragStart, dragPosition;
    private Vector2 dragScreenSize;
    private bool draggingThrow;
    private float releaseSpeed;
    private float chargeStartedAt;
    public float Charge01 => draggingThrow
        ? Mathf.Clamp01((Time.time - chargeStartedAt) / Mathf.Max(0.1f, fullChargeSeconds)) : 0f;
    private float ChargedThrowSpeed => Mathf.Lerp(
        Mathf.Clamp(minimumThrowSpeed, 0.1f, Mathf.Max(0.1f, throwSpeed)),
        Mathf.Max(0.1f, throwSpeed), Charge01);
    private Animator pickupAnimator;
    private float previousAnimatorSpeed, previousLayerWeight;
    private bool pickupAnimationOwned;
    private AnimatorCullingMode previousCulling;
    private RuntimeAnimatorController pickupController;
    private AnimatorStateInfo previousPickupState;
    private int pickupLayerIndex, pickupStateHash;
    private float pickupPlaybackStarted, pickupPlaybackDuration;
    private float PickupProgress => Mathf.Clamp01((Time.time - pickupPlaybackStarted) / pickupPlaybackDuration);
    private Coroutine releaseRoutine;
    private Coroutine hintRoutine;
    private CanvasGroup hintGroup;
    private int blockThroughFrame = -1;
    private int pickupPointerId = int.MinValue;
    private bool hasFocus = true, paused;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { blockers.Clear(); }

    private void Start()
    {
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        if (levels == null) levels = FindFirstObjectByType<JourneyLevelsController>();
        if (menu == null) menu = FindFirstObjectByType<JourneyMenuController>();
        if (player == null || gameplayCamera == null || handSocket == null || throwOrigin == null ||
            target == null || stonesRoot == null || levels == null || throwButton == null)
        {
            Debug.LogError("JourneyStoneThrowController: assign Player, Camera, Hand Socket, Throw Origin, Target, Stones Root, Levels and Throw Button.", this);
            enabled = false;
            return;
        }
        stones = stonesRoot.GetComponentsInChildren<JourneyThrowableStone>(true);
        if (stones.Length == 0) Debug.LogWarning("No JourneyThrowableStone components found under Stones Root.", this);
        if (trajectory != null) { trajectory.useWorldSpace = true; trajectory.enabled = false; }
        PrepareHintPanel();
        ResolveHandAnimator();
        initialized = true;
        UpdateUI();
    }

    private void Update()
    {
        bool active = CanContinueFlight;
        if (!active)
        {
            if (wasActive && hasFocus && !paused) ResetRound();
            wasActive = false;
            UpdateUI();
            return;
        }
        wasActive = true;
        if (pickingUp)
        {
            if (pickupAnimator == null || !pickupAnimator.isActiveAndEnabled ||
                pickupAnimator.runtimeAnimatorController != pickupController)
            {
                Debug.LogWarning("Pickup canceled: the hand Animator was disabled or its controller changed during playback.", this);
                CancelPickup();
                pickupPointerId = int.MinValue;
                SetBlock(false);
            }
            else SamplePickupAnimation();
        }
        if (!pickingUp && !draggingThrow && pickupPointerId == int.MinValue && Time.frameCount > blockThroughFrame) SetBlock(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = false;
        ReadPointers();
        if (held != null && draggingThrow && !pickingUp && !releasing) DrawPreview();
        else if (trajectory != null) trajectory.enabled = false;
        UpdateUI();
    }

    private void LateUpdate()
    {
        if (CanContinueFlight) Cursor.visible = false;
    }

    private void ReadPointers()
    {
        if (draggingThrow && dragScreenSize != new Vector2(Screen.width, Screen.height)) CancelThrowDrag();
        bool touchesPresent = false, pickupPresent = false, dragPresent = false;
        if (Touchscreen.current != null)
        {
            foreach (var touch in Touchscreen.current.touches)
            {
                bool pressed = touch.press.isPressed;
                var phase = touch.phase.ReadValue();
                bool ended = phase == UnityEngine.InputSystem.TouchPhase.Ended;
                bool canceled = phase == UnityEngine.InputSystem.TouchPhase.Canceled;
                if (!pressed && !ended && !canceled) continue;
                touchesPresent = true;
                int id = touch.touchId.ReadValue();
                if (id == pickupPointerId && pressed) pickupPresent = true;
                if (id == dragPointerId) dragPresent = true;
                HandlePointer(id, touch.position.ReadValue(), touch.press.wasPressedThisFrame, ended, canceled);
                if (id == dragPointerId) dragPresent = true;
            }
        }
        if (!touchesPresent && Mouse.current != null)
        {
            var mouse = Mouse.current;
            if (pickupPointerId == -1 && mouse.leftButton.isPressed) pickupPresent = true;
            HandlePointer(-1, mouse.position.ReadValue(), mouse.leftButton.wasPressedThisFrame,
                mouse.leftButton.wasReleasedThisFrame, false);
            dragPresent = dragPointerId == -1 && mouse.leftButton.isPressed;
        }
        if (draggingThrow && !dragPresent) CancelThrowDrag();
        if (!pickupPresent && Time.frameCount > blockThroughFrame) pickupPointerId = int.MinValue;
    }

    private void HandlePointer(int id, Vector2 position, bool down, bool ended, bool canceled)
    {
        if (draggingThrow)
        {
            if (id != dragPointerId) return;
            if (canceled) { CancelThrowDrag(); return; }
            dragPosition = position;
            if (ended)
            {
                Vector2 delta = position - dragStart;
                float threshold = minimumDragDistance * Mathf.Min(Screen.width, Screen.height);
                bool valid = delta.sqrMagnitude >= threshold * threshold &&
                    gameplayCamera.pixelRect.Contains(position);
                float speed = ChargedThrowSpeed;
                CancelThrowDrag();
                if (valid) ThrowHeldStone(position, speed);
            }
            return;
        }
        if (!down || canceled || releasing || pickingUp) return;
        if (held == null) { TryPickUp(id, position); return; }
        if (throwButton == null || !throwButton.isActiveAndEnabled || !throwButton.IsInteractable() ||
            SelectableAt(position) != throwButton) return;
        draggingThrow = true;
        chargeStartedAt = Time.time;
        dragPointerId = id;
        dragStart = dragPosition = position;
        dragScreenSize = new Vector2(Screen.width, Screen.height);
        // Capture only the throw gesture; ordinary carrying leaves both controls active.
        SetBlock(true);
    }

    private void CancelThrowDrag()
    {
        draggingThrow = false;
        dragPointerId = int.MinValue;
        blockThroughFrame = Time.frameCount + 1;
    }

    private void TryPickUp(int id, Vector2 position)
    {
        // A fresh touch on a nearby stone starts pickup; carrying is handled by the button drag.
        if (held != null || releasing || pickingUp || SelectableAt(position) != null ||
            LightBrushPuzzle.IsMovementBlocked) return;
        Ray ray = gameplayCamera.ScreenPointToRay(position);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, pickupRayLayers, QueryTriggerInteraction.Ignore)) return;
        JourneyThrowableStone stone = hit.collider.GetComponentInParent<JourneyThrowableStone>();
        if (stone == null || !stone.transform.IsChildOf(stonesRoot) || !stone.CanPickUp ||
            Vector3.Distance(player.position, stone.transform.position) > pickupDistance) return;
        if (!BeginPickupAnimation()) return;
        pickingUp = true;
        pickupCandidate = stone;
        pickupPointerId = id;
        SetBlock(true);
        pickupRoutine = StartCoroutine(PickupSequence());
    }

    private IEnumerator PickupSequence()
    {
        // Playback already started at normalized time zero on the pointer-down frame.
        // This wait only determines when the stone attaches to the hand.
        yield return null;
        LogPickupClip();
        bool attached = false;
        while (true)
        {
            if (!CanContinueFlight || pickupAnimator == null ||
                pickupAnimator.runtimeAnimatorController != pickupController)
            { CancelPickup(); yield break; }
            float progress = PickupProgress;
            if (!attached && progress >= Mathf.Clamp01(pickupAttachFraction))
            {
                if (pickupCandidate == null || !pickupCandidate.CanPickUp || !pickupCandidate.PickUp(handSocket))
                { CancelPickup(); yield break; }
                held = pickupCandidate;
                attached = true;
                PlaySound(pickupSound);
            }
            if (progress >= 1f) break;
            yield return null;
        }
        RestorePickupAnimation();
        pickingUp = false;
        pickupCandidate = null;
        pickupRoutine = null;
        blockThroughFrame = Time.frameCount + 1;
        ShowPickupHint();
        UpdateUI();
    }

    private void CancelPickup()
    {
        RestorePickupAnimation();
        if (!pickingUp) return;
        if (pickupRoutine != null) StopCoroutine(pickupRoutine);
        pickupRoutine = null;
        if (pickupCandidate != null) pickupCandidate.ResetStone();
        if (held == pickupCandidate) held = null;
        pickupCandidate = null;
        pickingUp = false;
        blockThroughFrame = Time.frameCount + 1;
    }

    private Vector3 AimPointAt(Vector2 screenPosition)
    {
        Ray ray = gameplayCamera.ScreenPointToRay(screenPosition);
        // Ignore the player's own body and pickup stones, even with an All Layers mask.
        float closest = aimRayDistance;
        Vector3 point = ray.GetPoint(closest);
        foreach (RaycastHit hit in Physics.RaycastAll(ray, aimRayDistance,
                     flightCollisionLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(player) ||
                hit.collider.GetComponentInParent<JourneyThrowableStone>() != null || hit.distance >= closest) continue;
            closest = hit.distance;
            point = hit.point;
        }
        return point;
    }

    private Vector3 VelocityTo(Vector3 aimPoint, float speedLimit, out float flightTime)
    {
        Vector3 displacement = aimPoint - throwOrigin.position;
        float gravity = Physics.gravity.magnitude;
        flightTime = Mathf.Max(0.1f, minimumFlightSeconds) +
            displacement.magnitude * Mathf.Max(0f, flightSecondsPerUnit);
        if (gravity > 0.0001f)
        {
            // The arc rises g*T*T/8 above the straight launch-to-target chord at halfway.
            float arcTime = Mathf.Sqrt(8f * Mathf.Max(0f, minimumArcHeight) / gravity);
            flightTime = Mathf.Max(flightTime, arcTime);
        }
        Vector3 velocity = (displacement - 0.5f * Physics.gravity * flightTime * flightTime) / flightTime;
        // Charge sets the available energy. Insufficient charge falls short instead
        // of silently supplying extra power or moving the stone along a scripted path.
        return Vector3.ClampMagnitude(velocity, Mathf.Max(0.1f, speedLimit));
    }

    private void DrawPreview()
    {
        if (trajectory == null || held == null) return;
        trajectory.enabled = true;
        int count = Mathf.Clamp(previewSegments, 12, 80);
        trajectory.positionCount = count + 1;
        Vector3 origin = throwOrigin.position, previous = origin;
        Vector3 velocity = VelocityTo(AimPointAt(dragPosition), ChargedThrowSpeed, out float flightTime);
        float step = Mathf.Max(Mathf.Max(0.02f, previewStep), flightTime / count);
        trajectory.SetPosition(0, origin);
        for (int i = 1; i <= count; i++)
        {
            float time = i * step;
            Vector3 point = origin + velocity * time + Physics.gravity * (0.5f * time * time);
            Vector3 delta = point - previous;
            if (Physics.SphereCast(previous, held.Radius, delta.normalized, out RaycastHit hit,
                delta.magnitude, flightCollisionLayers, QueryTriggerInteraction.Ignore))
            {
                trajectory.SetPosition(i, previous + delta.normalized * hit.distance);
                trajectory.positionCount = i + 1;
                break;
            }
            trajectory.SetPosition(i, point);
            previous = point;
        }
    }

    private void ThrowHeldStone(Vector2 screenPosition, float speed)
    {
        if (!CanContinueFlight || held == null || pickingUp || releasing) return;
        releaseAimPoint = AimPointAt(screenPosition);
        releaseSpeed = speed;
        releasing = true;
        if (trajectory != null) trajectory.enabled = false;
        Trigger(throwTrigger);
        releaseRoutine = StartCoroutine(ReleaseAfterAnimation());
        UpdateUI();
    }

    private IEnumerator ReleaseAfterAnimation()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, releaseDelay));
        if (held != null && CanContinueFlight)
        {
            held.Launch(throwOrigin.position, VelocityTo(releaseAimPoint, releaseSpeed, out _), flightCollisionLayers, this);
            held = null;
            PlaySound(throwSound);
        }
        releasing = false;
        releaseRoutine = null;
        blockThroughFrame = Time.frameCount + 1;
    }

    public void CancelAim()
    {
        CancelThrowDrag();
        if (releaseRoutine != null) StopCoroutine(releaseRoutine);
        releaseRoutine = null;
        releasing = false;
        if (trajectory != null) trajectory.enabled = false;
        blockThroughFrame = Time.frameCount + 1;
    }

    private void SetBlock(bool value)
    {
        bool changed = value ? blockers.Add(this) : blockers.Remove(this);
        if (!changed) return;
        RightScreenDragArea.ClearInput();
        JourneyMovementSettings.RefreshPuzzleInput();
    }

    private Selectable SelectableAt(Vector2 position)
    {
        if (EventSystem.current == null) return null;
        uiHits.Clear();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
        foreach (var hit in uiHits)
        {
            if (!(hit.module is GraphicRaycaster)) continue;
            Selectable selectable = hit.gameObject.GetComponentInParent<Selectable>();
            if (selectable != null) return selectable;
        }
        return null;
    }

    private void UpdateUI()
    {
        bool active = CanContinueFlight;
        if (throwButton != null)
        {
            throwButton.gameObject.SetActive(active && held != null && !pickingUp);
            throwButton.interactable = !releasing;
        }
        if (!active || held == null || pickingUp || releasing) HidePickupHint();
        if (pickupMarker != null)
        {
            JourneyThrowableStone closest = null;
            float best = pickupDistance;
            if (active && held == null && !pickingUp && stones != null)
                foreach (var stone in stones)
                {
                    if (stone == null || !stone.CanPickUp) continue;
                    float distance = Vector3.Distance(player.position, stone.transform.position);
                    if (distance < best) { best = distance; closest = stone; }
                }
            pickupMarker.gameObject.SetActive(closest != null);
            if (closest != null) pickupMarker.position = closest.transform.position + Vector3.up * 0.4f;
        }
    }

    private void PrepareHintPanel()
    {
        if (hintImage == null) return;
        if (transform.IsChildOf(hintImage.transform) ||
            (throwButton != null && throwButton.transform.IsChildOf(hintImage.transform)))
        {
            Debug.LogWarning("Hint Image must be a separate panel, not a parent of the controller or Throw button.", this);
            hintImage = null;
            return;
        }
        hintGroup = hintImage.GetComponent<CanvasGroup>();
        if (hintGroup == null) hintGroup = hintImage.gameObject.AddComponent<CanvasGroup>();
        hintGroup.interactable = false;
        hintGroup.blocksRaycasts = false;
        foreach (Graphic graphic in hintImage.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        HidePickupHint();
    }

    private void ShowPickupHint()
    {
        if (hintGroup == null || hintImage == null || !CanContinueFlight) return;
        HidePickupHint();
        hintImage.gameObject.SetActive(true);
        hintRoutine = StartCoroutine(AnimatePickupHint());
    }

    private IEnumerator AnimatePickupHint()
    {
        yield return FadeHint(0f, 1f, hintFadeInSeconds);
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, hintVisibleSeconds));
        yield return FadeHint(1f, 0f, hintFadeOutSeconds);
        if (hintImage != null) hintImage.gameObject.SetActive(false);
        hintRoutine = null;
    }

    private IEnumerator FadeHint(float from, float to, float duration)
    {
        if (hintGroup == null) yield break;
        float elapsed = 0f;
        hintGroup.alpha = from;
        while (elapsed < Mathf.Max(0f, duration))
        {
            yield return null;
            if (hintGroup == null) yield break;
            elapsed += Time.unscaledDeltaTime;
            hintGroup.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / duration));
        }
        hintGroup.alpha = to;
    }

    private void HidePickupHint()
    {
        if (hintRoutine != null) StopCoroutine(hintRoutine);
        hintRoutine = null;
        if (hintGroup != null) hintGroup.alpha = 0f;
        if (hintImage != null) hintImage.gameObject.SetActive(false);
    }

    private void ResolveHandAnimator()
    {
        // The animator driving the actual hand is authoritative, not an unrelated Inspector reference.
        Animator handAnimator = handSocket != null ? handSocket.GetComponentInParent<Animator>() : null;
        if (handAnimator != null && animator != handAnimator)
        {
            if (animator != null)
                Debug.LogWarning("Stone pickup: using the Animator above Hand Socket instead of the unrelated assigned Animator.", this);
            animator = handAnimator;
        }
    }

    private bool BeginPickupAnimation()
    {
        RestorePickupAnimation();
        ResolveHandAnimator();
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
        {
            Debug.LogError("Pickup needs the enabled Animator driving Hand Socket and a Runtime Animator Controller.", this);
            return false;
        }
        pickupLayerIndex = animator.GetLayerIndex(pickupLayerName);
        if (pickupLayerIndex < 0)
        {
            Debug.LogError("Pickup layer not found: " + pickupLayerName + ". Enter its exact name from the active Animator window.", this);
            return false;
        }
        string path = pickupStateName.StartsWith(pickupLayerName + ".", System.StringComparison.Ordinal)
            ? pickupStateName : pickupLayerName + "." + pickupStateName;
        pickupStateHash = Animator.StringToHash(path);
        if (!animator.HasState(pickupLayerIndex, pickupStateHash))
        {
            Debug.LogError("Pickup state not found: " + path + ". Use the Animator state name, not the clip filename.", this);
            return false;
        }
        pickupAnimator = animator;
        pickupController = animator.runtimeAnimatorController;
        previousAnimatorSpeed = animator.speed;
        previousCulling = animator.cullingMode;
        previousLayerWeight = animator.GetLayerWeight(pickupLayerIndex);
        previousPickupState = animator.GetCurrentAnimatorStateInfo(pickupLayerIndex);
        pickupPlaybackDuration = Mathf.Max(0.2f, pickupDurationSeconds);
        pickupPlaybackStarted = Time.time;
        pickupAnimationOwned = true;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        // Clear stale action triggers so they cannot restart the pickup/throw on restore.
        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.type == AnimatorControllerParameterType.Trigger &&
                (parameter.name == "Pickup" || parameter.name == throwTrigger)) animator.ResetTrigger(parameter.name);
        SamplePickupAnimation();
        if (logPickupDiagnostics)
            Debug.Log("Pickup: animator=" + animator.name + ", controller=" + pickupController.name +
                ", state=" + path + ", playback duration=" + pickupPlaybackDuration + "s, starts now.", this);
        return true;
    }

    private void SamplePickupAnimation()
    {
        if (!pickupAnimationOwned || pickupAnimator == null) return;
        // Let Unity evaluate the pose in its normal animation phase. We control the
        // normalized timeline, so state Speed/PickupSpeed cannot rush through it.
        pickupAnimator.speed = 0f;
        pickupAnimator.SetLayerWeight(pickupLayerIndex, 1f);
        pickupAnimator.Play(pickupStateHash, pickupLayerIndex, Mathf.Min(PickupProgress, 0.9999f));
    }

    private void LogPickupClip()
    {
        if (pickupAnimator == null) return;
        AnimatorClipInfo[] clips = pickupAnimator.GetCurrentAnimatorClipInfo(pickupLayerIndex);
        if (clips.Length == 0)
            Debug.LogWarning("Pickup state has no active clip. Check its Motion field and layer setup.", this);
        foreach (AnimatorClipInfo info in clips)
        {
            if (info.clip == null) continue;
            if (logPickupDiagnostics)
                Debug.Log("Pickup clip=" + info.clip.name + ", source length=" + info.clip.length +
                    "s, Humanoid=" + info.clip.isHumanMotion + ", requested duration=" + pickupPlaybackDuration + "s.", this);
            if (pickupAnimator.isHuman && !info.clip.isHumanMotion)
                Debug.LogWarning("Pickup uses a Generic clip on a Humanoid Animator. Matching bone names is not Humanoid retargeting. Re-import the source animation as Humanoid with a valid Avatar.", this);
        }
    }

    private void RestorePickupAnimation()
    {
        if (!pickupAnimationOwned) return;
        pickupAnimationOwned = false;
        if (pickupAnimator != null)
        {
            if (pickupAnimator.runtimeAnimatorController == pickupController &&
                pickupAnimator.HasState(pickupLayerIndex, previousPickupState.fullPathHash))
            {
                pickupAnimator.Play(previousPickupState.fullPathHash, pickupLayerIndex, previousPickupState.normalizedTime);
                pickupAnimator.SetLayerWeight(pickupLayerIndex, previousLayerWeight);
            }
            pickupAnimator.speed = previousAnimatorSpeed;
            pickupAnimator.cullingMode = previousCulling;
        }
        pickupAnimator = null;
        pickupController = null;
    }

    private void Trigger(string parameter)
    {
        if (animator == null || string.IsNullOrEmpty(parameter)) return;
        if (animator.runtimeAnimatorController != null)
            foreach (AnimatorControllerParameter item in animator.parameters)
                if (item.name == parameter && item.type == AnimatorControllerParameterType.Trigger)
                { animator.SetTrigger(parameter); return; }
        Debug.LogWarning("Stone animation: the hand Animator has no Trigger named '" + parameter +
            "'. Check Throw Trigger and the assigned Animator Controller.", this);
    }
    private void PlaySound(AudioClip clip)
    { if (effectsSource != null && clip != null) effectsSource.PlayOneShot(clip); }

    public void ResetRound()
    {
        HidePickupHint();
        CancelThrowDrag();
        CancelPickup();
        if (releaseRoutine != null) StopCoroutine(releaseRoutine);
        releaseRoutine = null;
        releasing = false;
        pickupPointerId = int.MinValue;
        held = null;
        if (trajectory != null) trajectory.enabled = false;
        SetBlock(false);
        if (stones != null)
            foreach (var stone in stones) if (stone != null) stone.ResetStone();
    }

    private void OnApplicationFocus(bool focus) { hasFocus = focus; if (!focus) { CancelPickup(); CancelAim(); pickupPointerId = int.MinValue; SetBlock(false); } }
    private void OnApplicationPause(bool pause) { paused = pause; if (pause) { CancelPickup(); CancelAim(); pickupPointerId = int.MinValue; SetBlock(false); } }
    private void OnDisable()
    {
        ResetRound();
        wasActive = false;
        if (throwButton != null) throwButton.gameObject.SetActive(false);
        HidePickupHint();
        if (pickupMarker != null) pickupMarker.gameObject.SetActive(false);
    }
}
