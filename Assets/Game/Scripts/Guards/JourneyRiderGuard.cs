using UnityEngine;

// Independent environment guard; its route points must be outside this moving actor.
[DisallowMultipleComponent]
public sealed class JourneyRiderGuard : MonoBehaviour
{
    public Animator animator;
    public Transform homePoint, lookoutPoint;
    public JourneyMenuController menu;
    [Min(0.1f)] public float walkSpeed = 1.8f;
    [Min(0.1f)] public float runSpeed = 4.2f;
    public bool runOnReturn = true;
    [Min(0f)] public float pauseBeforeRoar = 2f;
    [Min(0f)] public float idleAtHomeSeconds = 10f;
    [Min(0f), Tooltip("0 waits for the assigned animation and sound to finish.")]
    public float roarSeconds;
    [Tooltip("The setup assigns Victory; replace with a clip for the same rider rig if desired.")]
    public AnimationClip roarAnimation;
    [Range(1f, 60f)] public float maximumSlope = 35f;
    [Min(0f)] public float groundOffset;
    [Min(1f)] public float turningSpeed = 180f;
    public bool alignToSlope = true;
    [Header("Sounds (optional)")]
    public AudioSource movementSource, voiceSource;
    public AudioClip walkLoop, runLoop, roarSound, idleLoop;
    [Range(0f, 1f)] public float movementVolume = 0.65f;
    [Range(0f, 1f)] public float voiceVolume = 1f;
    private enum Phase { Outward, Pause, Roar, Return, Rest }
    private Phase phase;
    private AnimatorOverrideController overrides;
    private float timer, animationSpeed = 1f;
    private bool ready, paused, warned;
    private Vector3 lastNormal = Vector3.up;
    private void Start()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (menu == null) menu = FindFirstObjectByType<JourneyMenuController>(FindObjectsInactive.Include);
        if (animator == null || homePoint == null || lookoutPoint == null || homePoint.IsChildOf(transform) || lookoutPoint.IsChildOf(transform))
        { Debug.LogError("Guard requires Animator and two route points outside the moving actor.", this); enabled = false; return; }
        foreach (string name in new[] { "Idle", "Walk", "Run", "Roar" })
            if (!animator.HasState(0, Animator.StringToHash("Base Layer." + name)))
            { Debug.LogError("Run Quran Kids > Create Rider Knight Guard Test to assign the patrol controller.", this); enabled = false; return; }
        if (roarAnimation != null)
        {
            overrides = new AnimatorOverrideController(animator.runtimeAnimatorController);
            overrides["Roar"] = roarAnimation;
            animator.runtimeAnimatorController = overrides;
        }
        if (movementSource == null) movementSource = MakeSource();
        if (voiceSource == null || voiceSource == movementSource) voiceSource = MakeSource();
        movementSource.volume = movementVolume; voiceSource.volume = voiceVolume;
        animator.applyRootMotion = false;
        animationSpeed = animator.speed;
        if (JourneyGuardGround.TryTerrain(homePoint.position, maximumSlope, out var home, out lastNormal)) transform.position = home + Vector3.up * groundOffset;
        else transform.position = homePoint.position;
        ready = true;
        Enter(Phase.Outward);
    }
    private AudioSource MakeSource()
    {
        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false; source.spatialBlend = 1f;
        source.minDistance = 6f; source.maxDistance = 70f;
        source.rolloffMode = AudioRolloffMode.Linear;
        return source;
    }
    private void Update()
    {
        if (!ready) return;
        bool shouldPause = Time.timeScale <= 0f || (menu != null && !menu.IsGameplayActive);
        if (shouldPause != paused)
        {
            paused = shouldPause; animator.speed = paused ? 0f : animationSpeed;
            if (paused) { movementSource.Pause(); voiceSource.Pause(); }
            else { movementSource.UnPause(); voiceSource.UnPause(); }
        }
        if (paused) return;
        if (phase == Phase.Outward || phase == Phase.Return)
        {
            Move(phase == Phase.Outward ? lookoutPoint.position : homePoint.position, phase == Phase.Return && runOnReturn ? runSpeed : walkSpeed);
            return;
        }
        timer -= Time.deltaTime;
        if (timer > 0f) return;
        if (phase == Phase.Rest) Enter(Phase.Outward);
        else if (phase == Phase.Pause) Enter(Phase.Roar);
        else if (phase == Phase.Roar) Enter(Phase.Return);
    }
    private void Move(Vector3 destination, float speed)
    {
        Vector3 delta = destination - transform.position; delta.y = 0f;
        if (delta.sqrMagnitude <= 0.04f)
        { Enter(phase == Phase.Outward ? Phase.Pause : Phase.Rest); return; }
        Vector3 next = transform.position + delta.normalized * Mathf.Min(speed * Time.deltaTime, delta.magnitude);
        if (!JourneyGuardGround.TryTerrain(next, maximumSlope, out var ground, out var normal))
        {
            if (!warned) Debug.LogWarning("Guard route leaves a walkable Terrain. Move Home/Lookout onto the hill and check Maximum Slope.", this);
            warned = true; Enter(Phase.Rest); return;
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
    private void Enter(Phase next)
    {
        phase = next;
        movementSource.Stop(); voiceSource.Stop();
        voiceSource.loop = false;
        string state = phase == Phase.Rest && animator.HasState(0, Animator.StringToHash("Base Layer.Rest")) ? "Rest" : "Idle";
        if (phase == Phase.Outward || phase == Phase.Return)
        {
            bool running = phase == Phase.Return && runOnReturn;
            state = running ? "Run" : "Walk";
            AudioClip sound = running ? runLoop : walkLoop;
            if (sound != null) { movementSource.clip = sound; movementSource.loop = true; movementSource.Play(); }
        }
        else if (phase == Phase.Roar)
        {
            state = "Roar";
            timer = roarSeconds > 0f ? roarSeconds : Mathf.Max(1f, Mathf.Max(roarAnimation != null ? roarAnimation.length : 0f, roarSound != null ? roarSound.length : 0f));
            if (roarSound != null) voiceSource.PlayOneShot(roarSound);
        }
        else
        {
            timer = phase == Phase.Pause ? pauseBeforeRoar : idleAtHomeSeconds;
            if (idleLoop != null) { voiceSource.clip = idleLoop; voiceSource.loop = true; voiceSource.Play(); }
        }
        animator.CrossFadeInFixedTime("Base Layer." + state, 0.15f, 0, 0f);
    }
    private void OnEnable()
    {
        if (!ready) return;
        paused = false; animator.speed = animationSpeed;
        Enter(Phase.Rest);
    }
    private void OnDisable()
    {
        if (movementSource != null) movementSource.Stop();
        if (voiceSource != null) voiceSource.Stop();
        if (animator != null && ready) animator.speed = animationSpeed;
    }
    private void OnDestroy()
    {
        if (overrides != null) Destroy(overrides);
    }
    private void OnDrawGizmosSelected()
    {
        if (homePoint == null || lookoutPoint == null) return;
        Gizmos.color = Color.yellow; Gizmos.DrawLine(homePoint.position, lookoutPoint.position);
        Gizmos.color = Color.green; Gizmos.DrawWireSphere(homePoint.position, 0.5f);
        Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(lookoutPoint.position, 0.5f);
    }
}
