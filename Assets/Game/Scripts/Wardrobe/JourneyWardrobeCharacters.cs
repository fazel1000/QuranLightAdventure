using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Two complete characters share the player controller, but only Hodaart has editable clothes.
[DisallowMultipleComponent]
[RequireComponent(typeof(JourneyWardrobeRoom))]
public sealed class JourneyWardrobeCharacters : MonoBehaviour
{
    public GameObject cartoonGameplayPrefab;
    public Transform cartoonPreview;
    public Button hodaartButton, cartoonButton;
    public TMP_Text selectionText;
    [Tooltip("Show both characters after the first language choice, once per saved installation.")]
    public bool showSelectionOnFirstLaunch = true;
    public Color selectedColor = new Color(0.2f, 0.7f, 0.4f, 1f);
    public Color normalColor = new Color(0.18f, 0.24f, 0.32f, 1f);
    private const string ModelKey = "JourneyOfLight.Character.v1";
    private const string ReviewedKey = "JourneyOfLight.CharacterReviewed.v1";
    private JourneyWardrobeRoom room;
    private bool initialized, applied;
    private int selected;
    private Coroutine firstOpen;
    public bool ReturnToMainMenuOnClose { get; private set; }
    public Transform SelectedPreview => selected == 1 && cartoonPreview != null ? cartoonPreview : room != null && room.preview != null ? room.preview.transform : null;

    private void Awake() { room = GetComponent<JourneyWardrobeRoom>(); }
    private void OnEnable()
    {
        if (hodaartButton != null) hodaartButton.onClick.AddListener(SelectHodaart);
        if (cartoonButton != null) cartoonButton.onClick.AddListener(SelectCartoon);
        JourneyLocalization.Changed += Refresh;
    }
    private void OnDisable()
    {
        if (hodaartButton != null) hodaartButton.onClick.RemoveListener(SelectHodaart);
        if (cartoonButton != null) cartoonButton.onClick.RemoveListener(SelectCartoon);
        JourneyLocalization.Changed -= Refresh;
        if (firstOpen != null) StopCoroutine(firstOpen);
        firstOpen = null;
    }
    public void Initialize()
    {
        if (initialized) return;
        if (room == null) room = GetComponent<JourneyWardrobeRoom>();
        initialized = true;
        selected = Mathf.Clamp(PlayerPrefs.GetInt(ModelKey, 0), 0, 1);
        if (!Select(selected, false)) Select(0, false);
        Refresh();
        if (showSelectionOnFirstLaunch && PlayerPrefs.GetInt(ReviewedKey, 0) == 0)
            firstOpen = StartCoroutine(OpenAfterLanguageSelection());
    }
    private IEnumerator OpenAfterLanguageSelection()
    {
        yield return null;
        while (room != null && room.menu != null && room.menu.IsFirstLanguageOpen) yield return null;
        if (room != null && room.menu != null && !room.menu.HasStartedGame)
        {
            room.Open();
            ReturnToMainMenuOnClose = room.IsOpen;
        }
        firstOpen = null;
    }
    public void SelectHodaart() { Select(0, true); }
    public void SelectCartoon() { Select(1, true); }
    private bool Select(int index, bool save)
    {
        if (!initialized || room == null) return false;
        // During gameplay changing a model could interrupt an attack, vehicle, or throw.
        if (save && !room.IsOpen) return false;
        if (applied && index == selected) { Refresh(); return true; }
        GameObject prefab = index == 1 ? cartoonGameplayPrefab : room.gameplayModelPrefab;
        if (prefab == null || !room.TryChangeGameplayModel(prefab, index == 0)) return false;
        selected = index;
        applied = true;
        if (save) { PlayerPrefs.SetInt(ModelKey, index); PlayerPrefs.Save(); }
        Refresh();
        return true;
    }
    public void MarkSelectionReviewed()
    {
        ReturnToMainMenuOnClose = false;
        PlayerPrefs.SetInt(ReviewedKey, 1);
        PlayerPrefs.SetInt(ModelKey, selected);
        PlayerPrefs.Save();
    }
    private void Refresh()
    {
        if (!initialized || room == null || room.wardrobePanel == null) return;
        // Never assign Hodaart's appearance or outfit controls to Cartoon Kids.
        foreach (var choice in room.wardrobePanel.GetComponentsInChildren<JourneyWardrobeChoice>(true))
        {
            choice.appearance = room.preview;
            choice.gameObject.SetActive(selected == 0);
        }
        var drag = room.wardrobePanel.GetComponent<JourneyWardrobeDrag>();
        if (drag != null) { drag.target = SelectedPreview; drag.previewCamera = room.previewCamera; }
        Tint(hodaartButton, selected == 0);
        Tint(cartoonButton, selected == 1);
        JourneyLocalizedText.Write(selectionText, selected == 0 ?
            JourneyLocalization.Choose("کاراکتر انتخاب‌شده: Hodaart — لباس‌ها قابل تغییرند", "Selected: Hodaart — editable outfit", "الشخصية: Hodaart — ملابس قابلة للتغيير") :
            JourneyLocalization.Choose("کاراکتر انتخاب‌شده: Cartoon Kids — لباس آماده و ثابت", "Selected: Cartoon Kids — fixed outfit", "الشخصية: Cartoon Kids — ملابس جاهزة وثابتة"));
        if (room.IsOpen) room.FitPreviewCamera();
    }
    private void Tint(Button button, bool active)
    {
        if (button == null) return;
        Color color = active ? selectedColor : normalColor;
        ColorBlock colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.15f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = color * 0.8f;
        colors.pressedColor = new Color(colors.pressedColor.r, colors.pressedColor.g, colors.pressedColor.b, 1f);
        button.colors = colors;
    }
    [ContextMenu("Show character selection on next launch")]
    private void ResetFirstSelection() { PlayerPrefs.DeleteKey(ReviewedKey); PlayerPrefs.Save(); }
}
