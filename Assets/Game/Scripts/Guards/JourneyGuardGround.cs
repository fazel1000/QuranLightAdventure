using UnityEngine;

public static class JourneyGuardGround
{
    private static Terrain[] cachedTerrains;
    private static float refreshAt;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { cachedTerrains = null; refreshAt = 0f; }
    private static Terrain[] Terrains()
    {
        if (!Application.isPlaying) return Terrain.activeTerrains;
        if (cachedTerrains == null || Time.unscaledTime >= refreshAt)
        { cachedTerrains = Terrain.activeTerrains; refreshAt = Time.unscaledTime + 1f; }
        return cachedTerrains;
    }
    public static bool TryTerrain(Vector3 point, float maximumSlope, out Vector3 position, out Vector3 normal)
    {
        position = point; normal = Vector3.up;
        bool found = false;
        float height = float.NegativeInfinity;
        foreach (var terrain in Terrains())
        {
            if (terrain == null || terrain.terrainData == null) continue;
            Vector3 start = terrain.transform.position, size = terrain.terrainData.size;
            float u = (point.x - start.x) / size.x, v = (point.z - start.z) / size.z;
            if (u < 0f || u > 1f || v < 0f || v > 1f) continue;
            float y = terrain.SampleHeight(point) + start.y;
            if (y <= height) continue;
            height = y; found = true;
            position.y = y;
            normal = terrain.terrainData.GetInterpolatedNormal(u, v).normalized;
        }
        return found && Vector3.Angle(normal, Vector3.up) <= maximumSlope;
    }
    public static bool SafeSegment(Vector3 a, Vector3 b, float maximumSlope)
    {
        int steps = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(a, b)));
        for (int i = 0; i <= steps; i++)
            if (!TryTerrain(Vector3.Lerp(a, b, (float)i / steps), maximumSlope, out _, out _)) return false;
        return true;
    }
}
