using UnityEngine;

// Serialized tint survives saving/reopening an extracted-tree scene.
[ExecuteAlways, DisallowMultipleComponent]
public sealed class JourneyExtractedTreeTint : MonoBehaviour
{
    public Color tint = Color.white;
    private void OnEnable() { Apply(); }
    private void OnValidate() { Apply(); }
    public void Apply()
    {
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i]; if (material == null) continue;
                string property = material.HasProperty("_BaseColor") ? "_BaseColor" : material.HasProperty("_Color") ? "_Color" : null;
                if (property == null) continue;
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, i);
                block.SetColor(property, material.GetColor(property) * tint); renderer.SetPropertyBlock(block, i);
            }
        }
    }
}
