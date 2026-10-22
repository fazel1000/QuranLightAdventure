using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class JourneyMinimapMarker : MonoBehaviour
{
    public Transform trackedTarget, headingTarget;
    public Sprite icon;
    public Color tint = Color.white;
    [Min(2f)] public float size = 15f;
    public bool rotateIcon, keepOnEdge = true;
    public float headingOffset;
    public Transform Target => trackedTarget != null ? trackedTarget : transform;
    public static readonly HashSet<JourneyMinimapMarker> Active = new HashSet<JourneyMinimapMarker>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() { Active.Clear(); }
    private void OnEnable() { Active.Add(this); }
    private void OnDisable() { Active.Remove(this); }
}
