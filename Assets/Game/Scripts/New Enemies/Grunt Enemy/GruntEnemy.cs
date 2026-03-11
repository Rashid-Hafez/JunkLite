using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Grunt enemy — simple melee fodder designed to be combo-friendly.
    /// No dodge, no dash, no counter-attacks. High hitstun for juggling.
    /// Threatening in groups, punching bag solo.
    /// </summary>
    public class GruntEnemy : EnemyCharacter, IChaser, IMeleeAttacker
    {
        [Header("Grunt - Chase")]
        [SerializeField] private ChaseBehavior chase = new ChaseBehavior();
        [SerializeField] private float pursuitRadius = 12f;

        [Header("Grunt - Melee Attack")]
        [SerializeField] private MeleeAttackBehavior melee = new MeleeAttackBehavior();

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

            ReturnToIdle();
        }

        #endregion

        #region IMeleeAttacker

        public float MeleeAttackSpeed => melee.MeleeAttackSpeed;
        public float MeleeDamage => melee.MeleeDamage;
        public Vector2 MeleeKnockback => melee.MeleeKnockback;
        public Hitbox MeleeHitbox => melee.MeleeHitbox;
        public GameObject MeleeVFXPrefab => melee.MeleeVFXPrefab;

        public void OnMeleeAttack() { }

        public void OnMeleeComplete()
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
            enemyType = EnemyType.Grunt;

            if (melee.MeleeHitbox != null)
            {
                melee.MeleeHitbox.OnHit += OnMeleeHitboxHit;
                melee.MeleeHitbox.Deactivate();
            }
        }

        protected override void InitializeStateMachine()
        {
            stateMachine.RegisterStates(
                new IdleState(this),
                new ChaseState(this),
                new MeleeAttackState(this),
                new HurtState(this),
                new StunnedState(this),
                new ParriedState(this),
                new DeadState(this)
            );

            stateMachine.SetInitialState<IdleState>();
        }

        protected override void Update()
        {
            base.Update();

            if (isInCombat)
                UpdateCombatTracking();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (melee.MeleeHitbox != null)
                melee.MeleeHitbox.OnHit -= OnMeleeHitboxHit;
        }

        #endregion

        #region Combat Tracking

        private void UpdateCombatTracking()
        {
            if (stateMachine.CurrentState is ParriedState
                || stateMachine.CurrentState is StunnedState
                || stateMachine.CurrentState is HurtState)
                return;

            if (!HasTarget || !IsTargetAlive())
            {
                ReturnToIdle();
                return;
            }

            UpdateLastKnownPosition(Target.position);

            float dist = Movement.GetAbsAxisDistance(transform.position, Target.position);
            if (dist > pursuitRadius)
            {
                LoseTarget();

                if (chase.HasLastKnownPosition)
                {
                    if (stateMachine.CurrentState is not ChaseState)
                        stateMachine.ChangeState<ChaseState>();
                }
                else
                {
                    ReturnToIdle();
                }
            }
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
            ReturnToIdle();
        }

        public override void OnPlayerInAttackRange()
        {
            if (!IsAlive) return;
            if (!IsTargetAlive()) return;
            if (stateMachine.CurrentState is MeleeAttackState) return;

            stateMachine.ChangeState<MeleeAttackState>();
        }

        #endregion

        #region Recovery

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

        #endregion

        #region Decision Logic

        private void DecideNextAction()
        {
            if (!IsAlive)
            {
                stateMachine.ChangeState<DeadState>();
                return;
            }

            if (!HasTarget || !IsTargetAlive())
            {
                if (chase.HasLastKnownPosition)
                    stateMachine.ChangeState<ChaseState>();
                else
                    ReturnToIdle();
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
                || current is HurtState
                || current is StunnedState
                || current is ParriedState;
        }

        private void ReturnToIdle()
        {
            ExitCombat();
            chase.ClearLastKnownPosition();
            LoseTarget();
            detectionZone?.ResetRadius();
            stateMachine.ChangeState<IdleState>();
        }

        private bool IsTargetAlive()
        {
            if (Target == null) return false;
            var damageable = Target.GetComponent<IDamageable>()
                          ?? Target.GetComponentInParent<IDamageable>();
            return damageable != null && damageable.IsAlive;
        }

        #endregion

        #region Hitbox Handlers

        private void OnMeleeHitboxHit(Collider other, Hitbox hitbox)
        {
            var dmg = other.GetComponent<IDamageable>() ?? other.GetComponentInParent<IDamageable>();
            if (dmg == null || !dmg.IsAlive) return;
            dmg.TakeDamage(new DamageInfo(melee.MeleeDamage, gameObject, DamageType.Physical, melee.MeleeKnockback));
        }

        #endregion

        #region Debug

#if UNITY_EDITOR
        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.2f);
            Gizmos.DrawWireSphere(transform.position, pursuitRadius);
        }
#endif

        #endregion
    }
}