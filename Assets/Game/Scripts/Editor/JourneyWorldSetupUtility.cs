#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal static class JourneyWorldSetupUtility
{
    internal const string Folder = "Assets/Game/WorldSystems";
    internal static void EnsureFolder(string path)
    {
        string parent = "Assets";
        foreach (string part in path.Split('/').Skip(1))
        { string next = parent + "/" + part; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(parent, part); parent = next; }
    }
    internal static Sprite Circle() => SpriteAsset("Circle", 128, (x, y) => new Color(1f, 1f, 1f, Mathf.Clamp01((0.98f - new Vector2(x, y).magnitude) * 64f)));
    internal static Sprite Arrow() => SpriteAsset("PlayerArrow", 64, (x, y) => new Color(1f, 1f, 1f, y > -0.8f && y < 0.9f && Mathf.Abs(x) < (0.9f - y) * 0.5f ? 1f : 0f));
    private static Sprite SpriteAsset(string name, int size, Func<float, float, Color> pixel)
    {
        EnsureFolder(Folder + "/UI"); string path = Folder + "/UI/" + name + ".png";
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path); if (sprite != null) return sprite;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) pixels[y * size + x] = pixel((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
        texture.SetPixels(pixels); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
    internal static Material BubbleMaterial()
    {
        EnsureFolder(Folder + "/Effects"); string path = Folder + "/Effects/Bubbles.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path); if (material != null) return material;
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) { Debug.LogError("URP Particles/Unlit shader is missing. Assign your own Bubble Material."); return null; }
        var sprite = SpriteAsset("BubbleRing", 64, (x, y) =>
        {
            float r = new Vector2(x, y).magnitude;
            float rim = Mathf.Clamp01((0.94f - r) * 25f) * Mathf.Clamp01((r - 0.65f) * 10f);
            return new Color(0.85f, 0.97f, 1f, rim * 0.8f);
        });
        material = new Material(shader) { name = "Bubbles", renderQueue = 3000 };
        material.SetTexture("_BaseMap", sprite.texture); material.SetFloat("_Surface", 1f); material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f); material.SetFloat("_Cull", 0f);
        AssetDatabase.CreateAsset(material, path); return material;
    }
    internal static GameObject Child(Transform parent, string name, params Type[] components)
    {
        var existing = parent.Cast<Transform>().FirstOrDefault(t => t.name.Trim() == name);
        if (existing != null) return existing.gameObject;
        var go = new GameObject(name, components); Undo.RegisterCreatedObjectUndo(go, "Create " + name); go.transform.SetParent(parent, false); return go;
    }
    internal static T Add<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? Undo.AddComponent<T>(go);
    internal static void Dirty(UnityEngine.Object value)
    { if (value == null) return; EditorUtility.SetDirty(value); PrefabUtility.RecordPrefabInstancePropertyModifications(value); }
    internal static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one; }
    internal static void FitHull(BoxCollider collider, Transform meshRoot)
    {
        var owner = collider.transform; bool found = false; Bounds bounds = default;
        foreach (var filter in meshRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue; Bounds mesh = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var point = owner.InverseTransformPoint(filter.transform.TransformPoint(corner));
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; } else bounds.Encapsulate(point);
            }
        }
        collider.center = found ? bounds.center : Vector3.up;
        collider.size = found ? bounds.size + Vector3.one * 0.05f : new Vector3(2f, 2f, 5f);
    }
}
#endif
