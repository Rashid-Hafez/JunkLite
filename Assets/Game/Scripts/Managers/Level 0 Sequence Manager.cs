using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace junklite
{
    [DefaultExecutionOrder(2)]
    public class Level0SequenceManager : MonoBehaviour
    {
        // ====================================================================
        // INSPECTOR
        // ====================================================================

        [Header("Reposition")]
        [Tooltip("Where the player is moved after objective 1 (center of room).")]
        [SerializeField] private Transform centerRoomSpawn;

        [Header("Lighting")]
        [Tooltip("Scene spotlight (e.g. overhead) — enabled after intro dialogue, disabled after director finishes.")]
        [SerializeField] private Light overheadSpotlight;
        [Tooltip("Optional god-ray particle system that starts after intro dialogue and fades by stopping emission after the director finishes.")]
        [SerializeField] private ParticleSystem godRayParticles;
        [Tooltip("How long it takes for the god-ray particles to fade out after the director starts.")]
        [SerializeField] private float godRayFadeDuration = 1f;
        [Tooltip("How long it takes to ramp the environment reflection intensity to full during the reveal.")]
        [SerializeField] private float reflectionRevealDuration = 1f;

        [Header("Director / Timeline")]
        [SerializeField] private PlayableDirector director;
        [Tooltip("Optional director for the reusable 'platform complete' beat played after each obstacle course.")]
        [SerializeField] private PlayableDirector platformCompleteDirector;

        [Header("Dialogue")]
        [Tooltip("Played immediately after the player spawns.")]
        [SerializeField] private DialogueSequence introDialogue;
        [Tooltip("Played after the cinematic timeline finishes, before platform trial 1.")]
        [SerializeField] private DialogueSequence postCinematicDialogue;
        [Tooltip("Played after platform trial 1, before platform trial 2 starts.")]
        [SerializeField] private DialogueSequence secondPlatformDialogue;
        [Tooltip("Played after platform trial 2, before the combat wave spawns.")]
        [SerializeField] private DialogueSequence combatIntroDialogue;
        [Tooltip("Played after the final objective, before combat/inventory onboarding.")]
        [SerializeField] private DialogueSequence finalDialogue;

        [Header("Platform Steps (Timeline-controlled)")]
        [Tooltip("First dissolve platform group — activated before objective 1.")]
        [SerializeField] private GameObject platformStep1;
        [Tooltip("Second dissolve platform group — activated before objective 2.")]
        [SerializeField] private GameObject platformStep2;

        [Header("Objectives (SequenceTrigger colliders)")]
        [SerializeField] private SequenceTrigger objective1Trigger;
        [SerializeField] private SequenceTrigger objective2Trigger;
        [SerializeField] private SequenceTrigger finalObjectiveTrigger;

        [Header("Enemies")]
        [SerializeField] private GameObject[] enemyPrefabs;
        [SerializeField] private Transform[] enemySpawnPoints;

        [Header("Scene Transition")]
        [SerializeField] private string nextSceneName;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = true;

        // ====================================================================
        // RUNTIME STATE
        // ====================================================================

        private enum Stage
        {
            WaitingForPlayer,
            IntroDialogue,
            PlayTimeline,
            TutorialDialogue,
            ObjectiveOne,
            PlatformOneComplete,
            ResetForPlatformTwo,
            SecondPlatformDialogue,
            ObjectiveTwo,
            PlatformTwoComplete,
            ResetForCombat,
            CombatIntroDialogue,
            EnemyWave,
            FinalObjective,
            FinalDialogue,
            WaitCombatMode,
            WaitInventoryOpen,
            LoadNextScene,
            Done
        }

        private Stage currentStage;
        private PlayerCharacter currentPlayer;
        private Light playerLight;
        private Coroutine godRayFadeRoutine;

        private readonly List<EnemyCharacter> spawnedEnemies = new();
        private int enemiesAlive;

        private bool dialogueFinished;
        private bool timelineFinished;
        private bool objectiveHit;
        private bool combatModeToggled;
        private bool inventoryOpened;

        // ====================================================================
        // STARTUP
        // ====================================================================

        private void Awake()
        {
            Time.timeScale = 1f;

            if (platformStep1 != null) platformStep1.SetActive(false);
            if (platformStep2 != null) platformStep2.SetActive(false);

            if (overheadSpotlight != null) overheadSpotlight.enabled = false;
            if (godRayParticles != null)
            {
                godRayParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void Start()
        {
            StartCoroutine(RunSequence());
        }

        // ====================================================================
        // MAIN SEQUENCE COROUTINE
        // ====================================================================

        private IEnumerator RunSequence()
        {
            // 1 — Wait for GameManager to spawn the player
            SetStage(Stage.WaitingForPlayer);
            yield return WaitForPlayer();

            // Grab and disable the player's built-in light so they start as a silhouette
            playerLight = currentPlayer != null
                ? currentPlayer.GetComponentInChildren<Light>(true)
                : null;
            if (playerLight != null) playerLight.enabled = false;

            // 2 — Intro dialogue (player is a silhouette, scene is dark)
            SetStage(Stage.IntroDialogue);
            if (introDialogue != null)
                yield return RunDialogue(introDialogue);

            // Reveal: turn on both the player light and overhead spotlight
            if (playerLight != null) playerLight.enabled = true;
            if (overheadSpotlight != null) overheadSpotlight.enabled = true;
            if (godRayParticles != null)
            {
                godRayParticles.gameObject.SetActive(true);
                godRayParticles.Play(true);
            }
            StartCoroutine(LerpReflectionIntensity(RenderSettings.reflectionIntensity, 1f, reflectionRevealDuration));

            // 3 — Play director timeline (dolly zoom corridor)
            SetStage(Stage.PlayTimeline);
            if (godRayParticles != null)
            {
                if (godRayFadeRoutine != null)
                    StopCoroutine(godRayFadeRoutine);
                godRayFadeRoutine = StartCoroutine(FadeOutGodRayParticles());
            }
            if (director != null)
                yield return RunDirector(director, rewindToStart: true);

            // Director done — any remaining reveal particles have already been faded out.

            // 4 — Post-cinematic tutorial dialogue
            SetStage(Stage.TutorialDialogue);
            if (postCinematicDialogue != null)
                yield return RunDialogue(postCinematicDialogue);

            // 5 — Activate first platform step, wait for objective 1
            SetStage(Stage.ObjectiveOne);
            if (platformStep1 != null) platformStep1.SetActive(true);
            if (objective1Trigger != null)
                yield return WaitForTrigger(objective1Trigger);

            // 6 — Play completion beat for platform 1
            SetStage(Stage.PlatformOneComplete);
            if (platformCompleteDirector != null)
                yield return RunDirector(platformCompleteDirector, rewindToStart: true);

            // 7 — Reposition player to the shared reset marker
            SetStage(Stage.ResetForPlatformTwo);
            RepositionPlayer(centerRoomSpawn);
            yield return null;

            // 8 — Explain the second platform lesson
            SetStage(Stage.SecondPlatformDialogue);
            if (secondPlatformDialogue != null)
                yield return RunDialogue(secondPlatformDialogue);

            // 9 — Activate second platform step, wait for objective 2
            SetStage(Stage.ObjectiveTwo);
            if (platformStep2 != null) platformStep2.SetActive(true);
            if (objective2Trigger != null)
                yield return WaitForTrigger(objective2Trigger);

            // 10 — Play completion beat for platform 2
            SetStage(Stage.PlatformTwoComplete);
            if (platformCompleteDirector != null)
                yield return RunDirector(platformCompleteDirector, rewindToStart: true);

            // 11 — Reposition player to the same reset marker before combat
            SetStage(Stage.ResetForCombat);
            RepositionPlayer(centerRoomSpawn);
            yield return null;

            // 12 — Explain the combat tutorial before enemies appear
            SetStage(Stage.CombatIntroDialogue);
            if (combatIntroDialogue != null)
                yield return RunDialogue(combatIntroDialogue);

            // 13 — Spawn enemy wave, wait until all dead
            SetStage(Stage.EnemyWave);
            SpawnEnemyWave();
            yield return WaitForAllEnemiesDead();

            // 14 — Final objective
            SetStage(Stage.FinalObjective);
            if (finalObjectiveTrigger != null)
                yield return WaitForTrigger(finalObjectiveTrigger);

            // 15 — Final dialogue
            SetStage(Stage.FinalDialogue);
            if (finalDialogue != null)
                yield return RunDialogue(finalDialogue);

            // 16 — Wait for combat mode toggle (Q)
            SetStage(Stage.WaitCombatMode);
            yield return WaitForCombatModeToggle();

            // 17 — Wait for inventory open
            SetStage(Stage.WaitInventoryOpen);
            yield return WaitForInventoryOpen();

            // 18 — Load next scene
            SetStage(Stage.LoadNextScene);
            LoadConfiguredScene();
        }

        private void SetStage(Stage s)
        {
            currentStage = s;
            Debug.Log($"[Level0Sequence] >>> {s}");
        }

        // ====================================================================
        // PLAYER — obtained from GameManager, not spawned here
        // ====================================================================

        private IEnumerator WaitForPlayer()
        {
            if (GameManager.Instance != null && GameManager.Instance.Player != null)
            {
                currentPlayer = GameManager.Instance.Player;
                Debug.Log("[Level0Sequence] Player already exists");
                yield break;
            }

            bool received = false;
            void OnSpawned(PlayerCharacter p)
            {
                currentPlayer = p;
                received = true;
            }

            if (GameManager.Instance != null)
                GameManager.Instance.OnPlayerSpawned += OnSpawned;

            while (!received)
                yield return null;

            if (GameManager.Instance != null)
                GameManager.Instance.OnPlayerSpawned -= OnSpawned;

            Debug.Log("[Level0Sequence] Player received from GameManager");
        }

        private void RepositionPlayer(Transform spawnPoint)
        {
            RefreshPlayerRef();
            if (currentPlayer == null || spawnPoint == null) return;

            Vector3 pos = spawnPoint.position;
            currentPlayer.ReviveAt(pos);
            currentPlayer.Activate();

            if (CameraManager.Instance != null)
                CameraManager.Instance.ConnectToPlayer(currentPlayer);

            Debug.Log($"[Level0Sequence] Player repositioned to {pos}");
        }

        private void RefreshPlayerRef()
        {
            if (GameManager.Instance != null && GameManager.Instance.Player != null)
                currentPlayer = GameManager.Instance.Player;
        }

        // ====================================================================
        // DIALOGUE HELPERS
        // ====================================================================

        private IEnumerator RunDialogue(DialogueSequence sequence)
        {
            dialogueFinished = false;

            DialogueManager.instance.OnDialogueEnded += OnDialogueEnded;
            DialogueManager.instance.StartDialogue(sequence);

            while (!dialogueFinished)
                yield return null;

            DialogueManager.instance.OnDialogueEnded -= OnDialogueEnded;
        }

        private void OnDialogueEnded()
        {
            dialogueFinished = true;
        }

        // ====================================================================
        // TIMELINE HELPERS
        // ====================================================================

        private IEnumerator RunDirector(PlayableDirector targetDirector, bool rewindToStart)
        {
            timelineFinished = false;
            if (targetDirector == null)
                yield break;

            if (rewindToStart)
            {
                targetDirector.time = 0;
                targetDirector.Evaluate();
            }

            targetDirector.stopped += OnTimelineStopped;
            targetDirector.Play();

            while (!timelineFinished)
                yield return null;

            targetDirector.stopped -= OnTimelineStopped;
        }

        private void OnTimelineStopped(PlayableDirector _)
        {
            timelineFinished = true;
        }

        // ====================================================================
        // TRIGGER / OBJECTIVE HELPERS
        // ====================================================================

        private IEnumerator WaitForTrigger(SequenceTrigger trigger)
        {
            objectiveHit = false;
            trigger.ResetTrigger();
            trigger.OnTriggered += OnObjectiveHit;

            while (!objectiveHit)
                yield return null;

            trigger.OnTriggered -= OnObjectiveHit;
        }

        private void OnObjectiveHit()
        {
            objectiveHit = true;
        }

        // ====================================================================
        // ENEMY WAVE
        // ====================================================================

        private void SpawnEnemyWave()
        {
            spawnedEnemies.Clear();
            enemiesAlive = 0;

            if (enemyPrefabs == null || enemyPrefabs.Length == 0)
            {
                Debug.LogWarning("[Level0Sequence] No enemy prefabs assigned — skipping wave.");
                return;
            }

            int spawnCount = Mathf.Min(enemyPrefabs.Length,
                enemySpawnPoints != null ? enemySpawnPoints.Length : 1);

            for (int i = 0; i < spawnCount; i++)
            {
                Vector3 pos = (enemySpawnPoints != null && i < enemySpawnPoints.Length && enemySpawnPoints[i] != null)
                    ? enemySpawnPoints[i].position
                    : Vector3.zero;

                var go = Instantiate(enemyPrefabs[i], pos, Quaternion.identity);
                var enemy = go.GetComponent<EnemyCharacter>();

                if (enemy == null)
                {
                    Debug.LogWarning($"[Level0Sequence] Enemy prefab {i} missing EnemyCharacter!");
                    continue;
                }

                spawnedEnemies.Add(enemy);
                enemiesAlive++;

                var sm = enemy.GetComponent<StateMachine>();
                if (sm != null)
                    sm.OnStateChanged += OnWaveEnemyStateChanged;
            }

            Debug.Log($"[Level0Sequence] Spawned {enemiesAlive} enemies");
        }

        private void OnWaveEnemyStateChanged(IState from, IState to)
        {
            if (to is DeadState)
            {
                enemiesAlive = Mathf.Max(0, enemiesAlive - 1);
                Debug.Log($"[Level0Sequence] Enemy died — {enemiesAlive} remaining");
            }
        }

        private IEnumerator WaitForAllEnemiesDead()
        {
            while (enemiesAlive > 0)
                yield return null;

            UnsubscribeEnemyCallbacks();
            Debug.Log("[Level0Sequence] All enemies dead");
        }

        private void UnsubscribeEnemyCallbacks()
        {
            foreach (var enemy in spawnedEnemies)
            {
                if (enemy == null) continue;
                var sm = enemy.GetComponent<StateMachine>();
                if (sm != null)
                    sm.OnStateChanged -= OnWaveEnemyStateChanged;
            }
        }

        // ====================================================================
        // COMBAT MODE & INVENTORY GATES
        // ====================================================================

        private IEnumerator WaitForCombatModeToggle()
        {
            combatModeToggled = false;
            GameInputManager.Instance.OnCombatModeToggle += OnCombatModeToggled;

            while (!combatModeToggled)
                yield return null;

            GameInputManager.Instance.OnCombatModeToggle -= OnCombatModeToggled;
        }

        private void OnCombatModeToggled()
        {
            combatModeToggled = true;
        }

        private IEnumerator WaitForInventoryOpen()
        {
            inventoryOpened = false;
            GameInputManager.Instance.OnInventoryToggle += OnInventoryToggled;

            while (!inventoryOpened)
                yield return null;

            GameInputManager.Instance.OnInventoryToggle -= OnInventoryToggled;
        }

        private void OnInventoryToggled()
        {
            inventoryOpened = true;
        }

        private IEnumerator LerpReflectionIntensity(float from, float to, float duration)
        {
            if (duration <= 0f)
            {
                RenderSettings.reflectionIntensity = to;
                DynamicGI.UpdateEnvironment();
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                RenderSettings.reflectionIntensity = Mathf.Lerp(from, to, t);
                yield return null;
            }

            RenderSettings.reflectionIntensity = to;
            DynamicGI.UpdateEnvironment();
        }

        private IEnumerator FadeOutGodRayParticles()
        {
            if (godRayParticles == null)
                yield break;

            var emission = godRayParticles.emission;
            float startRate = emission.rateOverTimeMultiplier;

            if (godRayFadeDuration <= 0f)
            {
                emission.rateOverTimeMultiplier = 0f;
                godRayParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (overheadSpotlight != null) overheadSpotlight.enabled = false;
                godRayFadeRoutine = null;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < godRayFadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / godRayFadeDuration);
                emission.rateOverTimeMultiplier = Mathf.Lerp(startRate, 0f, t);
                yield return null;
            }

            emission.rateOverTimeMultiplier = 0f;
            godRayParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (overheadSpotlight != null) overheadSpotlight.enabled = false;
            godRayFadeRoutine = null;
        }

        // ====================================================================
        // SCENE TRANSITION
        // ====================================================================

        private void LoadConfiguredScene()
        {
            SetStage(Stage.Done);

            if (string.IsNullOrEmpty(nextSceneName))
            {
                Debug.LogWarning("[Level0Sequence] No next scene configured — tutorial complete but staying.");
                return;
            }

            Debug.Log($"[Level0Sequence] Loading next scene: {nextSceneName}");
            SceneManager.LoadScene(nextSceneName);
        }

        // ====================================================================
        // CLEANUP
        // ====================================================================

        private void OnDestroy()
        {
            UnsubscribeEnemyCallbacks();

            if (DialogueManager.instance != null)
                DialogueManager.instance.OnDialogueEnded -= OnDialogueEnded;

            if (director != null)
                director.stopped -= OnTimelineStopped;
            if (platformCompleteDirector != null)
                platformCompleteDirector.stopped -= OnTimelineStopped;

            if (godRayFadeRoutine != null)
                StopCoroutine(godRayFadeRoutine);

            if (GameInputManager.Instance != null)
            {
                GameInputManager.Instance.OnCombatModeToggle -= OnCombatModeToggled;
                GameInputManager.Instance.OnInventoryToggle -= OnInventoryToggled;
            }
        }

        // ====================================================================
        // DEBUG
        // ====================================================================

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (!showDebugInfo) return;

            GUILayout.BeginArea(new Rect(10, 10, 340, 140));
            GUILayout.Label("=== LEVEL 0 SEQUENCE ===");
            GUILayout.Label($"Stage: {currentStage}");
            GUILayout.Label($"Player: {(currentPlayer != null ? (currentPlayer.IsAlive ? "Alive" : "Dead") : "None")}");
            GUILayout.Label($"Enemies alive: {enemiesAlive}");
            GUILayout.Label($"Next scene: {(string.IsNullOrEmpty(nextSceneName) ? "(not set)" : nextSceneName)}");
            GUILayout.EndArea();
        }
#endif
    }
}
