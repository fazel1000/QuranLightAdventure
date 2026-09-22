using UnityEngine;

// Added to states by the setup tool, not to the player as a component.
public sealed class JourneyStoneAnimationLayer : StateMachineBehaviour
{
    public bool active;
    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (layerIndex > 0) animator.SetLayerWeight(layerIndex, active ? 1f : 0f);
    }
}
