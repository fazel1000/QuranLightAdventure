#if UNITY_EDITOR
using System;
using System.Linq;
using GameCreator.Runtime.Characters;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class JourneyStoneMusicSetup
{
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    [MenuItem("Quran Kids/Setup Stone Bonus and Music Zones")]
    private static void Setup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { Debug.LogError("Exit Play Mode before setting up stone bonuses and music zones."); return; }
        Scene scene = SceneManager.GetActiveScene();
        var music = All<JourneyShuffleMusic>(scene).FirstOrDefault();
        var pilots = All<JourneyDronePilot>(scene);
        var player = All<Character>(scene).FirstOrDefault(character => character.IsPlayer);
        var tower = All<JourneyStoneTower>(scene).FirstOrDefault(target => target.name.Equals("Sheytan", StringComparison.OrdinalIgnoreCase));
        if (tower == null) tower = All<JourneyStoneTower>(scene).FirstOrDefault();
        var sphere = tower != null ? tower.GetComponent<SphereCollider>() : null;
        if (sphere == null && tower != null)
            sphere = tower.GetComponentsInChildren<SphereCollider>(true).FirstOrDefault(candidate => candidate.GetComponentInParent<JourneyStoneTower>() == tower);
        var sign = All<Transform>(scene).FirstOrDefault(candidate => candidate.name.Trim() == "Sign 1 Side" &&
            candidate.GetComponentsInChildren<AudioSource>(true).Any(source => source.name.Trim() == "Minab"));
        if (tower == null || sphere == null || music == null || player == null || sign == null || pilots.Length == 0)
        {
            Debug.LogError("Setup needs JourneyStoneTower with a SphereCollider, JourneyShuffleMusic, JourneyDronePilot, Player, and Sign 1 Side with its Minab AudioSource in the active scene.");
            return;
        }
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Set up triple stone score and music control");
        Undo.RecordObject(tower, "Connect the statue bonus sphere");
        var targetData = new SerializedObject(tower);
        targetData.FindProperty("bonusHitSphere").objectReferenceValue = sphere;
        targetData.FindProperty("bonusHitMultiplier").intValue = 3;
        targetData.ApplyModifiedProperties();
        Undo.RecordObject(sphere, "Use the bonus sphere as a trigger"); sphere.enabled = true; sphere.isTrigger = true;
        foreach (var pilot in pilots)
        {
            Undo.RecordObject(pilot, "Pause gameplay music while following the drone");
            pilot.gameplayMusic = music; JourneyWorldSetupUtility.Dirty(pilot);
        }
        var zone = sign.GetComponent<JourneyMusicProximityZone>();
        bool fresh = zone == null;
        if (fresh) zone = Undo.AddComponent<JourneyMusicProximityZone>(sign.gameObject);
        Undo.RecordObject(zone, "Configure Minab proximity music fade");
        zone.music = music; zone.listener = player.transform; zone.distanceOrigin = sign;
        if (fresh) { zone.fadeStartDistance = 30f; zone.silentDistance = 10f; zone.fadeSeconds = 0.5f; }
        if (music.musicSource == null)
        {
            Undo.RecordObject(music, "Connect gameplay music source"); music.musicSource = music.GetComponent<AudioSource>();
            JourneyWorldSetupUtility.Dirty(music);
        }
        JourneyWorldSetupUtility.Dirty(tower); JourneyWorldSetupUtility.Dirty(sphere); JourneyWorldSetupUtility.Dirty(zone);
        EditorSceneManager.MarkSceneDirty(scene); Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = sign.gameObject;
        Debug.Log("Stone/music setup COMPLETE: statue sphere is a trigger with x3 total hit reward; drone-camera music pauses and resumes; Sign 1 Side fades music from 30m to silence at 10m. Save the scene, then run Validate Stone Bonus and Music Zones.", zone);
    }

    [MenuItem("Quran Kids/Validate Stone Bonus and Music Zones")]
    private static void Validate()
    {
        Scene scene = SceneManager.GetActiveScene(); int errors = 0;
        var towers = All<JourneyStoneTower>(scene);
        if (towers.Length == 0) { Debug.LogError("JourneyStoneTower is missing from the active scene."); errors++; }
        foreach (var tower in towers)
        {
            if (tower.BonusHitSphere == null || !tower.BonusHitSphere.enabled || !tower.BonusHitSphere.isTrigger ||
                tower.BonusHitSphere.GetComponentInParent<JourneyStoneTower>() != tower)
            { Debug.LogError("Statue needs its own enabled bonus SphereCollider with Is Trigger on. Run Setup Stone Bonus and Music Zones.", tower); errors++; }
            if (!tower.GetComponentsInChildren<Collider>(true).Any(collider => collider.enabled && !collider.isTrigger))
            { Debug.LogError("Keep the statue's non-trigger solid collider so stones can bounce naturally.", tower); errors++; }
        }
        var zones = All<JourneyMusicProximityZone>(scene);
        if (zones.Length == 0) { Debug.LogError("Minab music proximity zone is missing. Run Setup Stone Bonus and Music Zones."); errors++; }
        foreach (var zone in zones)
            if (zone.music == null || zone.listener == null || zone.distanceOrigin == null || zone.silentDistance < 0f || zone.fadeStartDistance <= zone.silentDistance)
            { Debug.LogError("Proximity zone needs Music, Listener, Distance Origin and Fade Start Distance greater than Silent Distance.", zone); errors++; }
        var pilots = All<JourneyDronePilot>(scene);
        if (pilots.Length == 0) { Debug.LogError("JourneyDronePilot is missing from the active scene."); errors++; }
        foreach (var pilot in pilots)
            if (pilot.gameplayMusic == null) { Debug.LogError("Drone pilot needs Gameplay Music.", pilot); errors++; }
        var musics = All<JourneyShuffleMusic>(scene);
        if (musics.Length == 0) { Debug.LogError("JourneyShuffleMusic is missing from the active scene."); errors++; }
        foreach (var music in musics)
            if (music.musicSource == null) { Debug.LogError("Gameplay music needs its AudioSource.", music); errors++; }
        if (errors == 0) Debug.Log("Stone/music references OK. Check normal vs bonus stone hits, drone impact/abort, user pause and volume, and walking through 30m/10m in Play and on device.");
        else Debug.LogError("Stone/music validation: " + errors + " errors.");
    }
}
#endif
