using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Hyena enemy - aggressive predator that chases and never loses aggro.
    /// Uses HP-weighted decision making for combat choices.
    /// 
    /// CAPABILITIES: ICharger, IDasher, IMeleeAttacker, IDodger, IChaser, IRecoverer
    /// 
    /// BEHAVIOR (decisions defined here):
    /// - Player spotted → Enter combat, start chasing (NEVER exits combat)
    /// - In attack range + Player attacking → Roll dodge chance (HP-weighted)
    /// - In attack range + Player not attacking → Pick attack (Melee or Dash)
    /// - Dodge/Attack complete → Check distance: Far? Chase. Close? Pick action.
    /// - Player escapes detection → Chase to last known position
    /// </summary>
    public class HyenaEnemy : EnemyCharacter, ICharger, IDasher, IMeleeAttacker, IDodger, IChaser, IRecoverer
    {
        [Header("Hyena - Charge")]
        [SerializeField] private float chargeTime = 0.3f;
        [SerializeField] private GameObject chargeVFXPrefab;

        [Header("Hyena - Dash Attack")]
        [SerializeField] private float dashSpeed = 18f;
        [SerializeField] private float dashDamage = 12f;
        [SerializeField] private Vector2 dashKnockback = new Vector2(12f, 4f);
        [SerializeField] private Hitbox dashHitbox;
        [SerializeField] private GameObject dashVFXPrefab;

        [Header("Hyena - Melee Attack")]
        [SerializeField] private float meleeAttackDuration = 0.4f;
        [SerializeField] private float meleeDamage = 8f;
        [SerializeField] private Vector2 meleeKnockback = new Vector2(8f, 3f);
        [SerializeField] private Hitbox meleeHitbox;
        [SerializeField] private GameObject meleeVFXPrefab;

        [Header("Hyena - Dodge")]
        [SerializeField] private float dodgeDistance = 4f;
        [SerializeField] private float dodgeDuration = 0.35f;
        [SerializeField] private float dodgeHeight = 2f;
        [SerializeField] private bool dodgeHasIFrames = true;
        [SerializeField] private GameObject dodgeVFXPrefab;

        [Header("Hyena - Dodge Chance (HP-Weighted)")]
        [Tooltip("Base chance to dodge when player is attacking (0-1)")]
        [SerializeField][Range(0f, 1f)] private float baseDodgeChance = 0.2f;
        [Tooltip("Additional dodge chance at 0% HP (0-1)")]
        [SerializeField][Range(0f, 1f)] private float lowHpDodgeBonus = 0.4f;

        [Header("Hyena - Recovery")]
        [SerializeField] private float recoveryTime = 0.2f;
        [SerializeField] private GameObject recoveryVFXPrefab;

        [Header("Hyena - Chase Settings")]
        [SerializeField] private float chaseSpeed = 8f;
        [Tooltip("Distance threshold to consider 'far' from player")]
        [SerializeField] private float farDistanceThreshold = 5f;

        [Header("Hyena - Action Weights")]
        [Tooltip("Weight for choosing melee attack")]
        [SerializeField] private float meleeWeight = 1f;
        [Tooltip("Weight for choosing dash attack")]
        [SerializeField] private float dashWeight = 0.6f;

        [Header("Hyena - VFX Settings")]
        [SerializeField] private float vfxScale = 2f;

        // Chase tracking
        private Vector3 lastKnownTargetPosition;
        private bool hasLastKnownPosition = false;

        // Active VFX instances
        private GameObject activeChargeVFX;
        private GameObject activeDashVFX;
        private GameObject activeMeleeVFX;
        private GameObject activeDodgeVFX;
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

        #region IMeleeAttacker Implementation

        public float MeleeAttackDuration => meleeAttackDuration;
        public float MeleeDamage => meleeDamage;
        public Vector2 MeleeKnockback => meleeKnockback;
        public Hitbox MeleeHitbox => meleeHitbox;
        public GameObject MeleeVFXPrefab => meleeVFXPrefab;

        public void OnMeleeComplete()
        {
            if (!IsAlive) return;
            DecideNextActionAfterAttack();
        }

        #endregion

        #region IDodger Implementation

        public float DodgeDistance => dodgeDistance;
        public float DodgeDuration => dodgeDuration;
        public float DodgeHeight => dodgeHeight;
        public bool DodgeHasIFrames => dodgeHasIFrames;
        public GameObject DodgeVFXPrefab => dodgeVFXPrefab;

        public void OnDodgeComplete()
        {
            if (!IsAlive) return;
            DecideNextActionAfterAttack();
        }

        #endregion

        #region IChaser Implementation

        public Vector3 LastKnownTargetPosition => lastKnownTargetPosition;
        public bool HasLastKnownPosition => hasLastKnownPosition;
        public float ChaseSpeed => chaseSpeed;

        public void OnReachedTarget()
        {
            if (!IsAlive) return;
            OnPlayerInAttackRange();
        }

        public void UpdateLastKnownPosition(Vector3 position)
        {
            lastKnownTargetPosition = position;
            hasLastKnownPosition = true;
        }

        #endregion

        #region IRecoverer Implementation

        public float RecoveryTime => recoveryTime;
        public GameObject RecoveryVFXPrefab => recoveryVFXPrefab;

        public void OnRecoveryComplete()
        {
            if (!IsAlive) return;
            DecideNextActionAfterAttack();
        }

        #endregion

        #region Lifecycle

        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.Hyena;

            // Setup hitbox events
            if (dashHitbox != null)
            {
                dashHitbox.OnHit += OnDashHitboxHit;
                dashHitbox.Deactivate();
            }

            if (meleeHitbox != null)
            {
                meleeHitbox.OnHit += OnMeleeHitboxHit;
                meleeHitbox.Deactivate();
            }
        }

        protected override void InitializeStateMachine()
        {
            stateMachine.RegisterStates(
                new PatrolState(this),
                new IdleState(this),
                new ChaseState(this),
                new ChargeState(this),
                new DashState(this),
                new MeleeAttackState(this),
                new DodgeState(this),
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

            // Continuously update last known position while we have a target
            if (HasTarget)
            {
                UpdateLastKnownPosition(Target.position);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (dashHitbox != null)
                dashHitbox.OnHit -= OnDashHitboxHit;

            if (meleeHitbox != null)
                meleeHitbox.OnHit -= OnMeleeHitboxHit;

            // Release VFX back to pool
            VFXPool.Release(ref activeChargeVFX);
            VFXPool.Release(ref activeDashVFX);
            VFXPool.Release(ref activeMeleeVFX);
            VFXPool.Release(ref activeDodgeVFX);
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

        #region Hyena Brain - Core Decisions

        public override void OnPlayerSpotted()
        {
            if (!IsAlive) return;
            if (isInCombat) return;

            EnterCombat();
            stateMachine.ChangeState<ChaseState>();

            Debug.Log($"{gameObject.name}: Player spotted! Beginning hunt.");
        }

        /// <summary>
        /// Hyena NEVER loses aggro - if player escapes detection,
        /// chase to last known position.
        /// </summary>
        public override void OnPlayerLost()
        {
            if (!IsAlive) return;

            // Don't exit combat - hyena is persistent!
            if (hasLastKnownPosition && isInCombat)
            {
                Debug.Log($"{gameObject.name}: Lost sight of player, chasing to last known position.");
                stateMachine.ChangeState<ChaseState>();
            }
        }

        /// <summary>
        /// Called by ChaseState when player is in attack range.
        /// This is where the smart decision-making happens.
        /// </summary>
        public override void OnPlayerInAttackRange()
        {
            if (!IsAlive) return;

            // Check if player is currently attacking
            bool playerIsAttacking = IsPlayerAttacking();

            if (playerIsAttacking)
            {
                float dodgeChance = CalculateDodgeChance();

                if (Random.value <= dodgeChance)
                {
                    Debug.Log($"{gameObject.name}: Dodging player attack! (chance was {dodgeChance:P0})");
                    stateMachine.ChangeState<DodgeState>();
                    return;
                }
                else
                {
                    Debug.Log($"{gameObject.name}: Failed dodge roll, attacking instead.");
                }
            }

            // Not dodging - pick an attack
            PickAttackAction();
        }

        protected override void OnTargetAcquired()
        {
            Debug.Log($"{gameObject.name}: Target acquired - the hunt begins!");
        }

        protected override void OnTargetLost()
        {
            Debug.Log($"{gameObject.name}: Target lost - but the hunt continues...");
        }

        #endregion

        #region Decision Helpers

        private void DecideNextActionAfterAttack()
        {
            if (!HasTarget)
            {
                if (hasLastKnownPosition)
                {
                    stateMachine.ChangeState<ChaseState>();
                }
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

            if (DistanceToTarget > farDistanceThreshold)
            {
                stateMachine.ChangeState<ChaseState>();
            }
            else
            {
                OnPlayerInAttackRange();
            }
        }

        private void PickAttackAction()
        {
            float totalWeight = meleeWeight + dashWeight;
            float roll = Random.value * totalWeight;

            if (roll <= meleeWeight)
            {
                Debug.Log($"{gameObject.name}: Choosing MELEE attack!");
                stateMachine.ChangeState<MeleeAttackState>();
            }
            else
            {
                Debug.Log($"{gameObject.name}: Choosing DASH attack!");
                stateMachine.ChangeState<ChargeState>();
            }
        }

        private float CalculateDodgeChance()
        {
            float hpPercent = GetHealthPercent();
            float dodgeChance = baseDodgeChance + (1f - hpPercent) * lowHpDodgeBonus;
            return Mathf.Clamp01(dodgeChance);
        }

        private float GetHealthPercent()
        {
            if (attributes != null && attributes.Health != null)
            {
                float current = attributes.Health.Current;
                float max = attributes.Health.Max;
                if (max > 0f)
                    return Mathf.Clamp01(current / max);
            }
            return 1f;
        }

        private bool IsPlayerAttacking()
        {
            if (TargetCharacter == null) return false;

            var playerState = TargetCharacter.GetComponent<PlayerState>();
            if (playerState != null)
                return playerState.IsAttacking;

            var charState = TargetCharacter.GetComponent<CharacterState>();
            if (charState != null)
                return charState.IsAttacking;

            return false;
        }

        #endregion

        #region Hitbox Handlers

        private void OnDashHitboxHit(Collider other, Hitbox hitbox)
        {
            hitbox?.Deactivate();

            var damageable = other.GetComponent<IDamageable>();
            if (damageable == null)
                damageable = other.GetComponentInParent<IDamageable>();

            if (damageable == null || !damageable.IsAlive)
                return;

            var info = new DamageInfo(dashDamage, gameObject, DamageType.Physical, dashKnockback);
            damageable.TakeDamage(info);

            Debug.Log($"{gameObject.name} DASH hit {other.name} for {dashDamage} damage!");
        }

        private void OnMeleeHitboxHit(Collider other, Hitbox hitbox)
        {
            var damageable = other.GetComponent<IDamageable>();
            if (damageable == null)
                damageable = other.GetComponentInParent<IDamageable>();

            if (damageable == null || !damageable.IsAlive)
                return;

            var info = new DamageInfo(meleeDamage, gameObject, DamageType.Physical, meleeKnockback);
            damageable.TakeDamage(info);

            Debug.Log($"{gameObject.name} MELEE hit {other.name} for {meleeDamage} damage!");
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

        public void SpawnMeleeVFX()
        {
            ReleaseMeleeVFX();
            activeMeleeVFX = VFXPool.Get(meleeVFXPrefab, transform, vfxScale);
        }

        public void ReleaseMeleeVFX()
        {
            VFXPool.Release(ref activeMeleeVFX);
        }

        public void SpawnDodgeVFX()
        {
            ReleaseDodgeVFX();
            activeDodgeVFX = VFXPool.Get(dodgeVFXPrefab, transform, vfxScale);
        }

        public void ReleaseDodgeVFX()
        {
            VFXPool.Release(ref activeDodgeVFX);
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

        #region Debug Gizmos

        protected override void OnDrawGizmosSelected()
        {
            base.OnDrawGizmosSelected();

            // Draw far distance threshold (chase re-engage distance)
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f); // Orange
            Gizmos.DrawWireSphere(transform.position, farDistanceThreshold);

            // Draw last known position if we have one (only in play mode)
            if (hasLastKnownPosition && Application.isPlaying)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireSphere(lastKnownTargetPosition, 0.5f);
                Gizmos.DrawLine(transform.position, lastKnownTargetPosition);
            }
        }

        #endregion
    }
}