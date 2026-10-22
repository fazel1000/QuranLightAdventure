using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(1800), DisallowMultipleComponent]
public sealed class JourneyMinimap : MonoBehaviour
{
    public JourneyMenuController menu;
    public Transform followTarget;
    public GameObject rootUI;
    public RawImage mapImage;
    public RectTransform iconRoot;
    public Texture2D offlineMap;
    public Sprite defaultIcon;
    [Tooltip("World X/Z lower-left corner and width/depth of the baked image.")]
    public Rect worldRect = new Rect(-1000f, -1000f, 2000f, 2000f);
    [Min(10f)] public float viewDiameter = 160f;
    [Min(0.02f)] public float refreshSeconds = 0.05f;
    [Header("Offline capture - editor only")]
    public LayerMask captureLayers = ~0;
    [Range(256, 4096)] public int captureResolution = 1024;
    private readonly Dictionary<JourneyMinimapMarker, Image> icons = new Dictionary<JourneyMinimapMarker, Image>();
    private readonly List<JourneyMinimapMarker> expired = new List<JourneyMinimapMarker>();
    private float nextRefresh;
    private void LateUpdate()
    {
        bool visible = offlineMap != null && followTarget != null && mapImage != null && iconRoot != null && (menu == null || menu.IsGameplayActive);
        if (rootUI != null && rootUI.activeSelf != visible) rootUI.SetActive(visible);
        if (!visible || Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + Mathf.Max(0.02f, refreshSeconds);
        float diameter = Mathf.Max(10f, viewDiameter);
        Vector2 center = new Vector2(followTarget.position.x, followTarget.position.z);
        mapImage.texture = offlineMap;
        mapImage.uvRect = new Rect((center.x - diameter * 0.5f - worldRect.xMin) / Mathf.Max(1f, worldRect.width),
            (center.y - diameter * 0.5f - worldRect.yMin) / Mathf.Max(1f, worldRect.height), diameter / Mathf.Max(1f, worldRect.width), diameter / Mathf.Max(1f, worldRect.height));
        expired.Clear();
        foreach (var pair in icons) if (pair.Key == null || !JourneyMinimapMarker.Active.Contains(pair.Key)) expired.Add(pair.Key);
        foreach (var marker in expired) { if (icons[marker] != null) Destroy(icons[marker].gameObject); icons.Remove(marker); }
        foreach (var marker in JourneyMinimapMarker.Active)
        {
            if (marker == null || marker.Target == null || marker.gameObject.scene != gameObject.scene) continue;
            if (!icons.TryGetValue(marker, out var graphic))
            {
                var go = new GameObject(marker.name + " Icon", typeof(RectTransform), typeof(Image));
                graphic = go.GetComponent<Image>(); graphic.rectTransform.SetParent(iconRoot, false);
                graphic.rectTransform.anchorMin = graphic.rectTransform.anchorMax = graphic.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                graphic.raycastTarget = false; graphic.preserveAspect = true; icons[marker] = graphic;
            }
            Vector3 world = marker.Target.position;
            Vector2 position = new Vector2((world.x - center.x) / diameter * iconRoot.rect.width, (world.z - center.y) / diameter * iconRoot.rect.height);
            float radius = Mathf.Max(1f, Mathf.Min(iconRoot.rect.width, iconRoot.rect.height) * 0.5f - marker.size * 0.6f);
            bool outside = position.sqrMagnitude > radius * radius;
            graphic.gameObject.SetActive(!outside || marker.keepOnEdge);
            graphic.rectTransform.anchoredPosition = marker.keepOnEdge ? Vector2.ClampMagnitude(position, radius) : position;
            graphic.rectTransform.sizeDelta = Vector2.one * marker.size;
            graphic.sprite = marker.icon != null ? marker.icon : defaultIcon; graphic.color = marker.tint;
            Transform heading = marker.headingTarget != null ? marker.headingTarget : marker.Target;
            graphic.rectTransform.localRotation = Quaternion.Euler(0f, 0f, marker.rotateIcon ? -(heading.eulerAngles.y + marker.headingOffset) : 0f);
        }
    }
    private void OnDisable() { if (rootUI != null) rootUI.SetActive(false); }
    private void OnDestroy() { foreach (var graphic in icons.Values) if (graphic != null) Destroy(graphic.gameObject); }
}
