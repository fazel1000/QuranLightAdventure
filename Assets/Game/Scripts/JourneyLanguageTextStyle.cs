using System;
using TMPro;
using UnityEngine;

// Fonts only: safe on dynamic verses, status labels and score labels as well as buttons.
[DefaultExecutionOrder(-4500)]
[DisallowMultipleComponent, RequireComponent(typeof(TMP_Text))]
public sealed class JourneyLanguageTextStyle : MonoBehaviour
{
    [Serializable]
    public sealed class LanguageStyle
    {
        public TMP_FontAsset font;
        [Min(0f), Tooltip("0 keeps the original size, or the size requested by the score/status service.")]
        public float fontSize;
    }
    public TMP_Text target;
    [Tooltip("Use the Arabic font/size regardless of the UI language, for Quran verse labels.")]
    public bool alwaysArabic;
    public LanguageStyle persian = new LanguageStyle();
    public LanguageStyle english = new LanguageStyle();
    public LanguageStyle arabic = new LanguageStyle();
    private TMP_FontAsset originalFont;
    private float originalSize, originalMin, originalMax, fallbackSize = -1f;
    private bool originalAutoSize, captured;
    private void Reset() { target = GetComponent<TMP_Text>(); }
    private void Awake() { Capture(); }
    private void OnEnable() { JourneyLocalization.Changed += Refresh; Refresh(); }
    private void OnDisable()
    {
        JourneyLocalization.Changed -= Refresh;
        if (!captured || target == null) return;
        target.font = originalFont;
        target.fontSize = originalSize;
        target.enableAutoSizing = originalAutoSize;
        target.fontSizeMin = originalMin; target.fontSizeMax = originalMax;
    }
    private void Capture()
    {
        if (captured) return;
        if (target == null) target = GetComponent<TMP_Text>();
        if (target == null) return;
        originalFont = target.font; originalSize = target.fontSize;
        originalAutoSize = target.enableAutoSizing;
        originalMin = target.fontSizeMin; originalMax = target.fontSizeMax;
        captured = true;
    }
    public void Refresh() { Apply(false); }
    public void Apply(bool forceArabic, float requestedFallback = -1f)
    {
        Capture();
        if (target == null) return;
        if (requestedFallback >= 0f) fallbackSize = requestedFallback;
        var localized = GetComponent<JourneyLocalizedText>();
        bool arabicOnly = forceArabic || alwaysArabic || (localized != null && localized.alwaysArabic);
        int language = arabicOnly ? 2 : JourneyLocalization.Language;
        LanguageStyle style = language == 1 ? english : language == 2 ? arabic : persian;
        target.font = style != null && style.font != null ? style.font : originalFont;
        bool fixedSize = style != null && style.fontSize > 0f;
        target.enableAutoSizing = !fixedSize && fallbackSize < 0f && originalAutoSize;
        target.fontSize = fixedSize ? style.fontSize : fallbackSize >= 0f ? Mathf.Max(1f, fallbackSize) : originalSize;
        if (!fixedSize) { target.fontSizeMin = originalMin; target.fontSizeMax = originalMax; }
    }
    public static void ApplyTo(TMP_Text text, bool forceArabic = false, float fallbackSize = -1f)
    {
        if (text == null) return;
        var style = text.GetComponent<JourneyLanguageTextStyle>();
        if (style != null && style.isActiveAndEnabled) style.Apply(forceArabic, fallbackSize);
    }
}
