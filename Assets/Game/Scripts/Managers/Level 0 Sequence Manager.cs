using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

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
        [Tooltip("Optional fallback director for the reusable 'platform complete' beat played after each obstacle course.")]
        [SerializeField] private PlayableDirector platformCompleteDirector;

        [Header("Completed Video")]
        [Tooltip("Optional root object for the completion video overlay. Enabled while the video plays.")]
        [SerializeField] private GameObject completedVideoRoot;
        [Tooltip("Optional transparent completion video player. Used after each platform step if assigned.")]
        [SerializeField] private VideoPlayer completedVideoPlayer;
        [Tooltip("Delay after the completed beat finishes before the player is moved to the room spawn.")]
        [SerializeField] private float completedResetDelay = 0.5f;

        [Header("Dialogue")]
        [Tooltip("Played immediately after the player spawns.")]
        [SerializeField] private DialogueSequence introDialogue;
        [Tooltip("Played after the cinematic timeline finishes, before platform trial 1.")]
        [SerializeField] private DialogueSequence postCinematicDialogue;
        [Tooltip("Played after platform trial 1, before platform trial 2 starts.")]
        [SerializeField] private DialogueSequence secondPlatformDialogue;
        [Tooltip("Played after platform trial 2, before platform trial 3 starts.")]
        [SerializeField] private DialogueSequence thirdPlatformDialogue;
        [Tooltip("Played after platform trial 3, before the combat wave spawns.")]
        [SerializeField] private DialogueSequence combatIntroDialogue;
        [Tooltip("Played after the first combat enemy dies and the player is moved back to the room spawn.")]
        [SerializeField] private DialogueSequence postEnemyDialogue;
        [Tooltip("Played after the player picks up the weapon, before the final enemy spawns.")]
        [SerializeField] private DialogueSequence postPickupDialogue;
        [Tooltip("Played after the final objective, before combat/inventory onboarding.")]
        [SerializeField] private DialogueSequence finalDialogue;

        [Header("Platform Steps (Timeline-controlled)")]
        [Tooltip("First dissolve platform group — activated before objective 1.")]
        [SerializeField] private GameObject platformStep1;
        [Tooltip("Second dissolve platform group — activated before objective 2.")]
        [SerializeField] private GameObject platformStep2;
        [Tooltip("Optional dissolver on the second platform group's parent. Called when step 2 should phase in.")]
        [SerializeField] private TutorialStepDissolver platformStep2Dissolver;
        [Tooltip("Third dissolve platform group — activated before objective 3.")]
        [SerializeField] private GameObject platformStep3;
        [Tooltip("Optional dissolver on the third platform group's parent. Called when step 3 should phase in.")]
        [SerializeField] private TutorialStepDissolver platformStep3Dissolver;

        [Header("Objectives (SequenceTrigger colliders)")]
        [SerializeField] private SequenceTrigger objective1Trigger;
        [SerializeField] private SequenceTrigger objective2Trigger;
        [SerializeField] private SequenceTrigger objective3Trigger;
        [SerializeField] private SequenceTrigger finalObjectiveTrigger;

        [Header("Enemies")]
        [SerializeField] private GameObject[] enemyPrefabs;
        [SerializeField] private Transform[] enemySpawnPoints;
        [Tooltip("Weapon pickup spawned after the first combat enemy dies.")]
        [SerializeField] private GameObject tutorialWeaponPickupPrefab;
        [SerializeField] private Transform tutorialWeaponPickupSpawnPoint;
        [Tooltip("Follow-up enemy spawned after the player picks up the weapon and enters combat mode.")]
        [SerializeField] private GameObject postPickupEnemyPrefab;
        [SerializeField] private Transform postPickupEnemySpawnPoint;

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
            ResetForPlatformThree,
            ThirdPlatformDialogue,
            ObjectiveThree,
            PlatformThreeComplete,
            ResetForCombat,
            CombatIntroDialogue,
            EnemyWave,
            PostEnemyDialogue,
            WeaponPickup,
            PostPickupDialogue,
            FinalEnemyWave,
            FinalDialogue,
            WaitCombatMode,
            LoadNextScene,
            Done
        }

        private Stage currentStage;
        private PlayerCharacter currentPlayer;
        private Light playerLight;
        private WeaponManager currentWeaponManager;
        private Coroutine godRayFadeRoutine;
        private bool platformCompleteBeatRunning;

        private readonly List<EnemyCharacter> spawnedEnemies = new();
        private int enemiesAlive;
        private WorldWeaponPickup activeTutorialPickup;

        private bool dialogueFinished;
        private bool timelineFinished;
        private bool objectiveHit;
        private bool combatModeToggled;
        private bool inventoryOpened;
        private bool weaponPickedUp;

        // ====================================================================
        // STARTUP
        // ====================================================================

        private void Awake()
        {
            Time.timeScale = 1f;

            if (platformStep2 != null) platformStep2.SetActive(false);
            if (platformStep3 != null) platformStep3.SetActive(false);

            if (overheadSpotlight != null) overheadSpotlight.enabled = false;
            if (godRayParticles != null)
            {
                godRayParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            if (completedVideoRoot != null)
                completedVideoRoot.SetActive(false);
            if (completedVideoPlayer != null)
                completedVideoPlayer.Stop();
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
            if (objective1Trigger != null)
                yield return WaitForTrigger(objective1Trigger);

            // 6 — Play completion beat for platform 1
            SetStage(Stage.PlatformOneComplete);
            yield return PlayPlatformCompleteBeatAndReset(platformStep1);
            if (platformStep2 != null)
                platformStep2.SetActive(true);
            if (platformStep2Dissolver != null)
                platformStep2Dissolver.UndissolveAll();

            // 7 — Explain the second platform lesson
            SetStage(Stage.SecondPlatformDialogue);
            if (secondPlatformDialogue != null)
                yield return RunDialogue(secondPlatformDialogue);

            // 8 — Activate second platform step, wait for objective 2
            SetStage(Stage.ObjectiveTwo);
            if (objective2Trigger != null)
                yield return WaitForTrigger(objective2Trigger);

            // 9 — Play completion beat for platform 2
            SetStage(Stage.PlatformTwoComplete);
            yield return PlayPlatformCompleteBeatAndReset(platformStep2);
            if (platformStep3 != null)
                platformStep3.SetActive(true);
            if (platformStep3Dissolver != null)
                platformStep3Dissolver.UndissolveAll();

            // 10 — Explain the third platform lesson
            SetStage(Stage.ThirdPlatformDialogue);
            if (thirdPlatformDialogue != null)
                yield return RunDialogue(thirdPlatformDialogue);

            // 11 — Activate third platform step, wait for objective 3
            SetStage(Stage.ObjectiveThree);
            if (objective3Trigger != null)
                yield return WaitForTrigger(objective3Trigger);

            // 12 — Play completion beat for platform 3
            SetStage(Stage.PlatformThreeComplete);
            yield return PlayPlatformCompleteBeatAndReset(platformStep3);

            // 13 — Explain the combat tutorial before enemies appear
            SetStage(Stage.CombatIntroDialogue);
            if (combatIntroDialogue != null)
                yield return RunDialogue(combatIntroDialogue);

            // 14 — Spawn enemy wave, wait until all dead
            SetStage(Stage.EnemyWave);
            SpawnEnemyWave();
            yield return WaitForAllEnemiesDead();

            // 15 — Move the player back to the room spawn, then explain the weapon pickup
            RepositionPlayer(centerRoomSpawn);
            SetStage(Stage.PostEnemyDialogue);
            if (postEnemyDialogue != null)
                yield return RunDialogue(postEnemyDialogue);

            // 16 — Spawn the tutorial weapon pickup and wait for the player to actually take it
            SetStage(Stage.WeaponPickup);
            SpawnTutorialWeaponPickup();
            yield return WaitForWeaponPickup();

            // 17 — Explain the next combat beat after the weapon is picked up
            SetStage(Stage.PostPickupDialogue);
            if (postPickupDialogue != null)
                yield return RunDialogue(postPickupDialogue);

            // 18 — Wait for the player to enter combat mode with the new weapon
            SetStage(Stage.WaitCombatMode);
            yield return WaitForCombatModeToggle();

            // 19 — Spawn the follow-up enemy and wait until it is dead
            SetStage(Stage.FinalEnemyWave);
            SpawnPostPickupEnemy();
            yield return WaitForAllEnemiesDead();

            // 20 — Final dialogue
            SetStage(Stage.FinalDialogue);
            if (finalDialogue != null)
                yield return RunDialogue(finalDialogue);

            // 21 — Load next scene
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

            if (currentPlayer != null)
                currentWeaponManager = currentPlayer.GetComponentInChildren<WeaponManager>(true);
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

        private IEnumerator PlayPlatformCompleteBeat()
        {
            if (completedVideoPlayer != null)
            {
                yield return PlayCompletedVideo();
                yield break;
            }

            if (platformCompleteDirector != null)
                yield return RunDirector(platformCompleteDirector, rewindToStart: true);
        }

        private IEnumerator PlayPlatformCompleteBeatAndReset(GameObject completedPlatformStep)
        {
            if (platformCompleteBeatRunning)
                yield break;

            platformCompleteBeatRunning = true;
            if (DialogueManager.instance != null)
                DialogueManager.instance.IsContinueInputSuppressed = true;
            yield return PlayPlatformCompleteBeat();

            if (completedResetDelay > 0f)
                yield return new WaitForSeconds(completedResetDelay);

            RepositionPlayer(centerRoomSpawn);
            if (completedPlatformStep != null)
                completedPlatformStep.SetActive(false);
            HideCompletedVideo();
            if (DialogueManager.instance != null)
                DialogueManager.instance.IsContinueInputSuppressed = false;
            platformCompleteBeatRunning = false;
        }

        private IEnumerator PlayCompletedVideo()
        {
            if (completedVideoPlayer == null)
                yield break;

            completedVideoPlayer.Stop();
            ClearCompletedVideoRenderTexture();

            if (completedVideoRoot != null)
                completedVideoRoot.SetActive(true);

            bool complete = false;
            void OnLoopPointReached(VideoPlayer _) => complete = true;

            completedVideoPlayer.loopPointReached += OnLoopPointReached;
            completedVideoPlayer.isLooping = false;
            completedVideoPlayer.time = 0d;
            completedVideoPlayer.frame = 0;
            completedVideoPlayer.Prepare();

            while (!completedVideoPlayer.isPrepared)
                yield return null;

            completedVideoPlayer.time = 0d;
            completedVideoPlayer.frame = 0;
            completedVideoPlayer.Play();

            while (!complete)
                yield return null;

            completedVideoPlayer.loopPointReached -= OnLoopPointReached;
        }

        private void HideCompletedVideo()
        {
            if (completedVideoPlayer != null)
            {
                completedVideoPlayer.Stop();
                ClearCompletedVideoRenderTexture();
            }

            if (completedVideoRoot != null)
                completedVideoRoot.SetActive(false);
        }

        private void ClearCompletedVideoRenderTexture()
        {
            if (completedVideoPlayer == null || completedVideoPlayer.targetTexture == null)
                return;

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = completedVideoPlayer.targetTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = previous;
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
            if (GameInputManager.Instance != null)
                GameInputManager.Instance.SetGameplayInputEnabled(false);
        }

        // ====================================================================
        // ENEMY WAVE
        // ====================================================================

        private void SpawnEnemyWave()
        {
            UnsubscribeEnemyCallbacks();
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

        private void SpawnPostPickupEnemy()
        {
            UnsubscribeEnemyCallbacks();
            spawnedEnemies.Clear();
            enemiesAlive = 0;

            if (postPickupEnemyPrefab == null)
            {
                Debug.LogWarning("[Level0Sequence] No post-pickup enemy prefab assigned.");
                return;
            }

            Vector3 pos = postPickupEnemySpawnPoint != null ? postPickupEnemySpawnPoint.position : Vector3.zero;
            SpawnTrackedEnemy(postPickupEnemyPrefab, pos);
            Debug.Log("[Level0Sequence] Spawned post-pickup enemy.");
        }

        private void SpawnTrackedEnemy(GameObject enemyPrefab, Vector3 position)
        {
            var go = Instantiate(enemyPrefab, position, Quaternion.identity);
            var enemy = go.GetComponent<EnemyCharacter>();

            if (enemy == null)
            {
                Debug.LogWarning("[Level0Sequence] Spawned enemy prefab missing EnemyCharacter!");
                return;
            }

            spawnedEnemies.Add(enemy);
            enemiesAlive++;

            var sm = enemy.GetComponent<StateMachine>();
            if (sm != null)
                sm.OnStateChanged += OnWaveEnemyStateChanged;
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

        private void SpawnTutorialWeaponPickup()
        {
            if (activeTutorialPickup != null)
                Destroy(activeTutorialPickup.gameObject);

            if (tutorialWeaponPickupPrefab == null)
            {
                Debug.LogWarning("[Level0Sequence] No tutorial weapon pickup prefab assigned.");
                return;
            }

            Vector3 pos = tutorialWeaponPickupSpawnPoint != null
                ? tutorialWeaponPickupSpawnPoint.position
                : centerRoomSpawn != null ? centerRoomSpawn.position : Vector3.zero;

            var go = Instantiate(tutorialWeaponPickupPrefab, pos, Quaternion.identity);
            activeTutorialPickup = go.GetComponent<WorldWeaponPickup>();
        }

        private IEnumerator WaitForWeaponPickup()
        {
            RefreshPlayerRef();

            if (currentWeaponManager == null)
            {
                Debug.LogWarning("[Level0Sequence] No WeaponManager found on current player.");
                yield break;
            }

            weaponPickedUp = false;
            currentWeaponManager.OnWeaponChanged += OnWeaponPickedUp;

            while (!weaponPickedUp)
                yield return null;

            currentWeaponManager.OnWeaponChanged -= OnWeaponPickedUp;
            activeTutorialPickup = null;
        }

        private void OnWeaponPickedUp()
        {
            weaponPickedUp = true;
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
            {
                DialogueManager.instance.OnDialogueEnded -= OnDialogueEnded;
                DialogueManager.instance.IsContinueInputSuppressed = false;
            }

            if (director != null)
                director.stopped -= OnTimelineStopped;
            if (platformCompleteDirector != null)
                platformCompleteDirector.stopped -= OnTimelineStopped;
            if (completedVideoPlayer != null)
                completedVideoPlayer.Stop();
            if (currentWeaponManager != null)
                currentWeaponManager.OnWeaponChanged -= OnWeaponPickedUp;
            if (activeTutorialPickup != null)
                Destroy(activeTutorialPickup.gameObject);

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
