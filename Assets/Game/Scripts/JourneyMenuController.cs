// Journey of Light: menu navigation and gameplay-input blocking.
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Attach to an always-active object, for example --Logic/MenuManager.
// Step 1: menu navigation. Level spawning, stars and island selection come next.
// Requires the existing Game Creator 2, RightScreenDragArea and LightBrushPuzzle.
[DisallowMultipleComponent]
[DefaultExecutionOrder(1000)]
public sealed class JourneyMenuController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject levelsPanel;
    [SerializeField] private GameObject settingPanel;

    [Header("Main Menu Buttons")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button settingButton;
    [SerializeField] private Button exitButton;

    [Header("Back Buttons")]
    [SerializeField] private Button levelsBackButton;
    [SerializeField] private Button settingBackButton;

    [Header("Game Creator Player (optional: found automatically)")]
    [SerializeField] private global::GameCreator.Runtime.Characters.Character player;

    public bool IsMenuOpen { get; private set; }

    private readonly global::System.Collections.Generic.Dictionary<GameObject, bool> savedObjectStates =
        new global::System.Collections.Generic.Dictionary<GameObject, bool>();
    private readonly global::System.Collections.Generic.Dictionary<Behaviour, bool> savedBehaviourStates =
        new global::System.Collections.Generic.Dictionary<Behaviour, bool>();

    private bool capturedPlayerControl;
    private bool previousPlayerControl;
    private bool started;
    private GameObject currentPanel;

    private void Awake()
    {
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
    }

    private void LateUpdate()
    {
        if (!IsMenuOpen) return;

        // GC's existing On Start currently locks the cursor. UI input needs
        // finite screen coordinates, so menus must keep that cursor unlocked.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

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

    public void ShowLevels()
    {
        ShowPanel(levelsPanel);
    }

    public void ShowSettings()
    {
        ShowPanel(settingPanel);
    }

    // The future level selector calls this AFTER checking the level's lock
    // and preparing its island. This method does not load another scene.
    public void HideMenusForGameplay()
    {
        ClearSelection();
        mainMenuPanel.SetActive(false);
        levelsPanel.SetActive(false);
        settingPanel.SetActive(false);
        currentPanel = null;
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
        levelsPanel.SetActive(target == levelsPanel);
        settingPanel.SetActive(target == settingPanel);
        target.transform.SetAsLastSibling();

        if (!IsMenuOpen)
        {
            IsMenuOpen = true;
            CaptureGameplayInput();
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void CaptureGameplayInput()
    {
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
            levelsPanel.transform.IsChildOf(target.transform) ||
            settingPanel.transform.IsChildOf(target.transform)) return;

        if (!savedObjectStates.ContainsKey(target))
            savedObjectStates.Add(target, target.activeSelf);

        if (target.activeSelf) target.SetActive(false);
    }

    private void RestoreGameplayInput()
    {
        IsMenuOpen = false;

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
        Bind(startButton, ShowLevels, add);
        Bind(settingButton, ShowSettings, add);
        Bind(exitButton, QuitGame, add);
        Bind(levelsBackButton, ShowMainMenu, add);
        Bind(settingBackButton, ShowMainMenu, add);
    }

    private static void Bind(Button button, UnityAction action, bool add)
    {
        if (button == null) return;
        button.onClick.RemoveListener(action);
        if (add) button.onClick.AddListener(action);
    }

    private bool ValidateReferences()
    {
        if (mainMenuPanel == null || levelsPanel == null || settingPanel == null ||
            startButton == null || settingButton == null || exitButton == null ||
            levelsBackButton == null || settingBackButton == null)
        {
            Debug.LogError("JourneyMenuController: assign all three Panels and all five Buttons in the Inspector before Play.", this);
            return false;
        }

        if (mainMenuPanel == levelsPanel || mainMenuPanel == settingPanel ||
            levelsPanel == settingPanel ||
            transform.IsChildOf(mainMenuPanel.transform) ||
            transform.IsChildOf(levelsPanel.transform) ||
            transform.IsChildOf(settingPanel.transform))
        {
            Debug.LogError("JourneyMenuController: use three different panels and put MenuManager outside them, for example under --Logic.", this);
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