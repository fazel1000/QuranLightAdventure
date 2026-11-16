using System;
using UnityEngine;
using UnityEngine.UI;

// Session progress: completed activities stay complete until the scene is reloaded.
[DisallowMultipleComponent, DefaultExecutionOrder(1900)]
public sealed class JourneyMinimapQuestGuide : MonoBehaviour
{
    [Serializable] public sealed class VoiceGoal
    {
        [Tooltip("Drag a world Transform here to position the minimap hint.")]
        public Transform location;
        [Tooltip("The enabled JourneyVoiceBoard that receives assessment results.")]
        public JourneyVoiceBoard board;
    }
    [Serializable] public sealed class CardGoal
    {
        public Transform location;
        public CardGameManager puzzle;
    }
    public JourneyMinimap minimap;
    [Header("Blinking objective icon")]
    public Sprite objectiveSprite;
    public Color objectiveColor = Color.white;
    [Min(2f)] public float iconSize = 24f;
    [Tooltip("Seconds visible, then the same number of seconds hidden.")]
    [Min(0.05f)] public float blinkSeconds = 0.5f;
    public bool keepOnEdge = true;

    [Header("01–05: record and receive a score on each board")]
    public VoiceGoal buttonsBoard = new VoiceGoal();
    public VoiceGoal buttonsBoard1 = new VoiceGoal();
    public VoiceGoal buttonsBoard2 = new VoiceGoal();
    public VoiceGoal buttonsBoard3 = new VoiceGoal();
    public VoiceGoal buttonsBoard4 = new VoiceGoal();
    [Header("06: successful stone hits")]
    public Transform stoneLocation;
    public JourneyStoneTower stoneTarget;
    [Min(1)] public int requiredStoneHits = 3;
    [Header("07: successful drone hits")]
    public Transform droneLocation;
    [Tooltip("Only hits on these targets count. Setup fills this from the active scene.")]
    public JourneyDroneTarget[] droneTargets = Array.Empty<JourneyDroneTarget>();
    [Min(1)] public int requiredDroneHits = 2;
    [Header("08: Hamd crystal installed and ALL verses assessed")]
    public VoiceGoal hamd = new VoiceGoal();
    [Header("09–12: matching cards completed and rewarded")]
    public CardGoal matchingCardPack1 = new CardGoal();
    public CardGoal matchingCardPack2 = new CardGoal();
    public CardGoal matchingCardPack3 = new CardGoal();
    public CardGoal matchingCardPack4 = new CardGoal();
    [Header("13–14: connect the letters")]
    public LightBrushPuzzle letterPuzzles;
    public Transform alHamdolellahLocation;
    public Transform salamAlaykomLocation;
    [Header("15–16: crystal installed and ALL verses assessed")]
    public VoiceGoal falagh = new VoiceGoal();
    public VoiceGoal naas = new VoiceGoal();
    [SerializeField, Tooltip("Runtime only: 0–15, or 16 when finished.")]
    private int currentStep;
    public int CurrentStep => currentStep;
    public bool IsComplete => currentStep >= 16;
    private Image graphic;
    private float blinkStarted;

