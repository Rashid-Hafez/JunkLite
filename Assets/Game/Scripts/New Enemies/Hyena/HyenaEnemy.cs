using UnityEngine;

namespace junklite
{

    public class HyenaEnemy : EnemyCharacter, IPatroller, IChaser, IMeleeAttacker, IDodger, ICharger, IDasher, IStunnable
    {
        [Header("Animation")]
        [SerializeField] private EnemySpineAnimationController spineController;

        [Header("Hyena - Patrol")]
        [SerializeField] private PatrolBehavior patrol = new PatrolBehavior();

        [Header("Hyena - Chase")]
        [SerializeField] private ChaseBehavior chase = new ChaseBehavior();
        [SerializeField] private float pursuitRadius = 15f;

        [Header("Hyena - Melee Attack")]
        [SerializeField] private MeleeAttackBehavior melee = new MeleeAttackBehavior();
        public float MeleeWindUpDuration => melee.MeleeWindUpDuration;
        public float MeleeAttackDuration => melee.MeleeAttackDuration;
        public float MeleeHitStartNormalized => melee.MeleeHitStartNormalized;
        public float MeleeHitEndNormalized => melee.MeleeHitEndNormalized;

        [Header("Hyena - Dodge")]
        [SerializeField] private DodgeBehavior dodge = new DodgeBehavior();
        [SerializeField][Range(0f, 1f)] private float dodgeChance = 0.3f;
        [SerializeField] private float dodgeCheckRange = 4f;
        [SerializeField] private float dodgeCooldown = 1f;

        [Header("Hyena - Dash Counter-Attack")]
        [SerializeField] private ChargeBehavior charge = new ChargeBehavior();
        [SerializeField] private DashBehavior dash = new DashBehavior();
        [SerializeField][Range(0f, 1f)] private float dashChance = 0.4f;
        [Tooltip("Stun duration after a missed (whiffed) dash")]
        [SerializeField] private float whiffStunDuration = 1.5f;
        [Tooltip("Max distance to target for counter-dash to be allowed after a reactive dodge")]
        [SerializeField] private float maxCounterDashRange = 8f;

        [Header("Hyena - Stun")]
        [SerializeField] private StunBehavior stun = new StunBehavior();

        // Dodge state
        private float lastDodgeTime = -999f;
        private bool wasPlayerAttacking;
        private bool dodgeWasReactive;

        // Dash state
        private bool dashHitConnected;

        public bool HasPatrol => patrol.HasPatrol;

        #region IPatroller

        public float PatrolDistance => patrol.PatrolDistance;
        public float PatrolSpeed => patrol.PatrolSpeed;
        public Vector3 SpawnPosition => patrol.SpawnPosition;
        public int PatrolDirection { get => patrol.PatrolDirection; set => patrol.PatrolDirection = value; }
        public bool IsWallAhead() => patrol.IsWallAhead();
        public bool IsAtPatrolBoundary() => patrol.IsAtPatrolBoundary();
        public void ReverseDirection() => patrol.ReverseDirection();

        #endregion

        #region IChaser

        public Vector3 LastKnownTargetPosition => chase.LastKnownTargetPosition;
        public bool HasLastKnownPosition => chase.HasLastKnownPosition;
        public float ChaseSpeed => chase.ChaseSpeed;
        public float ChaseStopDistance => chase.ChaseStopDistance;
        public void UpdateLastKnownPosition(Vector3 pos) => chase.UpdateLastKnownPosition(pos);

        public void OnReachedTarget()
        {
            if (!IsAlive) return;
            chase.ClearLastKnownPosition();

            if (HasTarget && IsTargetAlive())
            {
                DecideNextAction();
                return;
            }

            ReturnToPassive();
        }

        #endregion

        #region IMeleeAttacker

        public float MeleeAttackSpeed => melee.MeleeAttackSpeed;
        public float MeleeDamage => melee.MeleeDamage;
        public Vector2 MeleeKnockback => melee.MeleeKnockback;
        public Hitbox MeleeHitbox => melee.MeleeHitbox;
        public GameObject MeleeVFXPrefab => melee.MeleeVFXPrefab;

        public void OnMeleeWindUp()
        {
            if (spineController != null)
                spineController.PlayWindUpAnimation();
        }

        public void OnMeleeAttack()
        {
            if (spineController != null)
                spineController.PlayAttackAnimation();
        }

        public void OnMeleeComplete()
        {
            if (!IsAlive) return;
            DecideNextAction();
        }

        #endregion

        #region IDodger

        public float DodgeDistance => dodge.DodgeDistance;
        public float DodgeDuration => dodge.DodgeDuration;
        public float DodgeHeight => dodge.DodgeHeight;
        public bool DodgeHasIFrames => dodge.DodgeHasIFrames;
        public GameObject DodgeVFXPrefab => dodge.DodgeVFXPrefab;
        public LayerMask DodgeWallLayer => dodge.DodgeWallLayer;
        public float DodgeWallCheckBuffer => dodge.DodgeWallCheckBuffer;
        public float DodgeForwardChance => dodge.DodgeForwardChance;

