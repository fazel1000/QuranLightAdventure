#if UNITY_EDITOR
using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class JourneyMinimapQuestSetup
{
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static Transform Named(Scene scene, string name)
    {
        var matches = All<Transform>(scene).Where(t => string.Equals(Regex.Replace(t.name.Trim(), @"\s+", " "), Regex.Replace(name.Trim(), @"\s+", " "), StringComparison.Ordinal)).ToArray();
        if (matches.Length == 1) return matches[0];
        Debug.LogWarning("Quest guide: expected one '" + name + "', found " + matches.Length + ". Assign its Location/reference manually.");
        return null;
    }
    private static void Voice(Scene scene, JourneyMinimapQuestGuide.VoiceGoal goal, string name, string locationName = null)
    {
        if (goal.board == null)
        {
            var root = Named(scene, name);
            var boards = root != null ? root.GetComponentsInChildren<JourneyVoiceBoard>(true).Where(b => b.enabled).ToArray()
                : Array.Empty<JourneyVoiceBoard>();
            if (boards.Length == 1) goal.board = boards[0];
            else Debug.LogWarning("Quest guide: assign the enabled Voice Board for " + name + ".");
        }
        if (goal.location == null)
            goal.location = locationName != null ? Named(scene, locationName) : goal.board != null ? goal.board.transform : null;
    }
    private static void Card(Scene scene, JourneyMinimapQuestGuide.CardGoal goal, string name)
    {
        if (goal.location != null && goal.puzzle != null) return;
        var root = Named(scene, name);
        if (goal.location == null) goal.location = root;
        if (goal.puzzle != null || root == null) return;
        var puzzles = root.GetComponentsInChildren<CardGameManager>(true);
        if (puzzles.Length == 1) goal.puzzle = puzzles[0];
        else Debug.LogWarning("Quest guide: assign the Card Game Manager inside " + name + ".");
    }
    private static T Unique<T>(Scene scene) where T : Component
    {
        var found = All<T>(scene);
        if (found.Length == 1) return found[0];
        Debug.LogWarning("Quest guide: assign " + typeof(T).Name + " manually; found " + found.Length + " in the active scene.");
        return null;
    }
    [MenuItem("Quran Kids/Setup Minimap Quest Guide")]
    public static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("Exit Play Mode before setup."); return; }
        var scene = SceneManager.GetActiveScene();
        var maps = All<JourneyMinimap>(scene);
        if (maps.Length != 1) { Debug.LogError("The active scene must contain exactly one JourneyMinimap."); return; }
        var map = maps[0];
        var guide = map.GetComponent<JourneyMinimapQuestGuide>();
        if (guide == null) guide = Undo.AddComponent<JourneyMinimapQuestGuide>(map.gameObject);
        Undo.RecordObject(guide, "Connect minimap quest guide");
        guide.minimap = map;
        Voice(scene, guide.buttonsBoard, "ButtonsBoard");
        Voice(scene, guide.buttonsBoard1, "ButtonsBoard  (1)");
        Voice(scene, guide.buttonsBoard2, "ButtonsBoard  (2)");
        Voice(scene, guide.buttonsBoard3, "ButtonsBoard  (3)");
        Voice(scene, guide.buttonsBoard4, "ButtonsBoard  (4)");
        Voice(scene, guide.hamd, "Hamd Board", "Hamd Canvas");
        Voice(scene, guide.falagh, "Falagh Board");
        Voice(scene, guide.naas, "Naas Board");
        Card(scene, guide.matchingCardPack1, "Matching Card Pack 1");
        Card(scene, guide.matchingCardPack2, "Matching Card Pack 2");
        Card(scene, guide.matchingCardPack3, "Matching Card Pack 3");
        Card(scene, guide.matchingCardPack4, "Matching Card Pack 4");
        if (guide.letterPuzzles == null) guide.letterPuzzles = Unique<LightBrushPuzzle>(scene);
        if (guide.stoneTarget == null) guide.stoneTarget = Unique<JourneyStoneTower>(scene);
        if (guide.stoneLocation == null)
        {
            var thrower = Unique<JourneyStoneThrowController>(scene);
            if (thrower != null)
            {
                // The scene also has an AudioSource named Stones. Use the actual pickup root.
                var data = new SerializedObject(thrower);
                guide.stoneLocation = data.FindProperty("stonesRoot")?.objectReferenceValue as Transform;
            }
        }
        if (guide.droneTargets == null || guide.droneTargets.Length == 0) guide.droneTargets = All<JourneyDroneTarget>(scene);
        if (guide.droneLocation == null)
        {
            var launchers = All<JourneyDroneLauncher>(scene);
            // A convenient initial point; the designer can drag any dedicated region marker here.
            if (launchers.Length > 0) guide.droneLocation = launchers.OrderBy(l => l.name, StringComparer.Ordinal).First().transform;
        }
        if (guide.alHamdolellahLocation == null) guide.alHamdolellahLocation = Named(scene, "Al Hamdolellah");
        if (guide.salamAlaykomLocation == null) guide.salamAlaykomLocation = Named(scene, "Salam Alaykom");
        EditorUtility.SetDirty(guide); PrefabUtility.RecordPrefabInstancePropertyModifications(guide);
        EditorSceneManager.MarkSceneDirty(scene); Selection.activeGameObject = guide.gameObject;
        Debug.Log("Quest guide added. Assign Objective Sprite, review every Location and activity reference, then save the scene. Existing assignments were preserved.", guide);
    }
}
#endif
