using UnityEngine;
using Spine;
using Spine.Unity;

namespace junklite
{
    /// <summary>
    /// Simple enemy animation controller using Spine.
    /// Listens to state changes and plays appropriate animations.
    /// </summary>
    public class EnemySpineAnimationController : MonoBehaviour
    {
        [Header("Spine")]
        [SerializeField] private SkeletonAnimation skeletonAnimation;

        [Header("Animation Names")]
        [SerializeField] private string idle = "Idle";
        [SerializeField] private string walk = "Run";
        [SerializeField] private string run = "Run";
        [SerializeField] private string attack = "Attack_1";
        [SerializeField] private string charge = "Charge";
        [SerializeField] private string dash = "Dash";
        [SerializeField] private string dodge = "JumpBack";
        [SerializeField] private string hurt = "Hurt";
        [SerializeField] private string death = "Death";
        [Header("Stun")]
        [Tooltip("Looping animation to play while stunned due to a parry; leave blank to use 'hurt'")]
        [SerializeField] private string stunLoop = "";

        [Header("Hitbox Timing (Timer Fallback)")]
        [SerializeField] private bool useTimerFallback = true;
        [SerializeField] private float attackHitStartTime = 0.15f;
        [SerializeField] private float attackHitDuration = 0.1f;

        [Header("Spine Event Names (if using events instead of timer)")]
        [SerializeField] private string hitStartEvent = "hit_start";
        [SerializeField] private string hitEndEvent = "hit_end";

        [Header("Debug")]
        [SerializeField] private bool debugLog = false;

        private StateMachine stateMachine;
        private IMeleeAttacker meleeAttacker;
        private IDasher dasher;

        private TrackEntry currentAttackEntry;
        private bool isAttacking;
        private bool hitboxActive;
        private float attackStartTime;
        private bool hitStarted;
        private bool isInCooldown;
        private float cooldownTimer;
        private bool isDead;

        private void Awake()
        {
            if (skeletonAnimation == null)
                skeletonAnimation = GetComponentInChildren<SkeletonAnimation>(true);

            stateMachine = GetComponentInParent<StateMachine>();
            meleeAttacker = GetComponentInParent<IMeleeAttacker>();
            dasher = GetComponentInParent<IDasher>();
        }

        private void OnEnable()
        {
            if (stateMachine != null)
                stateMachine.OnStateChanged += HandleStateChanged;

            if (skeletonAnimation != null)
            {
                skeletonAnimation.AnimationState.Event += OnSpineEvent;
                skeletonAnimation.AnimationState.Complete += OnAnimationComplete;
            }
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

            if (skeletonAnimation != null)
            {
                skeletonAnimation.AnimationState.Event -= OnSpineEvent;
                skeletonAnimation.AnimationState.Complete -= OnAnimationComplete;
            }
        }

        private void Update()
        {
            if (isDead) return;

            // Handle cooldown between attacks
            if (isInCooldown)
            {
                cooldownTimer -= Time.deltaTime;
                if (cooldownTimer <= 0f)
                {
                    isInCooldown = false;
                    if (stateMachine != null && stateMachine.CurrentState is MeleeAttackState)
                    {
                        if (debugLog) Debug.Log($"[AnimCtrl] Cooldown done, restarting attack");
                        StartAttackAnimation();
                    }
                }
                return;
            }

            // Timer fallback for hitbox
            if (!useTimerFallback || !isAttacking) return;

            float elapsed = Time.time - attackStartTime;

            if (!hitStarted && elapsed >= attackHitStartTime)
            {
                hitStarted = true;
                ActivateHitbox();
            }

            if (hitStarted && hitboxActive && elapsed >= attackHitStartTime + attackHitDuration)
            {
                DeactivateHitbox();
            }
        }

        private void HandleStateChanged(IState from, IState to)
        {
            if (debugLog)
                Debug.Log($"[AnimCtrl] State: {from?.GetType().Name ?? "null"} -> {to?.GetType().Name ?? "null"}");

            if (skeletonAnimation == null || to == null) return;

            // Death takes priority - once dead, stay dead
            if (to is DeadState)
            {
                PlayDeath();
                return;
            }

            // Don't process other states if dead
            if (isDead) return;

            ResetAttackState();

            var state = skeletonAnimation.AnimationState;

            // Reset skeleton to default pose, then clear the track.
            // ClearTrack alone leaves bones wherever the previous animation left them.
            // SetToSetupPose resets ALL bones to their original transforms first.
            skeletonAnimation.Skeleton.SetToSetupPose();
            state.ClearTrack(0);

            if (to is IdleState)
                state.SetAnimation(0, idle, true);
            else if (to is PatrolState)
                state.SetAnimation(0, walk, true);
            else if (to is ChaseState)
                state.SetAnimation(0, run, true);
            else if (to is MeleeAttackState)
                StartAttackAnimation();
            else if (to is ChargeState)
                state.SetAnimation(0, charge, true);
            else if (to is DashState)
                state.SetAnimation(0, dash, false);
            else if (to is DodgeState)
                state.SetAnimation(0, dodge, false);
            else if (to is HurtState)
                state.SetAnimation(0, hurt, false);
            else if (to is StunnedState)
                state.SetAnimation(0, hurt, true); // loop 'hurt' while stunned
        }

