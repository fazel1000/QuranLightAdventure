using System.Collections.Generic;
using UnityEngine;

// Add to the existing Underwater Volume; its rendering Volume is kept intact.
[DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
public sealed class JourneyWaterVolume : MonoBehaviour
{
    public Transform waterSurface;
    [Tooltip("Used only when Water Surface is not assigned.")]
    public float surfaceLevel = 39.1f;
    [Min(0f)] public float boundaryInset = 0.05f;
    public static readonly HashSet<JourneyWaterVolume> Active = new HashSet<JourneyWaterVolume>();
    private BoxCollider region;
    public BoxCollider Region
    {
        get { if (region == null) region = GetComponent<BoxCollider>(); return region; }
    }
    public float SurfaceY => Mathf.Min(waterSurface != null ? waterSurface.position.y : surfaceLevel, Region.bounds.max.y);
    public bool IsUsable => enabled && gameObject.activeInHierarchy && Region != null && Region.enabled && Vector3.Dot(transform.up, Vector3.up) > 0.999f;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() { Active.Clear(); }
    private void OnEnable() { Active.Add(this); }
    private void OnDisable() { Active.Remove(this); }

    public bool ContainsHorizontal(Vector3 worldPoint, float margin = 0f)
    {
        if (!IsUsable) return false;
        Vector3 point = transform.InverseTransformPoint(worldPoint) - Region.center;
        Vector3 scale = transform.lossyScale;
        return Mathf.Abs(point.x) <= Region.size.x * 0.5f - margin / Mathf.Max(0.0001f, Mathf.Abs(scale.x)) &&
               Mathf.Abs(point.z) <= Region.size.z * 0.5f - margin / Mathf.Max(0.0001f, Mathf.Abs(scale.z));
    }
    public bool ContainsWaterAt(Vector3 worldPoint)
    {
        if (!ContainsHorizontal(worldPoint) || worldPoint.y > SurfaceY) return false;
        Vector3 point = transform.InverseTransformPoint(worldPoint);
        return point.y >= Region.center.y - Region.size.y * 0.5f;
    }
    public static JourneyWaterVolume FindAt(Vector3 point, UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var volume in Active)
            if (volume != null && volume.gameObject.scene == scene && volume.ContainsHorizontal(point)) return volume;
        return null;
    }

    // Fits all eight hull corners, including yaw and the actual transform scales.
    // Returns false if the volume is too small to contain this hull.
    public bool TryConstrainHull(Transform bodyRoot, BoxCollider hull, Vector3 desiredPosition,
        Quaternion desiredRotation, float surfaceClearance, out Vector3 result)
    {
        result = desiredPosition;
        if (!IsUsable || bodyRoot == null || hull == null) return false;
        Quaternion rotateOffset = desiredRotation * Quaternion.Inverse(bodyRoot.rotation);
        Vector3 offsetMin = Vector3.positiveInfinity, offsetMax = Vector3.negativeInfinity;
        float topOffset = float.NegativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = hull.center + Vector3.Scale(hull.size * 0.5f,
                new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
            Vector3 worldOffset = rotateOffset * (hull.transform.TransformPoint(corner) - bodyRoot.position);
            Vector3 localOffset = transform.InverseTransformVector(worldOffset);
            offsetMin = Vector3.Min(offsetMin, localOffset); offsetMax = Vector3.Max(offsetMax, localOffset);
            topOffset = Mathf.Max(topOffset, worldOffset.y);
        }
        Vector3 scale = transform.lossyScale;
        Vector3 padding = new Vector3(boundaryInset / Mathf.Max(0.0001f, Mathf.Abs(scale.x)),
            boundaryInset / Mathf.Max(0.0001f, Mathf.Abs(scale.y)), boundaryInset / Mathf.Max(0.0001f, Mathf.Abs(scale.z)));
        Vector3 min = Region.center - Region.size * 0.5f + padding - offsetMin;
        Vector3 max = Region.center + Region.size * 0.5f - padding - offsetMax;
        // Water is a horizontal surface; this volume may be yawed but not tilted.
        float maximumRootY = SurfaceY - topOffset - Mathf.Max(0f, surfaceClearance);
        max.y = Mathf.Min(max.y, transform.InverseTransformPoint(new Vector3(desiredPosition.x, maximumRootY, desiredPosition.z)).y);
        if (min.x > max.x || min.y > max.y || min.z > max.z) return false;
        Vector3 local = transform.InverseTransformPoint(desiredPosition);
        local = new Vector3(Mathf.Clamp(local.x, min.x, max.x), Mathf.Clamp(local.y, min.y, max.y), Mathf.Clamp(local.z, min.z, max.z));
        result = transform.TransformPoint(local); return true;
    }
    private void OnDrawGizmosSelected()
    {
        if (Region == null) return;
        Matrix4x4 previous = Gizmos.matrix; Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.8f); Gizmos.DrawWireCube(Region.center, Region.size); Gizmos.matrix = previous;
    }
}
