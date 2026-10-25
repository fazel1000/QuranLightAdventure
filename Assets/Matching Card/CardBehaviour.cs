using System.Collections;
using UnityEngine;

public class CardBehaviour : MonoBehaviour
{
    public bool isFlipped = false;
    private bool isLocked = false;
    [SerializeField] private float flipDuration = 0.3f;

    public IEnumerator Flip(bool open)
    {
        if (isLocked) yield break;

        Quaternion startRot = transform.rotation;
        Quaternion endRot;

        if (open)
        {
            endRot = startRot * Quaternion.Euler(0, 180, 0);
            isFlipped = true;
        }
        else
        {
            endRot = startRot * Quaternion.Euler(0, -180, 0);
            isFlipped = false;
        }

        float t = 0;
        while (t < flipDuration)
        {
            transform.rotation = Quaternion.Slerp(startRot, endRot, t / flipDuration);
            t += Time.deltaTime;
            yield return null;
        }
        transform.rotation = endRot;
    }

    public void Lock() => isLocked = true;
    public void Unlock() => isLocked = false;
}
