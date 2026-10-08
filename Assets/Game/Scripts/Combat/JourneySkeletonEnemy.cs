using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

[DisallowMultipleComponent, RequireComponent(typeof(NavMeshAgent))]
public sealed class JourneySkeletonEnemy : MonoBehaviour
{
    [Header("References")]
    public JourneyPlayerCombat playerCombat;
    public BoxCollider patrolArea;
    public Animator modelAnimator;
    [Header("Patrol and chase")]
    [Min(0f)] public float chaseExtraDistance = 10f;
    [Min(0.1f)] public float patrolSpeed = 1.5f;
    [Min(0.1f)] public float chaseSpeed = 3f;
    [Min(0f)] public float patrolWait = 1.5f;
    [Min(0.1f)] public float thinkInterval = 0.3f;
    [Min(0.1f)] public float sampleDistance = 1.5f;
    [Header("Combat")]
    [Min(1)] public int swordHitsToDie = 3;
    [Min(1)] public int attackDamage = 10;
    [Min(0.1f)] public float attackRange = 1.6f;
    [Min(0f)] public float attackCooldown = 1.2f;
    [Range(0.05f, 0.95f)] public float attackHitTime = 0.45f;
    [Min(0.1f)] public float hitHeight = 0.9f;
    [Min(0.1f)] public float turnSpeed = 360f;
    // Kept only for compatibility with existing Setup scripts and serialized scenes.
    // Corpses now remain in the scene indefinitely.
    [HideInInspector] public float corpseVisibleSeconds = 5f;
    [Header("Optional feedback")]
    public AudioSource effectsSource;
    public AudioClip hitSound, deathSound;
    public UnityEvent onDeath = new UnityEvent();

