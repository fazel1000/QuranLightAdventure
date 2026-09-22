using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

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
    [Header("UI - keep On Click lists empty")]
    [SerializeField] private Button throwButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TMP_Text hintText;
    [SerializeField] private LineRenderer trajectory;
    [Tooltip("Optional marker displayed above the nearest available stone.")]
    [SerializeField] private Transform pickupMarker;
    [Header("Throw")]
    [SerializeField, Min(0.2f)] private float pickupDistance = 2.5f;
    [SerializeField, Min(0.1f)] private float minimumThrowSpeed = 6f;
    [SerializeField, Min(0.1f)] private float maximumThrowSpeed = 22f;
    [SerializeField, Min(0.1f)] private float fullChargeSeconds = 1.5f;
    [SerializeField, Min(1f)] private float aimRayDistance = 100f;
    [Header("Optional pointer / charge display")]
    [SerializeField] private RectTransform aimReticle;
    [SerializeField] private Image chargeFill;
    [Tooltip("Include tower, walls and ground. EXCLUDE Player and pickup Stone layers.")]
    [SerializeField] private LayerMask flightCollisionLayers = ~0;
    [Tooltip("Include solid scenery and stones; the closest hit must be a stone.")]
    [SerializeField] private LayerMask pickupRayLayers = ~0;
    [SerializeField, Range(12, 80)] private int previewSegments = 40;
    [SerializeField, Min(0.02f)] private float previewStep = 0.08f;
    [Header("Pickup timing in seconds - tune against the clip preview")]
    [SerializeField, Min(0f)] private float pickupAttachDelay = 1.2f;
    [SerializeField, Min(0.2f)] private float pickupTotalDuration = 3.85f;
    [Header("Optional character animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string pickupTrigger = "";
    [SerializeField] private string throwTrigger = "";
    [SerializeField, Min(0f)] private float releaseDelay = 0.2f;
    [Header("Optional aiming camera")]
    [Tooltip("Keep outside Camera. Parent to Player for a shoulder-relative pose.")]
    [SerializeField] private Transform aimCameraPose;
    [Tooltip("Camera driver components only. Never put this controller here.")]
    [SerializeField] private Behaviour[] cameraDriversToPause;
    [SerializeField, Min(0.1f)] private float cameraBlendSpeed = 10f;
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
    private bool aiming, releasing, wasActive, initialized;
    private int pointerId = int.MinValue;
    private Vector2 aimScreenPosition;
    private float chargeStarted;
    private Vector3 releasedVelocity;
    private bool cursorOwned, oldCursorVisible;
    private CursorLockMode oldCursorLock;
    public float Charge01 => aiming ? Mathf.Clamp01((Time.time - chargeStarted) / Mathf.Max(0.1f, fullChargeSeconds)) : 0f;
    private float ChargedSpeed => Mathf.Lerp(Mathf.Max(0.1f, minimumThrowSpeed),
        Mathf.Max(minimumThrowSpeed, maximumThrowSpeed), Charge01);
    private Coroutine releaseRoutine;
    private bool[] cameraStates;
    private Vector3 oldCameraPosition;
    private Quaternion oldCameraRotation;
    private bool cameraCaptured;
    private int blockThroughFrame = -1;
    private int aimStartFrame = -1;
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
            target == null || stonesRoot == null || throwButton == null || cancelButton == null || levels == null)
        {
            Debug.LogError("JourneyStoneThrowController: assign Player, Camera, Hand Socket, Throw Origin, Target, Stones Root, Levels and both UI buttons.", this);
            enabled = false;
            return;
        }
        stones = stonesRoot.GetComponentsInChildren<JourneyThrowableStone>(true);
        if (stones.Length == 0) Debug.LogWarning("No JourneyThrowableStone components found under Stones Root.", this);
        if (trajectory != null) { trajectory.useWorldSpace = true; trajectory.enabled = false; }
        if (aimReticle != null)
            foreach (var graphic in aimReticle.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
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
        if (!pickingUp && !aiming && !releasing && pickupPointerId == int.MinValue && Time.frameCount > blockThroughFrame) SetBlock(false);
        if (held != null && !pickingUp)
        {
            ShowCursor();
            if (!aiming && Mouse.current != null && (Touchscreen.current == null || !Touchscreen.current.primaryTouch.press.isPressed))
                aimScreenPosition = Mouse.current.position.ReadValue();
        }
        ReadPointers();
        if (held != null && !pickingUp && !releasing) DrawPreview();
        UpdateUI();
    }

    private void LateUpdate()
    {
        if (!cameraCaptured || gameplayCamera == null || aimCameraPose == null) return;
        float t = 1f - Mathf.Exp(-cameraBlendSpeed * Time.deltaTime);
        gameplayCamera.transform.position = Vector3.Lerp(gameplayCamera.transform.position, aimCameraPose.position, t);
        gameplayCamera.transform.rotation = Quaternion.Slerp(gameplayCamera.transform.rotation, aimCameraPose.rotation, t);
    }

    private void ReadPointers()
    {
        bool touchesPresent = false, ownerPresent = false, pickupPresent = false;
        if (Touchscreen.current != null)
        {
            foreach (var touch in Touchscreen.current.touches)
            {
                bool down = touch.press.wasPressedThisFrame;
                bool pressed = touch.press.isPressed;
                bool up = touch.press.wasReleasedThisFrame;
                if (!pressed && !up) continue;
                touchesPresent = true;
                int id = touch.touchId.ReadValue();
                if (id == pointerId) ownerPresent = true;
                if (id == pickupPointerId && pressed) pickupPresent = true;
                HandlePointer(id, touch.position.ReadValue(), down, pressed, up);
            }
        }
        if (!touchesPresent && Mouse.current != null)
        {
            var mouse = Mouse.current;
            if (pointerId == -1 && mouse.leftButton.isPressed) ownerPresent = true;
            if (pickupPointerId == -1 && mouse.leftButton.isPressed) pickupPresent = true;
            HandlePointer(-1, mouse.position.ReadValue(), mouse.leftButton.wasPressedThisFrame,
                mouse.leftButton.isPressed, mouse.leftButton.wasReleasedThisFrame);
        }
        // Lost touches cancel instead of accidentally throwing on focus/device loss.
        if (aiming && !ownerPresent && Time.frameCount > aimStartFrame && pointerId != int.MinValue) CancelAim();
        if (!pickupPresent && Time.frameCount > blockThroughFrame) pickupPointerId = int.MinValue;
    }

    private void HandlePointer(int id, Vector2 position, bool down, bool pressed, bool up)
    {
        Selectable ui = down || up ? SelectableAt(position) : null;
        if (down && ui == cancelButton && cancelButton.gameObject.activeInHierarchy)
        { CancelAim(); return; }
        if (releasing || pickingUp) return;
        if (aiming)
        {
            if (id != pointerId) return;
            aimScreenPosition = position;
            if (up)
            {
                if (ui == cancelButton) CancelAim();
                else BeginRelease();
            }
            return;
        }
        if (!down) return;
        if (held != null)
        {
            if ((ui == throwButton && throwButton.IsInteractable()) || (id == -1 && ui == null))
                BeginAim(id, position);
            return;
        }
        if (ui != null || LightBrushPuzzle.IsMovementBlocked) return;
        Ray ray = gameplayCamera.ScreenPointToRay(position);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, pickupRayLayers, QueryTriggerInteraction.Ignore)) return;
        JourneyThrowableStone stone = hit.collider.GetComponentInParent<JourneyThrowableStone>();
        if (stone == null || !stone.transform.IsChildOf(stonesRoot) || !stone.CanPickUp ||
            Vector3.Distance(player.position, stone.transform.position) > pickupDistance) return;
        pickingUp = true;
        pickupCandidate = stone;
        aimScreenPosition = id == -1 ? position : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        pickupPointerId = id;
        SetBlock(true);
        Trigger(pickupTrigger);
        pickupRoutine = StartCoroutine(PickupSequence());
    }

    private IEnumerator PickupSequence()
    {
        float attachDelay = Mathf.Max(0f, pickupAttachDelay);
        yield return new WaitForSeconds(attachDelay);
        if (!CanContinueFlight || pickupCandidate == null || !pickupCandidate.CanPickUp ||
            !pickupCandidate.PickUp(handSocket))
        {
            pickingUp = false;
            pickupCandidate = null;
            pickupRoutine = null;
            blockThroughFrame = Time.frameCount + 1;
            yield break;
        }
        held = pickupCandidate;
        PlaySound(pickupSound);
        // PickUp itself eases the stone into the socket over 0.2 seconds.
        yield return new WaitForSeconds(Mathf.Max(0.2f, pickupTotalDuration - attachDelay));
        pickingUp = false;
        pickupCandidate = null;
        pickupRoutine = null;
        blockThroughFrame = Time.frameCount + 1;
        if (held != null && CanContinueFlight) ShowCursor();
        UpdateUI();
    }

    private void CancelPickup()
    {
        if (!pickingUp) return;
        if (pickupRoutine != null) StopCoroutine(pickupRoutine);
        pickupRoutine = null;
        if (pickupCandidate != null) pickupCandidate.ResetStone();
        if (held == pickupCandidate) held = null;
        pickupCandidate = null;
        pickingUp = false;
    }

    private void BeginAim(int id, Vector2 position)
    {
        aiming = true;
        aimStartFrame = Time.frameCount;
        pickupPointerId = int.MinValue;
        pointerId = id;
        aimScreenPosition = position;
        chargeStarted = Time.time;
        SetBlock(true);
        CaptureCamera();
    }

    private Vector3 Velocity()
    {
        Ray ray = gameplayCamera.ScreenPointToRay(aimScreenPosition);
        Vector3 aimPoint = Physics.Raycast(ray, out RaycastHit hit, aimRayDistance,
            flightCollisionLayers, QueryTriggerInteraction.Ignore) ? hit.point : ray.GetPoint(aimRayDistance);
        Vector3 direction = aimPoint - throwOrigin.position;
        if (direction.sqrMagnitude < 0.0001f) direction = ray.direction;
        // No tower reference, snap, ballistic auto-correction or aim assist.
        // Gravity naturally bends the path, shown in the trajectory preview.
        return direction.normalized * ChargedSpeed;
    }

    private void ShowCursor()
    {
        if (!cursorOwned)
        {
            oldCursorVisible = Cursor.visible;
            oldCursorLock = Cursor.lockState;
            cursorOwned = true;
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void RestoreCursor()
    {
        if (!cursorOwned) return;
        cursorOwned = false;
        // The menu owns the cursor when it is open.
        if (menu == null || !menu.IsMenuOpen)
        {
            Cursor.lockState = oldCursorLock;
            Cursor.visible = oldCursorVisible;
        }
    }

    private void DrawPreview()
    {
        if (trajectory == null || held == null) return;
        trajectory.enabled = true;
        int count = Mathf.Clamp(previewSegments, 12, 80);
        trajectory.positionCount = count + 1;
        Vector3 origin = throwOrigin.position, previous = origin, velocity = Velocity();
        trajectory.SetPosition(0, origin);
        for (int i = 1; i <= count; i++)
        {
            float time = i * Mathf.Max(0.02f, previewStep);
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

    private void BeginRelease()
    {
        releasedVelocity = Velocity(); // Snapshot charge and direction BEFORE leaving aim mode.
        aiming = false;
        releasing = true;
        pointerId = int.MinValue;
        if (trajectory != null) trajectory.enabled = false;
        Trigger(throwTrigger);
        releaseRoutine = StartCoroutine(ReleaseAfterAnimation());
    }

    private IEnumerator ReleaseAfterAnimation()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, releaseDelay));
        if (held != null && CanContinueFlight)
        {
            held.Launch(throwOrigin.position, releasedVelocity, flightCollisionLayers, this);
            held = null;
            RestoreCursor();
            PlaySound(throwSound);
        }
        releasing = false;
        releaseRoutine = null;
        RestoreCamera();
        blockThroughFrame = Time.frameCount + 1;
    }

    public void CancelAim()
    {
        if (releaseRoutine != null) StopCoroutine(releaseRoutine);
        releaseRoutine = null;
        releasing = false;
        aiming = false;
        pointerId = int.MinValue;
        if (trajectory != null) trajectory.enabled = false;
        RestoreCamera();
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
        if (cancelButton != null) cancelButton.gameObject.SetActive(active && aiming);
        if (chargeFill != null)
        {
            chargeFill.raycastTarget = false;
            chargeFill.gameObject.SetActive(active && held != null && !pickingUp);
            chargeFill.fillAmount = Charge01;
        }
        if (aimReticle != null)
        {
            aimReticle.gameObject.SetActive(active && held != null && !pickingUp);
            var parent = aimReticle.parent as RectTransform;
            var canvas = aimReticle.GetComponentInParent<Canvas>();
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (parent != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, aimScreenPosition, uiCamera, out Vector2 local))
                aimReticle.localPosition = new Vector3(local.x, local.y, aimReticle.localPosition.z);
        }
        if (hintText != null)
        {
            hintText.gameObject.SetActive(active);
            hintText.text = pickingUp ? "در حال برداشتن سنگ" : releasing ? "" : aiming ? "نگه دار تا قدرت بیشتر شود؛ رها کن تا پرتاب شود" :
                held != null ? "دکمه پرتاب را نگه دار" : "نزدیک سنگ برو و روی آن بزن";
        }
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

    private void CaptureCamera()
    {
        if (aimCameraPose == null || gameplayCamera == null) return;
        oldCameraPosition = gameplayCamera.transform.position;
        oldCameraRotation = gameplayCamera.transform.rotation;
        int count = cameraDriversToPause != null ? cameraDriversToPause.Length : 0;
        cameraStates = new bool[count];
        for (int i = 0; i < count; i++)
        {
            Behaviour driver = cameraDriversToPause[i];
            if (driver == null || driver == this) continue;
            cameraStates[i] = driver.enabled;
            driver.enabled = false;
        }
        cameraCaptured = true;
    }

    private void RestoreCamera()
    {
        if (!cameraCaptured) return;
        cameraCaptured = false;
        if (gameplayCamera != null) gameplayCamera.transform.SetPositionAndRotation(oldCameraPosition, oldCameraRotation);
        for (int i = 0; i < cameraStates.Length; i++)
            if (cameraDriversToPause[i] != null && cameraDriversToPause[i] != this)
                cameraDriversToPause[i].enabled = cameraStates[i];
    }

    private void Trigger(string parameter)
    { if (animator != null && !string.IsNullOrEmpty(parameter)) animator.SetTrigger(parameter); }
    private void PlaySound(AudioClip clip)
    { if (effectsSource != null && clip != null) effectsSource.PlayOneShot(clip); }

    public void ResetRound()
    {
        CancelPickup();
        if (releaseRoutine != null) StopCoroutine(releaseRoutine);
        releaseRoutine = null;
        aiming = releasing = false;
        pickupPointerId = int.MinValue;
        pointerId = int.MinValue;
        held = null;
        RestoreCursor();
        if (trajectory != null) trajectory.enabled = false;
        RestoreCamera();
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
        if (cancelButton != null) cancelButton.gameObject.SetActive(false);
        if (hintText != null) hintText.gameObject.SetActive(false);
        if (pickupMarker != null) pickupMarker.gameObject.SetActive(false);
        if (aimReticle != null) aimReticle.gameObject.SetActive(false);
        if (chargeFill != null) chargeFill.gameObject.SetActive(false);
    }
}
