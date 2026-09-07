
using UnityEngine;

// Attach to the always-active MenuManager, alongside JourneyMenuController.
// Assign the ScrollRect and the LevelButton prefab asset from the Project window.
// The prefab needs direct children: LevelTitle (RTLTMP), LockIcon (Image), StarsDisplay (Image).
[DisallowMultipleComponent]
[DefaultExecutionOrder(1100)]
public sealed class JourneyLevelsController : MonoBehaviour
{
    [global::System.Serializable]
    public sealed class LevelSelectedEvent : global::UnityEngine.Events.UnityEvent<int> { }

    [Header("References")]
    [SerializeField] private global::UnityEngine.UI.ScrollRect levelsScrollView;
    [SerializeField] private GameObject levelButtonPrefab;

    [Header("Stars - drag the six complete star-row sprites")]
    [SerializeField] private Sprite zeroStars;
    [SerializeField] private Sprite oneStar;
    [SerializeField] private Sprite twoStars;
    [SerializeField] private Sprite threeStars;
    [SerializeField] private Sprite fourStars;
    [SerializeField] private Sprite fiveStars;

    [Header("Levels")]
    [SerializeField, Range(1, 10)] private int levelCount = 10;
    [SerializeField, Range(1, 5)] private int starsRequiredToUnlockNext = 3;
    [SerializeField] private string progressKey = "JourneyOfLight.Chapter1.v1";

    [Header("Horizontal Row - size comes from the prefab")]
    [SerializeField, Min(0f)] private float spacing = 60f;
    [SerializeField, Min(0)] private int padding = 30;
    [SerializeField] private bool rightToLeft = true;

    [Header("Island Selection - connect in the next step")]
    [SerializeField] private LevelSelectedEvent onLevelSelected = new LevelSelectedEvent();

    public int HighestUnlockedLevel { get; private set; } = 1;
    public int SelectedLevelNumber { get; private set; }
    public int LevelCount => levelCount;

    private static readonly string[] LevelTitles =
    {
        "مرحله یک", "مرحله دو", "مرحله سه", "مرحله چهار", "مرحله پنج",
        "مرحله شش", "مرحله هفت", "مرحله هشت", "مرحله نه", "مرحله ده"
    };

    private sealed class LevelView
    {
        public global::UnityEngine.UI.Button Button;
        public RectTransform Rect;
        public Vector2 OriginalSize;
        public Vector3 OriginalScale;
        public GameObject LockIcon;
        public global::UnityEngine.UI.Image StarsImage;
        public global::UnityEngine.Events.UnityAction ClickAction;
    }

    private RectTransform content;
    private RectTransform viewport;
    private Behaviour[] contentLayoutComponents;
    private bool[] contentLayoutEnabledStates;
    private LevelView[] views;
    private GameObject[] sceneTemplates;
    private bool[] sceneTemplateActiveStates;
    private int[] bestStars;
    private bool initialized;
    private JourneyUIFeedback feedback;
    private bool wasVisible;
    private Vector2 lastViewportSize = new Vector2(-1f, -1f);
    private Vector4 lastLayoutSettings;

    private void Start()
    {
        levelCount = Mathf.Clamp(levelCount, 1, LevelTitles.Length);
        starsRequiredToUnlockNext = Mathf.Clamp(starsRequiredToUnlockNext, 1, 5);
        spacing = Mathf.Max(0f, spacing);
        padding = Mathf.Max(0, padding);

        if (!ValidateSetup())
        {
            enabled = false;
            return;
        }

        feedback = GetComponent<JourneyUIFeedback>();
        if (feedback == null) feedback = gameObject.AddComponent<JourneyUIFeedback>();

        LoadProgress();
        HideSceneTemplates();
        ConfigureScrollContent();
        CreateLevelButtons();
        initialized = true;
        RefreshButtons();
    }