    private void Awake()
    {
        currentStep = 0;
        blinkStarted = Time.time;
        if (minimap == null) minimap = GetComponent<JourneyMinimap>();
    }
    private static bool Recorded(VoiceGoal goal, bool all = false) => goal != null && goal.board != null &&
        (all ? goal.board.HasAssessedAllVerses : goal.board.HasAssessedVerse(goal.board.VerseNumber));
    private static bool Matched(CardGoal goal) => goal != null && goal.puzzle != null && goal.puzzle.IsComplete;
    private bool DroneGoalComplete()
    {
        long total = 0;
        // Ignore duplicate Inspector assignments so one impact cannot count twice.
        if (droneTargets == null) return false;
        for (int i = 0; i < droneTargets.Length; i++)
        {
            var target = droneTargets[i];
            if (target == null) continue;
            bool duplicate = false;
            for (int j = 0; j < i; j++) if (droneTargets[j] == target) { duplicate = true; break; }
            if (!duplicate) total += target.SuccessfulHits;
        }
        return total >= Mathf.Max(1, requiredDroneHits);
    }
    private bool StepComplete()
    {
        switch (currentStep)
        {
            case 0: return Recorded(buttonsBoard);
            case 1: return Recorded(buttonsBoard1);
            case 2: return Recorded(buttonsBoard2);
            case 3: return Recorded(buttonsBoard3);
            case 4: return Recorded(buttonsBoard4);
            case 5: return stoneTarget != null && stoneTarget.SuccessfulHits >= Mathf.Max(1, requiredStoneHits);
            case 6: return DroneGoalComplete();
            case 7: return Recorded(hamd, true);
            case 8: return Matched(matchingCardPack1);
            case 9: return Matched(matchingCardPack2);
            case 10: return Matched(matchingCardPack3);
            case 11: return Matched(matchingCardPack4);
            case 12: return letterPuzzles != null && letterPuzzles.IsPuzzleSolved(7);
            case 13: return letterPuzzles != null && letterPuzzles.IsPuzzleSolved(6);
            case 14: return Recorded(falagh, true);
            case 15: return Recorded(naas, true);
            default: return false;
        }
    }
    private Transform Location()
    {
        switch (currentStep)
        {
            case 0: return buttonsBoard?.location;
            case 1: return buttonsBoard1?.location;
            case 2: return buttonsBoard2?.location;
            case 3: return buttonsBoard3?.location;
            case 4: return buttonsBoard4?.location;
            case 5: return stoneLocation;
            case 6: return droneLocation;
            case 7: return hamd?.location;
            case 8: return matchingCardPack1?.location;
            case 9: return matchingCardPack2?.location;
            case 10: return matchingCardPack3?.location;
            case 11: return matchingCardPack4?.location;
            case 12: return alHamdolellahLocation;
            case 13: return salamAlaykomLocation;
            case 14: return falagh?.location;
            case 15: return naas?.location;
            default: return null;
        }
    }
    private void LateUpdate()
    {
        // Activities already finished earlier in this scene are not requested again.
        while (!IsComplete && StepComplete()) { currentStep++; blinkStarted = Time.time; }
        Transform target = Location();
        bool visible = target != null && objectiveSprite != null && minimap != null && minimap.isActiveAndEnabled &&
            minimap.offlineMap != null && minimap.mapImage != null && minimap.iconRoot != null &&
            minimap.iconRoot.gameObject.activeInHierarchy && minimap.followTarget != null &&
            (minimap.menu == null || minimap.menu.IsGameplayActive);
        if (!visible) { if (graphic != null) graphic.gameObject.SetActive(false); return; }
        if (graphic == null)
        {
            var go = new GameObject("Current Quest Hint", typeof(RectTransform), typeof(Image));
            graphic = go.GetComponent<Image>(); graphic.raycastTarget = false; graphic.preserveAspect = true;
            graphic.rectTransform.anchorMin = graphic.rectTransform.anchorMax = graphic.rectTransform.pivot = Vector2.one * 0.5f;
        }
        var rect = graphic.rectTransform;
        if (rect.parent != minimap.iconRoot) rect.SetParent(minimap.iconRoot, false);
        rect.SetAsLastSibling();
        float diameter = Mathf.Max(10f, minimap.viewDiameter);
        Vector3 delta = target.position - minimap.followTarget.position;
        Vector2 point = new Vector2(delta.x / diameter * minimap.iconRoot.rect.width, delta.z / diameter * minimap.iconRoot.rect.height);
        float size = Mathf.Max(2f, iconSize);
        float radius = Mathf.Max(1f, Mathf.Min(minimap.iconRoot.rect.width, minimap.iconRoot.rect.height) * 0.5f - size * 0.6f);
        bool on = Mathf.Repeat(Time.time - blinkStarted, Mathf.Max(0.05f, blinkSeconds) * 2f) < Mathf.Max(0.05f, blinkSeconds);
        graphic.gameObject.SetActive(on && (keepOnEdge || point.sqrMagnitude <= radius * radius));
        rect.anchoredPosition = keepOnEdge ? Vector2.ClampMagnitude(point, radius) : point;
        rect.sizeDelta = Vector2.one * size;
        graphic.sprite = objectiveSprite; graphic.color = objectiveColor;
    }
    private void OnDisable() { if (graphic != null) graphic.gameObject.SetActive(false); }
    private void OnDestroy() { if (graphic != null) Destroy(graphic.gameObject); }
}