    private enum Mode { Patrol, Chase, Return, Dead }
    private Mode mode;
    private static readonly HashSet<JourneySkeletonEnemy> enemies = new HashSet<JourneySkeletonEnemy>();
    public static IEnumerable<JourneySkeletonEnemy> ActiveEnemies => enemies;
    public bool IsDead => mode == Mode.Dead;
    public Vector3 HitPoint => transform.position + Vector3.up * hitHeight;
    private NavMeshAgent agent;
    private NavMeshPath path;
    private readonly Vector3[] corners = new Vector3[128];
    private Vector3 home, lastSafe;
    private int receivedHits;
    private bool ready, attacking, attackHitDone, attackObserved, pausedAnimation;
    private float nextThink, waitUntil, nextAttack, attackStarted, savedAnimatorSpeed;
    private string currentAnimation;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { enemies.Clear(); }
    private void OnEnable() { enemies.Add(this); }
    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (playerCombat == null) playerCombat = FindFirstObjectByType<JourneyPlayerCombat>();
        if (patrolArea == null || modelAnimator == null || playerCombat == null)
        { Fail("Assign Player Combat, Patrol Area and Model Animator."); return; }
        if (patrolArea.transform.IsChildOf(transform))
        { Fail("Patrol Area must not move with the enemy."); return; }
        if (Vector3.Dot(patrolArea.transform.up, Vector3.up) < 0.999f)
        { Fail("Patrol Area may rotate around Y only; keep X and Z rotation zero."); return; }
        if (!agent.isOnNavMesh || OutsideDistance(transform.position) > 0.01f)
        { Fail("Place this enemy on the baked NavMesh INSIDE its Patrol Area before Play."); return; }
        foreach (string state in new[] { "Idle", "Run", "Attack", "Death" })
            if (!modelAnimator.HasState(0, Animator.StringToHash("Base Layer." + state)))
            { Fail("Run Setup Journey Combat to create the Skeleton controller."); return; }
        path = new NavMeshPath();
        home = lastSafe = transform.position;
        agent.autoBraking = true;
        agent.autoTraverseOffMeshLink = false;
        agent.autoRepath = false;
        agent.stoppingDistance = 0.1f;
        modelAnimator.applyRootMotion = false;
        mode = Mode.Patrol;
        nextThink = Time.time + Random.Range(0f, thinkInterval);
        ready = true;
        PlayAnimation("Idle");
    }

    private void Update()
    {
        if (!ready) return;
        bool gameplay = playerCombat != null && playerCombat.GameplayActive;
        PauseAnimation(!gameplay);
        if (!gameplay) { StopAgent(false); return; }
        // The non-looping Death state holds its final pose. Keep the model active.
        // Navigation, combat and colliders were already disabled on death.
        if (IsDead) return;
        if (!agent.enabled || !agent.isOnNavMesh) return;
        // Avoidance may deviate from a valid path: enforce the same geometric leash.
        float allowance = mode == Mode.Patrol ? 0f : chaseExtraDistance;
        if (OutsideDistance(transform.position) > allowance + 0.05f)
        {
            agent.Warp(lastSafe);
            StopAgent(true);
            if (mode == Mode.Chase) ReturnHome();
        }
        else lastSafe = transform.position;

        // Test exit every frame, so an attack cannot land after the player leaves the leash.
        if (mode == Mode.Chase && OutsideDistance(playerCombat.BodyPoint) > chaseExtraDistance)
            ReturnHome();
        if (attacking) { UpdateAttack(); return; }
        if (Time.time >= nextThink)
        {
            nextThink = Time.time + Mathf.Max(0.1f, thinkInterval);
            Think();
        }
        if (!attacking) PlayAnimation(agent.velocity.sqrMagnitude > 0.02f ? "Run" : "Idle");
    }

    private void Think()
    {
        if (mode == Mode.Patrol)
        {
            if (PlayerInside()) { mode = Mode.Chase; Chase(); return; }
            agent.speed = patrolSpeed;
            if (agent.hasPath && agent.remainingDistance > agent.stoppingDistance + 0.15f)
            { agent.isStopped = false; return; }
            StopAgent(true);
            if (waitUntil == 0f) waitUntil = Time.time + patrolWait;
            if (Time.time < waitUntil) return;
            waitUntil = 0f;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector3 half = patrolArea.size * 0.5f;
                Vector3 local = patrolArea.center + new Vector3(Random.Range(-half.x, half.x), 0f,
                    Random.Range(-half.z, half.z));
                Vector3 candidate = patrolArea.transform.TransformPoint(local);
                candidate.y = home.y;
                if (TryPath(candidate, 0f)) return;
            }
            waitUntil = Time.time + patrolWait;
        }
        else if (mode == Mode.Chase) Chase();
        else if (mode == Mode.Return)
        {
            agent.speed = patrolSpeed;
            if (FlatDistance(transform.position, home) < 0.35f)
            {
                StopAgent(true);
                mode = Mode.Patrol;
                waitUntil = Time.time + patrolWait;
                return;
            }
            if (!TryPath(home, chaseExtraDistance)) StopAgent(true);
        }
    }

    private void Chase()
    {
        agent.speed = chaseSpeed;
        if (Vector3.Distance(HitPoint, playerCombat.HitPoint) <= attackRange &&
            playerCombat.HasClearLine(HitPoint, playerCombat.HitPoint, transform))
        {
            StopAgent(true);
            FacePlayer();
            if (Time.time >= nextAttack)
            {
                attacking = true;
                attackHitDone = attackObserved = false;
                attackStarted = Time.time;
                PlayAnimation("Attack", true);
            }
            return;
        }
        if (!TryPath(playerCombat.BodyPoint, chaseExtraDistance)) ReturnHome();
    }

    private void UpdateAttack()
    {
        StopAgent(true);
        FacePlayer();
        AnimatorStateInfo state = modelAnimator.GetCurrentAnimatorStateInfo(0);
        if (state.IsName("Base Layer.Attack"))
        {
            attackObserved = true;
            if (!attackHitDone && state.normalizedTime >= attackHitTime)
            {
                attackHitDone = true;
                if (Vector3.Distance(HitPoint, playerCombat.HitPoint) <= attackRange &&
                    OutsideDistance(playerCombat.BodyPoint) <= chaseExtraDistance &&
                    playerCombat.HasClearLine(HitPoint, playerCombat.HitPoint, transform))
                    playerCombat.TakeDamage(attackDamage);
            }
            if (state.normalizedTime >= 1f) FinishAttack();
        }
        else if (attackObserved || Time.time - attackStarted > 0.75f) FinishAttack();
        if (attacking && Time.time - attackStarted > 10f) FinishAttack();
    }

    private void FinishAttack()
    {
        attacking = false;
        nextAttack = Time.time + attackCooldown;
        PlayAnimation("Idle");
    }

    public void ReceiveSwordHit(JourneyPlayerCombat source)
    {
        if (!ready || IsDead || source != playerCombat || !source.GameplayActive) return;
        receivedHits++;
        if (effectsSource != null && hitSound != null) effectsSource.PlayOneShot(hitSound);
        if (receivedHits < Mathf.Max(1, swordHitsToDie)) return;
        mode = Mode.Dead;
        attacking = false;
        StopAgent(true);
        agent.enabled = false;
        foreach (Collider collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
        if (effectsSource != null && deathSound != null) effectsSource.PlayOneShot(deathSound);
        PlayAnimation("Death", true);
        onDeath?.Invoke();
    }

    public void ReturnHome()
    {
        if (!ready || IsDead) return;
        mode = Mode.Return;
        attacking = false;
        nextAttack = Time.time + attackCooldown;
        nextThink = 0f;
        StopAgent(true);
        PlayAnimation("Idle");
    }

    // Y is deliberately ignored for the 10m leash; entry also tests the box's height.
    public float OutsideDistance(Vector3 worldPoint)
    {
        Vector3 local = patrolArea.transform.InverseTransformPoint(worldPoint) - patrolArea.center;
        Vector3 half = patrolArea.size * 0.5f;
        Vector3 nearest = new Vector3(Mathf.Clamp(local.x, -half.x, half.x), local.y,
            Mathf.Clamp(local.z, -half.z, half.z));
        Vector3 difference = patrolArea.transform.TransformVector(local - nearest);
        difference.y = 0f;
        return difference.magnitude;
    }

    private bool PlayerInside()
    {
        Vector3 local = patrolArea.transform.InverseTransformPoint(playerCombat.BodyPoint) - patrolArea.center;
        Vector3 half = patrolArea.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    private bool TryPath(Vector3 destination, float allowance)
    {
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        if (!NavMesh.SamplePosition(destination, out NavMeshHit hit, sampleDistance, filter) ||
            OutsideDistance(hit.position) > allowance + 0.001f) return false;
        if (!agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) return false;
        int count = path.GetCornersNonAlloc(corners);
        if (count == 0 || count == corners.Length) return false;
        // A rectangle plus a distance buffer is convex: segments stay inside when all corners do.
        for (int i = 0; i < count; i++)
            if (OutsideDistance(corners[i]) > allowance + 0.001f) return false;
        agent.isStopped = false;
        return agent.SetPath(path);
    }

    private void FacePlayer()
    {
        Vector3 direction = playerCombat.BodyPoint - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), turnSpeed * Time.deltaTime);
    }
    private static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y; return Vector3.Distance(a, b); }
    private void StopAgent(bool clear)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;
        agent.isStopped = true;
        if (clear && agent.hasPath) agent.ResetPath();
    }
    private void PlayAnimation(string state, bool force = false)
    {
        if (!force && currentAnimation == state) return;
        currentAnimation = state;
        modelAnimator.CrossFadeInFixedTime("Base Layer." + state, 0.1f, 0, 0f);
    }
    private void PauseAnimation(bool pause)
    {
        if (modelAnimator == null || pausedAnimation == pause) return;
        if (pause) { savedAnimatorSpeed = modelAnimator.speed; modelAnimator.speed = 0f; }
        else modelAnimator.speed = savedAnimatorSpeed;
        pausedAnimation = pause;
    }
    private void Fail(string message) { Debug.LogError("JourneySkeletonEnemy: " + message, this); enabled = false; }
    private void OnDisable() { enemies.Remove(this); StopAgent(true); PauseAnimation(false); }
    private void OnDrawGizmosSelected()
    {
        if (patrolArea == null) return;
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = patrolArea.transform.localToWorldMatrix;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(patrolArea.center, patrolArea.size);
        Gizmos.matrix = previous;
    }
}
