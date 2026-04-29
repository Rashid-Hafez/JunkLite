using UnityEngine;
using Spine;
using Spine.Unity;

namespace junklite
{
    public class EnemySpineAnimationController : MonoBehaviour
    {
        [Header("Spine")]
        [SerializeField] private SkeletonAnimation skeletonAnimation;

        [Header("Animation Names")]
        [SerializeField] private string idle = "Idle";
        [SerializeField] private string walk = "Run";
        [SerializeField] private string run = "Run";
        [SerializeField] private string attackWindUp = "";
        [SerializeField] private string attack = "Attack_1";
        [SerializeField] private string charge = "Charge";
        [SerializeField] private string dash = "Dash";
        [SerializeField] private string dodge = "JumpBack";
        [SerializeField] private string hurt = "Hurt";
        [SerializeField] private string death = "Death";

        [Header("Stun")]
        [SerializeField] private string stunLoop = "";
        [Header("Parry")]
        [Tooltip("Spine animation to play when entering ParriedState (one-shot).")]
        [SerializeField] private string onParried = "OnParried";

        [Header("Debug")]
        [SerializeField] private bool debugLog = false;

        private StateMachine stateMachine;
        private EnemyCharacter enemyCharacter;
        private bool isDead;

        private void Awake()
        {
            if (skeletonAnimation == null)
                skeletonAnimation = GetComponentInChildren<SkeletonAnimation>(true);

            stateMachine = GetComponentInParent<StateMachine>();
            enemyCharacter = GetComponentInParent<EnemyCharacter>();
        }

        private void OnEnable()
        {
            if (stateMachine != null)
                stateMachine.OnStateChanged += HandleStateChanged;
        }

        private void Start()
        {
            StartCoroutine(SyncInitialState());
        }

        private System.Collections.IEnumerator SyncInitialState()
        {
            yield return null;

            if (stateMachine != null && stateMachine.CurrentState != null)
            {
                if (debugLog)
                    Debug.Log($"[AnimCtrl] Syncing initial state: {stateMachine.CurrentState.GetType().Name}");
                HandleStateChanged(null, stateMachine.CurrentState);
            }
        }

        private void OnDisable()
        {
            if (stateMachine != null)
                stateMachine.OnStateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(IState from, IState to)
        {
            if (debugLog)
                Debug.Log($"[AnimCtrl] State: {from?.GetType().Name ?? "null"} -> {to?.GetType().Name ?? "null"}");

            if (skeletonAnimation == null || to == null) return;

            if (to is DeadState)
            {
                PlayDeath();
                return;
            }

            if (isDead) return;

            var state = skeletonAnimation.AnimationState;
            skeletonAnimation.Skeleton.SetToSetupPose();
            state.ClearTrack(0);

            if (to is IdleState)
                state.SetAnimation(0, idle, true);
            else if (to is PatrolState)
                state.SetAnimation(0, walk, true);
            else if (to is ChaseState)
                state.SetAnimation(0, run, true);
            else if (to is MeleeAttackState)
            {
                // Don't play anything here ù the enemy will call
                // PlayWindUpAnimation() and PlayAttackAnimation()
                // at the right moments via OnMeleeWindUp / OnMeleeAttack.
            }
            else if (to is ChargeState)
                state.SetAnimation(0, charge, true);
            else if (to is DashState)
                state.SetAnimation(0, dash, false);
            else if (to is DodgeState)
                state.SetAnimation(0, dodge, false);
            else if (to is ParriedState)
            {
                // ParriedState = parry success. Play a short 'on parried' anim, then transition to the stun loop.
                string parriedAnim = string.IsNullOrEmpty(onParried) ? idle : onParried;
                string stunAnim = string.IsNullOrEmpty(stunLoop) ? hurt : stunLoop;

                state.SetAnimation(0, parriedAnim, false);
                state.AddAnimation(0, stunAnim, true, 0f);
            }
            else if (to is StunnedState)
            {
                // Parry stun = held neutral pose, normal stagger = hurt animation
                if (enemyCharacter != null && enemyCharacter.IsParryStunned)
                {
                    string stunAnim = string.IsNullOrEmpty(stunLoop) ? idle : stunLoop;
                    state.SetAnimation(0, stunAnim, true);
                }
                else
                {
                    state.SetAnimation(0, hurt, false);
                }
            }
        }

        #region Public API

        public void PlayWindUpAnimation()
        {
            if (isDead || skeletonAnimation == null) return;

            var state = skeletonAnimation.AnimationState;
            skeletonAnimation.Skeleton.SetToSetupPose();
            state.ClearTrack(0);

            if (!string.IsNullOrEmpty(attackWindUp))
                state.SetAnimation(0, attackWindUp, false);
            else
                state.SetAnimation(0, idle, true);
        }

        public void PlayAttackAnimation()
        {
            if (isDead || skeletonAnimation == null) return;

            var state = skeletonAnimation.AnimationState;
            var entry = state.SetAnimation(0, attack, false);
            if (entry != null) entry.MixDuration = 0f;
        }

        public void PlayStunLoop(float duration)
        {
            if (skeletonAnimation == null) return;
            string anim = string.IsNullOrEmpty(stunLoop) ? hurt : stunLoop;
            var state = skeletonAnimation.AnimationState;
            state.SetAnimation(0, anim, true);
            StartCoroutine(ClearStunAfter(duration));
        }

        #endregion

        private System.Collections.IEnumerator ClearStunAfter(float duration)
        {
            yield return new WaitForSeconds(duration);
            if (stateMachine != null && stateMachine.CurrentState != null)
                HandleStateChanged(null, stateMachine.CurrentState);
        }

        private void PlayDeath()
        {
            if (isDead) return;
            isDead = true;

            var state = skeletonAnimation.AnimationState;
            skeletonAnimation.Skeleton.SetToSetupPose();
            state.ClearTracks();
            var entry = state.SetAnimation(0, death, false);

            if (entry != null)
                entry.MixDuration = 0f;
            else
                Debug.LogError($"[AnimCtrl] Failed to play '{death}' - check animation name in Spine!");
        }
    }
}