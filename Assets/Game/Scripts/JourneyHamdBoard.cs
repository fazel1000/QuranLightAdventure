using DG.Tweening;
using UnityEngine;

// Shared by Hamd, Falagh and Naas boards. Keep the component name for existing scene references.
// One crystal belongs to one board. State is local to this scene instance.
[DisallowMultipleComponent, RequireComponent(typeof(JourneyVoiceBoard))]
public sealed class JourneyHamdBoard : MonoBehaviour
{
    public const string CrystalLockedMessage = "لطفاً کریستال این سوره را پیدا کن و در جای خود قرار بده";
    public const string CrystalCarriedMessage = "کریستال پیدا شد؛ نزدیک تابلو برو و جای کریستال را لمس کن";
    public enum CrystalState { InWorld, Carried, Installed }
    public CrystalState State { get; private set; }
    public bool IsActivated => State == CrystalState.Installed;

    [Header("Scene references")]
    public JourneyVoiceBoard voiceBoard;
    public Transform player;
    public JourneyMenuController menu;
    public JourneyStoneThrowController stoneController;
    public JourneyRadialMenu radialMenu;
    [Min(0.1f)] public float interactionDistance = 5f;

    [Header("Crystal in the world and its socket on this board")]
    public Transform crystal;
    public Collider[] crystalColliders;
    public Transform crystalPosition;
    public Collider socketCollider;
    [Tooltip("Dim crystal copy marking the clickable socket. Hidden after installation.")]
    public GameObject socketVisual;
    public Color socketColor = new Color(0.12f, 0.28f, 0.32f, 1f);
    public Sprite crystalIcon;
    [Header("Crystal name in radial inventory")]
    public string persianCrystalName = "کریستال سورهٔ حمد";
    public string englishCrystalName = "Al-Fatiha crystal";
    public string arabicCrystalName = "بلورة سورة الفاتحة";
    [Header("Crystal placement and sound")]
    public Vector3 placedLocalEulerAngles;
    public Vector3 placedLocalScale = Vector3.one;
    public AudioSource effectsSource;
    public AudioClip pickupSound;
    public AudioClip installationSound;
    [Range(0f, 1f)] public float effectsVolume = 1f;

    [Header("Next / previous 3D buttons")]
    public Collider upCollider;
    public Collider downCollider;
    public Renderer[] upRenderers;
    public Renderer[] downRenderers;
    public Color disabledColor = new Color(0.25f, 0.25f, 0.25f, 1f);
    [Tooltip("Button movement in each button's parent coordinates.")]
    public Vector3 pressedOffset = new Vector3(0f, 0f, 0.04f);
    [Min(0.01f)] public float pressSeconds = 0.12f;
    private JourneyBoardTint upTint, downTint;
    private Vector3 upHome, downHome;
    private Tween upTween, downTween;
    private bool initialized;
    public bool CanInteract => isActiveAndEnabled && player != null && menu != null && menu.IsGameplayActive && Time.timeScale > 0f &&
        !JourneyRideSession.IsOccupied && !JourneyRadialMenu.IsOpen && !JourneyPlayerCombat.IsMovementBlocked &&
        (stoneController == null || !stoneController.isActiveAndEnabled || !stoneController.IsUsingStone);
    public bool InBoardRange => CanInteract &&
        (Near(transform.position) || crystalPosition != null && Near(crystalPosition.position));
    private bool Near(Vector3 point) => player != null &&
        (player.position - point).sqrMagnitude < interactionDistance * interactionDistance;

