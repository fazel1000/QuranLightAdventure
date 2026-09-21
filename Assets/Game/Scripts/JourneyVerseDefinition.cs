using UnityEngine;

[CreateAssetMenu(fileName = "Verse", menuName = "Quran Kids/Journey Verse")]
public sealed class JourneyVerseDefinition : ScriptableObject
{
    [Range(1, 114)] public int surahNumber = 112;
    [Min(1), Tooltip("JSON numbering: for Ikhlas 1=Bismillah, 2=Qul huwa Allahu ahad, 3=Allahu samad, 4=Lam yalid, 5=Wa lam yakun.")]
    public int verseNumber = 1;
    public AudioClip verseAudio;
    [TextArea(2, 5), Tooltip("Optional display text. Empty = use the Quran JSON. Does not change the text used for assessment.")]
    public string displayText;
}
