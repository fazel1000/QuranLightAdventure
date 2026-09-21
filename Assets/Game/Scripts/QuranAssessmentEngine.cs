using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Eitan.Sherpa.Onnx.Unity.Mono.Components;
using UnityEngine;
using UnityEngine.Networking;

public class QuranAssessmentEngine : MonoBehaviour
{
    [Header("Quran Data")]
    [SerializeField] private QuranJsonRepository quranRepository;

    [Header("SherpaONNXUnity (EitanWong)")]
    [SerializeField] private RealtimeSpeechRecognizerComponent realtimeSpeechRecognizer;

    [Header("Android Offline Model")]
    [SerializeField] private string offlineModelId = "quran-streaming-zipformer2-ctc";
    [SerializeField] private string offlineModelFileName = "model.int8.onnx";

    [Header("Scoring")]
    [SerializeField, Range(0f, 100f)] private float excellentThreshold = 90f;
    [SerializeField, Range(0f, 100f)] private float goodThreshold = 75f;
    [SerializeField, Min(1f)] private float initializationTimeoutSeconds = 120f;

    [Header("Independent Clip Transcription")]
    [Tooltip("Adds guaranteed silence to the end of each recorded clip so the online recognizer detects an endpoint and resets its stream.")]
    [SerializeField, Range(0f, 5f)] private float trailingSilencePaddingSeconds = 3f;

    [Tooltip("Safety net for recognizer versions that still return previous transcriptions as a prefix.")]
    [SerializeField] private bool removeAccumulatedRecognizerPrefix = true;

    private Task<string> startupModelPreparationTask;
    private readonly SemaphoreSlim transcriptionGate =
        new SemaphoreSlim(1, 1);

    private readonly List<string> previousRecognizerOutputTokens =
        new List<string>();

    public bool IsReady
    {
        get
        {
            return quranRepository != null &&
                   quranRepository.IsReady &&
                   realtimeSpeechRecognizer != null &&
                   realtimeSpeechRecognizer.IsInitialized;
        }
    }

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // All Awake methods have completed at this point. Start preparing the
        // bundled offline model immediately when the game scene opens.
        if (startupModelPreparationTask == null)
            startupModelPreparationTask = PrepareAndroidOfflineModelAndRecognizerAsync();
#endif
    }

    // Used by the 3D UI before enabling its Record button.
    public async Task<string> EnsureReadyAsync()
    {
        if (quranRepository == null) return "Quran repository is not assigned.";
        if (!quranRepository.IsReady) quranRepository.Initialize();
        if (!quranRepository.IsReady) return "Quran data could not be loaded.";
        if (realtimeSpeechRecognizer == null) return "Speech recognizer is not assigned.";
#if UNITY_ANDROID && !UNITY_EDITOR
        if (startupModelPreparationTask != null && startupModelPreparationTask.IsCompleted &&
            (startupModelPreparationTask.IsFaulted || startupModelPreparationTask.IsCanceled ||
             !string.IsNullOrEmpty(startupModelPreparationTask.Result)))
            startupModelPreparationTask = null;
#endif
        string error = await GetOrStartAndroidModelPreparationAsync();
        if (!string.IsNullOrEmpty(error)) return error;
        if (!realtimeSpeechRecognizer.IsInitialized) await InitializeRecognizerAsync();
        return IsReady ? string.Empty : "Speech model could not be initialized.";
    }

    public async Task<QuranAssessmentResult> AssessAsync(
        int surahNumber,
        int verseNumber,
        AudioClip recording)
    {
        if (recording == null)
            return QuranAssessmentResult.Failed("Recording is missing.");

        if (quranRepository == null)
            return QuranAssessmentResult.Failed("QuranJsonRepository is not assigned.");

        if (!quranRepository.IsReady)
        {
            quranRepository.Initialize();

            if (!quranRepository.IsReady)
                return QuranAssessmentResult.Failed("Quran JSON repository is not ready.");
        }

        List<string> expected;

        try
        {
            expected = quranRepository.GetExpectedTokens(
                surahNumber,
                verseNumber);
        }
        catch (Exception ex)
        {
            return QuranAssessmentResult.Failed(
                "Expected phoneme tokenization failed: " + ex.Message);
        }

        if (expected.Count == 0)
        {
            return QuranAssessmentResult.Failed(
                $"Expected phoneme tokens are missing for {surahNumber}:{verseNumber}.");
        }

        if (realtimeSpeechRecognizer == null)
        {
            return QuranAssessmentResult.Failed(
                "RealtimeSpeechRecognizerComponent is not assigned.");
        }

        string offlineInstallError =
            await GetOrStartAndroidModelPreparationAsync();

        if (!string.IsNullOrEmpty(offlineInstallError))
        {
            return QuranAssessmentResult.Failed(
                offlineInstallError);
        }

        if (!realtimeSpeechRecognizer.IsInitialized)
        {
            await InitializeRecognizerAsync();
        }

        if (!realtimeSpeechRecognizer.IsInitialized)
        {
            return QuranAssessmentResult.Failed(
                "Quran phoneme model is not initialized.");
        }

        try
        {
            List<string> actual;

            await transcriptionGate.WaitAsync();

            try
            {
                AudioClip recognitionClip =
                    CreateRecognitionClipWithTrailingSilence(recording);

                try
                {
                    string recognizedText =
                        await realtimeSpeechRecognizer
                            .TranscribeClipAsync(recognitionClip);

                    List<string> rawActual =
                        TokenizeRecognizedText(recognizedText);

                    actual = ExtractCurrentUtteranceTokens(rawActual);
                }
                finally
                {
                    if (recognitionClip != null &&
                        recognitionClip != recording)
                    {
                        Destroy(recognitionClip);
                    }
                }
            }
            finally
            {
                transcriptionGate.Release();
            }

            if (actual.Count == 0)
            {
                return QuranAssessmentResult.Failed(
                    "The model returned no usable phoneme tokens.");
            }

            Alignment alignment = Align(expected, actual);

            float score = CalculateScore(expected, alignment);

            return new QuranAssessmentResult
            {
                success = true,
                score = Mathf.Clamp(score, 0f, 100f),
                expectedPhonemes = string.Join(" ", expected),
                recognizedPhonemes = string.Join(" ", actual),
                matches = alignment.matches,
                substitutions = alignment.substitutions,
                deletions = alignment.deletions,
                insertions = alignment.insertions,
                grade = GetGrade(score)
            };
        }
        catch (Exception ex)
        {
            Debug.LogException(ex, this);
            return QuranAssessmentResult.Failed(ex.Message);
        }
    }

    private async Task InitializeRecognizerAsync()
    {
        using (CancellationTokenSource timeout =
               new CancellationTokenSource(
                   TimeSpan.FromSeconds(initializationTimeoutSeconds)))
        {
            try
            {
                await realtimeSpeechRecognizer
                    .StartRecognizerAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                // The readiness check below returns the user-facing error.
            }
        }
    }

    private Task<string> GetOrStartAndroidModelPreparationAsync()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (startupModelPreparationTask == null)
        {
            startupModelPreparationTask =
                PrepareAndroidOfflineModelAndRecognizerAsync();
        }

        return startupModelPreparationTask;
