using UnityEngine;
namespace junklite
{
    /// <summary>
    /// Hyena enemy - aggressive predator with dodge and counter-attack.
    /// 
    /// CAPABILITIES: IPatroller, IMeleeAttacker, IChaser, IDodger, ICharger, IDasher
    /// 
    /// BEHAVIOR:
    /// - Patrol until player spotted
    /// - Chase player, attack when in range
    /// - Can dodge player attacks (reactive)
    /// - After dodge, chance to counter-attack with dash
    /// - Dash chance increases as health decreases
    /// </summary>
    public class HyenaEnemy : EnemyCharacter, IPatroller, IMeleeAttacker, IChaser, IDodger, ICharger, IDasher
    {
        [Header("Animation")]
        [SerializeField] private EnemySpineAnimationController spineController;

        [Header("Hyena - Patrol")]
        [SerializeField] private PatrolBehavior patrol = new PatrolBehavior();
        [Header("Hyena - Melee Attack")]
        [SerializeField] private MeleeAttackBehavior melee = new MeleeAttackBehavior();
        [Header("Hyena - Chase")]
        [SerializeField] private ChaseBehavior chase = new ChaseBehavior();
        [SerializeField] private float pursuitRadius = 15f;
        [Header("Hyena - Dodge")]
        [SerializeField] private DodgeBehavior dodge = new DodgeBehavior();
        [SerializeField][Range(0f, 1f)] private float dodgeChance = 0.3f;
        [SerializeField] private float dodgeCheckRange = 4f;
        [SerializeField] private float dodgeCooldown = 1f;
        [SerializeField][Range(0f, 1f)] private float counterAttackChance = 0.4f;
        [Header("Hyena - Dash Attack")]
        [SerializeField] private ChargeBehavior charge = new ChargeBehavior();
        [SerializeField] private DashBehavior dash = new DashBehavior();
        [SerializeField][Range(0f, 1f)] private float maxDashChance = 0.5f;
        [Tooltip("After a missed dash, if the player hits the hyena within this window, it gets stunned (Hurt trigger).")]
        [SerializeField] private float vulnerableStunWindow = 1.5f;
        // State
        private float lastDodgeTime = -999f;
        private bool dashHitConnected;
        private float vulnerableToStunUntil = -999f;
        private bool wasPlayerAttacking;
        private float dodgeInvulnerabilityEndTime;
        private float currentDashChance;
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
        // IMeleeAttacker
        // ============================================================
        public float MeleeAttackSpeed => melee.MeleeAttackSpeed;
        public float MeleeDamage => melee.MeleeDamage;
        public Vector2 MeleeKnockback => melee.MeleeKnockback;
        public Hitbox MeleeHitbox => melee.MeleeHitbox;
        public GameObject MeleeVFXPrefab => melee.MeleeVFXPrefab;

        public void OnMeleeAttack()
        {
            // Animation controller handles this via state change detection
            // You can add VFX spawn or sound here if needed
        }

        public void OnMeleeComplete()
        {
            if (!IsAlive) return;

            // Stay in melee only if target exists, is alive, and in range
            if (HasTarget && IsTargetAlive() && IsTargetInAttackRange) return;

            // Target dead or out of range
            if (!HasTarget || !IsTargetAlive())
            {
                ReturnToPassive();
                return;
            }

            stateMachine.ChangeState<ChaseState>();
        }

        // ============================================================
        // IChaser
        // ============================================================
        public Vector3 LastKnownTargetPosition => chase.LastKnownTargetPosition;
        public bool HasLastKnownPosition => chase.HasLastKnownPosition;
        public float ChaseSpeed => chase.ChaseSpeed;
        public float ChaseStopDistance => chase.ChaseStopDistance;
        public void OnReachedTarget()
        {
            if (!IsAlive) return;
            chase.ClearLastKnownPosition();
            ReturnToPassive();
        }
        public void UpdateLastKnownPosition(Vector3 pos) => chase.UpdateLastKnownPosition(pos);
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
            // Chance to counter-attack immediately after dodge
            if (HasTarget && Random.value <= counterAttackChance)
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
            dashHitConnected = false; // Reset so we can detect if this dash hits the player
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

            // If we had a target and didn't hit them, we're vulnerable to stun
            if (HasTarget && !dashHitConnected)
                vulnerableToStunUntil = Time.time + vulnerableStunWindow;

            // If target is dead after our dash hit, return to passive
            if (!HasTarget || !IsTargetAlive())
            {
                ReturnToPassive();
                return;
            }

