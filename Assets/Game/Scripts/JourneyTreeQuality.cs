using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class JourneyTreeQuality : MonoBehaviour
{
    [Serializable]
    public class Tree
    {
        public GameObject original, high, low;
    }

    public List<Tree> trees = new List<Tree>();
    private readonly Dictionary<LODGroup, LOD[]> groups = new Dictionary<LODGroup, LOD[]>();
    private readonly Dictionary<Renderer, bool> enabledAtStart = new Dictionary<Renderer, bool>();
    private readonly Dictionary<TerrainData, TreePrototype[]> terrains = new Dictionary<TerrainData, TreePrototype[]>();
    private bool initialized;

    private static Mesh MeshOf(Renderer renderer)
    {
        if (!renderer) return null;
        var filter = renderer.GetComponent<MeshFilter>();
        return filter ? filter.sharedMesh : null;
    }

    private void Initialize()
    {
        var meshes = new HashSet<Mesh>();
        foreach (var tree in trees)
        {
            if (!tree.original || !tree.high || !tree.low) continue;
            var group = tree.original.GetComponent<LODGroup>();
            if (!group || group.lodCount < 2) continue;
            foreach (var renderer in group.GetLODs()[0].renderers)
                if (MeshOf(renderer)) meshes.Add(MeshOf(renderer));
        }
        foreach (var group in FindObjectsByType<LODGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (group.gameObject.scene != gameObject.scene || group.lodCount < 2) continue;
            var lods = group.GetLODs();
            if (!Array.Exists(lods[0].renderers, r => meshes.Contains(MeshOf(r)))) continue;
            groups.Add(group, lods);
            foreach (var lod in lods)
                foreach (var r in lod.renderers)
                    if (r) enabledAtStart[r] = r.enabled;
        }
        foreach (var terrain in FindObjectsByType<Terrain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var data = terrain.terrainData;
            if (terrain.gameObject.scene != gameObject.scene || !data || terrains.ContainsKey(data)) continue;
            var prototypes = data.treePrototypes;
            if (Array.Exists(prototypes, p => trees.Exists(t => t.original == p.prefab && t.high && t.low)))
                terrains.Add(data, prototypes);
        }
        initialized = true;
    }

    public void Apply(bool low)
    {
        if (!initialized) Initialize();
        foreach (var entry in groups)
        {
            if (!entry.Key) continue;
            var original = entry.Value;
            int first = Mathf.Min(low ? 2 : 1, original.Length - 1);
            var remaining = new LOD[original.Length - first];
            Array.Copy(original, first, remaining, 0, remaining.Length);
            var visible = new HashSet<Renderer>();
            foreach (var lod in remaining) visible.UnionWith(lod.renderers);
            entry.Key.SetLODs(remaining);
            foreach (var lod in original)
                foreach (var r in lod.renderers)
                    if (r) r.enabled = enabledAtStart[r] && visible.Contains(r);
        }
        foreach (var entry in terrains)
        {
            if (!entry.Key) continue;
            var prototypes = entry.Key.treePrototypes;
            for (int i = 0; i < prototypes.Length && i < entry.Value.Length; i++)
            {
                var tree = trees.Find(t => t.original == entry.Value[i].prefab && t.high && t.low);
                if (tree != null) prototypes[i].prefab = low ? tree.low : tree.high;
            }
            entry.Key.treePrototypes = prototypes;
            entry.Key.RefreshPrototypes();
        }
    }

    private void OnDestroy()
    {
        foreach (var entry in terrains)
            if (entry.Key) { entry.Key.treePrototypes = entry.Value; entry.Key.RefreshPrototypes(); }
        foreach (var entry in groups)
            if (entry.Key) entry.Key.SetLODs(entry.Value);
        foreach (var entry in enabledAtStart)
            if (entry.Key) entry.Key.enabled = entry.Value;
    }
}
