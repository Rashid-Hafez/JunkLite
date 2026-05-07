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
        #region Inspector

        [Header("Reposition")]
        [SerializeField] private Transform centerRoomSpawn;

        [Header("Lighting")]
        [SerializeField] private Light overheadSpotlight;
        [SerializeField] private ParticleSystem godRayParticles;
        [SerializeField] private float godRayFadeDuration = 1f;
        [SerializeField] private float reflectionRevealDuration = 1f;

        [Header("Director / Timeline")]
        [SerializeField] private PlayableDirector director;
        [SerializeField] private PlayableDirector platformCompleteDirector;

        [Header("Completion Animation")]
        [SerializeField] private PngSequencePlayer completionAnimation;
        [SerializeField] private GameObject videoScreen;
        [SerializeField] private float completedResetDelay = 0.5f;

        [Header("Dialogue")]
        [SerializeField] private DialogueSequence introDialogue;
        [SerializeField] private DialogueSequence postCinematicDialogue;
        [SerializeField] private DialogueSequence secondPlatformDialogue;
        [SerializeField] private DialogueSequence thirdPlatformDialogue;
        [SerializeField] private DialogueSequence combatIntroDialogue;
        [SerializeField] private DialogueSequence postEnemyDialogue;
        [SerializeField] private DialogueSequence postPickupDialogue;
        [SerializeField] private DialogueSequence finalDialogue;

        [Header("Platform Steps")]
        [SerializeField] private GameObject platformStep1;
        [SerializeField] private GameObject platformStep2;
        [SerializeField] private TutorialStepDissolver platformStep2Dissolver;
        [SerializeField] private GameObject platformStep3;
        [SerializeField] private TutorialStepDissolver platformStep3Dissolver;

        [Header("Objectives")]
        [SerializeField] private SequenceTrigger objective1Trigger;
        [SerializeField] private SequenceTrigger objective2Trigger;
        [SerializeField] private SequenceTrigger objective3Trigger;
        [SerializeField] private SequenceTrigger finalObjectiveTrigger;

        [Header("Enemies")]
        [SerializeField] private GameObject[] enemyPrefabs;
        [SerializeField] private Transform[] enemySpawnPoints;
        [SerializeField] private GameObject tutorialWeaponPickupPrefab;
        [SerializeField] private Transform tutorialWeaponPickupSpawnPoint;
        [SerializeField] private GameObject postPickupEnemyPrefab;
        [SerializeField] private Transform postPickupEnemySpawnPoint;

        [Header("Scene Transition")]
        [SerializeField] private string nextSceneName;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = true;

        #endregion

        #region Runtime State

        private enum Stage
        {
            WaitingForPlayer, IntroDialogue, PlayTimeline, TutorialDialogue,
            ObjectiveOne, PlatformOneComplete, SecondPlatformDialogue,
            ObjectiveTwo, PlatformTwoComplete, ThirdPlatformDialogue,
            ObjectiveThree, PlatformThreeComplete, CombatIntroDialogue,
            EnemyWave, PostEnemyDialogue, WeaponPickup, PostPickupDialogue,
            FinalEnemyWave, FinalDialogue, WaitCombatMode, LoadNextScene, Done
        }

        private Stage currentStage;
        private PlayerCharacter currentPlayer;
        private Light playerLight;
        private WeaponManager currentWeaponManager;
        private Coroutine revealFadeRoutine;
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

        #endregion

        #region Lifecycle

        private void Awake()
        {
            Time.timeScale = 1f;

            if (platformStep2 != null) platformStep2.SetActive(false);
            if (platformStep3 != null) platformStep3.SetActive(false);
            if (overheadSpotlight != null) overheadSpotlight.enabled = false;
            if (godRayParticles != null)
                godRayParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            DisableVideoScreen();
        }

        private void Start()
        {
            DisableVideoScreen();
            StartCoroutine(RunSequence());
        }

        private void OnDestroy()
        {
            UnsubscribeEnemyCallbacks();

            if (DialogueManager.instance != null)
            {
                DialogueManager.instance.OnDialogueEnded -= OnDialogueEnded;
                DialogueManager.instance.IsContinueInputSuppressed = false;
            }

            if (director != null) director.stopped -= OnTimelineStopped;
            if (platformCompleteDirector != null) platformCompleteDirector.stopped -= OnTimelineStopped;
            if (completionAnimation != null) completionAnimation.Stop();
            if (currentWeaponManager != null) currentWeaponManager.OnWeaponChanged -= OnWeaponPickedUp;
            if (activeTutorialPickup != null) Destroy(activeTutorialPickup.gameObject);
            if (revealFadeRoutine != null) StopCoroutine(revealFadeRoutine);

            if (GameInputManager.Instance != null)
            {
                GameInputManager.Instance.OnCombatModeToggle -= OnCombatModeToggled;
                GameInputManager.Instance.OnInventoryToggle -= OnInventoryToggled;
            }
        }

        #endregion

        #region Main Sequence

        private IEnumerator RunSequence()
        {
            SetStage(Stage.WaitingForPlayer);
            yield return WaitForPlayer();

            playerLight = currentPlayer != null
                ? currentPlayer.GetComponentInChildren<Light>(true)
                : null;
            if (playerLight != null) playerLight.enabled = false;

            SetStage(Stage.IntroDialogue);
            if (introDialogue != null)
                yield return RunDialogue(introDialogue);

            if (playerLight != null) playerLight.enabled = true;
            if (overheadSpotlight != null) overheadSpotlight.enabled = true;
            if (godRayParticles != null)
            {
                godRayParticles.gameObject.SetActive(true);
                godRayParticles.Play(true);
            }
            StartCoroutine(LerpReflectionIntensity(RenderSettings.reflectionIntensity, 1f, reflectionRevealDuration));

            SetStage(Stage.PlayTimeline);
            if (revealFadeRoutine != null) StopCoroutine(revealFadeRoutine);
            revealFadeRoutine = StartCoroutine(FadeOutRevealEffects());
            if (director != null)
                yield return RunDirector(director, rewindToStart: true);

            SetStage(Stage.TutorialDialogue);
            if (postCinematicDialogue != null)
                yield return RunDialogue(postCinematicDialogue);

            SetStage(Stage.ObjectiveOne);
            if (objective1Trigger != null)
                yield return WaitForTrigger(objective1Trigger);

            SetStage(Stage.PlatformOneComplete);
            yield return PlayCompletionBeatAndReset(platformStep1);
            if (platformStep2 != null) platformStep2.SetActive(true);
            if (platformStep2Dissolver != null) platformStep2Dissolver.UndissolveAll();

            SetStage(Stage.SecondPlatformDialogue);
            if (secondPlatformDialogue != null)
                yield return RunDialogue(secondPlatformDialogue);

            SetStage(Stage.ObjectiveTwo);
            if (objective2Trigger != null)
                yield return WaitForTrigger(objective2Trigger);

            SetStage(Stage.PlatformTwoComplete);
            yield return PlayCompletionBeatAndReset(platformStep2);
            if (platformStep3 != null) platformStep3.SetActive(true);
            if (platformStep3Dissolver != null) platformStep3Dissolver.UndissolveAll();

            SetStage(Stage.ThirdPlatformDialogue);
            if (thirdPlatformDialogue != null)
                yield return RunDialogue(thirdPlatformDialogue);

            SetStage(Stage.ObjectiveThree);
            if (objective3Trigger != null)
                yield return WaitForTrigger(objective3Trigger);

            SetStage(Stage.PlatformThreeComplete);
            yield return PlayCompletionBeatAndReset(platformStep3);

            SetStage(Stage.CombatIntroDialogue);
            if (combatIntroDialogue != null)
                yield return RunDialogue(combatIntroDialogue);

            SetStage(Stage.EnemyWave);
            SpawnEnemyWave();
            yield return WaitForAllEnemiesDead();

            RepositionPlayer(centerRoomSpawn);
            SetStage(Stage.PostEnemyDialogue);
            if (postEnemyDialogue != null)
                yield return RunDialogue(postEnemyDialogue);

            SetStage(Stage.WeaponPickup);
            SpawnTutorialWeaponPickup();
            yield return WaitForWeaponPickup();

            SetStage(Stage.PostPickupDialogue);
            if (postPickupDialogue != null)
                yield return RunDialogue(postPickupDialogue);

            SetStage(Stage.WaitCombatMode);
            yield return WaitForCombatModeToggle();

            SetStage(Stage.FinalEnemyWave);
            SpawnPostPickupEnemy();
            yield return WaitForAllEnemiesDead();

            SetStage(Stage.FinalDialogue);
            if (finalDialogue != null)
                yield return RunDialogue(finalDialogue);

            SetStage(Stage.LoadNextScene);
            LoadConfiguredScene();
        }

        private void SetStage(Stage s)
        {
            currentStage = s;
            Debug.Log($"[Level0Sequence] >>> {s}");
        }

        #endregion

        private void DisableVideoScreen()
        {
            if (videoScreen == null)
                videoScreen = GameObject.Find("VideoScreen");

            if (videoScreen != null)
                videoScreen.SetActive(false);
        }

        #region Player

        private IEnumerator WaitForPlayer()
        {
            if (GameManager.Instance?.Player != null)
            {
                currentPlayer = GameManager.Instance.Player;
                yield break;
            }

            bool received = false;
            void OnSpawned(PlayerCharacter p) { currentPlayer = p; received = true; }

            if (GameManager.Instance != null)
                GameManager.Instance.OnPlayerSpawned += OnSpawned;

            while (!received)
                yield return null;

            if (GameManager.Instance != null)
                GameManager.Instance.OnPlayerSpawned -= OnSpawned;
        }

        private void RepositionPlayer(Transform spawnPoint)
        {
            RefreshPlayerRef();
            if (currentPlayer == null || spawnPoint == null) return;

            currentPlayer.ReviveAt(spawnPoint.position);
            currentPlayer.Activate();

            if (CameraManager.Instance != null)
                CameraManager.Instance.ConnectToPlayer(currentPlayer);
        }

        private void RefreshPlayerRef()
        {
            if (GameManager.Instance?.Player != null)
                currentPlayer = GameManager.Instance.Player;

            if (currentPlayer != null)
                currentWeaponManager = currentPlayer.GetComponentInChildren<WeaponManager>(true);
        }

        #endregion

        #region Dialogue

        private IEnumerator RunDialogue(DialogueSequence sequence)
        {
            if (sequence == null)
                yield break;

            while (DialogueManager.instance == null)
                yield return null;

            dialogueFinished = false;
            DialogueManager.instance.OnDialogueEnded += OnDialogueEnded;
            DialogueManager.instance.StartDialogue(sequence);

            while (!dialogueFinished)
                yield return null;

            DialogueManager.instance.OnDialogueEnded -= OnDialogueEnded;
        }

        private void OnDialogueEnded() => dialogueFinished = true;

        #endregion

        #region Timeline

        private IEnumerator RunDirector(PlayableDirector targetDirector, bool rewindToStart)
        {
            timelineFinished = false;
            if (targetDirector == null) yield break;

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

        private void OnTimelineStopped(PlayableDirector _) => timelineFinished = true;

        #endregion

        #region Completion Beat

        private IEnumerator PlayCompletionBeatAndReset(GameObject completedPlatformStep)
        {
            if (platformCompleteBeatRunning) yield break;
            platformCompleteBeatRunning = true;

            if (DialogueManager.instance != null)
                DialogueManager.instance.IsContinueInputSuppressed = true;

            // Always move the player to safety first so they cannot fall
            // while the completion beat is playing.
            RepositionPlayer(centerRoomSpawn);

            if (completedPlatformStep != null)
                completedPlatformStep.SetActive(false);

            yield return null;

            if (completionAnimation != null)
            {
                yield return completionAnimation.Play();

                // Hold on last frame for the reset delay, then fade out
                if (completedResetDelay > 0f)
                    yield return new WaitForSeconds(completedResetDelay);

                yield return completionAnimation.FadeOutAndHide();
            }
            else if (platformCompleteDirector != null)
            {
                yield return RunDirector(platformCompleteDirector, rewindToStart: true);

                if (completedResetDelay > 0f)
                    yield return new WaitForSeconds(completedResetDelay);
            }

            if (DialogueManager.instance != null)
                DialogueManager.instance.IsContinueInputSuppressed = false;

            platformCompleteBeatRunning = false;
        }

        #endregion

        #region Triggers

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
            GameInputManager.Instance?.SetGameplayInputEnabled(false);
        }

        #endregion

        #region Enemy Wave

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
                if (sm != null) sm.OnStateChanged += OnWaveEnemyStateChanged;
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
            if (sm != null) sm.OnStateChanged += OnWaveEnemyStateChanged;
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
                if (sm != null) sm.OnStateChanged -= OnWaveEnemyStateChanged;
            }
        }

        #endregion

        #region Weapon Pickup

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

        private void OnWeaponPickedUp() => weaponPickedUp = true;

        #endregion

        #region Combat & Inventory Gates

        private IEnumerator WaitForCombatModeToggle()
        {
            combatModeToggled = false;
            GameInputManager.Instance.OnCombatModeToggle += OnCombatModeToggled;

            while (!combatModeToggled)
                yield return null;

            GameInputManager.Instance.OnCombatModeToggle -= OnCombatModeToggled;
        }

        private void OnCombatModeToggled() => combatModeToggled = true;

        private IEnumerator WaitForInventoryOpen()
        {
            inventoryOpened = false;
            GameInputManager.Instance.OnInventoryToggle += OnInventoryToggled;

            while (!inventoryOpened)
                yield return null;

            GameInputManager.Instance.OnInventoryToggle -= OnInventoryToggled;
        }

        private void OnInventoryToggled() => inventoryOpened = true;

        #endregion

        #region Lighting

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
                RenderSettings.reflectionIntensity = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            RenderSettings.reflectionIntensity = to;
            DynamicGI.UpdateEnvironment();
        }

        private IEnumerator FadeOutRevealEffects()
        {
            float startSpotlightIntensity = overheadSpotlight != null ? overheadSpotlight.intensity : 0f;
            ParticleSystem.EmissionModule emission = default;
            float startRate = 0f;
            bool hasGodRayParticles = godRayParticles != null;
            if (hasGodRayParticles)
            {
                emission = godRayParticles.emission;
                startRate = emission.rateOverTimeMultiplier;
            }

            if (godRayFadeDuration <= 0f)
            {
                if (overheadSpotlight != null)
                {
                    overheadSpotlight.intensity = 0f;
                    overheadSpotlight.enabled = false;
                }
                if (hasGodRayParticles)
                {
                    emission.rateOverTimeMultiplier = 0f;
                    godRayParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
                revealFadeRoutine = null;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < godRayFadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / godRayFadeDuration);

                if (overheadSpotlight != null)
                    overheadSpotlight.intensity = Mathf.Lerp(startSpotlightIntensity, 0f, t);

                if (hasGodRayParticles)
                    emission.rateOverTimeMultiplier = Mathf.Lerp(startRate, 0f, t);

                yield return null;
            }

            if (overheadSpotlight != null)
            {
                overheadSpotlight.intensity = 0f;
                overheadSpotlight.enabled = false;
            }

            if (hasGodRayParticles)
            {
                emission.rateOverTimeMultiplier = 0f;
                godRayParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            revealFadeRoutine = null;
        }

        #endregion

        #region Scene Transition

        private void LoadConfiguredScene()
        {
            SetStage(Stage.Done);

            if (string.IsNullOrEmpty(nextSceneName))
            {
                Debug.LogWarning("[Level0Sequence] No next scene configured.");
                return;
            }

            GameManager.Instance.LoadLevel(nextSceneName);
        }

        #endregion

        #region Debug GUI

#if UNITY_EDITOR
        private void OnGUI()
        {
            if (!showDebugInfo) return;

            GUILayout.BeginArea(new Rect(10, 10, 340, 120));
            GUILayout.Label("=== LEVEL 0 SEQUENCE ===");
            GUILayout.Label($"Stage:         {currentStage}");
            GUILayout.Label($"Player:        {(currentPlayer != null ? (currentPlayer.IsAlive ? "Alive" : "Dead") : "None")}");
            GUILayout.Label($"Enemies alive: {enemiesAlive}");
            GUILayout.Label($"Next scene:    {(string.IsNullOrEmpty(nextSceneName) ? "(not set)" : nextSceneName)}");
            GUILayout.EndArea();
        }
#endif

        #endregion
    }
}