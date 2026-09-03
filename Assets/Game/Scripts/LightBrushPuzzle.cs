using UnityEngine;
using UnityEngine.InputSystem;

public class LightBrushPuzzle : MonoBehaviour
{
    [SerializeField] private Camera puzzleCamera;
    [SerializeField] private Transform planeAnchor;
    [SerializeField] private Transform[] orderedLetters;
    [SerializeField] private LayerMask letterLayer;
    [SerializeField] private float brushOffset = 0.04f;
    [SerializeField] private float minDistance = 0.03f;

    private LineRenderer line;
    private bool drawing;
    private bool completed;
    private int currentLetter;

    private void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.positionCount = 0;

        if (puzzleCamera == null) puzzleCamera = Camera.main;
    }

    private void Update()
    {
        if (completed || !ReadPointer(
                out Vector2 position,
                out bool pressed,
                out bool held,
                out bool released)) return;

        if (pressed) BeginDrawing(position);
        if (drawing && held) ContinueDrawing(position);
        if (drawing && released) ResetBrush();
    }

    private void BeginDrawing(Vector2 screenPosition)
    {
        if (orderedLetters.Length == 0) return;

        Transform letter = HitLetter(screenPosition);
        if (letter != orderedLetters[0]) return;

        drawing = true;
        currentLetter = 0;
        line.positionCount = 0;
        AddPoint(LetterPoint(letter));
    }

    private void ContinueDrawing(Vector2 screenPosition)
    {
        if (TryGetPlanePoint(screenPosition, out Vector3 point))
        {
            if (line.positionCount == 0 ||
                Vector3.Distance(
                    line.GetPosition(line.positionCount - 1),
                    point) >= minDistance)
            {
                AddPoint(point);
            }
        }

        if (currentLetter + 1 >= orderedLetters.Length) return;

        Transform letter = HitLetter(screenPosition);
        if (letter != orderedLetters[currentLetter + 1]) return;

        currentLetter++;
        AddPoint(LetterPoint(letter));

        if (currentLetter == orderedLetters.Length - 1)
        {
            completed = true;
            drawing = false;
            Debug.Log("محمد کامل شد!");
        }
    }

    private Transform HitLetter(Vector2 screenPosition)
    {
        Ray ray = puzzleCamera.ScreenPointToRay(screenPosition);

        if (!Physics.Raycast(
                ray, out RaycastHit hit, 500f,
                letterLayer, QueryTriggerInteraction.Collide))
            return null;

        foreach (Transform letter in orderedLetters)
        {
            if (hit.transform == letter || hit.transform.IsChildOf(letter))
                return letter;
        }

        return null;
    }

    private bool TryGetPlanePoint(Vector2 screenPosition, out Vector3 point)
    {
        Plane plane = new Plane(
            puzzleCamera.transform.forward,
            planeAnchor.position
        );

        Ray ray = puzzleCamera.ScreenPointToRay(screenPosition);

        if (plane.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance)
                    - puzzleCamera.transform.forward * brushOffset;
            return true;
        }

        point = default;
        return false;
    }

    private Vector3 LetterPoint(Transform letter)
    {
        return letter.position
               - puzzleCamera.transform.forward * brushOffset;
    }

    private void AddPoint(Vector3 point)
    {
        line.positionCount++;
        line.SetPosition(line.positionCount - 1, point);
    }

    private void ResetBrush()
    {
        drawing = false;
        currentLetter = 0;
        line.positionCount = 0;
    }

    private bool ReadPointer(
        out Vector2 position,
        out bool pressed,
        out bool held,
        out bool released)
    {
        if (Touchscreen.current != null)
        {
            var touch = Touchscreen.current.primaryTouch;

            if (touch.press.isPressed ||
                touch.press.wasPressedThisFrame ||
                touch.press.wasReleasedThisFrame)
            {
                position = touch.position.ReadValue();
                pressed = touch.press.wasPressedThisFrame;
                held = touch.press.isPressed;
                released = touch.press.wasReleasedThisFrame;
                return true;
            }
        }

        if (Mouse.current != null)
        {
            position = Mouse.current.position.ReadValue();
            pressed = Mouse.current.leftButton.wasPressedThisFrame;
            held = Mouse.current.leftButton.isPressed;
            released = Mouse.current.leftButton.wasReleasedThisFrame;
            return pressed || held || released;
        }

        position = default;
        pressed = held = released = false;
        return false;
    }
}