using UnityEngine;

[DisallowMultipleComponent]
public sealed class JourneyDroneTarget : MonoBehaviour
{
    [Min(0)] public int coinsPerImpact = 100;
    public bool awardOnlyOnce;
    public Transform smokePoint;
    public GameObject smokePrefab;
    [Tooltip("Keep one looping smoke instance on this target until the target or scene is removed.")]
    public bool persistentSmoke = true;
    [Min(1f)] public float smokeLifetime = 45f;
    private bool awarded;
    private GameObject liveSmoke;

    public void Hit(LightBrushPuzzle wallet, Vector3 point, GameObject fallbackSmoke)
    {
        if ((!awardOnlyOnce || !awarded) && wallet != null) wallet.AddCoins(coinsPerImpact);
        awarded = true;
        GameObject prefab = smokePrefab != null ? smokePrefab : fallbackSmoke;
        if (liveSmoke == null && prefab != null)
        {
            liveSmoke = Instantiate(prefab, smokePoint != null ? smokePoint.position : point, Quaternion.identity);
            liveSmoke.SetActive(false);
            liveSmoke.transform.SetParent(transform, true);
            foreach (var ps in liveSmoke.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (persistentSmoke)
                {
                    var main = ps.main;
                    main.loop = true;
                    main.stopAction = ParticleSystemStopAction.None;
                }
            }
            liveSmoke.SetActive(true);
            foreach (var ps in liveSmoke.GetComponentsInChildren<ParticleSystem>(true)) ps.Play(true);
            if (!persistentSmoke) Destroy(liveSmoke, smokeLifetime);
        }
    }
}
