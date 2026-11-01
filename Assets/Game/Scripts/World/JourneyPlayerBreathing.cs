using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(50), DisallowMultipleComponent]
public sealed class JourneyPlayerBreathing : MonoBehaviour
{
    [Header("Player services")]
    public JourneyPlayerSwimming swimming;
    public JourneyEquipmentInventory inventory;
    public JourneyPlayerCombat combat;
    public JourneyMenuController menu;
    [Header("Air reserves - seconds")]
    [Min(0.1f)] public float breathSeconds = 30f;
    [Min(0.1f)] public float scubaSeconds = 600f;
    [Header("Drowning uses the player's existing main health")]
    [Min(1)] public int drowningDamagePerTick = 5;
    [Min(0.1f)] public float drowningDamageInterval = 1f;
    [Tooltip("Drag an Empty on dry land here. Only deaths from drowning use this point.")]
    public Transform drowningRespawnPoint;
    [Header("HUD roots - assign the two HealthUI parents, not Canvas")]
    public GameObject breathHealthUI;
    public GameObject scubaGearHealthUI;
    public Image breathFill;
    public Image scubaGearFill;
    [Tooltip("Optional when using actual UI Slider components instead of filled Images.")]
    public Slider breathSlider;
    public Slider scubaGearSlider;
    public float BreathRemaining => reserve != null ? reserve.BreathRemaining : breathSeconds;
    public float ScubaRemaining => reserve != null ? reserve.ScubaRemaining : scubaSeconds;
    private JourneyBreathingReserve reserve;
    private float damageClock;
    private bool focused = true, paused;

    private void ResolveReferences()
    {
        if (swimming == null) swimming = GetComponent<JourneyPlayerSwimming>();
        if (inventory == null) inventory = GetComponent<JourneyEquipmentInventory>();
        if (combat == null) combat = GetComponent<JourneyPlayerCombat>();
        if (menu == null) menu = FindFirstObjectByType<JourneyMenuController>(FindObjectsInactive.Include);
    }
    private void Awake()
    {
        ResolveReferences(); reserve = new JourneyBreathingReserve(breathSeconds, scubaSeconds);
        RefreshHUD(false, false);
    }
    private void OnEnable()
    {
        ResolveReferences();
        if (combat != null) combat.onRespawn.AddListener(ResetAfterRespawn);
    }
    private void Update()
    {
        bool active = focused && !paused && Time.timeScale > 0f && menu != null && menu.IsGameplayActive &&
            swimming != null && swimming.isActiveAndEnabled && combat != null && combat.WorldGameplayActive;
        if (!active || JourneyRideSession.IsOccupied)
        { RefreshHUD(false, false); return; }
        reserve.Configure(breathSeconds, scubaSeconds);
        if (!swimming.IsUnderwaterNow)
        {
            reserve.Refill(); damageClock = 0f; RefreshHUD(false, false); return;
        }
        bool scuba = inventory != null && inventory.HasVisibleScubaTank;
        float withoutAir = reserve.Consume(Time.deltaTime, scuba);
        damageClock = withoutAir > 0f ? damageClock + withoutAir : 0f;
        RefreshHUD(true, scuba);
        float interval = Mathf.Max(0.1f, drowningDamageInterval);
        int ticks = Mathf.FloorToInt(damageClock / interval);
        if (ticks <= 0) return;
        damageClock -= ticks * interval;
        int damage = (int)System.Math.Min(int.MaxValue, (long)ticks * Mathf.Max(1, drowningDamagePerTick));
        combat.TakeDrowningDamage(damage, drowningRespawnPoint);
        // A lethal hit synchronously resets reserves and hides HUD through onRespawn.
    }
    private void RefreshHUD(bool underwater, bool scuba)
    {
        SetVisible(breathHealthUI, underwater && !scuba);
        SetVisible(scubaGearHealthUI, underwater && scuba);
        float breath = reserve != null ? reserve.BreathRemaining / reserve.BreathCapacity : 1f;
        float tank = reserve != null ? reserve.ScubaRemaining / reserve.ScubaCapacity : 1f;
        if (breathFill != null) breathFill.fillAmount = Mathf.Clamp01(breath);
        if (scubaGearFill != null) scubaGearFill.fillAmount = Mathf.Clamp01(tank);
        if (breathSlider != null) breathSlider.SetValueWithoutNotify(Mathf.Lerp(breathSlider.minValue, breathSlider.maxValue, Mathf.Clamp01(breath)));
        if (scubaGearSlider != null) scubaGearSlider.SetValueWithoutNotify(Mathf.Lerp(scubaGearSlider.minValue, scubaGearSlider.maxValue, Mathf.Clamp01(tank)));
    }
    private static void SetVisible(GameObject root, bool visible)
    { if (root != null && root.activeSelf != visible) root.SetActive(visible); }
    private void ResetAfterRespawn()
    {
        reserve?.Refill(); damageClock = 0f; RefreshHUD(false, false);
    }
    private void OnDisable()
    {
        if (combat != null) combat.onRespawn.RemoveListener(ResetAfterRespawn);
        RefreshHUD(false, false);
    }
    private void OnApplicationFocus(bool value) { focused = value; }
    private void OnApplicationPause(bool value) { paused = value; }
}
