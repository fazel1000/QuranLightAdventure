using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class QuranJsonRepository : MonoBehaviour
{
    [Header("Offline Quran Data")]
    [SerializeField] private TextAsset orderedQuranPhonemesJson;

    [Tooltip("Optional. If empty, tokens.txt is loaded from StreamingAssets/tokens.txt.")]
    [SerializeField] private TextAsset tokensText;

    [SerializeField] private string tokensStreamingAssetsPath = "tokens.txt";

    private Dictionary<string, QuranVerseJsonData> verses;
    private List<string> vocabulary;
    private Dictionary<string, int> tokenToId;
    private Dictionary<int, string> idToToken;

    public bool IsReady { get; private set; }

    private void Awake()
    {
        Initialize();
    }

    public void Initialize()
    {
        IsReady = false;

        try
        {
            string json = LoadJsonText();

            if (string.IsNullOrWhiteSpace(json))
                throw new Exception("ordered_quran_phonemes.json could not be loaded.");

            string tokenText = LoadTokensText();

            if (string.IsNullOrWhiteSpace(tokenText))
                throw new Exception("tokens.txt could not be loaded.");

            verses = QuranJsonParser.ParseVerses(json);
            LoadTokens(tokenText);

            if (verses == null || verses.Count == 0)
                throw new Exception("No Quran verses were loaded from ordered_quran_phonemes.json.");

            if (vocabulary == null || vocabulary.Count == 0)
                throw new Exception("No vocabulary tokens were loaded from tokens.txt.");

            IsReady = true;

            Debug.Log(
                "QuranJsonRepository initialized. Verses=" +
                verses.Count +
                ", Tokens=" +
                vocabulary.Count,
                this);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex, this);
        }
    }

    private string LoadJsonText()
    {
        if (orderedQuranPhonemesJson != null)
            return orderedQuranPhonemesJson.text;

        string path =
            Path.Combine(
                Application.streamingAssetsPath,
                "ordered_quran_phonemes.json");

        if (File.Exists(path))
            return File.ReadAllText(path, Encoding.UTF8);

        return string.Empty;
    }

    private string LoadTokensText()
    {
        if (tokensText != null)
            return tokensText.text;

        string path =
            Path.Combine(
                Application.streamingAssetsPath,
                tokensStreamingAssetsPath);

        if (File.Exists(path))
            return File.ReadAllText(path, Encoding.UTF8);

        Debug.LogError(
            "QuranJsonRepository: tokens.txt was not found at: " +
            path,
            this);

        return string.Empty;
    }

    public bool TryGetVerse(
        int surahNumber,
        int verseNumber,
        out QuranVerseJsonData verse)
    {
        return TryGetVerse(
            surahNumber + ":" + verseNumber,
            out verse);
    }

    public bool TryGetVerse(
        string key,
        out QuranVerseJsonData verse)
    {
        verse = null;

        if (!IsReady ||
            verses == null ||
            string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return verses.TryGetValue(
            key.Trim(),
            out verse);
    }

    public string GetVerseText(
        int surahNumber,
        int verseNumber)
    {
        QuranVerseJsonData verse;

        if (!TryGetVerse(
            surahNumber,
            verseNumber,
            out verse))
        {
            return string.Empty;
        }

        return verse.ayaText;
    }

    public string GetVerseDisplayText(
        int surahNumber,
        int verseNumber)
    {
        QuranVerseJsonData verse;

        if (!TryGetVerse(
            surahNumber,
            verseNumber,
            out verse))
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(
            verse.ayaDisplayText))
        {
            return verse.ayaDisplayText;
        }

        return verse.ayaText;
    }

    public List<string> GetExpectedTokens(
        int surahNumber,
        int verseNumber)
    {
        QuranVerseJsonData verse;

        if (!TryGetVerse(
            surahNumber,
            verseNumber,
            out verse))
        {
            return new List<string>();
        }

        return TokenizePhonemeSequence(
            verse.AyaPhoneme);
    }

    public List<string> TokenizePhonemeSequence(
        string phonemeSequence)
    {
        List<string> result =
            new List<string>();

        if (string.IsNullOrWhiteSpace(
            phonemeSequence))
        {
            return result;
        }

        string[] words =
            phonemeSequence
                .Trim()
                .Split(
                    new[] { ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < words.Length; i++)
            TokenizeWord(words[i], result);

        return result;
    }

    public bool TryGetTokenId(
        string token,
        out int id)
    {
        id = -1;

        if (tokenToId == null ||
            string.IsNullOrEmpty(token))
        {
            return false;
        }

        return tokenToId.TryGetValue(
            token,
            out id);
    }

    public bool TryGetTokenById(
        int id,
        out string token)
    {
        token = null;

        if (idToToken == null)
            return false;

        return idToToken.TryGetValue(
            id,
            out token);
    }

    public bool IsVocabularyToken(
        string token)
    {
        return !string.IsNullOrEmpty(token) &&
               tokenToId != null &&
               tokenToId.ContainsKey(token);
    }

    private void LoadTokens(string text)
    {
        vocabulary =
            new List<string>();

        tokenToId =
            new Dictionary<string, int>(
                StringComparer.Ordinal);

        idToToken =
            new Dictionary<int, string>();

        string[] lines =
            text.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < lines.Length; i++)
        {
            string line =
                lines[i].Trim();

            if (string.IsNullOrEmpty(line))
                continue;

            int separator =
                line.LastIndexOf(' ');

            if (separator <= 0 ||
                separator >= line.Length - 1)
            {
                continue;
            }

            string token =
                line.Substring(
                    0,
                    separator)
                .Trim();

            string idText =
                line.Substring(
                    separator + 1)
                .Trim();

            int id;

            if (!int.TryParse(
                idText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out id))
            {
                continue;
            }

            if (token == "<blank>")
            {
                idToToken[id] = token;
                continue;
            }

            if (!tokenToId.ContainsKey(token))
                tokenToId.Add(token, id);

            if (!idToToken.ContainsKey(id))
                idToToken.Add(id, token);

            vocabulary.Add(token);
        }

        vocabulary.Sort(
            delegate(string a, string b)
            {
                int lengthCompare =
                    b.Length.CompareTo(a.Length);

                if (lengthCompare != 0)
                    return lengthCompare;

                return string.CompareOrdinal(
                    a,
                    b);
            });
    }

    private void TokenizeWord(
        string word,
        List<string> result)
    {
        if (string.IsNullOrEmpty(word))
            return;

        int position = 0;

        while (position < word.Length)
        {
            string bestToken = null;

            for (int i = 0;
                 i < vocabulary.Count;
                 i++)
            {
                string token =
                    vocabulary[i];

                if (token.Length == 0)
                    continue;

                if (position + token.Length >
                    word.Length)
                {
                    continue;
                }

                if (string.CompareOrdinal(
                    word,
                    position,
                    token,
                    0,
                    token.Length) == 0)
                {
                    bestToken = token;
                    break;
                }
            }

            if (bestToken == null)
            {
                throw new InvalidOperationException(
                    "Cannot tokenize Quran phoneme sequence at character " +
                    position +
                    ": \"" +
                    word +
                    "\"");
            }

            result.Add(bestToken);
            position += bestToken.Length;
        }
    }
}

