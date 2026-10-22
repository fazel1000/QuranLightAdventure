#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class JourneyWorldSystemsValidation
{
    [MenuItem("Quran Kids/Validate Air Water and World Systems")]
    private static void Validate()
    {
        var scene = SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var errors = new List<string>(); var warnings = new List<string>();
        void Check(bool value, string message) { if (!value) errors.Add(message); }
        var binder = roots.SelectMany(go => go.GetComponentsInChildren<JourneyPlayerModelBindings>(true)).FirstOrDefault();
        Check(binder != null, "Player model bindings are missing.");
        if (binder != null)
        {
            Check(binder.player != null && binder.gameplayController != null && binder.combat != null, "Player, gameplay controller and combat must be assigned.");
            Check(binder.handSocket != null && binder.throwOrigin != null && binder.shieldHitbox != null, "Hand, throw and shield references must be assigned.");
            var controller = binder.gameplayController as AnimatorController;
            Check(controller != null && controller.layers.Any(l => l.name == JourneyPlayerCombat.LayerName), "Gameplay controller needs the sword actions layer.");
        }
        var pilot = roots.SelectMany(go => go.GetComponentsInChildren<JourneyAirWaterPilot>(true)).FirstOrDefault();
        Check(pilot != null, "Air/water pilot is missing.");
        if (pilot != null)
        {
            Check(pilot.player != null && pilot.menu != null && pilot.viewCamera != null, "Pilot player, menu and camera must be assigned.");
            foreach (var controls in new[] { pilot.helicopter, pilot.submarine }) Check(controls.panel != null && controls.enter != null && controls.exit != null && controls.up != null && controls.down != null, "Both vehicle UI sets need their panel, Enter, Exit, Up and Down.");
            Check(pilot.submarine.nitro != null, "Submarine Nitro button is missing.");
        }
        var vehicles = roots.SelectMany(go => go.GetComponentsInChildren<JourneyAirWaterVehicle>(true)).ToArray();
        Check(vehicles.Any(v => v.kind == JourneyAirWaterVehicle.VehicleKind.Helicopter), "Helicopter controller missing.");
        Check(vehicles.Any(v => v.kind == JourneyAirWaterVehicle.VehicleKind.Submarine), "Submarine controller missing.");
        foreach (var vehicle in vehicles)
        {
            Check(vehicle.GetComponent<Rigidbody>() != null && vehicle.hull != null && vehicle.driverSeat != null && vehicle.exitPoint != null, vehicle.name + ": physics or seat references are incomplete.");
            if (vehicle.kind == JourneyAirWaterVehicle.VehicleKind.Helicopter) Check(vehicle.mainRotor != null && vehicle.tailRotor != null && vehicle.visualRoot != null, "Helicopter rotor/visual references are incomplete.");
            else
            {
                var bubbles = vehicle.GetComponent<JourneySubmarineBubbles>();
                Check(bubbles != null && bubbles.exhaustPoints.Length == 3 && bubbles.exhaustPoints.All(p => p != null), "Submarine needs three exhaust points.");
                if (vehicle.ambientLoop == null) warnings.Add("Assign the submarine Ambient Loop (nearby idle sound).");
            }
            if (vehicle.engineLoop == null) warnings.Add("Assign Engine Loop on " + vehicle.name + ".");
        }
        // Run the actual production boost curve, including both transition boundaries.
        Check(Mathf.Approximately(JourneyAirWaterVehicle.BoostEnvelope(0f, 3f, 5f, 3f), 0f), "Boost must start at normal speed.");
        foreach (float age in new[] { 3f, 5f, 8f }) Check(Mathf.Approximately(JourneyAirWaterVehicle.BoostEnvelope(age, 3f, 5f, 3f), 1f), "Boost full-speed hold is incorrect.");
        Check(Mathf.Approximately(JourneyAirWaterVehicle.BoostEnvelope(11f, 3f, 5f, 3f), 0f), "Boost must return to normal speed after 11 seconds.");
        float previous = 0f;
        for (int i = 0; i <= 60; i++)
        { float value = JourneyAirWaterVehicle.BoostEnvelope(i * 0.05f, 3f, 5f, 3f); Check(value >= previous - 0.0001f && value <= 1f, "Boost ramp must increase smoothly without overshoot."); previous = value; }
        var map = roots.SelectMany(go => go.GetComponentsInChildren<JourneyMinimap>(true)).FirstOrDefault();
        Check(map != null && map.rootUI != null && map.mapImage != null && map.iconRoot != null, "Minimap UI is incomplete.");
        if (map != null && map.offlineMap == null) warnings.Add("Run Bake Offline Minimap before testing the minimap.");
        var rain = roots.SelectMany(go => go.GetComponentsInChildren<JourneyRainCycle>(true)).FirstOrDefault();
        Check(rain != null, "Rain cycle is missing."); if (rain != null && rain.rainEffectPrefab == null) warnings.Add("Assign Rain Effect Prefab.");
        var radial = roots.SelectMany(go => go.GetComponentsInChildren<JourneyRadialMenu>(true)).FirstOrDefault();
        Check(radial != null && radial.menuRoot != null && radial.items.Count > 0, "Radial menu is incomplete.");
        if (radial != null && radial.items.All(item => item == null || item.icon == null)) warnings.Add("Assign radial item icons and On Selected actions/equipment objects.");
        foreach (string error in errors) Debug.LogError(error);
        foreach (string warning in warnings.Distinct()) Debug.LogWarning(warning);
        Debug.Log("World systems validation: " + errors.Count + " errors, " + warnings.Distinct().Count() + " assignment reminders. Physics and animation still need a Play/device test.");
    }
}
#endif
