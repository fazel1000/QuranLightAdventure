using UnityEngine;


namespace ModularBoysCharacters
{
    public class CharacterSelectionController : MonoBehaviour
    {
        [SerializeField] private GameObject[] characters;
        [SerializeField] private GameObject[] characterUIs;

        private int currentIndex = 0;
        private Animator currentAnimator;

        void Start()
        {
            ShowCharacter(currentIndex);
        }

        void ShowCharacter(int index)
        {
            for (int i = 0; i < characters.Length; i++)
            {
                characters[i].SetActive(i == index);

                if (characterUIs != null && characterUIs.Length > i)
                    characterUIs[i].SetActive(i == index);
            }

            currentAnimator = characters[index].GetComponent<Animator>();
        }


        // ====================== Animations ======================

        public void PlayWalk()
        {
            if (currentAnimator == null) return;
            PlayIdle();
            currentAnimator.SetBool("Walk", true);
        }

        public void PlayRun()
        {
            if (currentAnimator == null) return;
            PlayIdle();
            currentAnimator.SetBool("Run", true);
        }

        public void PlayJump()
        {
            if (currentAnimator == null) return;
            PlayIdle();
            currentAnimator.SetTrigger("Jump");
        }

        public void PlayIdle()
        {
            if (currentAnimator == null) return;
            currentAnimator.SetBool("Walk", false);
            currentAnimator.SetBool("Run", false);
        }


    }
}
