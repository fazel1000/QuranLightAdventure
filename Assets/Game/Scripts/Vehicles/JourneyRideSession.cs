using UnityEngine;

// One player can control only one car, drone, helicopter or submarine at a time.
public static class JourneyRideSession
{
    private static Object owner;
    public static bool IsOccupied => owner != null;
    public static bool BusyFor(Object requester) => owner != null && owner != requester;
    public static bool TryAcquire(Object requester)
    {
        if (requester == null || BusyFor(requester)) return false;
        owner = requester; return true;
    }
    public static void Release(Object requester) { if (owner == requester) owner = null; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { owner = null; }
}
