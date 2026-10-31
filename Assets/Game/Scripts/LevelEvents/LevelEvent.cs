using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    public enum LevelEventSource
    {
        [Tooltip("Player walks into this object's trigger collider.")]
        TriggerEnter,
        ObjectiveStarted,
        ObjectiveCompleted,
        [Tooltip("The source door finishes opening for the player.")]
        DoorOpened,
        [Tooltip("Runs once the player has spawned in the level.")]
        LevelStart,
        [Tooltip("Only runs when something calls Play() (another event, a UnityEvent, a Timeline signal).")]
        Manual,
        [Tooltip("The source enemy dies.")]
        EnemyDied,
    }

    /// <summary>
    /// Scene-local story beat: when its source fires, runs an ordered list of actions
    /// (objectives, dialogue, timelines, player locks, item grants...).
    /// Owns every player lock its actions take and releases them all when it finishes,
    /// is disabled, or the player dies, so a beat can never leave the player stuck.
    /// </summary>
    [DisallowMultipleComponent]
    public class LevelEvent : MonoBehaviour
    {
        [SerializeField] private LevelEventSource source = LevelEventSource.TriggerEnter;
        [SerializeField] private ObjectiveDefinition sourceObjective;
        [SerializeField] private SlidingDoor sourceDoor;
        [SerializeField] private EnemyCharacter sourceEnemy;
        [Tooltip("Run only the first time the source fires.")]
        [SerializeField] private bool playOnce = true;
        [Tooltip("If the player is fighting when the source fires, wait until combat ends.")]
        [SerializeField] private bool waitUntilOutOfCombat;
        [SerializeReference] private List<LevelEventAction> actions = new();

        private readonly List<IDisposable> leases = new();
        private ObjectiveTracker subscribedTracker;
        private PlayerLifecycle subscribedLifecycle;
        private SlidingDoor subscribedDoor;
        private EnemyCharacter subscribedEnemy;
        private Coroutine running;
        // Separate from the handle: a run with only instant steps finishes inside StartCoroutine.
        private bool isRunning;
        private bool played;

        public LevelEventSource Source => source;
        public ObjectiveDefinition SourceObjective => sourceObjective;
        public SlidingDoor SourceDoor => sourceDoor;
        public EnemyCharacter SourceEnemy => sourceEnemy;
        public IReadOnlyList<LevelEventAction> Actions => actions;
        public bool IsRunning => isRunning;
        public bool HasPlayed => played;
        public PlayerCharacter Player => PlayerLifecycle.Instance != null ? PlayerLifecycle.Instance.Player : null;

        private void Awake()
        {
            if (source == LevelEventSource.TriggerEnter && TryGetComponent(out Collider trigger))
                trigger.isTrigger = true;
        }

        private void OnEnable() => Rebind();

        private void Start()
        {
            Rebind();
            if (source == LevelEventSource.LevelStart && Player != null) Play();
        }

        private void OnDisable()
        {
            Abort();
            Unbind();
        }

        /// <summary>Runs the actions now (respects Play Once). Safe to call from UnityEvents and signals.</summary>
        public void Play()
        {
            if (isRunning || (playOnce && played) || !isActiveAndEnabled) return;
            played = true;
            isRunning = true;
            running = StartCoroutine(Run());
        }

        /// <summary>Stops the running actions and releases every player lock they hold.</summary>
        public void Abort()
        {
            if (running != null) StopCoroutine(running);
            running = null;
            isRunning = false;
            ReleaseLeases();
        }

        private IEnumerator Run()
        {
            if (waitUntilOutOfCombat)
                while (Player == null || Player.IsInCombat) yield return null;

            foreach (LevelEventAction action in actions)
            {
                if (action == null) continue;
                yield return action.Run(this);
            }

            running = null;
            isRunning = false;
            ReleaseLeases();
        }

        #region Player locks

        /// <summary>Locks player input and movement until <see cref="ReleaseLeases"/> (or the event ends).</summary>
        public void LockPlayer(bool freezeMovement = true)
        {
            PlayerCharacter player = Player;
            if (player == null) return;
            if (player.State != null) leases.Add(player.State.AcquireInputLock());
            if (freezeMovement && player.Controller != null) leases.Add(player.Controller.AcquireMovementLock());
        }

        /// <summary>Keeps a lock alive until the event ends or <see cref="ReleaseLeases"/> runs.</summary>
        public void AddLease(IDisposable lease)
        {
            if (lease != null) leases.Add(lease);
        }

        public void ReleaseLeases()
        {
            for (int i = leases.Count - 1; i >= 0; i--) leases[i]?.Dispose();
            leases.Clear();
        }

        #endregion

        #region Sources

        private void OnTriggerEnter(Collider other)
        {
            if (source == LevelEventSource.TriggerEnter && other.CompareTag("Player")) Play();
        }

        private void HandleObjective(ObjectiveDefinition objective, ObjectiveState state)
        {
            if (objective == null || sourceObjective == null || objective.Id != sourceObjective.Id) return;
            if ((source == LevelEventSource.ObjectiveStarted && state == ObjectiveState.Active) ||
                (source == LevelEventSource.ObjectiveCompleted && state == ObjectiveState.Complete))
                Play();
        }

        private void HandleDoorOpened(SlidingDoor door) => Play();

        private void HandleEnemyDied(EnemyCharacter enemy) => Play();

        private void HandlePlayerSpawned(PlayerCharacter player)
        {
            if (source == LevelEventSource.LevelStart) Play();
        }

        private void HandlePlayerDied(PlayerCharacter player) => Abort();

        private void Rebind()
        {
            ObjectiveTracker tracker = ObjectiveTracker.Instance;
            if (tracker != subscribedTracker)
            {
                if (subscribedTracker != null) subscribedTracker.StateChanged -= HandleObjective;
                subscribedTracker = tracker;
                if (subscribedTracker != null) subscribedTracker.StateChanged += HandleObjective;
            }

            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            if (lifecycle != subscribedLifecycle)
            {
                if (subscribedLifecycle != null)
                {
                    subscribedLifecycle.PlayerSpawned -= HandlePlayerSpawned;
                    subscribedLifecycle.PlayerDied -= HandlePlayerDied;
                }
                subscribedLifecycle = lifecycle;
                if (subscribedLifecycle != null)
                {
                    subscribedLifecycle.PlayerSpawned += HandlePlayerSpawned;
                    subscribedLifecycle.PlayerDied += HandlePlayerDied;
                }
            }

            SlidingDoor door = source == LevelEventSource.DoorOpened ? sourceDoor : null;
            if (door != subscribedDoor)
            {
                if (subscribedDoor != null) subscribedDoor.PlayerOpened -= HandleDoorOpened;
                subscribedDoor = door;
                if (subscribedDoor != null) subscribedDoor.PlayerOpened += HandleDoorOpened;
            }

            EnemyCharacter enemy = source == LevelEventSource.EnemyDied ? sourceEnemy : null;
            if (enemy != subscribedEnemy)
            {
                if (subscribedEnemy != null) subscribedEnemy.Died -= HandleEnemyDied;
                subscribedEnemy = enemy;
                if (subscribedEnemy != null) subscribedEnemy.Died += HandleEnemyDied;
            }
        }

        private void Unbind()
        {
            if (subscribedTracker != null) subscribedTracker.StateChanged -= HandleObjective;
            if (subscribedLifecycle != null)
            {
                subscribedLifecycle.PlayerSpawned -= HandlePlayerSpawned;
                subscribedLifecycle.PlayerDied -= HandlePlayerDied;
            }
            if (subscribedDoor != null) subscribedDoor.PlayerOpened -= HandleDoorOpened;
            if (subscribedEnemy != null) subscribedEnemy.Died -= HandleEnemyDied;
            subscribedTracker = null;
            subscribedLifecycle = null;
            subscribedDoor = null;
            subscribedEnemy = null;
        }

        #endregion

        private void OnDrawGizmos()
        {
            if (source != LevelEventSource.TriggerEnter || !TryGetComponent(out BoxCollider box)) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.45f, 0.1f, played ? 0.08f : 0.2f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.9f);
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
