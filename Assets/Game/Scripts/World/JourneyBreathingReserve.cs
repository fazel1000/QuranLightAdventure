using System;

// Time budgets are independent of UI, equipment toggles and Unity frame callbacks.
public sealed class JourneyBreathingReserve
{
    public float BreathCapacity { get; private set; }
    public float ScubaCapacity { get; private set; }
    public float BreathRemaining { get; private set; }
    public float ScubaRemaining { get; private set; }

    public JourneyBreathingReserve(float breathSeconds, float scubaSeconds)
    {
        Configure(breathSeconds, scubaSeconds); Refill();
    }
    private static float Capacity(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 1f : Math.Max(0.1f, value);
    public void Configure(float breathSeconds, float scubaSeconds)
    {
        BreathCapacity = Capacity(breathSeconds); ScubaCapacity = Capacity(scubaSeconds);
        BreathRemaining = Math.Min(BreathRemaining, BreathCapacity);
        ScubaRemaining = Math.Min(ScubaRemaining, ScubaCapacity);
    }
    public void Refill()
    { BreathRemaining = BreathCapacity; ScubaRemaining = ScubaCapacity; }

    // Returns only the part of this frame spent without air, including boundary crossings.
    public float Consume(float seconds, bool useScuba)
    {
        if (seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return 0f;
        float remaining = useScuba ? ScubaRemaining : BreathRemaining;
        float supplied = Math.Min(remaining, seconds);
        if (useScuba) ScubaRemaining = Math.Max(0f, remaining - supplied);
        else BreathRemaining = Math.Max(0f, remaining - supplied);
        return seconds - supplied;
    }
}
