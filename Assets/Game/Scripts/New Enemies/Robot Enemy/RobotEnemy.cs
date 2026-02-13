using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Robot enemy - dashes at player when spotted.
    /// Has a chance to grab and throw the player on hit.
    /// 
    /// CAPABILITIES: IPatroller, ICharger, IDasher, IGrabber, IRecoverer
    /// 
    /// BEHAVIOR:
    /// - Player spotted → Enter combat, start charging
    /// - Charge complete → Dash to player position
    /// - Dash hit (grab) → Hold player in GrabState → Throw → Recover
    /// - Dash hit (no grab) → Recover
    /// - Dash complete (miss) → Recover
    /// - Recovery complete → If player still visible, charge again; else exit combat and patrol
    /// </summary>
    public class RobotEnemy : EnemyCharacter, IPatroller, ICharger, IDasher, IGrabber, IRecoverer
    {
        // ============================================================
        // BEHAVIORS - Shared, reusable implementations
        // ============================================================

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

        [Header("Robot - VFX Settings")]
        [SerializeField] private float vfxScale = 2f;

        // Active VFX instances
        private GameObject activeChargeVFX;
        private GameObject activeDashVFX;
        private GameObject activeGrabVFX;
        private GameObject activeRecoveryVFX;

        // Helper
        public bool HasPatrol => patrol.HasPatrol;

        // ============================================================
        // IPatroller - Delegates to PatrolBehavior
        // ============================================================

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

        // ============================================================
        // ICharger - Delegates to ChargeBehavior
        // ============================================================

        public float ChargeTime => charge.ChargeTime;
        public GameObject ChargeVFXPrefab => charge.ChargeVFXPrefab;

        public void OnChargeComplete()
        {
            if (!IsAlive) return;
            stateMachine.ChangeState<DashState>();
        }

        // ============================================================
        // IDasher - Delegates to DashBehavior
        // ============================================================

        public float DashSpeed => dash.DashSpeed;
        public float DashDamage => dash.DashDamage;
        public float DashStopDistance => dash.DashStopDistance;
        public Vector2 DashKnockback => dash.DashKnockback;
        public Hitbox DashHitbox => dash.DashHitbox;
        public GameObject DashVFXPrefab => dash.DashVFXPrefab;

        public void OnDashComplete()
        {
            if (!IsAlive) return;
            stateMachine.ChangeState<RecoverState>();
        }

        // ============================================================
        // IGrabber - Delegates to GrabBehavior
        // ============================================================

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

        // ============================================================
        // IRecoverer - Delegates to RecoveryBehavior
        // ============================================================

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

        // ============================================================
        // Lifecycle
        // ============================================================

        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.Robot;

            // Initialize behaviors
            patrol.Initialize(transform);

            // Setup dash hitbox events
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

            VFXPool.Release(ref activeChargeVFX);
            VFXPool.Release(ref activeDashVFX);
            VFXPool.Release(ref activeGrabVFX);
            VFXPool.Release(ref activeRecoveryVFX);
        }

        // ============================================================
        // Robot Brain - Core Decisions
        // ============================================================

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

        // ============================================================
        // Dash Hit Behavior (Robot-specific)
        // ============================================================

        private void OnDashHitboxHit(Collider other, Hitbox hitbox)
        {
            hitbox?.Deactivate();

            var damageable = other.GetComponent<IDamageable>();
            if (damageable == null)
                damageable = other.GetComponentInParent<IDamageable>();

            if (damageable == null || !damageable.IsAlive)
                return;

            int throwDir = Movement != null ? Movement.FacingDirection : 1;

            // Use GrabBehavior's helper
            if (grab.RollForGrab())
            {
                var grabbable = other.GetComponent<IGrabbable>();
                if (grabbable == null)
                    grabbable = other.GetComponentInParent<IGrabbable>();

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

                    Debug.Log($"{gameObject.name} GRABBED {other.name}!");
                    return;
                }
            }

            // Normal hit
            var info = new DamageInfo(dash.DashDamage, gameObject, DamageType.Physical, dash.DashKnockback);
            damageable.TakeDamage(info);
            Debug.Log($"{gameObject.name} hit {other.name} for {dash.DashDamage} damage");
        }

        // ============================================================
        // VFX Methods
        // ============================================================

        public void SpawnChargeVFX()
        {
            ReleaseChargeVFX();
            activeChargeVFX = VFXPool.Get(charge.ChargeVFXPrefab, transform, vfxScale);
        }

        public void ReleaseChargeVFX() => VFXPool.Release(ref activeChargeVFX);

        public void SpawnDashVFX()
        {
            ReleaseDashVFX();
            activeDashVFX = VFXPool.Get(dash.DashVFXPrefab, transform, vfxScale);
        }

        public void ReleaseDashVFX() => VFXPool.Release(ref activeDashVFX);

        public void SpawnGrabVFX()
        {
            ReleaseGrabVFX();
            activeGrabVFX = VFXPool.Get(grab.GrabVFXPrefab, transform, vfxScale);
        }

        public void ReleaseGrabVFX() => VFXPool.Release(ref activeGrabVFX);

        public void SpawnRecoveryVFX()
        {
            ReleaseRecoveryVFX();
            activeRecoveryVFX = VFXPool.Get(recovery.RecoveryVFXPrefab, transform, vfxScale);
        }

        public void ReleaseRecoveryVFX() => VFXPool.Release(ref activeRecoveryVFX);

        // ============================================================
        // Debug Gizmos
        // ============================================================

        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();
            patrol.DrawGizmos(transform);
        }
    }
}