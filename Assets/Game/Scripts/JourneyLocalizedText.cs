using TMPro;
using RTLTMPro;
using UnityEngine;

// Attach only to authored UI labels, never to dynamically written scores/status labels.
[DisallowMultipleComponent, RequireComponent(typeof(TMP_Text))]
public sealed class JourneyLocalizedText : MonoBehaviour
{
    public TMP_Text target;
    [TextArea] public string persian;
    [TextArea] public string english;
    [TextArea] public string arabic;
    [Tooltip("For fixed Quran verse labels only. Dynamic verse labels need no localization component.")]
    public bool alwaysArabic;
    private void Reset()
    {
        target = GetComponent<TMP_Text>();
        string source = Original(target);
        JourneyLocalization.TryGet(source, out persian, out english, out arabic);
    }
    public static string Original(TMP_Text text)
    {
        if (text is RTLTextMeshPro rtl) return rtl.OriginalText;
        if (text is RTLTextMeshPro3D rtl3d) return rtl3d.OriginalText;
        return text != null ? text.text : "";
    }
    private void OnEnable() { JourneyLocalization.Changed += Refresh; Refresh(); }
    private void OnDisable() { JourneyLocalization.Changed -= Refresh; }
    public void Refresh()
    {
        if (target == null) target = GetComponent<TMP_Text>();
        if (target == null) return;
        // For protected verse labels, an empty Arabic field means leave the verse untouched.
        if (alwaysArabic && string.IsNullOrEmpty(arabic)) return;
        Write(target, alwaysArabic ? arabic : JourneyLocalization.Choose(persian, english, arabic), alwaysArabic);
    }
    public static void Write(TMP_Text text, string value, bool arabicOnly = false)
    {
        if (text == null) return;
        bool farsi = !arabicOnly && JourneyLocalization.Language == 0;
        if (text is RTLTextMeshPro rtl) { rtl.Farsi = farsi; rtl.ForceFix = false; rtl.text = value; }
        else if (text is RTLTextMeshPro3D rtl3d) { rtl3d.Farsi = farsi; rtl3d.text = value; }
        else { text.isRightToLeftText = false; text.text = value; }
    }
}
