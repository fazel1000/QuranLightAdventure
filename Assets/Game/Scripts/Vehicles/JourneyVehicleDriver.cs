using System.Collections.Generic;
using GameCreator.Runtime.Characters;
using GameCreator.Runtime.Cameras;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(1500), DisallowMultipleComponent]
public sealed class JourneyVehicleDriver : MonoBehaviour
{
    public Character player;
    public JourneyMenuController menu;
    public Camera viewCamera;
    public TCamera gameCreatorCamera;
    public GameObject drivingPanel;
    public Button enterButton, exitButton;
    public JourneyVehiclePedal gas, brake, nitro;
    public GameObject[] onFootUI;
    public Behaviour[] suspendWhileDriving;
    [Min(1f)] public float enterDistance = 3f;
    [Min(0.1f)] public float exitMaximumSpeed = 2f;
    public LayerMask environmentMask = ~0;
    [Min(0.1f)] public float exitCapsuleRadius = 0.3f;
    [Min(1f)] public float exitCapsuleHeight = 1.8f;
    public bool IsDriving => current != null;
    private JourneyVehicle current, nearest;
    private bool characterEnabled, cameraEnabled, focus = true, paused;
    private Vector3 entryPosition;
    private Quaternion entryRotation;
    private readonly Dictionary<Behaviour, bool> behaviours = new Dictionary<Behaviour, bool>();
    private readonly Dictionary<Renderer, bool> renderers = new Dictionary<Renderer, bool>();
    private readonly Dictionary<Collider, bool> colliders = new Dictionary<Collider, bool>();
    private readonly Dictionary<GameObject, bool> ui = new Dictionary<GameObject, bool>();
    private readonly RaycastHit[] hits = new RaycastHit[64];
    private readonly Collider[] overlaps = new Collider[64];
    private bool activeSession;
    private bool Gameplay => focus && !paused && Time.timeScale > 0f && menu != null && menu.IsGameplayActive;
    private void OnEnable()
    {
        if (enterButton != null) enterButton.onClick.AddListener(Enter);
        if (exitButton != null) exitButton.onClick.AddListener(Exit);
    }
    private void Update()
    {
        if (activeSession && (current == null || !current.isActiveAndEnabled)) Leave(entryPosition, entryRotation);
        nearest = null;
        if (!activeSession && Gameplay && player != null)
        {
            float distance = enterDistance * enterDistance;
            foreach (JourneyVehicle car in JourneyVehicle.Available)
            {
                if (car == null || !car.Ready || car.IsOccupied || car.SpeedKph > exitMaximumSpeed) continue;
                float next = (car.driverSeat.position - player.transform.position).sqrMagnitude;
                if (next < distance) { distance = next; nearest = car; }
            }
        }
        if (enterButton != null) enterButton.gameObject.SetActive(!activeSession && Gameplay && nearest != null);
        if (drivingPanel != null) drivingPanel.SetActive(activeSession && Gameplay);
        if (!activeSession || current == null) return;
        current.Freeze(!Gameplay);
        if (!Gameplay) { ResetInput(); return; }
        if (exitButton != null) exitButton.interactable = current.SpeedKph <= exitMaximumSpeed;
        current.SetInput(JourneyMovementSettings.ReadDrivingSteering(), gas != null && gas.Held ? 1f : 0f,
            brake != null && brake.Held ? 1f : 0f, false);
        if (nitro != null && nitro.ConsumePress()) current.RequestNitro();
    }
    public void Enter()
    {
        if (activeSession || !Gameplay || nearest == null || player == null) return;
        if (!JourneyMovementSettings.HasSharedJoystick)
        { Debug.LogWarning("Select Player > Input Move > Mobile > Left Stick Or Drag before driving.", this); return; }
        current = nearest;
        entryPosition = player.transform.position;
        entryRotation = player.transform.rotation;
        characterEnabled = player.enabled;
        cameraEnabled = gameCreatorCamera != null && gameCreatorCamera.enabled;
        behaviours.Clear(); renderers.Clear(); colliders.Clear(); ui.Clear();
        if (suspendWhileDriving != null) foreach (Behaviour item in suspendWhileDriving)
        {
            if (item == null || item == this || item == player || item == menu || item is JourneyMovementSettings) continue;
            if (!behaviours.ContainsKey(item)) { behaviours.Add(item, item.enabled); item.enabled = false; }
        }
        foreach (Renderer item in player.GetComponentsInChildren<Renderer>(true))
        { renderers.Add(item, item.enabled); item.enabled = false; }
        foreach (Collider item in player.GetComponentsInChildren<Collider>(true))
        { colliders.Add(item, item.enabled); item.enabled = false; }
        if (onFootUI != null) foreach (GameObject item in onFootUI)
            if (item != null && !ui.ContainsKey(item)) { ui.Add(item, item.activeSelf); item.SetActive(false); }
        player.enabled = false;
        if (gameCreatorCamera != null) gameCreatorCamera.enabled = false;
        activeSession = true;
        current.SetOccupied(true);
        ResetInput();
        JourneyMovementSettings.SetDrivingMode(true);
        FollowCamera(true);
    }
    public void Exit()
    {
        if (!activeSession || current == null || !Gameplay || current.SpeedKph > exitMaximumSpeed) return;
        if (!TryExit(out Vector3 position))
        { Debug.LogWarning("Exit blocked: stop where ExitPoint is above clear ground.", current); return; }
        Leave(position, Quaternion.Euler(0f, current.transform.eulerAngles.y, 0f));
    }
    private bool TryExit(out Vector3 position)
    {
        position = current.exitPoint.position;
        int count = Physics.RaycastNonAlloc(position + Vector3.up * 2f, Vector3.down, hits, 5f, environmentMask, QueryTriggerInteraction.Ignore);
        if (count == hits.Length) return false;
        float distance = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            Transform t = hits[i].collider.transform;
            if (t.IsChildOf(current.transform) || t.IsChildOf(player.transform)) continue;
            if (hits[i].distance >= distance) continue;
            distance = hits[i].distance; position = hits[i].point; found = hits[i].normal.y >= 0.65f;
        }
        if (!found) return false;
        position += Vector3.up * 0.06f;
        float radius = exitCapsuleRadius;
        float height = Mathf.Max(exitCapsuleHeight, radius * 2f);
        count = Physics.OverlapCapsuleNonAlloc(position + Vector3.up * radius,
            position + Vector3.up * (height - radius), radius, overlaps, environmentMask, QueryTriggerInteraction.Ignore);
        if (count == overlaps.Length) return false;
        for (int i = 0; i < count; i++) if (!overlaps[i].transform.IsChildOf(player.transform)) return false;
        return true;
    }
    private void Leave(Vector3 position, Quaternion rotation)
    {
        if (!activeSession) return;
        ResetInput();
        if (current != null) { current.Freeze(false); current.SetOccupied(false); }
        JourneyMovementSettings.SetDrivingMode(false);
        if (player != null)
        {
            player.Driver.SetPosition(position);
            player.Driver.SetRotation(rotation);
            player.enabled = characterEnabled;
        }
        foreach (var pair in colliders) if (pair.Key != null) pair.Key.enabled = pair.Value;
        foreach (var pair in renderers) if (pair.Key != null) pair.Key.enabled = pair.Value;
        foreach (var pair in behaviours) if (pair.Key != null) pair.Key.enabled = pair.Value;
        foreach (var pair in ui) if (pair.Key != null) pair.Key.SetActive(pair.Value);
        if (gameCreatorCamera != null)
        {
            gameCreatorCamera.enabled = cameraEnabled;
            if (cameraEnabled)
            {
                if (gameCreatorCamera.Transition.CurrentShotCamera != null)
                    gameCreatorCamera.Transition.CurrentShotCamera.OnEnableShot(gameCreatorCamera);
                gameCreatorCamera.Sync();
            }
        }
        current = null; activeSession = false;
        if (drivingPanel != null) drivingPanel.SetActive(false);
    }
    private void LateUpdate()
    {
        if (!activeSession || current == null || player == null) return;
        player.transform.SetPositionAndRotation(current.driverSeat.position, current.driverSeat.rotation);
        if (Gameplay) FollowCamera(false);
    }
    private void FollowCamera(bool snap)
    {
        if (viewCamera == null || current == null) return;
        Vector3 target = current.cameraTarget.position;
        Vector3 forward = Vector3.ProjectOnPlane(current.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
        Vector3 desired = target - forward * current.cameraDistance + Vector3.up * current.cameraHeight;
        Vector3 direction = desired - target;
        float length = direction.magnitude;
        int count = Physics.SphereCastNonAlloc(target, 0.2f, direction.normalized, hits, length, environmentMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (!hits[i].transform.IsChildOf(current.transform) && !hits[i].transform.IsChildOf(player.transform))
                length = Mathf.Min(length, Mathf.Max(0.3f, hits[i].distance - 0.1f));
        desired = target + direction.normalized * length;
        viewCamera.transform.position = snap ? desired : Vector3.Lerp(viewCamera.transform.position, desired, 1f - Mathf.Exp(-8f * Time.deltaTime));
        Vector3 look = target - viewCamera.transform.position;
        if (look.sqrMagnitude > 0.001f) viewCamera.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
    }
    private void ResetInput()
    {
        if (gas != null) gas.ResetInput(); if (brake != null) brake.ResetInput(); if (nitro != null) nitro.ResetInput();
        if (current != null) current.SetInput(0f, 0f, 1f, false);
    }
    private void OnApplicationFocus(bool value) { focus = value; if (!value) ResetInput(); }
    private void OnApplicationPause(bool value) { paused = value; if (value) ResetInput(); }
    private void OnDisable()
    {
        if (enterButton != null) enterButton.onClick.RemoveListener(Enter);
        if (exitButton != null) exitButton.onClick.RemoveListener(Exit);
        if (activeSession) Leave(entryPosition, entryRotation);
        if (enterButton != null) enterButton.gameObject.SetActive(false);
    }
}