[Serializable]
public sealed class QuranVerseJsonData
{
    public string key;
    public string ayaText;
    public string ayaDisplayText;
    public string AyaPhoneme;
    public List<string> ayaPhonemesList;
}

public static class QuranJsonParser
{
    public static Dictionary<string, QuranVerseJsonData> ParseVerses(
        string json)
    {
        Dictionary<string, QuranVerseJsonData> result =
            new Dictionary<string, QuranVerseJsonData>();

        if (string.IsNullOrWhiteSpace(json))
            return result;

        object root =
            JsonLiteParser.Parse(json);

        Dictionary<string, object> rootObject =
            root as Dictionary<string, object>;

        if (rootObject == null)
        {
            throw new Exception(
                "ordered_quran_phonemes.json root is not a JSON object.");
        }

        foreach (KeyValuePair<string, object> pair in rootObject)
        {
            Dictionary<string, object> verseObject =
                pair.Value as Dictionary<string, object>;

            if (verseObject == null)
                continue;

            QuranVerseJsonData verse =
                new QuranVerseJsonData();

            verse.key = pair.Key;
            verse.ayaText =
                GetString(
                    verseObject,
                    "aya_text");

            verse.ayaDisplayText =
                GetString(
                    verseObject,
                    "aya_display_text");

            verse.AyaPhoneme =
                GetString(
                    verseObject,
                    "aya_phoneme");

            verse.ayaPhonemesList =
                GetStringList(
                    verseObject,
                    "aya_phonemes_list");

            result[pair.Key] = verse;
        }

        return result;
    }

    private static string GetString(
        Dictionary<string, object> obj,
        string key)
    {
        object value;

        if (obj.TryGetValue(
            key,
            out value) &&
            value != null)
        {
            return value.ToString();
        }

        return string.Empty;
    }

    private static List<string> GetStringList(
        Dictionary<string, object> obj,
        string key)
    {
        List<string> result =
            new List<string>();

        object value;

        if (!obj.TryGetValue(
            key,
            out value))
        {
            return result;
        }

        List<object> list =
            value as List<object>;

        if (list == null)
            return result;

        for (int i = 0;
             i < list.Count;
             i++)
        {
            if (list[i] != null)
                result.Add(
                    list[i].ToString());
        }

        return result;
    }
}

