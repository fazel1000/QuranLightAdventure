using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
public class CardGameManager : MonoBehaviour
{
    [Header("Puzzle root and its 32 cards")]
    public Transform cardsRoot;
    public List<CardBehaviour> cards = new List<CardBehaviour>();
    [Min(2)] public int requiredCardCount = 32;
    public bool shuffleOnStart = true;
    [SerializeField, Min(0f)] private float waitBeforeFlipBack = 1f;

    [Header("Completion")]
    [Min(0)] public int completionPoints = 500;
    public LightBrushPuzzle scoreReceiver;
    public AudioClip SuccessAudioClip;

    [Header("Audio - dedicated puzzle effects source")]
    public AudioClip MatchAudioClip;
    public AudioClip NotMatchAudioClip;
    public AudioSource AudioMusic;

    [Header("Touch and mouse")]
    public Camera interactionCamera;
    public GameCreator.Runtime.Characters.Character player;
    public JourneyMenuController menu;
    [Min(0.1f)] public float maxTouchDistance = 15f;
    [Min(1f)] public float tapSlopPixels = 24f;
    public bool IsComplete { get; private set; }
    public int MatchedCards { get; private set; }

    private const int NoPointer = int.MinValue, MousePointer = -1;
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private readonly RaycastHit[] rayHits = new RaycastHit[64];
    private CardBehaviour firstCard, secondCard, pressedCard;
    private int activePointer = NoPointer;
    private Vector2 pressPosition;
    private bool cancelledTap, initialized, checking, focused = true, appPaused;
    private bool GameplayAllowed => focused && !appPaused && Time.timeScale > 0f &&
        (menu == null || menu.IsGameplayActive) && !JourneyRideSession.IsOccupied &&
        !JourneyRadialMenu.IsOpen && !LightBrushPuzzle.IsMovementBlocked;

