using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Generic finite state machine component.
    /// Manages state registration, transitions, and lifecycle.
    /// </summary>
    public class StateMachine : MonoBehaviour
    {
        [Header("Debug")]
        [SerializeField] private bool logTransitions = false;

        private Dictionary<Type, IState> states = new Dictionary<Type, IState>();
        private IState currentState;
        private IState previousState;
        private bool isPaused;

        // Events
        public event Action<IState, IState> OnStateChanged; // (from, to)

        // Public accessors
        public IState CurrentState => currentState;
        public IState PreviousState => previousState;
        public bool IsPaused => isPaused;
        public string CurrentStateName => currentState?.GetType().Name ?? "None";

        private void Update()
        {
            if (isPaused) return;
            currentState?.Update();
        }

        private void FixedUpdate()
        {
            if (isPaused) return;
            currentState?.FixedUpdate();
        }

        /// <summary>
        /// Register a state instance with the state machine.
        /// </summary>
        public void RegisterState(IState state)
        {
            var type = state.GetType();
            if (states.ContainsKey(type))
            {
                Debug.LogWarning($"State {type.Name} already registered. Replacing.");
            }
            states[type] = state;
        }

        /// <summary>
        /// Register multiple states at once.
        /// </summary>
        public void RegisterStates(params IState[] statesToRegister)
        {
            foreach (var state in statesToRegister)
            {
                RegisterState(state);
            }
        }

        /// <summary>
        /// Get a registered state by type.
        /// </summary>
        public T GetState<T>() where T : class, IState
        {
            if (states.TryGetValue(typeof(T), out var state))
            {
                return state as T;
            }
            return null;
        }

        /// <summary>
        /// Check if currently in a specific state.
        /// </summary>
        public bool IsInState<T>() where T : IState
        {
            return currentState?.GetType() == typeof(T);
        }

        /// <summary>
        /// Change to a new state by type.
        /// </summary>
        public void ChangeState<T>() where T : IState
        {
            var type = typeof(T);
            if (!states.TryGetValue(type, out var newState))
            {
                Debug.LogError($"State {type.Name} not registered!");
                return;
            }

            ChangeState(newState);
        }

        /// <summary>
        /// Change to a new state instance.
        /// </summary>
        public void ChangeState(IState newState)
        {
            if (newState == null)
            {
                Debug.LogError("Cannot change to null state!");
                return;
            }

            // Don't transition to the same state
            if (currentState == newState) return;

            if (logTransitions)
            {
                Debug.Log($"[FSM] {gameObject.name}: {currentState?.GetType().Name ?? "None"} -> {newState.GetType().Name}");
            }

            // Exit current state
            previousState = currentState;
            currentState?.Exit();

            // Enter new state
            currentState = newState;
            currentState.Enter();

            // IMPORTANT: This is where the state machine is notified that the state has changed. 
            // AND WE CAN CHANGE ANIMATIONS HERE.  
            
            // example for EnemyAnimationController:
            // private void OnEnable()
            // {
            //     if (driveFromStateMachine && stateMachine != null)
            //         stateMachine.OnStateChanged += HandleStateChanged;
            // }

            OnStateChanged?.Invoke(previousState, currentState); 
            ///------------------------------------------------------------------------------------------------
        }

        /// <summary>
        /// Return to the previous state.
        /// </summary>
        public void RevertToPreviousState()
        {
            if (previousState != null)
            {
                ChangeState(previousState);
            }
        }

        /// <summary>
        /// Set the initial state (use during initialization).
        /// </summary>
        public void SetInitialState<T>() where T : IState
        {
            var type = typeof(T);
            if (!states.TryGetValue(type, out var initialState))
            {
                Debug.LogError($"Initial state {type.Name} not registered!");
                return;
            }

            if (logTransitions)
            {
                Debug.Log($"[FSM] {gameObject.name}: Initial state -> {type.Name}");
            }

            currentState = initialState;
            currentState.Enter();
        }

        /// <summary>
        /// Pause the state machine (stops Update/FixedUpdate calls).
        /// </summary>
        public void Pause()
        {
            isPaused = true;
        }

        /// <summary>
        /// Resume the state machine.
        /// </summary>
        public void Resume()
        {
            isPaused = false;
        }

        /// <summary>
        /// Force exit from current state (cleanup).
        /// </summary>
        public void Stop()
        {
            currentState?.Exit();
            currentState = null;
            previousState = null;
        }

        private void OnDestroy()
        {
            Stop();
            states.Clear();
        }
    }
}