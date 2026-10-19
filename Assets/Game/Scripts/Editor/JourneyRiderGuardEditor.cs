#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(JourneyRiderGuard))]
public sealed class JourneyRiderGuardEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var guard = (JourneyRiderGuard)target;
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("Edit each Patrol Points entry: point, wait, roar and run. Duplicate/prefab the whole static patrol group, including its points.", MessageType.Info);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || EditorUtility.IsPersistent(guard)))
        {
            if (GUILayout.Button("Add Patrol Point")) JourneyRiderGuardSetup.AddPoint(guard);
            if (GUILayout.Button("Make Route Unique For This Rider")) JourneyRiderGuardSetup.MakeRouteUnique(guard);
        }
        if (Application.isPlaying) EditorGUILayout.LabelField("Distance state", guard.IsSleeping ? "Sleeping" : "Near player");
    }
    private void OnSceneGUI()
    {
        var guard = (JourneyRiderGuard)target;
        if (guard.patrolPoints == null) return;
        for (int i = 0; i < guard.patrolPoints.Count; i++)
        {
            var entry = guard.patrolPoints[i]; if (entry == null || entry.point == null) continue;
            Handles.Label(entry.point.position + Vector3.up, "Point " + (i + 1));
            EditorGUI.BeginChangeCheck();
            Vector3 position = Handles.PositionHandle(entry.point.position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(entry.point, "Move patrol point"); entry.point.position = position;
                PrefabUtility.RecordPrefabInstancePropertyModifications(entry.point);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(entry.point.gameObject.scene);
            }
        }
    }
}
#endif