    private void Awake()
    {
        if (cardsRoot == null) cardsRoot = transform;
        if (AudioMusic == null) AudioMusic = GetComponent<AudioSource>();
        if (menu == null) menu = FindFirstObjectByType<JourneyMenuController>(FindObjectsInactive.Include);
        if (scoreReceiver == null) scoreReceiver = FindFirstObjectByType<LightBrushPuzzle>(FindObjectsInactive.Include);
        if (player == null)
            foreach (var character in FindObjectsByType<GameCreator.Runtime.Characters.Character>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (character.IsPlayer && character.gameObject.scene == gameObject.scene) { player = character; break; }
    }
    private void Start()
    {
        if (cards.Count == 0) cards.AddRange(cardsRoot.GetComponentsInChildren<CardBehaviour>(true));
        if (!ValidateConfiguration(out string error))
        { Debug.LogError("Matching Card: " + error, this); return; }
        foreach (var card in cards) card.ResetCard();
        if (shuffleOnStart) ShuffleCardPositions();
        initialized = true;
    }
    public bool ValidateConfiguration(out string error)
    {
        error = null;
        if (requiredCardCount < 2 || requiredCardCount % 2 != 0 || cards.Count != requiredCardCount)
        { error = "Assign exactly " + requiredCardCount + " cards. Cube.032 is not a card. Run Quran Kids > Setup Matching Card Puzzle."; return false; }
        var unique = new HashSet<CardBehaviour>(); var pairs = new Dictionary<int, int>();
        Transform root = cardsRoot != null ? cardsRoot : transform;
        foreach (var card in cards)
        {
            if (card == null || !unique.Add(card) || !card.transform.IsChildOf(root) || card.pairId < 1)
            { error = "Cards must be distinct children of this puzzle and each needs a positive Pair Id."; return false; }
            pairs.TryGetValue(card.pairId, out int count); pairs[card.pairId] = count + 1;
        }
        foreach (var pair in pairs)
            if (pair.Value != 2) { error = "Pair Id " + pair.Key + " must appear on exactly two cards, but appears on " + pair.Value + "."; return false; }
        return true;
    }
    private void ShuffleCardPositions()
    {
        var positions = new Vector3[cards.Count];
        for (int i = 0; i < cards.Count; i++) positions[i] = cardsRoot.InverseTransformPoint(cards[i].transform.position);
        for (int i = positions.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1); Vector3 saved = positions[i]; positions[i] = positions[j]; positions[j] = saved;
        }
        for (int i = 0; i < cards.Count; i++) cards[i].transform.position = cardsRoot.TransformPoint(positions[i]);
        Physics.SyncTransforms();
    }
    private void Update()
    {
        if (!initialized || IsComplete || checking || !GameplayAllowed) { CancelTap(); return; }
        bool touchUsed = false, observedPointer = false;
        if (Touchscreen.current != null)
            foreach (var touch in Touchscreen.current.touches)
            {
                bool held = touch.press.isPressed, released = touch.press.wasReleasedThisFrame;
                if (!held && !released) continue;
                touchUsed = true;
                int id = touch.touchId.ReadValue(); Vector2 position = touch.position.ReadValue();
                if (touch.press.wasPressedThisFrame) BeginTap(id, position);
                if (activePointer != id) continue;
                observedPointer = true; ContinueTap(position);
                if (released) EndTap(id, position);
            }
        if (activePointer != NoPointer && activePointer != MousePointer && !observedPointer) CancelTap();
        var mouse = Mouse.current;
        if (touchUsed || mouse == null) { if (activePointer == MousePointer) CancelTap(); return; }
        Vector2 mousePosition = mouse.position.ReadValue();
        if (mouse.leftButton.wasPressedThisFrame) BeginTap(MousePointer, mousePosition);
        if (activePointer != MousePointer) return;
        ContinueTap(mousePosition);
        if (mouse.leftButton.wasReleasedThisFrame) EndTap(MousePointer, mousePosition);
        else if (!mouse.leftButton.isPressed) CancelTap();
    }
    private void BeginTap(int pointer, Vector2 position)
    {
        if (activePointer != NoPointer || IsOverUI(position)) return;
        CardBehaviour card = HitCard(position);
        if (!CanSelectCard(card)) return;
        activePointer = pointer; pressedCard = card; pressPosition = position; cancelledTap = false;
    }
    private void ContinueTap(Vector2 position)
    { if ((position - pressPosition).sqrMagnitude > tapSlopPixels * tapSlopPixels) cancelledTap = true; }
    private void EndTap(int pointer, Vector2 position)
    {
        if (pointer != activePointer) return;
        CardBehaviour card = pressedCard;
        bool valid = !cancelledTap && !IsOverUI(position) && HitCard(position) == card;
        CancelTap();
        if (valid && GameplayAllowed && CanSelectCard(card)) SelectCard(card);
    }
    private void CancelTap() { activePointer = NoPointer; pressedCard = null; cancelledTap = false; }
    private CardBehaviour HitCard(Vector2 position)
    {
        Camera view = interactionCamera != null ? interactionCamera : Camera.main;
        if (view == null) return null;
        Ray ray = view.ScreenPointToRay(position); RaycastHit[] results = rayHits;
        int count = Physics.RaycastNonAlloc(ray, results, maxTouchDistance, ~0, QueryTriggerInteraction.Ignore);
        if (count == results.Length)
        { results = Physics.RaycastAll(ray, maxTouchDistance, ~0, QueryTriggerInteraction.Ignore); count = results.Length; }
        RaycastHit nearest = default; float distance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = results[i];
            if (hit.collider == null || hit.distance >= distance || (player != null && hit.collider.transform.IsChildOf(player.transform))) continue;
            nearest = hit; distance = hit.distance;
        }
        if (nearest.collider == null) return null;
        var card = nearest.collider.GetComponentInParent<CardBehaviour>();
        return card != null && cards.Contains(card) ? card : null;
    }
    private bool IsOverUI(Vector2 position)
    {
        if (EventSystem.current == null) return false;
        uiHits.Clear(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
        foreach (var hit in uiHits)
            if (hit.module is GraphicRaycaster && (hit.gameObject.GetComponentInParent<Selectable>() != null ||
                hit.gameObject.GetComponentInParent<GameCreator.Runtime.Common.TTouchStick>() != null)) return true;
        return false;
    }
    private bool CanSelectCard(CardBehaviour card) => initialized && !checking && !IsComplete && card != null && card.isActiveAndEnabled && !card.isFlipped &&
        !card.IsLocked && !card.IsAnimating && card != firstCard && card != secondCard;
    private void SelectCard(CardBehaviour card)
    {
        if (firstCard == null) { firstCard = card; StartCoroutine(card.Flip(true)); return; }
        secondCard = card; checking = true; StartCoroutine(CheckMatch(firstCard, secondCard));
    }
    private IEnumerator CheckMatch(CardBehaviour a, CardBehaviour b)
    {
        yield return b.Flip(true);
        while (a != null && b != null && (a.IsAnimating || b.IsAnimating)) yield return null;
        while (a != null && b != null && !GameplayAllowed) yield return null;
        if (a == null || b == null || !a.isActiveAndEnabled || !b.isActiveAndEnabled)
        { ClearUnmatchedSelection(); yield break; }
        if (a.pairId == b.pairId)
        {
            a.Lock(); b.Lock(); MatchedCards += 2;
            if (MatchedCards == requiredCardCount) CompletePuzzle();
            else PlaySound(MatchAudioClip);
        }
        else
        {
            PlaySound(NotMatchAudioClip);
            yield return new WaitForSeconds(Mathf.Max(0f, waitBeforeFlipBack));
            if (a != null) StartCoroutine(a.Flip(false));
            if (b != null) yield return b.Flip(false);
            while (a != null && a.IsAnimating) yield return null;
        }
        firstCard = null; secondCard = null; checking = false;
    }
    private void CompletePuzzle()
    {
        if (IsComplete || cards.Count != requiredCardCount) return;
        foreach (var card in cards) if (card == null || !card.IsLocked) return;
        IsComplete = true;
        int points = Mathf.Max(0, completionPoints);
        if (scoreReceiver != null) scoreReceiver.AddCoins(points);
        else JourneyCoinWallet.Add(points);
        PlaySound(SuccessAudioClip != null ? SuccessAudioClip : MatchAudioClip);
    }
    private void PlaySound(AudioClip clip)
    { if (AudioMusic != null && clip != null) AudioMusic.PlayOneShot(clip); }
    private void ClearUnmatchedSelection()
    {
        if (firstCard != null && !firstCard.IsLocked) firstCard.CancelFlip();
        if (secondCard != null && !secondCard.IsLocked) secondCard.CancelFlip();
        firstCard = null; secondCard = null; checking = false;
    }
    private void OnDisable()
    {
        StopAllCoroutines(); CancelTap(); ClearUnmatchedSelection();
    }
    private void OnApplicationFocus(bool value) { focused = value; if (!value) CancelTap(); }
    private void OnApplicationPause(bool value) { appPaused = value; if (value) CancelTap(); }
}
