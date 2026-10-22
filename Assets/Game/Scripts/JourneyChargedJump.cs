using GameCreator.Runtime.Characters;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class JourneyChargedJump : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public Character player;
    public JourneyMenuController menu;
    public JourneyVehicleDriver vehicle;
    public JourneyDronePilot drone;
    [Range(0.05f, 1.5f)] public float minimumHeight = 0.3f;
    [Range(0.05f, 1.5f)] public float maximumHeight = 1.5f;
    [Min(0.1f)] public float fullChargeSeconds = 0.8f;
    public bool hideWhenUnavailable = true;
    private CanvasGroup visibility;
    private float originalAlpha;
    private bool originalInteractable, originalBlocks;
    private int pointer = int.MinValue;
    private float began, takeoffY, height, jumpTime;
    private bool jumping, leftGround;
    public float Charge01 => pointer == int.MinValue ? 0f : Mathf.Clamp01((Time.unscaledTime - began) / Mathf.Max(0.1f, fullChargeSeconds));
    private void Awake()
    {
        visibility = GetComponent<CanvasGroup>();
        if (visibility == null && hideWhenUnavailable) visibility = gameObject.AddComponent<CanvasGroup>();
        if (visibility != null)
        { originalAlpha = visibility.alpha; originalInteractable = visibility.interactable; originalBlocks = visibility.blocksRaycasts; }
        if (player == null) foreach (var c in FindObjectsByType<Character>(FindObjectsSortMode.None)) if (c.IsPlayer) { player = c; break; }
        if (menu == null) menu = FindFirstObjectByType<JourneyMenuController>();
        if (vehicle == null) vehicle = FindFirstObjectByType<JourneyVehicleDriver>();
        if (drone == null) drone = FindFirstObjectByType<JourneyDronePilot>();
    }
    private bool Allowed => player != null && player.isActiveAndEnabled && !player.IsDead && player.Driver != null &&
        player.Motion.CanJump && player.Player.IsControllable && Time.timeScale > 0f && !JourneyRadialMenu.IsOpen && !JourneyRideSession.IsOccupied &&
        (menu == null || menu.IsGameplayActive) && (vehicle == null || !vehicle.IsDriving) &&
        (drone == null || !drone.IsFlying) && !LightBrushPuzzle.IsMovementBlocked && !JourneyStoneThrowController.IsMovementBlocked && !JourneyVoiceBoard.IsPointerCaptured;
    public void OnPointerDown(PointerEventData e)
    {
        if (pointer != int.MinValue || e.button != PointerEventData.InputButton.Left || !Allowed || !player.Driver.IsGrounded || jumping) return;
        pointer = e.pointerId; began = Time.unscaledTime;
    }
    public void OnPointerUp(PointerEventData e)
    {
        if (e.pointerId != pointer) return;
        float charge = Charge01; pointer = int.MinValue;
        if (!Allowed || !player.Driver.IsGrounded || !player.Jump.CanJump()) return;
        float cap = Mathf.Clamp(maximumHeight, 0.05f, 1.5f);
        height = Mathf.Lerp(Mathf.Clamp(minimumHeight, 0.05f, cap), cap, charge);
        float gravity = Mathf.Abs(player.Motion.GravityUpwards * player.Driver.GravityInfluence);
        if (gravity < 0.01f) return;
        takeoffY = player.transform.position.y; jumpTime = Time.time;
        player.Jump.Do(Mathf.Sqrt(2f * gravity * height));
        jumping = true; leftGround = false;
    }
    public void OnPointerExit(PointerEventData e) { if (pointer == e.pointerId) pointer = int.MinValue; }
    private void Update()
    {
        bool allowed = Allowed;
        if (!allowed) pointer = int.MinValue;
        if (hideWhenUnavailable && visibility != null)
        { visibility.alpha = allowed ? originalAlpha : 0f; visibility.interactable = allowed && originalInteractable; visibility.blocksRaycasts = allowed && originalBlocks; }
    }
    private void LateUpdate()
    {
        if (!jumping || player == null || player.Driver == null) return;
        if (!player.Driver.IsGrounded) leftGround = true;
        if ((leftGround && player.Driver.IsGrounded) || (!leftGround && Time.time - jumpTime > 0.3f)) { jumping = false; return; }
        // Limit jump-generated ascent relative to takeoff, not to terrain below a cliff.
        if (player.Driver.WorldMoveDirection.y > 0f && player.transform.position.y > takeoffY + height)
        {
            Vector3 position = player.transform.position; position.y = takeoffY + height;
            player.Driver.SetPosition(position); player.Driver.ResetVerticalVelocity();
        }
    }
    private void OnApplicationFocus(bool focus) { if (!focus) pointer = int.MinValue; }
    private void OnApplicationPause(bool paused) { if (paused) pointer = int.MinValue; }
    private void OnDisable()
    {
        pointer = int.MinValue;
        if (visibility != null)
        { visibility.alpha = originalAlpha; visibility.interactable = originalInteractable; visibility.blocksRaycasts = originalBlocks; }
    }
}
