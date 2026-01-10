using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Enemy animation driver (simple + hardcoded).
    ///
    /// Main idea:
    /// - The FSM (StateMachine) changes states.
    /// - StateMachine broadcasts OnStateChanged(previousState, currentState).
    /// - This component subscribes to that event and sets Animator params based on the NEW state.
    ///
    /// This keeps animation logic out of individual states, and keeps the code readable.
    /// </summary>
    public class EnemyAnimationController : MonoBehaviour
    {
        [Header("Animator")]
        [SerializeField] private Animator animator;
        [SerializeField] private bool autoFindAnimator = true;

        [Header("Optional: Auto update locomotion params")]
        [Tooltip("When enabled, pushes Speed/IsMoving into Animator every frame based on Rigidbody planar velocity.")]
        [SerializeField] private bool autoUpdateLocomotion = true;
        [SerializeField] private float movingSpeedThreshold = 0.05f;

        [Header("Optional: Drive from StateMachine transitions")]
        [Tooltip("If enabled, triggers animations when the FSM changes states.")]
        [SerializeField] private bool driveFromStateMachine = true;

        private StateMachine stateMachine;
        private Rigidbody rb;

        private void Awake()
        {
            if (autoFindAnimator && animator == null)
                animator = GetComponentInChildren<Animator>(true);

            stateMachine = GetComponentInParent<StateMachine>();
            rb = GetComponentInParent<Rigidbody>();
        }

        private void Update()
        {
            if (!autoUpdateLocomotion || animator == null || rb == null) return;

            Vector3 v = rb.linearVelocity;
            float planarSpeed = new Vector2(v.x, v.z).magnitude;

            // Locomotion params (common across most enemies)
            animator.SetFloat("Speed", planarSpeed);
            animator.SetBool("IsMoving", planarSpeed > movingSpeedThreshold);
        }

        private void OnEnable()
        {
            if (driveFromStateMachine && stateMachine != null)
                stateMachine.OnStateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (driveFromStateMachine && stateMachine != null)
                stateMachine.OnStateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(IState from, IState to)
        {
            // MAIN DRIVER:
            // FSM changes state -> StateMachine fires OnStateChanged(previous, current) -> we land here
            // -> set Animator parameters based on the NEW state (to).
            if (animator == null || to == null) return;

            // Triggers on ENTER of these states:
            if (to is ChargeState) animator.SetTrigger("Charge");
            else if (to is DashState) animator.SetTrigger("Dash");
            else if (to is RecoverState) animator.SetTrigger("Recover");
            else if (to is StunnedState) animator.SetTrigger("Hurt");
            else if (to is DeadState) animator.SetTrigger("Die");

            // If you prefer bools instead of triggers, do it here:
            // animator.SetBool("IsCharging", to is ChargeState);
            // animator.SetBool("IsDashing",  to is DashState);
        }
    }
}