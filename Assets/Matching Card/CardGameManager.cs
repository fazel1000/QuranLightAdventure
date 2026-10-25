using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Assets._Scripts.FFF.InGame.TapAwayPuzzle.Managers;

public class CardGameManager : MonoBehaviour
{
    [Header("Audio")]
    public AudioClip MatchAudioClip;
    public AudioClip NotMatchAudioClip;
    public AudioSource AudioMusic;

    [Header("Cards Settings")]
    [SerializeField]
    private float flipDuration = 0.3f;

    [SerializeField]
    private float waitBeforeFlipBack = 1f;

    [SerializeField]
    private string[] validTags =
    {
        "1", "2", "3", "4",
        "5", "6", "7", "8",
        "9", "10", "11", "12",
        "13", "14", "15", "16"
    };

    [Header("End Game Cube")]
    public GameObject rewardCube;

    [SerializeField]
    private float rewardRotationDuration = 1f;

    [Header("Timer UI")]
    public TMP_Text timerText;

    private float timer;
    private bool timerRunning;

    [Header("Leaderboard Reference")]
    public LeaderboardManager leaderboardManager;

    private GameObject firstCard;
    private GameObject secondCard;

    private bool canClick;
    private bool gameOver;

    private readonly List<GameObject> allCards =
        new List<GameObject>();

    private readonly List<Vector3> originalPositions =
        new List<Vector3>();

    private void Awake()
    {
        ResetTimer();
    }

    private void OnEnable()
    {
        PickupAndPlaceWithEffect.OnOnionPlaced +=
            OnOnionPlacedHandler;

        Manager_TapAway.OnFirstCellRemoved +=
            OnFirstTapAwayCellRemoved;

        Manager_TapAway.OnPuzzleReset +=
            OnTapAwayPuzzleReset;
    }

    private void Start()
    {
        FindAllCards();
        ShuffleCardPositions();

        canClick =
            PickupAndPlaceWithEffect.OnionPlaced;
    }

    private void OnDisable()
    {
        PickupAndPlaceWithEffect.OnOnionPlaced -=
            OnOnionPlacedHandler;

        Manager_TapAway.OnFirstCellRemoved -=
            OnFirstTapAwayCellRemoved;

        Manager_TapAway.OnPuzzleReset -=
            OnTapAwayPuzzleReset;
    }

    private void Update()
    {
        /*
         * تایمر باید حتی زمانی که هنوز پیاز
         * در OnionSlot قرار نگرفته ادامه پیدا کند.
         */
        if (timerRunning && !gameOver)
        {
            timer += Time.deltaTime;
            UpdateTimerUI();
        }

        if (!canClick || gameOver)
        {
            return;
        }

        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        Camera mainCamera = Camera.main;

        if (mainCamera == null)
        {
            return;
        }

        Ray ray = mainCamera.ScreenPointToRay(
            Input.mousePosition
        );

        if (!Physics.Raycast(
                ray,
                out RaycastHit hit))
        {
            return;
        }

        GameObject clickedCard =
            hit.collider.gameObject;

        if (!IsValidTag(clickedCard.tag))
        {
            return;
        }

        if (
            clickedCard == firstCard ||
            clickedCard == secondCard)
        {
            return;
        }

        CardBehaviour card =
            clickedCard.GetComponent<CardBehaviour>();

        if (card == null || card.isFlipped)
        {
            return;
        }

        /*
         * شروع تایمر از کلیک کارت حذف شده است.
         * تایمر فقط از رویداد اولین حذف سلول
         * TapAway شروع می‌شود.
         */

        StartCoroutine(
            card.Flip(true)
        );

        if (firstCard == null)
        {
            firstCard = clickedCard;
        }
        else if (secondCard == null)
        {
            secondCard = clickedCard;
            canClick = false;

            StartCoroutine(
                CheckMatch()
            );
        }
    }

    private void FindAllCards()
    {
        allCards.Clear();
        originalPositions.Clear();

        GameObject[] sceneObjects =
            GameObject.FindObjectsOfType<GameObject>();

        foreach (GameObject cardObject in sceneObjects)
        {
            if (
                IsValidTag(cardObject.tag) &&
                cardObject.GetComponent<CardBehaviour>() != null)
            {
                allCards.Add(cardObject);

                originalPositions.Add(
                    cardObject.transform.position
                );
            }
        }
    }

    private void OnFirstTapAwayCellRemoved()
    {
        if (gameOver || timerRunning)
        {
            return;
        }

        timer = 0f;
        timerRunning = true;

        UpdateTimerUI();

        if (timerText != null)
        {
            timerText.gameObject.SetActive(true);
        }

        Debug.Log(
            "Game timer started after the first TapAway cell removal."
        );
    }

    private void OnTapAwayPuzzleReset()
    {
        if (gameOver)
        {
            return;
        }

        ResetTimer();

        Debug.Log(
            "Game timer reset because the TapAway puzzle was reset."
        );
    }

