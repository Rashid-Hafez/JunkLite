using UnityEngine;

namespace junklite
{

    public class HyenaEnemy : EnemyCharacter, IPatroller, IChaser, IMeleeAttacker, IDodger, ICharger, IDasher
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

        [Header("Hyena - Dodge")]
        [SerializeField] private DodgeBehavior dodge = new DodgeBehavior();
        [SerializeField][Range(0f, 1f)] private float dodgeChance = 0.3f;
        [SerializeField] private float dodgeCheckRange = 4f;
        [SerializeField] private float dodgeCooldown = 1f;

        [Header("Hyena - Dash Counter-Attack")]
        [SerializeField] private ChargeBehavior charge = new ChargeBehavior();
        [SerializeField] private DashBehavior dash = new DashBehavior();
        [SerializeField][Range(0f, 1f)] private float dashChance = 0.4f;
        [Tooltip("After a missed dash, if hit within this window, enemy enters StunnedState.")]
        [SerializeField] private float vulnerableStunWindow = 1.5f;

        // Dodge state
        private float lastDodgeTime = -999f;
        private bool wasPlayerAttacking;

        // Dash state
        private bool dashHitConnected;
        private float vulnerableToStunUntil = -999f;

        public bool HasPatrol => patrol.HasPatrol;

        // ============================================================
        // IPatroller
        // ============================================================
        public float PatrolDistance => patrol.PatrolDistance;
        public float PatrolSpeed => patrol.PatrolSpeed;
        public Vector3 SpawnPosition => patrol.SpawnPosition;
        public int PatrolDirection { get => patrol.PatrolDirection; set => patrol.PatrolDirection = value; }
        public bool IsWallAhead() => patrol.IsWallAhead();
        public bool IsAtPatrolBoundary() => patrol.IsAtPatrolBoundary();
        public void ReverseDirection() => patrol.ReverseDirection();

        // ============================================================
        // IChaser
        // ============================================================
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

        // ============================================================
        // IMeleeAttacker
        // ============================================================
        public float MeleeAttackSpeed => melee.MeleeAttackSpeed;
        public float MeleeDamage => melee.MeleeDamage;
        public Vector2 MeleeKnockback => melee.MeleeKnockback;
        public Hitbox MeleeHitbox => melee.MeleeHitbox;
        public GameObject MeleeVFXPrefab => melee.MeleeVFXPrefab;

        public void OnMeleeAttack() { }

        public void OnMeleeComplete()
        {
            if (!IsAlive) return;

            if (HasTarget && IsTargetAlive() && IsTargetInAttackRange) return;

            if (!HasTarget || !IsTargetAlive())
            {
                ReturnToPassive();
                return;
            }

            stateMachine.ChangeState<ChaseState>();
        }

        // ============================================================
        // IDodger
        // ============================================================
        public float DodgeDistance => dodge.DodgeDistance;
        public float DodgeDuration => dodge.DodgeDuration;
        public float DodgeHeight => dodge.DodgeHeight;
        public bool DodgeHasIFrames => dodge.DodgeHasIFrames;
        public GameObject DodgeVFXPrefab => dodge.DodgeVFXPrefab;

        public void OnDodgeComplete()
        {
            if (!IsAlive) return;

            // Chance to counter-attack with dash
            if (HasTarget && Random.value <= dashChance)
            {
                stateMachine.ChangeState<ChargeState>();
                return;
            }

            DecideNextAction();
        }

        // ============================================================
        // ICharger
        // ============================================================
        public float ChargeTime => charge.ChargeTime;
        public GameObject ChargeVFXPrefab => charge.ChargeVFXPrefab;

        public void OnChargeComplete()
        {
            if (!IsAlive) return;
            dashHitConnected = false;
            stateMachine.ChangeState<DashState>();
        }

        // ============================================================
        // IDasher
        // ============================================================
        public float DashSpeed => dash.DashSpeed;
        public float DashDamage => dash.DashDamage;
        public Vector2 DashKnockback => dash.DashKnockback;
        public Hitbox DashHitbox => dash.DashHitbox;
        public GameObject DashVFXPrefab => dash.DashVFXPrefab;
        public float DashStopDistance => dash.DashStopDistance;

