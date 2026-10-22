using System.Collections.Generic;
using GameCreator.Runtime.Characters;
using RTLTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class JourneyPlayerCombat : MonoBehaviour
{
    public const string LayerName = "Journey Sword Actions";
    public const string AttackState = "Journey Sword Actions.SwordAttack";
    [Header("Scene references")]
    public Character player;
    public Animator playerAnimator;
    public JourneyMenuController menu;
    public Transform startPoint;
    public JourneyStoneThrowController stoneController;
    public Button attackButton;
    [Tooltip("Hide attack and defense while picking up, holding or throwing a stone.")]
    public bool hideCombatButtonsWhileUsingStone = true;
    private sealed class HiddenButton
    {
        public CanvasGroup group;
        public float alpha;
        public bool blocksRaycasts;
    }
    private readonly Dictionary<Button, HiddenButton> hiddenStoneButtons = new Dictionary<Button, HiddenButton>();
    public const string DefenseState = "Journey Sword Actions.ShieldBlock";
    [Header("Shield defense")]
    public Button defendButton;
    public BoxCollider shieldHitbox;
    [Tooltip("The Player's body collider / CharacterController, not the shield.")]
    public Collider bodyHitbox;
    [Min(0.1f)] public float defenseSpeed = 0.65f;
    [Range(0f, 1f)] public float defenseStart = 0.15f;
    [Range(0f, 1f)] public float defenseEnd = 0.85f;
    [Min(0f)] public float defenseCooldown = 0.15f;
    public AudioClip shieldBlockSound;
    public UnityEvent onShieldBlock = new UnityEvent();
    public bool IsDefending { get; private set; }
    private bool defenseReady, observedDefense;
    private float defenseStarted, nextDefenseAt;
    private int defenseRequestFrame;
    public bool ShieldActive
    {
        get
        {
            if (!IsDefending || !GameplayActive || playerAnimator == null ||
                playerAnimator.runtimeAnimatorController != expectedController ||
                !playerAnimator.isActiveAndEnabled || Time.frameCount <= defenseRequestFrame ||
                shieldHitbox == null || !shieldHitbox.enabled || !shieldHitbox.gameObject.activeInHierarchy) return false;
            var state = playerAnimator.GetCurrentAnimatorStateInfo(layer);
            return state.IsName(DefenseState) && state.normalizedTime >= defenseStart &&
                state.normalizedTime <= Mathf.Max(defenseStart, defenseEnd);
        }
    }
    [Header("Health UI")]
    public Image healthFill;
    public RTLTextMeshPro healthPercentText;
    [Min(1)] public int maximumHealth = 100;
    [Min(0f)] public float hitInvulnerability = 0.45f;
    [Min(0f)] public float respawnInvulnerability = 2f;
    [Header("Sword - hit timing is normalized clip time")]
    [Min(0.1f)] public float attackSpeed = 1f;
    [Range(0f, 1f)] public float hitWindowStart = 0.3f;
    [Range(0f, 1f)] public float hitWindowEnd = 0.6f;
    [Min(0.1f)] public float swordReach = 2f;
    [Range(10f, 180f)] public float swordArc = 110f;
    [Tooltip("Offset above the Game Creator Player root (usually already at capsule centre).")]
    [Min(0f)] public float hitHeight = 0.3f;
    [Min(0f)] public float attackCooldown = 0.15f;
    [Tooltip("Solid walls and scenery. Keep terrain and walls included.")]
    public LayerMask obstructionLayers = ~0;
    [Header("Optional sounds and events")]
    public AudioSource effectsSource;
    public AudioClip swingSound, hurtSound;
    public UnityEvent<int> onHealthChanged = new UnityEvent<int>();
    public UnityEvent onRespawn = new UnityEvent();

    public int CurrentHealth { get; private set; }
    public bool IsAttacking { get; private set; }
    // World simulation must not depend on the on-foot combat component being enabled.
    public bool WorldGameplayActive => ready && focused && !paused &&
        menu != null && menu.IsGameplayActive && Time.timeScale > 0f;
    public bool GameplayActive => ready && isActiveAndEnabled && focused && !paused && !JourneyRadialMenu.IsOpen &&
        menu != null && menu.IsGameplayActive && Time.timeScale > 0f;
    public Vector3 BodyPoint => player.transform.position;
    public Vector3 HitPoint => player.transform.position + Vector3.up * hitHeight;
    public Transform PlayerTransform => player.transform;
    public static bool IsMovementBlocked => activeAttacker != null && (activeAttacker.IsAttacking || activeAttacker.IsDefending);
    private static JourneyPlayerCombat activeAttacker;
    private readonly HashSet<JourneySkeletonEnemy> hitThisSwing = new HashSet<JourneySkeletonEnemy>();
    private readonly RaycastHit[] sightHits = new RaycastHit[64];
    private int layer = -1;
    private int attackRequestFrame = -1;
    private bool ready, focused = true, paused, observedAttack;
    private float invulnerableUntil, nextAttackAt, attackStarted, previousAttackTime;
    private RuntimeAnimatorController expectedController;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() { activeAttacker = null; }

    private void OnEnable()
    {
        if (attackButton != null) attackButton.onClick.AddListener(TryAttack);
        if (defendButton != null) defendButton.onClick.AddListener(TryDefend);
    }
    private void Start()
    {
        if (player == null) player = GetComponent<Character>();
        if (menu == null) menu = FindFirstObjectByType<JourneyMenuController>();
        if (player == null || playerAnimator == null || menu == null || startPoint == null || attackButton == null)
        { Fail("Assign Player, Player Animator, Menu, Start Point and Attack Button."); return; }
        if (!RebindCharacterAnimator(player.Animim.Animator != null ? player.Animim.Animator : playerAnimator))
        { Fail("Run Quran Kids > Setup Journey Combat using the player's current Animator."); return; }
        if (bodyHitbox == null) bodyHitbox = player.GetComponent<Collider>();
        defenseReady = defendButton != null && shieldHitbox != null && bodyHitbox != null &&
            playerAnimator.HasState(layer, Animator.StringToHash(DefenseState));
        if (defendButton != null && !defenseReady)
            Debug.LogWarning("Shield: assign body/shield colliders and run Setup Journey Shield.", this);
        CurrentHealth = Mathf.Max(1, maximumHealth);
        ready = true;
        RefreshHealth();
    }

    public bool RebindCharacterAnimator(Animator replacement)
    {
        EndDefense(); EndAttack(); playerAnimator = replacement;
        layer = replacement != null ? replacement.GetLayerIndex(LayerName) : -1;
        expectedController = replacement != null ? replacement.runtimeAnimatorController : null;
        ready = replacement != null && layer >= 0 && replacement.HasState(layer, Animator.StringToHash(AttackState)) &&
            player != null && menu != null && startPoint != null && attackButton != null;
        if (replacement != null) replacement.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        defenseReady = ready && defendButton != null && shieldHitbox != null && bodyHitbox != null && replacement.HasState(layer, Animator.StringToHash(DefenseState));
        return ready;
    }

    private void Update()
    {
        if (!ready) return;
        if (attackButton != null) attackButton.interactable = CanAttack();
        if (defendButton != null) defendButton.interactable = CanDefend();
        UpdateDefense();
        if (!IsAttacking) return;
        if (!GameplayActive || playerAnimator == null || !playerAnimator.isActiveAndEnabled ||
            playerAnimator.runtimeAnimatorController != expectedController)
        { EndAttack(); return; }
        // Wait for the Animator to evaluate this new playback request.
        if (Time.frameCount <= attackRequestFrame) return;
        AnimatorStateInfo state = playerAnimator.GetCurrentAnimatorStateInfo(layer);
        if (state.fullPathHash == Animator.StringToHash(AttackState))
        {
            observedAttack = true;
            float time = state.normalizedTime;
            // Also catches a hit window crossed between two frames at low frame rates.
            if (time >= hitWindowStart && previousAttackTime <= Mathf.Max(hitWindowStart, hitWindowEnd)) ApplySwordHits();
            previousAttackTime = time;
            playerAnimator.SetLayerWeight(layer, Mathf.Min(Mathf.Clamp01(time / 0.06f), Mathf.Clamp01((1f - time) / 0.08f)));
            if (time >= 1f) EndAttack();
        }
        else if (observedAttack || Time.time - attackStarted > 0.75f) EndAttack();
        // Covers a changed controller or a paused external animation graph.
        if (IsAttacking && Time.time - attackStarted > 15f) EndAttack();
    }

    private void LateUpdate()
    {
        bool hide = ready && hideCombatButtonsWhileUsingStone && stoneController != null &&
            stoneController.isActiveAndEnabled && stoneController.IsUsingStone;
        if (!hide) { RestoreStoneButtons(); return; }
        HideStoneButton(attackButton);
        HideStoneButton(defendButton);
    }
    private void HideStoneButton(Button button)
    {
        if (button == null) return;
        if (!hiddenStoneButtons.TryGetValue(button, out HiddenButton saved))
        {
            var group = button.GetComponent<CanvasGroup>();
            if (group == null) group = button.gameObject.AddComponent<CanvasGroup>();
            saved = new HiddenButton { group = group, alpha = group.alpha, blocksRaycasts = group.blocksRaycasts };
            hiddenStoneButtons.Add(button, saved);
        }
        if (saved.group == null) return;
        saved.group.alpha = 0f;
        saved.group.blocksRaycasts = false;
    }
    private void RestoreStoneButtons()
    {
        foreach (var saved in hiddenStoneButtons.Values)
        {
            if (saved.group == null) continue;
            saved.group.alpha = saved.alpha;
            saved.group.blocksRaycasts = saved.blocksRaycasts;
        }
        hiddenStoneButtons.Clear();
    }

    private bool CanAttack()
    {
        return GameplayActive && !IsAttacking && !IsDefending && Time.time >= nextAttackAt &&
            !LightBrushPuzzle.IsMovementBlocked && !JourneyVoiceBoard.IsPointerCaptured &&
            (stoneController == null || !stoneController.IsUsingStone) &&
            playerAnimator != null && playerAnimator.isActiveAndEnabled &&
            playerAnimator.runtimeAnimatorController == expectedController && playerAnimator.speed > 0f;
    }

    public void TryAttack()
    {
        if (!CanAttack()) return;
        IsAttacking = true;
        activeAttacker = this;
        observedAttack = false;
        previousAttackTime = 0f;
        hitThisSwing.Clear();
        attackStarted = Time.time;
        attackRequestFrame = Time.frameCount;
        RightScreenDragArea.ClearInput();
        JourneyMovementSettings.RefreshPuzzleInput();
        playerAnimator.SetFloat("JourneySwordSpeed", Mathf.Max(0.1f, attackSpeed));
        // Restart directly: self-crossfade can expose the finished previous state.
        // Update blends the layer weight at the beginning and end of the clip.
        playerAnimator.SetLayerWeight(layer, 0f);
        playerAnimator.Play(AttackState, layer, 0f);
        if (effectsSource != null && swingSound != null) effectsSource.PlayOneShot(swingSound);
    }

    private bool CanDefend()
    {
        return defenseReady && GameplayActive && !IsAttacking && !IsDefending && Time.time >= nextDefenseAt &&
            !LightBrushPuzzle.IsMovementBlocked && !JourneyVoiceBoard.IsPointerCaptured &&
            (stoneController == null || !stoneController.IsUsingStone) && playerAnimator != null &&
            playerAnimator.isActiveAndEnabled && playerAnimator.speed > 0f &&
            playerAnimator.runtimeAnimatorController == expectedController;
    }

    public void TryDefend()
    {
        if (!CanDefend()) return;
        IsDefending = true;
        activeAttacker = this;
        observedDefense = false;
        defenseStarted = Time.time;
        defenseRequestFrame = Time.frameCount;
        RightScreenDragArea.ClearInput();
        JourneyMovementSettings.RefreshPuzzleInput();
        playerAnimator.SetFloat("JourneyShieldSpeed", Mathf.Max(0.1f, defenseSpeed));
        playerAnimator.SetLayerWeight(layer, 0f);
        playerAnimator.Play(DefenseState, layer, 0f);
    }

    private void UpdateDefense()
    {
        if (!IsDefending) return;
        if (!GameplayActive || playerAnimator == null || !playerAnimator.isActiveAndEnabled ||
            playerAnimator.runtimeAnimatorController != expectedController)
        { EndDefense(); return; }
        if (Time.frameCount <= defenseRequestFrame) return;
        var state = playerAnimator.GetCurrentAnimatorStateInfo(layer);
        if (state.IsName(DefenseState))
        {
            observedDefense = true;
            float t = state.normalizedTime;
            playerAnimator.SetLayerWeight(layer, Mathf.Min(Mathf.Clamp01(t / 0.06f), Mathf.Clamp01((1f - t) / 0.08f)));
            if (t >= 1f) EndDefense();
        }
        else if (observedDefense || Time.time - defenseStarted > 0.75f) EndDefense();
        if (IsDefending && Time.time - defenseStarted > 15f) EndDefense();
    }

    private void EndDefense()
    {
        if (!IsDefending) return;
        if (playerAnimator != null && playerAnimator.runtimeAnimatorController == expectedController && layer >= 0)
            playerAnimator.SetLayerWeight(layer, 0f);
        IsDefending = false;
        if (activeAttacker == this) activeAttacker = null;
        nextDefenseAt = Time.time + Mathf.Max(0f, defenseCooldown);
        JourneyMovementSettings.RefreshPuzzleInput();
    }

    public void NotifyShieldBlock()
    {
        if (effectsSource != null && shieldBlockSound != null) effectsSource.PlayOneShot(shieldBlockSound);
        onShieldBlock?.Invoke();
    }

    private void ApplySwordHits()
    {
        Vector3 forward = playerAnimator.transform.forward;
        forward.y = 0f;
        foreach (JourneySkeletonEnemy enemy in JourneySkeletonEnemy.ActiveEnemies)
        {
            if (enemy == null || enemy.IsDead || hitThisSwing.Contains(enemy)) continue;
            Vector3 delta = enemy.HitPoint - HitPoint;
            if (delta.sqrMagnitude > swordReach * swordReach) continue;
            delta.y = 0f;
            if (delta.sqrMagnitude > 0.001f && Vector3.Angle(forward, delta) > swordArc * 0.5f) continue;
            if (!HasClearLine(HitPoint, enemy.HitPoint, enemy.transform)) continue;
            hitThisSwing.Add(enemy);
            enemy.ReceiveSwordHit(this);
        }
    }

    // Shared by both sides: skip their own colliders, but never hit through scenery.
    public bool HasClearLine(Vector3 from, Vector3 to, Transform enemyRoot)
    {
        Vector3 direction = to - from;
        float distance = direction.magnitude;
        if (distance < 0.001f) return true;
        int count = Physics.RaycastNonAlloc(from, direction / distance, sightHits, distance,
            obstructionLayers, QueryTriggerInteraction.Ignore);
        if (count == sightHits.Length) return false;
        for (int i = 0; i < count; i++)
        {
            Transform hit = sightHits[i].transform;
            if (hit.IsChildOf(player.transform) || hit.IsChildOf(enemyRoot)) continue;
            return false;
        }
        return true;
    }

    public void TakeDamage(int damage)
    {
        if (!GameplayActive || damage <= 0 || Time.time < invulnerableUntil) return;
        CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
        invulnerableUntil = Time.time + Mathf.Max(0f, hitInvulnerability);
        if (effectsSource != null && hurtSound != null) effectsSource.PlayOneShot(hurtSound);
        RefreshHealth();
        if (CurrentHealth == 0) Respawn();
    }

    private void Respawn()
    {
        EndDefense();
        EndAttack();
        if (stoneController != null) stoneController.CancelForRespawn();
        foreach (JourneySkeletonEnemy enemy in JourneySkeletonEnemy.ActiveEnemies)
            if (enemy != null) enemy.ReturnHome();
        player.Driver.SetPosition(startPoint.position);
        player.Driver.SetRotation(startPoint.rotation);
        CurrentHealth = Mathf.Max(1, maximumHealth);
        invulnerableUntil = Time.time + Mathf.Max(0f, respawnInvulnerability);
        RightScreenDragArea.ClearInput();
        JourneyMovementSettings.RefreshPuzzleInput();
        RefreshHealth();
        onRespawn?.Invoke();
    }

    private void RefreshHealth()
    {
        float ratio = CurrentHealth / (float)Mathf.Max(1, maximumHealth);
        if (healthFill != null) healthFill.fillAmount = ratio;
        if (healthPercentText != null) healthPercentText.text = Mathf.RoundToInt(ratio * 100f) + "%";
        onHealthChanged?.Invoke(CurrentHealth);
    }

    private void EndAttack()
    {
        if (!IsAttacking) return;
        if (playerAnimator != null && playerAnimator.runtimeAnimatorController == expectedController && layer >= 0)
            playerAnimator.SetLayerWeight(layer, 0f);
        IsAttacking = false;
        if (activeAttacker == this) activeAttacker = null;
        nextAttackAt = Time.time + Mathf.Max(0f, attackCooldown);
        JourneyMovementSettings.RefreshPuzzleInput();
    }
    private void Fail(string message) { Debug.LogError("JourneyPlayerCombat: " + message, this); enabled = false; }
    private void OnDisable()
    {
        RestoreStoneButtons();
        if (attackButton != null) { attackButton.onClick.RemoveListener(TryAttack); attackButton.interactable = false; }
        if (defendButton != null) { defendButton.onClick.RemoveListener(TryDefend); defendButton.interactable = false; }
        EndDefense();
        EndAttack();
    }
    private void OnApplicationFocus(bool value) { focused = value; if (!value) { EndDefense(); EndAttack(); } }
    private void OnApplicationPause(bool value) { paused = value; if (value) { EndDefense(); EndAttack(); } }
}