internal static class JsonLiteParser
{
    private sealed class Parser
    {
        private readonly string json;
        private int index;

        public Parser(string json)
        {
            this.json = json;
        }

        public object ParseValue()
        {
            SkipWhitespace();

            if (index >= json.Length)
                throw new Exception(
                    "Unexpected end of JSON.");

            char c = json[index];

            if (c == '{')
                return ParseObject();

            if (c == '[')
                return ParseArray();

            if (c == '"')
                return ParseString();

            if (c == 't')
            {
                Expect("true");
                return true;
            }

            if (c == 'f')
            {
                Expect("false");
                return false;
            }

            if (c == 'n')
            {
                Expect("null");
                return null;
            }

            return ParseNumber();
        }

        private Dictionary<string, object> ParseObject()
        {
            Dictionary<string, object> obj =
                new Dictionary<string, object>();

            Expect('{');
            SkipWhitespace();

            if (Peek('}'))
            {
                index++;
                return obj;
            }

            while (true)
            {
                SkipWhitespace();

                string key =
                    ParseString();

                SkipWhitespace();
                Expect(':');

                object value =
                    ParseValue();

                obj[key] = value;

                SkipWhitespace();

                if (Peek('}'))
                {
                    index++;
                    break;
                }

                Expect(',');
            }

            return obj;
        }

        private List<object> ParseArray()
        {
            List<object> list =
                new List<object>();

            Expect('[');
            SkipWhitespace();

            if (Peek(']'))
            {
                index++;
                return list;
            }

            while (true)
            {
                list.Add(
                    ParseValue());

                SkipWhitespace();

                if (Peek(']'))
                {
                    index++;
                    break;
                }

                Expect(',');
            }

            return list;
        }

        private string ParseString()
        {
            Expect('"');

            StringBuilder builder =
                new StringBuilder();

            while (index < json.Length)
            {
                char c =
                    json[index++];

                if (c == '"')
                    return builder.ToString();

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (index >= json.Length)
                {
                    throw new Exception(
                        "Invalid JSON escape.");
                }

                char escape =
                    json[index++];

                switch (escape)
                {
                    case '"':
                        builder.Append('"');
                        break;

                    case '\\':
                        builder.Append('\\');
                        break;

                    case '/':
                        builder.Append('/');
                        break;

                    case 'b':
                        builder.Append('\b');
                        break;

                    case 'f':
                        builder.Append('\f');
                        break;

                    case 'n':
                        builder.Append('\n');
                        break;

                    case 'r':
                        builder.Append('\r');
                        break;

                    case 't':
                        builder.Append('\t');
                        break;

                    case 'u':
                        if (index + 4 >
                            json.Length)
                        {
                            throw new Exception(
                                "Invalid Unicode escape.");
                        }

                        string hex =
                            json.Substring(
                                index,
                                4);

                        builder.Append(
                            (char)Convert.ToInt32(
                                hex,
                                16));

                        index += 4;
                        break;

                    default:
                        throw new Exception(
                            "Unknown JSON escape: \\" +
                            escape);
                }
            }

            throw new Exception(
                "Unterminated JSON string.");
        }

        private object ParseNumber()
        {
            int start = index;

            while (index < json.Length)
            {
                char c =
                    json[index];

                if ((c >= '0' && c <= '9') ||
                    c == '-' ||
                    c == '+' ||
                    c == '.' ||
                    c == 'e' ||
                    c == 'E')
                {
                    index++;
                    continue;
                }

                break;
            }

            string number =
                json.Substring(
                    start,
                    index - start);

            double value;

            if (!double.TryParse(
                number,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value))
            {
                throw new Exception(
                    "Invalid JSON number: " +
                    number);
            }

            return value;
        }

        private void SkipWhitespace()
        {
            while (index < json.Length &&
                   char.IsWhiteSpace(
                       json[index]))
            {
                index++;
            }
        }

        private bool Peek(char expected)
        {
            return index < json.Length &&
                   json[index] == expected;
        }

        private void Expect(char expected)
        {
            SkipWhitespace();

            if (index >= json.Length ||
                json[index] != expected)
            {
                throw new Exception(
                    "Expected '" +
                    expected +
                    "' at JSON position " +
                    index +
                    ".");
            }

            index++;
        }

        private void Expect(string expected)
        {
            if (index + expected.Length >
                json.Length ||
                !string.Equals(
                    json.Substring(
                        index,
                        expected.Length),
                    expected,
                    StringComparison.Ordinal))
            {
                throw new Exception(
                    "Expected '" +
                    expected +
                    "' at JSON position " +
                    index +
                    ".");
            }

            index += expected.Length;
        }
    }

    public static object Parse(string json)
    {
        return new Parser(json).ParseValue();
    }
}