        public void OnDodgeComplete()
        {
            if (!IsAlive) return;

            if (dodgeWasReactive && HasTarget
                && DistanceToTarget <= maxCounterDashRange
                && Random.value <= dashChance)
            {
                dodgeWasReactive = false;
                stateMachine.ChangeState<ChargeState>();
                return;
            }

            dodgeWasReactive = false;
            DecideNextAction();
        }

        #endregion

        #region ICharger

        public float ChargeTime => charge.ChargeTime;
        public GameObject ChargeVFXPrefab => charge.ChargeVFXPrefab;

        public void OnChargeComplete()
        {
            if (!IsAlive) return;
            dashHitConnected = false;
            stateMachine.ChangeState<DashState>();
        }

        #endregion

        #region IDasher

        public float DashSpeed => dash.DashSpeed;
        public float DashDamage => dash.DashDamage;
        public Vector2 DashKnockback => dash.DashKnockback;
        public Hitbox DashHitbox => dash.DashHitbox;
        public GameObject DashVFXPrefab => dash.DashVFXPrefab;
        public float DashStopDistance => dash.DashStopDistance;
        public bool DashCanBeInterrupted => dash.DashCanBeInterrupted;
        public float DashAttackStartNormalized => dash.DashAttackStartNormalized;
        public float DashAttackActiveDuration => dash.DashAttackActiveDuration;
        public float DashWhiffResolveDelay => dash.DashWhiffResolveDelay;

        public void OnDashComplete()
        {
            if (!IsAlive) return;

            if (HasTarget && !dashHitConnected)
            {
                ForcedStunDuration = whiffStunDuration;
                stateMachine.ChangeState<StunnedState>();
                return;
            }

            if (!HasTarget || !IsTargetAlive())
            {
                ReturnToPassive();
                return;
            }

            DecideNextAction();
        }

        #endregion

        #region IStunnable

        public float StaggerDuration => stun.StaggerDuration;
        public float ForcedStunDuration { get => stun.ForcedStunDuration; set => stun.ForcedStunDuration = value; }
        public GameObject StunVFXObject => stun.StunVFXObject;

        public override void OnStunComplete()
        {
            if (!IsAlive) return;
            DecideNextAction();
        }

        #endregion

        #region Target Management

        public override void ClearTarget()
        {
            if (isInCombat) return;
            base.ClearTarget();
        }

        private void LoseTarget()
        {
            base.ClearTarget();
        }

        #endregion

        #region Lifecycle

        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.Hyena;
            patrol.Initialize(transform);

            if (melee.MeleeHitbox != null)
            {
                melee.MeleeHitbox.OnHit += OnMeleeHitboxHit;
                melee.MeleeHitbox.Deactivate();
            }
            if (dash.DashHitbox != null)
            {
                dash.DashHitbox.OnHit += OnDashHitboxHit;
                dash.DashHitbox.Deactivate();
            }
        }

        public override void OnParryStunned(float duration)
        {
            base.OnParryStunned(duration);
        }

