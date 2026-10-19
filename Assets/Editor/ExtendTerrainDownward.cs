
using UnityEngine;
using UnityEditor;
using System.IO;

public class ExtendTerrainDownward : EditorWindow
{
    float extraDepth = 20f;

    [MenuItem("Tools/Terrain/Extend Downward")]
    static void OpenWindow()
    {
        GetWindow<ExtendTerrainDownward>("Extend Terrain");
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField(
            "Extend Terrain Height Downward",
            EditorStyles.boldLabel
        );

        extraDepth = EditorGUILayout.FloatField(
            "Extra Depth (meters)", extraDepth
        );

        Terrain terrain = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<Terrain>()
            : null;

        if (terrain == null)
        {
            EditorGUILayout.HelpBox(
                "First select a Terrain object in the Hierarchy.",
                MessageType.Warning
            );
            return;
        }

        if (GUILayout.Button("Extend Downward"))
        {
            Extend(terrain, extraDepth);
        }
    }

    static void Extend(Terrain terrain, float depth)
    {
        if (depth <= 0 || terrain.terrainData == null)
            return;

        if (Mathf.Abs(terrain.transform.lossyScale.y - 1f) > 0.001f)
        {
            Debug.LogError(
                "This script requires Terrain Y scale to be 1."
            );
            return;
        }

        TerrainData original = terrain.terrainData;
        string sourcePath = AssetDatabase.GetAssetPath(original);
        TerrainData data;

        if (!string.IsNullOrEmpty(sourcePath))
        {
            string folder = Path.GetDirectoryName(sourcePath);
            string name = Path.GetFileNameWithoutExtension(sourcePath);
            string newPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(folder, name + "_Extended.asset")
                    .Replace("\\", "/")
            );

            if (!AssetDatabase.CopyAsset(sourcePath, newPath))
            {
                Debug.LogError("Could not duplicate TerrainData.");
                return;
            }

            data = AssetDatabase.LoadAssetAtPath<TerrainData>(newPath);
        }
        else
        {
            data = Instantiate(original);
            data.name = original.name + "_Extended";

            string newPath = AssetDatabase.GenerateUniqueAssetPath(
                "Assets/" + data.name + ".asset"
            );

            AssetDatabase.CreateAsset(data, newPath);
        }

        if (data == null)
            return;

        Undo.RegisterCompleteObjectUndo(
            terrain, "Extend Terrain Downward"
        );
        Undo.RegisterCompleteObjectUndo(
            terrain.transform, "Extend Terrain Downward"
        );

        terrain.terrainData = data;

        TerrainCollider terrainCollider =
            terrain.GetComponent<TerrainCollider>();

        if (terrainCollider != null)
        {
            Undo.RegisterCompleteObjectUndo(
                terrainCollider, "Extend Terrain Downward"
            );
            terrainCollider.terrainData = data;
        }

        Undo.RegisterCompleteObjectUndo(
            data, "Extend Terrain Downward"
        );

        int resolution = data.heightmapResolution;
        float oldHeight = data.size.y;
        float newHeight = oldHeight + depth;

        float[,] heights = data.GetHeights(
            0, 0, resolution, resolution
        );

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                heights[z, x] =
                    (heights[z, x] * oldHeight + depth)
                    / newHeight;
            }
        }

        Vector3 size = data.size;
        size.y = newHeight;
        data.size = size;
        data.SetHeights(0, 0, heights);

        Vector3 position = terrain.transform.position;
        position.y -= depth;
        terrain.transform.position = position;

        EditorUtility.SetDirty(data);
        EditorUtility.SetDirty(terrain);
        EditorUtility.SetDirty(terrain.transform);
        AssetDatabase.SaveAssets();

        Debug.Log(
            "Terrain extended downward by " + depth + " meters."
        );
    }
}

