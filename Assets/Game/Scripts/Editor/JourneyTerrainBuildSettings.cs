using UnityEditor;
using UnityEngine;

[FilePath("ProjectSettings/JourneyTerrainBuildSettings.asset", FilePathAttribute.Location.ProjectFolder)]
internal sealed class JourneyTerrainBuildSettings : ScriptableSingleton<JourneyTerrainBuildSettings>
{
    [SerializeField] private string mainTerrainGuid;

    internal string ResolveGuid(string fallback)
    {
        return string.IsNullOrEmpty(mainTerrainGuid) ? fallback : mainTerrainGuid;
    }

    [SettingsProvider]
    private static SettingsProvider CreateProvider()
    {
        return new SettingsProvider("Project/Journey/Graphics", SettingsScope.Project)
        {
            label = "Graphics",
            guiHandler = _ => instance.DrawSettings()
        };
    }

    private void DrawSettings()
    {
        string path = string.IsNullOrEmpty(mainTerrainGuid) ? null : AssetDatabase.GUIDToAssetPath(mainTerrainGuid);
        var current = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        EditorGUI.BeginChangeCheck();
        var selected = (TerrainData)EditorGUILayout.ObjectField("Main Terrain Data", current, typeof(TerrainData), false);
        if (EditorGUI.EndChangeCheck())
        {
            mainTerrainGuid = selected ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(selected)) : "";
            Save(true);
        }
        if (!string.IsNullOrEmpty(mainTerrainGuid) && !selected)
            EditorGUILayout.HelpBox("The selected TerrainData is missing. Assign the local TerrainData used by Terrain - Journey - Main.", MessageType.Warning);
    }
}