    private bool ValidateSetup()
    {
        if (levelsScrollView == null || levelButtonPrefab == null)
            return SetupError("Assign Levels Scroll View and Level Button Prefab.");

        content = levelsScrollView.content;
        if (content == null || !(content.parent is RectTransform))
            return SetupError("The ScrollRect needs a Content object inside its Viewport.");

        viewport = levelsScrollView.viewport != null
            ? levelsScrollView.viewport
            : content.parent as RectTransform;

        if (levelButtonPrefab.scene.IsValid())
            return SetupError("Drag the LevelButton prefab asset from Project, not a Hierarchy instance.");

        if (levelButtonPrefab.GetComponent<global::UnityEngine.UI.Button>() == null ||
            levelButtonPrefab.GetComponent<global::UnityEngine.UI.Image>() == null)
            return SetupError("The prefab root needs both Button and Image components.");

        Transform title = levelButtonPrefab.transform.Find("LevelTitle");
        if (title == null || title.GetComponent<global::RTLTMPro.RTLTextMeshPro>() == null)
            return SetupError("The prefab needs a direct child LevelTitle with RTLTextMeshPro.");

        Transform lockIcon = levelButtonPrefab.transform.Find("LockIcon");
        if (lockIcon == null || lockIcon.GetComponent<global::UnityEngine.UI.Image>() == null)
            return SetupError("The prefab needs a direct child LockIcon with an Image component.");

        Transform stars = levelButtonPrefab.transform.Find("StarsDisplay");
        if (stars == null || stars.GetComponent<global::UnityEngine.UI.Image>() == null)
            return SetupError("The prefab needs a direct child StarsDisplay with an Image component.");

        if (zeroStars == null || oneStar == null || twoStars == null ||
            threeStars == null || fourStars == null || fiveStars == null)
            return SetupError("Assign all six Stars sprites, from Zero Stars to Five Stars.");

        if (string.IsNullOrWhiteSpace(progressKey))
            return SetupError("Progress Key cannot be empty.");

        return true;
    }

    private bool SetupError(string message)
    {
        Debug.LogError("JourneyLevelsController: " + message, this);
        return false;
    }

    private void HideSceneTemplates()
    {
        // A hand-placed template is useful when editing the UI. Exclude it from
        // the runtime row so the generated list does not contain an extra card.
        // Remember only matching level cards; leave other Content children alone.
        int childCount = content.childCount;
        sceneTemplates = new GameObject[childCount];
        sceneTemplateActiveStates = new bool[childCount];

        for (int index = 0; index < childCount; index++)
        {
            GameObject child = content.GetChild(index).gameObject;
            if (child.GetComponent<global::UnityEngine.UI.Button>() == null) continue;
            if (transform.IsChildOf(child.transform)) continue;

            Transform title = child.transform.Find("LevelTitle");
            Transform lockIcon = child.transform.Find("LockIcon");
            if (title == null || lockIcon == null) continue;
            if (title.GetComponent<global::RTLTMPro.RTLTextMeshPro>() == null ||
                lockIcon.GetComponent<global::UnityEngine.UI.Image>() == null) continue;

            sceneTemplates[index] = child;
            sceneTemplateActiveStates[index] = child.activeSelf;
            child.SetActive(false);
        }
    }

    private void RestoreSceneTemplates()
    {
        if (sceneTemplates == null) return;

        for (int index = 0; index < sceneTemplates.Length; index++)
        {
            if (sceneTemplates[index] != null)
                sceneTemplates[index].SetActive(sceneTemplateActiveStates[index]);
        }

        sceneTemplates = null;
        sceneTemplateActiveStates = null;
    }

