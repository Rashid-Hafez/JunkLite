using UnityEngine;
using Spine;
using Spine.Unity;

namespace junklite
{
    /// <summary>
    /// Enemy animation controller using Spine.
    /// 
    /// HITBOX CONTROL:
    /// Option 1: Add Spine events "hit_start" and "hit_end" in your animation
    /// Option 2: Enable timer fallback and set attackHitStartTime/attackHitDuration
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

        [Header("Spine Event Names")]
        [SerializeField] private string hitStartEvent = "hit_start";
        [SerializeField] private string hitEndEvent = "hit_end";

        [Header("Timer Fallback (if no Spine events)")]
        [SerializeField] private bool useTimerFallback = true;
        [SerializeField] private float attackHitStartTime = 0.15f;
        [SerializeField] private float attackHitDuration = 0.1f;

        [Header("Debug")]
        [SerializeField] private bool debugLog = false;

        private StateMachine stateMachine;
        private EnemyCharacter enemy;
        private IMeleeAttacker meleeAttacker;
        private IDasher dasher;

        private TrackEntry currentAttackEntry;
        private bool isAttacking;
        private bool hitboxActive;

        // Timer fallback
        private float attackStartTime;
        private bool hitStarted;

        // Cooldown between attacks
        private bool isInCooldown;
        private float cooldownTimer;

        private void Awake()
        {
            if (skeletonAnimation == null)
                skeletonAnimation = GetComponentInChildren<SkeletonAnimation>(true);

            enemy = GetComponentInParent<EnemyCharacter>();
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
            // Delay one frame to ensure state machine has initialized
            StartCoroutine(SyncInitialState());
        }

        private System.Collections.IEnumerator SyncInitialState()
        {
            yield return null; // Wait one frame

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
            // Handle cooldown between attacks
            if (isInCooldown)
            {
                cooldownTimer -= Time.deltaTime;
                if (cooldownTimer <= 0f)
                {
                    isInCooldown = false;

                    // If still in MeleeAttackState, restart attack
                    if (stateMachine != null && stateMachine.CurrentState is MeleeAttackState)
                    {
                        if (debugLog)
                            Debug.Log($"[AnimCtrl] Cooldown done, restarting attack");
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

        private void OnSpineEvent(TrackEntry trackEntry, Spine.Event e)
        {
            // Only process events from our attack animation
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
            // Only care about our attack animation completing
            if (!isAttacking) return;
            if (trackEntry != currentAttackEntry) return;
            if (trackEntry.Animation.Name != attack) return;

            if (debugLog)
                Debug.Log($"[AnimCtrl] Attack animation complete at {Time.time:F2}");

            DeactivateHitbox();
            isAttacking = false;
            currentAttackEntry = null;

            // Let enemy decide what to do next
            meleeAttacker?.OnMeleeComplete();

            // If still in MeleeAttackState after OnMeleeComplete, start cooldown then restart
            if (stateMachine != null && stateMachine.CurrentState is MeleeAttackState)
            {
                float cooldown = meleeAttacker?.MeleeAttackSpeed ?? 0f;
                if (cooldown > 0f)
                {
                    isInCooldown = true;
                    cooldownTimer = cooldown;
                    if (debugLog)
                        Debug.Log($"[AnimCtrl] Starting cooldown: {cooldown}s");
                }
                else
                {
                    // No cooldown, restart immediately
                    StartAttackAnimation();
                }
            }
        }

        private void ActivateHitbox()
        {
            if (hitboxActive) return;

            hitboxActive = true;
            meleeAttacker?.MeleeHitbox?.Activate();

            if (debugLog)
                Debug.Log($"[AnimCtrl] Hitbox ON");
        }

        private void DeactivateHitbox()
        {
            if (!hitboxActive) return;

            hitboxActive = false;
            meleeAttacker?.MeleeHitbox?.Deactivate();

            if (debugLog)
                Debug.Log($"[AnimCtrl] Hitbox OFF");
        }

        private void StartAttackAnimation()
        {
            if (skeletonAnimation == null) return;

            // Ensure clean state
            isAttacking = true;
            hitStarted = false;
            hitboxActive = false;
            attackStartTime = Time.time;

            // Clear and play fresh with no mixing
            var state = skeletonAnimation.AnimationState;
            state.ClearTrack(0);
            currentAttackEntry = state.SetAnimation(0, attack, false);
            currentAttackEntry.MixDuration = 0f;  // No blending from previous animation

            if (debugLog)
                Debug.Log($"[AnimCtrl] Attack started at {Time.time:F2}");
        }

        private void HandleStateChanged(IState from, IState to)
        {
            if (debugLog)
                Debug.Log($"[AnimCtrl] State: {from?.GetType().Name} -> {to?.GetType().Name}");

            if (skeletonAnimation == null || to == null) return;

            // Always clean up attack state when changing states
            ResetAttackState();

            var state = skeletonAnimation.AnimationState;

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
            else if (to is StunnedState)
                state.SetAnimation(0, hurt, false);
            else if (to is DeadState)
                state.SetAnimation(0, death, false);
        }

        private void ResetAttackState()
        {
            // Force deactivate all hitboxes regardless of flag state
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