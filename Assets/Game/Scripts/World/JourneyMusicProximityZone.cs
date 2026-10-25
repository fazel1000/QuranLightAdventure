using GameCreator.Runtime.Characters;
using UnityEngine;

[DisallowMultipleComponent, DefaultExecutionOrder(-100)]
public sealed class JourneyMusicProximityZone : MonoBehaviour
{
    public JourneyShuffleMusic music;
    [Tooltip("The gameplay player's root. Distance is independent of the drone camera.")]
    public Transform listener;
    [Tooltip("Use the sign's root when this component is on its Minab child.")]
    public Transform distanceOrigin;
    [Min(0f)] public float fadeStartDistance = 30f;
    [Min(0f)] public float silentDistance = 10f;
    [Tooltip("Extra smoothing when moving away or changing position suddenly. Entering the inner radius always silences music immediately.")]
    [Min(0f)] public float fadeSeconds = 0.5f;
    public float CurrentMultiplier { get; private set; } = 1f;
    private JourneyShuffleMusic registeredMusic;
    private bool sampled;

    private void OnEnable()
    {
        if (music == null) music = FindFirstObjectByType<JourneyShuffleMusic>(FindObjectsInactive.Include);
        if (listener == null)
            foreach (var character in FindObjectsByType<Character>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (character.IsPlayer && character.gameObject.scene == gameObject.scene)
                { listener = character.transform; break; }
        sampled = false;
        UpdateGain(0f);
    }
    private void Update() { UpdateGain(Time.unscaledDeltaTime); }
    private void UpdateGain(float deltaTime)
    {
        if (registeredMusic != music)
        {
            if (registeredMusic != null) registeredMusic.ClearVolumeMultiplier(this);
            registeredMusic = music; sampled = false;
        }
        if (registeredMusic == null) return;
        if (listener == null)
        { registeredMusic.ClearVolumeMultiplier(this); sampled = false; return; }
        Transform origin = distanceOrigin != null ? distanceOrigin : transform;
        float inner = Mathf.Max(0f, silentDistance);
        float outer = Mathf.Max(inner + 0.01f, fadeStartDistance);
        float distance = Vector3.Distance(listener.position, origin.position);
        float t = Mathf.InverseLerp(inner, outer, distance);
        float target = Mathf.SmoothStep(0f, 1f, t);
        CurrentMultiplier = !sampled || target <= 0f || fadeSeconds <= 0f ? target
            : Mathf.MoveTowards(CurrentMultiplier, target, deltaTime / fadeSeconds);
        sampled = true;
        registeredMusic.SetVolumeMultiplier(this, CurrentMultiplier);
    }
    private void OnDisable()
    {
        if (registeredMusic != null) registeredMusic.ClearVolumeMultiplier(this);
        registeredMusic = null; sampled = false; CurrentMultiplier = 1f;
    }
    private void OnDrawGizmosSelected()
    {
        Transform origin = distanceOrigin != null ? distanceOrigin : transform;
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(origin.position, Mathf.Max(0f, fadeStartDistance));
        Gizmos.color = Color.red; Gizmos.DrawWireSphere(origin.position, Mathf.Max(0f, silentDistance));
    }
}
