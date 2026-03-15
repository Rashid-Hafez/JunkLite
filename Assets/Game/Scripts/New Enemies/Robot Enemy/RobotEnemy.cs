using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Robot enemy - dashes at player when spotted.
    /// Has a chance to grab and throw the player on hit.
    /// </summary>
    public class RobotEnemy : EnemyCharacter, IPatroller, ICharger, IDasher, IGrabber, IRecoverer
    {
        [Header("Robot - Patrol")]
        [SerializeField] private PatrolBehavior patrol = new PatrolBehavior();

        [Header("Robot - Charge")]
        [SerializeField] private ChargeBehavior charge = new ChargeBehavior();

        [Header("Robot - Dash Attack")]
        [SerializeField] private DashBehavior dash = new DashBehavior();

        [Header("Robot - Grab Attack")]
        [SerializeField] private GrabBehavior grab = new GrabBehavior();

        [Header("Robot - Recovery")]
        [SerializeField] private RecoveryBehavior recovery = new RecoveryBehavior();

        public bool HasPatrol => patrol.HasPatrol;

        #region IPatroller

        public float PatrolDistance => patrol.PatrolDistance;
        public float PatrolSpeed => patrol.PatrolSpeed;
        public Vector3 SpawnPosition => patrol.SpawnPosition;
        public int PatrolDirection
        {
            get => patrol.PatrolDirection;
            set => patrol.PatrolDirection = value;
        }
        public bool IsWallAhead() => patrol.IsWallAhead();
        public bool IsAtPatrolBoundary() => patrol.IsAtPatrolBoundary();
        public void ReverseDirection() => patrol.ReverseDirection();

        #endregion

        #region ICharger

        public float ChargeTime => charge.ChargeTime;
        public GameObject ChargeVFXPrefab => charge.ChargeVFXPrefab;

        public void OnChargeComplete()
        {
            if (!IsAlive) return;
            stateMachine.ChangeState<DashState>();
        }

        #endregion

        #region IDasher

        public float DashSpeed => dash.DashSpeed;
        public float DashDamage => dash.DashDamage;
        public float DashStopDistance => dash.DashStopDistance;
        public Vector2 DashKnockback => dash.DashKnockback;
        public Hitbox DashHitbox => dash.DashHitbox;
        public GameObject DashVFXPrefab => dash.DashVFXPrefab;
        public bool DashCanBeInterrupted => dash.DashCanBeInterrupted;
        public float DashAttackStartNormalized => dash.DashAttackStartNormalized;
        public float DashAttackActiveDuration => dash.DashAttackActiveDuration;
        public float DashWhiffResolveDelay => dash.DashWhiffResolveDelay;

        public void OnDashComplete()
        {
            if (!IsAlive) return;
            stateMachine.ChangeState<RecoverState>();
        }

        #endregion

        #region IGrabber

        public bool CanGrab => grab.CanGrab;
        public float GrabChance => grab.GrabChance;
        public float GrabDuration => grab.GrabDuration;
        public Vector3 GrabOffset => grab.GrabOffset;
        public Vector2 ThrowForce => grab.ThrowForce;
        public float ThrowDamage => grab.ThrowDamage;
        public GameObject GrabVFXPrefab => grab.GrabVFXPrefab;

        public void OnGrabComplete()
        {
            if (!IsAlive) return;
            stateMachine.ChangeState<RecoverState>();
        }

        #endregion

        #region IRecoverer

        public float RecoveryTime => recovery.RecoveryTime;
        public GameObject RecoveryVFXPrefab => recovery.RecoveryVFXPrefab;

        public void OnRecoveryComplete()
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

        #endregion

        #region Lifecycle

        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.Robot;
            patrol.Initialize(transform);

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
                new ChargeState(this),
                new DashState(this),
                new GrabState(this),
                new RecoverState(this),
                new StunnedState(this),
                new ParriedState(this),
                new DeadState(this)
            );

            if (HasPatrol)
                stateMachine.SetInitialState<PatrolState>();
            else
                stateMachine.SetInitialState<IdleState>();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (dash.DashHitbox != null)
                dash.DashHitbox.OnHit -= OnDashHitboxHit;
        }

        #endregion

        #region Detection Events

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

            if (!isInCombat)
            {
                if (HasPatrol)
                    stateMachine.ChangeState<PatrolState>();
                else
                    stateMachine.ChangeState<IdleState>();
            }
        }

        protected override void OnTargetAcquired()
        {
            Debug.Log($"{gameObject.name}: Target acquired!");
        }

        protected override void OnTargetLost()
        {
            Debug.Log($"{gameObject.name}: Target lost.");
        }

        #endregion

        #region Recovery

        public override void OnStunComplete()
        {
            if (!IsAlive) return;

            if (HasTarget)
                stateMachine.ChangeState<ChargeState>();
            else
            {
                ExitCombat();
                if (HasPatrol)
                    stateMachine.ChangeState<PatrolState>();
                else
                    stateMachine.ChangeState<IdleState>();
            }
        }

        #endregion

        #region Hitbox Handlers

        private void OnDashHitboxHit(Collider other, Hitbox hitbox)
        {
            hitbox?.Deactivate();

            var damageable = other.GetComponent<IDamageable>()
                          ?? other.GetComponentInParent<IDamageable>();

            if (damageable == null || !damageable.IsAlive)
                return;

            int throwDir = Movement != null ? Movement.FacingDirection : 1;

            if (grab.RollForGrab())
            {
                var grabbable = other.GetComponent<IGrabbable>()
                             ?? other.GetComponentInParent<IGrabbable>();

                if (grabbable != null && grabbable.CanBeGrabbed)
                {
                    var damageInfo = new DamageInfo(dash.DashDamage, gameObject, DamageType.Physical);
                    damageable.TakeDamage(damageInfo);

                    var grabInfo = new GrabInfo(
                        gameObject,
                        grab.GrabDuration,
                        grab.GrabOffset,
                        grab.ThrowForce,
                        grab.ThrowDamage,
                        throwDir
                    );
                    grabbable.GetGrabbed(grabInfo);

                    stateMachine.ChangeState<GrabState>();
                    return;
                }
            }

            var info = new DamageInfo(dash.DashDamage, gameObject, DamageType.Physical, dash.DashKnockback);
            damageable.TakeDamage(info);
        }

        #endregion

        #region Debug

#if UNITY_EDITOR
        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();
            patrol.DrawGizmos(transform);
        }
#endif

        #endregion
    }
}