#else
        return Task.FromResult(string.Empty);
#endif
    }

    private async Task<string> PrepareAndroidOfflineModelAndRecognizerAsync()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (realtimeSpeechRecognizer == null)
            return "RealtimeSpeechRecognizerComponent is not assigned.";

        // Awake may have tried to initialize before the model was installed.
        // Dispose that attempt, install the model, then initialize a clean one.
        if (!realtimeSpeechRecognizer.IsInitialized)
            realtimeSpeechRecognizer.DisposeModule();

        string installError =
            await EnsureAndroidOfflineModelInstalledAsync();

        if (!string.IsNullOrEmpty(installError))
            return installError;

        if (!realtimeSpeechRecognizer.IsInitialized)
            await InitializeRecognizerAsync();

        if (!realtimeSpeechRecognizer.IsInitialized)
            return "Quran phoneme model is not initialized.";
#endif

        return string.Empty;
    }

    private async Task<string> EnsureAndroidOfflineModelInstalledAsync()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (string.IsNullOrWhiteSpace(offlineModelId))
            return "Offline Quran model ID is empty.";

        string relativeDirectory =
            CombineAsUrl(
                "sherpa-onnx",
                "models",
                "speech-recognition",
                offlineModelId.Trim());

        string destinationDirectory =
            Path.Combine(
                Application.persistentDataPath,
                "sherpa-onnx",
                "models",
                "speech-recognition",
                offlineModelId.Trim());

        try
        {
            Directory.CreateDirectory(destinationDirectory);
        }
        catch (Exception ex)
        {
            return "Could not create the offline model directory: " + ex.Message;
        }

        string[] requiredFiles =
        {
            offlineModelFileName,
            "tokens.txt"
        };

        for (int i = 0; i < requiredFiles.Length; i++)
        {
            string fileName = requiredFiles[i];

            if (string.IsNullOrWhiteSpace(fileName))
                return "An offline model filename is empty.";

            string destinationPath =
                Path.Combine(destinationDirectory, fileName);

            try
            {
                if (File.Exists(destinationPath) &&
                    new FileInfo(destinationPath).Length > 0)
                {
                    continue;
                }
            }
            catch
            {
                // Copy the file again when its current state cannot be checked.
            }

            string sourceUrl =
                Application.streamingAssetsPath.TrimEnd('/', '\\') +
                "/" + relativeDirectory +
                "/" + fileName;

            string temporaryPath = destinationPath + ".download";

            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);

                using (UnityWebRequest request =
                       UnityWebRequest.Get(sourceUrl))
                {
                    request.downloadHandler =
                        new DownloadHandlerFile(temporaryPath, false);

                    UnityWebRequestAsyncOperation operation =
                        request.SendWebRequest();

                    while (!operation.isDone)
                        await Task.Yield();

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        if (File.Exists(temporaryPath))
                            File.Delete(temporaryPath);

                        return
                            "Offline Quran model could not be copied from the APK. " +
                            fileName + ": " + request.error;
                    }
                }

                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);

                File.Move(temporaryPath, destinationPath);
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
                catch
                {
                    // Preserve the original installation error.
                }

                return
                    "Offline Quran model installation failed for " +
                    fileName + ": " + ex.Message;
            }
        }
