using System;
using System.Collections.Generic;
using GameCreator.Runtime.Characters;
using GameCreator.Runtime.Cameras;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(1700), DisallowMultipleComponent]
public sealed class JourneyAirWaterPilot : MonoBehaviour
{
    [Serializable] public sealed class Controls
    {
        public GameObject panel;
        public Button enter, exit, nitro;
        public JourneyVehiclePedal up, down;
    }
    public Character player;
    public JourneyMenuController menu;
    public Camera viewCamera;
    public TCamera gameCreatorCamera;
    public Controls helicopter = new Controls(), submarine = new Controls();
    public Behaviour[] suspendWhileRiding = Array.Empty<Behaviour>();
    public GameObject[] onFootUI = Array.Empty<GameObject>();
    [Min(0.1f)] public float enterDistance = 4f;
    [Min(0.1f)] public float exitMaximumSpeed = 2f;
    [Min(1f)] public float orbitDegreesPerScreen = 180f;
    public LayerMask environmentMask = ~0;
    public bool IsRiding => active;
    public JourneyAirWaterVehicle Current => current;
    private JourneyAirWaterVehicle current, nearestHelicopter, nearestSubmarine;
    private bool active, focused = true, appPaused, characterEnabled, cameraEnabled;
    private Vector3 entryPosition;
    private Quaternion entryRotation;
    private float yaw, pitch;
    private readonly Dictionary<Behaviour, bool> behaviours = new Dictionary<Behaviour, bool>();
    private readonly Dictionary<Renderer, bool> renderers = new Dictionary<Renderer, bool>();
    private readonly Dictionary<Collider, bool> colliders = new Dictionary<Collider, bool>();
    private readonly Dictionary<GameObject, bool> ui = new Dictionary<GameObject, bool>();
    private readonly Collider[] overlaps = new Collider[64];
    private readonly RaycastHit[] hits = new RaycastHit[64];
    private bool Gameplay => focused && !appPaused && Time.timeScale > 0f && menu != null && menu.IsGameplayActive;
    private bool OnFoot => !active && !JourneyRideSession.BusyFor(this) && player != null && player.isActiveAndEnabled &&
        Gameplay && !JourneyRadialMenu.IsOpen && !JourneyStoneThrowController.IsMovementBlocked && !LightBrushPuzzle.IsMovementBlocked && !JourneyPlayerCombat.IsMovementBlocked;
    private void OnEnable() { Bind(true); }
    private void Bind(bool add)
    {
        Listen(helicopter.enter, EnterHelicopter, add); Listen(helicopter.exit, Exit, add);
        Listen(submarine.enter, EnterSubmarine, add); Listen(submarine.exit, Exit, add); Listen(submarine.nitro, Nitro, add);
    }
    private static void Listen(Button button, UnityEngine.Events.UnityAction action, bool add)
    { if (button == null) return; button.onClick.RemoveListener(action); if (add) button.onClick.AddListener(action); }
    private void Update()
    {
        if (active && (current == null || !current.isActiveAndEnabled)) Leave(entryPosition, entryRotation);
        nearestHelicopter = nearestSubmarine = null;
        if (OnFoot)
        {
            float helicopterDistance = enterDistance * enterDistance, submarineDistance = helicopterDistance;
            foreach (var vehicle in JourneyAirWaterVehicle.Available)
            {
                if (vehicle == null || !vehicle.Ready || vehicle.IsOccupied || vehicle.Speed > exitMaximumSpeed) continue;
                float distance = (vehicle.exitPoint.position - player.transform.position).sqrMagnitude;
                if (vehicle.kind == JourneyAirWaterVehicle.VehicleKind.Helicopter && distance < helicopterDistance)
                { helicopterDistance = distance; nearestHelicopter = vehicle; }
                if (vehicle.kind == JourneyAirWaterVehicle.VehicleKind.Submarine && distance < submarineDistance)
                { submarineDistance = distance; nearestSubmarine = vehicle; }
            }
            // A single nearby entry prompt, even if two vehicles are parked together.
            if (nearestHelicopter != null && nearestSubmarine != null)
            { if (helicopterDistance <= submarineDistance) nearestSubmarine = null; else nearestHelicopter = null; }
        }
        Show(helicopter, JourneyAirWaterVehicle.VehicleKind.Helicopter, nearestHelicopter);
        Show(submarine, JourneyAirWaterVehicle.VehicleKind.Submarine, nearestSubmarine);
        if (!active || current == null) return;
        current.Freeze(!Gameplay);
        if (!Gameplay) { ResetInput(); return; }
        Vector2 stick = JourneyMovementSettings.ReadFlightSteering();
        Vector3 forward = Vector3.ProjectOnPlane(viewCamera != null ? viewCamera.transform.forward : current.Forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = current.Forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Controls controls = current.kind == JourneyAirWaterVehicle.VehicleKind.Helicopter ? helicopter : submarine;
        float vertical = (controls.up != null && controls.up.Held ? 1f : 0f) - (controls.down != null && controls.down.Held ? 1f : 0f);
        current.SetInput(forward * stick.y + right * stick.x, vertical);
    }
    private void Show(Controls controls, JourneyAirWaterVehicle.VehicleKind kind, JourneyAirWaterVehicle nearest)
    {
        bool driving = active && current != null && current.kind == kind && Gameplay;
        bool entering = OnFoot && nearest != null;
        if (controls.panel != null) controls.panel.SetActive(driving || entering);
        Active(controls.enter, entering); Active(controls.exit, driving); Active(controls.up, driving); Active(controls.down, driving); Active(controls.nitro, driving);
        if (controls.exit != null) controls.exit.interactable = driving && current.Speed <= exitMaximumSpeed;
        if (controls.nitro != null) controls.nitro.interactable = driving && current.CanBoost;
    }
    private static void Active(Component item, bool value) { if (item != null && item.gameObject.activeSelf != value) item.gameObject.SetActive(value); }
    private void EnterHelicopter() { Enter(nearestHelicopter); }
    private void EnterSubmarine() { Enter(nearestSubmarine); }
    private void Nitro() { if (active && current != null && Gameplay) current.RequestBoost(); }
    private void Enter(JourneyAirWaterVehicle vehicle)
    {
        if (!OnFoot || vehicle == null || !vehicle.Ready || viewCamera == null) return;
        if (!JourneyMovementSettings.HasSharedJoystick)
        { Debug.LogWarning("Air/water vehicle: set Player > Input Move to Mobile / Left Stick Or Drag.", this); return; }
        if (!JourneyRideSession.TryAcquire(this)) return;
        current = vehicle; active = true;
        entryPosition = player.transform.position; entryRotation = player.transform.rotation;
        characterEnabled = player.enabled; cameraEnabled = gameCreatorCamera != null && gameCreatorCamera.enabled;
        behaviours.Clear(); colliders.Clear(); renderers.Clear(); ui.Clear();
        foreach (var item in suspendWhileRiding) Suspend(item);
        Suspend(player.Animim.Animator);
        foreach (var item in player.GetComponentsInChildren<Renderer>(true)) { renderers[item] = item.enabled; item.enabled = false; }
        foreach (var item in player.GetComponentsInChildren<Collider>(true)) { colliders[item] = item.enabled; item.enabled = false; }
        foreach (var item in onFootUI)
        {
            if (item == null || item.GetComponentInChildren<RightScreenDragArea>(true) != null || ui.ContainsKey(item)) continue;
            ui[item] = item.activeSelf; item.SetActive(false);
        }
        player.enabled = false;
        if (gameCreatorCamera != null) gameCreatorCamera.enabled = false;
        current.SetOccupied(true); current.Freeze(false);
        JourneyMovementSettings.SetDrivingMode(true); RightScreenDragArea.ClearInput(); ResetInput();
        yaw = Quaternion.LookRotation(current.Forward, Vector3.up).eulerAngles.y;
        pitch = Mathf.Atan2(current.cameraHeight, current.cameraDistance) * Mathf.Rad2Deg;
        FollowCamera(true);
    }
    private void Suspend(Behaviour item)
    {
        if (item == null || item == this || item == player || item == menu || item == gameCreatorCamera || item is RightScreenDragArea ||
            item is JourneyMovementSettings || item is JourneySkeletonEnemy || item is JourneyRiderGuard || behaviours.ContainsKey(item)) return;
        behaviours[item] = item.enabled; item.enabled = false;
    }
    public void Exit()
    {
        if (!active || current == null || !Gameplay || current.Speed > exitMaximumSpeed) return;
        if (!TryExit(out var position)) { Debug.LogWarning("ExitPoint is blocked. Move it outside the vehicle with enough space for the player.", current); return; }
        Leave(position, Quaternion.Euler(0f, Quaternion.LookRotation(current.Forward).eulerAngles.y, 0f));
    }
    private bool TryExit(out Vector3 position)
    {
        float height = Mathf.Max(0.6f, player.Motion.Height), radius = Mathf.Min(0.3f, height * 0.4f);
        position = current.exitPoint.position + Vector3.up * (height * 0.5f + 0.05f);
        // Snap to nearby solid ground; underwater/in-air exits retain the authored ExitPoint.
        int count = Physics.RaycastNonAlloc(current.exitPoint.position + Vector3.up, Vector3.down, hits, 3f, environmentMask, QueryTriggerInteraction.Ignore);
        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].transform.IsChildOf(current.transform) || hits[i].transform.IsChildOf(player.transform) || hits[i].normal.y < 0.65f) continue;
            if (hits[i].distance < best) { best = hits[i].distance; position = hits[i].point + Vector3.up * (height * 0.5f + 0.05f); }
        }
        count = Physics.OverlapCapsuleNonAlloc(position - Vector3.up * (height * 0.5f - radius), position + Vector3.up * (height * 0.5f - radius), radius, overlaps, environmentMask, QueryTriggerInteraction.Ignore);
        if (count == overlaps.Length) return false;
        for (int i = 0; i < count; i++) if (!overlaps[i].transform.IsChildOf(player.transform)) return false;
        return true;
    }
    private void Leave(Vector3 position, Quaternion rotation)
    {
        if (!active) return;
        ResetInput();
        if (current != null) { current.Freeze(false); current.SetOccupied(false); }
        current = null; active = false; JourneyRideSession.Release(this);
        JourneyMovementSettings.SetDrivingMode(false); RightScreenDragArea.ClearInput();
        foreach (var item in behaviours) if (item.Key != null) item.Key.enabled = item.Value;
        if (player != null)
        { player.Driver.SetPosition(position); player.Driver.SetRotation(rotation); player.enabled = characterEnabled; }
        foreach (var item in colliders) if (item.Key != null) item.Key.enabled = item.Value;
        foreach (var item in renderers) if (item.Key != null) item.Key.enabled = item.Value;
        foreach (var item in ui) if (item.Key != null) item.Key.SetActive(item.Value);
        if (gameCreatorCamera != null)
        {
            gameCreatorCamera.enabled = cameraEnabled;
            if (cameraEnabled)
            {
                if (gameCreatorCamera.Transition.CurrentShotCamera != null) gameCreatorCamera.Transition.CurrentShotCamera.OnEnableShot(gameCreatorCamera);
                gameCreatorCamera.Sync();
            }
        }
    }
    private void LateUpdate()
    {
        if (!active || current == null || player == null) return;
        player.transform.SetPositionAndRotation(current.driverSeat.position, current.driverSeat.rotation);
        if (Gameplay) FollowCamera(false);
    }
    private void FollowCamera(bool snap)
    {
        if (viewCamera == null || current == null) return;
        Vector2 delta = RightScreenDragArea.ConsumeDelta();
        yaw += delta.x * orbitDegreesPerScreen / Mathf.Max(1, Screen.width);
        pitch = Mathf.Clamp(pitch - delta.y * orbitDegreesPerScreen / Mathf.Max(1, Screen.height), -15f, 80f);
        Vector3 target = current.cameraTarget != null ? current.cameraTarget.position : current.driverSeat.position;
        float distance = Mathf.Sqrt(current.cameraDistance * current.cameraDistance + current.cameraHeight * current.cameraHeight);
        Vector3 direction = Quaternion.Euler(pitch, yaw, 0f) * Vector3.back;
        int count = Physics.SphereCastNonAlloc(target, 0.25f, direction, hits, distance, environmentMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) if (hits[i].collider is TerrainCollider) distance = Mathf.Min(distance, Mathf.Max(0.5f, hits[i].distance - 0.15f));
        Vector3 desired = target + direction * distance;
        viewCamera.transform.position = snap ? desired : Vector3.Lerp(viewCamera.transform.position, desired, 1f - Mathf.Exp(-10f * Time.deltaTime));
        Vector3 look = target - viewCamera.transform.position;
        if (look.sqrMagnitude > 0.001f) viewCamera.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
    }
    private void ResetInput()
    {
        if (helicopter.up != null) helicopter.up.ResetInput(); if (helicopter.down != null) helicopter.down.ResetInput();
        if (submarine.up != null) submarine.up.ResetInput(); if (submarine.down != null) submarine.down.ResetInput();
        if (current != null) current.SetInput(Vector3.zero, 0f);
    }
    private void OnApplicationFocus(bool value) { focused = value; if (!value) { ResetInput(); RightScreenDragArea.ClearInput(); } }
    private void OnApplicationPause(bool value) { appPaused = value; if (value) { ResetInput(); RightScreenDragArea.ClearInput(); } }
    private void OnDisable()
    {
        Bind(false); if (active) Leave(entryPosition, entryRotation);
        if (helicopter.panel != null) helicopter.panel.SetActive(false); if (submarine.panel != null) submarine.panel.SetActive(false);
        JourneyRideSession.Release(this);
    }
}
