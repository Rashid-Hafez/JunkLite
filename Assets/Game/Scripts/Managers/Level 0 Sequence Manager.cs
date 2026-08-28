using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

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
        [SerializeField] private PlayableDirector endCinematicDirector;

        [Header("Completion Animation")]
        [SerializeField] private PngSequencePlayer completionAnimation;
        [SerializeField] private GameObject videoScreen;
        [SerializeField] private float completedResetDelay = 0.5f;

        [Header("Cinematic Audio")]
        [SerializeField] private SoundEntry introSequenceSfx;
        [SerializeField] private SoundEntry lightRevealSfx;
        [SerializeField] private SoundEntry platformCompleteSfx;
        [SerializeField] private SoundEntry platformDissolveSfx;
        [SerializeField] private Transform introSfxPoint;
        [SerializeField] private Transform platformStep1DissolveSfxPoint;
        [SerializeField] private Transform platformStep2DissolveSfxPoint;
        [SerializeField] private Transform platformStep3DissolveSfxPoint;

        [Header("Dialogue")]
        [SerializeField] private DialogueSequence introDialogue;
        [SerializeField] private DialogueSequence postCinematicDialogue;
        [SerializeField] private DialogueSequence secondPlatformDialogue;
        [SerializeField] private DialogueSequence thirdPlatformDialogue;
        [SerializeField] private DialogueSequence combatIntroDialogue;
        [SerializeField] private DialogueSequence parryPromptDialogue;
        [SerializeField] private DialogueSequence postEnemyDialogue;
        [SerializeField] private DialogueSequence postPickupDialogue;
        [SerializeField] private DialogueSequence modActivationPromptDialogue;
        [SerializeField] private DialogueSequence inventoryPromptDialogue;
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

        [Header("Encounter")]
        [SerializeField] private EncounterController encounter;
        [SerializeField, HideInInspector] private GameObject[] enemyPrefabs;
        [SerializeField, HideInInspector] private Transform[] enemySpawnPoints;
        [SerializeField] private GameObject tutorialWeaponPickupPrefab;
        [SerializeField] private Transform tutorialWeaponPickupSpawnPoint;

        [Header("Mod pickup (after hyena)")]
        [Tooltip("Prefab from Project, or an inactive pickup already in the scene.")]
        [SerializeField] private GameObject tutorialModPickup;
        [SerializeField] private Transform tutorialModPickupSpawnPoint;

        [Header("Scene Transition")]
        [SerializeField] private string nextSceneName;
        [SerializeField] private int nextSceneBuildIndex = -1;

        [Header("End beat — glitch (Cinemachine Volume Settings)")]
        [Tooltip("Cinematic vcam extension that holds the Volume Profile with Analog/Digital Glitch.")]
        [SerializeField] private CinemachineVolumeSettings endSequenceVolumeSettings;
        [SerializeField] private float endGlitchRampInSeconds = 2f;
        [SerializeField] private float endGlitchHoldSeconds = 0.35f;
        [SerializeField] private float endGlitchRampOutSeconds = 1.25f;
        [Range(0f, 1f)] [SerializeField] private float endGlitchAnalogPeak = 0.5f;
        [Range(0f, 1f)] [SerializeField] private float endGlitchDigitalPeak = 0.4f;

        [Header("End beat — outro video (then load next scene)")]
        [SerializeField] private GameObject playerUI;
        [SerializeField] private VideoPlayer endOutroVideoPlayer;
        [SerializeField] private VideoClip endOutroVideoClip;
        [Tooltip("Optional UI root shown while the outro clip plays (e.g. full-screen RawImage parent).")]
        [SerializeField] private GameObject endOutroVideoScreenRoot;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = true;
        [SerializeField] private float postParryRespawnDelay = 0.6f;
        [SerializeField] private int objectiveRepositionPasses = 3;

        #endregion

        #region Runtime State

        private enum Stage
        {
            WaitingForPlayer, IntroDialogue, PlayTimeline, TutorialDialogue,
            ObjectiveOne, PlatformOneComplete, SecondPlatformDialogue,
            ObjectiveTwo, PlatformTwoComplete, ThirdPlatformDialogue,
            ObjectiveThree, PlatformThreeComplete, CombatIntroDialogue,
            EnemyWave, PostEnemyDialogue, WeaponPickup, PostPickupDialogue,
            WaitCombatMode, ModActivationPrompt, WaitModActivation,
            InventoryPrompt, WaitInventoryOpen, WaitInventoryClose,
            FinalGlitchRamp, FinalDialogue, OutroVideo, LoadNextScene, Done
        }

        private Stage currentStage;
        private PlayerCharacter currentPlayer;
        private Light playerLight;
        private WeaponManager currentWeaponManager;
        private PlayerWeaponLoadout currentWeaponLoadout;
        private ModManager currentModManager;
        private SpineAnimationController currentSpineAnimationController;
        private Coroutine revealFadeRoutine;
        private bool platformCompleteBeatRunning;

        private readonly HashSet<EnemyCharacter> tutorialEncounterEnemies = new();
        private WorldWeaponPickup activeTutorialPickup;
        private bool tutorialModHyenaSpawnHandled;

        private bool dialogueFinished;
        private bool timelineFinished;
        private bool objectiveHit;
        private bool combatModeToggled;
        private bool inventoryOpened;
        private bool inventoryClosed;
        private bool modActivated;
        private bool weaponPickedUp;
        private bool parryTutorialPromptUsed;
        private bool parryTutorialPressed;
        private Coroutine parryTutorialRoutine;

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
            UnsubscribeEncounterCallbacks();

            if (DialogueManager.instance != null)
            {
                DialogueManager.instance.OnDialogueEnded -= OnDialogueEnded;
                DialogueManager.instance.IsContinueInputSuppressed = false;
            }

            if (director != null) director.stopped -= OnTimelineStopped;
            if (platformCompleteDirector != null) platformCompleteDirector.stopped -= OnTimelineStopped;
            if (endCinematicDirector != null) endCinematicDirector.stopped -= OnTimelineStopped;
            if (completionAnimation != null) completionAnimation.Stop();
            if (currentWeaponLoadout != null) currentWeaponLoadout.WeaponChanged -= OnWeaponPickedUp;
            if (currentModManager != null) currentModManager.OnActiveModActivated -= OnActiveModActivated;
            if (activeTutorialPickup != null) Destroy(activeTutorialPickup.gameObject);
            if (parryTutorialRoutine != null) StopCoroutine(parryTutorialRoutine);
            if (revealFadeRoutine != null) StopCoroutine(revealFadeRoutine);

            if (GameInputManager.Instance != null)
            {
                GameInputManager.Instance.OnCombatModeToggle -= OnCombatModeToggled;
                GameInputManager.Instance.OnInventoryToggle -= OnInventoryToggled;
                GameInputManager.Instance.OnParry -= OnTutorialParryPressed;
                GameInputManager.Instance.SetParryOnlyInputEnabled(false);
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
            PlayCinematicSfx(introSequenceSfx, introSfxPoint);
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

            PlayCinematicSfx(platformDissolveSfx, platformStep1DissolveSfxPoint);

            SetStage(Stage.TutorialDialogue);
            if (postCinematicDialogue != null)
                yield return RunDialogue(postCinematicDialogue);

            SetStage(Stage.ObjectiveOne);
            if (objective1Trigger != null)
                yield return WaitForTrigger(objective1Trigger);

            SetStage(Stage.PlatformOneComplete);
            yield return PlayCompletionBeatAndReset(platformStep1);
            if (platformStep2 != null) platformStep2.SetActive(true);
            if (platformStep2Dissolver != null)
            {
                PlayCinematicSfx(platformDissolveSfx, platformStep2DissolveSfxPoint);
                platformStep2Dissolver.UndissolveAll();
            }

            SetStage(Stage.SecondPlatformDialogue);
            if (secondPlatformDialogue != null)
                yield return RunDialogue(secondPlatformDialogue);

            SetStage(Stage.ObjectiveTwo);
            if (objective2Trigger != null)
                yield return WaitForTrigger(objective2Trigger);

            SetStage(Stage.PlatformTwoComplete);
            yield return PlayCompletionBeatAndReset(platformStep2);
            if (platformStep3 != null) platformStep3.SetActive(true);
            if (platformStep3Dissolver != null)
            {
                PlayCinematicSfx(platformDissolveSfx, platformStep3DissolveSfxPoint);
                platformStep3Dissolver.UndissolveAll();
            }

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
            PrepareEncounter();
            SubscribeEncounterCallbacks();
            encounter.StartEncounter();
            yield return encounter.WaitUntilFinished();

            bool encounterCompleted = encounter.State == EncounterState.Completed;
            UnsubscribeEncounterCallbacks();
            if (!encounterCompleted)
            {
                Debug.LogWarning(
                    $"[Level0Sequence] Encounter ended in state {encounter.State}; tutorial progression stopped.",
                    encounter);
                yield break;
            }

            yield return WaitForParryEffectsToSettle();

            // If any mod pickups were spawned by enemy deaths (e.g. hyena drops),
            // wait until the player collects them before repositioning.
            var modPickups = FindObjectsOfType<WorldModPickup>();
            if (modPickups != null && modPickups.Length > 0)
            {
                Debug.Log($"[Level0Sequence] Waiting for {modPickups.Length} mod pickup(s) to be collected before repositioning.");
                while (true)
                {
                    var remaining = FindObjectsOfType<WorldModPickup>();
                    bool anyActive = false;
                    foreach (var mp in remaining)
                    {
                        if (mp != null && mp.gameObject.activeInHierarchy)
                        {
                            anyActive = true;
                            break;
                        }
                    }
                    if (!anyActive) break;
                    yield return null;
                }
                Debug.Log("[Level0Sequence] Mod pickup(s) collected, continuing sequence.");
            }

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

            SetStage(Stage.ModActivationPrompt);
            if (modActivationPromptDialogue != null)
                yield return RunDialogue(modActivationPromptDialogue);

            SetStage(Stage.WaitModActivation);
            yield return WaitForActiveModActivation();
            yield return WaitForModActivationAnimationComplete();

            SetStage(Stage.InventoryPrompt);
            if (inventoryPromptDialogue != null)
                yield return RunDialogue(inventoryPromptDialogue);

            SetStage(Stage.WaitInventoryOpen);
            yield return WaitForInventoryOpen();

            SetStage(Stage.WaitInventoryClose);
            yield return WaitForInventoryClose();

            SetStage(Stage.FinalGlitchRamp);
            yield return RunEndGlitchRampIfConfigured();

            SetStage(Stage.FinalDialogue);
            if (finalDialogue != null)
                yield return RunDialogue(finalDialogue);

            if (endCinematicDirector != null)
                yield return RunDirector(endCinematicDirector, rewindToStart: true);

            SetStage(Stage.OutroVideo);
            yield return PlayEndOutroVideoIfConfigured();

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
            while (PlayerLifecycle.Instance == null)
                yield return null;

            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            if (lifecycle.Player != null)
            {
                currentPlayer = lifecycle.Player;
                yield break;
            }

            bool received = false;
            void OnSpawned(PlayerCharacter p) { currentPlayer = p; received = true; }

            lifecycle.PlayerSpawned += OnSpawned;

            // Close the subscribe/read race if a spawn happened this frame.
            if (lifecycle.Player != null)
                OnSpawned(lifecycle.Player);

            while (!received && PlayerLifecycle.Instance == lifecycle)
                yield return null;

            lifecycle.PlayerSpawned -= OnSpawned;
        }

        private void RepositionPlayer(Transform spawnPoint)
        {
            RefreshPlayerRef();
            if (currentPlayer == null)
            {
                Debug.LogWarning($"[Level0Sequence] Reposition skipped during {currentStage}: currentPlayer is null.");
                return;
            }

            if (spawnPoint == null)
            {
                Debug.LogWarning($"[Level0Sequence] Reposition skipped during {currentStage}: spawnPoint is null.");
                return;
            }

            Vector3 from = currentPlayer.transform.position;
            Vector3 target = spawnPoint.position;
            Debug.Log($"[Level0Sequence] Reposition begin during {currentStage}: player '{currentPlayer.name}' from {from} to spawn '{spawnPoint.name}' at {target}. timeScale={Time.timeScale:0.###}");

            currentPlayer.ReviveAt(target);
            currentPlayer.Activate();

            if (CameraManager.Instance != null)
            {
                CameraManager.Instance.ConnectToPlayer(currentPlayer);
                Debug.Log($"[Level0Sequence] Camera reconnected to player after reposition during {currentStage}.");
            }

            Debug.Log($"[Level0Sequence] Reposition complete during {currentStage}: player now at {currentPlayer.transform.position}.");
        }

        private IEnumerator ForceObjectiveReposition(Transform spawnPoint)
        {
            int passes = Mathf.Max(1, objectiveRepositionPasses);

            for (int i = 0; i < passes; i++)
            {
                RepositionPlayer(spawnPoint);

                if (i == 0)
                    yield return null;
                else
                    yield return new WaitForFixedUpdate();
            }

            if (currentPlayer != null && spawnPoint != null)
            {
                float remaining = Vector3.Distance(currentPlayer.transform.position, spawnPoint.position);
                Debug.Log($"[Level0Sequence] Objective reposition settled during {currentStage}: remaining distance to spawn = {remaining:0.###}");
            }
        }

        private void RefreshPlayerRef()
        {
            if (PlayerLifecycle.Instance?.Player != null)
                currentPlayer = PlayerLifecycle.Instance.Player;

            if (currentPlayer != null)
            {
                currentWeaponManager = currentPlayer.GetComponentInChildren<WeaponManager>(true);
                currentWeaponLoadout = currentWeaponManager != null
                    ? currentWeaponManager.Loadout
                    : currentPlayer.GetComponentInChildren<PlayerWeaponLoadout>(true);
                currentModManager = currentPlayer.GetComponentInChildren<ModManager>(true);
                currentSpineAnimationController = currentPlayer.GetComponentInChildren<SpineAnimationController>(true);
            }
        }

        private void PlayCinematicSfx(SoundEntry entry, Transform point = null)
        {
            if (AudioManager.Instance == null || entry == null || !entry.IsValid)
                return;

            if (point != null)
            {
                AudioManager.Instance.PlaySpatialAtPosition(entry, point.position, spatialBlend: 1f);
                return;
            }

            AudioManager.Instance.PlayUI(entry);
        }

        private IEnumerator WaitForParryEffectsToSettle()
        {
            RefreshPlayerRef();

            PlayerState playerState = currentPlayer != null ? currentPlayer.PlayerState : null;
            bool parryLikelyActive = playerState != null &&
                                     (playerState.IsParrying || playerState.IsInputLocked || Time.timeScale < 0.999f);

            if (!parryLikelyActive)
            {
                Debug.Log($"[Level0Sequence] No parry settle wait needed during {currentStage}. timeScale={Time.timeScale:0.###}");
                yield break;
            }

            Debug.Log($"[Level0Sequence] Waiting for parry effects during {currentStage}. IsParrying={playerState?.IsParrying ?? false}, IsInputLocked={playerState?.IsInputLocked ?? false}, timeScale={Time.timeScale:0.###}");

            float endTime = Time.realtimeSinceStartup + Mathf.Max(0f, postParryRespawnDelay);
            while (Time.realtimeSinceStartup < endTime)
                yield return null;

            while (true)
            {
                RefreshPlayerRef();
                playerState = currentPlayer != null ? currentPlayer.PlayerState : null;

                bool waitingOnParry = playerState != null && (playerState.IsParrying || playerState.IsInputLocked);
                bool waitingOnTimeScale = Time.timeScale < 0.999f;

                if (!waitingOnParry && !waitingOnTimeScale)
                    break;

                yield return null;
            }

            Debug.Log($"[Level0Sequence] Parry effects settled during {currentStage}. timeScale={Time.timeScale:0.###}");
            yield return null;
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
            yield return ForceObjectiveReposition(centerRoomSpawn);

            if (completedPlatformStep != null)
                completedPlatformStep.SetActive(false);

            if (completionAnimation != null)
            {
                PlayCinematicSfx(platformCompleteSfx);
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

        private void PrepareEncounter()
        {
            parryTutorialPromptUsed = false;
            tutorialModHyenaSpawnHandled = false;

            if (encounter != null)
                return;

            EncounterController localEncounter = GetComponent<EncounterController>();
            if (localEncounter != null && localEncounter.ConfiguredWaveCount > 0)
            {
                encounter = localEncounter;
                Debug.LogWarning(
                    "[Level0Sequence] Using the local EncounterController. Assign it explicitly in the Inspector.",
                    encounter);
                return;
            }

            encounter = localEncounter != null
                ? localEncounter
                : gameObject.AddComponent<EncounterController>();

            List<EncounterEnemyEntry> legacyEntries = new();
            int prefabCount = enemyPrefabs?.Length ?? 0;
            int spawnPointCount = enemySpawnPoints?.Length ?? 0;
            int entryCount = Mathf.Min(prefabCount, spawnPointCount);

            if (prefabCount != spawnPointCount)
            {
                Debug.LogWarning(
                    $"[Level0Sequence] Legacy encounter arrays have different lengths " +
                    $"({prefabCount} prefabs, {spawnPointCount} spawn points); only paired entries will migrate.",
                    this);
            }

            for (int i = 0; i < entryCount; i++)
            {
                GameObject prefabObject = enemyPrefabs[i];
                Transform spawnPoint = enemySpawnPoints[i];
                EnemyCharacter enemyPrefab = prefabObject != null
                    ? prefabObject.GetComponent<EnemyCharacter>()
                    : null;

                if (enemyPrefab == null || spawnPoint == null)
                {
                    Debug.LogWarning(
                        $"[Level0Sequence] Legacy encounter entry {i} is invalid and will be skipped.",
                        this);
                    continue;
                }

                legacyEntries.Add(EncounterEnemyEntry.SpawnPrefab(enemyPrefab, spawnPoint));
            }

            encounter.ConfigureRuntimeWaves(new[] { new EncounterWave(legacyEntries) });
            Debug.LogWarning(
                "[Level0Sequence] Built a runtime EncounterController from legacy enemy arrays. " +
                "Author and assign a scene-local encounter to remove this compatibility path.",
                encounter);
        }

        private void SubscribeEncounterCallbacks()
        {
            UnsubscribeEncounterCallbacks();
            encounter.EnemyRegistered += OnEncounterEnemyRegistered;
            encounter.EnemyDied += OnEncounterEnemyDied;
        }

        private void OnEncounterEnemyRegistered(EnemyCharacter enemy)
        {
            if (enemy == null || !tutorialEncounterEnemies.Add(enemy))
                return;

            enemy.OnAttackNotifyShown += OnTutorialEnemyAttackNotifyShown;
        }

        private void OnTutorialEnemyAttackNotifyShown(EnemyCharacter enemy)
        {
            if (currentStage != Stage.EnemyWave || parryTutorialPromptUsed || enemy == null)
                return;

            parryTutorialPromptUsed = true;
            parryTutorialRoutine = StartCoroutine(RunParryTutorialPrompt(enemy));
        }

        private IEnumerator RunParryTutorialPrompt(EnemyCharacter enemy)
        {
            enemy.SetTutorialFrozen(true);

            if (GameInputManager.Instance != null)
                GameInputManager.Instance.SetGameplayInputEnabled(false);

            if (parryPromptDialogue != null)
                yield return RunDialogue(parryPromptDialogue);

            parryTutorialPressed = false;
            if (GameInputManager.Instance != null)
            {
                GameInputManager.Instance.SetParryOnlyInputEnabled(true);
                GameInputManager.Instance.OnParry += OnTutorialParryPressed;
            }

            while (!parryTutorialPressed)
                yield return null;

            if (GameInputManager.Instance != null)
            {
                GameInputManager.Instance.OnParry -= OnTutorialParryPressed;
                GameInputManager.Instance.SetParryOnlyInputEnabled(false);
                GameInputManager.Instance.SetGameplayInputEnabled(true);
            }

            if (enemy != null)
                enemy.SetTutorialFrozen(false);

            parryTutorialRoutine = null;
        }

        private void OnTutorialParryPressed() => parryTutorialPressed = true;

        private void OnEncounterEnemyDied(EnemyCharacter enemy)
        {
            if (enemy != null && tutorialEncounterEnemies.Remove(enemy))
                enemy.OnAttackNotifyShown -= OnTutorialEnemyAttackNotifyShown;

            if (enemy != null && enemy.EnemyType == EnemyType.Hyena)
                SpawnOrActivateTutorialModAfterHyenaDeath();
        }

        private void SpawnOrActivateTutorialModAfterHyenaDeath()
        {
            if (tutorialModHyenaSpawnHandled)
                return;
            if (tutorialModPickup == null)
                return;
            if (tutorialModPickupSpawnPoint == null)
            {
                Debug.LogWarning("[Level0Sequence] tutorialModPickupSpawnPoint not assigned — cannot place mod pickup.");
                return;
            }

            tutorialModHyenaSpawnHandled = true;

            if (!tutorialModPickup.scene.IsValid())
            {
                Instantiate(tutorialModPickup, tutorialModPickupSpawnPoint.position,
                    tutorialModPickupSpawnPoint.rotation);
                return;
            }

            tutorialModPickup.transform.SetPositionAndRotation(tutorialModPickupSpawnPoint.position,
                tutorialModPickupSpawnPoint.rotation);
            tutorialModPickup.SetActive(true);
        }

        private void UnsubscribeEncounterCallbacks()
        {
            if (encounter != null)
            {
                encounter.EnemyRegistered -= OnEncounterEnemyRegistered;
                encounter.EnemyDied -= OnEncounterEnemyDied;
            }

            foreach (EnemyCharacter enemy in tutorialEncounterEnemies)
            {
                if (enemy == null)
                    continue;

                enemy.OnAttackNotifyShown -= OnTutorialEnemyAttackNotifyShown;
                enemy.SetTutorialFrozen(false);
            }

            tutorialEncounterEnemies.Clear();
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

            if (currentWeaponLoadout == null)
            {
                Debug.LogWarning("[Level0Sequence] No PlayerWeaponLoadout found on current player.");
                yield break;
            }

            weaponPickedUp = false;
            currentWeaponLoadout.WeaponChanged += OnWeaponPickedUp;

            while (!weaponPickedUp)
                yield return null;

            currentWeaponLoadout.WeaponChanged -= OnWeaponPickedUp;
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

        private IEnumerator WaitForInventoryClose()
        {
            inventoryClosed = false;
            GameInputManager.Instance.OnInventoryToggle += OnInventoryClosedToggled;

            while (!inventoryClosed)
                yield return null;

            GameInputManager.Instance.OnInventoryToggle -= OnInventoryClosedToggled;
        }

        private void OnInventoryClosedToggled() => inventoryClosed = true;

        private IEnumerator WaitForActiveModActivation()
        {
            RefreshPlayerRef();

            if (currentModManager == null)
            {
                Debug.LogWarning("[Level0Sequence] No ModManager found on current player.");
                yield break;
            }

            modActivated = false;
            currentModManager.OnActiveModActivated += OnActiveModActivated;

            while (!modActivated)
                yield return null;

            currentModManager.OnActiveModActivated -= OnActiveModActivated;
        }

        private void OnActiveModActivated(int _) => modActivated = true;

        private IEnumerator WaitForModActivationAnimationComplete()
        {
            RefreshPlayerRef();

            if (currentSpineAnimationController == null)
                yield break;

            yield return null;

            while (currentSpineAnimationController.IsForceOverrideActive)
                yield return null;
        }

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

        #region End cinematic (glitch + outro video)

        private struct EndGlitchSnapshot
        {
            private readonly bool _hasAnalog;
            private readonly bool _analogActive;
            private readonly float _as, _av, _ah, _ac;
            private readonly bool _osS, _osV, _osH, _osC;

            private readonly bool _hasDigital;
            private readonly bool _digitalActive;
            private readonly float _di;
            private readonly bool _osI;

            public EndGlitchSnapshot(URPGlitch.AnalogGlitchVolume a, URPGlitch.DigitalGlitchVolume d)
            {
                _hasAnalog = a != null;
                _analogActive = false;
                _as = _av = _ah = _ac = 0f;
                _osS = _osV = _osH = _osC = false;
                _hasDigital = d != null;
                _digitalActive = false;
                _di = 0f;
                _osI = false;

                if (_hasAnalog)
                {
                    _analogActive = a.active;
                    _as = a.scanLineJitter.value;
                    _av = a.verticalJump.value;
                    _ah = a.horizontalShake.value;
                    _ac = a.colorDrift.value;
                    _osS = a.scanLineJitter.overrideState;
                    _osV = a.verticalJump.overrideState;
                    _osH = a.horizontalShake.overrideState;
                    _osC = a.colorDrift.overrideState;
                }

                if (_hasDigital)
                {
                    _digitalActive = d.active;
                    _di = d.intensity.value;
                    _osI = d.intensity.overrideState;
                }
            }

            public void Restore(URPGlitch.AnalogGlitchVolume a, URPGlitch.DigitalGlitchVolume d)
            {
                if (_hasAnalog && a != null)
                {
                    a.active = _analogActive;
                    a.scanLineJitter.value = _as;
                    a.verticalJump.value = _av;
                    a.horizontalShake.value = _ah;
                    a.colorDrift.value = _ac;
                    a.scanLineJitter.overrideState = _osS;
                    a.verticalJump.overrideState = _osV;
                    a.horizontalShake.overrideState = _osH;
                    a.colorDrift.overrideState = _osC;
                }

                if (_hasDigital && d != null)
                {
                    d.active = _digitalActive;
                    d.intensity.value = _di;
                    d.intensity.overrideState = _osI;
                }
            }
        }

        private IEnumerator RunEndGlitchRampIfConfigured()
        {
            if (endSequenceVolumeSettings == null || !endSequenceVolumeSettings.IsValid)
                yield break;

            VolumeProfile profile = endSequenceVolumeSettings.Profile;
            profile.TryGet(out URPGlitch.AnalogGlitchVolume analog);
            profile.TryGet(out URPGlitch.DigitalGlitchVolume digital);

            if (analog == null && digital == null)
            {
                Debug.LogWarning(
                    "[Level0Sequence] Volume profile has no AnalogGlitchVolume or DigitalGlitchVolume — skip end glitch ramp.");
                yield break;
            }

            var snapshot = new EndGlitchSnapshot(analog, digital);

            try
            {
                if (analog != null)
                {
                    analog.active = true;
                    analog.scanLineJitter.overrideState = true;
                    analog.verticalJump.overrideState = true;
                    analog.horizontalShake.overrideState = true;
                    analog.colorDrift.overrideState = true;
                }

                if (digital != null)
                {
                    digital.active = true;
                    digital.intensity.overrideState = true;
                }

                float inDur = Mathf.Max(0.01f, endGlitchRampInSeconds);
                float hold = Mathf.Max(0f, endGlitchHoldSeconds);
                float outDur = Mathf.Max(0.01f, endGlitchRampOutSeconds);

                float elapsed = 0f;
                while (elapsed < inDur)
                {
                    elapsed += Time.deltaTime;
                    float k = Mathf.Clamp01(elapsed / inDur);
                    ApplyEndGlitchStrength(analog, digital, k);
                    yield return null;
                }

                ApplyEndGlitchStrength(analog, digital, 1f);

                elapsed = 0f;
                while (elapsed < hold)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                elapsed = 0f;
                while (elapsed < outDur)
                {
                    elapsed += Time.deltaTime;
                    float k = 1f - Mathf.Clamp01(elapsed / outDur);
                    ApplyEndGlitchStrength(analog, digital, k);
                    yield return null;
                }

                ApplyEndGlitchStrength(analog, digital, 0f);
            }
            finally
            {
                snapshot.Restore(analog, digital);
            }
        }

        private void ApplyEndGlitchStrength(URPGlitch.AnalogGlitchVolume analog, URPGlitch.DigitalGlitchVolume digital, float strength01)
        {
            float ap = Mathf.Clamp01(endGlitchAnalogPeak);
            float dp = Mathf.Clamp01(endGlitchDigitalPeak);
            strength01 = Mathf.Clamp01(strength01);

            if (analog != null)
            {
                float v = strength01 * ap;
                analog.scanLineJitter.value = v;
                analog.verticalJump.value = v;
                analog.horizontalShake.value = v;
                analog.colorDrift.value = v;
            }

            if (digital != null)
                digital.intensity.value = strength01 * dp;
        }

        private IEnumerator PlayEndOutroVideoIfConfigured()
        {
            if (endOutroVideoClip == null || endOutroVideoPlayer == null)
            {
                Debug.LogWarning("[Level0Sequence] End outro video clip or VideoPlayer not assigned — skipping outro.");
                yield break;
            }

            endOutroVideoScreenRoot?.SetActive(true);

            endOutroVideoPlayer.playOnAwake = false;
            endOutroVideoPlayer.isLooping = false;
            endOutroVideoPlayer.clip = endOutroVideoClip;

            var audioSource = endOutroVideoPlayer.GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = endOutroVideoPlayer.gameObject.AddComponent<AudioSource>();

            endOutroVideoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            endOutroVideoPlayer.EnableAudioTrack(0, true);
            endOutroVideoPlayer.SetTargetAudioSource(0, audioSource);

            bool finished = false;
            bool prepared = false;
            bool failed = false;

            void OnLoopPointReached(VideoPlayer _) => finished = true;

            void OnError(VideoPlayer _, string message)
            {
                Debug.LogWarning($"[Level0Sequence] End outro video error: {message}");
                failed = true;
                prepared = true;
                finished = true;
            }

            void OnPrepared(VideoPlayer _) => prepared = true;

            endOutroVideoPlayer.loopPointReached += OnLoopPointReached;
            endOutroVideoPlayer.errorReceived += OnError;
            endOutroVideoPlayer.prepareCompleted += OnPrepared;

            endOutroVideoPlayer.Stop();
            endOutroVideoPlayer.time = 0;
            endOutroVideoPlayer.frame = 0;
            endOutroVideoPlayer.Prepare();

            while (!prepared)
                yield return null;

            endOutroVideoPlayer.prepareCompleted -= OnPrepared;

            if (!failed)
            {
                if (playerUI != null)
                playerUI.SetActive(false);
                Cursor.visible = false;

                endOutroVideoPlayer.Play();

                while (!finished && endOutroVideoPlayer.isPlaying)
                    yield return null;
            }

            endOutroVideoPlayer.loopPointReached -= OnLoopPointReached;
            endOutroVideoPlayer.errorReceived -= OnError;

            endOutroVideoPlayer.Stop();
            endOutroVideoScreenRoot?.SetActive(false);
            if (playerUI != null)
                playerUI.SetActive(true);
            Cursor.visible = true;
        }

        #endregion

        #region Scene Transition

        private void LoadConfiguredScene()
        {
            SetStage(Stage.Done);

            if (GameManager.Instance == null)
            {
                Debug.LogError("[Level0Sequence] GameManager missing. Cannot load next scene.");
                return;
            }

            if (nextSceneBuildIndex >= 0)
            {
                Debug.Log($"[Level0Sequence] Loading next scene by build index: {nextSceneBuildIndex}");
                GameManager.Instance.LoadLevel(nextSceneBuildIndex);
                return;
            }

            if (!string.IsNullOrEmpty(nextSceneName))
            {
                Debug.Log($"[Level0Sequence] Loading next scene by name: {nextSceneName}");
                GameManager.Instance.LoadLevel(nextSceneName);
                return;
            }

            int fallbackIndex = SceneManager.GetActiveScene().buildIndex + 1;
            if (fallbackIndex >= 0 && fallbackIndex < SceneManager.sceneCountInBuildSettings)
            {
                Debug.LogWarning($"[Level0Sequence] Next scene not configured. Falling back to next build index: {fallbackIndex}");
                GameManager.Instance.LoadLevel(fallbackIndex);
                return;
            }

            Debug.LogWarning("[Level0Sequence] No next scene configured and no valid fallback index.");
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
            GUILayout.Label($"Enemies alive: {(encounter != null ? encounter.AliveEnemyCount : 0)}");
            GUILayout.Label($"Next scene:    {(string.IsNullOrEmpty(nextSceneName) ? "(not set)" : nextSceneName)}");
            GUILayout.EndArea();
        }
#endif

        #endregion
    }
}