    private void ConfigureScrollContent()
    {
        SuspendContentLayout();

        // Only configure Content; preserve the user's Scroll View anchors/top offset.
        // Stretch vertically to the Viewport; width grows to fit the entire row.
        content.anchorMin = new Vector2(0f, 0f);
        content.anchorMax = new Vector2(0f, 1f);
        content.pivot = new Vector2(0f, 0.5f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        content.localScale = Vector3.one;

        levelsScrollView.horizontal = true;
        levelsScrollView.vertical = false;
        if (levelsScrollView.horizontalScrollbar != null)
            levelsScrollView.horizontalScrollbar.gameObject.SetActive(true);
        if (levelsScrollView.verticalScrollbar != null)
            levelsScrollView.verticalScrollbar.gameObject.SetActive(false);

        levelsScrollView.StopMovement();
    }

    private void SuspendContentLayout()
    {
        // Layout groups can prohibit another group on the same object even when
        // disabled. This controller positions its cards directly, so no new
        // layout component needs to be added or an existing component removed.
        Behaviour[] components = content.GetComponents<Behaviour>();
        contentLayoutComponents = new Behaviour[components.Length];
        contentLayoutEnabledStates = new bool[components.Length];

        for (int index = 0; index < components.Length; index++)
        {
            Behaviour component = components[index];
            if (!(component is global::UnityEngine.UI.LayoutGroup) &&
                !(component is global::UnityEngine.UI.ContentSizeFitter) &&
                !(component is global::UnityEngine.UI.AspectRatioFitter)) continue;

            contentLayoutComponents[index] = component;
            contentLayoutEnabledStates[index] = component.enabled;
            component.enabled = false;
        }
    }

    private void RestoreContentLayout()
    {
        if (contentLayoutComponents == null) return;

        for (int index = 0; index < contentLayoutComponents.Length; index++)
        {
            if (contentLayoutComponents[index] != null)
                contentLayoutComponents[index].enabled = contentLayoutEnabledStates[index];
        }

        contentLayoutComponents = null;
        contentLayoutEnabledStates = null;
    }

    private void CreateLevelButtons()
    {
        views = new LevelView[levelCount];

        for (int index = 0; index < levelCount; index++)
        {
            int levelNumber = index + 1;
            GameObject instance = Instantiate(levelButtonPrefab, content, false);
            instance.name = "LevelButton_" + levelNumber.ToString("00");
            instance.transform.localRotation = Quaternion.identity;

            global::UnityEngine.UI.Button button =
                instance.GetComponent<global::UnityEngine.UI.Button>();
            global::UnityEngine.UI.Image background =
                instance.GetComponent<global::UnityEngine.UI.Image>();
            global::RTLTMPro.RTLTextMeshPro title =
                instance.transform.Find("LevelTitle").GetComponent<global::RTLTMPro.RTLTextMeshPro>();
            GameObject lockIcon = instance.transform.Find("LockIcon").gameObject;
            global::UnityEngine.UI.Image starsImage = instance.transform.Find("StarsDisplay")
                .GetComponent<global::UnityEngine.UI.Image>();
            // Keep the position and size authored in the prefab.
            starsImage.raycastTarget = false;
            starsImage.preserveAspect = true;
            starsImage.type = global::UnityEngine.UI.Image.Type.Simple;
            starsImage.enabled = true;
            starsImage.gameObject.SetActive(true);

            background.raycastTarget = true;
            button.targetGraphic = background;
            title.text = LevelTitles[index];
            title.raycastTarget = false;

            foreach (global::UnityEngine.UI.Graphic graphic in
                     lockIcon.GetComponentsInChildren<global::UnityEngine.UI.Graphic>(true))
                graphic.raycastTarget = false;

            global::UnityEngine.UI.LayoutElement element =
                instance.GetComponent<global::UnityEngine.UI.LayoutElement>();
            if (element != null) element.ignoreLayout = false;

            global::UnityEngine.Events.UnityAction clickAction = () => SelectLevel(levelNumber);
            button.onClick.AddListener(clickAction);
            RectTransform instanceRect = instance.GetComponent<RectTransform>();
            views[index] = new LevelView
            {
                Button = button,
                Rect = instanceRect,
                OriginalSize = new Vector2(
                    Mathf.Max(1f, instanceRect.rect.width),
                    Mathf.Max(1f, instanceRect.rect.height)),
                OriginalScale = instanceRect.localScale,
                LockIcon = lockIcon,
                StarsImage = starsImage,
                ClickAction = clickAction
            };

            instance.SetActive(true);
        }
    }

    private void LateUpdate()
    {
        if (!initialized || levelsScrollView == null || content == null || viewport == null) return;

        bool visible = levelsScrollView.isActiveAndEnabled;
        if (!visible)
        {
            wasVisible = false;
            return;
        }

        // Menus can be inactive during Start. Rebuild on opening and after resizing.
        if (!wasVisible) Canvas.ForceUpdateCanvases();

        // Measure the Viewport, not the Content whose width this method changes.
        Vector2 viewportSize = viewport.rect.size;
        if (float.IsNaN(viewportSize.x) || float.IsInfinity(viewportSize.x) ||
            float.IsNaN(viewportSize.y) || float.IsInfinity(viewportSize.y) ||
            viewportSize.x <= 0f || viewportSize.y <= 0f) return;

        Vector4 layoutSettings = new Vector4(0f, spacing, padding, rightToLeft ? 1f : 0f);
        if (!wasVisible || (viewportSize - lastViewportSize).sqrMagnitude > 0.25f ||
            layoutSettings != lastLayoutSettings)
        {
            bool returnToStart = !wasVisible || layoutSettings.w != lastLayoutSettings.w;
            float scrollPosition = returnToStart
                ? (rightToLeft ? 1f : 0f)
                : Mathf.Clamp01(levelsScrollView.horizontalNormalizedPosition);

            lastViewportSize = viewportSize;
            lastLayoutSettings = layoutSettings;
            ApplyButtonLayout(viewportSize);

            levelsScrollView.StopMovement();
            levelsScrollView.horizontalNormalizedPosition = scrollPosition;
        }

        wasVisible = true;
    }

    private void ApplyButtonLayout(Vector2 viewportSize)
    {
        float inset = Mathf.Max(0, padding);
        float gap = Mathf.Max(0f, spacing);

        // Use each prefab's actual width and scale, without fitting it to the viewport.
        float rowWidth = Mathf.Max(0, views.Length - 1) * gap;
        foreach (LevelView view in views)
        {
            if (view.Rect != null)
                rowWidth += view.OriginalSize.x * Mathf.Abs(view.OriginalScale.x);
        }

        float contentWidth = Mathf.Max(viewportSize.x, rowWidth + inset * 2f);
        float left = (contentWidth - rowWidth) * 0.5f;
        content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, contentWidth);
        float offset = 0f;

        foreach (LevelView view in views)
        {
            RectTransform rect = view.Rect;
            if (rect == null) continue;

            float width = view.OriginalSize.x * Mathf.Abs(view.OriginalScale.x);
            float x = rightToLeft
                ? left + rowWidth - offset - width * 0.5f
                : left + offset + width * 0.5f;

            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            // Preserve the prefab dimensions after changing the positioning anchors.
            rect.sizeDelta = view.OriginalSize;
            rect.anchoredPosition = new Vector2(x, 0f);
            offset += width + gap;
        }
    }

