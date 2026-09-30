
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Attach to an always-active object, for example --Logic/MenuManager.
// Routes button feedback and menu music through the shared JourneyUIFeedback component.
// Requires the existing Game Creator 2, RightScreenDragArea and LightBrushPuzzle.
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class JourneyMenuController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject settingPanel;

    [Header("Main Menu Buttons")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button settingButton;
    [SerializeField] private Button exitButton;

    [Header("Back Buttons")]
    [SerializeField] private Button settingBackButton;

    [Header("Gameplay Back Button (optional)")]
    [SerializeField] private Button gameplayBackButton;

    private JourneyUIFeedback feedback;

    [Header("Game Creator Player (optional: found automatically)")]
    [SerializeField] private global::GameCreator.Runtime.Characters.Character player;

    [Header("Single World - First Start Position")]
    [SerializeField] private Transform startPoint;
    public bool HasStartedGame { get; private set; }
    public bool IsGameplayActive => HasStartedGame && !IsMenuOpen;

    public enum GameLanguage { Persian = 0, English = 1, Arabic = 2 }
    [Header("Language Buttons - translations will be added later")]
    [SerializeField] private Button persianButton;
    [SerializeField] private Button englishButton;
    [SerializeField] private Button arabicButton;
    [SerializeField] private UnityEvent<int> onLanguageChanged = new UnityEvent<int>();
    private const string LanguageKey = "JourneyOfLight.Language";
    public GameLanguage SelectedLanguage { get; private set; }

    public bool IsMenuOpen { get; private set; } = true;

    private readonly global::System.Collections.Generic.Dictionary<GameObject, bool> savedObjectStates =
        new global::System.Collections.Generic.Dictionary<GameObject, bool>();
    private readonly global::System.Collections.Generic.Dictionary<Behaviour, bool> savedBehaviourStates =
        new global::System.Collections.Generic.Dictionary<Behaviour, bool>();

    private bool capturedGameplayInput;
    private bool capturedPlayerControl;
    private bool previousPlayerControl;
    private bool started;
    private GameObject currentPanel;

    private void Awake()
    {
        SelectedLanguage = (GameLanguage)Mathf.Clamp(PlayerPrefs.GetInt(LanguageKey, 0), 0, 2);
        // Migration: the obsolete selector must not initialize after removing its UI.
        foreach (JourneyLevelsController selector in FindObjectsByType<JourneyLevelsController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (selector.gameObject.scene == gameObject.scene) selector.enabled = false;
        feedback = GetComponent<JourneyUIFeedback>();
        if (feedback == null) feedback = gameObject.AddComponent<JourneyUIFeedback>();
        if (!ValidateReferences()) enabled = false;
    }

    private void OnEnable()
    {
        BindButtons(true);

        if (started && currentPanel != null) ShowPanel(currentPanel);
    }

    private void Start()
    {
        started = true;
        ShowMainMenu();
        onLanguageChanged?.Invoke((int)SelectedLanguage);
    }

    private void LateUpdate()
    {
        if (!IsMenuOpen) return;

        // GC's existing On Start currently locks the cursor. UI input needs
        // finite screen coordinates, so menus must keep that cursor unlocked.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = false;

        BlockPlayerControl();

        // Game Creator can create these controls after this manager starts.
        HideAndRemember(global::GameCreator.Runtime.Common.TouchStickLeft.INSTANCE);
        HideAndRemember(global::GameCreator.Runtime.Common.TouchStickRight.INSTANCE);

        foreach (global::System.Collections.Generic.KeyValuePair<GameObject, bool> item in savedObjectStates)
        {
            if (item.Key != null && item.Key.activeSelf)
                item.Key.SetActive(false);
        }

        foreach (global::System.Collections.Generic.KeyValuePair<Behaviour, bool> item in savedBehaviourStates)
        {
            if (item.Key != null && item.Key.enabled)
                item.Key.enabled = false;
        }
    }

    public void ShowMainMenu()
    {
        ShowPanel(mainMenuPanel);
    }

    // Compatibility for old Inspector events; there is no level selection screen.
    public void ShowLevels() => StartGame();

    public void StartGame()
    {
        if (!isActiveAndEnabled || IsGameplayActive) return;
        if (!HasStartedGame)
        {
            if (startPoint == null || player == null || player.Driver == null)
            {
                Debug.LogError("JourneyMenuController: assign Start Point and the Game Creator Player before starting.", this);
                return;
            }
            player.Driver.SetPosition(startPoint.position);
            player.Driver.SetRotation(startPoint.rotation);
            HasStartedGame = true;
        }
        HideMenusForGameplay();
    }

    public void SelectPersian() => SelectLanguage(GameLanguage.Persian);
    public void SelectEnglish() => SelectLanguage(GameLanguage.English);
    public void SelectArabic() => SelectLanguage(GameLanguage.Arabic);

    private void SelectLanguage(GameLanguage language)
    {
        SelectedLanguage = language;
        PlayerPrefs.SetInt(LanguageKey, (int)language);
        PlayerPrefs.Save();
        onLanguageChanged?.Invoke((int)language);
    }

    private void OnPersianClicked() => PlayClick(persianButton, JourneyUIFeedback.ButtonSound.Settings, SelectPersian);
    private void OnEnglishClicked() => PlayClick(englishButton, JourneyUIFeedback.ButtonSound.Settings, SelectEnglish);
    private void OnArabicClicked() => PlayClick(arabicButton, JourneyUIFeedback.ButtonSound.Settings, SelectArabic);

    public void ShowSettings()
    {
        ShowPanel(settingPanel);
    }

    // Preserve old Inspector calls while ensuring the initial spawn is applied.
    public void HideMenusForGameplay()
    {
        if (!HasStartedGame) { StartGame(); return; }
        ClearSelection();
        mainMenuPanel.SetActive(false);
        settingPanel.SetActive(false);
        currentPanel = null;
        feedback.StopMenuMusic();
        RestoreGameplayInput();
    }

    public void QuitGame()
    {
        // Application.Quit alone does nothing in the Unity Editor.
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void ShowPanel(GameObject target)
    {
        if (target == null) return;

        ClearSelection();
        currentPanel = target;

        mainMenuPanel.SetActive(target == mainMenuPanel);
        settingPanel.SetActive(target == settingPanel);
        target.transform.SetAsLastSibling();
        feedback.ShowMenuMusic(false);

        if (!IsMenuOpen || !capturedGameplayInput)
        {
            IsMenuOpen = true;
            CaptureGameplayInput();
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = false;
    }

    private void CaptureGameplayInput()
    {
        capturedGameplayInput = true;
        if (player == null)
        {
            global::GameCreator.Runtime.Characters.Character[] characters =
                FindObjectsByType<global::GameCreator.Runtime.Characters.Character>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (global::GameCreator.Runtime.Characters.Character character in characters)
            {
                if (!character.IsPlayer || character.gameObject.scene != gameObject.scene)
                    continue;

                player = character;
                break;
            }
        }

        BlockPlayerControl();
        HideAndRemember(global::GameCreator.Runtime.Common.TouchStickLeft.INSTANCE);
        HideAndRemember(global::GameCreator.Runtime.Common.TouchStickRight.INSTANCE);

        RightScreenDragArea[] dragAreas = FindObjectsByType<RightScreenDragArea>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (RightScreenDragArea area in dragAreas)
        {
            if (area.gameObject.scene == gameObject.scene)
                HideAndRemember(area.gameObject);
        }

        RightScreenDragArea.ClearInput();

        // LightBrushPuzzle reads touch input independently of the UI.
        // Disable its input loop while menus are open to prevent background draws.
        LightBrushPuzzle[] puzzles = FindObjectsByType<LightBrushPuzzle>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (LightBrushPuzzle puzzle in puzzles)
        {
            if (puzzle.gameObject.scene != gameObject.scene) continue;
            savedBehaviourStates.Add(puzzle, puzzle.enabled);
            puzzle.enabled = false;
        }
    }

    private void BlockPlayerControl()
    {
        if (player == null || player.Player == null) return;

        if (!capturedPlayerControl)
        {
            previousPlayerControl = player.Player.IsControllable;
            capturedPlayerControl = true;
        }

        player.Player.IsControllable = false;
    }

    private void HideAndRemember(GameObject target)
    {
        if (target == null) return;

        // Never hide the manager, its parents, or an ancestor of a menu panel.
        if (transform.IsChildOf(target.transform) ||
            mainMenuPanel.transform.IsChildOf(target.transform) ||
            settingPanel.transform.IsChildOf(target.transform)) return;

        if (!savedObjectStates.ContainsKey(target))
            savedObjectStates.Add(target, target.activeSelf);

        if (target.activeSelf) target.SetActive(false);
    }

    private void RestoreGameplayInput()
    {
        IsMenuOpen = false;
        capturedGameplayInput = false;

        foreach (global::System.Collections.Generic.KeyValuePair<GameObject, bool> item in savedObjectStates)
        {
            if (item.Key != null) item.Key.SetActive(item.Value);
        }
        savedObjectStates.Clear();

        foreach (global::System.Collections.Generic.KeyValuePair<Behaviour, bool> item in savedBehaviourStates)
        {
            if (item.Key != null) item.Key.enabled = item.Value;
        }
        savedBehaviourStates.Clear();

        if (capturedPlayerControl && player != null && player.Player != null)
            player.Player.IsControllable = previousPlayerControl;

        capturedPlayerControl = false;
        RightScreenDragArea.ClearInput();

        // Leave the cursor unlocked: this project's gameplay also uses absolute
        // UI pointer positions for the joystick and camera drag area.
    }

    private static void ClearSelection()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void BindButtons(bool add)
    {
        Bind(startButton, OnStartClicked, add);
        Bind(settingButton, OnSettingsClicked, add);
        Bind(exitButton, OnExitClicked, add);
        Bind(persianButton, OnPersianClicked, add);
        Bind(englishButton, OnEnglishClicked, add);
        Bind(arabicButton, OnArabicClicked, add);
        Bind(settingBackButton, OnSettingsBackClicked, add);
        Bind(gameplayBackButton, OnGameplayBackClicked, add);
    }

    private void OnStartClicked()
    {
        PlayClick(startButton, JourneyUIFeedback.ButtonSound.Start, StartGame);
    }

    private void OnSettingsClicked()
    {
        PlayClick(settingButton, JourneyUIFeedback.ButtonSound.Settings, ShowSettings);
    }

    private void OnExitClicked()
    {
        PlayClick(exitButton, JourneyUIFeedback.ButtonSound.Exit, QuitGame);
    }

    private void OnSettingsBackClicked()
    {
        PlayClick(settingBackButton, JourneyUIFeedback.ButtonSound.Back, ShowMainMenu);
    }

    private void OnGameplayBackClicked()
    {
        PlayClick(gameplayBackButton, JourneyUIFeedback.ButtonSound.Back, ShowMainMenu);
    }

    private void PlayClick(Button button, JourneyUIFeedback.ButtonSound sound, global::System.Action action)
    {
        if (feedback != null && feedback.isActiveAndEnabled)
            feedback.PlayButton(button, sound, action);
        else
            action?.Invoke();
    }

    private static void Bind(Button button, UnityAction action, bool add)
    {
        if (button == null) return;
        button.onClick.RemoveListener(action);
        if (add) button.onClick.AddListener(action);
    }

    private bool ValidateReferences()
    {
        if (mainMenuPanel == null || settingPanel == null || startButton == null ||
            settingButton == null || exitButton == null || settingBackButton == null)
        {
            Debug.LogError("JourneyMenuController: assign Main Menu, Setting Panel, Start, Settings, Exit and Settings Back.", this);
            return false;
        }
        if (mainMenuPanel == settingPanel || transform.IsChildOf(mainMenuPanel.transform) ||
            transform.IsChildOf(settingPanel.transform))
        {
            Debug.LogError("JourneyMenuController: use separate panels and keep MenuManager outside them.", this);
            return false;
        }
        return true;
    }

    private void OnDisable()
    {
        BindButtons(false);
        if (IsMenuOpen) RestoreGameplayInput();
    }
}