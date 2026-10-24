using System.Collections;
using GameCreator.Runtime.Cameras;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent, DefaultExecutionOrder(1250)]
public sealed class JourneyCameraPresets : MonoBehaviour
{
    public enum Preset { Near, Average, Away }
    public ShotCamera thirdPersonShot;
    public Button nearButton, averageButton, awayButton;
    [Header("Average: captured from the existing camera by Setup")]
    public float averageShoulder = 0.5f, averageLift = 0.5f;
    [Min(0.6f)] public float averageBaseRadius = 5f;
    [Range(0.05f, 1f)] public float averageZoom = 0.57f;
    [Min(0f)] public float minimumZoomDistance = 0.5f;
    [Header("Near: over the right shoulder")]
    [Min(0.6f)] public float nearDistance = 1.7f;
    public float nearShoulder = 0.65f, nearLift = 0.65f;
    [Header("Away")]
    [Min(1.05f)] public float awayDistanceMultiplier = 1.6f;
    [Min(0f)] public float transitionSeconds = 0.35f;
    public Color selectedColor = new Color(0.95f, 0.72f, 0.2f, 1f);
    public Color normalColor = Color.white;
    public Preset Selected { get; private set; } = Preset.Average;
    private const string PreferenceKey = "JourneyOfLight.CameraPreset";
    private ShotSystemThirdPerson system;
    private ShotSystemZoom zoom;
    private bool ready, wasRiding;
    private Vector3 currentOffsets, targetOffsets, startOffsets;
    private float transitionAge;
    private bool transitioning;
    private void Awake() { Selected = (Preset)Mathf.Clamp(PlayerPrefs.GetInt(PreferenceKey, 1), 0, 2); }
    private void OnEnable()
    {
        if (nearButton != null) nearButton.onClick.AddListener(SelectNear);
        if (averageButton != null) averageButton.onClick.AddListener(SelectAverage);
        if (awayButton != null) awayButton.onClick.AddListener(SelectAway);
        RefreshButtons();
    }
    private IEnumerator Start()
    {
        yield return null;
        if (thirdPersonShot == null || !(thirdPersonShot.ShotType is ShotTypeThirdPerson))
        { Debug.LogWarning("Camera presets: run Setup Equipment Wardrobe and Camera to connect the existing third-person Shot Camera.", this); yield break; }
        system = thirdPersonShot.ShotType.GetSystem(ShotSystemThirdPerson.ID) as ShotSystemThirdPerson;
        zoom = thirdPersonShot.ShotType.GetSystem(ShotSystemZoom.ID) as ShotSystemZoom;
        ready = system != null && zoom != null;
        Apply(false);
    }
    public void SelectNear() { Select(Preset.Near); }
    public void SelectAverage() { Select(Preset.Average); }
    public void SelectAway() { Select(Preset.Away); }
    private void Select(Preset value)
    {
        Selected = value; PlayerPrefs.SetInt(PreferenceKey, (int)value); PlayerPrefs.Save();
        Apply(true); RefreshButtons();
    }
    private void Update()
    {
        bool riding = JourneyRideSession.IsOccupied;
        if (wasRiding && !riding) Apply(false);
        wasRiding = riding;
        if (ready && transitioning && !riding)
        {
            transitionAge += Time.unscaledDeltaTime;
            float amount = Mathf.Clamp01(transitionAge / Mathf.Max(0.01f, transitionSeconds));
            currentOffsets = Vector3.Lerp(startOffsets, targetOffsets, Mathf.SmoothStep(0f, 1f, amount));
            system.Aim(currentOffsets.x, currentOffsets.y, currentOffsets.z, 0f);
            if (amount >= 1f) transitioning = false;
        }
    }
    private void Apply(bool smooth)
    {
        if (!ready || JourneyRideSession.IsOccupied) return;
        zoom.Level = Mathf.Clamp(averageZoom, 0.05f, 1f);
        float averageDistance = minimumZoomDistance + (averageBaseRadius - minimumZoomDistance) * zoom.Level;
        float distance = Selected == Preset.Near ? nearDistance : Selected == Preset.Away ? averageDistance * awayDistanceMultiplier : averageDistance;
        float radius = minimumZoomDistance + (Mathf.Max(minimumZoomDistance + 0.1f, distance) - minimumZoomDistance) / zoom.Level;
        // Aim takes offsets from the authored shot values; Average restores all offsets to zero.
        targetOffsets = new Vector3(Selected == Preset.Near ? nearShoulder - averageShoulder : 0f,
            Selected == Preset.Near ? nearLift - averageLift : 0f, radius - averageBaseRadius);
        if (smooth && transitionSeconds > 0f)
        { startOffsets = currentOffsets; transitionAge = 0f; transitioning = true; }
        else
        { currentOffsets = targetOffsets; transitioning = false; system.Aim(currentOffsets.x, currentOffsets.y, currentOffsets.z, 0f); }
    }
    private void RefreshButtons()
    {
        Button[] buttons = { nearButton, averageButton, awayButton };
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            ColorBlock colors = buttons[i].colors; colors.normalColor = i == (int)Selected ? selectedColor : normalColor;
            colors.selectedColor = colors.normalColor; buttons[i].colors = colors;
            if (buttons[i].targetGraphic != null) buttons[i].targetGraphic.color = colors.normalColor;
        }
    }
    private void OnDisable()
    {
        if (nearButton != null) nearButton.onClick.RemoveListener(SelectNear);
        if (averageButton != null) averageButton.onClick.RemoveListener(SelectAverage);
        if (awayButton != null) awayButton.onClick.RemoveListener(SelectAway);
    }
}