    private void Awake()
    {
        if (voiceBoard == null) voiceBoard = GetComponent<JourneyVoiceBoard>();
        if (menu == null) menu = FindFirstObjectByType<JourneyMenuController>(FindObjectsInactive.Include);
        if (stoneController == null) stoneController = FindFirstObjectByType<JourneyStoneThrowController>(FindObjectsInactive.Include);
        if (radialMenu == null) radialMenu = FindFirstObjectByType<JourneyRadialMenu>(FindObjectsInactive.Include);
        if (player == null)
            foreach (var candidate in FindObjectsByType<GameCreator.Runtime.Characters.Character>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (candidate.IsPlayer && candidate.gameObject.scene == gameObject.scene) { player = candidate.transform; break; }
        if (crystalColliders == null || crystalColliders.Length == 0)
            crystalColliders = crystal != null ? crystal.GetComponentsInChildren<Collider>(true) : new Collider[0];
        upTint = new JourneyBoardTint(upRenderers); downTint = new JourneyBoardTint(downRenderers);
        if (upCollider != null) upHome = upCollider.transform.localPosition;
        if (downCollider != null) downHome = downCollider.transform.localPosition;
        if (socketVisual != null)
            foreach (var renderer in socketVisual.GetComponentsInChildren<Renderer>(true))
            {
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                properties.SetColor("_BaseColor", socketColor); properties.SetColor("_Color", socketColor);
                properties.SetColor("_EmissionColor", Color.black); renderer.SetPropertyBlock(properties);
            }
        initialized = true;
        RefreshState();
    }
    private void OnEnable()
    {
        JourneyLocalization.Changed += RefreshInventory;
        RefreshInventory();
    }
    private void Update()
    {
        bool canChange = InBoardRange && voiceBoard != null && voiceBoard.CanChangeVerse;
        upTint?.Set(canChange && voiceBoard.HasNextVerse ? (Color?)null : disabledColor);
        downTint?.Set(canChange && voiceBoard.HasPreviousVerse ? (Color?)null : disabledColor);
    }

    // Input is routed by JourneyVoiceBoard so the same touch also blocks camera drag.
    public bool TryPress(Collider hit)
    {
        if (hit == null) return false;
        if (hit == socketCollider)
        {
            if (State == CrystalState.Carried && InBoardRange) InstallCrystal();
            else if (!IsActivated && voiceBoard != null) voiceBoard.RefreshAvailability();
            return true;
        }
        foreach (var collider in crystalColliders)
            if (collider != null && hit == collider)
            {
                if (State == CrystalState.InWorld && CanInteract && crystal != null && Near(crystal.position)) CollectCrystal();
                return true;
            }
        if (hit == upCollider || hit == downCollider)
        {
            if (InBoardRange && voiceBoard != null && voiceBoard.ChangeVerse(hit == upCollider ? 1 : -1))
                AnimateArrow(hit == upCollider);
            return true;
        }
        return false;
    }
    private void CollectCrystal()
    {
        State = CrystalState.Carried;
        crystal.gameObject.SetActive(false);
        PlaySound(pickupSound);
        RefreshState();
    }
    private void InstallCrystal()
    {
        if (State != CrystalState.Carried || crystal == null || crystalPosition == null) return;
        State = CrystalState.Installed; // Set first; repeated presses cannot play the sound again.
        foreach (var body in crystal.GetComponentsInChildren<Rigidbody>(true))
        { body.isKinematic = true; body.useGravity = false; }
        crystal.SetParent(crystalPosition, false);
        crystal.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(placedLocalEulerAngles));
        crystal.localScale = placedLocalScale;
        foreach (var collider in crystalColliders) if (collider != null) collider.enabled = false;
        if (socketCollider != null) socketCollider.enabled = false;
        crystal.gameObject.SetActive(true);
        Physics.SyncTransforms();
        RefreshState();
        PlaySound(installationSound);
    }
    private void RefreshState()
    {
        if (socketVisual != null) socketVisual.SetActive(!IsActivated);
        RefreshInventory();
        if (voiceBoard != null) voiceBoard.RefreshAvailability();
    }
    private void RefreshInventory()
    {
        if (radialMenu == null) return;
        radialMenu.SetRuntimeItem(this, JourneyLocalization.Choose(persianCrystalName, englishCrystalName, arabicCrystalName),
            crystalIcon, isActiveAndEnabled && State == CrystalState.Carried, ShowCrystalHint);
    }
    private void ShowCrystalHint() { if (voiceBoard != null) voiceBoard.RefreshAvailability(); }
    private void PlaySound(AudioClip clip)
    {
        if (effectsSource != null && clip != null) effectsSource.PlayOneShot(clip, effectsVolume);
    }
    private void AnimateArrow(bool up)
    {
        var button = up ? upCollider : downCollider;
        if (button == null) return;
        Vector3 home = up ? upHome : downHome;
        if (up) upTween?.Kill(); else downTween?.Kill();
        button.transform.localPosition = home;
        Tween tween = button.transform.DOLocalMove(home + pressedOffset, pressSeconds).SetLoops(2, LoopType.Yoyo).SetEase(Ease.OutQuad);
        if (up) upTween = tween; else downTween = tween;
    }
    private void OnDisable()
    {
        JourneyLocalization.Changed -= RefreshInventory;
        if (radialMenu != null) radialMenu.SetRuntimeItem(this, "", null, false, null);
        upTween?.Kill(); downTween?.Kill(); upTint?.Restore(); downTint?.Restore();
        if (!initialized) return;
        if (upCollider != null) upCollider.transform.localPosition = upHome;
        if (downCollider != null) downCollider.transform.localPosition = downHome;
    }
}
