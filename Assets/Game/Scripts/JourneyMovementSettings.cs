using System;
using GameCreator.Runtime.Common;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Attach this component to the existing, always-active MenuManager.
// Assign the Toggle inside Setting Panel. LeftDragArea is created automatically.
// On Player > Character > Player, select Input Move > Mobile > Left Stick Or Drag.
[DisallowMultipleComponent]
[DefaultExecutionOrder(1200)]
public sealed class JourneyMovementSettings : MonoBehaviour
{
    [Header("Setting Panel")]
    [Tooltip("On: drag anywhere on the left half. Off: the existing Game Creator joystick.")]
    [SerializeField] private Toggle leftDragToggle;

    [Header("Left Drag")]
    [Tooltip("Drag distance in Canvas units needed to reach full movement speed.")]
    [SerializeField, Min(1f)] private float dragRadius = 120f;
    [SerializeField, Range(0f, 0.9f)] private float deadZone = 0.12f;

    private const string PreferenceKey = "JourneyOfLight.Controls.LeftDrag";
    private const int NoPointer = int.MinValue;

    private static JourneyMovementSettings instance;
    private static InputValueVector2MobileLeftStickOrDrag movementInput;

    private JourneyMenuController menu;
    private RectTransform dragArea;
    private bool initialized;
    private bool useLeftDrag;
    private bool hasFocus = true;
    private bool isPaused;
    private bool isQuitting;
    private int activePointerId = NoPointer;
    private Vector2 pressPosition;
    private Vector2 movement;
    private Vector2 lastScreenSize;

