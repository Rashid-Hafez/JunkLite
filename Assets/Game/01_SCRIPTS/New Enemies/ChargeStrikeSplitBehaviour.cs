using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Splits one Attack clip into "charge" (first part) and "strike" (rest).
    /// Not called from code — add in the Unity Animator: select a state → Add Behaviour → ChargeStrikeSplitBehaviour.
    /// Add to two Animator states that use the SAME clip:
    /// - Charge state: set IsChargeState = true → plays clip from 0 and pauses at ChargeEndNormalizedTime.
    /// - Strike state: set IsChargeState = false → on enter, plays the same clip starting at ChargeEndNormalizedTime so the rest plays.
    /// </summary>
    public class ChargeStrikeSplitBehaviour : StateMachineBehaviour
    {
        [Tooltip("Normalized time (0–1) where the charge ends and the strike begins. e.g. 0.5 = first half is charge, second half is strike.")]
        [Range(0.1f, 0.99f)]
        [SerializeField] private float chargeEndNormalizedTime = 0.5f;

        [Tooltip("True = this state is the CHARGE (pause at chargeEnd). False = this state is the STRIKE (start at chargeEnd).")]
        [SerializeField] private bool isChargeState = true;

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (isChargeState)
            {
                // Charge: play from 0 (default). We'll pause in Update.
                animator.speed = 1f;
                return;
            }

            // Strike: start playback at the charge-end point so the "rest" of the clip plays
            animator.Play(stateInfo.fullPathHash, layerIndex, chargeEndNormalizedTime);
            animator.speed = 1f;
        }

        public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            if (!isChargeState) return;

            // Charge: pause when we reach the end of the charge portion
            if (stateInfo.normalizedTime >= chargeEndNormalizedTime)
                animator.speed = 0f;
        }

        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            // Restore speed so other states aren't stuck at 0
            animator.speed = 1f;
        }
    }
}
