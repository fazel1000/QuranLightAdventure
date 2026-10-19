using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent, DefaultExecutionOrder(900)]
public sealed class JourneySettingsPages : MonoBehaviour
{
    public RectTransform firstPage, secondPage;
    public Button rightArrow, leftArrow, secondBackButton;
    public JourneyMenuController menu;
    [Min(0.05f)] public float slideSeconds = 0.35f;
    public int CurrentPage { get; private set; }
    public bool IsOpen { get; private set; }
    private Vector2 firstPosition, secondPosition;
    private bool captured;
    private Coroutine sliding;
    private CanvasGroup firstGroup, secondGroup;
    private bool firstInteractable, firstBlocks, secondInteractable, secondBlocks;
    private void Awake() { Capture(); if (secondPage != null) secondPage.gameObject.SetActive(false); }
    private void OnEnable()
    {
        if (rightArrow != null) rightArrow.onClick.AddListener(Next);
        if (leftArrow != null) leftArrow.onClick.AddListener(Previous);
        if (secondBackButton != null) secondBackButton.onClick.AddListener(Back);
    }
    private void OnDisable()
    {
        if (rightArrow != null) rightArrow.onClick.RemoveListener(Next);
        if (leftArrow != null) leftArrow.onClick.RemoveListener(Previous);
        if (secondBackButton != null) secondBackButton.onClick.RemoveListener(Back);
        HideAll();
    }
    private void Capture()
    {
        if (captured || firstPage == null || secondPage == null) return;
        firstPosition = firstPage.anchoredPosition; secondPosition = secondPage.anchoredPosition;
        firstGroup = firstPage.GetComponent<CanvasGroup>() ?? firstPage.gameObject.AddComponent<CanvasGroup>();
        secondGroup = secondPage.GetComponent<CanvasGroup>() ?? secondPage.gameObject.AddComponent<CanvasGroup>();
        captured = true;
    }
    public void Next() { BeginSlide(1); }
    public void Previous() { BeginSlide(0); }
    private void Back() { if (menu != null) menu.ShowMainMenu(); }
    private void BeginSlide(int page)
    {
        if (!IsOpen || !captured || sliding != null || page == CurrentPage) return;
        sliding = StartCoroutine(Slide(page));
    }
    private IEnumerator Slide(int page)
    {
        firstInteractable = firstGroup.interactable; firstBlocks = firstGroup.blocksRaycasts;
        secondInteractable = secondGroup.interactable; secondBlocks = secondGroup.blocksRaycasts;
        firstGroup.interactable = secondGroup.interactable = false;
        firstGroup.blocksRaycasts = secondGroup.blocksRaycasts = false;
        firstPage.gameObject.SetActive(true); secondPage.gameObject.SetActive(true);
        firstPage.SetAsLastSibling(); secondPage.SetAsLastSibling();
        float width = Mathf.Max(1f, firstPage.rect.width);
        float elapsed = 0f;
        float duration = Mathf.Max(0.05f, slideSeconds);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            float progress = page == 1 ? t : 1f - t;
            firstPage.anchoredPosition = firstPosition + Vector2.left * width * progress;
            secondPage.anchoredPosition = secondPosition + Vector2.right * width * (1f - progress);
            yield return null;
        }
        RestoreInteraction(); sliding = null;
        ShowPageImmediate(page);
    }
    private void RestoreInteraction()
    {
        if (firstGroup == null || secondGroup == null) return;
        firstGroup.interactable = firstInteractable; firstGroup.blocksRaycasts = firstBlocks;
        secondGroup.interactable = secondInteractable; secondGroup.blocksRaycasts = secondBlocks;
    }
    private void CancelSlide()
    {
        if (sliding == null) return;
        StopCoroutine(sliding); sliding = null; RestoreInteraction();
    }
    public void ShowPageImmediate(int page)
    {
        Capture(); if (!captured) return;
        CancelSlide(); CurrentPage = Mathf.Clamp(page, 0, 1); IsOpen = true;
        firstPage.anchoredPosition = firstPosition; secondPage.anchoredPosition = secondPosition;
        firstPage.gameObject.SetActive(CurrentPage == 0); secondPage.gameObject.SetActive(CurrentPage == 1);
        (CurrentPage == 0 ? firstPage : secondPage).SetAsLastSibling();
    }
    public void HideAll()
    {
        CancelSlide(); IsOpen = false;
        if (captured) { firstPage.anchoredPosition = firstPosition; secondPage.anchoredPosition = secondPosition; }
        if (firstPage != null) firstPage.gameObject.SetActive(false);
        if (secondPage != null) secondPage.gameObject.SetActive(false);
    }
}