    public bool UseLeftDrag => useLeftDrag;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Also reset when Enter Play Mode has domain reload disabled.
        instance = null;
        movementInput = null;
    }

    private void Awake()
    {
        menu = GetComponent<JourneyMenuController>();
        if (menu == null || leftDragToggle == null)
        {
            Debug.LogError("JourneyMovementSettings: put this component on MenuManager " +
                "beside JourneyMenuController and assign Left Drag Toggle.", this);
            enabled = false;
            return;
        }

        Canvas canvas = leftDragToggle.GetComponentInParent<Canvas>(true);
        if (canvas == null || canvas.rootCanvas.renderMode == RenderMode.WorldSpace)
        {
            Debug.LogError("JourneyMovementSettings: the Toggle must be inside the " +
                "existing screen-space UI Canvas.", this);
            enabled = false;
            return;
        }

        useLeftDrag = PlayerPrefs.GetInt(PreferenceKey, 0) == 1;
        hasFocus = Application.isFocused;
        lastScreenSize = new Vector2(Screen.width, Screen.height);
        CreateDragArea(canvas.rootCanvas);
        initialized = true;
    }

    private void OnEnable()
    {
        if (!initialized) return;

        if (instance != null && instance != this)
        {
            Debug.LogError("JourneyMovementSettings: keep only one enabled instance.", this);
            enabled = false;
            return;
        }

        instance = this;
        hasFocus = Application.isFocused;
        leftDragToggle.SetIsOnWithoutNotify(useLeftDrag);
        leftDragToggle.onValueChanged.AddListener(SetLeftDrag);
        RefreshControls();
    }

    private void Start()
    {
        if (initialized && movementInput == null)
        {
            Debug.LogWarning("JourneyMovementSettings: select Player > Character > " +
                "Player > Input Move > Mobile > Left Stick Or Drag to enable the toggle.", this);
        }
    }

    private void CreateDragArea(Canvas canvas)
    {
        GameObject areaObject = new GameObject("LeftDragArea",
            typeof(RectTransform), typeof(Image), typeof(EventTrigger));
        areaObject.SetActive(false);
        dragArea = areaObject.GetComponent<RectTransform>();
        dragArea.SetParent(canvas.transform, false);
        dragArea.SetAsFirstSibling();
        dragArea.anchorMin = Vector2.zero;
        dragArea.anchorMax = new Vector2(0.5f, 1f);
        dragArea.offsetMin = Vector2.zero;
        dragArea.offsetMax = Vector2.zero;
        dragArea.localScale = Vector3.one;

        Image image = areaObject.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, 0f);
        image.raycastTarget = true;

        // It sits behind the existing buttons and covers only the left half.
        // UI raycasting chooses the target before these pointer handlers run.
        EventTrigger trigger = areaObject.GetComponent<EventTrigger>();
        AddPointerEvent(trigger, EventTriggerType.InitializePotentialDrag,
            data => data.useDragThreshold = false);
        AddPointerEvent(trigger, EventTriggerType.PointerDown, BeginDrag);
        AddPointerEvent(trigger, EventTriggerType.Drag, ContinueDrag);
        AddPointerEvent(trigger, EventTriggerType.PointerUp, EndDrag);
    }

    private static void AddPointerEvent(EventTrigger trigger, EventTriggerType type,
        Action<PointerEventData> action)
    {
        EventTrigger.Entry entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(data =>
        {
            if (data is PointerEventData pointer) action(pointer);
        });
        trigger.triggers.Add(entry);
    }

    public void SetLeftDrag(bool enabledDrag)
    {
        useLeftDrag = enabledDrag;
        ResetDrag();

        if (leftDragToggle != null)
            leftDragToggle.SetIsOnWithoutNotify(useLeftDrag);

        PlayerPrefs.SetInt(PreferenceKey, useLeftDrag ? 1 : 0);
        PlayerPrefs.Save();
        RefreshControls();
    }

    private bool GameplayAllowsInput()
    {
        return initialized && hasFocus && !isPaused && !isQuitting &&
            Time.timeScale > 0f && menu != null && !menu.IsMenuOpen;
    }

    private bool CanDrag()
    {
        return isActiveAndEnabled && GameplayAllowsInput() && useLeftDrag &&
            movementInput != null && dragArea != null &&
            dragArea.gameObject.activeInHierarchy;
    }

    private void LateUpdate()
    {
        if (!initialized) return;

        Vector2 screenSize = new Vector2(Screen.width, Screen.height);
        if (screenSize != lastScreenSize)
        {
            lastScreenSize = screenSize;
            ResetDrag();
        }

        // JourneyMenuController runs earlier. Apply the selected mode after its
        // input snapshots are restored, including when the toggle changed in Settings.
        RefreshControls();
    }

    private void RefreshControls()
    {
        if (!initialized || instance != this) return;

        bool gameplay = isActiveAndEnabled && GameplayAllowsInput();
        bool showDrag = gameplay && useLeftDrag && movementInput != null;

        if (dragArea != null && dragArea.gameObject.activeSelf != showDrag)
            dragArea.gameObject.SetActive(showDrag);

        if (!showDrag || dragArea == null || !dragArea.gameObject.activeInHierarchy)
            ResetDrag();

        // Own only the joystick created by the new Input Move provider.
        // Do not change the right drag area or shared Input System actions.
        GameObject joystick = movementInput != null ? movementInput.JoystickRoot : null;
        bool showJoystick = gameplay && !useLeftDrag;
        if (joystick != null && joystick.activeSelf != showJoystick)
            joystick.SetActive(showJoystick);
    }

    private void BeginDrag(PointerEventData data)
    {
        if (!CanDrag() || activePointerId != NoPointer ||
            data.button != PointerEventData.InputButton.Left) return;

        Vector2 screenPosition = data.position;
        if (!Finite(screenPosition) || screenPosition.x < 0f ||
            screenPosition.x >= Screen.width * 0.5f ||
            screenPosition.y < 0f || screenPosition.y >= Screen.height) return;

        if (!TryLocalPosition(data, out Vector2 localPosition)) return;

        activePointerId = data.pointerId;
        pressPosition = localPosition;
        movement = Vector2.zero;
    }

    private void ContinueDrag(PointerEventData data)
    {
        // UGUI pointer IDs remain separate for the left and right fingers.
        if (activePointerId == NoPointer || data.pointerId != activePointerId) return;
        if (!CanDrag() || !TryLocalPosition(data, out Vector2 localPosition))
        {
            ResetDrag();
            return;
        }

        float radius = float.IsNaN(dragRadius) || float.IsInfinity(dragRadius)
            ? 120f : Mathf.Max(1f, dragRadius);
        Vector2 direction = (localPosition - pressPosition) / radius;
        if (!Finite(direction))
        {
            ResetDrag();
            return;
        }

        direction = Vector2.ClampMagnitude(direction, 1f);
        float distance = direction.magnitude;
        float threshold = float.IsNaN(deadZone) || float.IsInfinity(deadZone)
            ? 0.12f : Mathf.Clamp(deadZone, 0f, 0.9f);

        movement = distance <= threshold || distance <= 0.0001f
            ? Vector2.zero
            : direction / distance * ((distance - threshold) / (1f - threshold));
    }

    private bool TryLocalPosition(PointerEventData data, out Vector2 position)
    {
        position = Vector2.zero;
        return dragArea != null && Finite(data.position) &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                dragArea, data.position, data.pressEventCamera, out position) &&
            Finite(position);
    }

    private void EndDrag(PointerEventData data)
    {
        if (data.pointerId == activePointerId) ResetDrag();
    }

    private void ResetDrag()
    {
        activePointerId = NoPointer;
        pressPosition = Vector2.zero;
        movement = Vector2.zero;
    }

    internal static bool TryReadMovement(out Vector2 value)
    {
        value = Vector2.zero;
        if (instance == null || !instance.isActiveAndEnabled) return false;

        if (!instance.GameplayAllowsInput())
        {
            instance.ResetDrag();
            return true;
        }

        if (!instance.useLeftDrag) return false;
        if (instance.CanDrag()) value = instance.movement;
        else instance.ResetDrag();
        return true;
    }

    internal static void AttachInput(InputValueVector2MobileLeftStickOrDrag input)
    {
        movementInput = input;
        if (instance == null) return;
        instance.ResetDrag();
        instance.RefreshControls();
    }

    internal static void DetachInput(InputValueVector2MobileLeftStickOrDrag input)
    {
        if (!ReferenceEquals(movementInput, input)) return;
        movementInput = null;
        if (instance == null) return;
        instance.ResetDrag();
        instance.RefreshControls();
    }

    internal static bool Finite(Vector2 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    }

    private void OnApplicationFocus(bool focused)
    {
        hasFocus = focused;
        ResetDrag();
        RefreshControls();
    }

    private void OnApplicationPause(bool paused)
    {
        isPaused = paused;
        ResetDrag();
        RefreshControls();
    }

    private void OnApplicationQuit()
    {
        isQuitting = true;
    }

    private void OnDisable()
    {
        if (leftDragToggle != null)
            leftDragToggle.onValueChanged.RemoveListener(SetLeftDrag);

        ResetDrag();
        if (dragArea != null) dragArea.gameObject.SetActive(false);
        if (instance != this) return;

        instance = null;
        // If just this component is disabled, the input provider falls back to
        // the normal joystick. Keep it hidden if a menu is still open.
        GameObject joystick = movementInput != null ? movementInput.JoystickRoot : null;
        if (joystick != null) joystick.SetActive(GameplayAllowsInput());
    }

    private void OnDestroy()
    {
        if (dragArea != null) Destroy(dragArea.gameObject);
    }
}

// This is a serializable Game Creator input type, not another MonoBehaviour.
// It intentionally shares this file so installation requires only one .cs file.
[Title("Left Stick Or Drag")]
[Category("Mobile/Left Stick Or Drag")]
[Description("Uses the normal left joystick or the left drag mode chosen in Settings.")]
[Keywords("Movement", "Joystick", "Touch", "Left", "Drag")]
[Serializable]
public sealed class InputValueVector2MobileLeftStickOrDrag : TInputValueVector2MobileStick
{
    internal GameObject JoystickRoot => Stick != null ? Stick.Root : null;

    protected override ITouchStick CreateTouchStick()
    {
        return TouchStickLeft.Create();
    }

    public override void OnStartup()
    {
        base.OnStartup();
        JourneyMovementSettings.AttachInput(this);
    }

    public override void OnDispose()
    {
        JourneyMovementSettings.DetachInput(this);
        base.OnDispose();
    }

    public override Vector2 Read()
    {
        if (JourneyMovementSettings.TryReadMovement(out Vector2 movement))
            return movement;

        Vector2 joystick = base.Read();
        return JourneyMovementSettings.Finite(joystick)
            ? Vector2.ClampMagnitude(joystick, 1f)
            : Vector2.zero;
    }
}