        public void OnDashComplete()
        {
            if (!IsAlive) return;

            // Missed dash? Open vulnerability window
            if (HasTarget && !dashHitConnected)
                vulnerableToStunUntil = Time.time + vulnerableStunWindow;

            if (!HasTarget || !IsTargetAlive())
            {
                ReturnToPassive();
                return;
            }

            DecideNextAction();
        }

        // ============================================================
        // TARGET MANAGEMENT
        // ============================================================
        public override void ClearTarget()
        {
            if (isInCombat) return;
            base.ClearTarget();
        }

        // ============================================================
        // Lifecycle
        // ============================================================
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
            // first, let base class handle the stun state transition
            base.OnParryStunned(duration);

            // hyena-specific: play a looping stun animation for the requested duration
            if (spineController != null)
                spineController.PlayStunLoop(duration);
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
                new HurtState(this),
                new StunnedState(this),
                new DeadState(this)
            );

            if (HasPatrol)
                stateMachine.SetInitialState<PatrolState>();
            else
                stateMachine.SetInitialState<IdleState>();
        }

        // ============================================================
        // COMBAT TRACKING
        // ============================================================
        private void UpdateCombatTracking()
        {
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

        private void LoseTarget()
        {
            base.ClearTarget();
        }

        // ============================================================
        // REACTIVE DODGE
        // ============================================================
        private void CheckForDodgeOpportunity()
        {
            if (!IsAlive || !HasTarget)
            {
                wasPlayerAttacking = false;
                return;
            }

            if (stateMachine.CurrentState is DodgeState || stateMachine.CurrentState is HurtState)
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

            // Player's world-space facing direction (works regardless of rotation)
            Vector3 playerFacing = Target.right * Mathf.Sign(Target.localScale.x);

            // Is the player facing toward me?
            Vector3 playerToEnemy = (transform.position - Target.position).normalized;
            return Vector3.Dot(playerFacing, playerToEnemy) > 0f;
        }

        // ============================================================
        // DAMAGE — vulnerability window after missed dash
        // ============================================================
        public override bool TakeDamage(DamageInfo info)
        {
            bool dealt = base.TakeDamage(info);

            if (dealt && Time.time <= vulnerableToStunUntil)
            {
                vulnerableToStunUntil = -999f;
                stateMachine.ChangeState<StunnedState>();
            }

            return dealt;
        }

        // ============================================================
        // Detection Events
        // ============================================================
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

        // ============================================================
        // Recovery
        // ============================================================
        public override void OnHurtComplete()
        {
            if (!IsAlive) return;
            DecideNextAction();
        }

        public override void OnStunComplete()
        {
            if (!IsAlive) return;
            DecideNextAction();
        }

        // ============================================================
        // Decision Logic
        // ============================================================
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

        // ============================================================
        // Helpers
        // ============================================================
        private bool IsInActionState()
        {
            var current = stateMachine.CurrentState;
            return current is MeleeAttackState
                || current is DodgeState
                || current is ChargeState
                || current is DashState
                || current is HurtState
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
            if (Target == null) return false;
            var damageable = Target.GetComponent<IDamageable>()
                          ?? Target.GetComponentInParent<IDamageable>();
            return damageable != null && damageable.IsAlive;
        }

        // ============================================================
        // Hitbox Handlers
        // ============================================================
        private void OnMeleeHitboxHit(Collider other, Hitbox hitbox)
        {
            var dmg = other.GetComponent<IDamageable>() ?? other.GetComponentInParent<IDamageable>();
            if (dmg == null || !dmg.IsAlive) return;
            dmg.TakeDamage(new DamageInfo(melee.MeleeDamage, gameObject, DamageType.Physical, melee.MeleeKnockback));
        }

        private void OnDashHitboxHit(Collider other, Hitbox hitbox)
        {
            dashHitConnected = true;
            hitbox?.Deactivate();
            var dmg = other.GetComponent<IDamageable>() ?? other.GetComponentInParent<IDamageable>();
            if (dmg == null || !dmg.IsAlive) return;
            dmg.TakeDamage(new DamageInfo(dash.DashDamage, gameObject, DamageType.Physical, dash.DashKnockback));
        }

        // ============================================================
        // Debug
        // ============================================================
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
    }
}