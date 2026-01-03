using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Base class for all enemy states.
    /// Provides access to the enemy context and common functionality.
    /// </summary>
    public abstract class EnemyStateBase : IState
    {
        protected readonly EnemyCharacter enemy;
        protected readonly StateMachine stateMachine;

        // Quick accessors for common properties
        protected Transform Transform => enemy.transform;
        protected Transform Target => enemy.Target;
        protected bool HasTarget => enemy.HasTarget;
        protected bool IsTargetInAttackRange => enemy.IsTargetInAttackRange;
        protected float DistanceToTarget => enemy.DistanceToTarget;
        protected bool IsAlive => enemy.IsAlive;

        protected EnemyStateBase(EnemyCharacter enemy)
        {
            this.enemy = enemy;
            this.stateMachine = enemy.StateMachine;
        }

        /// <summary>
        /// Whether the enemy can take damage while in this state.
        /// Override to return false for invulnerability frames or immunity states.
        /// Default is true.
        /// </summary>
        public virtual bool CanTakeDamage => true;

        public virtual void Enter() { }

        public virtual void Update() { }

        public virtual void FixedUpdate() { }

        public virtual void Exit() { }

        /// <summary>
        /// Helper to transition to another state.
        /// </summary>
        protected void ChangeState<T>() where T : IState
        {
            stateMachine.ChangeState<T>();
        }
    }
}