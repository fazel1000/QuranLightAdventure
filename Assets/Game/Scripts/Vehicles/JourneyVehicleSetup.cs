#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using GameCreator.Runtime.Characters;
using GameCreator.Runtime.Cameras;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public sealed class JourneyVehicleSetup : EditorWindow
{
    private GameObject vehicleRoot, panel;
    private Character player;
    private JourneyMenuController menu;
    private Camera view;
    private Button enter, exit, gas, brake, nitro;
    private Transform body, fl, fr, bl, br;
    [MenuItem("Quran Kids/Setup Journey Vehicle")]
    private static void Open() { GetWindow<JourneyVehicleSetup>("Journey Vehicle").Scan(); }
    private static T Find<T>() where T : UnityEngine.Object => UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
    private static GameObject Named(string name) => UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        .FirstOrDefault(t => t.name == name && t.gameObject.scene.IsValid())?.gameObject;
    private static Button NamedButton(string name) => Named(name)?.GetComponent<Button>();
    private void Scan()
    {
        vehicleRoot = Selection.activeGameObject != null && Selection.activeGameObject.name.StartsWith("Prefab_")
            ? Selection.activeGameObject : Named("Prefab_MilitaryJeep_MK1_standardShader");
        player = Named("Player")?.GetComponent<Character>() ?? Find<Character>(); menu = Find<JourneyMenuController>(); view = Camera.main;
        panel = Named("DrivingPanel"); enter = NamedButton("EnterVehicleButton"); exit = NamedButton("ExitVehicleButton");
        gas = NamedButton("GasButton"); brake = NamedButton("BrakeButton"); nitro = NamedButton("NitroButton");
        DetectMeshes();
    }
    private void DetectMeshes()
    {
        if (vehicleRoot == null) return;
        Transform[] all = vehicleRoot.GetComponentsInChildren<Transform>(true);
        fl = all.FirstOrDefault(t => t.name.EndsWith("_Wheel_FL_mesh"));
        fr = all.FirstOrDefault(t => t.name.EndsWith("_Wheel_FR_mesh"));
        bl = all.FirstOrDefault(t => t.name.EndsWith("_Wheel_BL_mesh"));
        br = all.FirstOrDefault(t => t.name.EndsWith("_Wheel_BR_mesh"));
        body = all.FirstOrDefault(t => t.name.EndsWith("_mesh") && !t.name.Contains("_Wheel_"));
    }
    private static T Field<T>(string label, T value) where T : UnityEngine.Object => (T)EditorGUILayout.ObjectField(label, value, typeof(T), true);
    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Run outside Play Mode. Adds physics to the scene car, and connects the existing left joystick. The pack prefab asset is not edited.", MessageType.Info);
        if (GUILayout.Button("Find scene references")) Scan();
        EditorGUI.BeginChangeCheck(); vehicleRoot = Field("Vehicle Root", vehicleRoot);
        if (EditorGUI.EndChangeCheck()) DetectMeshes();
        body = Field("Body visual", body);
        fl = Field("Front Left visual", fl); fr = Field("Front Right visual", fr);
        bl = Field("Back Left visual", bl); br = Field("Back Right visual", br);
        player = Field("Player Character", player); menu = Field("Menu", menu); view = Field("Main Camera", view);
        panel = Field("Driving Panel", panel); enter = Field("Enter Button", enter); exit = Field("Exit Button", exit);
        gas = Field("Gas Button", gas); brake = Field("Brake Button", brake); nitro = Field("Nitro Button", nitro);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            if (GUILayout.Button("Create and Connect"))
                try { Build(); } catch (Exception ex) { Debug.LogException(ex); EditorUtility.DisplayDialog("Journey Vehicle", ex.Message, "OK"); }
    }
    private static T Add<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? Undo.AddComponent<T>(go);
    private static Transform Child(Transform parent, string name, Vector3 position)
    {
        Transform result = parent.Find(name);
        if (result != null) return result;
        GameObject go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Create vehicle helper");
        go.transform.SetParent(parent, false); go.transform.localPosition = position;
        go.layer = parent.gameObject.layer;
        return go.transform;
    }
    private static Bounds LocalBounds(Transform root, Transform mesh)
    {
        bool found = false; Bounds result = default;
        foreach (MeshFilter filter in mesh.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            Bounds b = filter.sharedMesh.bounds;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            {
                Vector3 p = root.InverseTransformPoint(filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z))));
                if (!found) { result = new Bounds(p, Vector3.zero); found = true; } else result.Encapsulate(p);
            }
        }
        if (!found) throw new InvalidOperationException("No readable mesh bounds under " + mesh.name);
        return result;
    }
    private void Build()
    {
        if (vehicleRoot == null || body == null || fl == null || fr == null || bl == null || br == null || player == null || menu == null || view == null || panel == null || enter == null || exit == null || gas == null || brake == null || nitro == null)
            throw new InvalidOperationException("Assign all fields first. UI controls must have Button components.");
        if (!vehicleRoot.scene.IsValid() || EditorUtility.IsPersistent(vehicleRoot)) throw new InvalidOperationException("Drag the car instance from Hierarchy, not the Project prefab asset.");
        if ((vehicleRoot.transform.lossyScale - Vector3.one).sqrMagnitude > 0.0001f) throw new InvalidOperationException("Vehicle root and its parents must have world scale 1,1,1. Leave visual mesh scales unchanged.");
        if (enter.transform.IsChildOf(panel.transform)) throw new InvalidOperationException("EnterVehicleButton must be outside DrivingPanel.");
        if (view.GetComponent<TCamera>() == null) throw new InvalidOperationException("Main Camera must have the Game Creator camera component.");
        if (Find<JourneyMovementSettings>() == null) throw new InvalidOperationException("Add JourneyMovementSettings to the always-active MenuManager first.");
        Transform root = vehicleRoot.transform;
        Transform[] visuals = { fl, fr, bl, br };
        if (visuals.Distinct().Count() != 4 || visuals.Any(t => !t.IsChildOf(root)) || !body.IsChildOf(root))
            throw new InvalidOperationException("Body and four different wheel visuals must belong to this vehicle.");
        if (vehicleRoot.GetComponentsInChildren<Rigidbody>(true).Any(rb => rb.transform != root))
            throw new InvalidOperationException("Remove child Rigidbody components first. Only the vehicle root needs a Rigidbody.");
        Bounds bounds = LocalBounds(root, body);
        Bounds[] wheelBounds = visuals.Select(t => LocalBounds(root, t)).ToArray();
        Undo.SetCurrentGroupName("Setup Journey Vehicle");
        Rigidbody rb = Add<Rigidbody>(vehicleRoot);
        JourneyVehicle vehicle = Add<JourneyVehicle>(vehicleRoot);
        bool fresh = vehicle.bodyCollider == null;
        Undo.RecordObject(rb, "Configure vehicle physics"); Undo.RecordObject(vehicle, "Connect vehicle");
        if (fresh)
        {
            rb.mass = 900f; rb.useGravity = true; rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            vehicle.centerOfMass = new Vector3(0f, wheelBounds.Average(b => b.center.y) + 0.05f, 0f);
        }
        Transform chassis = Child(root, "VehicleBodyCollider", Vector3.zero);
        BoxCollider box = Add<BoxCollider>(chassis.gameObject);
        if (fresh) { Undo.RecordObject(box, "Size chassis"); box.center = bounds.center; box.size = bounds.size; }
        vehicle.bodyCollider = box;
        vehicle.driverSeat = Child(root, "DriverSeat", bounds.center + Vector3.up * 0.2f);
        vehicle.exitPoint = Child(root, "ExitPoint", new Vector3(bounds.min.x - 0.8f, 0f, bounds.center.z));
        vehicle.cameraTarget = Child(root, "CameraTarget", bounds.center + Vector3.up * 0.4f);
        Transform physicsRoot = Child(root, "WheelColliders", Vector3.zero);
        var wheels = new JourneyVehicle.Wheel[4]; string[] names = { "FrontLeft", "FrontRight", "BackLeft", "BackRight" };
        for (int i = 0; i < 4; i++)
        {
            bool existing = physicsRoot.Find(names[i]) != null;
            Vector3 center = wheelBounds[i].center;
            Transform helper = Child(physicsRoot, names[i], center + Vector3.up * 0.125f);
            WheelCollider wc = Add<WheelCollider>(helper.gameObject);
            if (!existing)
            {
                Undo.RecordObject(wc, "Configure wheel");
                wc.radius = Mathf.Max(0.05f, Mathf.Max(wheelBounds[i].extents.y, wheelBounds[i].extents.z));
                wc.mass = 20f; wc.suspensionDistance = 0.25f;
                JointSpring spring = wc.suspensionSpring; spring.spring = 30000f; spring.damper = 4500f; spring.targetPosition = 0.5f; wc.suspensionSpring = spring;
            }
            wheels[i] = new JourneyVehicle.Wheel { collider = wc, visual = visuals[i], steering = i < 2 };
        }
        vehicle.wheels = wheels;
        JourneyVehicleDriver driver = Find<JourneyVehicleDriver>();
        if (driver == null)
        {
            GameObject go = new GameObject("Journey Vehicle Driver"); Undo.RegisterCreatedObjectUndo(go, "Create driver");
            driver = Undo.AddComponent<JourneyVehicleDriver>(go);
        }
        Undo.RecordObject(driver, "Connect driver");
        driver.player = player; driver.menu = menu; driver.viewCamera = view; driver.gameCreatorCamera = view.GetComponent<TCamera>();
        if (driver.gameCreatorCamera == null) throw new InvalidOperationException("Main Camera must be the Game Creator camera with a TCamera-derived component.");
        driver.drivingPanel = panel; driver.enterButton = enter; driver.exitButton = exit;
        driver.gas = Add<JourneyVehiclePedal>(gas.gameObject); driver.brake = Add<JourneyVehiclePedal>(brake.gameObject); driver.nitro = Add<JourneyVehiclePedal>(nitro.gameObject);
        string[] suspended = { "JourneyPlayerCombat", "LightBrushPuzzle", "JourneyStoneThrowController", "JourneyVoiceBoard", "RightScreenDragArea" };
        driver.suspendWhileDriving = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(b => b.gameObject.scene == player.gameObject.scene && suspended.Contains(b.GetType().Name)).Cast<Behaviour>().ToArray();
        driver.onFootUI = new[] { Named("AttackButton"), Named("DefendButton"), Named("Throw"), Named("Cancel") }.Where(go => go != null)
            .Concat(driver.suspendWhileDriving.Where(b => b is RightScreenDragArea).Select(b => b.gameObject)).Distinct().ToArray();
        Undo.RecordObject(panel, "Hide driving UI"); panel.SetActive(false);
        Undo.RecordObject(enter.gameObject, "Hide enter UI"); enter.gameObject.SetActive(false);
        foreach (Component item in vehicleRoot.GetComponentsInChildren<Component>(true))
            if (item != null) { EditorUtility.SetDirty(item); PrefabUtility.RecordPrefabInstancePropertyModifications(item); }
        EditorUtility.SetDirty(driver); EditorSceneManager.MarkSceneDirty(vehicleRoot.scene);
        Selection.activeGameObject = vehicleRoot;
        EditorUtility.DisplayDialog("Journey Vehicle", "Connected. Check the body box, four WheelCollider circles and ExitPoint in Scene view, then save the scene. No On Click entries are needed.", "OK");
    }
}
#endif
