using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Scene-local list of level objectives and which ones the player has completed.
    /// Completion is idempotent: finishing an objective twice changes nothing and raises no event.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ObjectiveTracker : MonoBehaviour
    {
        public static ObjectiveTracker Instance { get; private set; }

        [Tooltip("Every objective this level can show. Order is applied by sortOrder in the Missions tab.")]
        [SerializeField] private List<ObjectiveDefinition> objectives = new();
        [Tooltip("The goal the Missions tab highlights as the main objective.")]
        [SerializeField] private ObjectiveDefinition primary;

        private readonly HashSet<string> completed = new();

        public event Action Changed;

        public ObjectiveDefinition Primary => primary;
        public IReadOnlyList<ObjectiveDefinition> Objectives => objectives;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool IsComplete(ObjectiveDefinition objective) => objective != null && completed.Contains(objective.Id);

        /// <summary>Marks the objective complete. Returns false if it was null or already complete.</summary>
        public bool Complete(ObjectiveDefinition objective)
        {
            if (objective == null || !completed.Add(objective.Id)) return false;
            Changed?.Invoke();
            return true;
        }
    }
}
