using UnityEngine;
using UnityEngine.UI;

// Highlight only; JourneyMenuController owns click handling.
[DisallowMultipleComponent]
public sealed class JourneyLanguageHighlight : MonoBehaviour
{
    public Button persian, english, arabic;
    public Color selectedColor = new Color(0.2f, 0.85f, 0.45f, 1f);
    private Button[] buttons;
    private ColorBlock[] originals;
    private Selectable.Transition[] transitions;
    private void Awake()
    {
        buttons = new[] { persian, english, arabic }; originals = new ColorBlock[3]; transitions = new Selectable.Transition[3];
        for (int i = 0; i < 3; i++) if (buttons[i] != null) { originals[i] = buttons[i].colors; transitions[i] = buttons[i].transition; }
    }
    private void OnEnable() { JourneyLocalization.Changed += Refresh; Refresh(); }
    private void OnDisable()
    {
        JourneyLocalization.Changed -= Refresh;
        if (buttons == null) return;
        for (int i = 0; i < 3; i++) if (buttons[i] != null) { buttons[i].colors = originals[i]; buttons[i].transition = transitions[i]; }
    }
    private void Refresh()
    {
        if (buttons == null) return;
        for (int i = 0; i < 3; i++)
        {
            if (buttons[i] == null) continue;
            buttons[i].transition = Selectable.Transition.ColorTint;
            var colors = originals[i];
            if (i == JourneyLocalization.Language)
            { colors.normalColor = selectedColor; colors.highlightedColor = selectedColor; colors.selectedColor = selectedColor; colors.pressedColor = selectedColor * 0.85f; }
            buttons[i].colors = colors;
        }
    }
}