    private void ResetTimer()
    {
        timer = 0f;
        timerRunning = false;

        if (timerText == null)
        {
            return;
        }

        timerText.text = "00:00";
        timerText.gameObject.SetActive(false);
    }

    private void OnOnionPlacedHandler()
    {
        if (!gameOver)
        {
            canClick = true;
        }
    }

    private IEnumerator CheckMatch()
    {
        yield return new WaitForSeconds(
            flipDuration
        );

        CardBehaviour card1 =
            firstCard.GetComponent<CardBehaviour>();

        CardBehaviour card2 =
            secondCard.GetComponent<CardBehaviour>();

        if (firstCard.tag == secondCard.tag)
        {
            if (
                AudioMusic != null &&
                MatchAudioClip != null)
            {
                AudioMusic.PlayOneShot(
                    MatchAudioClip
                );
            }

            card1.Lock();
            card2.Lock();
        }
        else
        {
            yield return new WaitForSeconds(
                waitBeforeFlipBack
            );

            if (
                AudioMusic != null &&
                NotMatchAudioClip != null)
            {
                AudioMusic.PlayOneShot(
                    NotMatchAudioClip
                );
            }

            StartCoroutine(
                card1.Flip(false)
            );

            StartCoroutine(
                card2.Flip(false)
            );
        }

        firstCard = null;
        secondCard = null;
        canClick = true;

        if (!CheckAllMatched())
        {
            yield break;
        }

        timerRunning = false;
        gameOver = true;
        canClick = false;

        yield return new WaitForSeconds(1f);

        StartCoroutine(
            SpinRewardCube()
        );

        if (timerText != null)
        {
            timerText.gameObject.SetActive(false);
        }

        SubmitRecordIfPlayerInfoIsValid();
    }

    private void SubmitRecordIfPlayerInfoIsValid()
    {
        string enteredName =
            MainMenuController.playerName?.Trim();

        string enteredCountry =
            MainMenuController.playerCountry?.Trim();

        bool nameIsValid =
            !string.IsNullOrWhiteSpace(enteredName) &&
            !string.Equals(
                enteredName,
                "Player",
                StringComparison.OrdinalIgnoreCase
            );

        bool countryIsValid =
            !string.IsNullOrWhiteSpace(enteredCountry) &&
            !string.Equals(
                enteredCountry,
                "Unknown",
                StringComparison.OrdinalIgnoreCase
            );

        if (!nameIsValid || !countryIsValid)
        {
            Debug.Log(
                "Record was not submitted: " +
                "player name and country are required."
            );

            return;
        }

        if (leaderboardManager == null)
        {
            Debug.LogWarning(
                "Record was not submitted: " +
                "LeaderboardManager is not assigned."
            );

            return;
        }

        leaderboardManager.SubmitScore(
            enteredName,
            enteredCountry,
            timer
        );

        Debug.Log(
            "Record submitted for: " +
            enteredName + " - " +
            enteredCountry +
            " | Time: " +
            timer
        );
    }

    private bool CheckAllMatched()
    {
        foreach (GameObject cardObject in allCards)
        {
            CardBehaviour card =
                cardObject.GetComponent<CardBehaviour>();

            if (
                card == null ||
                !card.isFlipped)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsValidTag(
        string objectTag)
    {
        foreach (string validTag in validTags)
        {
            if (objectTag == validTag)
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerator SpinRewardCube()
    {
        if (rewardCube == null)
        {
            yield break;
        }

        Quaternion startRotation =
            rewardCube.transform.rotation;

        Quaternion endRotation =
            startRotation *
            Quaternion.Euler(
                0f,
                0f,
                180f
            );

        float elapsedTime = 0f;

        while (
            elapsedTime <
            rewardRotationDuration)
        {
            float progress =
                elapsedTime /
                rewardRotationDuration;

            rewardCube.transform.rotation =
                Quaternion.Slerp(
                    startRotation,
                    endRotation,
                    progress
                );

            elapsedTime += Time.deltaTime;

            yield return null;
        }

        rewardCube.transform.rotation =
            endRotation;
    }

    private void ShuffleCardPositions()
    {
        List<Vector3> availablePositions =
            new List<Vector3>(
                originalPositions
            );

        foreach (GameObject cardObject in allCards)
        {
            if (availablePositions.Count == 0)
            {
                break;
            }

            int randomIndex =
                UnityEngine.Random.Range(
                    0,
                    availablePositions.Count
                );

            cardObject.transform.position =
                availablePositions[randomIndex];

            availablePositions.RemoveAt(
                randomIndex
            );
        }
    }

    private void UpdateTimerUI()
    {
        if (timerText == null)
        {
            return;
        }

        int minutes =
            Mathf.FloorToInt(
                timer / 60f
            );

        int seconds =
            Mathf.FloorToInt(
                timer % 60f
            );

        timerText.text =
            $"{minutes:00}:{seconds:00}";
    }
}