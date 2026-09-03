using System;
using UnityEngine;
using UnityEngine.InputSystem;
using GameCreator.Runtime.Common;

[Title("Right Screen Drag")]
[Category("Mobile/Right Screen Drag")]
[Serializable]
public class InputValueVector2MobileRightDrag : TInputValueVector2
{
    [SerializeField] private float sensitivity = 0.08f;

    [NonSerialized] private int touchId = -1;
    [NonSerialized] private Vector2 value;

    public override void OnUpdate()
    {
        value = Vector2.zero;

        Touchscreen screen = Touchscreen.current;
        if (screen == null) return;

        if (touchId < 0)
        {
            foreach (var touch in screen.touches)
            {
                if (touch.press.wasPressedThisFrame &&
                    touch.position.ReadValue().x >= Screen.width * 0.5f)
                {
                    touchId = touch.touchId.ReadValue();
                    break;
                }
            }
        }

        foreach (var touch in screen.touches)
        {
            if (touch.touchId.ReadValue() != touchId) continue;

            if (!touch.press.isPressed)
            {
                touchId = -1;
                return;
            }

            Vector2 delta = touch.delta.ReadValue() * sensitivity;
            value = new Vector2(delta.x, -delta.y);
            return;
        }
    }

    public override Vector2 Read() => value;
}