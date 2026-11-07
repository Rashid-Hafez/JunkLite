using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    public enum ObjectiveState
    {
        Inactive,
        Active,
        Complete,
    }

    /// <summary>
    /// Scene-local list of level objectives and their per-run state (Inactive → Active → Complete).
    /// Begin and Complete are idempotent: repeating them changes nothing and raises no event.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectiveTracker : MonoBehaviour
    {
        public static ObjectiveTracker Instance { get; private set; }

        [Tooltip("Every objective this level can show. Order is applied by sortOrder in the Missions tab.")]
        [SerializeField] private List<ObjectiveDefinition> objectives = new();
        [Tooltip("The goal the Missions tab highlights as the main objective.")]
        [SerializeField] private ObjectiveDefinition primary;
        [Tooltip("Objectives that are already active when the level loads.")]
        [SerializeField] private List<ObjectiveDefinition> startActive = new();

        private readonly HashSet<string> completed = new();
        private readonly HashSet<string> active = new();
        // Most recently begun last, so the HUD follows the newest goal.
        private readonly List<ObjectiveDefinition> activeOrder = new();

        /// <summary>Any state change. Kept for simple listeners (doors, Missions tab).</summary>
        public event Action Changed;
        /// <summary>Raised with the objective and its new state.</summary>
        public event Action<ObjectiveDefinition, ObjectiveState> StateChanged;

        public ObjectiveDefinition Primary => primary;
        public IReadOnlyList<ObjectiveDefinition> Objectives => objectives;

        /// <summary>The objective the HUD shows: the newest active one, else the primary while it is unfinished.</summary>
        public ObjectiveDefinition Current
        {
            get
            {
                if (activeOrder.Count > 0) return activeOrder[^1];
                return primary != null && !IsComplete(primary) ? primary : null;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            foreach (ObjectiveDefinition objective in startActive)
                Begin(objective);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public ObjectiveState GetState(ObjectiveDefinition objective)
        {
            if (objective == null) return ObjectiveState.Inactive;
            if (completed.Contains(objective.Id)) return ObjectiveState.Complete;
            return active.Contains(objective.Id) ? ObjectiveState.Active : ObjectiveState.Inactive;
        }

        public bool IsComplete(ObjectiveDefinition objective) => objective != null && completed.Contains(objective.Id);
        public bool IsActive(ObjectiveDefinition objective) => GetState(objective) == ObjectiveState.Active;

        /// <summary>Makes an inactive objective active. Returns false if null, already active or complete.</summary>
        public bool Begin(ObjectiveDefinition objective)
        {
            if (objective == null || completed.Contains(objective.Id) || !active.Add(objective.Id)) return false;
            activeOrder.Add(objective);
            Raise(objective, ObjectiveState.Active);
            return true;
        }

        /// <summary>Marks the objective complete. Returns false if it was null or already complete.</summary>
        public bool Complete(ObjectiveDefinition objective)
        {
            if (objective == null || !completed.Add(objective.Id)) return false;
            active.Remove(objective.Id);
            activeOrder.Remove(objective);
            Raise(objective, ObjectiveState.Complete);
            return true;
        }

        private void Raise(ObjectiveDefinition objective, ObjectiveState state)
        {
            StateChanged?.Invoke(objective, state);
            Changed?.Invoke();
        }
    }
}
