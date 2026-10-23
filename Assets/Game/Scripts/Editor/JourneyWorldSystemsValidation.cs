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
            Check(pilot.vehicleLookArea != null, "Vehicle Right Look Area is missing. Run Setup Air Water and World Systems.");
            foreach (var controls in new[] { pilot.helicopter, pilot.submarine }) Check(controls.panel != null && controls.enter != null && controls.exit != null && controls.up != null && controls.down != null, "Both vehicle UI sets need their panel, Enter, Exit, Up and Down.");
            Check(pilot.submarine.nitro != null, "Submarine Nitro button is missing.");
        }
        var vehicles = roots.SelectMany(go => go.GetComponentsInChildren<JourneyAirWaterVehicle>(true)).ToArray();
        Check(vehicles.Any(v => v.kind == JourneyAirWaterVehicle.VehicleKind.Helicopter), "Helicopter controller missing.");
        Check(vehicles.Any(v => v.kind == JourneyAirWaterVehicle.VehicleKind.Submarine), "Submarine controller missing.");
        foreach (var vehicle in vehicles)
        {
            Check(vehicle.GetComponent<Rigidbody>() != null && vehicle.hull != null && vehicle.driverSeat != null && vehicle.exitPoint != null, vehicle.name + ": physics or seat references are incomplete.");
            if (vehicle.hull != null) Check(vehicle.hull.enabled && !vehicle.hull.isTrigger, vehicle.name + ": Hull must be enabled and non-trigger.");
            var body = vehicle.GetComponent<Rigidbody>();
            if (body != null) Check(body.detectCollisions && (body.constraints & RigidbodyConstraints.FreezePosition) == 0, vehicle.name + ": enable Detect Collisions and unfreeze all position axes.");
            if (vehicle.kind == JourneyAirWaterVehicle.VehicleKind.Helicopter) Check(vehicle.mainRotor != null && vehicle.tailRotor != null && vehicle.visualRoot != null, "Helicopter rotor/visual references are incomplete.");
            else
            {
                var bubbles = vehicle.GetComponent<JourneySubmarineBubbles>();
                Check(bubbles != null && bubbles.exhaustPoints.Length == 3 && bubbles.exhaustPoints.All(p => p != null), "Submarine needs three exhaust points.");
                Check(vehicle.waterVolume != null && vehicle.waterVolume.IsUsable, "Submarine Water Volume must reference an active, upright JourneyWaterVolume on Underwater Volume.");
                if (vehicle.waterVolume != null && vehicle.hull != null)
                    Check(vehicle.waterVolume.TryConstrainHull(vehicle.transform, vehicle.hull, vehicle.transform.position,
                        vehicle.transform.rotation, vehicle.surfaceClearance, out _), "Submarine Hull does not fit inside Underwater Volume.");
                if (vehicle.ambientLoop == null) warnings.Add("Assign the submarine Ambient Loop (nearby idle sound).");
                var impact = vehicle.GetComponent<JourneySubmarineSeabedImpact>();
                Check(impact != null && impact.impactAudioSource != null, "Submarine seabed impact component/source is missing. Run Setup Air Water and World Systems.");
                if (impact != null && impact.seabedImpactClip == null) warnings.Add("Assign Submarine > JourneySubmarineSeabedImpact > Seabed Impact Clip.");
                if (impact != null && impact.sandImpactPrefab == null) warnings.Add("Assign Submarine > JourneySubmarineSeabedImpact > Sand Impact Prefab.");
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
        var volume = roots.SelectMany(go => go.GetComponentsInChildren<JourneyWaterVolume>(true)).FirstOrDefault();
        Check(volume != null && volume.IsUsable && volume.Region.isTrigger, "Underwater Volume needs an enabled trigger BoxCollider and JourneyWaterVolume.");
        var swimming = roots.SelectMany(go => go.GetComponentsInChildren<JourneyPlayerSwimming>(true)).FirstOrDefault();
        Check(swimming != null && swimming.preferredWater != null && swimming.viewCamera != null && swimming.swimmingClip != null && swimming.treadingClip != null,
            "Player swimming/water/animation references are incomplete. Run Setup Swimming and Stone Aiming.");
        if (swimming != null && binder != null && binder.gameplayController is AnimatorController swimController)
            Check(swimController.layers.Any(l => l.name == JourneyPlayerSwimming.LayerName) &&
                swimController.parameters.Any(p => p.name == JourneyPlayerSwimming.SpeedParameter) &&
                swimController.parameters.Any(p => p.name == JourneyPlayerSwimming.PlaybackSpeedParameter), "Gameplay controller needs the swimming layer and both swimming speed parameters. Run Setup Swimming and Stone Aiming.");
        foreach (var stone in roots.SelectMany(go => go.GetComponentsInChildren<JourneyStoneThrowController>(true)))
        {
            var data = new SerializedObject(stone);
            Check(data.FindProperty("aimReticle").objectReferenceValue != null, "Stone Aim Reticle is missing. Run Setup Swimming and Stone Aiming.");
            var stonesRoot = data.FindProperty("stonesRoot").objectReferenceValue as Transform;
            if (stonesRoot == null || stonesRoot.GetComponentsInChildren<JourneyThrowableStone>(true).Length == 0)
                warnings.Add("No throwable stones under Stones Root. Select rock objects and run Quran Kids > Stones > Make Selected Rocks Throwable.");
        }
        if (errors.Count > 0)
            Debug.LogError("World systems validation FAILED in active scene '" + scene.name + "':\n- " + string.Join("\n- ", errors) +
                "\nRun Setup Air Water and World Systems outside Play Mode and inspect its first FAILED step. Save the scene after setup.");
        foreach (string warning in warnings.Distinct()) Debug.LogWarning(warning);
        if (errors.Count == 0) Debug.Log("World systems validation [" + scene.name + "]: 0 errors, " + warnings.Distinct().Count() + " assignment reminders. Physics and animation still need a Play/device test.");
    }
}
#endif