    private void LoadProgress()
    {
        HighestUnlockedLevel = Mathf.Clamp(
            PlayerPrefs.GetInt(progressKey + ".Unlocked", 1), 1, levelCount);

        bestStars = new int[levelCount];
        for (int index = 0; index < levelCount; index++)
            bestStars[index] = Mathf.Clamp(PlayerPrefs.GetInt(StarsKey(index + 1), 0), 0, 5);
    }

    private string StarsKey(int levelNumber)
    {
        return progressKey + ".Level." + levelNumber + ".Stars";
    }

    public bool IsLevelUnlocked(int levelNumber)
    {
        return initialized && levelNumber >= 1 && levelNumber <= levelCount &&
               levelNumber <= HighestUnlockedLevel;
    }

    public int GetBestStars(int levelNumber)
    {
        if (!initialized || levelNumber < 1 || levelNumber > levelCount) return 0;
        return bestStars[levelNumber - 1];
    }

    public void SelectLevel(int levelNumber)
    {
        // Check the lock here as well as Button.interactable.
        if (!IsLevelUnlocked(levelNumber)) return;

        if (feedback != null && feedback.isActiveAndEnabled)
            feedback.PlayButton(views[levelNumber - 1].Button, JourneyUIFeedback.ButtonSound.Level,
                () => FinishLevelSelection(levelNumber));
        else
            FinishLevelSelection(levelNumber);
    }

    private void FinishLevelSelection(int levelNumber)
    {
        if (!IsLevelUnlocked(levelNumber)) return;
        SelectedLevelNumber = levelNumber;
        Debug.Log("Journey of Light: selected level " + levelNumber, this);
        onLevelSelected?.Invoke(levelNumber);
    }

    // Call only after the island's actual completion/scoring system awards stars.
    // Replays retain the best score. A low score never locks an unlocked level.
    public void CompleteLevel(int levelNumber, int earnedStars)
    {
        if (!IsLevelUnlocked(levelNumber)) return;

        earnedStars = Mathf.Clamp(earnedStars, 0, 5);
        bestStars[levelNumber - 1] = Mathf.Max(bestStars[levelNumber - 1], earnedStars);
        PlayerPrefs.SetInt(StarsKey(levelNumber), bestStars[levelNumber - 1]);

        if (bestStars[levelNumber - 1] >= starsRequiredToUnlockNext && levelNumber < levelCount)
            HighestUnlockedLevel = Mathf.Max(HighestUnlockedLevel, levelNumber + 1);

        PlayerPrefs.SetInt(progressKey + ".Unlocked", HighestUnlockedLevel);
        PlayerPrefs.Save();
        RefreshButtons();
    }

    public void CompleteSelectedLevel(int earnedStars)
    {
        CompleteLevel(SelectedLevelNumber, earnedStars);
    }

    public void RefreshButtons()
    {
        if (!initialized) return;

        for (int index = 0; index < views.Length; index++)
        {
            LevelView view = views[index];
            if (view.Button == null) continue;

            bool unlocked = IsLevelUnlocked(index + 1);
            view.Button.interactable = unlocked;
            if (view.LockIcon != null) view.LockIcon.SetActive(!unlocked);
            if (view.StarsImage != null)
                view.StarsImage.sprite = GetStarsSprite(GetBestStars(index + 1));
        }
    }

    private Sprite GetStarsSprite(int stars)
    {
        switch (Mathf.Clamp(stars, 0, 5))
        {
            case 1: return oneStar;
            case 2: return twoStars;
            case 3: return threeStars;
            case 4: return fourStars;
            case 5: return fiveStars;
            default: return zeroStars;
        }
    }

    private void OnDestroy()
    {
        if (views != null)
        {
            foreach (LevelView view in views)
            {
                if (view == null || view.Button == null) continue;
                view.Button.onClick.RemoveListener(view.ClickAction);
                if (Application.isPlaying)
                {
                    view.Button.gameObject.SetActive(false);
                    Destroy(view.Button.gameObject);
                }
            }
        }

        if (Application.isPlaying)
        {
            RestoreSceneTemplates();
            RestoreContentLayout();
        }
    }
}