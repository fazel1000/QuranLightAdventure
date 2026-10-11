#if UNITY_EDITOR
using System;
using System.Linq;
using GameCreator.Runtime.Characters;
using GameCreator.Runtime.Cameras;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class JourneyDroneSetup : EditorWindow
{
    private GameObject launcherRoot, explosionPrefab, smokePrefab;
    private JourneyVehicleDriver vehicleDriver;
    private LightBrushPuzzle wallet;
    private Sprite launchSprite, coinSprite;
    private Vector2 scroll;
    [MenuItem("Quran Kids/Setup Journey Drone (Arcade)")]
    private static void Open() { GetWindow<JourneyDroneSetup>("Journey Drone").Scan(); }
    private static Transform[] SceneTransforms() => UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        .Where(t => t.gameObject.scene.IsValid() && t.gameObject.scene.isLoaded).ToArray();
    private static GameObject Named(string name) => SceneTransforms().FirstOrDefault(t => t.name == name)?.gameObject;
    private static T Find<T>() where T : UnityEngine.Object => UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
    private static T Field<T>(string label, T value) where T : UnityEngine.Object => (T)EditorGUILayout.ObjectField(label, value, typeof(T), true);
    private static GameObject EffectAsset(string name)
    {
        foreach (var t in SceneTransforms().Where(t => t.name == name))
        {
            var asset = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            if (asset != null) return asset;
        }
        return null;
    }
    private void Scan()
    {
        var selected = Selection.activeGameObject;
        launcherRoot = selected != null && selected.transform.Find("Drone Root") != null ? selected :
            SceneTransforms().FirstOrDefault(t => t.Find("Drone Root") != null && t.Find("Booster Root") != null)?.gameObject;
        vehicleDriver = Find<JourneyVehicleDriver>(); wallet = Find<LightBrushPuzzle>();
        explosionPrefab = EffectAsset("Collision Explosion"); smokePrefab = EffectAsset("Collision Smoke");
    }
    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox("Arcade game setup. Adds physics to the selected launcher, a separate launch button, one pilot manager, and target components to aircraft under the three groups. Existing vehicle setup is preserved. Set RailEnd in Scene after installation.", MessageType.Info);
        if (GUILayout.Button("Find scene references")) Scan();
        launcherRoot = Field("Launcher root", launcherRoot);
        vehicleDriver = Field("Existing vehicle driver", vehicleDriver);
        wallet = Field("Existing coin receiver", wallet);
        explosionPrefab = Field("Collision explosion prefab", explosionPrefab);
        smokePrefab = Field("Target smoke prefab", smokePrefab);
        launchSprite = Field("Launch button sprite (optional)", launchSprite);
        coinSprite = Field("Coin icon sprite (optional)", coinSprite);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Create and Connect"))
                try { Build(); } catch (Exception ex) { Debug.LogException(ex); EditorUtility.DisplayDialog("Drone setup", ex.Message, "OK"); }
        }
        EditorGUILayout.EndScrollView();
    }
    private static T Add<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? Undo.AddComponent<T>(go);
    private static Transform Child(Transform parent, string name, Vector3 localPosition)
    {
        var existing = parent.Find(name); if (existing != null) return existing;
        var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Create drone helper");
        go.transform.SetParent(parent, false); go.transform.localPosition = localPosition;
        return go.transform;
    }
    private static void ConfigureBody(Rigidbody body)
    {
        Undo.RecordObject(body, "Configure drone body");
        body.isKinematic = true; body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.detectCollisions = true; body.constraints = RigidbodyConstraints.None;
        EditorUtility.SetDirty(body);
    }
    private void Build()
    {
        if (launcherRoot == null || !launcherRoot.scene.IsValid()) throw new InvalidOperationException("Select the launcher instance in the scene.");
        if (vehicleDriver == null || vehicleDriver.player == null || vehicleDriver.menu == null ||
            vehicleDriver.viewCamera == null || vehicleDriver.enterButton == null || wallet == null)
            throw new InvalidOperationException("Assign the existing vehicle driver with Player, Menu, Camera and Enter Button, and the LightBrush coin receiver.");
        var root = launcherRoot.transform;
        var drone = root.Find("Drone Root"); var booster = root.Find("Booster Root");
        var rail = root.Find("Thrown Rail Root"); var holder = root.Find("Holder Base");
        if (drone == null || booster == null || rail == null || holder == null)
            throw new InvalidOperationException("Root must have Drone Root, Booster Root, Thrown Rail Root and Holder Base as direct children.");
        if (root.GetComponent<Rigidbody>() != null)
            throw new InvalidOperationException("Remove the Rigidbody from the entire launcher root; only Drone Root and Booster Root need one.");
        if (drone.GetComponentsInChildren<Rigidbody>(true).Any(b => b.transform != drone))
            throw new InvalidOperationException("Remove nested Rigidbodies from Drone Root's children before setup.");
        if (booster.GetComponentsInChildren<Rigidbody>(true).Any(b => b.transform != booster))
            throw new InvalidOperationException("Remove nested Rigidbodies from Booster Root's children before setup.");
        var droneColliders = drone.GetComponentsInChildren<Collider>(true);
        if (droneColliders.Length == 0) throw new InvalidOperationException("Keep a convex MeshCollider on aerofoil, or add a fitted BoxCollider under Drone Root.");
        foreach (var collider in droneColliders)
        {
            if (collider is MeshCollider mesh && (!mesh.convex || mesh.sharedMesh == null))
                throw new InvalidOperationException("Drone mesh colliders must have a mesh and Convex enabled.");
            Undo.RecordObject(collider, "Enable drone collider"); collider.enabled = true; collider.isTrigger = false;
        }
        if (booster.GetComponent<Collider>() == null)
            throw new InvalidOperationException("Keep the fitted BoxCollider on Booster Root.");
        var audio = drone.GetComponentInChildren<AudioSource>(true);
        if (audio == null)
            audio = Add<AudioSource>(Child(drone, "DroneAudio", Vector3.zero).gameObject);
        if (audio.transform == drone)
            throw new InvalidOperationException("Move the AudioSource to its own child of Drone Root so impact audio can survive the drone's removal.");
        Undo.RecordObject(audio, "Configure drone audio"); audio.playOnAwake = false; audio.spatialBlend = 1f;
        audio.minDistance = 3f; audio.maxDistance = 80f;
        var launcher = Add<JourneyDroneLauncher>(launcherRoot);
        Undo.RecordObject(launcher, "Connect drone launcher");
        launcher.droneRoot = drone; launcher.boosterRoot = booster; launcher.thrownRailRoot = rail; launcher.holderBase = holder;
        launcher.droneBody = Add<Rigidbody>(drone.gameObject); launcher.boosterBody = Add<Rigidbody>(booster.gameObject);
        ConfigureBody(launcher.droneBody); ConfigureBody(launcher.boosterBody);
        var railEnd = root.Find("RailEnd");
        if (railEnd == null) railEnd = Child(root, "RailEnd", root.InverseTransformPoint(drone.position + launcher.Forward.normalized * 3f));
        launcher.railEnd = railEnd; launcher.audioSource = audio;
        launcher.boosterFlame = booster.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Booster Flame")?.gameObject;
        launcher.engineSmokePoint = Child(drone, "EngineSmokePoint", launcher.launchAlongPositiveX ? Vector3.left : Vector3.right);
        if (launcher.boosterFlame != null) { Undo.RecordObject(launcher.boosterFlame, "Disable idle flame"); launcher.boosterFlame.SetActive(false); }
        if (explosionPrefab != null) launcher.impactExplosionPrefab = explosionPrefab;
        if (smokePrefab != null) launcher.targetSmokePrefab = smokePrefab;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        { Undo.RecordObject(t.gameObject, "Clear static flags"); GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0); }

        var pilot = Find<JourneyDronePilot>();
        if (pilot == null)
        {
            var go = new GameObject("Journey Drone Pilot"); Undo.RegisterCreatedObjectUndo(go, "Create drone pilot");
            pilot = Undo.AddComponent<JourneyDronePilot>(go);
        }
        Undo.RecordObject(pilot, "Connect drone pilot");
        pilot.player = vehicleDriver.player; pilot.menu = vehicleDriver.menu;
        pilot.vehicleDriver = vehicleDriver; pilot.viewCamera = vehicleDriver.viewCamera;
        pilot.gameCreatorCamera = vehicleDriver.gameCreatorCamera; pilot.coinReceiver = wallet;
        pilot.suspendWhileFlying = vehicleDriver.suspendWhileDriving != null ? vehicleDriver.suspendWhileDriving.ToArray() : new Behaviour[0];
        pilot.onFootUI = vehicleDriver.onFootUI != null ? vehicleDriver.onFootUI.ToArray() : new GameObject[0];
        if (launchSprite != null) pilot.launchSprite = launchSprite;
        if (pilot.launchButton == null)
        {
            var template = vehicleDriver.enterButton;
            var go = Instantiate(template.gameObject, template.transform.parent);
            go.name = "LaunchDroneButton"; Undo.RegisterCreatedObjectUndo(go, "Create launch button");
            var button = go.GetComponent<Button>(); button.onClick = new Button.ButtonClickedEvent(); button.interactable = true;
            foreach (var pedal in go.GetComponents<JourneyVehiclePedal>()) Undo.DestroyObjectImmediate(pedal);
            foreach (var label in go.GetComponentsInChildren<TMP_Text>(true)) label.text = "پرتاب";
            foreach (var label in go.GetComponentsInChildren<Text>(true)) label.text = "Launch";
            var rect = go.GetComponent<RectTransform>(); rect.anchoredPosition += Vector2.up * (rect.sizeDelta.y + 24f);
            pilot.launchButton = button; go.SetActive(false);
        }
        int targets = 0;
        foreach (string name in new[] { "F 35 Group", "B 2 - Spirit Group", "F 15 Japan Group" })
        {
            GameObject group = Named(name); if (group == null) continue;
            foreach (Transform aircraft in group.transform)
            {
                if (aircraft.GetComponentsInChildren<Collider>(true).Length == 0) continue;
                var target = Add<JourneyDroneTarget>(aircraft.gameObject);
                Undo.RecordObject(target, "Connect drone target");
                if (smokePrefab != null) target.smokePrefab = smokePrefab;
                EditorUtility.SetDirty(target); PrefabUtility.RecordPrefabInstancePropertyModifications(target); targets++;
            }
        }
        if (coinSprite != null) ConnectCoinIcon();
        // The scene contains preview effects; only stop the specifically named preview instances.
        foreach (var t in SceneTransforms().Where(t => t.name == "Collision Smoke" || t.name == "Collision Explosion"))
        { Undo.RecordObject(t.gameObject, "Disable preview effect"); t.gameObject.SetActive(false); }
        EditorUtility.SetDirty(launcher); EditorUtility.SetDirty(pilot);
        PrefabUtility.RecordPrefabInstancePropertyModifications(launcher);
        EditorSceneManager.MarkSceneDirty(launcherRoot.scene);
        Selection.activeGameObject = launcherRoot;
        Debug.Log("Drone setup connected " + targets + " aircraft. Set RailEnd at the end-of-travel position of Drone Root's pivot. Assign sounds and engine smoke, then save and test in Unity.", launcher);
    }
    private void ConnectCoinIcon()
    {
        var serialized = new SerializedObject(wallet);
        var textProperty = serialized.FindProperty("coinCountText");
        var text = textProperty.objectReferenceValue as Component;
        if (text == null) throw new InvalidOperationException("Assign Coin Count Text on LightBrush first.");
        var imageProperty = serialized.FindProperty("coinImage");
        var image = imageProperty.objectReferenceValue as Image;
        if (image == null)
        {
            var go = new GameObject("Coin Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, "Create coin icon"); go.transform.SetParent(text.transform.parent, false);
            image = go.GetComponent<Image>();
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f); rect.anchoredPosition = new Vector2(-20f, 0f); rect.sizeDelta = new Vector2(64f, 64f);
        }
        Undo.RecordObject(image, "Connect coin sprite"); image.sprite = coinSprite; image.preserveAspect = true; image.raycastTarget = false;
        imageProperty.objectReferenceValue = image; serialized.FindProperty("coinSprite").objectReferenceValue = coinSprite;
        serialized.ApplyModifiedProperties(); wallet.RefreshScoreDisplay();
    }
}
#endif