        protected override void Update()
        {
            base.Update();

            if (isInCombat)
            {
                UpdateCombatTracking();
                CheckForDodgeOpportunity();
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (melee.MeleeHitbox != null)
                melee.MeleeHitbox.OnHit -= OnMeleeHitboxHit;
            if (dash.DashHitbox != null)
                dash.DashHitbox.OnHit -= OnDashHitboxHit;
        }

        protected override void InitializeStateMachine()
        {
            stateMachine.RegisterStates(
                new PatrolState(this),
                new IdleState(this),
                new ChaseState(this),
                new MeleeAttackState(this),
                new DodgeState(this),
                new ChargeState(this),
                new DashState(this),
                new StunnedState(this),
                new ParriedState(this),
                new DeadState(this)
            );

            if (HasPatrol)
                stateMachine.SetInitialState<PatrolState>();
            else
                stateMachine.SetInitialState<IdleState>();
        }

        #endregion

        #region Combat Tracking

        private void UpdateCombatTracking()
        {
            if (stateMachine.CurrentState is ParriedState
                || stateMachine.CurrentState is StunnedState)
                return;

            if (!HasTarget || !IsTargetAlive())
            {
                ReturnToPassive();
                return;
            }

            UpdateLastKnownPosition(Target.position);

            float dist = Movement.GetAbsAxisDistance(transform.position, Target.position);
            if (dist > pursuitRadius)
            {
                LoseTarget();

                if (chase.HasLastKnownPosition)
                {
                    if (!(stateMachine.CurrentState is ChaseState))
                        stateMachine.ChangeState<ChaseState>();
                }
                else
                {
                    ReturnToPassive();
                }
            }
        }

        #endregion

        #region Reactive Dodge

        private void CheckForDodgeOpportunity()
        {
            if (!IsAlive || !HasTarget)
            {
                wasPlayerAttacking = false;
                return;
            }

            if (stateMachine.CurrentState is DodgeState
                || stateMachine.CurrentState is ParriedState
                || stateMachine.CurrentState is StunnedState
                || stateMachine.CurrentState is ChargeState
                || stateMachine.CurrentState is DashState)
            {
                wasPlayerAttacking = IsPlayerAttacking();
                return;
            }

            if (Time.time - lastDodgeTime < dodgeCooldown || DistanceToTarget > dodgeCheckRange)
            {
                wasPlayerAttacking = IsPlayerAttacking();
                return;
            }

            bool attacking = IsPlayerAttacking();

            if (attacking && !wasPlayerAttacking && IsPlayerFacingMe())
            {
                if (Random.value <= dodgeChance)
                {
                    lastDodgeTime = Time.time;
                    dodgeWasReactive = true;
                    stateMachine.ChangeState<DodgeState>();
                }
            }

            wasPlayerAttacking = attacking;
        }

        private bool IsPlayerAttacking()
        {
            if (TargetCharacter == null) return false;
            var ps = TargetCharacter.GetComponentInParent<PlayerState>();
            if (ps != null) return ps.IsAttacking;
            var cs = TargetCharacter.GetComponent<CharacterState>();
            return cs != null && cs.IsAttacking;
        }

        private bool IsPlayerFacingMe()
        {
            if (Target == null) return false;

            Vector3 playerFacing = Target.right * Mathf.Sign(Target.localScale.x);
            Vector3 playerToEnemy = (transform.position - Target.position).normalized;
            return Vector3.Dot(playerFacing, playerToEnemy) > 0f;
        }

        #endregion

        #region Detection Events

        public override void OnPlayerSpotted()
        {
            if (!IsAlive) return;

            if (!isInCombat)
            {
                EnterCombat();
                detectionZone?.SetRadius(pursuitRadius);
            }

            if (IsInActionState()) return;
            if (stateMachine.CurrentState is ChaseState) return;

            stateMachine.ChangeState<ChaseState>();
        }

        public override void OnPlayerLost()
        {
            if (!IsAlive) return;
            if (isInCombat) return;
            ReturnToPassive();
        }

        public override void OnPlayerInAttackRange()
        {
            if (!IsAlive) return;
            if (!IsTargetAlive()) return;
            if (stateMachine.CurrentState is MeleeAttackState) return;

            stateMachine.ChangeState<MeleeAttackState>();
        }

        #endregion

        #region Decision Logic

        private void DecideNextAction()
        {
            if (!HasTarget || !IsTargetAlive())
            {
                if (chase.HasLastKnownPosition)
                    stateMachine.ChangeState<ChaseState>();
                else
                    ReturnToPassive();
                return;
            }

            if (IsTargetInAttackRange)
                stateMachine.ChangeState<MeleeAttackState>();
            else
                stateMachine.ChangeState<ChaseState>();
        }

        #endregion

        #region Helpers

        private bool IsInActionState()
        {
            var current = stateMachine.CurrentState;
            return current is MeleeAttackState
                || current is DodgeState
                || current is ChargeState
                || current is DashState
                || current is StunnedState;
        }

        private void ReturnToPassive()
        {
            ExitCombat();
            chase.ClearLastKnownPosition();
            LoseTarget();
            detectionZone?.ResetRadius();

            if (HasPatrol)
                stateMachine.ChangeState<PatrolState>();
            else
                stateMachine.ChangeState<IdleState>();
        }

        private bool IsTargetAlive()
        {
            return DamageReceiverUtility.IsAlive(Target);
        }

        #endregion

        #region Hitbox Handlers

        private void OnMeleeHitboxHit(Collider other, Hitbox hitbox)
        {
            if (!DamageReceiverUtility.IsAlive(other)) return;

            DamageReceiverUtility.Receive(other, new DamageRequest(
                melee.MeleeDamage,
                gameObject,
                DamageType.Physical,
                melee.MeleeKnockback));
        }

        private void OnDashHitboxHit(Collider other, Hitbox hitbox)
        {
            hitbox?.Deactivate();
            if (!DamageReceiverUtility.IsAlive(other)) return;

            DamageResult result = DamageReceiverUtility.Receive(other, new DamageRequest(
                dash.DashDamage,
                gameObject,
                DamageType.Physical,
                dash.DashKnockback));
            if (result.WasApplied)
                dashHitConnected = true;
        }

        #endregion

        #region Debug

#if UNITY_EDITOR
        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();
            patrol.DrawGizmos(transform);

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, pursuitRadius);

            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, dodgeCheckRange);
        }
#endif

        #endregion
    }
}
