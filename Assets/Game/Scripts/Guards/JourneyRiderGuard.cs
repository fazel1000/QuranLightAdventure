using System;
using System.Collections.Generic;
using GameCreator.Runtime.Characters;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class JourneyRiderGuard : MonoBehaviour
{
    [Serializable]
    public sealed class PatrolPoint
    {
        public Transform point;
        [Min(0f)] public float waitSeconds = 2f;
        public bool roarHere = true;
        [Min(0f)] public float afterRoarSeconds;
        public bool runToPoint;
    }
    public Animator animator;
    public Transform routeRoot;
    public List<PatrolPoint> patrolPoints = new List<PatrolPoint>();
    public bool pingPong;
    public JourneyMenuController menu;
    [Min(0.1f)] public float walkSpeed = 1.8f;
    [Min(0.1f)] public float runSpeed = 4.2f;
    [Range(0.1f, 1f), Tooltip("0.8 reduces running movement and animation speed by 20%, including existing guards.")]
    public float runSpeedScale = 0.8f;
    [Min(0f), Tooltip("0 waits for the roar animation and sound to finish.")]
    public float roarSeconds;
    public AnimationClip roarAnimation;
    [Range(1f, 60f)] public float maximumSlope = 35f;
    [Min(0f)] public float groundOffset;
    [Min(1f)] public float turningSpeed = 180f;
    public bool alignToSlope = true;
    [Header("Distance optimization")]
    public bool sleepWhenDistant = true;
    public Transform distanceTarget;
    [Min(1f)] public float wakeDistance = 45f;
    [Min(1f)] public float sleepDistance = 55f;
    [Min(0.1f)] public float distanceCheckInterval = 0.5f;
    [Header("Sounds")]
    public AudioSource movementSource, voiceSource;
    public AudioClip walkLoop, runLoop, roarSound, idleLoop;
    [Range(0f, 1f)] public float movementVolume = 0.65f;
    [Range(0f, 1f)] public float voiceVolume = 1f;
    // Serialized compatibility: old two-point scenes migrate without losing their references or timing.
    [HideInInspector] public Transform homePoint, lookoutPoint;
    [HideInInspector] public bool runOnReturn = true;
    [HideInInspector] public float pauseBeforeRoar = 2f, idleAtHomeSeconds = 10f;
    private enum Phase { Moving, Waiting, Roaring, AfterRoar }
    private Phase phase;
    private int index, direction = 1;
    private float timer, animationSpeed = 1f, nextDistanceCheck;
    private bool ready, frozen, sleeping, warned, originalAnimatorEnabled;
    private Vector3 lastNormal = Vector3.up;
    private AnimatorOverrideController overrides;
    private int activeStateHash;
    private float frozenAnimationTime;
    private readonly List<PatrolPoint> route = new List<PatrolPoint>();
    private static Transform cachedPlayer;
    private static float nextPlayerLookup;
    public bool IsSleeping => sleeping;
    public float EffectiveRunSpeed => runSpeed * runSpeedScale;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { cachedPlayer = null; nextPlayerLookup = 0f; }
    public void MigrateLegacyRoute()
    {
        if (patrolPoints == null) patrolPoints = new List<PatrolPoint>();
        if (patrolPoints.Count != 0 || homePoint == null || lookoutPoint == null) return;
        patrolPoints.Add(new PatrolPoint { point = homePoint, waitSeconds = idleAtHomeSeconds, roarHere = false, runToPoint = runOnReturn });
        patrolPoints.Add(new PatrolPoint { point = lookoutPoint, waitSeconds = pauseBeforeRoar, roarHere = true });
        if (routeRoot == null && homePoint.parent != null && homePoint.parent == lookoutPoint.parent && !homePoint.parent.IsChildOf(transform)) routeRoot = homePoint.parent;
    }
    private void Start()
    {
        MigrateLegacyRoute();
        foreach (var item in patrolPoints)
            if (item != null && item.point != null && item.point != transform && !item.point.IsChildOf(transform)) route.Add(item);
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (menu == null) menu = FindFirstObjectByType<JourneyMenuController>(FindObjectsInactive.Include);
        if (animator == null || route.Count == 0)
        { Debug.LogError("Guard needs an Animator and patrol points outside the moving actor. Use Configure Selected Riders.", this); enabled = false; return; }
        foreach (string name in new[] { "Idle", "Walk", "Run", "Roar" })
            if (!animator.HasState(0, Animator.StringToHash("Base Layer." + name)))
            { Debug.LogError("Assign the patrol controller using Configure Selected Riders.", this); enabled = false; return; }
        if (roarAnimation != null)
        {
            overrides = new AnimatorOverrideController(animator.runtimeAnimatorController);
            overrides["Roar"] = roarAnimation; animator.runtimeAnimatorController = overrides;
        }
        if (movementSource == null) movementSource = MakeSource();
        if (voiceSource == null || voiceSource == movementSource) voiceSource = MakeSource();
        movementSource.volume = movementVolume; voiceSource.volume = voiceVolume;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullCompletely;
        originalAnimatorEnabled = animator.enabled; animationSpeed = animator.speed;
        foreach (var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true)) renderer.updateWhenOffscreen = false;
        if (JourneyGuardGround.TryTerrain(route[0].point.position, maximumSlope, out var home, out lastNormal)) transform.position = home + Vector3.up * groundOffset;
        else transform.position = route[0].point.position;
        index = route.Count > 1 ? 1 : 0;
        ready = true; Enter(Phase.Moving);
        EvaluateDistance(true); ApplyFreeze();
    }
    private AudioSource MakeSource()
    {
        var source = gameObject.AddComponent<AudioSource>(); source.playOnAwake = false; source.spatialBlend = 1f;
        source.minDistance = 6f; source.maxDistance = 70f; source.rolloffMode = AudioRolloffMode.Linear; return source;
    }
    private static Transform Player()
    {
        if (cachedPlayer != null || Time.unscaledTime < nextPlayerLookup) return cachedPlayer;
        nextPlayerLookup = Time.unscaledTime + 1f;
        foreach (var character in FindObjectsByType<Character>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (character.IsPlayer) { cachedPlayer = character.transform; break; }
        return cachedPlayer;
    }
    private void EvaluateDistance(bool force = false)
    {
        if (!force && Time.unscaledTime < nextDistanceCheck) return;
        nextDistanceCheck = Time.unscaledTime + Mathf.Max(0.1f, distanceCheckInterval) + UnityEngine.Random.Range(0f, 0.08f);
        Transform target = distanceTarget != null ? distanceTarget : Player();
        if (!sleepWhenDistant || target == null) { sleeping = false; return; }
        float range = sleeping ? wakeDistance : Mathf.Max(wakeDistance, sleepDistance);
        sleeping = (target.position - transform.position).sqrMagnitude > range * range;
    }
    private void ApplyFreeze()
    {
        bool next = sleeping || Time.timeScale <= 0f || (menu != null && !menu.IsGameplayActive);
        if (next == frozen) return;
        frozen = next;
        if (frozen)
        {
            var state = animator.GetCurrentAnimatorStateInfo(0);
            frozenAnimationTime = state.fullPathHash == activeStateHash ? state.normalizedTime : 0f;
            animator.enabled = false; movementSource.Pause(); voiceSource.Pause();
        }
        else
        {
            animator.enabled = originalAnimatorEnabled;
            if (originalAnimatorEnabled) animator.Play(activeStateHash, 0, frozenAnimationTime);
            animator.speed = phase == Phase.Moving && route[index].runToPoint ? animationSpeed * runSpeedScale : animationSpeed;
            movementSource.UnPause(); voiceSource.UnPause();
        }
    }
    private void Update()
    {
        if (!ready) return;
        EvaluateDistance(); ApplyFreeze();
        if (frozen) return;
        if (route[index].point == null) { enabled = false; return; }
        if (phase == Phase.Moving) { Move(); return; }
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        if (phase == Phase.Waiting && route[index].roarHere) Enter(Phase.Roaring);
        else if (phase == Phase.Roaring && route[index].afterRoarSeconds > 0f) Enter(Phase.AfterRoar);
        else Advance();
    }
    private void Move()
    {
        Vector3 delta = route[index].point.position - transform.position; delta.y = 0f;
        if (delta.sqrMagnitude <= 0.04f) { Enter(Phase.Waiting); return; }
        float speed = route[index].runToPoint ? EffectiveRunSpeed : walkSpeed;
        Vector3 next = transform.position + delta.normalized * Mathf.Min(speed * Time.deltaTime, delta.magnitude);
        if (!JourneyGuardGround.TryTerrain(next, maximumSlope, out var ground, out var normal))
        {
            if (!warned) Debug.LogWarning("Guard route leaves walkable Terrain. Adjust the patrol points or Maximum Slope.", this);
            warned = true; return;
        }
        lastNormal = Vector3.Slerp(lastNormal, normal, 8f * Time.deltaTime).normalized;
        Vector3 up = alignToSlope ? lastNormal : Vector3.up;
        Vector3 forward = Vector3.ProjectOnPlane(delta, up).normalized;
        if (forward.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(forward, up), turningSpeed * Time.deltaTime);
            if (Vector3.Angle(transform.forward, forward) > 50f) return;
        }
        transform.position = ground + Vector3.up * groundOffset;
    }
    private void Advance()
    {
        if (pingPong && route.Count > 1)
        { if (index == route.Count - 1) direction = -1; else if (index == 0) direction = 1; index += direction; }
        else index = (index + 1) % route.Count;
        Enter(Phase.Moving);
    }
    private void Enter(Phase next)
    {
        phase = next; movementSource.Stop(); voiceSource.Stop(); voiceSource.loop = false;
        animator.speed = phase == Phase.Moving && route[index].runToPoint ? animationSpeed * runSpeedScale : animationSpeed;
        string state = index == 0 && animator.HasState(0, Animator.StringToHash("Base Layer.Rest")) ? "Rest" : "Idle";
        if (phase == Phase.Moving)
        {
            state = route[index].runToPoint ? "Run" : "Walk";
            var sound = route[index].runToPoint ? runLoop : walkLoop;
            if (sound != null) { movementSource.clip = sound; movementSource.loop = true; movementSource.Play(); }
        }
        else if (phase == Phase.Roaring)
        {
            state = "Roar";
            timer = roarSeconds > 0f ? roarSeconds : Mathf.Max(1f, Mathf.Max(roarAnimation != null ? roarAnimation.length : 0f, roarSound != null ? roarSound.length : 0f));
            if (roarSound != null) voiceSource.PlayOneShot(roarSound);
        }
        else
        {
            timer = phase == Phase.Waiting ? route[index].waitSeconds : route[index].afterRoarSeconds;
            if (idleLoop != null) { voiceSource.clip = idleLoop; voiceSource.loop = true; voiceSource.Play(); }
        }
        activeStateHash = Animator.StringToHash("Base Layer." + state);
        animator.CrossFadeInFixedTime(activeStateHash, 0.15f, 0, 0f);
    }
    private void OnEnable()
    {
        if (!ready) return;
        frozen = false; animator.enabled = originalAnimatorEnabled; Enter(phase); EvaluateDistance(true); ApplyFreeze();
    }
    private void OnDisable()
    {
        if (movementSource != null) movementSource.Stop(); if (voiceSource != null) voiceSource.Stop();
        if (animator != null && ready) { animator.speed = animationSpeed; animator.enabled = originalAnimatorEnabled; }
    }
    private void OnDestroy() { if (overrides != null) Destroy(overrides); }
    private void OnDrawGizmosSelected()
    {
        if (patrolPoints == null) return;
        for (int i = 0; i < patrolPoints.Count; i++)
        {
            var item = patrolPoints[i]; if (item == null || item.point == null) continue;
            Gizmos.color = item.roarHere ? Color.yellow : Color.green; Gizmos.DrawWireSphere(item.point.position, 0.4f);
            int next = i + 1;
            if (next == patrolPoints.Count) { if (pingPong) continue; next = 0; }
            if (patrolPoints[next] != null && patrolPoints[next].point != null) Gizmos.DrawLine(item.point.position, patrolPoints[next].point.position);
        }
    }
}
