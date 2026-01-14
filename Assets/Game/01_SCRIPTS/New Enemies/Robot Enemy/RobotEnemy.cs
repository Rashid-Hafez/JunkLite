using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Robot enemy - dashes at player when spotted.
    /// Has a chance to grab and throw the player on hit.
    /// 
    /// CAPABILITIES: ICharger, IDasher, IGrabber, IRecoverer
    /// 
    /// BEHAVIOR (decisions defined here):
    /// - Player spotted → Enter combat, start charging
    /// - Charge complete → Dash to player position
    /// - Dash hit (grab) → Hold player in GrabState → Throw → Recover
    /// - Dash hit (no grab) → Recover
    /// - Dash complete (miss) → Recover
    /// - Recovery complete → If player still visible, charge again; else exit combat and patrol
    /// </summary>
    public class RobotEnemy : EnemyCharacter, ICharger, IDasher, IGrabber, IRecoverer
    {
        [Header("Robot - Charge")]
        [SerializeField] private float chargeTime = 1f;
        [SerializeField] private GameObject chargeVFXPrefab;

        [Header("Robot - Dash Attack")]
        [SerializeField] private float dashSpeed = 15f;
        [SerializeField] private float dashDamage = 10f;
        [SerializeField] private Vector2 dashKnockback = new Vector2(15f, 5f);
        [SerializeField] private Hitbox dashHitbox;
        [SerializeField] private GameObject dashVFXPrefab;

        [Header("Robot - Grab Attack")]
        [SerializeField] private bool canGrab = true;
        [SerializeField][Range(0f, 1f)] private float grabChance = 0.3f;
        [SerializeField] private float grabDuration = 0.5f;
        [SerializeField] private Vector2 throwForce = new Vector2(25f, 10f);
        [SerializeField] private float throwDamage = 5f;
        [SerializeField] private Vector3 grabOffset = new Vector3(0f, 1.5f, 0f);
        [SerializeField] private GameObject grabVFXPrefab;

        [Header("Robot - Recovery")]
        [SerializeField] private float recoveryTime = 0.3f;
        [SerializeField] private GameObject recoveryVFXPrefab;

        [Header("Robot - VFX Settings")]
        [SerializeField] private float vfxScale = 2f;

        // Active VFX instances
        private GameObject activeChargeVFX;
        private GameObject activeDashVFX;
        private GameObject activeGrabVFX;
        private GameObject activeRecoveryVFX;

        #region ICharger Implementation

        public float ChargeTime => chargeTime;
        public GameObject ChargeVFXPrefab => chargeVFXPrefab;

        public void OnChargeComplete()
        {
            if (!IsAlive) return;
            stateMachine.ChangeState<DashState>();
        }

        #endregion

        #region IDasher Implementation

        public float DashSpeed => dashSpeed;
        public float DashDamage => dashDamage;
        public Vector2 DashKnockback => dashKnockback;
        public Hitbox DashHitbox => dashHitbox;
        public GameObject DashVFXPrefab => dashVFXPrefab;

        public void OnDashComplete()
        {
            if (!IsAlive) return;
            stateMachine.ChangeState<RecoverState>();
        }

        #endregion

        #region IGrabber Implementation

        public bool CanGrab => canGrab;
        public float GrabChance => grabChance;
        public float GrabDuration => grabDuration;
        public Vector3 GrabOffset => grabOffset;
        public Vector2 ThrowForce => throwForce;
        public float ThrowDamage => throwDamage;
        public GameObject GrabVFXPrefab => grabVFXPrefab;

        public void OnGrabComplete()
        {
            if (!IsAlive) return;
            stateMachine.ChangeState<RecoverState>();
        }

        #endregion

        #region IRecoverer Implementation

        public float RecoveryTime => recoveryTime;
        public GameObject RecoveryVFXPrefab => recoveryVFXPrefab;

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

            // Setup dash hitbox events
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

            if (dashHitbox != null)
                dashHitbox.OnHit -= OnDashHitboxHit;

            // Release VFX back to pool
            VFXPool.Release(ref activeChargeVFX);
            VFXPool.Release(ref activeDashVFX);
            VFXPool.Release(ref activeGrabVFX);
            VFXPool.Release(ref activeRecoveryVFX);
        }

        #endregion

        #region Damage Handling

        public override void TakeDamage(DamageInfo info)
        {
            if (state != null && !state.CanTakeDamage)
                return;

            base.TakeDamage(info);
        }

        #endregion

        #region Robot Brain - Core Decisions

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

            // Only return to patrol if not in combat
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

        #region Dash Hit Behavior

        private void OnDashHitboxHit(Collider other, Hitbox hitbox)
        {
            hitbox?.Deactivate();

            var damageable = other.GetComponent<IDamageable>();
            if (damageable == null)
                damageable = other.GetComponentInParent<IDamageable>();

            if (damageable == null || !damageable.IsAlive)
                return;

            int throwDir = Movement != null ? Movement.FacingDirection : 1;

            // Check for grab
            bool doGrab = canGrab && Random.value <= grabChance;

            if (doGrab)
            {
                var grabbable = other.GetComponent<IGrabbable>();
                if (grabbable == null)
                    grabbable = other.GetComponentInParent<IGrabbable>();

                if (grabbable != null && grabbable.CanBeGrabbed)
                {
                    // Deal damage first
                    var damageInfo = new DamageInfo(dashDamage, gameObject, DamageType.Physical);
                    damageable.TakeDamage(damageInfo);

                    // Then grab
                    var grabInfo = new GrabInfo(
                        gameObject,
                        grabDuration,
                        grabOffset,
                        throwForce,
                        throwDamage,
                        throwDir
                    );
                    grabbable.GetGrabbed(grabInfo);

                    stateMachine.ChangeState<GrabState>();

                    Debug.Log($"{gameObject.name} GRABBED {other.name}!");
                    return;
                }
            }

            // Normal hit - damage + knockback
            var info = new DamageInfo(dashDamage, gameObject, DamageType.Physical, dashKnockback);
            damageable.TakeDamage(info);
            Debug.Log($"{gameObject.name} hit {other.name} for {dashDamage} damage");
        }

        #endregion

        #region VFX Methods (Pooled)

        public void SpawnChargeVFX()
        {
            ReleaseChargeVFX();
            activeChargeVFX = VFXPool.Get(chargeVFXPrefab, transform, vfxScale);
        }

        public void ReleaseChargeVFX()
        {
            VFXPool.Release(ref activeChargeVFX);
        }

        public void SpawnDashVFX()
        {
            ReleaseDashVFX();
            activeDashVFX = VFXPool.Get(dashVFXPrefab, transform, vfxScale);
        }

        public void ReleaseDashVFX()
        {
            VFXPool.Release(ref activeDashVFX);
        }

        public void SpawnGrabVFX()
        {
            ReleaseGrabVFX();
            activeGrabVFX = VFXPool.Get(grabVFXPrefab, transform, vfxScale);
        }

        public void ReleaseGrabVFX()
        {
            VFXPool.Release(ref activeGrabVFX);
        }

        public void SpawnRecoveryVFX()
        {
            ReleaseRecoveryVFX();
            activeRecoveryVFX = VFXPool.Get(recoveryVFXPrefab, transform, vfxScale);
        }

        public void ReleaseRecoveryVFX()
        {
            VFXPool.Release(ref activeRecoveryVFX);
        }

        #endregion
    }
}