        private void PlayDeath()
        {
            if (isDead) return;
            isDead = true;

            if (debugLog)
                Debug.Log($"[AnimCtrl] DEATH - clearing all and playing: {death}");

            ResetAttackState();

            var state = skeletonAnimation.AnimationState;
            skeletonAnimation.Skeleton.SetToSetupPose();
            state.ClearTracks();
            var entry = state.SetAnimation(0, death, false);

            if (entry != null)
            {
                entry.MixDuration = 0f;
                if (debugLog)
                    Debug.Log($"[AnimCtrl] Death animation playing");
            }
            else
            {
                Debug.LogError($"[AnimCtrl] Failed to play '{death}' - check animation name in Spine!");
            }
        }

        private void OnSpineEvent(TrackEntry trackEntry, Spine.Event e)
        {
            if (!isAttacking) return;
            if (trackEntry.Animation.Name != attack) return;

            if (debugLog)
                Debug.Log($"[AnimCtrl] Spine Event: {e.Data.Name}");

            if (e.Data.Name == hitStartEvent)
                ActivateHitbox();
            else if (e.Data.Name == hitEndEvent)
                DeactivateHitbox();
        }

        private void OnAnimationComplete(TrackEntry trackEntry)
        {
            if (!isAttacking) return;
            if (trackEntry != currentAttackEntry) return;
            if (trackEntry.Animation.Name != attack) return;

            if (debugLog)
                Debug.Log($"[AnimCtrl] Attack complete");

            DeactivateHitbox();
            isAttacking = false;
            currentAttackEntry = null;

            meleeAttacker?.OnMeleeComplete();

            if (stateMachine != null && stateMachine.CurrentState is MeleeAttackState)
            {
                float cooldown = meleeAttacker?.MeleeAttackSpeed ?? 0f;
                if (cooldown > 0f)
                {
                    isInCooldown = true;
                    cooldownTimer = cooldown;
                }
            }
        }

        /// <summary>
        /// External call to play a looping stun animation for a set duration.
        /// After the timer expires we simply re-sync the animation based on current state.
        /// </summary>
        public void PlayStunLoop(float duration)
        {
            if (skeletonAnimation == null) return;
            string anim = string.IsNullOrEmpty(stunLoop) ? hurt : stunLoop;
            var state = skeletonAnimation.AnimationState;
            state.SetAnimation(0, anim, true);
            StartCoroutine(ClearStunAfter(duration));
        }

        private System.Collections.IEnumerator ClearStunAfter(float duration)
        {
            yield return new WaitForSeconds(duration);
            if (stateMachine != null && stateMachine.CurrentState != null)
                HandleStateChanged(null, stateMachine.CurrentState);
        }

        private void StartAttackAnimation()
        {
            if (skeletonAnimation == null || isDead) return;

            isAttacking = true;
            hitStarted = false;
            hitboxActive = false;
            attackStartTime = Time.time;

            var state = skeletonAnimation.AnimationState;
            currentAttackEntry = state.SetAnimation(0, attack, false);
            currentAttackEntry.MixDuration = 0f;

            if (debugLog)
                Debug.Log($"[AnimCtrl] Attack started");
        }

        private void ActivateHitbox()
        {
            if (hitboxActive || isDead) return;
            hitboxActive = true;
            meleeAttacker?.MeleeHitbox?.Activate();
            if (debugLog) Debug.Log($"[AnimCtrl] Hitbox ON");
        }

        private void DeactivateHitbox()
        {
            if (!hitboxActive) return;
            hitboxActive = false;
            meleeAttacker?.MeleeHitbox?.Deactivate();
            if (debugLog) Debug.Log($"[AnimCtrl] Hitbox OFF");
        }

        private void ResetAttackState()
        {
            hitboxActive = false;
            meleeAttacker?.MeleeHitbox?.Deactivate();
            dasher?.DashHitbox?.Deactivate();
            isAttacking = false;
            isInCooldown = false;
            hitStarted = false;
            currentAttackEntry = null;
        }
    }
}