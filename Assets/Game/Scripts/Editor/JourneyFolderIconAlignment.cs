#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public sealed class JourneyFolderIconAlignment : EditorWindow
{
    [MenuItem("Quran Kids/Folder Icon Alignment")]
    private static void Open() => GetWindow<JourneyFolderIconAlignment>("Folder Icons");
    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Adjust only the small Project rows. The Unity 6 default corrects the 7-point overlay padding. Grid icons are unchanged.", MessageType.Info);
        EditorGUI.BeginChangeCheck();
        float x = EditorGUILayout.Slider("Horizontal offset", EditorPrefs.GetFloat("Journey.FolderIconOffsetX", 7f), -16f, 16f);
        float size = EditorGUILayout.Slider("Icon size", EditorPrefs.GetFloat("Journey.FolderIconSize", 16f), 12f, 24f);
        if (EditorGUI.EndChangeCheck())
        { EditorPrefs.SetFloat("Journey.FolderIconOffsetX", x); EditorPrefs.SetFloat("Journey.FolderIconSize", size); EditorApplication.RepaintProjectWindow(); }
        if (GUILayout.Button("Reset Unity 6 defaults"))
        { EditorPrefs.DeleteKey("Journey.FolderIconOffsetX"); EditorPrefs.DeleteKey("Journey.FolderIconSize"); EditorApplication.RepaintProjectWindow(); }
    }
}
#endif
