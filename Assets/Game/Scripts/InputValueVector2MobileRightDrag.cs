using System;
using GameCreator.Runtime.Common;
using UnityEngine;

[Title("Right Screen Drag")]
[Category("Mobile/Right Screen Drag")]
[Description("Reads camera rotation from a dedicated UI drag area")]
[Serializable]
public class InputValueVector2MobileRightDrag : TInputValueVector2
{
    [SerializeField] private float sensitivity = 0.08f;
    [SerializeField] private bool invertVertical = true;

    [NonSerialized] private Vector2 value;

    public override void OnStartup()
    {
        value = Vector2.zero;
        RightScreenDragArea.ClearInput();
    }

    public override void OnDispose()
    {
        value = Vector2.zero;
        RightScreenDragArea.ClearInput();
    }

    public override void OnUpdate()
    {
        Vector2 delta = RightScreenDragArea.ConsumeDelta() * sensitivity;

        value = new Vector2(
            delta.x,
            invertVertical ? -delta.y : delta.y
        );
    }

    public override Vector2 Read()
    {
        return value;
    }
}