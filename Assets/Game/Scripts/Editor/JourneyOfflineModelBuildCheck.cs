#if UNITY_EDITOR
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Register an explicit fallback through Unity's build API, then inspect the final archive.
public sealed class JourneyOfflineModelBuildCheck : BuildPlayerProcessor, IPostprocessBuildWithReport
{
    public override int callbackOrder => 10000;
    private const string ModelFolder = "sherpa-onnx/models/speech-recognition/quran-streaming-zipformer2-ctc";
    private const string BundleFolder = "JourneyQuranOffline";
    public override void PrepareForBuild(BuildPlayerContext context)
    {
        if (context.BuildPlayerOptions.target != BuildTarget.Android) return;
        Check();
        string source = Path.Combine(Application.streamingAssetsPath, ModelFolder);
        // Outside Assets, so package asset processors cannot remove these staging files.
        string stage = Path.GetFullPath("Library/JourneyQuranOfflineBuild");
        Directory.CreateDirectory(stage);
        File.Copy(Path.Combine(source, "model.int8.onnx"), Path.Combine(stage, "model.bytes"), true);
        File.Copy(Path.Combine(source, "tokens.txt"), Path.Combine(stage, "tokens.bytes"), true);
        if (Directory.Exists(Path.Combine(Application.streamingAssetsPath, BundleFolder)))
            throw new BuildFailedException("JourneyQuranOffline is reserved for generated build data. Move your manually created StreamingAssets/JourneyQuranOffline folder out of StreamingAssets.");
        context.AddAdditionalPathToStreamingAssets(stage, BundleFolder);
    }
    [MenuItem("Quran Kids/Check Android Quran Model Files")]
    private static void Check()
    {
        string folder = Path.Combine(Application.streamingAssetsPath, ModelFolder);
        CheckFile(Path.Combine(folder, "model.int8.onnx"), 4096);
        CheckFile(Path.Combine(folder, "tokens.txt"), 1);
        Debug.Log("Source model files are present. The next Android build will include an explicit fallback copy and check the final APK/AAB. This does not validate recognizer compatibility.");
    }
    private static void CheckFile(string path, long minimumBytes)
    {
        if (!File.Exists(path)) throw new BuildFailedException("Required offline Quran model file is missing: " + path);
        if (new FileInfo(path).Length < minimumBytes) throw new BuildFailedException("Offline Quran model file is empty or too small: " + path);
        using (var stream = File.OpenRead(path))
        {
            byte[] head = new byte[128]; int n = stream.Read(head, 0, head.Length);
            if (Encoding.UTF8.GetString(head, 0, n).Contains("git-lfs.github.com/spec"))
                throw new BuildFailedException("Git LFS pointer found instead of the real model: " + path);
        }
    }
    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android) return;
        string path = report.summary.outputPath;
        if (!File.Exists(path) || !(path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".aab", StringComparison.OrdinalIgnoreCase)))
        { Debug.LogWarning("Exported Gradle project or split build: verify the final packaged APK/AAB with Quran Kids > Inspect Quran Model In APK or AAB."); return; }
        VerifyArchive(path, true);
    }
    [MenuItem("Quran Kids/Inspect Quran Model In APK or AAB")]
    private static void Inspect()
    {
        string path = EditorUtility.OpenFilePanel("Select the actual Android build", "", "");
        if (!string.IsNullOrEmpty(path)) VerifyArchive(path, false);
    }
    private static void VerifyArchive(string path, bool requireFallback)
    {
        using (var input = File.OpenRead(path))
        using (var zip = new ZipArchive(input, ZipArchiveMode.Read))
        {
            foreach (string file in new[] { "model.int8.onnx", "tokens.txt" })
            {
                string alias = BundleFolder + "/" + (file.EndsWith(".onnx") ? "model.bytes" : "tokens.bytes");
                var entry = FindEntry(zip, alias);
                if (entry == null && !requireFallback) entry = FindEntry(zip, ModelFolder + "/" + file);
                if (entry == null) throw new BuildFailedException("Model file NOT FOUND in Android archive: " + file + ". Rebuild after installing this patch. Archive: " + path);
                long expected = new FileInfo(Path.Combine(Application.streamingAssetsPath, ModelFolder, file)).Length;
                if (entry.Length != expected) throw new BuildFailedException("Bundled model length differs from the current source: " + entry.FullName);
                Debug.Log("Verified bundled file: " + entry.FullName + " (" + entry.Length + " bytes)");
            }
        }
    }
    private static ZipArchiveEntry FindEntry(ZipArchive zip, string relative)
    {
        foreach (var entry in zip.Entries)
            if (entry.FullName == "assets/" + relative || entry.FullName == "base/assets/" + relative ||
                entry.FullName.EndsWith("/assets/" + relative, StringComparison.Ordinal)) return entry;
        return null;
    }
}
#endif
