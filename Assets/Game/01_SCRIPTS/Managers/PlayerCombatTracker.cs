using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Tracks how many enemies are currently in combat with the player.
    /// Fires OnCombatStarted when the first enemy locks on, OnCombatEnded when the last one releases.
    /// Used by GameManager (or music controller) to switch between level and combat music.
    /// </summary>
    public class PlayerCombatTracker : MonoBehaviour
    {
        public static PlayerCombatTracker Instance { get; private set; }

        /// <summary>Fired when the count of enemies in combat goes from 0 to 1.</summary>
        public event Action OnCombatStarted;

        /// <summary>Fired when the count of enemies in combat goes from 1 to 0.</summary>
        public event Action OnCombatEnded;

        /// <summary>True if at least one enemy is currently in combat with the player.</summary>
        public bool IsPlayerInCombat => enemiesInCombat.Count > 0;

        private readonly HashSet<EnemyCharacter> enemiesInCombat = new();

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>Call from EnemyCharacter.EnterCombat(). Only counts enemies targeting the current player.</summary>
        public void NotifyEnemyEnteredCombat(EnemyCharacter enemy)
        {
            if (enemy == null) return;

            var player = GameManager.Instance?.Player;
            if (player == null || !enemy.HasTarget || enemy.TargetCharacter != player)
                return;

            int countBefore = enemiesInCombat.Count;
            enemiesInCombat.Add(enemy);
            if (countBefore == 0 && enemiesInCombat.Count == 1)
                OnCombatStarted?.Invoke();
        }

        /// <summary>Call from EnemyCharacter.ExitCombat() or HandleDeath() (before clearing target).</summary>
        public void NotifyEnemyExitedCombat(EnemyCharacter enemy)
        {
            if (enemy == null) return;

            int countBefore = enemiesInCombat.Count;
            enemiesInCombat.Remove(enemy);
            if (countBefore == 1 && enemiesInCombat.Count == 0)
                OnCombatEnded?.Invoke();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
