#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

// Runs the actual reserve class outside Play without touching scene or purchase data.
public static class JourneyDivingReserveChecks
{
    private static void Near(float actual, float expected, string label)
    {
        if (float.IsNaN(actual) || Math.Abs(actual - expected) > 0.001f)
            throw new InvalidOperationException(label + ": expected " + expected + ", got " + actual);
    }
    [MenuItem("Quran Kids/Tests/Run Diving Reserve Checks")]
    public static void Run()
    {
        var reserve = new JourneyBreathingReserve(30f, 600f);
        Near(reserve.Consume(29.5f, false), 0f, "No early drowning");
        Near(reserve.BreathRemaining, 0.5f, "Breath remaining before limit");
        Near(reserve.Consume(1f, false), 0.5f, "Only time beyond 30 seconds is unprotected");
        Near(reserve.BreathRemaining, 0f, "Breath cannot become negative");
        Near(reserve.ScubaRemaining, 600f, "Holding breath does not consume the tank");
        Near(reserve.Consume(600f, true), 0f, "Tank supplies exactly ten minutes");
        Near(reserve.Consume(2f, true), 2f, "Empty tank supplies no air");
        reserve.Refill();
        Near(reserve.BreathRemaining, 30f, "Surfacing refills breath");
        Near(reserve.ScubaRemaining, 600f, "Surfacing refills tank");

        reserve.Consume(10f, false); reserve.Consume(120f, true);
        Near(reserve.BreathRemaining, 20f, "Putting on scuba keeps the previous breath reserve");
        Near(reserve.Consume(20f, false), 0f, "Taking off scuba resumes the remaining breath");
        Near(reserve.ScubaRemaining, 480f, "Taking off scuba does not refill it");
        Near(reserve.Consume(0.5f, true), 0f, "Re-equipping uses existing tank air");
        Near(reserve.ScubaRemaining, 479.5f, "Re-equipping does not reset tank timer");
        Near(reserve.Consume(1f, false), 1f, "Re-selecting an empty breath reserve does not refill it");
        Near(reserve.Consume(0f, true), 0f, "Paused frame has no consumption");
        Near(reserve.Consume(-1f, true), 0f, "Negative time is ignored");
        Near(reserve.Consume(float.NaN, true), 0f, "Invalid time is ignored");
        Near(reserve.ScubaRemaining, 479.5f, "Invalid frames do not change reserves");
        reserve.Configure(5f, 10f);
        Near(reserve.ScubaRemaining, 10f, "Shorter test duration clamps remaining tank air");
        Near(reserve.BreathRemaining, 0f, "Changing durations does not refill empty breath");
        Debug.Log("Diving reserve checks PASSED (21 assertions). No scene or saved purchase data was changed. Waterline, HUD, equipment and respawn still require Play/device tests.");
    }
}
#endif
