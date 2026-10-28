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
        [Tooltip("Platform the player starts on; dissolves in when the lights come up.")]
        [SerializeField] private DissolveController startPlatformDissolver;
        [Tooltip("Flat ambient colour the room fades to when the lights come on. The intro starts in black.")]
        [SerializeField] private Color revealedAmbientColor = new Color(0.12f, 0.15f, 0.17f, 1f);
        [SerializeField] private float ambientRevealDuration = 2.5f;

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
        [SerializeField] private DialogueSequence postEnemyDialogue;
        [SerializeField] private DialogueSequence inventoryPromptDialogue;
        [SerializeField] private DialogueSequence finalDialogue;

        [Header("Tutorial Prompts")]
        [Tooltip("Optic HUD prompt. Created at runtime when left empty.")]
        [SerializeField] private TutorialPromptHUD promptHud;

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
            EnemyWave, WaitModPickup, PostEnemyDialogue, WeaponPickup,
            InventoryPrompt, WaitModSlotted, WaitModActivation,
            FinalGlitchRamp, FinalDialogue, OutroVideo, LoadNextScene, Done
        }

        private Stage currentStage;
        private PlayerCharacter currentPlayer;
        private Light playerLight;
        private WeaponManager currentWeaponManager;
        private PlayerArmament currentArmament;
        private ModManager currentModManager;
        private SpineAnimationController currentSpineAnimationController;
        private Coroutine revealFadeRoutine;
        private bool platformCompleteBeatRunning;

        private readonly HashSet<EnemyCharacter> tutorialEncounterEnemies = new();
        private WeaponUpgradePickup activeTutorialPickup;
        private bool tutorialModHyenaSpawnHandled;

        private bool dialogueFinished;
        private bool timelineFinished;
        private bool objectiveHit;
        private bool modActivated;
        private bool weaponPickedUp;
        private bool modSlotsChanged;

        // Parry gate: the Hyena can't be hurt until the player lands a parry on it.
        private EnemyCharacter parryGateEnemy;
        private ParryHandler parryGateHandler;
        private bool parryGateCleared;

        private GameObject tutorialModPickupInstance;
        private ModData tutorialModData;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            Time.timeScale = 1f;

            if (platformStep2 != null) platformStep2.SetActive(false);
            if (platformStep3 != null) platformStep3.SetActive(false);
            if (overheadSpotlight != null) overheadSpotlight.enabled = false;
            SetAmbient(Color.black);
            ResetEndGlitch();
            if (startPlatformDissolver != null) startPlatformDissolver.SetImmediate(2f);
            if (promptHud == null)
                promptHud = new GameObject("TutorialPromptHUD").AddComponent<TutorialPromptHUD>();
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
            // Stopped/reloaded mid-ramp: the coroutine's finally never runs, and the profile is a shared asset.
            ResetEndGlitch();
            if (currentPlayer != null) currentPlayer.CannotDie = false;

            if (DialogueManager.instance != null)
            {
                DialogueManager.instance.OnDialogueEnded -= OnDialogueEnded;
                DialogueManager.instance.IsContinueInputSuppressed = false;
            }

            if (director != null) director.stopped -= OnTimelineStopped;
            if (platformCompleteDirector != null) platformCompleteDirector.stopped -= OnTimelineStopped;
            if (endCinematicDirector != null) endCinematicDirector.stopped -= OnTimelineStopped;
            if (completionAnimation != null) completionAnimation.Stop();
            if (currentArmament != null) currentArmament.WeaponChanged -= OnWeaponPickedUp;
            if (currentModManager != null)
            {
                currentModManager.OnActiveModActivated -= OnActiveModActivated;
                currentModManager.OnModSlotsChanged -= OnModSlotsChanged;
            }
            if (activeTutorialPickup != null) Destroy(activeTutorialPickup.gameObject);
            EndParryGate(cleared: false);
            if (revealFadeRoutine != null) StopCoroutine(revealFadeRoutine);

        }

        #endregion

        #region Main Sequence

        private IEnumerator RunSequence()
        {
            SetStage(Stage.WaitingForPlayer);
            yield return WaitForPlayer();
            // The simulation never kills Cassidy.
            if (currentPlayer != null) currentPlayer.CannotDie = true;

            playerLight = currentPlayer != null
                ? currentPlayer.GetComponentInChildren<Light>(true)
                : null;
            if (playerLight != null) playerLight.enabled = false;

            SetStage(Stage.IntroDialogue);
            if (introDialogue != null)
                yield return RunDialogue(introDialogue);

            // Crow brings the lights up: volumetric spot, ambient and reflections fade in together.
            if (playerLight != null) playerLight.enabled = true;
            if (overheadSpotlight != null) overheadSpotlight.enabled = true;
            if (startPlatformDissolver != null) startPlatformDissolver.Undissolve();
            PlayCinematicSfx(introSequenceSfx, introSfxPoint);
            PlayCinematicSfx(lightRevealSfx);
            if (godRayParticles != null)
            {
                godRayParticles.gameObject.SetActive(true);
                godRayParticles.Play(true);
            }
            StartCoroutine(LerpReflectionIntensity(RenderSettings.reflectionIntensity, 1f, reflectionRevealDuration));
            StartCoroutine(LerpAmbient(RenderSettings.ambientLight, revealedAmbientColor, ambientRevealDuration));

            SetStage(Stage.PlayTimeline);
            if (director != null)
                yield return RunDirector(director, rewindToStart: true);

            // The spotlight fades once the platform has arrived.
            if (revealFadeRoutine != null) StopCoroutine(revealFadeRoutine);
            revealFadeRoutine = StartCoroutine(FadeOutRevealEffects());

            PlayCinematicSfx(platformDissolveSfx, platformStep1DissolveSfxPoint);

            SetStage(Stage.TutorialDialogue);
            if (postCinematicDialogue != null)
                yield return RunDialogue(postCinematicDialogue);

            SetStage(Stage.ObjectiveOne);
            yield return RunObjective(objective1Trigger,
                new TutorialPromptHUD.Entry("Player/Move", "Move", keyboardOverride: "A / D", gamepadOverride: "L-Stick"),
                new TutorialPromptHUD.Entry("Player/Jump", "Jump"));

            SetStage(Stage.PlatformOneComplete);
            yield return PlayCompletionBeatAndReset(platformStep1);
            RevealPlatformStep(platformStep2, platformStep2Dissolver, platformStep2DissolveSfxPoint);

            SetStage(Stage.SecondPlatformDialogue);
            if (secondPlatformDialogue != null)
                yield return RunDialogue(secondPlatformDialogue);

            SetStage(Stage.ObjectiveTwo);
            yield return RunObjective(objective2Trigger,
                new TutorialPromptHUD.Entry("Player/Jump", "Double jump", suffix: "x2"));

            SetStage(Stage.PlatformTwoComplete);
            yield return PlayCompletionBeatAndReset(platformStep2);
            RevealPlatformStep(platformStep3, platformStep3Dissolver, platformStep3DissolveSfxPoint);

            SetStage(Stage.ThirdPlatformDialogue);
            if (thirdPlatformDialogue != null)
                yield return RunDialogue(thirdPlatformDialogue);

            SetStage(Stage.ObjectiveThree);
            // The gap needs both: double jump for height, dash for distance.
            yield return RunObjective(objective3Trigger,
                new TutorialPromptHUD.Entry("Player/Jump", "Double jump", suffix: "x2"),
                new TutorialPromptHUD.Entry("Player/Dash", "Dash"));

            SetStage(Stage.PlatformThreeComplete);
            yield return PlayCompletionBeatAndReset(platformStep3);

            SetStage(Stage.CombatIntroDialogue);
            if (combatIntroDialogue != null)
                yield return RunDialogue(combatIntroDialogue);

            SetStage(Stage.EnemyWave);
            if (!PrepareEncounter())
                yield break;

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

            SetStage(Stage.WaitModPickup);
            yield return WaitForTutorialModPickup();

            RepositionPlayer(centerRoomSpawn);
            SetStage(Stage.PostEnemyDialogue);
            if (postEnemyDialogue != null)
                yield return RunDialogue(postEnemyDialogue);

            SetStage(Stage.WeaponPickup);
            SpawnTutorialWeaponPickup();
            promptHud.Show(new TutorialPromptHUD.Entry("Player/Interact", "Equip the weapon"));
            yield return WaitForWeaponPickup();
            promptHud.Complete();

            SetStage(Stage.InventoryPrompt);
            if (inventoryPromptDialogue != null)
                yield return RunDialogue(inventoryPromptDialogue);

            SetStage(Stage.WaitModSlotted);
            yield return RunInventoryModTutorial();

            SetStage(Stage.WaitModActivation);
            yield return RunModActivationPrompt();

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

        private IEnumerator RunObjective(SequenceTrigger trigger, params TutorialPromptHUD.Entry[] prompt)
        {
            promptHud.Show(prompt);
            if (trigger != null)
                yield return WaitForTrigger(trigger);
            promptHud.Complete();
        }

        private void RevealPlatformStep(GameObject step, TutorialStepDissolver dissolver, Transform sfxPoint)
        {
            if (step != null) step.SetActive(true);
            if (dissolver == null) return;

            PlayCinematicSfx(platformDissolveSfx, sfxPoint);
            dissolver.UndissolveAll();
        }

        private void SetStage(Stage s)
        {
            currentStage = s;
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

            Vector3 target = spawnPoint.position;

            currentPlayer.ReviveAt(target);
            currentPlayer.Activate();

            if (CameraManager.Instance != null)
            {
                CameraManager.Instance.ConnectToPlayer(currentPlayer);
            }
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

        }

        private void RefreshPlayerRef()
        {
            if (PlayerLifecycle.Instance?.Player != null)
                currentPlayer = PlayerLifecycle.Instance.Player;

            if (currentPlayer != null)
            {
                currentWeaponManager = currentPlayer.GetComponentInChildren<WeaponManager>(true);
                currentArmament = currentWeaponManager != null
                    ? currentWeaponManager.Armament
                    : currentPlayer.GetComponentInChildren<PlayerArmament>(true);
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
                yield break;
            }

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

        private bool PrepareEncounter()
        {
            parryGateCleared = false;
            tutorialModHyenaSpawnHandled = false;

            if (encounter != null)
                return true;

            Debug.LogError(
                "[Level0Sequence] An authored EncounterController is required before combat can start.",
                this);
            return false;
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

            if (!parryGateCleared && parryGateEnemy == null && enemy.EnemyType == EnemyType.Hyena)
                BeginParryGate(enemy);
        }

        // No pause and no input locks: the Hyena just can't be hurt, and the prompt stays,
        // until the player lands a successful parry on it.
        private void BeginParryGate(EnemyCharacter enemy)
        {
            RefreshPlayerRef();
            parryGateHandler = currentPlayer != null ? currentPlayer.GetComponentInChildren<ParryHandler>(true) : null;
            if (parryGateHandler == null)
            {
                Debug.LogWarning("[Level0Sequence] No ParryHandler on the player; parry gate skipped.", this);
                return;
            }

            parryGateEnemy = enemy;
            enemy.SetTutorialInvulnerable(true);
            enemy.OnAttackNotifyShown += OnParryGateAttackTelegraphed;
            parryGateHandler.ParrySucceeded += OnTutorialParrySucceeded;
        }

        // The prompt appears on the Hyena's first telegraph, so it teaches the timing; it then stays until cleared.
        private void OnParryGateAttackTelegraphed(EnemyCharacter enemy)
        {
            if (enemy != parryGateEnemy || promptHud.IsShowing)
                return;

            promptHud.Show(new TutorialPromptHUD.Entry("Player/Parry", "Parry"));
        }

        private void OnTutorialParrySucceeded(GameObject attacker)
        {
            if (parryGateEnemy == null)
                return;

            // Ignore parries against other enemies; accept source-less hits.
            EnemyCharacter parried = attacker != null ? attacker.GetComponentInParent<EnemyCharacter>() : null;
            if (attacker != null && parried != parryGateEnemy)
                return;

            EndParryGate(cleared: true);
        }

        private void EndParryGate(bool cleared)
        {
            if (parryGateHandler != null)
                parryGateHandler.ParrySucceeded -= OnTutorialParrySucceeded;
            if (parryGateEnemy != null)
            {
                parryGateEnemy.SetTutorialInvulnerable(false);
                parryGateEnemy.OnAttackNotifyShown -= OnParryGateAttackTelegraphed;
            }

            bool wasActive = parryGateEnemy != null;
            parryGateHandler = null;
            parryGateEnemy = null;
            if (!wasActive)
                return;

            if (cleared)
                parryGateCleared = true;

            if (promptHud == null || !promptHud.IsShowing)
                return;

            if (cleared)
                promptHud.Complete();
            else
                promptHud.Hide(instant: true);
        }

        private void OnEncounterEnemyDied(EnemyCharacter enemy)
        {
            if (enemy != null)
                tutorialEncounterEnemies.Remove(enemy);

            if (enemy != null && enemy == parryGateEnemy)
                EndParryGate(cleared: false);

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
                tutorialModPickupInstance = Instantiate(tutorialModPickup, tutorialModPickupSpawnPoint.position,
                    tutorialModPickupSpawnPoint.rotation);
            }
            else
            {
                tutorialModPickupInstance = tutorialModPickup;
                tutorialModPickup.transform.SetPositionAndRotation(tutorialModPickupSpawnPoint.position,
                    tutorialModPickupSpawnPoint.rotation);
                tutorialModPickup.SetActive(true);
            }

            // The tutorial teaches slotting, so this mod goes to the inventory instead of auto-equipping.
            var pickup = tutorialModPickupInstance.GetComponent<WorldModPickup>();
            if (pickup != null)
            {
                pickup.autoEquip = false;
                tutorialModData = pickup.modData;
            }
        }

        private IEnumerator WaitForTutorialModPickup()
        {
            if (tutorialModPickupInstance == null || !tutorialModPickupInstance.activeInHierarchy)
                yield break;

            promptHud.Show(new TutorialPromptHUD.Entry(null, "Collect the mod"));
            while (tutorialModPickupInstance != null && tutorialModPickupInstance.activeInHierarchy)
                yield return null;
            promptHud.Complete();
        }

        private void UnsubscribeEncounterCallbacks()
        {
            if (encounter != null)
            {
                encounter.EnemyRegistered -= OnEncounterEnemyRegistered;
                encounter.EnemyDied -= OnEncounterEnemyDied;
            }

            EndParryGate(cleared: false);
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
            activeTutorialPickup = go.GetComponent<WeaponUpgradePickup>();
        }

        private IEnumerator WaitForWeaponPickup()
        {
            RefreshPlayerRef();

            if (currentArmament == null)
            {
                Debug.LogWarning("[Level0Sequence] No PlayerArmament found on current player.");
                yield break;
            }

            // Already upgraded on an earlier run: the pickup disables itself, so do not wait.
            weaponPickedUp = currentArmament.HasUpgradedWeapon;
            currentArmament.WeaponChanged += OnWeaponPickedUp;

            while (!weaponPickedUp)
                yield return null;

            currentArmament.WeaponChanged -= OnWeaponPickedUp;
            activeTutorialPickup = null;
        }

        private void OnWeaponPickedUp() => weaponPickedUp = true;

        #endregion

        #region Combat & Inventory Gates

        private static bool IsInventoryOpen =>
            GameUIManager.Instance != null && GameUIManager.Instance.PlayerHUD != null &&
            GameUIManager.Instance.PlayerHUD.IsInventoryOpen;

        private bool IsTutorialModSlotted(out bool isActiveSlot, out int slotIndex)
        {
            isActiveSlot = false;
            slotIndex = -1;
            return currentModManager != null && tutorialModData != null &&
                   currentModManager.FindEquippedMod(tutorialModData, out isActiveSlot, out slotIndex);
        }

        /// <summary>
        /// Open inventory → slot the Hyena mod → close. Each prompt stays until done, and closing
        /// the inventory early just asks the player to reopen it.
        /// </summary>
        private IEnumerator RunInventoryModTutorial()
        {
            RefreshPlayerRef();
            var openPrompt = new TutorialPromptHUD.Entry("Player/Inventory", "Open inventory");
            yield return RunInventoryGates(openPrompt);
        }

        private IEnumerator RunInventoryGates(TutorialPromptHUD.Entry openPrompt)
        {
            if (currentModManager == null || tutorialModData == null)
            {
                // No mod to slot (e.g. no Hyena in the wave): still teach open/close.
                promptHud.Show(openPrompt);
                while (!IsInventoryOpen) yield return null;
                promptHud.Complete();
            }
            else
            {
                modSlotsChanged = true;
                currentModManager.OnModSlotsChanged += OnModSlotsChanged;

                while (!IsTutorialModSlotted(out _, out _))
                {
                    if (!IsInventoryOpen)
                    {
                        promptHud.Show(openPrompt);
                        while (!IsInventoryOpen) yield return null;
                        promptHud.Complete();
                    }

                    promptHud.Show(new TutorialPromptHUD.Entry(null, $"Attach {ModLabel()} to a mod slot"));
                    promptHud.SetMenuDocked(true);
                    modSlotsChanged = false;
                    while (IsInventoryOpen && !(modSlotsChanged && IsTutorialModSlotted(out _, out _)))
                        yield return null;

                    if (IsTutorialModSlotted(out _, out _))
                        promptHud.Complete();
                }

                currentModManager.OnModSlotsChanged -= OnModSlotsChanged;
            }

            if (IsInventoryOpen)
            {
                promptHud.Show(new TutorialPromptHUD.Entry("Player/Inventory", "Close inventory"));
                promptHud.SetMenuDocked(true);
                while (IsInventoryOpen) yield return null;
                promptHud.Complete();
            }
        }

        private string ModLabel() =>
            tutorialModData != null && !string.IsNullOrEmpty(tutorialModData.modName)
                ? tutorialModData.modName
                : "the mod";

        private void OnModSlotsChanged() => modSlotsChanged = true;

        private IEnumerator RunModActivationPrompt()
        {
            RefreshPlayerRef();
            if (!IsTutorialModSlotted(out bool isActiveSlot, out int slotIndex) || !isActiveSlot || slotIndex > 3)
                yield break;

            promptHud.Show(new TutorialPromptHUD.Entry($"Player/ModActivate{slotIndex + 1}", "Activate mod"));
            yield return WaitForActiveModActivation();
            promptHud.Complete();
            yield return WaitForModActivationAnimationComplete();
        }

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

        private IEnumerator LerpAmbient(Color from, Color to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                SetAmbient(Color.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
                yield return null;
            }
            SetAmbient(to);
        }

        // Shaders read the SH ambient probe, which isn't rebuilt when ambientLight changes at runtime,
        // so write a flat probe directly (cheap enough to do every frame of the fade).
        private static void SetAmbient(Color color)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = color;
            var probe = new SphericalHarmonicsL2();
            probe.AddAmbientLight(color);
            RenderSettings.ambientProbe = probe;
        }

        private IEnumerator FadeOutRevealEffects()
        {
            Light fadeSpotlight = overheadSpotlight;
            float startSpotlightIntensity = fadeSpotlight != null ? fadeSpotlight.intensity : 0f;
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
                if (fadeSpotlight != null)
                {
                    fadeSpotlight.intensity = 0f;
                    fadeSpotlight.enabled = false;
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

                if (fadeSpotlight != null)
                    fadeSpotlight.intensity = Mathf.Lerp(startSpotlightIntensity, 0f, t);

                if (hasGodRayParticles)
                    emission.rateOverTimeMultiplier = Mathf.Lerp(startRate, 0f, t);

                yield return null;
            }

            if (fadeSpotlight != null)
            {
                fadeSpotlight.intensity = 0f;
                fadeSpotlight.enabled = false;
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

        private void ResetEndGlitch()
        {
            if (endSequenceVolumeSettings == null || !endSequenceVolumeSettings.IsValid)
                return;
            VolumeProfile profile = endSequenceVolumeSettings.Profile;
            profile.TryGet(out URPGlitch.AnalogGlitchVolume analog);
            profile.TryGet(out URPGlitch.DigitalGlitchVolume digital);
            ApplyEndGlitchStrength(analog, digital, 0f);
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
                GameManager.Instance.LoadLevel(nextSceneBuildIndex);
                return;
            }

            if (!string.IsNullOrEmpty(nextSceneName))
            {
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
