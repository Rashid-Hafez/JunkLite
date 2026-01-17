using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Hyena enemy - aggressive predator with dodge and counter-attack.
    /// </summary>
    public class HyenaEnemy : EnemyCharacter, IPatroller, IMeleeAttacker, IChaser, IDodger, ICharger, IDasher, IRecoverer
    {
        [Header("Patrol")]
        [SerializeField] private float patrolDistance = 5f;
        [SerializeField] private float patrolSpeed = 3f;
        [SerializeField] private float wallCheckDistance = 0.5f;
        [SerializeField] private LayerMask wallLayer;

        [Header("Melee Attack")]
        [SerializeField] private float meleeAttackDuration = 0.4f;
        [SerializeField] private float attackCooldown = 0.3f;
        [SerializeField] private float meleeDamage = 8f;
        [SerializeField] private Vector2 meleeKnockback = new Vector2(8f, 3f);
        [SerializeField] private Hitbox meleeHitbox;
        [SerializeField] private GameObject meleeVFXPrefab;

        [Header("Dash Attack")]
        [SerializeField] private float chargeTime = 0.2f;
        [SerializeField] private GameObject chargeVFXPrefab;
        [SerializeField] private float dashSpeed = 18f;
        [SerializeField] private float dashDamage = 12f;
        [SerializeField] private Vector2 dashKnockback = new Vector2(12f, 4f);
        [SerializeField] private Hitbox dashHitbox;
        [SerializeField] private GameObject dashVFXPrefab;
        [SerializeField] private float dashStopDistance = 0.5f;
        [SerializeField][Range(0f, 1f)] private float maxDashChance = 0.5f;

        [Header("Chase")]
        [SerializeField] private float chaseSpeed = 8f;
        [SerializeField] private float pursuitRadius = 15f;

        [Header("Dodge")]
        [SerializeField] private float dodgeDistance = 3f;
        [SerializeField] private float dodgeSpeed = 10f;
        [SerializeField] private float dodgeHeight = 0.5f;
        [SerializeField] private bool dodgeHasIFrames = true;
        [SerializeField] private GameObject dodgeVFXPrefab;
        [SerializeField][Range(0f, 1f)] private float dodgeChance = 0.3f;
        [SerializeField] private float dodgeCheckRange = 4f;
        [SerializeField] private float dodgeCooldown = 1f;

        [Header("Post-Dodge")]
        [SerializeField] private float postDodgeRecoveryTime = 0.3f;
        [SerializeField][Range(0f, 1f)] private float counterAttackChance = 0.4f;
        [SerializeField] private GameObject recoveryVFXPrefab;

        // State
        private Vector3 spawnPosition;
        private int patrolDirection = 1;
        private Vector3 lastKnownTargetPosition;
        private bool hasLastKnownPosition;
        private float lastDodgeTime = -999f;
        private bool wasPlayerAttacking;
        private float dodgeInvulnerabilityEndTime;
        private bool shouldCounterAttack;
        private float currentDashChance;

        public bool HasPatrol => patrolDistance > 0f;

        #region IPatroller
        public float PatrolDistance => patrolDistance;
        public float PatrolSpeed => patrolSpeed;
        public Vector3 SpawnPosition => spawnPosition;
        public int PatrolDirection { get => patrolDirection; set => patrolDirection = value; }

        public bool IsWallAhead()
        {
            Vector3 dir = patrolDirection > 0 ? Vector3.right : Vector3.left;
            return Physics.Raycast(transform.position, dir, wallCheckDistance, wallLayer);
        }

        public bool IsAtPatrolBoundary()
        {
            float dist = transform.position.x - spawnPosition.x;
            return (patrolDirection > 0 && dist >= patrolDistance) ||
                   (patrolDirection < 0 && dist <= -patrolDistance);
        }

        public void ReverseDirection() => patrolDirection *= -1;
        #endregion

        #region IMeleeAttacker
        public float MeleeAttackDuration => meleeAttackDuration;
        public float AttackCooldown => attackCooldown;
        public float MeleeDamage => meleeDamage;
        public Vector2 MeleeKnockback => meleeKnockback;
        public Hitbox MeleeHitbox => meleeHitbox;
        public GameObject MeleeVFXPrefab => meleeVFXPrefab;

        public void OnMeleeComplete()
        {
            if (!IsAlive) return;
            if (HasTarget && IsTargetInAttackRange) return; // Stay in melee
            stateMachine.ChangeState<ChaseState>();
        }
        #endregion

        #region IChaser
        public Vector3 LastKnownTargetPosition => lastKnownTargetPosition;
        public bool HasLastKnownPosition => hasLastKnownPosition;
        public float ChaseSpeed => chaseSpeed;

        public void OnReachedTarget()
        {
            if (!IsAlive) return;
            hasLastKnownPosition = false;
            ExitCombat();
            if (HasPatrol)
                stateMachine.ChangeState<PatrolState>();
            else
                stateMachine.ChangeState<IdleState>();
        }

        public void UpdateLastKnownPosition(Vector3 pos)
        {
            lastKnownTargetPosition = pos;
            hasLastKnownPosition = true;
        }
        #endregion

        #region IDodger
        public float DodgeDistance => dodgeDistance;
        public float DodgeSpeed => dodgeSpeed;
        public float DodgeDuration => dodgeSpeed > 0f ? dodgeDistance / dodgeSpeed : 0.3f;
        public float DodgeHeight => dodgeHeight;
        public bool DodgeHasIFrames => dodgeHasIFrames;
        public GameObject DodgeVFXPrefab => dodgeVFXPrefab;

        public void OnDodgeComplete()
        {
            if (!IsAlive) return;
            shouldCounterAttack = Random.value <= counterAttackChance;
            stateMachine.ChangeState<RecoverState>();
        }
        #endregion

        #region ICharger
        public float ChargeTime => chargeTime;
        public GameObject ChargeVFXPrefab => chargeVFXPrefab;

        public void OnChargeComplete()
        {
            if (!IsAlive) return;
            stateMachine.ChangeState<DashState>();
        }
        #endregion

        #region IDasher
        public float DashSpeed => dashSpeed;
        public float DashDamage => dashDamage;
        public Vector2 DashKnockback => dashKnockback;
        public Hitbox DashHitbox => dashHitbox;
        public GameObject DashVFXPrefab => dashVFXPrefab;
        public float DashStopDistance => dashStopDistance;

        public void OnDashComplete()
        {
            if (!IsAlive) return;
            shouldCounterAttack = false;
            stateMachine.ChangeState<RecoverState>();
        }
        #endregion

        #region IRecoverer
        public float RecoveryTime => postDodgeRecoveryTime;
        public GameObject RecoveryVFXPrefab => recoveryVFXPrefab;

        public void OnRecoveryComplete()
        {
            if (!IsAlive) return;

            if (shouldCounterAttack && HasTarget)
            {
                shouldCounterAttack = false;
                stateMachine.ChangeState<ChargeState>();
                return;
            }

            shouldCounterAttack = false;
            DecideNextCombatAction();
        }
        #endregion

        #region Combat Logic
        private void DecideNextCombatAction()
        {
            if (!HasTarget)
            {
                if (hasLastKnownPosition)
                    stateMachine.ChangeState<ChaseState>();
                else
                {
                    ExitCombat();
                    if (HasPatrol)
                        stateMachine.ChangeState<PatrolState>();
                    else
                        stateMachine.ChangeState<IdleState>();
                }
                return;
            }

            if (IsTargetInAttackRange)
                stateMachine.ChangeState<MeleeAttackState>();
            else if (ShouldDashAttack())
                stateMachine.ChangeState<ChargeState>();
            else
                stateMachine.ChangeState<ChaseState>();
        }

        private bool ShouldDashAttack() => currentDashChance > 0f && Random.value <= currentDashChance;

        private void UpdateDashChance()
        {
            if (attributes?.Health == null) return;
            float max = attributes.Health.Max;
            if (max <= 0f) return;
            currentDashChance = (1f - attributes.Health.Current / max) * maxDashChance;
        }
        #endregion

        #region Damage
        public override bool TakeDamage(DamageInfo info)
        {
            if (dodgeHasIFrames && Time.time < dodgeInvulnerabilityEndTime)
                return false;

            if (state != null && !state.CanTakeDamage)
                return false;

            bool dealt = base.TakeDamage(info);
            if (dealt) UpdateDashChance();
            return dealt;
        }
        #endregion

        #region Lifecycle
        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.Hyena;
            spawnPosition = transform.position;

            if (meleeHitbox != null)
            {
                meleeHitbox.OnHit += OnMeleeHitboxHit;
                meleeHitbox.Deactivate();
            }

            if (dashHitbox != null)
            {
                dashHitbox.OnHit += OnDashHitboxHit;
                dashHitbox.Deactivate();
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
                new RecoverState(this),
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
            if (meleeHitbox != null) meleeHitbox.OnHit -= OnMeleeHitboxHit;
            if (dashHitbox != null) dashHitbox.OnHit -= OnDashHitboxHit;
        }
        #endregion

        #region Dodge Check
        private void CheckForDodgeOpportunity()
        {
            if (!IsAlive || !HasTarget)
            {
                wasPlayerAttacking = false;
                return;
            }

            if (stateMachine.CurrentState is DodgeState)
            {
                wasPlayerAttacking = IsPlayerAttacking();
                return;
            }

            if (DistanceToTarget > dodgeCheckRange || Time.time - lastDodgeTime < dodgeCooldown)
            {
                wasPlayerAttacking = IsPlayerAttacking();
                return;
            }

            bool attacking = IsPlayerAttacking();

            if (attacking && !wasPlayerAttacking && IsPlayerFacingMe())
            {
                if (Random.value <= dodgeChance)
                {
                    dodgeInvulnerabilityEndTime = Time.time + DodgeDuration;
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
            float dir = transform.position.x - Target.position.x;
            return (facing > 0 && dir > 0) || (facing < 0 && dir < 0);
        }
        #endregion

        #region Brain
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
            hasLastKnownPosition = false;
            ExitCombat();
            detectionZone?.ResetRadius();
            if (HasPatrol)
                stateMachine.ChangeState<PatrolState>();
            else
                stateMachine.ChangeState<IdleState>();
        }

        public override void OnPlayerInAttackRange()
        {
            if (!IsAlive) return;
            if (ShouldDashAttack())
                stateMachine.ChangeState<ChargeState>();
            else
                stateMachine.ChangeState<MeleeAttackState>();
        }
        #endregion

        #region Hitbox Handlers
        private void OnMeleeHitboxHit(Collider other, Hitbox hitbox)
        {
            var dmg = other.GetComponent<IDamageable>() ?? other.GetComponentInParent<IDamageable>();
            if (dmg == null || !dmg.IsAlive) return;
            dmg.TakeDamage(new DamageInfo(meleeDamage, gameObject, DamageType.Physical, meleeKnockback));
        }

        private void OnDashHitboxHit(Collider other, Hitbox hitbox)
        {
            hitbox?.Deactivate();
            var dmg = other.GetComponent<IDamageable>() ?? other.GetComponentInParent<IDamageable>();
            if (dmg == null || !dmg.IsAlive) return;

            Vector3 dir = (other.transform.position - transform.position).normalized;
            Vector2 kb = new Vector2(dir.x * dashKnockback.x, dashKnockback.y);
            dmg.TakeDamage(new DamageInfo(dashDamage, gameObject, DamageType.Physical, kb));
        }
        #endregion

#if UNITY_EDITOR
        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();

            if (patrolDistance > 0f)
            {
                Vector3 origin = Application.isPlaying ? spawnPosition : transform.position;
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(origin + Vector3.left * patrolDistance, origin + Vector3.right * patrolDistance);
            }

            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, dodgeCheckRange);
        }
#endif
    }
}