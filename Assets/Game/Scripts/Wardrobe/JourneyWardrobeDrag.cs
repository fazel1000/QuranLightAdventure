using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Orbit the preview camera around the character; both horizontal and vertical drag work.
public sealed class JourneyWardrobeDrag : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    public Transform target;
    public Camera previewCamera;
    public RectTransform previewArea;
    [Min(1f)] public float degreesPerScreenWidth = 360f;
    [Min(1f)] public float degreesPerScreenHeight = 180f;
    [Tooltip("Off allows full vertical turns as well as horizontal turns. On keeps the camera upright within the pitch limits.")]
    public bool limitVerticalRotation;
    public bool invertHorizontal;
    public bool invertVertical;
    [Range(-89f, 0f)] public float minimumPitch = -70f;
    [Range(0f, 89f)] public float maximumPitch = 70f;
    private int pointer = int.MinValue;
    private Vector3 pivot;
    private float distance, yaw, pitch;
    private Quaternion orbitRotation;
    private Rect screenRect;

    public void OnPointerDown(PointerEventData e)
    {
        if (pointer != int.MinValue || e.button != PointerEventData.InputButton.Left || target == null || previewCamera == null) return;
        GameObject hit = e.pointerCurrentRaycast.gameObject;
        if (hit != null && hit.GetComponentInParent<Selectable>() != null) return;
        screenRect = previewCamera.pixelRect;
        if (previewArea != null)
        {
            Canvas canvas = previewArea.GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            var corners = new Vector3[4]; previewArea.GetWorldCorners(corners);
            Vector2 a = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(eventCamera, corners[2]);
            screenRect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }
        if (!screenRect.Contains(e.position)) return;
        // Use the visible body's center, rather than the feet at the model's transform origin.
        Bounds bounds = new Bounds(target.position, Vector3.zero);
        bool found = false;
        foreach (var renderer in target.GetComponentsInChildren<Renderer>())
        {
            if (!renderer.enabled) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        pivot = found ? bounds.center : target.position + Vector3.up;
        distance = Mathf.Max(0.1f, Vector3.Distance(previewCamera.transform.position, pivot));
        // The current camera orientation is retained across finger releases, including inverted views.
        orbitRotation = previewCamera.transform.rotation;
        yaw = orbitRotation.eulerAngles.y;
        pitch = Mathf.DeltaAngle(0f, orbitRotation.eulerAngles.x);
        pointer = e.pointerId;
    }
    public void OnDrag(PointerEventData e)
    {
        if (e.pointerId != pointer || target == null || previewCamera == null) return;
        float horizontal = e.delta.x * degreesPerScreenWidth / Mathf.Max(1f, screenRect.width) * (invertHorizontal ? -1f : 1f);
        float vertical = -e.delta.y * degreesPerScreenHeight / Mathf.Max(1f, screenRect.height) * (invertVertical ? -1f : 1f);
        if (limitVerticalRotation)
        {
            yaw = Mathf.Repeat(yaw + horizontal, 360f);
            pitch = Mathf.Clamp(pitch + vertical, minimumPitch, maximumPitch);
            orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        else
        {
            // World-up horizontal orbit and camera-local vertical orbit, with no angle clamp.
            orbitRotation = Quaternion.AngleAxis(horizontal, Vector3.up) * orbitRotation * Quaternion.AngleAxis(vertical, Vector3.right);
        }
        previewCamera.transform.SetPositionAndRotation(pivot + orbitRotation * (Vector3.back * distance), orbitRotation);
    }
    public void OnPointerUp(PointerEventData e) { if (pointer == e.pointerId) pointer = int.MinValue; }
    private void OnDisable() { pointer = int.MinValue; }
    private void OnApplicationFocus(bool focused) { if (!focused) pointer = int.MinValue; }
    private void OnApplicationPause(bool paused) { if (paused) pointer = int.MinValue; }
}
