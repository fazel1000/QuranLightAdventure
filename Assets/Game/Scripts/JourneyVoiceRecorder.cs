using System;
using UnityEngine;

// Owns only the microphone recording started by this component.
public sealed class JourneyVoiceRecorder : MonoBehaviour
{
    [SerializeField, Min(1)] private int sampleRate = 16000;
    [SerializeField, Min(1)] private int maxRecordSeconds = 15;
    [SerializeField, Min(0.2f)] private float silenceSecondsToStop = 3f;
    [SerializeField, Range(0f, 1f)] private float silenceThreshold = 0.015f;
    private AudioClip clip;
    private string device;
    private float startedAt, lastSpeechAt, nextCheck;
    private bool heardSpeech;
    private readonly float[] window = new float[256];
    public bool IsRecording { get; private set; }
    public event Action<AudioClip> RecordingFinished;

    public bool Begin()
    {
        if (IsRecording || Microphone.devices.Length == 0) return false;
        device = Microphone.devices[0];
        clip = Microphone.Start(device, false, Mathf.Max(1, maxRecordSeconds), Mathf.Max(8000, sampleRate));
        if (clip == null) return false;
        IsRecording = true;
        heardSpeech = false;
        startedAt = lastSpeechAt = nextCheck = Time.realtimeSinceStartup;
        return true;
    }

    private void Update()
    {
        if (!IsRecording) return;
        float now = Time.realtimeSinceStartup;
        if (now - startedAt >= maxRecordSeconds) { Finish(); return; }
        if (now < nextCheck) return;
        nextCheck = now + 0.2f;
        int pos = Microphone.GetPosition(device);
        if (pos < window.Length || clip == null) return;
        if (!clip.GetData(window, pos - window.Length)) return;
        float peak = 0f;
        foreach (float sample in window) peak = Mathf.Max(peak, Mathf.Abs(sample));
        if (peak >= silenceThreshold) { heardSpeech = true; lastSpeechAt = now; }
        else if (heardSpeech && now - lastSpeechAt >= silenceSecondsToStop) Finish();
    }

    public void Finish()
    {
        if (!IsRecording) return;
        int frames = Microphone.GetPosition(device);
        if (frames <= 0 && clip != null && Time.realtimeSinceStartup - startedAt >= maxRecordSeconds)
            frames = clip.samples;
        Microphone.End(device);
        IsRecording = false;
        AudioClip result = null;
        if (clip != null && frames > 0)
        {
            frames = Mathf.Min(frames, clip.samples);
            float[] samples = new float[frames * clip.channels];
            if (clip.GetData(samples, 0))
            {
                result = AudioClip.Create("PlayerRecitation", frames, clip.channels, clip.frequency, false);
                result.SetData(samples, 0);
            }
        }
        if (clip != null) Destroy(clip);
        clip = null;
        // Receiver owns the trimmed clip and must destroy it after assessment.
        if (RecordingFinished != null) RecordingFinished.Invoke(result);
        else if (result != null) Destroy(result);
    }

    public void Cancel()
    {
        if (IsRecording) Microphone.End(device);
        IsRecording = false;
        if (clip != null) Destroy(clip);
        clip = null;
    }
    private void OnDisable() { Cancel(); }
    private void OnApplicationPause(bool paused) { if (paused) Cancel(); }
}
