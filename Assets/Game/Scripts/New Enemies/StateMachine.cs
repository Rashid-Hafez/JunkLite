using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Generic finite state machine component.
    /// Manages state registration, transitions, and lifecycle.
    /// </summary>
    public class StateMachine : MonoBehaviour
    {
        private Dictionary<Type, IState> states = new Dictionary<Type, IState>();
        private IState currentState;
        private IState previousState;
        private bool isPaused;

        public event Action<IState, IState> OnStateChanged; // (from, to)

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

        public void RegisterState(IState state)
        {
            var type = state.GetType();
            if (states.ContainsKey(type))
                Debug.LogWarning($"State {type.Name} already registered. Replacing.");
            states[type] = state;
        }

        public void RegisterStates(params IState[] statesToRegister)
        {
            foreach (var state in statesToRegister)
                RegisterState(state);
        }

        public T GetState<T>() where T : class, IState
        {
            if (states.TryGetValue(typeof(T), out var state))
                return state as T;
            return null;
        }

        public bool HasState<T>() where T : class, IState
        {
            return states.ContainsKey(typeof(T));
        }

        public bool IsInState<T>() where T : IState
        {
            return currentState?.GetType() == typeof(T);
        }

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

        public void ChangeState(IState newState)
        {
            if (newState == null)
            {
                Debug.LogError("Cannot change to null state!");
                return;
            }

            if (currentState == newState) return;

            previousState = currentState;
            currentState?.Exit();

            currentState = newState;
            currentState.Enter();

            OnStateChanged?.Invoke(previousState, currentState);
        }

        public void RevertToPreviousState()
        {
            if (previousState != null)
                ChangeState(previousState);
        }

        public void SetInitialState<T>() where T : IState
        {
            var type = typeof(T);
            if (!states.TryGetValue(type, out var initialState))
            {
                Debug.LogError($"Initial state {type.Name} not registered!");
                return;
            }

            currentState = initialState;
            currentState.Enter();
        }

        public void Pause() => isPaused = true;
        public void Resume() => isPaused = false;

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
