using System.Collections.Generic;
using GameCreator.Runtime.Characters;
using GameCreator.Runtime.Cameras;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(1600), DisallowMultipleComponent]
public sealed class JourneyDronePilot : MonoBehaviour
{
    public Character player;
    public JourneyMenuController menu;
    public JourneyVehicleDriver vehicleDriver;
    public Camera viewCamera;
    public TCamera gameCreatorCamera;
    public LightBrushPuzzle coinReceiver;
    public Button launchButton;
    public Sprite launchSprite;
    public Behaviour[] suspendWhileFlying;
    public GameObject[] onFootUI;
    [Min(0.1f)] public float interactionDistance = 4f;
    public bool IsFlying => active;
    public bool Gameplay => focus && !appPaused && Time.timeScale > 0f && menu != null && menu.IsGameplayActive;
    private JourneyDroneLauncher current, nearest;
    private bool active, cameraTaken, focus = true, appPaused;
    private bool characterWasEnabled, cameraWasEnabled;
    private Vector3 savedCameraPosition;
    private Quaternion savedCameraRotation;
    private readonly Dictionary<Behaviour, bool> savedBehaviours = new Dictionary<Behaviour, bool>();
    private readonly Dictionary<GameObject, bool> savedUI = new Dictionary<GameObject, bool>();
    private void OnEnable()
    {
        if (launchButton != null) launchButton.onClick.AddListener(LaunchNearest);
    }
    private void Update()
    {
        if (launchButton != null && launchSprite != null && launchButton.image != null)
            launchButton.image.sprite = launchSprite;
        nearest = null;
        if (active)
        {
            if (current == null || current.Finished || !current.isActiveAndEnabled) Restore();
            else
            {
                current.SetPaused(!Gameplay);
                if (!cameraTaken && Gameplay && current.Elapsed >= current.cameraDelay) TakeCamera();
            }
        }
        if (!active && Gameplay && player != null && (vehicleDriver == null || !vehicleDriver.IsDriving) &&
            !LightBrushPuzzle.IsMovementBlocked && !JourneyPlayerCombat.IsMovementBlocked && !JourneyVoiceBoard.IsPointerCaptured)
        {
            float best = interactionDistance * interactionDistance;
            foreach (var drone in JourneyDroneLauncher.Available)
            {
                if (drone == null || !drone.Ready || drone.Launched) continue;
                float distance = (drone.transform.position - player.transform.position).sqrMagnitude;
                if (distance < best) { best = distance; nearest = drone; }
            }
        }
        if (launchButton != null) launchButton.gameObject.SetActive(!active && nearest != null && Gameplay);
    }
    private void FixedUpdate()
    {
        if (!active || current == null || current.Finished) return;
        current.SetPaused(!Gameplay);
        if (!Gameplay) return;
        current.Tick(Time.fixedDeltaTime, cameraTaken ? JourneyMovementSettings.ReadFlightSteering() : Vector2.zero, cameraTaken);
    }
    public void LaunchNearest()
    {
        if (active || !Gameplay || nearest == null || player == null || viewCamera == null || coinReceiver == null ||
            (vehicleDriver != null && vehicleDriver.IsDriving)) return;
        if (!JourneyMovementSettings.HasSharedJoystick)
        { Debug.LogWarning("Drone: select Player > Input Move > Mobile > Left Stick Or Drag.", this); return; }
        current = nearest;
        if (!current.Launch(this)) { current = null; return; }
        characterWasEnabled = player.enabled;
        cameraWasEnabled = gameCreatorCamera != null && gameCreatorCamera.enabled;
        savedCameraPosition = viewCamera.transform.position; savedCameraRotation = viewCamera.transform.rotation;
        active = true; cameraTaken = false;
        savedBehaviours.Clear(); savedUI.Clear();
        if (suspendWhileFlying != null) foreach (var item in suspendWhileFlying)
            if (!(item is RightScreenDragArea) && item != gameCreatorCamera) Suspend(item);
        Suspend(vehicleDriver);
        if (onFootUI != null) foreach (var item in onFootUI)
            if (!IsCameraDragUI(item)) HideUI(item);
        player.enabled = false;
        if (launchButton != null) launchButton.gameObject.SetActive(false);
    }
    private static bool IsCameraDragUI(GameObject item)
    { return item != null && item.GetComponentInChildren<RightScreenDragArea>(true) != null; }
    private void HideUI(GameObject item)
    {
        if (item == null || savedUI.ContainsKey(item)) return;
        savedUI.Add(item, item.activeSelf); item.SetActive(false);
    }
    private void Suspend(Behaviour item)
    {
        if (item == null || item == this || item == menu || item == player || item is JourneyMovementSettings ||
            item is JourneySkeletonEnemy || savedBehaviours.ContainsKey(item)) return;
        savedBehaviours.Add(item, item.enabled); item.enabled = false;
    }
    private void TakeCamera()
    {
        cameraTaken = true;
        // Return to the view chosen by the user during the launch delay.
        savedCameraPosition = viewCamera.transform.position; savedCameraRotation = viewCamera.transform.rotation;
        if (suspendWhileFlying != null) foreach (var item in suspendWhileFlying)
            if (item is RightScreenDragArea) Suspend(item);
        if (onFootUI != null) foreach (var item in onFootUI)
            if (IsCameraDragUI(item)) HideUI(item);
        RightScreenDragArea.ClearInput();
        if (gameCreatorCamera != null) gameCreatorCamera.enabled = false;
        JourneyMovementSettings.SetDrivingMode(true);
        FollowCamera(true);
    }
    private void LateUpdate()
    {
        if (active && current != null && !current.Finished && cameraTaken && Gameplay) FollowCamera(false);
    }
    private void FollowCamera(bool snap)
    {
        if (current.droneRoot == null || viewCamera == null) return;
        Vector3 target = current.droneRoot.position;
        Vector3 desired = target - current.Forward.normalized * current.cameraDistance + Vector3.up * current.cameraHeight;
        Vector3 ray = desired - target;
        float length = ray.magnitude;
        foreach (var hit in Physics.SphereCastAll(target, 0.12f, ray.normalized, length, current.impactLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(current.transform) || hit.transform.IsChildOf(player.transform) ||
                (current.boosterRoot != null && hit.transform.IsChildOf(current.boosterRoot))) continue;
            length = Mathf.Min(length, Mathf.Max(0.15f, hit.distance - 0.1f));
        }
        desired = target + ray.normalized * length;
        viewCamera.transform.position = snap ? desired : Vector3.Lerp(viewCamera.transform.position, desired, 1f - Mathf.Exp(-12f * Time.deltaTime));
        Vector3 look = target + current.Forward - viewCamera.transform.position;
        if (look.sqrMagnitude > 0.0001f) viewCamera.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
    }
    private void Restore()
    {
        if (!active) return;
        active = false;
        if (current != null) current.SetPaused(false);
        if (cameraTaken) JourneyMovementSettings.SetDrivingMode(false);
        if (player != null) player.enabled = characterWasEnabled;
        foreach (var pair in savedBehaviours) if (pair.Key != null) pair.Key.enabled = pair.Value;
        foreach (var pair in savedUI) if (pair.Key != null) pair.Key.SetActive(pair.Value);
        if (cameraTaken && viewCamera != null) viewCamera.transform.SetPositionAndRotation(savedCameraPosition, savedCameraRotation);
        if (gameCreatorCamera != null && cameraTaken)
        {
            gameCreatorCamera.enabled = cameraWasEnabled;
            if (cameraWasEnabled)
            {
                if (gameCreatorCamera.Transition.CurrentShotCamera != null)
                    gameCreatorCamera.Transition.CurrentShotCamera.OnEnableShot(gameCreatorCamera);
                gameCreatorCamera.Sync();
            }
        }
        cameraTaken = false; current = null;
    }
    private void OnApplicationFocus(bool value)
    { focus = value; if (current != null) current.SetPaused(!Gameplay); }
    private void OnApplicationPause(bool value)
    { appPaused = value; if (current != null) current.SetPaused(!Gameplay); }
    private void OnDisable()
    {
        if (launchButton != null) launchButton.onClick.RemoveListener(LaunchNearest);
        if (current != null) current.Abort();
        Restore();
        if (launchButton != null) launchButton.gameObject.SetActive(false);
    }
}
