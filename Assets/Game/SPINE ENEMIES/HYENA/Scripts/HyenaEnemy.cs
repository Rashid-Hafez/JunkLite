using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Hyena Spine enemy (FSM-driven, visuals driven by Animator/SkeletonMecanim).
    /// Mirrors RobotEnemy decision hooks so it plugs into the same state architecture.
    ///
    /// BEHAVIOR:
    /// - Player spotted → Enter combat, start charging
    /// - Charge complete → Dash/attack towards captured target position
    /// - Dash complete → Recover
    /// - Recovery complete → If player still visible, charge again; else exit combat and patrol/idle
    /// </summary>
    public class HyenaEnemy : EnemyCharacter
    {
        [Header("Hyena - Dash Attack")]
        [SerializeField] private float dashChargeTime = 0.75f;
        [SerializeField] private float dashSpeed = 14f;
        [SerializeField] private float dashRecoveryTime = 0.35f;
        [SerializeField] private float dashDamage = 12f;
        [SerializeField] private Vector2 dashKnockback = new Vector2(14f, 5f);

        // Override base class properties (consumed by states)
        public override float DashChargeTime => dashChargeTime;
        public override float DashSpeed => dashSpeed;
        public override float DashRecoveryTime => dashRecoveryTime;
        public override float DashDamage => dashDamage;
        public override Vector2 DashKnockback => dashKnockback;

        protected override void Awake()
        {
            base.Awake();

            // Optional: if you use StunnedState, you can auto-transition when knockback begins.
            if (movement != null)
            {
                movement.OnKnockbackStart += HandleKnockbackStart;
                movement.OnKnockbackEnd += HandleKnockbackEnd;
            }
        }

        protected override void OnDestroy()
        {
            if (movement != null)
            {
                movement.OnKnockbackStart -= HandleKnockbackStart;
                movement.OnKnockbackEnd -= HandleKnockbackEnd;
            }
            base.OnDestroy();
        }

        protected override void InitializeStateMachine()
        {
            stateMachine.RegisterStates(
                new PatrolState(this),
                new IdleState(this),
                new ChargeState(this),
                new DashState(this),
                new RecoverState(this),
                new StunnedState(this),
                new DeadState(this)
            );

            if (HasPatrol)
                stateMachine.SetInitialState<PatrolState>();
            else
                stateMachine.SetInitialState<IdleState>();
        }

        // === HYENA BRAIN - All decisions live here ===

        public override void OnPlayerSpotted()
        {
            if (!IsAlive) return;
            if (isInCombat) return;

            EnterCombat();
            stateMachine.ChangeState<ChargeState>();
        }

        public override void OnPlayerLost()
        {
            if (!IsAlive) return;

            // Only exit combat if not in an active combat sequence.
            if (!isInCombat)
            {
                if (HasPatrol)
                    stateMachine.ChangeState<PatrolState>();
                else
                    stateMachine.ChangeState<IdleState>();
            }
        }

        public override void OnChargeComplete()
        {
            if (!IsAlive) return;
            stateMachine.ChangeState<DashState>();
        }

        public override void OnDashComplete()
        {
            if (!IsAlive) return;
            stateMachine.ChangeState<RecoverState>();
        }

        public override void OnRecoveryComplete()
        {
            if (!IsAlive) return;

            if (HasTarget)
            {
                stateMachine.ChangeState<ChargeState>();
            }
            else
            {
                ExitCombat();
                if (HasPatrol)
                    stateMachine.ChangeState<PatrolState>();
                else
                    stateMachine.ChangeState<IdleState>();
            }
        }

        public override void OnStunComplete()
        {
            if (!IsAlive) return;

            // After stun, return to recovery to re-stabilize, then decision continues.
            stateMachine.ChangeState<RecoverState>();
        }

        private void HandleKnockbackStart()
        {
            if (!IsAlive) return;
            // Don't interrupt DeadState
            if (stateMachine != null && stateMachine.IsInState<DeadState>()) return;
            stateMachine?.ChangeState<StunnedState>();
        }

        private void HandleKnockbackEnd()
        {
            // Inform the state (if we are still in it) so it can complete cleanly.
            var st = stateMachine?.GetState<StunnedState>();
            st?.NotifyKnockbackEnded();
        }
    }
}


