using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Hyena-specific decisions layered over the reusable melee-chaser loop.
    /// Only reactive dodge and counter-dash policy live here.
    /// </summary>
    public sealed class HyenaBrain : MeleeChaserBrain
    {
        [Header("Reactive Dodge")]
        [SerializeField] private DodgeBehavior dodge = new();
        [SerializeField, Range(0f, 1f)] private float dodgeChance = 0.3f;
        [SerializeField] private float dodgeCheckRange = 4f;
        [SerializeField] private float dodgeCooldown = 1f;

        [Header("Counter Dash")]
        [SerializeField] private ChargeBehavior charge = new();
        [SerializeField] private DashBehavior dash = new();
        [SerializeField, Range(0f, 1f)] private float dashChance = 0.4f;
        [SerializeField] private float whiffStunDuration = 1.5f;
        [SerializeField] private float maxCounterDashRange = 8f;

        private float lastDodgeTime = float.NegativeInfinity;
        private bool wasPlayerAttacking;
        private bool dodgeWasReactive;

        protected override void Awake()
        {
            base.Awake();
            EnsureHyenaBehaviors();
            InitializeHyenaCapabilities();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            InitializeHyenaCapabilities();
        }

        protected override void OnDisable()
        {
            UninitializeHyenaCapabilities();
            base.OnDisable();
        }

        protected override void InitializeStateMachine()
        {
            StateMachine.RegisterStates(
                new PatrolState(Actor),
                new IdleState(Actor),
                new ChaseState(Actor),
                new MeleeAttackState(Actor),
                new DodgeState(Actor),
                new ChargeState(Actor),
                new DashState(Actor),
                new StunnedState(Actor),
                new ParriedState(Actor),
                new DeadState(Actor));

            if (patrolWhenPassive && patrol.HasPatrol)
                StateMachine.SetInitialState<PatrolState>();
            else
                StateMachine.SetInitialState<IdleState>();
        }

        protected override void TickBrain()
        {
            base.TickBrain();

            if (Actor.IsInCombat)
                CheckForDodgeOpportunity();
        }

        protected override bool IsDecisionLocked()
        {
            IState current = StateMachine.CurrentState;
            return base.IsDecisionLocked()
                || current is DodgeState
                || current is ChargeState
                || current is DashState;
        }

        public override bool TryGetCapability<T>(out T capability)
        {
            if (dodge is T dodgeCapability)
                capability = dodgeCapability;
            else if (charge is T chargeCapability)
                capability = chargeCapability;
            else if (dash is T dashCapability)
                capability = dashCapability;
            else
                return base.TryGetCapability(out capability);

            return true;
        }

        public void ApplyLegacyHyenaConfiguration(
            EnemySpineAnimationController animationController,
            PatrolBehavior patrolBehavior,
            ChaseBehavior chaseBehavior,
            float configuredPursuitRadius,
            MeleeAttackBehavior meleeBehavior,
            DodgeBehavior dodgeBehavior,
            float configuredDodgeChance,
            float configuredDodgeCheckRange,
            float configuredDodgeCooldown,
            ChargeBehavior chargeBehavior,
            DashBehavior dashBehavior,
            float configuredDashChance,
            float configuredWhiffStunDuration,
            float configuredMaxCounterDashRange,
            StunBehavior stunBehavior)
        {
            if (OwnsSerializedConfiguration)
                return;

            ApplyLegacyConfiguration(
                animationController,
                true,
                patrolBehavior,
                chaseBehavior,
                configuredPursuitRadius,
                meleeBehavior,
                stunBehavior);

            UninitializeHyenaCapabilities();
            dodge = dodgeBehavior ?? new DodgeBehavior();
            dodgeChance = configuredDodgeChance;
            dodgeCheckRange = configuredDodgeCheckRange;
            dodgeCooldown = configuredDodgeCooldown;
            charge = chargeBehavior ?? new ChargeBehavior();
            dash = dashBehavior ?? new DashBehavior();
            dashChance = configuredDashChance;
            whiffStunDuration = configuredWhiffStunDuration;
            maxCounterDashRange = configuredMaxCounterDashRange;
            InitializeHyenaCapabilities();
        }

        private void CheckForDodgeOpportunity()
        {
            if (!Actor.IsAlive || !Actor.HasTarget)
            {
                wasPlayerAttacking = false;
                return;
            }

            IState current = StateMachine.CurrentState;
            if (current is DodgeState
                || current is ParriedState
                || current is StunnedState
                || current is ChargeState
                || current is DashState)
            {
                wasPlayerAttacking = IsPlayerAttacking();
                return;
            }

            if (Time.time - lastDodgeTime < dodgeCooldown || Actor.DistanceToTarget > dodgeCheckRange)
            {
                wasPlayerAttacking = IsPlayerAttacking();
                return;
            }

            bool attacking = IsPlayerAttacking();
            if (attacking && !wasPlayerAttacking && IsPlayerFacingMe() && Random.value <= dodgeChance)
            {
                lastDodgeTime = Time.time;
                dodgeWasReactive = true;
                ChangeState<DodgeState>();
            }

            wasPlayerAttacking = attacking;
        }

        private void HandleDodgeCompleted()
        {
            if (!Actor.IsAlive)
                return;

            if (dodgeWasReactive
                && Actor.HasTarget
                && Actor.DistanceToTarget <= maxCounterDashRange
                && Random.value <= dashChance)
            {
                dodgeWasReactive = false;
                ChangeState<ChargeState>();
                return;
            }

            dodgeWasReactive = false;
            EvaluateNextAction(true);
        }

        private void HandleChargeCompleted()
        {
            if (!Actor.IsAlive)
                return;

            dash.ResetHitResult();
            ChangeState<DashState>();
        }

        private void HandleDashCompleted()
        {
            if (!Actor.IsAlive)
                return;

            if (!Actor.HasTarget)
            {
                ReturnToPassive();
                return;
            }

            if (!dash.LastHitApplied)
            {
                stun.ForcedStunDuration = whiffStunDuration;
                ChangeState<StunnedState>();
                return;
            }

            EvaluateNextAction(true);
        }

        private bool IsPlayerAttacking()
        {
            if (Actor.TargetCharacter == null)
                return false;

            PlayerState playerState = Actor.TargetCharacter.GetComponentInParent<PlayerState>();
            if (playerState != null)
                return playerState.IsAttacking;

            CharacterState characterState = Actor.TargetCharacter.GetComponent<CharacterState>();
            return characterState != null && characterState.IsAttacking;
        }

        private bool IsPlayerFacingMe()
        {
            Transform target = Actor.Target;
            if (target == null)
                return false;

            Vector3 playerFacing = target.right * Mathf.Sign(target.localScale.x);
            Vector3 playerToEnemy = (transform.position - target.position).normalized;
            return Vector3.Dot(playerFacing, playerToEnemy) > 0f;
        }

        private void EnsureHyenaBehaviors()
        {
            dodge ??= new DodgeBehavior();
            charge ??= new ChargeBehavior();
            dash ??= new DashBehavior();
        }

        private void InitializeHyenaCapabilities()
        {
            EnsureHyenaBehaviors();
            UninitializeHyenaCapabilities();

            dodge.Completed += HandleDodgeCompleted;
            charge.Completed += HandleChargeCompleted;
            dash.Completed += HandleDashCompleted;
            dash.Initialize(gameObject);
        }

        private void UninitializeHyenaCapabilities()
        {
            if (dodge != null)
                dodge.Completed -= HandleDodgeCompleted;
            if (charge != null)
                charge.Completed -= HandleChargeCompleted;
            if (dash != null)
            {
                dash.Completed -= HandleDashCompleted;
                dash.Dispose();
            }
        }

#if UNITY_EDITOR
        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();

            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, dodgeCheckRange);
        }
#endif
    }
}
