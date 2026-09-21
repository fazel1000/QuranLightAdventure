using Eitan.SherpaONNXUnity.Runtime;
using UnityEngine;

/// <summary>
/// Prime Sherpa's Unity path/thread caches before scene components start models.
/// Verified against the public API in package commit cf70be74cb51.
/// No GameObject or Inspector reference is required.
/// </summary>
public static class JourneySherpaBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeBeforeScene()
    {
        // This Unity callback runs on the main thread. The package's public
        // initializer primes ThreadingUtils and SherpaPathResolver before
        // background validation asks for streamingAssetsPath.
        SherpaONNXRuntimeSettingsApplier.ApplyFromResources();
    }
}