#endif

        return string.Empty;
    }

    private static string CombineAsUrl(params string[] parts)
    {
        return string.Join(
            "/",
            Array.ConvertAll(
                parts,
                part => (part ?? string.Empty).Trim('/', '\\')));
    }

    private void AddNormalizedToken(
        string raw,
        List<string> destination)
    {
        string token = raw.Trim();

        if (string.IsNullOrEmpty(token))
            return;

        int id;

        if (int.TryParse(token, out id))
        {
            string vocabularyToken;

            if (quranRepository.TryGetTokenById(id, out vocabularyToken))
            {
                if (!string.Equals(vocabularyToken, "<blank>", StringComparison.Ordinal))
                    destination.Add(vocabularyToken);
            }

            return;
        }

        if (token == "<blank>")
            return;

        if (quranRepository.IsVocabularyToken(token))
        {
            destination.Add(token);
            return;
        }

        List<string> subTokens =
            quranRepository.TokenizePhonemeSequence(token);

        destination.AddRange(subTokens);
    }

    private List<string> TokenizeRecognizedText(string text)
    {
        List<string> result = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
            return result;

        string[] parts = text
            .Trim()
            .Split(new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < parts.Length; i++)
            AddNormalizedToken(parts[i], result);

        return result;
    }

    private AudioClip CreateRecognitionClipWithTrailingSilence(
        AudioClip recording)
    {
        if (recording == null ||
            trailingSilencePaddingSeconds <= 0f)
        {
            return recording;
        }

        int paddingFrames =
            Mathf.CeilToInt(
                recording.frequency *
                trailingSilencePaddingSeconds);

        if (paddingFrames <= 0)
            return recording;

        int originalSampleCount =
            recording.samples * recording.channels;

        float[] originalSamples =
            new float[originalSampleCount];

        if (!recording.GetData(originalSamples, 0))
            return recording;

        AudioClip paddedClip = AudioClip.Create(
            recording.name + "_EndpointPadded",
            recording.samples + paddingFrames,
            recording.channels,
            recording.frequency,
            false);

        if (!paddedClip.SetData(originalSamples, 0))
        {
            Destroy(paddedClip);
            return recording;
        }

        return paddedClip;
    }

    private List<string> ExtractCurrentUtteranceTokens(
        List<string> rawActual)
    {
        List<string> current =
            rawActual == null
                ? new List<string>()
                : new List<string>(rawActual);

        if (removeAccumulatedRecognizerPrefix &&
            previousRecognizerOutputTokens.Count > 0 &&
            current.Count > previousRecognizerOutputTokens.Count &&
            StartsWithTokens(
                current,
                previousRecognizerOutputTokens))
        {
            current.RemoveRange(
                0,
                previousRecognizerOutputTokens.Count);
        }

        previousRecognizerOutputTokens.Clear();

        if (rawActual != null)
            previousRecognizerOutputTokens.AddRange(rawActual);

        return current;
    }

    private static bool StartsWithTokens(
        List<string> value,
        List<string> prefix)
    {
        if (value == null ||
            prefix == null ||
            prefix.Count == 0 ||
            value.Count < prefix.Count)
        {
            return false;
        }

        for (int i = 0; i < prefix.Count; i++)
        {
            if (!string.Equals(
                    value[i],
                    prefix[i],
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static Alignment Align(
        List<string> expected,
        List<string> actual)
    {
        int n = expected.Count;
        int m = actual.Count;

        float[,] distance = new float[n + 1, m + 1];

        for (int i = 1; i <= n; i++)
            distance[i, 0] =
                distance[i - 1, 0] +
                TokenWeight(expected[i - 1]);

        for (int j = 1; j <= m; j++)
            distance[0, j] =
                distance[0, j - 1] +
                0.5f * TokenWeight(actual[j - 1]);

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                float expectedWeight =
                    TokenWeight(expected[i - 1]);

                float substitutionCost =
                    expected[i - 1] == actual[j - 1]
                        ? 0f
                        : expectedWeight;

                float substitution =
                    distance[i - 1, j - 1] +
                    substitutionCost;

                float deletion =
                    distance[i - 1, j] +
                    expectedWeight;

                float insertion =
                    distance[i, j - 1] +
                    0.5f * TokenWeight(actual[j - 1]);

                distance[i, j] =
                    Mathf.Min(
                        substitution,
                        Mathf.Min(deletion, insertion));
            }
        }

        Alignment result = new Alignment();

        int x = n;
        int y = m;

        while (x > 0 || y > 0)
        {
            if (x > 0 && y > 0)
            {
                float expectedWeight =
                    TokenWeight(expected[x - 1]);

                float diagonalCost =
                    expected[x - 1] == actual[y - 1]
                        ? 0f
                        : expectedWeight;

                float diagonal =
                    distance[x - 1, y - 1] +
                    diagonalCost;

                if (ApproximatelyEqual(
                    distance[x, y],
                    diagonal))
                {
                    if (expected[x - 1] == actual[y - 1])
                        result.matches++;
                    else
                        result.substitutions++;

                    x--;
                    y--;
                    continue;
                }
            }

            if (x > 0)
            {
                float deletion =
                    distance[x - 1, y] +
                    TokenWeight(expected[x - 1]);

                if (ApproximatelyEqual(
                    distance[x, y],
                    deletion))
                {
                    result.deletions++;
                    x--;
                    continue;
                }
            }

            if (y > 0)
            {
                result.insertions++;
                y--;
                continue;
            }
        }

        result.totalExpectedWeight = 0f;

        for (int i = 0; i < expected.Count; i++)
            result.totalExpectedWeight += TokenWeight(expected[i]);

        result.totalErrorWeight = distance[n, m];

        return result;
    }

    private static float CalculateScore(
        List<string> expected,
        Alignment alignment)
    {
        if (expected == null || expected.Count == 0)
            return 0f;

        float totalWeight = alignment.totalExpectedWeight;

        if (totalWeight <= 0f)
            return 0f;

        float score =
            100f *
            (1f - alignment.totalErrorWeight / totalWeight);

        return Mathf.Clamp(score, 0f, 100f);
    }

    private static float TokenWeight(string token)
    {
        if (string.IsNullOrEmpty(token))
            return 1f;

        bool hasArabicMark = false;

        for (int i = 0; i < token.Length; i++)
        {
            char c = token[i];

            if ((c >= '\u064B' && c <= '\u065F') ||
                (c >= '\u0670' && c <= '\u06ED'))
            {
                hasArabicMark = true;
                break;
            }
        }

        // Tajweed/harakah-related encoded tokens stay significant,
        // but small transcription marks do not dominate the score.
        return hasArabicMark ? 1.15f : 1f;
    }

    private static bool ApproximatelyEqual(float a, float b)
    {
        return Mathf.Abs(a - b) <= 0.0001f;
    }

    private string GetGrade(float score)
    {
        if (score >= excellentThreshold)
            return "Excellent";

        if (score >= goodThreshold)
            return "Good";

        return "TryAgain";
    }

    [Serializable]
    private sealed class Alignment
    {
        public int matches;
        public int substitutions;
        public int deletions;
        public int insertions;
        public float totalExpectedWeight;
        public float totalErrorWeight;
    }
}

[Serializable]
public sealed class QuranAssessmentResult
{
    public bool success;
    public float score;
    public string expectedPhonemes;
    public string recognizedPhonemes;
    public int matches;
    public int substitutions;
    public int deletions;
    public int insertions;
    public string grade;
    public string error;

    public static QuranAssessmentResult Failed(string message)
    {
        return new QuranAssessmentResult
        {
            success = false,
            score = 0f,
            error = message,
            grade = "Failed"
        };
    }
}