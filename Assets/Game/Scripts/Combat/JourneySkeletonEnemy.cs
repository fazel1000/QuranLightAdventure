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
    [Tooltip("Optional ordered route. Keep the points outside the moving enemy, e.g. under a sibling Patrol Route object.")]
    public Transform[] patrolPoints = new Transform[0];
    public bool loopPatrol = true;
    [Min(0.5f)] public float routePadding = 4f;
    public bool useDistanceDetection = true;
    [Min(0.5f)] public float detectionDistance = 8f;
    [Min(0.5f)] public float loseTargetDistance = 14f;
    [Tooltip("Far enemies stop navigation and Animator evaluation; they resume when the player returns.")]
    [Min(10f)] public float sleepDistance = 60f;
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
    [Header("Weapon contacts - both points must follow the animated blade")]
    public Transform bladeBase;
    public Transform bladeTip;
    [Min(0.01f)] public float bladeRadius = 0.06f;
    [Range(0f, 1f)] public float attackHitEnd = 0.8f;
    [Tooltip("Track the FIRST contact throughout the swing, including before Attack Hit Time.")]
    public bool checkWholeSwing = true;
    [Tooltip("Logs body/shield/miss and health changes once per attack. Disable after testing.")]
    public bool debugContacts;
    private bool warnedBodyHitbox;
    private readonly JourneyBladeContacts bladeContacts = new JourneyBladeContacts();
    private bool bladeHistory;
    private Vector3 previousBladeBase, previousBladeTip;
    private float previousSwingTime;
    private int swingRequestFrame;
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
    private int routeIndex, routeDirection = 1;
    private bool routeTravelling, sleeping, animatorBeforeSleep;
    private bool HasRoute => patrolPoints != null && System.Array.Exists(patrolPoints, point => point != null);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { enemies.Clear(); }
    private void OnEnable() { enemies.Add(this); }
    private void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (playerCombat == null) playerCombat = FindFirstObjectByType<JourneyPlayerCombat>();
        if ((!HasRoute && patrolArea == null) || modelAnimator == null || playerCombat == null)
        { Fail("Assign Player Combat, Model Animator, and Patrol Points or a Patrol Area."); return; }
        if (patrolArea != null && patrolArea.transform.IsChildOf(transform))
        { Fail("Patrol Area must not move with the enemy."); return; }
        if (patrolArea != null && Vector3.Dot(patrolArea.transform.up, Vector3.up) < 0.999f)
        { Fail("Patrol Area may rotate around Y only; keep X and Z rotation zero."); return; }
        if (HasRoute && System.Array.Exists(patrolPoints, point => point != null && point.IsChildOf(transform)))
        { Fail("Patrol Points must be outside the moving enemy hierarchy. Use a sibling Patrol Route."); return; }
        if (!agent.isOnNavMesh || OutsideDistance(transform.position) > 0.01f)
        { Fail("Place this enemy on the baked NavMesh inside its Patrol Points bounds or Patrol Area before Play."); return; }
        foreach (string state in new[] { "Idle", "Run", "Attack", "Death" })
            if (!modelAnimator.HasState(0, Animator.StringToHash("Base Layer." + state)))
            { Fail("Run Setup Journey Combat to create the Skeleton controller."); return; }
        if ((bladeBase == null) != (bladeTip == null))
        { Fail("Assign BOTH Blade Base and Blade Tip."); return; }
        if (bladeBase == null)
            Debug.LogWarning("Enemy has no blade markers: using legacy timed damage; shield cannot block this enemy until markers are assigned.", this);
        if (bladeBase != null)
        {
            if (!bladeBase.IsChildOf(transform) || !bladeTip.IsChildOf(transform))
            { Fail("Blade markers must belong to this enemy's animated weapon."); return; }
            modelAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        path = new NavMeshPath();
        home = lastSafe = transform.position;
        routeIndex = NearestRoutePoint();
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
        bool gameplay = playerCombat != null && playerCombat.WorldGameplayActive;
        PauseAnimation(!gameplay);
        if (!gameplay) { bladeHistory = false; StopAgent(false); return; }
        // The non-looping Death state holds its final pose. Keep the model active.
        // Navigation, combat and colliders were already disabled on death.
        if (IsDead) return;
        bool distant = Vector3.SqrMagnitude(playerCombat.BodyPoint - transform.position) > sleepDistance * sleepDistance;
        if (distant)
        {
            StopAgent(true);
            if (!sleeping) { if (attacking) FinishAttack(); sleeping = true; animatorBeforeSleep = modelAnimator.enabled; modelAnimator.enabled = false; }
            return;
        }
        if (sleeping)
        { sleeping = false; modelAnimator.enabled = animatorBeforeSleep; currentAnimation = null; nextThink = 0f; routeTravelling = false; }
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
        if (mode == Mode.Chase && (OutsideDistance(playerCombat.BodyPoint) > chaseExtraDistance ||
            useDistanceDetection && FlatDistance(transform.position, playerCombat.BodyPoint) > Mathf.Max(detectionDistance + 1f, loseTargetDistance)))
            ReturnHome();
        if (attacking && !playerCombat.GameplayActive) FinishAttack();
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
        if (mode == Mode.Return && PlayerInside()) { mode = Mode.Chase; Chase(); return; }
        if (mode == Mode.Patrol)
        {
            if (PlayerInside()) { mode = Mode.Chase; Chase(); return; }
            agent.speed = patrolSpeed;
            if (HasRoute) { PatrolRoute(); return; }
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
                routeIndex = NearestRoutePoint(); routeTravelling = false;
                waitUntil = Time.time + patrolWait;
                return;
            }
            if (!TryPath(home, chaseExtraDistance)) StopAgent(true);
        }
    }
    private void PatrolRoute()
    {
        if (agent.pathPending) return;
        if (routeTravelling)
        {
            if (agent.hasPath && agent.remainingDistance > agent.stoppingDistance + 0.2f) { agent.isStopped = false; return; }
            routeTravelling = false; StopAgent(true); AdvanceRoutePoint(); waitUntil = Time.time + patrolWait; return;
        }
        if (Time.time < waitUntil) return;
        for (int attempt = 0; attempt < patrolPoints.Length; attempt++)
        {
            Transform point = patrolPoints[Mathf.Clamp(routeIndex, 0, patrolPoints.Length - 1)];
            if (point != null && TryPath(point.position, 0f)) { routeTravelling = true; return; }
            AdvanceRoutePoint();
        }
        waitUntil = Time.time + Mathf.Max(0.3f, patrolWait);
    }
    private int NearestRoutePoint()
    {
        int best = 0; float distance = float.PositiveInfinity;
        if (patrolPoints == null) return best;
        for (int i = 0; i < patrolPoints.Length; i++)
        {
            if (patrolPoints[i] == null) continue;
            float next = Vector3.SqrMagnitude(patrolPoints[i].position - transform.position);
            if (next < distance) { best = i; distance = next; }
        }
        return best;
    }
    private void AdvanceRoutePoint()
    {
        if (patrolPoints.Length <= 1) { routeIndex = 0; return; }
        if (loopPatrol) routeIndex = (routeIndex + 1) % patrolPoints.Length;
        else
        {
            routeIndex += routeDirection;
            if (routeIndex >= patrolPoints.Length) { routeDirection = -1; routeIndex = patrolPoints.Length - 2; }
            else if (routeIndex < 0) { routeDirection = 1; routeIndex = 1; }
        }
    }

    private void Chase()
    {
        agent.speed = chaseSpeed;
        if (playerCombat.GameplayActive && Vector3.Distance(HitPoint, playerCombat.HitPoint) <= attackRange &&
            playerCombat.HasClearLine(HitPoint, playerCombat.HitPoint, transform))
        {
            StopAgent(true);
            FacePlayer();
            if (Time.time >= nextAttack)
            {
                attacking = true;
                attackHitDone = attackObserved = false;
                attackStarted = Time.time;
                previousSwingTime = 0f;
                bladeHistory = false;
                swingRequestFrame = Time.frameCount;
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
    }

    // Query after animation evaluation, using the visible blade pose.
    private void LateUpdate()
    {
        if (!ready || IsDead || !attacking || playerCombat == null || !playerCombat.GameplayActive) return;
        if (Time.frameCount <= swingRequestFrame) return;
        var state = modelAnimator.GetCurrentAnimatorStateInfo(0);
        if (state.IsName("Base Layer.Attack"))
        {
            attackObserved = true;
            float t = state.normalizedTime;
            bool physical = bladeBase != null && bladeTip != null;
            float windowStart = physical && checkWholeSwing ? 0f : attackHitTime;
            float windowEnd = physical && checkWholeSwing ? 1f : Mathf.Max(attackHitTime, attackHitEnd);
            bool crossedWindow = t >= windowStart && previousSwingTime <= windowEnd;
            if (physical && !warnedBodyHitbox && (playerCombat.bodyHitbox == null ||
                !playerCombat.bodyHitbox.enabled || !playerCombat.bodyHitbox.gameObject.activeInHierarchy))
            {
                warnedBodyHitbox = true;
                Debug.LogWarning("Combat: Player Body Hitbox is missing or disabled. Assign the enabled CombatBodyHitbox capsule in JourneyPlayerCombat.", playerCombat);
            }
            bool inRange = OutsideDistance(playerCombat.BodyPoint) <= chaseExtraDistance &&
                Vector3.Distance(HitPoint, playerCombat.HitPoint) <= attackRange + (physical ? 1f : 0f) &&
                playerCombat.HasClearLine(HitPoint, playerCombat.HitPoint, transform);
            if (!attackHitDone && crossedWindow && inRange)
            {
                if (physical)
                {
                    // Clip the swept interval to the active damage window.
                    Vector3 a = bladeBase.position, b = bladeTip.position;
                    Vector3 fromA = bladeHistory ? previousBladeBase : a;
                    Vector3 fromB = bladeHistory ? previousBladeTip : b;
                    float dt = t - previousSwingTime;
                    if (bladeHistory && dt > 0.00001f)
                    {
                        float begin = Mathf.Clamp01((windowStart - previousSwingTime) / dt);
                        float finish = Mathf.Clamp01((windowEnd - previousSwingTime) / dt);
                        a = Vector3.Lerp(previousBladeBase, bladeBase.position, finish);
                        b = Vector3.Lerp(previousBladeTip, bladeTip.position, finish);
                        fromA = Vector3.Lerp(previousBladeBase, bladeBase.position, begin);
                        fromB = Vector3.Lerp(previousBladeTip, bladeTip.position, begin);
                    }
                    int contact = bladeContacts.Check(fromA, fromB, a, b, bladeRadius, playerCombat);
                    if (contact != 0)
                    {
                        attackHitDone = true;
                        if (contact == 1)
                        {
                            if (debugContacts) Debug.Log(name + ": FIRST CONTACT = SHIELD; this swing is blocked.", this);
                            playerCombat.NotifyShieldBlock();
                        }
                        else if (contact == 2)
                        {
                            int before = playerCombat.CurrentHealth;
                            playerCombat.TakeDamage(attackDamage);
                            if (debugContacts) Debug.Log(name + ": FIRST CONTACT = BODY; HP " + before + " -> " +
                                playerCombat.CurrentHealth + ". Unchanged HP may mean hit/respawn invulnerability; full HP may mean respawn.", this);
                        }
                    }
                }
                else
                {
                    attackHitDone = true;
                    playerCombat.TakeDamage(attackDamage);
                }
            }
            if (physical)
            {
                previousBladeBase = bladeBase.position;
                previousBladeTip = bladeTip.position;
                bladeHistory = true;
            }
            previousSwingTime = t;
            if (t >= 1f)
            {
                if (debugContacts && !attackHitDone && physical)
                    Debug.Log(name + ": NO VALID CONTACT. Check body capsule, blade markers, range, and wall mask.", this);
                FinishAttack();
            }
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
        if (sleeping)
        { sleeping = false; modelAnimator.enabled = animatorBeforeSleep; currentAnimation = null; nextThink = 0f; routeTravelling = false; }
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
        routeTravelling = false;
        nextAttack = Time.time + attackCooldown;
        nextThink = 0f;
        StopAgent(true);
        PlayAnimation("Idle");
    }

    // Y is deliberately ignored for the 10m leash; entry also tests the box's height.
    public float OutsideDistance(Vector3 worldPoint)
    {
        if (HasRoute)
        {
            Bounds bounds = new Bounds(home, Vector3.zero); bool found = false;
            foreach (var point in patrolPoints)
            { if (point == null) continue; if (!found) { bounds = new Bounds(point.position, Vector3.zero); found = true; } else bounds.Encapsulate(point.position); }
            bounds.Expand(routePadding * 2f);
            Vector3 closest = bounds.ClosestPoint(worldPoint); closest.y = worldPoint.y;
            return Vector3.Distance(closest, worldPoint);
        }
        if (patrolArea == null) return FlatDistance(worldPoint, home);
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
        if (useDistanceDetection)
            return playerCombat.GameplayActive && FlatDistance(transform.position, playerCombat.BodyPoint) <= detectionDistance &&
                OutsideDistance(playerCombat.BodyPoint) <= chaseExtraDistance && playerCombat.HasClearLine(HitPoint, playerCombat.HitPoint, transform);
        if (patrolArea == null) return false;
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
        if (modelAnimator == null || !modelAnimator.isActiveAndEnabled) return;
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
    private void OnDisable()
    { enemies.Remove(this); StopAgent(true); if (sleeping && modelAnimator != null) modelAnimator.enabled = animatorBeforeSleep; sleeping = false; PauseAnimation(false); }
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.7f, 0.1f, 0.8f); Gizmos.DrawWireSphere(transform.position, detectionDistance);
        if (patrolPoints != null)
        {
            Gizmos.color = Color.cyan; Transform previousPatrolPoint = null;
            foreach (var point in patrolPoints)
            { if (point == null) continue; Gizmos.DrawWireSphere(point.position, 0.2f); if (previousPatrolPoint != null) Gizmos.DrawLine(previousPatrolPoint.position, point.position); previousPatrolPoint = point; }
            if (loopPatrol && previousPatrolPoint != null && patrolPoints.Length > 1 && patrolPoints[0] != null) Gizmos.DrawLine(previousPatrolPoint.position, patrolPoints[0].position);
        }
        if (bladeBase != null && bladeTip != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(bladeBase.position, bladeTip.position);
            Gizmos.DrawWireSphere(bladeBase.position, bladeRadius);
            Gizmos.DrawWireSphere(bladeTip.position, bladeRadius);
        }
        if (patrolArea == null) return;
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = patrolArea.transform.localToWorldMatrix;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(patrolArea.center, patrolArea.size);
        Gizmos.matrix = previous;
    }
}
