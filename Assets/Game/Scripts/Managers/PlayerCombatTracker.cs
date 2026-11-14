using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Tracks how many enemies are currently in combat with the player.
    /// Fires OnCombatStarted when the first enemy locks on, OnCombatEnded when the last one releases.
    /// Tracks the current player through PlayerLifecycle; there is no GameManager dependency.
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
        private PlayerCharacter cachedPlayer;
        private PlayerLifecycle subscribedPlayerLifecycle;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Do not destroy a composed GameRoot because one hosted service
                // was duplicated by a legacy scene object.
                enabled = false;
                Destroy(this);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            RebindPlayerLifecycle();
        }

        private void Start()
        {
            // PlayerLifecycle can be added by GameManager.Awake for an unmigrated scene.
            RebindPlayerLifecycle();
        }

        private void OnDisable()
        {
            UnsubscribeFromPlayerLifecycle();
        }

        /// <summary>
        /// Returns the lifecycle-owned player, with a scene lookup only for
        /// compatibility with an unmigrated bootstrap.
        /// </summary>
        private PlayerCharacter GetPlayer()
        {
            if (cachedPlayer != null && cachedPlayer.IsAlive)
                return cachedPlayer;

            RebindPlayerLifecycle();
            if (subscribedPlayerLifecycle?.Player != null)
            {
                cachedPlayer = subscribedPlayerLifecycle.Player;
                return cachedPlayer;
            }

            cachedPlayer = FindFirstObjectByType<PlayerCharacter>();
            return cachedPlayer;
        }

        private void RebindPlayerLifecycle()
        {
            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            if (subscribedPlayerLifecycle == lifecycle)
                return;

            UnsubscribeFromPlayerLifecycle();
            subscribedPlayerLifecycle = lifecycle;

            if (subscribedPlayerLifecycle == null)
                return;

            subscribedPlayerLifecycle.PlayerSpawned += HandlePlayerSpawned;
            subscribedPlayerLifecycle.PlayerDied += HandlePlayerDied;

            if (subscribedPlayerLifecycle.Player != null)
                cachedPlayer = subscribedPlayerLifecycle.Player;
        }

        private void UnsubscribeFromPlayerLifecycle()
        {
            if (subscribedPlayerLifecycle == null)
                return;

            subscribedPlayerLifecycle.PlayerSpawned -= HandlePlayerSpawned;
            subscribedPlayerLifecycle.PlayerDied -= HandlePlayerDied;
            subscribedPlayerLifecycle = null;
        }

        private void HandlePlayerSpawned(PlayerCharacter player)
        {
            ClearCombatState();
            cachedPlayer = player;
        }

        private void HandlePlayerDied(PlayerCharacter player)
        {
            ClearCombatState();
        }

        /// <summary>Call from EnemyCharacter.EnterCombat(). Only counts enemies targeting the current player.</summary>
        public void NotifyEnemyEnteredCombat(EnemyCharacter enemy)
        {
            if (enemy == null) return;

            var player = GetPlayer();
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

        /// <summary>
        /// Call when the scene reloads or a new player spawns to clear stale references.
        /// PlayerLifecycle calls this before publishing a newly spawned player.
        /// </summary>
        public void ClearCombatState()
        {
            bool wasInCombat = enemiesInCombat.Count > 0;
            enemiesInCombat.Clear();
            cachedPlayer = null;

            if (wasInCombat)
                OnCombatEnded?.Invoke();
        }

        void OnDestroy()
        {
            UnsubscribeFromPlayerLifecycle();

            if (Instance == this)
                Instance = null;
        }
    }
}
