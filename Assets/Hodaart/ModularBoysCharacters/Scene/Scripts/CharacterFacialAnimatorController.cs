using UnityEngine;

namespace ModularBoysCharacters
{
    public class CharacterFacialAnimatorController : MonoBehaviour
    {
        [SerializeField]
        private Animator facialAnimator;
        // Start is called once before the first execution of Update after the MonoBehaviour is created

        public void PlayIdle()
        {
            if (facialAnimator == null) return;
            facialAnimator.SetBool("Talk", false);
            facialAnimator.SetBool("Smile", false);
            facialAnimator.SetBool("Frown", false);
            facialAnimator.SetBool("Surprised", false);
        }
        public void PlayTalk()
        {
            if (facialAnimator == null) return;
            PlayIdle();
            facialAnimator.SetBool("Talk", true);
        }
        public void PlaySmile()
        {
            if (facialAnimator == null) return;
            PlayIdle();
            facialAnimator.SetBool("Smile", true);
        }
        public void PlayFrown()
        {
            if (facialAnimator == null) return;
            PlayIdle();
            facialAnimator.SetBool("Frown", true);
        }
        public void PlaySurprised()
        {
            if (facialAnimator == null) return;
            PlayIdle();
            facialAnimator.SetBool("Surprised", true);
        }
        public void PlayBlink()
        {
            if (facialAnimator == null) return;
            PlayIdle();
            facialAnimator.SetTrigger("Blink");
        }
    }
}
