using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Surah", menuName = "Quran Kids/Journey Surah")]
public sealed class JourneySurahDefinition : ScriptableObject
{
    [Range(1, 114)] public int surahNumber = 112;
    [Tooltip("One entry per JSON verse. Ikhlas has five entries including Bismillah.")]
    public VerseEntry[] verses =
    {
        new VerseEntry { verseNumber = 1 },
        new VerseEntry { verseNumber = 2 },
        new VerseEntry { verseNumber = 3 },
        new VerseEntry { verseNumber = 4 },
        new VerseEntry { verseNumber = 5 }
    };

    [Serializable]
    public sealed class VerseEntry
    {
        [Min(1)] public int verseNumber = 1;
        public AudioClip verseAudio;
        [TextArea(2, 5), Tooltip("Leave empty to display aya_display_text from the Quran JSON.")]
        public string displayText;
    }

    public bool TryGetVerse(int number, out VerseEntry entry)
    {
        entry = null;
        if (verses == null || number < 1) return false;
        foreach (VerseEntry candidate in verses)
        {
            if (candidate == null || candidate.verseNumber != number) continue;
            // Ambiguous numbers must not play or assess an arbitrary entry.
            if (entry != null) { entry = null; return false; }
            entry = candidate;
        }
        return entry != null;
    }

#if UNITY_EDITOR
    [ContextMenu("Fill Verse List From Quran JSON")]
    private void FillFromJson()
    {
        TextAsset json = Resources.Load<TextAsset>("QuranData/ordered_quran_phonemes (1)");
        if (json == null)
        {
            Debug.LogError("Quran JSON was not found in Resources/QuranData.", this);
            return;
        }
        try
        {
            var data = QuranJsonParser.ParseVerses(json.text);
            var numbers = new SortedSet<int>();
            foreach (string key in data.Keys)
            {
                string[] parts = key.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out int surah) &&
                    int.TryParse(parts[1], out int number) && surah == surahNumber && number > 0)
                    numbers.Add(number);
            }
            if (numbers.Count == 0)
            {
                Debug.LogError("No verses found for this Surah Number; existing entries were kept.", this);
                return;
            }
            var next = new List<VerseEntry>();
            foreach (int number in numbers)
            {
                TryGetVerse(number, out VerseEntry old);
                next.Add(old ?? new VerseEntry { verseNumber = number });
            }
            UnityEditor.Undo.RecordObject(this, "Fill Quran verse list");
            verses = next.ToArray();
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
        }
        catch (Exception ex) { Debug.LogException(ex, this); }
    }
#endif
}
