using System.Collections.Generic;
using UnityEngine;

// Copies geometry only: previews cannot run weapon scripts, physics or audio.
public static class JourneyEquipmentVisuals
{
    public static GameObject Copy(GameObject source, Transform parent, int layer, List<Mesh> ownedMeshes)
    {
        var result = new GameObject(source != null ? source.name + " Preview" : "Equipment Preview");
        result.transform.SetParent(parent, false); result.layer = layer;
        if (source == null) return result;
        var excluded = new HashSet<Renderer>();
        foreach (var lod in source.GetComponentsInChildren<LODGroup>(true))
        {
            LOD[] levels = lod.GetLODs();
            for (int i = 1; i < levels.Length; i++) foreach (var renderer in levels[i].renderers) if (renderer != null) excluded.Add(renderer);
        }
        foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
        {
            if (excluded.Contains(renderer)) continue;
            Mesh mesh = null;
            if (renderer is SkinnedMeshRenderer skinned)
            { mesh = new Mesh { name = source.name + " Preview Mesh" }; skinned.BakeMesh(mesh); ownedMeshes.Add(mesh); }
            else if (renderer is MeshRenderer)
            { var filter = renderer.GetComponent<MeshFilter>(); if (filter != null) mesh = filter.sharedMesh; }
            if (mesh == null) continue;
            var go = new GameObject(renderer.name, typeof(MeshFilter), typeof(MeshRenderer)); go.layer = layer;
            go.transform.SetParent(result.transform, false);
            go.transform.localPosition = source.transform.InverseTransformPoint(renderer.transform.position);
            go.transform.localRotation = Quaternion.Inverse(source.transform.rotation) * renderer.transform.rotation;
            Vector3 scale = source.transform.lossyScale, child = renderer.transform.lossyScale;
            go.transform.localScale = new Vector3(Ratio(child.x, scale.x), Ratio(child.y, scale.y), Ratio(child.z, scale.z));
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var copy = go.GetComponent<MeshRenderer>(); copy.sharedMaterials = renderer.sharedMaterials;
            copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            copy.receiveShadows = false;
        }
        return result;
    }
    private static float Ratio(float value, float divisor) => Mathf.Abs(divisor) > 0.0001f ? value / divisor : 1f;
    public static bool BoundsOf(GameObject root, out Bounds bounds)
    {
        bool found = false; bounds = new Bounds(root.transform.position, Vector3.zero);
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        { if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds); }
        return found;
    }
    public static void ReleaseMeshes(List<Mesh> meshes)
    { foreach (var mesh in meshes) if (mesh != null) Object.Destroy(mesh); meshes.Clear(); }
}