            DecideNextAction();
        }
        // ============================================================
        // Decision Logic
        // ============================================================
        private void DecideNextAction()
        {
            // No target or target is dead
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
            else if (ShouldDashAttack())
                stateMachine.ChangeState<ChargeState>();
            else
                stateMachine.ChangeState<ChaseState>();
        }
        private void ReturnToPassive()
        {
            ExitCombat();
            detectionZone?.ResetRadius();
            if (HasPatrol)
                stateMachine.ChangeState<PatrolState>();
            else
                stateMachine.ChangeState<IdleState>();
        }
        private bool ShouldDashAttack() => currentDashChance > 0f && Random.value <= currentDashChance;
        private void UpdateDashChance()
        {
            if (attributes?.Health == null) return;
            float max = attributes.Health.Max;
            if (max <= 0f) return;
            currentDashChance = (1f - attributes.Health.Current / max) * maxDashChance;
        }
        // ============================================================
        // Damage
        // ============================================================
        public override bool TakeDamage(DamageInfo info)
        {
            if (dodge.DodgeHasIFrames && Time.time < dodgeInvulnerabilityEndTime)
                return false;
            if (state != null && !state.CanTakeDamage)
                return false;
            bool dealt = base.TakeDamage(info);
            if (dealt)
            {
                UpdateDashChance();
                // If we're in the vulnerable window (after a missed dash), enter StunnedState so Hurt trigger fires
                if (Time.time <= vulnerableToStunUntil)
                {
                    vulnerableToStunUntil = -1f;
                    stateMachine.ChangeState<StunnedState>();
                }
            }
            return dealt;
        }

        public override void OnStunComplete()
        {
            if (!IsAlive) return;
            DecideNextAction();
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
                new DeadState(this)
            );
            if (HasPatrol)
                stateMachine.SetInitialState<PatrolState>();
            else
                stateMachine.SetInitialState<IdleState>();
        }
        protected override void Update()
        {
            base.Update();
            if (HasTarget) UpdateLastKnownPosition(Target.position);
            CheckForDodgeOpportunity();
        }
        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (melee.MeleeHitbox != null) melee.MeleeHitbox.OnHit -= OnMeleeHitboxHit;
            if (dash.DashHitbox != null) dash.DashHitbox.OnHit -= OnDashHitboxHit;
        }
        // ============================================================
        // Reactive Dodge
        // ============================================================
        private void CheckForDodgeOpportunity()
        {
            if (!IsAlive || !HasTarget)
            {
                wasPlayerAttacking = false;
                return;
            }
            // Don't interrupt dodge
            if (stateMachine.CurrentState is DodgeState)
            {
                wasPlayerAttacking = IsPlayerAttacking();
                return;
            }
            // Check cooldown and range
            if (Time.time - lastDodgeTime < dodgeCooldown || DistanceToTarget > dodgeCheckRange)
            {
                wasPlayerAttacking = IsPlayerAttacking();
                return;
            }
            bool attacking = IsPlayerAttacking();
            // Trigger on attack START (rising edge) when player faces us
            if (attacking && !wasPlayerAttacking && IsPlayerFacingMe())
            {
                if (Random.value <= dodgeChance)
                {
                    dodgeInvulnerabilityEndTime = Time.time + dodge.DodgeDuration;
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
            float facing = Mathf.Sign(Target.localScale.x);
            float toMe = transform.position.x - Target.position.x;
            return (facing > 0 && toMe > 0) || (facing < 0 && toMe < 0);
        }
        // ============================================================
        // Brain - Detection Events
        // ============================================================
        public override void OnPlayerSpotted()
        {
            if (!IsAlive || isInCombat) return;
            EnterCombat();
            detectionZone?.SetRadius(pursuitRadius);
            stateMachine.ChangeState<ChaseState>();
        }
        public override void OnPlayerLost()
        {
            if (!IsAlive) return;
            chase.ClearLastKnownPosition();
            ReturnToPassive();
        }
        public override void OnPlayerInAttackRange()
        {
            if (!IsAlive) return;
            if (!IsTargetAlive()) return;

            if (ShouldDashAttack())
                stateMachine.ChangeState<ChargeState>();
            else
                stateMachine.ChangeState<MeleeAttackState>();
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
            dashHitConnected = true; // We hit someone (e.g. player) during this dash
            hitbox?.Deactivate();
            var dmg = other.GetComponent<IDamageable>() ?? other.GetComponentInParent<IDamageable>();
            if (dmg == null || !dmg.IsAlive) return;
            // Pass raw knockback - player's TakeDamage calculates direction from Source
            dmg.TakeDamage(new DamageInfo(dash.DashDamage, gameObject, DamageType.Physical, dash.DashKnockback));
        }
        private bool IsTargetAlive()
        {
            if (Target == null) return false;

            var damageable = Target.GetComponent<IDamageable>()
                          ?? Target.GetComponentInParent<IDamageable>();

            return damageable != null && damageable.IsAlive;
        }
        // ============================================================
        // Debug Gizmos
        // ============================================================
#if UNITY_EDITOR
        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();
            patrol.DrawGizmos(transform);
            // Dodge check range
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, dodgeCheckRange);
        }
#endif
    }


}