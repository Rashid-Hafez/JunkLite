using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Brain for Robot Enemy.
    /// Behavior: Patrol -> Spot Player -> Chase/Attack -> Recover -> Repeat
    /// Future: Dash attack, spin attack, combo, grenade
    /// </summary>
    public class RobotBrain : EnemyBrain
    {
        [Header("Robot - Patrol")]
        [SerializeField] private float patrolDistance = 5f;
        [SerializeField] private float patrolWaitTime = 1f;

        [Header("Robot - Combat")]
        [SerializeField] private float chaseSpeed = 4f;
        [SerializeField] private float stunDuration = 0.3f;

        // Patrol state
        private Vector3 patrolOrigin;
        private int patrolDirection = 1;
        private float patrolWaitTimer;

        // State timers
        private float stateTimer;

        // ================= INIT =================
        public override void Initialize(EnemyCharacter owner)
        {
            base.Initialize(owner);
            patrolOrigin = transform.position;
        }

        // ================= STATE ENTER/EXIT =================
        protected override void OnEnterState(EnemyBrainState newState)
        {
            stateTimer = 0f;
            Debug.Log($"[RobotBrain] Entering state: {newState}");

            switch (newState)
            {
                case EnemyBrainState.Idle:
                    StopMoving();
                    break;

                case EnemyBrainState.Patrol:
                    patrolWaitTimer = 0f;
                    break;

                case EnemyBrainState.Alert:
                    StopMoving();
                    FacePlayer();
                    break;

                case EnemyBrainState.Chase:
                    break;

                case EnemyBrainState.Attack:
                    StopMoving();
                    FacePlayer();
                    AttackHandler?.TryAttack();
                    break;

                case EnemyBrainState.Recover:
                    StopMoving();
                    break;

                case EnemyBrainState.Stunned:
                    StopMoving();
                    AttackHandler?.CancelAttack();
                    break;
            }
        }

        // ================= IDLE =================
        protected override void TickIdle()
        {
            Debug.Log("[RobotBrain] TickIdle - transitioning to Patrol");
            // Try to find player, then start patrolling
            TryFindPlayer();
            SetState(EnemyBrainState.Patrol);
        }

        // ================= PATROL =================
        protected override void TickPatrol()
        {
            // Check for player
            if (IsPlayerInRange(detectionRange))
            {
                SetState(EnemyBrainState.Alert);
                return;
            }

            // Waiting at patrol point?
            if (patrolWaitTimer > 0f)
            {
                patrolWaitTimer -= Time.deltaTime;
                StopMoving();
                return;
            }

            // Check for wall or ledge - turn around
            if (Controller.ShouldTurnAround)
            {
                patrolDirection *= -1;
                patrolWaitTimer = patrolWaitTime;
                StopMoving();
                return;
            }

            // Move in patrol direction
            Controller.SetMoveInput(patrolDirection);

            // Check if reached patrol boundary
            float distanceFromOrigin = transform.position.x - patrolOrigin.x;

            if (Mathf.Abs(distanceFromOrigin) >= patrolDistance)
            {
                // Turn around
                patrolDirection *= -1;
                patrolWaitTimer = patrolWaitTime;
            }
        }

        // ================= ALERT =================
        protected override void TickAlert()
        {
            stateTimer += Time.deltaTime;

            FacePlayer();

            // Brief pause before action
            if (stateTimer >= alertDuration)
            {
                DecideNextAction();
            }
        }

        // ================= CHASE =================
        protected override void TickChase()
        {
            // Lost player?
            if (player == null)
            {
                SetState(EnemyBrainState.Patrol);
                return;
            }

            // In attack range?
            if (IsPlayerInRange(attackRange))
            {
                SetState(EnemyBrainState.Attack);
                return;
            }

            // Too far? Give up chase
            if (!IsPlayerInRange(detectionRange * 1.5f))
            {
                SetState(EnemyBrainState.Patrol);
                return;
            }

            // Chase player
            MoveTowardPlayer();
        }

        // ================= ATTACK =================
        protected override void TickAttack()
        {
            // Wait for attack to finish
            if (AttackHandler == null || !AttackHandler.IsAttacking)
            {
                SetState(EnemyBrainState.Recover);
            }
        }

        // ================= RECOVER =================
        protected override void TickRecover()
        {
            stateTimer += Time.deltaTime;

            if (stateTimer >= recoverDuration)
            {
                // Can still see player?
                if (IsPlayerInRange(detectionRange))
                {
                    SetState(EnemyBrainState.Alert);
                }
                else
                {
                    SetState(EnemyBrainState.Patrol);
                }
            }
        }

        // ================= STUNNED =================
        protected override void TickStunned()
        {
            stateTimer += Time.deltaTime;

            if (stateTimer >= stunDuration)
            {
                // After stun, become alert if player nearby
                if (IsPlayerInRange(detectionRange))
                {
                    SetState(EnemyBrainState.Alert);
                }
                else
                {
                    SetState(EnemyBrainState.Patrol);
                }
            }
        }

        // ================= DECISION MAKING =================
        private void DecideNextAction()
        {
            float distance = DistanceToPlayer();

            // In attack range -> Attack
            if (distance <= attackRange)
            {
                SetState(EnemyBrainState.Attack);
            }
            // Not in range -> Chase
            else
            {
                SetState(EnemyBrainState.Chase);
            }

            // Future: Add RNG for dash attack, spin attack, grenade, combo
        }

        // ================= DEBUG =================
        private void OnDrawGizmosSelected()
        {
            // Patrol range
            Vector3 origin = Application.isPlaying ? patrolOrigin : transform.position;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(origin + Vector3.left * patrolDistance, origin + Vector3.right * patrolDistance);

            // Attack range
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);

            // Detection range
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRange);
        }
    }
}