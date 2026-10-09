using UnityEngine;

// Reused query buffers; no OnTriggerEnter/Rigidbody dependency for an animated blade.
internal sealed class JourneyBladeContacts
{
    private readonly Collider[] overlaps = new Collider[64];
    private readonly RaycastHit[] casts = new RaycastHit[64];
    private static int syncedFrame = -1;
    private float shieldTime, bodyTime;


    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { syncedFrame = -1; }

    // 0 = miss, 1 = shield first, 2 = body first. Body wins ambiguous ties.
    public int Check(Vector3 oldBase, Vector3 oldTip, Vector3 newBase, Vector3 newTip,
        float radius, JourneyPlayerCombat player)
    {
        Collider body = player.bodyHitbox;
        BoxCollider shield = player.shieldHitbox;
        if (body == null || !body.enabled || !body.gameObject.activeInHierarchy) return 0;
        if (syncedFrame != Time.frameCount)
        {
            Physics.SyncTransforms();
            syncedFrame = Time.frameCount;
        }
        bool canBlock = player.ShieldActive;
        int mask = 1 << body.gameObject.layer;
        if (shield != null) mask |= 1 << shield.gameObject.layer;
        radius = Mathf.Max(0.01f, radius);
        shieldTime = bodyTime = float.PositiveInfinity;
        Overlap(oldBase, oldTip, radius, 0f, mask, body, shield, canBlock);
        // Sample along the blade. End capsules cover gaps between longitudinal samples.
        float length = Mathf.Max(Vector3.Distance(oldBase, oldTip), Vector3.Distance(newBase, newTip));
        int steps = Mathf.Clamp(Mathf.CeilToInt(length / (radius * 1.5f)), 2, 32);
        for (int i = 0; i <= steps; i++)
        {
            float fraction = i / (float)steps;
            Vector3 origin = Vector3.Lerp(oldBase, oldTip, fraction);
            Vector3 delta = Vector3.Lerp(newBase, newTip, fraction) - origin;
            float distance = delta.magnitude;
            if (distance < 0.00001f) continue;
            int count = Physics.SphereCastNonAlloc(origin, radius, delta / distance, casts,
                distance, mask, QueryTriggerInteraction.Collide);
            if (count == casts.Length)
            {
                // Rare overflow: read ALL contacts rather than silently consume the hit.
                RaycastHit[] all = Physics.SphereCastAll(origin, radius, delta / distance,
                    distance, mask, QueryTriggerInteraction.Collide);
                for (int j = 0; j < all.Length; j++)
                    Consider(all[j].collider, all[j].distance / distance, body, shield, canBlock);
            }
            else
            {
                for (int j = 0; j < count; j++)
                    Consider(casts[j].collider, casts[j].distance / distance, body, shield, canBlock);
            }
        }
        Overlap(newBase, newTip, radius, 1f, mask, body, shield, canBlock);
        // Once body contact has happened, a later shield contact cannot undo it.
        // If frame resolution cannot distinguish the order, do not grant a free block.
        if (!float.IsPositiveInfinity(bodyTime) && bodyTime <= shieldTime + 0.00001f) return 2;
        return float.IsPositiveInfinity(shieldTime) ? 0 : 1;
    }

    private void Overlap(Vector3 a, Vector3 b, float radius, float time, int mask,
        Collider body, BoxCollider shield, bool canBlock)
    {
        int count = Physics.OverlapCapsuleNonAlloc(a, b, radius, overlaps, mask, QueryTriggerInteraction.Collide);
        if (count == overlaps.Length)
        {
            Collider[] all = Physics.OverlapCapsule(a, b, radius, mask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < all.Length; i++) Consider(all[i], time, body, shield, canBlock);
        }
        else
        {
            for (int i = 0; i < count; i++) Consider(overlaps[i], time, body, shield, canBlock);
        }
    }

    private void Consider(Collider hit, float time, Collider body, BoxCollider shield, bool canBlock)
    {
        if (hit == body) bodyTime = Mathf.Min(bodyTime, time);
        if (canBlock && hit == shield) shieldTime = Mathf.Min(shieldTime, time);
    }
}
