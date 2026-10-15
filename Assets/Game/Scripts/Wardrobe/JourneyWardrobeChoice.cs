using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class JourneyWardrobeChoice : MonoBehaviour
{
    public JourneyWardrobeAppearance appearance;
    [Range(0, 7)] public int category;
    public Button previous, next;
    public TMP_Text label;
    private static readonly string[] FA = { "لباس", "شلوار", "کفش", "مو", "عینک", "پوشش سر", "کوله", "کلاه" };
    private static readonly string[] EN = { "Top", "Pants", "Shoes", "Hair", "Glasses", "Head covering", "Backpack", "Hat" };
    private static readonly string[] AR = { "الملابس", "السروال", "الحذاء", "الشعر", "النظارات", "غطاء الرأس", "الحقيبة", "القبعة" };
    private void OnEnable()
    {
        if (previous != null) previous.onClick.AddListener(Previous);
        if (next != null) next.onClick.AddListener(Next);
        JourneyLocalization.Changed += Refresh; JourneyWardrobeAppearance.Changed += Refresh; Refresh();
    }
    private void OnDisable()
    {
        if (previous != null) previous.onClick.RemoveListener(Previous);
        if (next != null) next.onClick.RemoveListener(Next);
        JourneyLocalization.Changed -= Refresh; JourneyWardrobeAppearance.Changed -= Refresh;
    }
    private void Previous() { if (appearance != null) appearance.Step(category, -1); }
    private void Next() { if (appearance != null) appearance.Step(category, 1); }
    private void Refresh()
    {
        int count = appearance != null ? appearance.OptionCount(category) : 0;
        if (previous != null) previous.interactable = count > 1;
        if (next != null) next.interactable = count > 1;
        string name = JourneyLocalization.Choose(FA[category], EN[category], AR[category]);
        int index = appearance != null ? appearance.GetSelection(category) : 0;
        string choice = index == 0 ? JourneyLocalization.Choose("هیچ‌کدام", "None", "بدون") : index.ToString();
        JourneyLocalizedText.Write(label, name + " : " + choice);
    }
}
