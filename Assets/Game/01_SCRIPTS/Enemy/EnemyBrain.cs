using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Abstract brain for enemy AI. Handles state machine and decision making.
    /// Accesses all components through EnemyCharacter (the hub).
    /// </summary>
    public abstract class EnemyBrain : MonoBehaviour
    {
        [Header("Detection")]
        [SerializeField] protected float detectionRange = 6f;
        [SerializeField] protected float attackRange = 1.5f;
        [SerializeField] protected LayerMask playerLayer;

        [Header("Timing")]
        [SerializeField] protected float alertDuration = 0.2f;
        [SerializeField] protected float recoverDuration = 0.4f;

        // Central hub - all access goes through here
        protected EnemyCharacter enemy;

        // Quick accessors (for cleaner code in derived classes)
        protected EnemyController Controller => enemy.Controller;
        protected EnemyAttackHandler AttackHandler => enemy.AttackHandler;
        protected CharacterState State => enemy.State;

        protected Transform player;
        protected EnemyBrainState currentState;
        protected bool brainEnabled;

        // Public accessors
        public EnemyBrainState CurrentState => currentState;
        public bool IsEnabled => brainEnabled;
        public float DetectionRange => detectionRange;
        public float AttackRange => attackRange;

        // ================= INIT =================
        public virtual void Initialize(EnemyCharacter owner)
        {
            enemy = owner;
            currentState = EnemyBrainState.Idle;
        }

        public void EnableBrain(bool enabled)
        {
            brainEnabled = enabled;

            if (enabled)
            {
                // Force transition to Idle which will then go to Patrol
                currentState = EnemyBrainState.Idle;
            }
            else
            {
                SetState(EnemyBrainState.Idle);
                Controller?.Stop();
            }
        }

        protected virtual void Update()
        {
            if (!brainEnabled || enemy == null)
                return;

            // Skip IsAlive check for debugging - add back later if needed
            // if (!enemy.IsAlive) return;

            Think();
        }

        // ================= THINK =================
        protected virtual void Think()
        {
            switch (currentState)
            {
                case EnemyBrainState.Idle:
                    TickIdle();
                    break;
                case EnemyBrainState.Patrol:
                    TickPatrol();
                    break;
                case EnemyBrainState.Alert:
                    TickAlert();
                    break;
                case EnemyBrainState.Chase:
                    TickChase();
                    break;
                case EnemyBrainState.Attack:
                    TickAttack();
                    break;
                case EnemyBrainState.Recover:
                    TickRecover();
                    break;
                case EnemyBrainState.Stunned:
                    TickStunned();
                    break;
                case EnemyBrainState.Retreat:
                    TickRetreat();
                    break;
            }
        }

        // ================= STATES (Override in derived) =================
        protected abstract void TickIdle();
        protected abstract void TickPatrol();
        protected abstract void TickAlert();
        protected abstract void TickChase();
        protected abstract void TickAttack();
        protected abstract void TickRecover();
        protected abstract void TickStunned();
        protected virtual void TickRetreat() { }

        // ================= HELPERS =================
        protected bool TryFindPlayer()
        {
            if (player != null) return true;

            var playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                player = playerObj.transform;
                enemy.SetTarget(player);
                return true;
            }
            return false;
        }

        protected bool IsPlayerInRange(float range)
        {
            if (player == null) return false;
            return DistanceToPlayer() <= range;
        }

        protected float DistanceToPlayer()
        {
            if (player == null) return float.MaxValue;
            return Vector3.Distance(transform.position, player.position);
        }

        protected int DirectionToPlayer()
        {
            if (player == null) return 0;
            return player.position.x > transform.position.x ? 1 : -1;
        }

        protected void FacePlayer()
        {
            if (Controller == null || player == null) return;
            Controller.FaceTarget(player.position);
        }

        protected void MoveTowardPlayer()
        {
            if (Controller == null || player == null) return;
            Controller.MoveToward(player.position);
        }

        protected void MoveAwayFromPlayer()
        {
            if (Controller == null || player == null) return;
            Controller.MoveAway(player.position);
        }

        protected void StopMoving()
        {
            Controller?.Stop();
        }

        protected void SetState(EnemyBrainState newState)
        {
            if (currentState == newState) return;

            OnExitState(currentState);
            currentState = newState;
            OnEnterState(newState);
        }

        protected virtual void OnEnterState(EnemyBrainState newState) { }
        protected virtual void OnExitState(EnemyBrainState oldState) { }

        // ================= EVENTS =================
        public virtual void OnDamaged(DamageInfo info)
        {
            if (!enemy.IsAlive) return;

            // Find who hit us
            if (info.Source != null && player == null)
            {
                player = info.Source.transform;
                enemy.SetTarget(player);
            }

            SetState(EnemyBrainState.Stunned);
        }
    }

    public enum EnemyBrainState
    {
        Idle,       // Doing nothing
        Patrol,     // Walking between points
        Alert,      // Just spotted player, deciding action
        Chase,      // Moving toward player
        Attack,     // Performing an attack
        Recover,    // Brief pause after action
        Stunned,    // Hit by player, temporarily disabled
        Retreat     // Moving away from player
    }
}