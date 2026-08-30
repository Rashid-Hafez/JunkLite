using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Decision owner for a composed enemy. Perception reports facts, states execute
    /// actions, and the brain owns voluntary transitions between those actions.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyCharacter))]
    [RequireComponent(typeof(StateMachine))]
    [RequireComponent(typeof(EnemyMovement))]
    public abstract class EnemyBrain : MonoBehaviour
    {
        protected EnemyCharacter Actor { get; private set; }
        protected StateMachine StateMachine { get; private set; }
        protected EnemyMovement Movement { get; private set; }
        protected EnemyPerception Perception { get; private set; }

        private bool initialized;
        private bool perceptionSubscribed;

        protected virtual void Awake()
        {
            Actor = GetComponent<EnemyCharacter>();
            StateMachine = GetComponent<StateMachine>();
            Movement = GetComponent<EnemyMovement>();
            Perception = Actor != null ? Actor.Perception : null;
            if (Perception == null)
                Perception = GetComponentInChildren<EnemyPerception>(true);
        }

        protected virtual void OnEnable()
        {
            SubscribeToPerception();
        }

        protected virtual void Start()
        {
            SubscribeToPerception();
            InitializeStateMachine();
            initialized = true;

            if (Perception != null && Perception.HasTarget)
                OnTargetChanged(null, Perception.CurrentTarget);
        }

        protected virtual void Update()
        {
            if (!initialized || Actor == null || !Actor.IsAlive || Actor.IsTutorialFrozen)
                return;
            if (StateMachine == null || StateMachine.IsPaused)
                return;

            TickBrain();
        }

        protected virtual void OnDisable()
        {
            UnsubscribeFromPerception();
        }

        protected abstract void InitializeStateMachine();
        protected abstract void EvaluateNextAction(bool actionCompleted = false);

        protected virtual void TickBrain() { }

        protected virtual void OnTargetChanged(PlayerCharacter previous, PlayerCharacter current)
        {
            EvaluateNextAction();
        }

        protected void ChangeState<T>() where T : IState
        {
            if (StateMachine == null || StateMachine.IsInState<T>())
                return;

            StateMachine.ChangeState<T>();
        }

        protected bool IsForcedState()
        {
            IState current = StateMachine != null ? StateMachine.CurrentState : null;
            return current is DeadState || current is StunnedState || current is ParriedState;
        }

        private void SubscribeToPerception()
        {
            if (perceptionSubscribed || Perception == null)
                return;

            Perception.TargetChanged += HandleTargetChanged;
            perceptionSubscribed = true;
        }

        private void UnsubscribeFromPerception()
        {
            if (!perceptionSubscribed || Perception == null)
                return;

            Perception.TargetChanged -= HandleTargetChanged;
            perceptionSubscribed = false;
        }

        private void HandleTargetChanged(PlayerCharacter previous, PlayerCharacter current)
        {
            if (!isActiveAndEnabled || !initialized || Actor == null
                || !Actor.isActiveAndEnabled || !Actor.IsAlive
                || Perception == null || !Perception.isActiveAndEnabled)
                return;

            OnTargetChanged(previous, current);
        }
    }
}
