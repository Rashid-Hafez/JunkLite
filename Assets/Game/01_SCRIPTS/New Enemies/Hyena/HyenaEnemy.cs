using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Hyena enemy - aggressive predator with dodge and counter-attack.
    /// 
    /// CAPABILITIES: IPatroller, IMeleeAttacker, IChaser, IDodger, ICharger, IDasher, IRecoverer
    /// 
    /// BEHAVIOR:
    /// - Patrol until player spotted
    /// - Chase player
    /// - When in range:
    ///   - If player is attacking and facing hyena → Roll dodge chance → Dodge backwards
    ///   - Else → Melee attack
    /// - After dodge → Brief recovery, then chance to counter with dash attack
    /// - After attack/dodge → Check range, chase or attack again
    /// </summary>
    public class HyenaEnemy : EnemyCharacter, IPatroller, IMeleeAttacker, IChaser, IDodger, ICharger, IDasher, IRecoverer
    {
        [Header("Hyena - Patrol")]
        [SerializeField] private float patrolDistance = 5f;
        [SerializeField] private float patrolSpeed = 3f;
        [SerializeField] private float wallCheckDistance = 0.5f;
        [SerializeField] private LayerMask wallLayer;

        [Header("Hyena - Melee Attack")]
        [SerializeField] private float meleeAttackDuration = 0.4f;
        [SerializeField] private float attackCooldown = 0.3f;
        [SerializeField] private float meleeDamage = 8f;
        [SerializeField] private Vector2 meleeKnockback = new Vector2(8f, 3f);
        [SerializeField] private Hitbox meleeHitbox;
        [SerializeField] private GameObject meleeVFXPrefab;

        [Header("Hyena - Dash Attack")]
        [SerializeField] private float chargeTime = 0.2f;
        [SerializeField] private GameObject chargeVFXPrefab;
        [SerializeField] private float dashSpeed = 18f;
        [SerializeField] private float dashDamage = 12f;
        [SerializeField] private Vector2 dashKnockback = new Vector2(12f, 4f);
        [SerializeField] private Hitbox dashHitbox;
        [SerializeField] private GameObject dashVFXPrefab;
        [Tooltip("Distance from target where dash stops")]
        [SerializeField] private float dashStopDistance = 0.5f;
        [Tooltip("Maximum dash attack chance when at low HP (0-1)")]
        [SerializeField][Range(0f, 1f)] private float maxDashChance = 0.5f;

        [Header("Hyena - Chase Settings")]
        [SerializeField] private float chaseSpeed = 8f;

        [Header("Hyena - Dodge")]
        [SerializeField] private float dodgeDistance = 3f;
        [SerializeField] private float dodgeSpeed = 10f;
        [SerializeField] private float dodgeHeight = 0.5f;
        [SerializeField] private bool dodgeHasIFrames = true;
        [SerializeField] private GameObject dodgeVFXPrefab;
        [Tooltip("Chance to dodge when player is attacking (0-1)")]
        [SerializeField][Range(0f, 1f)] private float dodgeChance = 0.3f;
        [Tooltip("Range within which hyena will check for player attacks to dodge")]
        [SerializeField] private float dodgeCheckRange = 4f;
        [Tooltip("Cooldown between dodge attempts")]
        [SerializeField] private float dodgeCooldown = 1f;

        [Header("Hyena - Pursuit Break-off")]
        [Tooltip("When in combat, detection zone expands to this radius. If player exits, hyena breaks off.")]
        [SerializeField] private float pursuitRadius = 15f;

        [Header("Hyena - Post-Dodge Behavior")]
        [Tooltip("Recovery time after dodge before next action")]
        [SerializeField] private float postDodgeRecoveryTime = 0.3f;
        [Tooltip("Chance to counter-attack with dash after dodging (0-1)")]
        [SerializeField][Range(0f, 1f)] private float counterAttackChance = 0.4f;
        [SerializeField] private GameObject recoveryVFXPrefab;

        [Header("Hyena - VFX Settings")]
        [SerializeField] private float vfxScale = 2f;

        // Patrol state
        private Vector3 spawnPosition;
        private int patrolDirection = 1;

        // Chase tracking
        private Vector3 lastKnownTargetPosition;
        private bool hasLastKnownPosition = false;

        // Dodge tracking
        private float lastDodgeTime = -999f;
        private bool wasPlayerAttacking = false;
        private float dodgeInvulnerabilityEndTime = 0f;

        // Post-dodge state tracking
        private bool shouldCounterAttack = false;

        // Dash chance (increases with damage taken)
        private float currentDashChance = 0f;

        // Active VFX instances
        private GameObject activeMeleeVFX;
        private GameObject activeDodgeVFX;
        private GameObject activeChargeVFX;
        private GameObject activeDashVFX;
        private GameObject activeRecoveryVFX;

        // Helper for checking if patrol is enabled
        public bool HasPatrol => patrolDistance > 0f;

        #region IPatroller Implementation

        public float PatrolDistance => patrolDistance;
        public float PatrolSpeed => patrolSpeed;
        public Vector3 SpawnPosition => spawnPosition;
        public int PatrolDirection { get => patrolDirection; set => patrolDirection = value; }

        public bool IsWallAhead()
        {
            Vector3 direction = patrolDirection > 0 ? Vector3.right : Vector3.left;
            return Physics.Raycast(transform.position, direction, wallCheckDistance, wallLayer);
        }

        public bool IsAtPatrolBoundary()
        {
            float distanceFromSpawn = transform.position.x - spawnPosition.x;

            if (patrolDirection > 0 && distanceFromSpawn >= patrolDistance)
                return true;
            if (patrolDirection < 0 && distanceFromSpawn <= -patrolDistance)
                return true;

            return false;
        }

        public void ReverseDirection()
        {
            patrolDirection *= -1;
        }

        #endregion

        #region IMeleeAttacker Implementation

        public float MeleeAttackDuration => meleeAttackDuration;
        public float AttackCooldown => attackCooldown;
        public float MeleeDamage => meleeDamage;
        public Vector2 MeleeKnockback => meleeKnockback;
        public Hitbox MeleeHitbox => meleeHitbox;
        public GameObject MeleeVFXPrefab => meleeVFXPrefab;

        public void OnMeleeComplete()
        {
            if (!IsAlive) return;

            // Check if player is still in attack range (uses base class attackRange)
            if (HasTarget && IsTargetInAttackRange)
            {
                Debug.Log($"{gameObject.name}: Player still in range → Slash again!");
                // Stay in MeleeAttackState - it will handle the cooldown and next slash
                return;
            }

            Debug.Log($"{gameObject.name}: Player out of range → Chase!");
            stateMachine.ChangeState<ChaseState>();
        }

        #endregion

        #region IChaser Implementation

        public Vector3 LastKnownTargetPosition => lastKnownTargetPosition;
        public bool HasLastKnownPosition => hasLastKnownPosition;
        public float ChaseSpeed => chaseSpeed;

        public void OnReachedTarget()
        {
            // Called by ChaseState when we reach last known position but no target
            if (!IsAlive) return;

            Debug.Log($"{gameObject.name}: Reached last known position, target gone → Patrol.");
            hasLastKnownPosition = false;
            ExitCombat();

            if (HasPatrol)
                stateMachine.ChangeState<PatrolState>();
            else
                stateMachine.ChangeState<IdleState>();
        }

        public void UpdateLastKnownPosition(Vector3 position)
        {
            lastKnownTargetPosition = position;
            hasLastKnownPosition = true;
        }

        #endregion

        #region IDodger Implementation

        public float DodgeDistance => dodgeDistance;
        public float DodgeSpeed => dodgeSpeed;
        // Duration calculated from distance/speed
        public float DodgeDuration => dodgeSpeed > 0f ? dodgeDistance / dodgeSpeed : 0.3f;
        public float DodgeHeight => dodgeHeight;
        public bool DodgeHasIFrames => dodgeHasIFrames;
        public GameObject DodgeVFXPrefab => dodgeVFXPrefab;

        public void OnDodgeComplete()
        {
            if (!IsAlive) return;

            Debug.Log($"{gameObject.name}: Dodge complete → Recovery...");

            // Roll for counter-attack
            shouldCounterAttack = Random.value <= counterAttackChance;

            if (shouldCounterAttack)
                Debug.Log($"{gameObject.name}: Will counter-attack after recovery!");

            // Go to recovery state (brief pause before next action)
            stateMachine.ChangeState<RecoverState>();
        }

        #endregion

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
        public float DashStopDistance => dashStopDistance;

        public void OnDashComplete()
        {
            if (!IsAlive) return;

            // Brief pause after dash before next action (prevents jittering)
            // Don't roll for counter-attack after dash (that's only after dodge)
            shouldCounterAttack = false;
            stateMachine.ChangeState<RecoverState>();
        }

        #endregion

        #region IRecoverer Implementation

        public float RecoveryTime => postDodgeRecoveryTime;
        public GameObject RecoveryVFXPrefab => recoveryVFXPrefab;

        public void OnRecoveryComplete()
        {
            if (!IsAlive) return;

            Debug.Log($"{gameObject.name}: Recovery complete → Deciding action...");

            // Check if we should counter-attack
            if (shouldCounterAttack && HasTarget)
            {
                Debug.Log($"{gameObject.name}: COUNTER-ATTACK! Charging dash!");
                shouldCounterAttack = false;
                stateMachine.ChangeState<ChargeState>();
                return;
            }

            shouldCounterAttack = false;

            // Normal combat decision
            DecideNextCombatAction();
        }

        #endregion

        /// <summary>
        /// Standard combat decision with chance for dash attack based on damage taken.
        /// In range = melee, out of range = chase OR dash attack (if wounded enough).
        /// </summary>
        private void DecideNextCombatAction()
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

            if (IsTargetInAttackRange)
            {
                stateMachine.ChangeState<MeleeAttackState>();
            }
            else
            {
                // When out of range, roll for dash attack (wounded hyenas are more aggressive)
                if (ShouldDashAttack())
                {
                    Debug.Log($"{gameObject.name}: Wounded aggression! Dash attack!");
                    stateMachine.ChangeState<ChargeState>();
                }
                else
                {
                    stateMachine.ChangeState<ChaseState>();
                }
            }
        }

        #region Damage Handling

        public override bool TakeDamage(DamageInfo info)
        {
            // Check immediate dodge i-frames (covers the frame between decision and state change)
            if (dodgeHasIFrames && Time.time < dodgeInvulnerabilityEndTime)
            {
                Debug.Log($"{gameObject.name}: Damage blocked by dodge i-frames!");
                return false; // Damage NOT dealt
            }

            // Check if current state allows taking damage (e.g., during dodge state)
            if (state != null && !state.CanTakeDamage)
            {
                Debug.Log($"{gameObject.name}: Damage blocked by state i-frames!");
                return false; // Damage NOT dealt
            }

            // Let base class handle the actual damage
            bool damageDealt = base.TakeDamage(info);

            // Update dash chance based on missing HP (more wounded = more aggressive)
            if (damageDealt)
            {
                UpdateDashChance();
            }

            return damageDealt;
        }

        /// <summary>
        /// Updates dash attack chance based on missing HP.
        /// At full HP: 0% chance. At 0% HP: maxDashChance.
        /// </summary>
        private void UpdateDashChance()
        {
            if (attributes == null || attributes.Health == null) return;

            float max = attributes.Health.Max;
            if (max <= 0f) return;

            float healthPercent = attributes.Health.Current / max;
            float missingHealthPercent = 1f - healthPercent;

            // Scale from 0 to maxDashChance based on missing health
            currentDashChance = missingHealthPercent * maxDashChance;

            Debug.Log($"{gameObject.name}: Dash chance updated to {currentDashChance:P0} (HP: {healthPercent:P0})");
        }

        /// <summary>
        /// Rolls to see if hyena should dash attack instead of normal behavior.
        /// </summary>
        private bool ShouldDashAttack()
        {
            if (currentDashChance <= 0f) return false;
            return Random.value <= currentDashChance;
        }

        #endregion

        #region Lifecycle

        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.Hyena;
            spawnPosition = transform.position;

            // Setup melee hitbox events
            if (meleeHitbox != null)
            {
                meleeHitbox.OnHit += OnMeleeHitboxHit;
                meleeHitbox.Deactivate();
            }

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

            // Continuously update last known position while we have a target
            if (HasTarget)
            {
                UpdateLastKnownPosition(Target.position);
            }

            // Continuous dodge check - react to player attacks
            CheckForDodgeOpportunity();
        }

        /// <summary>
        /// Continuously checks if we should dodge based on player attacking.
        /// Only triggers on the FRAME the player starts attacking (not every frame they're attacking).
        /// Only dodges if the player is facing the Hyena (attack is directed at us).
        /// </summary>
        private void CheckForDodgeOpportunity()
        {
            if (!IsAlive || !HasTarget)
            {
                wasPlayerAttacking = false;
                return;
            }

            // Don't check if already dodging
            if (stateMachine.CurrentState is DodgeState)
            {
                wasPlayerAttacking = IsPlayerAttacking();
                return;
            }

            // Check if player is within dodge check range
            if (DistanceToTarget > dodgeCheckRange)
            {
                wasPlayerAttacking = IsPlayerAttacking();
                return;
            }

            // Check if dodge is on cooldown
            if (Time.time - lastDodgeTime < dodgeCooldown)
            {
                wasPlayerAttacking = IsPlayerAttacking();
                return;
            }

            bool isPlayerAttackingNow = IsPlayerAttacking();

            // Only react when player STARTS attacking (rising edge detection)
            if (isPlayerAttackingNow && !wasPlayerAttacking)
            {
                // Check if player is facing us - only dodge if attack is directed at us
                if (!IsPlayerFacingMe())
                {
                    Debug.Log($"{gameObject.name}: Player attacking but not facing me - no dodge needed.");
                    wasPlayerAttacking = isPlayerAttackingNow;
                    return;
                }

                Debug.Log($"{gameObject.name}: Player attacking and facing me! Rolling dodge...");

                // Roll dodge chance
                if (Random.value <= dodgeChance)
                {
                    Debug.Log($"{gameObject.name}: Dodge SUCCESS! Evading!");

                    // Grant IMMEDIATE i-frames (covers the gap before state changes)
                    dodgeInvulnerabilityEndTime = Time.time + DodgeDuration;

                    lastDodgeTime = Time.time;
                    stateMachine.ChangeState<DodgeState>();
                }
                else
                {
                    Debug.Log($"{gameObject.name}: Dodge FAILED roll.");
                }
            }

            wasPlayerAttacking = isPlayerAttackingNow;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (meleeHitbox != null)
                meleeHitbox.OnHit -= OnMeleeHitboxHit;

            if (dashHitbox != null)
                dashHitbox.OnHit -= OnDashHitboxHit;

            // Release VFX back to pool
            VFXPool.Release(ref activeMeleeVFX);
            VFXPool.Release(ref activeDodgeVFX);
            VFXPool.Release(ref activeChargeVFX);
            VFXPool.Release(ref activeDashVFX);
            VFXPool.Release(ref activeRecoveryVFX);
        }

        #endregion

        #region Hyena Brain - Core Decisions

        public override void OnPlayerSpotted()
        {
            if (!IsAlive) return;
            if (isInCombat) return;

            Debug.Log($"{gameObject.name}: Player spotted → CHASE!");
            EnterCombat();
            ExpandDetectionZone(); // Expand to pursuit radius
            stateMachine.ChangeState<ChaseState>();
        }

        public override void OnPlayerLost()
        {
            if (!IsAlive) return;

            Debug.Log($"{gameObject.name}: Player lost (exited pursuit range).");

            // If in combat and have last known position, keep chasing
            if (isInCombat && hasLastKnownPosition)
            {
                Debug.Log($"{gameObject.name}: Chasing to last known position...");
                stateMachine.ChangeState<ChaseState>();
                return;
            }

            // Otherwise exit combat and patrol
            ExitCombat();
            ShrinkDetectionZone(); // Shrink back to original radius
            if (HasPatrol)
                stateMachine.ChangeState<PatrolState>();
            else
                stateMachine.ChangeState<IdleState>();
        }

        /// <summary>
        /// Expands detection zone to pursuit radius when entering combat.
        /// </summary>
        private void ExpandDetectionZone()
        {
            if (detectionZone != null && pursuitRadius > 0f)
            {
                detectionZone.SetRadius(pursuitRadius);
                Debug.Log($"{gameObject.name}: Detection zone expanded to {pursuitRadius} (pursuit mode)");
            }
        }

        /// <summary>
        /// Shrinks detection zone back to original radius when exiting combat.
        /// </summary>
        private void ShrinkDetectionZone()
        {
            if (detectionZone != null)
            {
                detectionZone.ResetRadius();
                Debug.Log($"{gameObject.name}: Detection zone reset to original (patrol mode)");
            }
        }

        /// <summary>
        /// Called by ChaseState when player is in attack range.
        /// Rolls for dash attack based on current dash chance (increases with damage taken).
        /// </summary>
        public override void OnPlayerInAttackRange()
        {
            if (!IsAlive) return;

            // Roll for dash attack (chance based on damage taken)
            if (ShouldDashAttack())
            {
                Debug.Log($"{gameObject.name}: In range → DASH ATTACK! (chance was {currentDashChance:P0})");
                stateMachine.ChangeState<ChargeState>();
                return;
            }

            Debug.Log($"{gameObject.name}: In range → MELEE ATTACK!");
            stateMachine.ChangeState<MeleeAttackState>();
        }

        /// <summary>
        /// Gets current health as a percentage (0-1).
        /// </summary>
        private float GetHealthPercent()
        {
            if (attributes != null && attributes.Health != null)
            {
                float current = attributes.Health.Current;
                float max = attributes.Health.Max;
                if (max > 0f)
                    return Mathf.Clamp01(current / max);
            }
            return 1f; // Assume full health if no attributes
        }

        protected override void OnTargetAcquired()
        {
            Debug.Log($"{gameObject.name}: Target acquired!");
        }

        protected override void OnTargetLost()
        {
            Debug.Log($"{gameObject.name}: Target reference lost.");
        }

        #endregion

        #region Decision Helpers

        /// <summary>
        /// Checks if the player is currently in an attacking state.
        /// </summary>
        private bool IsPlayerAttacking()
        {
            if (TargetCharacter == null) return false;

            // Try PlayerState first (more specific)
            var playerState = TargetCharacter.GetComponentInParent<PlayerState>();
            if (playerState != null)
                return playerState.IsAttacking;

            // Fallback to CharacterState
            var charState = TargetCharacter.GetComponent<CharacterState>();
            if (charState != null)
                return charState.IsAttacking;

            return false;
        }

        /// <summary>
        /// Checks if the player is facing the Hyena (attack would be directed at us).
        /// Uses player's localScale.x to determine facing direction.
        /// </summary>
        private bool IsPlayerFacingMe()
        {
            if (Target == null) return false;

            // Get player's facing direction from scale (common 2D/2.5D convention)
            float playerFacing = Mathf.Sign(Target.localScale.x);

            // Get direction from player to hyena
            float directionToHyena = transform.position.x - Target.position.x;

            // Player is facing us if:
            // - Player facing right (scale.x > 0) AND hyena is to the right of player (directionToHyena > 0)
            // - Player facing left (scale.x < 0) AND hyena is to the left of player (directionToHyena < 0)
            return (playerFacing > 0 && directionToHyena > 0) || (playerFacing < 0 && directionToHyena < 0);
        }

        #endregion

        #region Hitbox Handler

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

        private void OnDashHitboxHit(Collider other, Hitbox hitbox)
        {
            hitbox?.Deactivate();

            var damageable = other.GetComponent<IDamageable>();
            if (damageable == null)
                damageable = other.GetComponentInParent<IDamageable>();

            if (damageable == null || !damageable.IsAlive)
                return;

            // Calculate knockback direction (away from hyena)
            Vector3 knockbackDir = (other.transform.position - transform.position).normalized;
            Vector2 directionalKnockback = new Vector2(
                knockbackDir.x * dashKnockback.x,
                dashKnockback.y
            );

            var info = new DamageInfo(dashDamage, gameObject, DamageType.Physical, directionalKnockback);
            damageable.TakeDamage(info);

            Debug.Log($"{gameObject.name} DASH hit {other.name} for {dashDamage} damage!");
        }

        #endregion

        #region VFX Methods (Pooled)

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

            // Patrol range
            if (patrolDistance > 0f)
            {
                Vector3 origin = Application.isPlaying ? spawnPosition : transform.position;
                Vector3 leftPoint = origin + Vector3.left * patrolDistance;
                Vector3 rightPoint = origin + Vector3.right * patrolDistance;

                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(leftPoint, rightPoint);
                Gizmos.DrawWireSphere(leftPoint, 0.3f);
                Gizmos.DrawWireSphere(rightPoint, 0.3f);

                Gizmos.color = Color.green;
                Gizmos.DrawWireCube(origin, Vector3.one * 0.2f);

                Gizmos.color = Color.red;
                Vector3 wallDir = patrolDirection > 0 ? Vector3.right : Vector3.left;
                Gizmos.DrawRay(transform.position, wallDir * wallCheckDistance);
            }

            // Dodge check range (yellow)
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, dodgeCheckRange);

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