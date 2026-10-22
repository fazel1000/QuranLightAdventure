#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using GameCreator.Runtime.Characters;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class JourneyAirWaterWorldSetup
{
    [MenuItem("Quran Kids/Setup Air Water and World Systems")]
    private static void Setup()
    {
        if (EditorApplication.isPlaying) return;
        Scene scene = SceneManager.GetActiveScene();
        var menu = All<JourneyMenuController>(scene).FirstOrDefault();
        var car = All<JourneyVehicleDriver>(scene).FirstOrDefault();
        Character player = car != null ? car.player : All<Character>(scene).FirstOrDefault(c => c.IsPlayer);
        if (menu == null || player == null) { Debug.LogError("Open First Grade of Elementary School with its Player and JourneyMenuController."); return; }
        var menuData = new SerializedObject(menu);
        var main = menuData.FindProperty("mainMenuPanel").objectReferenceValue as GameObject;
        var canvas = main != null ? main.GetComponentInParent<Canvas>(true) : null;
        if (canvas == null) { Debug.LogError("The menu must belong to the gameplay Canvas."); return; }
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Set up air/water and world systems");
        ConfigurePlayer(scene, player);
        var manager = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "Journey World Systems");
        if (manager == null) { manager = new GameObject("Journey World Systems"); Undo.RegisterCreatedObjectUndo(manager, "Create world manager"); SceneManager.MoveGameObjectToScene(manager, scene); }
        Transform water = Named(scene, "Water Plane");
        Transform helicopter = All<Transform>(scene).FirstOrDefault(t => t.name.Trim() == "Helicopter Police" && Find(t, "Main Rotor Blades") != null);
        Transform submarine = All<Transform>(scene).FirstOrDefault(t => t.name.Trim() == "Submarine" && Find(t, "SubmarineSeat") != null);
        if (helicopter != null) ConfigureVehicle(helicopter, false, menu, water); else Debug.LogWarning("Helicopter Police with Main Rotor Blades was not found.");
        if (submarine != null) ConfigureVehicle(submarine, true, menu, water); else Debug.LogWarning("Submarine with SubmarineSeat was not found.");
        bool newPilot = manager.GetComponent<JourneyAirWaterPilot>() == null;
        var pilot = JourneyWorldSetupUtility.Add<JourneyAirWaterPilot>(manager);
        Undo.RecordObject(pilot, "Connect flight UI"); pilot.player = player; pilot.menu = menu;
        pilot.viewCamera = car != null ? car.viewCamera : Camera.main;
        pilot.gameCreatorCamera = car != null ? car.gameCreatorCamera : All<GameCreator.Runtime.Cameras.TCamera>(scene).FirstOrDefault();
        ConnectControls(pilot.helicopter, Named(scene, "Helicopter Buttons"), false, newPilot);
        ConnectControls(pilot.submarine, Named(scene, "Submarine Buttons"), true, newPilot);
        var radial = ConfigureRadial(scene, canvas.rootCanvas.transform, menu);
        var jump = All<JourneyChargedJump>(scene).FirstOrDefault();
        var foot = new HashSet<GameObject>(car != null && car.onFootUI != null ? car.onFootUI.Where(go => go != null) : Enumerable.Empty<GameObject>());
        if (jump != null) foot.Add(jump.gameObject); if (radial != null) foot.Add(radial.gameObject);
        if (car != null && car.enterButton != null) foot.Add(car.enterButton.gameObject);
        var drone = All<JourneyDronePilot>(scene).FirstOrDefault(); if (drone != null && drone.launchButton != null) foot.Add(drone.launchButton.gameObject);
        Transform playerButtons = Named(scene, "Player Buttons");
        if (playerButtons != null) foreach (var button in playerButtons.GetComponentsInChildren<Button>(true)) foot.Add(button.gameObject);
        pilot.onFootUI = foot.ToArray();
        var suspend = new HashSet<Behaviour>(car != null && car.suspendWhileDriving != null ? car.suspendWhileDriving.Where(b => b != null) : Enumerable.Empty<Behaviour>());
        if (jump != null) suspend.Add(jump);
        pilot.suspendWhileRiding = suspend.ToArray();
        // Existing car/drone snapshots must also hide the new on-foot actions.
        if (car != null) { Undo.RecordObject(car, "Include new on-foot controls"); car.onFootUI = Union(car.onFootUI, jump != null ? jump.gameObject : null, radial != null ? radial.gameObject : null); JourneyWorldSetupUtility.Dirty(car); }
        if (drone != null) { Undo.RecordObject(drone, "Include new on-foot controls"); drone.onFootUI = Union(drone.onFootUI, jump != null ? jump.gameObject : null, radial != null ? radial.gameObject : null); JourneyWorldSetupUtility.Dirty(drone); }
        ConfigureMinimap(scene, manager, canvas.rootCanvas.transform, menu, player);
        if (newPilot)
        {
            var map = manager.GetComponent<JourneyMinimap>();
            if (map != null && map.rootUI != null)
            {
                ReserveMapCorner((RectTransform)canvas.rootCanvas.transform, (RectTransform)map.rootUI.transform,
                    foot.Select(go => go.GetComponent<Button>()).Where(button => button != null).Select(button => (RectTransform)button.transform));
                if (car != null) ReserveMapCorner((RectTransform)canvas.rootCanvas.transform, (RectTransform)map.rootUI.transform,
                    new Component[] { car.gas, car.brake, car.nitro, car.exitButton }.Where(c => c != null).Select(c => (RectTransform)c.transform));
            }
        }
        var rain = JourneyWorldSetupUtility.Add<JourneyRainCycle>(manager);
        Undo.RecordObject(rain, "Connect rain cycle"); rain.menu = menu; rain.followTarget = pilot.viewCamera != null ? pilot.viewCamera.transform : player.transform; rain.waterSurface = water;
        JourneyWorldSetupUtility.Dirty(rain); JourneyWorldSetupUtility.Dirty(pilot);
        EditorSceneManager.MarkSceneDirty(scene); Undo.CollapseUndoOperations(group); AssetDatabase.SaveAssets(); Selection.activeGameObject = manager;
        Debug.Log("Connected player animation, helicopter, submarine, rain, minimap and radial menu. Assign vehicle sound clips, Rain Effect Prefab and radial item icons/actions; run Bake Offline Minimap, then save the scene.", manager);
    }
    private static IEnumerable<T> All<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<T>(true));
    private static Transform Named(Scene scene, string name) => All<Transform>(scene).FirstOrDefault(t => string.Equals(t.name.Trim(), name, StringComparison.OrdinalIgnoreCase));
    private static Transform Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => string.Equals(t.name.Trim(), name, StringComparison.OrdinalIgnoreCase));
    private static GameObject[] Union(GameObject[] source, params GameObject[] extra) => (source ?? Array.Empty<GameObject>()).Concat(extra).Where(go => go != null).Distinct().ToArray();
    private static bool HasState(AnimatorStateMachine machine, string name) => machine.states.Any(s => s.state.name == name) || machine.stateMachines.Any(s => HasState(s.stateMachine, name));
    private static bool GameplayController(AnimatorController controller) => controller != null && controller.layers.Any(l => l.name == JourneyPlayerCombat.LayerName && HasState(l.stateMachine, "SwordAttack") && HasState(l.stateMachine, "ShieldBlock")) && controller.parameters.Any(p => p.name == "Grounded");
    private static void ConfigurePlayer(Scene scene, Character player)
    {
        var animator = player.Animim.Animator;
        if (animator == null || !animator.isHuman) { Debug.LogError("Player model must have a valid Humanoid Animator.", player); return; }
        var binder = JourneyWorldSetupUtility.Add<JourneyPlayerModelBindings>(player.gameObject);
        Undo.RecordObject(binder, "Keep gameplay animation after model changes");
        var controller = binder.gameplayController as AnimatorController;
        if (!GameplayController(controller)) controller = animator.runtimeAnimatorController as AnimatorController;
        if (!GameplayController(controller)) controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Game/Animations/Combat/Generated/Player_With_Shield 3.controller");
        if (!GameplayController(controller)) controller = AssetDatabase.FindAssets("t:AnimatorController", new[] { "Assets/Game" }).Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<AnimatorController>).FirstOrDefault(GameplayController);
        if (controller == null) { Debug.LogError("A gameplay controller containing locomotion, SwordAttack and ShieldBlock is required.", player); return; }
        Undo.RecordObject(animator, "Restore gameplay controller"); animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var combat = player.GetComponent<JourneyPlayerCombat>(); var stone = All<JourneyStoneThrowController>(scene).FirstOrDefault();
        binder.player = player; binder.gameplayController = controller; binder.combat = combat; binder.stone = stone;
        Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand), left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (right != null)
        {
            binder.handSocket = stone != null && stone.HandSocket != null ? stone.HandSocket : JourneyWorldSetupUtility.Child(right, "HandSocket").transform;
            binder.throwOrigin = stone != null && stone.ThrowOrigin != null ? stone.ThrowOrigin : JourneyWorldSetupUtility.Child(right, "ThrowOrigin").transform;
            if (stone != null)
            {
                var data = new SerializedObject(stone); data.FindProperty("handSocket").objectReferenceValue = binder.handSocket;
                data.FindProperty("throwOrigin").objectReferenceValue = binder.throwOrigin; data.FindProperty("animator").objectReferenceValue = animator;
                data.ApplyModifiedProperties(); JourneyWorldSetupUtility.Dirty(stone);
            }
        }
        if (combat != null)
        {
            Undo.RecordObject(combat, "Restore combat model references"); combat.playerAnimator = animator; combat.player = player;
            if (left != null && combat.shieldHitbox == null)
            {
                var shield = JourneyWorldSetupUtility.Child(left, "ShieldHitbox"); var box = JourneyWorldSetupUtility.Add<BoxCollider>(shield);
                box.size = new Vector3(0.5f, 0.65f, 0.12f); box.isTrigger = true; shield.transform.localPosition = new Vector3(0f, 0f, 0.08f); shield.layer = player.gameObject.layer;
                combat.shieldHitbox = box;
            }
            binder.shieldHitbox = combat.shieldHitbox;
            if (combat.bodyHitbox == null || combat.bodyHitbox.transform.IsChildOf(animator.transform))
            {
                var body = JourneyWorldSetupUtility.Child(player.transform, "CombatBodyHitbox"); var capsule = JourneyWorldSetupUtility.Add<CapsuleCollider>(body);
                capsule.height = 1.65f; capsule.radius = 0.3f; capsule.isTrigger = true; body.layer = player.gameObject.layer; combat.bodyHitbox = capsule;
            }
            JourneyWorldSetupUtility.Dirty(combat);
        }
        var appearance = JourneyWorldSetupUtility.Add<JourneyWardrobeAppearance>(animator.gameObject);
        Undo.RecordObject(appearance, "Preserve Boy Character 2's authored outfit"); appearance.preserveAuthoredOutfitUntilChoice = true;
        foreach (var room in All<JourneyWardrobeRoom>(scene))
        {
            Undo.RecordObject(room, "Keep selected scene model and gameplay controller"); room.gameplayAnimatorController = controller;
            room.useHodaartForPlayer = false; room.combat = combat; room.stoneController = stone; JourneyWorldSetupUtility.Dirty(room);
        }
        JourneyWorldSetupUtility.Dirty(appearance); JourneyWorldSetupUtility.Dirty(animator); JourneyWorldSetupUtility.Dirty(binder);
    }
    private static void ConfigureVehicle(Transform root, bool submarine, JourneyMenuController menu, Transform water)
    {
        bool fresh = root.GetComponent<JourneyAirWaterVehicle>() == null;
        var vehicle = JourneyWorldSetupUtility.Add<JourneyAirWaterVehicle>(root.gameObject); var body = JourneyWorldSetupUtility.Add<Rigidbody>(root.gameObject);
        Undo.RecordObjects(new Object[] { vehicle, body }, "Configure vehicle physics");
        vehicle.kind = submarine ? JourneyAirWaterVehicle.VehicleKind.Submarine : JourneyAirWaterVehicle.VehicleKind.Helicopter;
        vehicle.menu = menu; vehicle.driverSeat = Find(root, submarine ? "SubmarineSeat" : "DriverSeat"); vehicle.exitPoint = Find(root, "ExitPoint"); vehicle.waterSurface = water;
        if (fresh)
        {
            vehicle.localForward = submarine ? Vector3.forward : Vector3.back;
            vehicle.horizontalSpeed = submarine ? 9f : 16f; vehicle.verticalSpeed = submarine ? 3f : 5f;
            vehicle.cameraDistance = submarine ? 12f : 10f; vehicle.cameraHeight = submarine ? 3f : 4f;
            body.mass = submarine ? 2500f : 1200f; body.linearDamping = 0.2f; body.angularDamping = 2f;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ; body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }
        bool newHull = root.GetComponent<BoxCollider>() == null;
        vehicle.hull = JourneyWorldSetupUtility.Add<BoxCollider>(root.gameObject);
        if (newHull) JourneyWorldSetupUtility.FitHull(vehicle.hull, submarine ? root : Find(root, "Body") ?? root);
        vehicle.hull.isTrigger = false;
        if (vehicle.cameraTarget == null)
        {
            vehicle.cameraTarget = JourneyWorldSetupUtility.Child(root, "Vehicle Camera Target").transform;
            vehicle.cameraTarget.localPosition = vehicle.hull.center + Vector3.up * 0.5f;
        }
        if (!submarine)
        {
            vehicle.mainRotor = Find(root, "Main Rotor Blades"); vehicle.tailRotor = Find(root, "Tail Rotor Blades");
            vehicle.visualRoot = JourneyWorldSetupUtility.Child(root, "Flight Visual").transform;
            foreach (var part in new[] { Find(root, "Body"), vehicle.mainRotor, vehicle.tailRotor })
                if (part != null && part.parent != vehicle.visualRoot) Undo.SetTransformParent(part, vehicle.visualRoot, "Tilt helicopter visuals together");
        }
        if (vehicle.engineSource == null)
            vehicle.engineSource = root.GetComponentsInChildren<AudioSource>(true).FirstOrDefault(source => source != vehicle.effectsSource) ?? JourneyWorldSetupUtility.Add<AudioSource>(JourneyWorldSetupUtility.Child(root, submarine ? "Submarine" : "Helicopter Engine"));
        if (vehicle.effectsSource == null || vehicle.effectsSource == vehicle.engineSource) vehicle.effectsSource = JourneyWorldSetupUtility.Add<AudioSource>(JourneyWorldSetupUtility.Child(root, "Vehicle Effects Audio"));
        foreach (var audio in new[] { vehicle.engineSource, vehicle.effectsSource })
        { Undo.RecordObject(audio, "Make vehicle audio spatial"); audio.playOnAwake = false; audio.spatialBlend = 1f; audio.minDistance = 5f; audio.maxDistance = 90f; JourneyWorldSetupUtility.Dirty(audio); }
        if (submarine)
        {
            var bubbles = JourneyWorldSetupUtility.Add<JourneySubmarineBubbles>(root.gameObject); Undo.RecordObject(bubbles, "Connect engine exhaust points");
            bubbles.exhaustPoints = new[] { Find(root, "Exhaust Point 1"), Find(root, "Exhaust Point 2"), Find(root, "Exhaust Point 3") }.Where(t => t != null).ToArray();
            if (bubbles.bubbleMaterial == null) bubbles.bubbleMaterial = JourneyWorldSetupUtility.BubbleMaterial(); JourneyWorldSetupUtility.Dirty(bubbles);
        }
        JourneyWorldSetupUtility.Dirty(vehicle); JourneyWorldSetupUtility.Dirty(vehicle.hull); JourneyWorldSetupUtility.Dirty(body);
    }
    private static void ConnectControls(JourneyAirWaterPilot.Controls controls, Transform panel, bool submarine, bool layout)
    {
        if (panel == null) { Debug.LogWarning((submarine ? "Submarine" : "Helicopter") + " Buttons panel missing."); return; }
        controls.panel = panel.gameObject;
        controls.enter = Find(panel, submarine ? "Enter Submarine" : "Enter Helicopter")?.GetComponent<Button>();
        controls.exit = Find(panel, submarine ? "Exit Submarine" : "Exit Helicopter")?.GetComponent<Button>();
        var up = Find(panel, "Up"); var down = Find(panel, "Down");
        controls.up = up != null ? JourneyWorldSetupUtility.Add<JourneyVehiclePedal>(up.gameObject) : null;
        controls.down = down != null ? JourneyWorldSetupUtility.Add<JourneyVehiclePedal>(down.gameObject) : null;
        controls.nitro = submarine ? Find(panel, "Nitro Submarine")?.GetComponent<Button>() : null;
        if (layout)
        {
            Undo.RecordObject(panel, "Anchor vehicle controls to the screen"); JourneyWorldSetupUtility.Stretch((RectTransform)panel);
            Place(controls.down, new Vector2(1f, 0f), new Vector2(-110f, 355f));
            Place(controls.up, new Vector2(1f, 0f), new Vector2(-110f, 520f));
            Place(controls.nitro, new Vector2(1f, 0f), new Vector2(-110f, 685f));
            Place(controls.enter, new Vector2(1f, 0.5f), new Vector2(-320f, 180f));
            Place(controls.exit, new Vector2(1f, 0.5f), new Vector2(-320f, 180f));
        }
        Undo.RecordObject(panel.gameObject, "Hide vehicle controls before entering"); panel.gameObject.SetActive(false);
    }
    private static void Place(Component component, Vector2 anchor, Vector2 position)
    {
        if (component == null) return;
        var rect = (RectTransform)component.transform; Undo.RecordObject(rect, "Reserve space for the minimap");
        rect.anchorMin = rect.anchorMax = anchor; rect.pivot = Vector2.one * 0.5f; rect.anchoredPosition = position; JourneyWorldSetupUtility.Dirty(rect);
    }
    private static void ReserveMapCorner(RectTransform canvas, RectTransform map, IEnumerable<RectTransform> controls)
    {
        Canvas.ForceUpdateCanvases(); var list = controls.Distinct().ToArray();
        Rect Bounds(RectTransform rect)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            var points = corners.Select(canvas.InverseTransformPoint).ToArray();
            float x = points.Min(p => p.x), y = points.Min(p => p.y);
            return new Rect(x, y, points.Max(p => p.x) - x, points.Max(p => p.y) - y);
        }
        Rect mapBounds = Bounds(map);
        foreach (var control in list)
        {
            Rect bounds = Bounds(control); if (!bounds.Overlaps(mapBounds)) continue;
            float x = mapBounds.xMin - 20f - bounds.width * 0.5f;
            for (int attempt = 0; attempt < list.Length; attempt++)
            {
                Rect candidate = new Rect(x - bounds.width * 0.5f, bounds.yMin, bounds.width, bounds.height); bool collision = false;
                foreach (var other in list)
                {
                    if (other == control) continue; Rect obstacle = Bounds(other);
                    if (!candidate.Overlaps(obstacle)) continue;
                    x = Mathf.Min(x, obstacle.xMin - 20f - bounds.width * 0.5f); collision = true;
                }
                if (!collision) break;
            }
            Undo.RecordObject(control, "Keep minimap clear of action buttons");
            Vector3 center = canvas.InverseTransformPoint(control.position); center.x += x - bounds.center.x;
            control.position = canvas.TransformPoint(center); JourneyWorldSetupUtility.Dirty(control);
        }
    }
    private static JourneyRadialMenu ConfigureRadial(Scene scene, Transform canvas, JourneyMenuController menu)
    {
        var button = Named(scene, "RadialMenu Button") ?? Named(scene, "Radial Menu Button");
        if (button == null)
        {
            var go = JourneyWorldSetupUtility.Child(canvas, "RadialMenu Button", typeof(RectTransform), typeof(Image), typeof(Button));
            button = go.transform; var rect = (RectTransform)button; rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f); rect.pivot = Vector2.one * 0.5f;
            rect.sizeDelta = Vector2.one * 90f; rect.anchoredPosition = new Vector2(-310f, 85f);
            var image = go.GetComponent<Image>(); image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Picture/RadialMenu Button.png") ?? JourneyWorldSetupUtility.Circle(); image.color = Color.white;
            go.GetComponent<Button>().targetGraphic = image;
            Debug.Log("RadialMenu Button was missing from the saved scene, so one was created.", go);
        }
        var radial = JourneyWorldSetupUtility.Add<JourneyRadialMenu>(button.gameObject); Undo.RecordObject(radial, "Connect radial item menu"); radial.menu = menu;
        if (radial.menuRoot == null)
        {
            var go = JourneyWorldSetupUtility.Child(canvas, "Journey Radial Menu", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f; rect.sizeDelta = Vector2.one * 440f;
            var background = go.GetComponent<Image>(); background.sprite = JourneyWorldSetupUtility.Circle(); background.color = new Color(0.02f, 0.04f, 0.08f, 0.85f); background.raycastTarget = true;
            radial.menuRoot = go.GetComponent<CanvasGroup>(); go.SetActive(false);
        }
        if (radial.slotBackground == null) radial.slotBackground = JourneyWorldSetupUtility.Circle();
        if (radial.items.Count == 0) for (int i = 0; i < 6; i++) radial.items.Add(new JourneyRadialMenu.Item { name = "Item " + (i + 1) });
        JourneyWorldSetupUtility.Dirty(radial); return radial;
    }
    private static void ConfigureMinimap(Scene scene, GameObject manager, Transform canvas, JourneyMenuController menu, Character player)
    {
        var minimap = JourneyWorldSetupUtility.Add<JourneyMinimap>(manager); Undo.RecordObject(minimap, "Connect offline minimap");
        minimap.menu = menu; minimap.followTarget = player.transform;
        Sprite circle = JourneyWorldSetupUtility.Circle(); if (minimap.defaultIcon == null) minimap.defaultIcon = circle;
        if (minimap.rootUI == null)
        {
            var go = JourneyWorldSetupUtility.Child(canvas, "Journey Minimap", typeof(RectTransform), typeof(Image), typeof(Mask));
            var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f); rect.sizeDelta = Vector2.one * 230f; rect.anchoredPosition = new Vector2(-24f, 24f);
            var image = go.GetComponent<Image>(); image.sprite = circle; image.raycastTarget = false; go.GetComponent<Mask>().showMaskGraphic = true;
            var map = JourneyWorldSetupUtility.Child(rect, "Offline Map", typeof(RectTransform), typeof(RawImage)); JourneyWorldSetupUtility.Stretch((RectTransform)map.transform);
            minimap.mapImage = map.GetComponent<RawImage>(); minimap.mapImage.raycastTarget = false;
            var icons = JourneyWorldSetupUtility.Child(rect, "Live Icons", typeof(RectTransform)); JourneyWorldSetupUtility.Stretch((RectTransform)icons.transform);
            minimap.iconRoot = (RectTransform)icons.transform; minimap.rootUI = go; go.SetActive(false);
        }
        var marker = Marker(player.gameObject, JourneyWorldSetupUtility.Arrow(), Color.white, true);
        marker.size = 22f; marker.headingTarget = player.Animim.Mannequin; JourneyWorldSetupUtility.Dirty(marker);
        foreach (var car in All<JourneyVehicle>(scene)) Marker(car.gameObject, circle, new Color(1f, 0.8f, 0.2f), false);
        foreach (var drone in All<JourneyDroneLauncher>(scene))
            if (drone.droneRoot != null) Marker(drone.droneRoot.gameObject, circle, new Color(1f, 0.45f, 0.25f), false);
        foreach (var vehicle in All<JourneyAirWaterVehicle>(scene))
        {
            var mark = Marker(vehicle.gameObject, circle, vehicle.kind == JourneyAirWaterVehicle.VehicleKind.Helicopter ? Color.cyan : new Color(0.3f, 1f, 0.5f), false);
            mark.headingOffset = vehicle.kind == JourneyAirWaterVehicle.VehicleKind.Helicopter ? 180f : 0f; JourneyWorldSetupUtility.Dirty(mark);
        }
        JourneyWorldSetupUtility.Dirty(minimap);
    }
    private static JourneyMinimapMarker Marker(GameObject target, Sprite icon, Color color, bool rotate)
    {
        bool fresh = target.GetComponent<JourneyMinimapMarker>() == null;
        var marker = JourneyWorldSetupUtility.Add<JourneyMinimapMarker>(target); Undo.RecordObject(marker, "Connect map marker");
        if (fresh) { marker.icon = icon; marker.tint = color; marker.rotateIcon = rotate; }
        JourneyWorldSetupUtility.Dirty(marker); return marker;
    }
}
#endif
