using UnityEngine;

public class AttackBlackoutBehaviour : StateMachineBehaviour
{
    [Range(0f, 1f)]
    public float triggerNormalizedTime = 0.35f;

    private bool fired;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        fired = false;
    }

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (fired)
        {
            return;
        }

        float t = stateInfo.normalizedTime;
        if (t > 1f)
        {
            t = t % 1f;
        }

        if (t < Mathf.Clamp01(triggerNormalizedTime))
        {
            return;
        }

        DinoAttackController controller = animator.GetComponentInParent<DinoAttackController>();
        if (controller != null)
        {
            controller.AnimEvent_TriggerBlackout();
        }

        fired = true;
    }
}
