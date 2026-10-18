using UnityEngine;
using UnityEngine.UI;

// Composite the preview into the Canvas, above its background image.
[DisallowMultipleComponent, RequireComponent(typeof(JourneyWardrobeRoom))]
public sealed class JourneyWardrobePreviewSurface : MonoBehaviour
{
    public RawImage previewImage;
    [Range(256, 4096)] public int maximumTextureSize = 2048;
    private JourneyWardrobeRoom room;
    private RenderTexture texture, previousTexture;
    private Rect previousRect;
    private CameraClearFlags previousFlags;
    private Color previousColor;
    private float previousAspect;
    private bool rendering;
    public bool IsRendering => rendering;
    private readonly Vector3[] corners = new Vector3[4];
    private void Awake() { room = GetComponent<JourneyWardrobeRoom>(); }
    public void Begin()
    {
        if (rendering) return;
        if (room == null) room = GetComponent<JourneyWardrobeRoom>();
        if (room == null || room.previewCamera == null || previewImage == null) return;
        Camera camera = room.previewCamera;
        previousTexture = camera.targetTexture; previousRect = camera.rect;
        previousFlags = camera.clearFlags; previousColor = camera.backgroundColor;
        previousAspect = camera.aspect;
        rendering = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.rect = new Rect(0f, 0f, 1f, 1f);
        previewImage.color = Color.white;
        Resize();
        var drag = room.wardrobePanel != null ? room.wardrobePanel.GetComponent<JourneyWardrobeDrag>() : null;
        if (drag != null) drag.previewArea = previewImage.rectTransform;
    }
    private void LateUpdate()
    {
        if (!rendering || previewImage == null || room == null || room.previewCamera == null) return;
        Vector2Int size = Size();
        if (texture == null || size.x != texture.width || size.y != texture.height)
        { Resize(); room.FitPreviewCamera(); }
    }
    private Vector2Int Size()
    {
        previewImage.rectTransform.GetWorldCorners(corners);
        Canvas canvas = previewImage.canvas;
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[2]);
        float width = Mathf.Max(1f, Mathf.Abs(b.x - a.x)), height = Mathf.Max(1f, Mathf.Abs(b.y - a.y));
        float scale = Mathf.Min(1f, Mathf.Max(256, maximumTextureSize) / Mathf.Max(width, height));
        return new Vector2Int(Mathf.Max(16, Mathf.RoundToInt(width * scale)), Mathf.Max(16, Mathf.RoundToInt(height * scale)));
    }
    private void Resize()
    {
        Vector2Int size = Size();
        ReleaseTexture();
        texture = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32);
        texture.name = "Journey Wardrobe Preview";
        texture.antiAliasing = 1;
        texture.Create();
        room.previewCamera.targetTexture = texture;
        room.previewCamera.aspect = (float)size.x / size.y;
        previewImage.texture = texture;
    }
    public void End()
    {
        if (!rendering) return;
        rendering = false;
        if (room != null && room.previewCamera != null)
        {
            var camera = room.previewCamera;
            camera.targetTexture = previousTexture; camera.rect = previousRect;
            camera.clearFlags = previousFlags; camera.backgroundColor = previousColor;
            camera.aspect = previousAspect;
        }
        if (previewImage != null) previewImage.texture = null;
        ReleaseTexture();
    }
    private void ReleaseTexture()
    {
        if (texture == null) return;
        if (room != null && room.previewCamera != null && room.previewCamera.targetTexture == texture)
            room.previewCamera.targetTexture = null;
        texture.Release();
        if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture);
        texture = null;
    }
    private void OnDisable() { End(); }
    private void OnDestroy() { End(); }
}
