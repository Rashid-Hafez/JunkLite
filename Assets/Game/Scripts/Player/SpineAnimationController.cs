using System;
using UnityEngine;
using Spine;
using Spine.Unity;

namespace junklite
{
    /// <summary>
    /// Event-driven Spine animation controller.
    /// Pure animation playback - attack requests come from PlayerState events.
    /// </summary>
    public class SpineAnimationController : MonoBehaviour
    {
        [Header("Spine")]
        [SerializeField] private SkeletonAnimation skeletonAnimation;

        [Header("Tracks")]
        [SerializeField] private int locomotionTrack = 0;
        [SerializeField] private int overlayTrack = 1;

        [Header("Locomotion Animations")]
        [SerializeField] private string idle = "idle";
        [SerializeField] private string run = "run";
        [SerializeField] private string jumpStart = "Jump_1_Start";
        [SerializeField] private string jumpAir = "Jump_2_Air";
        [SerializeField] private string landing = "Jump_3_Land";
        [SerializeField] private string doubleJump = "doubleJump";
        [SerializeField] private string wallSlide = "wallSlide";
        [SerializeField] private string dash = "dash";
        [SerializeField] private string stun = "stun";
        [SerializeField] private string death = "death";

        [Header("Animation Looping")]
        [SerializeField] private bool idleLoop = true;
        [SerializeField] private bool runLoop = true;
        [SerializeField] private bool jumpStartLoop = false;
        [SerializeField] private bool jumpAirLoop = false;
        [SerializeField] private bool landingLoop = false;
        [SerializeField] private bool doubleJumpLoop = false;
        [SerializeField] private bool wallSlideLoop = false;
        [SerializeField] private bool dashLoop = false;
        [SerializeField] private bool stunLoop = false;
        [SerializeField] private bool deathLoop = false;

        [Header("Animation Timing (TimeScale)")]
        [SerializeField, Range(0.1f, 3f)] private float idleTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float runTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float jumpStartTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float jumpAirTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float landingTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float doubleJumpTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float wallSlideTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float dashTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float stunTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float deathTimeScale = 1f;

        [Header("Locomotion Settings")]
        [SerializeField] private float speedThreshold = 0.1f;
        [SerializeField] private float locomotionBlend = 0.1f;
        [SerializeField] private float minRunTimeScale = 0.8f;
        [SerializeField] private float maxRunTimeScale = 1.3f;
        [SerializeField] private float doubleJumpBlend = 0.05f;

        [Header("Attack Animation Settings")]
        [SerializeField] private bool attackOverwrite = false;
        [SerializeField] private float attackMix = 0.1f;
        [SerializeField] private float attackMixOut = 0.25f;
        [SerializeField] private float attackMixOutDelay = 0.05f;
        [Tooltip("State1 = fists. Used when not in mod combat (weapons not equipped).")]
        [SerializeField, Range(0.1f, 3f)] private float fistAttackTimeScale = 1f;
        [Tooltip("State2 = weapons. Used when in mod combat (Q / weapons equipped).")]
        [SerializeField, Range(0.1f, 3f)] private float weaponAttackTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float downAttackTimeScale = 1.4f;

        [Header("Force Override (e.g. GroundPound)")]
        [Tooltip("When grounded, wait this long after the override animation ends before returning to idle/run")]
        [SerializeField] private float forceOverrideGroundedHoldDuration = 0.4f;

        [Header("Mod Combat Entry")]
        [Tooltip("Spine animation played when entering mod combat (Q). Leave empty to skip.")]
        [SerializeField] private string modCombatEntryAnim = "Attack_WolverinePose";
        [Tooltip("VFX prefab spawned at the player when entering mod combat (same ripple as parry).")]
        [SerializeField] private GameObject modEntryVFXPrefab;
        [Tooltip("Fallback animation if the configured mod-entry animation is missing on the skeleton.")]
        [SerializeField] private string modCombatEntryFallbackAnim = "Attack_WolverinePose";

        [Header("Footsteps")]
        [SerializeField] private EventDataReferenceAsset footstepEvent;
        [SerializeField] private AudioSource footstepSource;
        [SerializeField] private float footstepPitchOffset = 0.2f;

        // References
        private PlayerState playerState;
        private Character2D5Controller controller;
        private PlayerAudioHandler audioHandler;
        private WeaponManager weaponManager;

        // State tracking
        private bool wasAirborne = false;
        private string currentLocomotionAnim = "";
        private bool attackActive = false;
        private TrackEntry currentAttackEntry = null;
        private bool forceOverrideActive = false;

        // when an attack animation finishes during a parry, we defer the end until parry state clears
        private bool waitingForParryEnd = false;

        public TrackEntry CurrentAttackEntry => currentAttackEntry;

        public bool TryGetDeathAnimationDuration(out float duration)
        {
            duration = 0f;

            var animation = skeletonAnimation?.Skeleton?.Data?.FindAnimation(death);
            if (animation == null)
                return false;

            duration = animation.Duration / Mathf.Max(0.01f, deathTimeScale);
            return duration > 0f;
        }

        #region Unity Lifecycle

        private void Awake()
        {
            if (!skeletonAnimation)
                skeletonAnimation = GetComponentInChildren<SkeletonAnimation>();

            playerState = GetComponentInParent<PlayerState>();
            controller = GetComponentInParent<Character2D5Controller>();
            audioHandler = GetComponentInParent<PlayerAudioHandler>() ?? GetComponent<PlayerAudioHandler>();
            weaponManager = GetComponentInParent<WeaponManager>();

            if (skeletonAnimation != null)
            {
                skeletonAnimation.Initialize(false);
                ConfigureMixes();
            }
        }

        private void Start()
        {
            if (playerState == null)
            {
                Debug.LogError("[SpineAnim] PlayerState not found!", this);
                return;
            }

            // Subscribe to state events
            playerState.OnGroundedChanged += OnGroundedChanged;
            playerState.OnJumpStateChanged += OnJumpStateChanged;
            playerState.OnDashingChanged += OnDashingChanged;
            playerState.OnWallSlideChanged += OnWallSlideChanged;
            playerState.OnLedgeDetectedChanged += OnLedgeDetectedChanged;
            playerState.OnParryChanged += OnParryChanged;
            playerState.OnDoubleJumpChanged += OnDoubleJumpChanged;
            playerState.OnStunnedChanged += OnStunnedChanged;
            playerState.OnDeath += OnDeath;
            playerState.OnAttackingChanged += OnAttackingChanged;

            playerState.OnAttackAnimationRequested += OnAttackAnimationRequested; // new

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.Event += HandleSpineEvent;

            if (controller != null)
                controller.OnDoubleJumpPerformed += OnControllerDoubleJumpPerformed;

            if (weaponManager != null)
                weaponManager.OnCombatModeChanged += HandleCombatModeChanged;

            PlayLocomotion(idle, true);
        }

        private void Update()
        {
            if (skeletonAnimation == null || controller == null || playerState == null)
                return;

            SyncCurrentLocomotion();
            ApplyAnyStateFallbacks();
            UpdateLocomotionFromSpeed();
            UpdateRunSpeed();
        }

        private void OnDestroy()
        {
            if (playerState != null)
            {
                playerState.OnGroundedChanged -= OnGroundedChanged;
                playerState.OnJumpStateChanged -= OnJumpStateChanged;
                playerState.OnDashingChanged -= OnDashingChanged;
                playerState.OnWallSlideChanged -= OnWallSlideChanged;
                playerState.OnLedgeDetectedChanged -= OnLedgeDetectedChanged;
                playerState.OnParryChanged -= OnParryChanged;
                playerState.OnDoubleJumpChanged -= OnDoubleJumpChanged;
                playerState.OnStunnedChanged -= OnStunnedChanged;
                playerState.OnDeath -= OnDeath;
                playerState.OnAttackingChanged -= OnAttackingChanged;
                playerState.OnAttackAnimationRequested -= OnAttackAnimationRequested;
            }

            if (controller != null)
                controller.OnDoubleJumpPerformed -= OnControllerDoubleJumpPerformed;

            if (weaponManager != null)
                weaponManager.OnCombatModeChanged -= HandleCombatModeChanged;

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.Event -= HandleSpineEvent;
        }

        #endregion

        #region Attack Animation (Public API)

        /// <summary>
        /// Plays an attack animation by name. Called via PlayerState request.
        /// </summary>
        public void PlayAttackAnimation(string animationName)
        {
            if (string.IsNullOrEmpty(animationName))
            {
                NotifyPlayerAttackComplete();
                return;
            }

            if (!HasAnimation(animationName))
            {
                NotifyPlayerAttackComplete();
                return;
            }

            if (attackActive)
            {
                ForceFinishAttack();
            }

            // Special-case: ensure the parry hit animation fully replaces everything
            // and does not blend bones. This prevents head twisting from blending.
            if (string.Equals(animationName, "Perry_2", System.StringComparison.OrdinalIgnoreCase))
            {
                bool has = HasAnimation(animationName);
                if (!has)
                {
                    Debug.LogWarning("Perry_2 not found on skeleton data! Check naming.");
                }
                attackActive = true;
                // clear overlay so it cannot influence locomotion bones (we'll play on overlay)
                skeletonAnimation?.AnimationState.ClearTrack(overlayTrack);
                var entry = skeletonAnimation.AnimationState.SetAnimation(overlayTrack, animationName, false);
                if (entry == null)
                {
                    Debug.LogWarning("SetAnimation returned null entry for Perry_2");
                }
                // immediate replace (no blend) to avoid bone mixing
                if (entry != null)
                {
                    entry.MixDuration = 0f;
                    entry.MixBlend = MixBlend.Replace;
                    // standard time scale (no global compensation)
                    entry.TimeScale = GetTimeScaleFor(animationName, 1f);
                    entry.Complete += _ => FinishAttackOverwrite();
                    entry.Interrupt += _ => OnAttackInterrupted();
                }
                currentAttackEntry = entry;
                return;
            }

            attackActive = true;

            bool isWeaponState = weaponManager != null && weaponManager.IsModCombat;
            float timeScale = isWeaponState ? weaponAttackTimeScale : fistAttackTimeScale;
            if (playerState != null && playerState.IsDownAttackRequested)
                timeScale = downAttackTimeScale;

            if (attackOverwrite)
            {
                var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, animationName, false);
                entry.MixDuration = attackMix;
                entry.MixBlend = MixBlend.Replace;
                entry.TimeScale = timeScale;
                entry.Complete += _ => FinishAttackOverwrite();
                entry.Interrupt += _ => OnAttackInterrupted();
                currentAttackEntry = entry;
            }
            else
            {
                var entry = skeletonAnimation.AnimationState.SetAnimation(overlayTrack, animationName, false);
                entry.MixDuration = attackMix;
                entry.MixBlend = MixBlend.Replace;
                entry.TimeScale = timeScale;
                entry.Complete += _ => FinishAttackOverlay();
                entry.Interrupt += _ => OnAttackInterrupted();
                currentAttackEntry = entry;
            }
        }

        private void FinishAttackOverlay()
        {
            if (!attackActive) return;

            // hold last frame if parry still active
            if (playerState != null && playerState.IsParrying)
            {
                waitingForParryEnd = true;
                return;
            }

            attackActive = false;
            currentAttackEntry = null;

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.AddEmptyAnimation(overlayTrack, attackMixOut, attackMixOutDelay);

            NotifyPlayerAttackComplete();
        }

        private void FinishAttackOverwrite()
        {
            if (!attackActive) return;

            // hold last frame if parry still active
            if (playerState != null && playerState.IsParrying)
            {
                waitingForParryEnd = true;
                return;
            }

            attackActive = false;
            currentAttackEntry = null;

            // Return to appropriate locomotion
            if (playerState != null)
            {
                if (playerState.IsGrounded)
                {
                    float speed = GetSpeed();
                    PlayLocomotion(speed > speedThreshold ? run : idle, true);
                }
                else
                {
                    PlayJumpAir();
                }
            }

            NotifyPlayerAttackComplete();
        }

        private void OnAttackInterrupted()
        {
            if (!attackActive) return;

            attackActive = false;
            currentAttackEntry = null;

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.ClearTrack(overlayTrack);

            if (playerState != null)
                playerState.NotifyAttackAnimationInterrupted();
        }

        private void ForceFinishAttack()
        {
            attackActive = false;
            currentAttackEntry = null;

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.ClearTrack(overlayTrack);

            NotifyPlayerAttackComplete();
        }

        private void NotifyPlayerAttackComplete()
        {
            if (playerState != null)
                playerState.NotifyAttackAnimationComplete();
        }

        /// <summary>
        /// Force-play an animation on the locomotion track with no blend. Used by mods (e.g. Phantom Strike GroundPound).
        /// Blocks normal locomotion until the animation completes, then restores idle/run or jump air.
        /// </summary>
        /// <param name="animationName">Spine animation name (e.g. "GroundPound")</param>
        /// <param name="loop">Whether to loop</param>
        /// <param name="onComplete">Called when the animation finishes</param>
        /// <returns>True if the animation was started, false if not found or override already active</returns>
        public bool ForcePlayOverride(string animationName, bool loop, Action onComplete)
        {
            if (skeletonAnimation == null || string.IsNullOrEmpty(animationName))
            {
                onComplete?.Invoke();
                return false;
            }

            if (!HasAnimation(animationName))
            {
                onComplete?.Invoke();
                return false;
            }

            forceOverrideActive = true;
            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, animationName, loop);
            entry.MixDuration = 0f;
            entry.MixBlend = MixBlend.Replace;
            entry.TimeScale = GetTimeScaleFor(animationName, 1f);
            currentLocomotionAnim = animationName;
            entry.Complete += _ =>
            {
                forceOverrideActive = false;
                if (playerState != null && playerState.IsAlive)
                {
                    if (playerState.IsGrounded && forceOverrideGroundedHoldDuration > 0f)
                    {
                        StartCoroutine(CoDelayedRestoreAfterForceOverride(onComplete));
                        return;
                    }
                    if (playerState.IsGrounded)
                    {
                        float speed = GetSpeed();
                        PlayLocomotion(speed > speedThreshold ? run : idle, true);
                    }
                    else
                    {
                        PlayJumpAir();
                    }
                }
                onComplete?.Invoke();
            };
            entry.Interrupt += _ =>
            {
                forceOverrideActive = false;
                onComplete?.Invoke();
            };
            return true;
        }

        private System.Collections.IEnumerator CoDelayedRestoreAfterForceOverride(Action onComplete)
        {
            yield return new WaitForSeconds(forceOverrideGroundedHoldDuration);
            if (playerState != null && playerState.IsAlive && playerState.IsGrounded)
            {
                float speed = GetSpeed();
                PlayLocomotion(speed > speedThreshold ? run : idle, true);
            }
            onComplete?.Invoke();
        }

        /// <summary>True while a ForcePlayOverride animation is playing.</summary>
        public bool IsForceOverrideActive => forceOverrideActive;

        #endregion

        #region Mod Combat Entry

        private void HandleCombatModeChanged()
        {
            if (weaponManager == null || !weaponManager.IsModCombat)
                return;

            string animationToPlay = ResolveModCombatEntryAnimation();
            if (string.IsNullOrEmpty(animationToPlay))
                return;

            if (playerState != null)
                playerState.SetInputLocked(true);

            if (controller != null)
                controller.StopAllVelocity();

            if (modEntryVFXPrefab != null)
                Instantiate(modEntryVFXPrefab, transform.position, Quaternion.identity);

            ForcePlayOverride(animationToPlay, false, () =>
            {
                if (playerState != null)
                    playerState.SetInputLocked(false);
            });
        }

        private string ResolveModCombatEntryAnimation()
        {
            if (HasAnimation(modCombatEntryAnim))
                return modCombatEntryAnim;

            if (HasAnimation(modCombatEntryFallbackAnim))
            {
                return modCombatEntryFallbackAnim;
            }

            return string.Empty;
        }

        #endregion

        #region Attack Interruption

        private void InterruptAttack(string reason)
        {
            if (!attackActive) return;

            attackActive = false;
            currentAttackEntry = null;

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.ClearTrack(overlayTrack);

            if (playerState != null)
                playerState.NotifyAttackAnimationInterrupted();
        }

        #endregion

        #region State Event Handlers

        private void OnAttackAnimationRequested(string animationName)
        {
            if (playerState == null) return;

            PlayAttackAnimation(animationName);
        }

        private void OnAttackingChanged(bool attacking)
        {
            if (!attacking || playerState == null)
                return;

        }

        private void OnGroundedChanged(bool grounded)
        {
            // Don't override when a force-override animation is playing (e.g. GroundPound)
            if (forceOverrideActive)
                return;

            // Don't override locomotion (landing/run/idle) while any attack is playing
            if (attackActive)
                return;

            if (grounded)
            {
                ClearDoubleJumpFlag();

                float speed = GetSpeed();
                if (speed > speedThreshold)
                {
                    // Moving - skip landing, go straight to run
                    PlayLocomotion(run, true);
                }
                else if (wasAirborne)
                {
                    // Idle landing
                    PlayLanding();
                }
                else
                {
                    PlayLocomotion(idle, true);
                }

                wasAirborne = false;
            }
            else
            {
                wasAirborne = true;
            }
        }

        private void OnJumpStateChanged(bool jumping)
        {
            if (jumping)
                PlayJumpStart();
        }

        private void OnDashingChanged(bool dashing)
        {
            if (dashing)
            {
                InterruptAttack("dash");
                PlayLocomotion(dash, false);
            }
            else
            {
                if (playerState.IsGrounded)
                {
                    float speed = GetSpeed();
                    PlayLocomotion(speed > speedThreshold ? run : idle, true);
                }
                else
                {
                    PlayJumpAir();
                }
            }
        }

        private void OnWallSlideChanged(bool sliding)
        {
            if (sliding)
            {
                PlayLocomotion(wallSlide, false);
            }
            else
            {
                if (playerState.IsGrounded)
                {
                    float speed = GetSpeed();
                    PlayLocomotion(speed > speedThreshold ? run : idle, true);
                }
                else
                {
                    PlayJumpAir();
                }
            }
        }

        private void OnLedgeDetectedChanged(bool detected)
        {
            // placeholder: could play specific animation or adjust logic
            if (detected)
            {
                // maybe a ledgegrab animation exists
                PlayLocomotion("ledge", false);
            }
            else
            {
                if (playerState.IsGrounded)
                {
                    float speed = GetSpeed();
                    PlayLocomotion(speed > speedThreshold ? run : idle, true);
                }
                else
                {
                    PlayJumpAir();
                }
            }
        }

        private void OnParryChanged(bool parrying)
        {
            if (parrying)
            {
                // force a parry locomotion if available
                PlayLocomotion("parry", false);
            }
            else
            {
                // if we were holding an attack animation, end it now
                if (waitingForParryEnd)
                {
                    waitingForParryEnd = false;
                    ForceFinishAttack();
                }

                // resume normal locomotion state
                if (playerState.IsGrounded)
                {
                    float speed = GetSpeed();
                    PlayLocomotion(speed > speedThreshold ? run : idle, true);
                }
                else
                {
                    PlayJumpAir();
                }
            }
        }

        private void OnDoubleJumpChanged(bool doubleJumping)
        {
            if (!doubleJumping) return;
            if (!playerState.IsJumping && !playerState.IsFalling)
            {
                ClearDoubleJumpFlag(); // e.g. stunned or not airborne - don't leave flag set without playing
                return;
            }

            if (!HasAnimation(doubleJump))
            {
                PlayJumpAir();
                ClearDoubleJumpFlag();
                return;
            }

            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, doubleJump, GetLoopFor(doubleJump, false));
            entry.MixDuration = doubleJumpBlend;
            entry.MixBlend = MixBlend.Replace;
            entry.TimeScale = GetTimeScaleFor(doubleJump, 1f);
            currentLocomotionAnim = doubleJump;
            entry.Complete += _ => ClearDoubleJumpFlag();
            entry.Interrupt += _ => ClearDoubleJumpFlag();

            var airEntry = skeletonAnimation.AnimationState.AddAnimation(locomotionTrack, jumpAir, GetLoopFor(jumpAir, false), 0f);
            airEntry.TimeScale = GetTimeScaleFor(jumpAir, 1f);
            airEntry.Complete += OnJumpAirComplete;
        }

        private void OnControllerDoubleJumpPerformed()
        {
            if (!HasAnimation(doubleJump))
            {
                PlayJumpAir();
                return;
            }

            if (GetCurrentLocomotionName() == doubleJump)
                return;

            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, doubleJump, GetLoopFor(doubleJump, false));
            entry.MixDuration = doubleJumpBlend;
            entry.MixBlend = MixBlend.Replace;
            entry.TimeScale = GetTimeScaleFor(doubleJump, 1f);
            currentLocomotionAnim = doubleJump;
            entry.Complete += _ => ClearDoubleJumpFlag();
            entry.Interrupt += _ => ClearDoubleJumpFlag();

            var airEntry = skeletonAnimation.AnimationState.AddAnimation(locomotionTrack, jumpAir, GetLoopFor(jumpAir, false), 0f);
            airEntry.TimeScale = GetTimeScaleFor(jumpAir, 1f);
            airEntry.Complete += OnJumpAirComplete;
        }

        private void OnStunnedChanged(bool stunned)
        {
            // Don't let stun overwrite the death animation
            if (!playerState.IsAlive) return;

            if (stunned)
            {
                InterruptAttack("stun");
                ClearDoubleJumpFlag();
                controller?.ResetAirJumpCount();
                if (HasAnimation(stun))
                    PlayLocomotion(stun, true);
            }
            else
            {
                if (playerState.IsGrounded)
                {
                    float speed = GetSpeed();
                    PlayLocomotion(speed > speedThreshold ? run : idle, true);
                }
                else
                {
                    PlayJumpAir();
                }
            }
        }

        private void OnDeath()
        {
            InterruptAttack("death");
            if (skeletonAnimation != null && HasAnimation(death))
            {
                skeletonAnimation.AnimationState.ClearTrack(overlayTrack);
                PlayLocomotion(death, false);
            }
        }

        #endregion

        #region Locomotion

        private void PlayLocomotion(string animName, bool loop)
        {
            if (!HasAnimation(animName) || GetCurrentLocomotionName() == animName)
                return;

            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, animName, GetLoopFor(animName, loop));
            entry.MixDuration = locomotionBlend;
            entry.TimeScale = GetTimeScaleFor(animName, 1f);
            currentLocomotionAnim = animName;
        }

        private void PlayJumpStart()
        {
            if (!HasAnimation(jumpStart))
            {
                PlayJumpAir();
                return;
            }

            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, jumpStart, GetLoopFor(jumpStart, false));
            entry.MixDuration = locomotionBlend;
            entry.TimeScale = GetTimeScaleFor(jumpStart, 1f);
            currentLocomotionAnim = jumpStart;

            var airEntry = skeletonAnimation.AnimationState.AddAnimation(locomotionTrack, jumpAir, GetLoopFor(jumpAir, false), 0f);
            airEntry.TimeScale = GetTimeScaleFor(jumpAir, 1f);
            airEntry.Complete += OnJumpAirComplete;
        }

        private void PlayJumpAir()
        {
            if (!HasAnimation(jumpAir)) return;

            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, jumpAir, GetLoopFor(jumpAir, false));
            entry.MixDuration = locomotionBlend;
            entry.TimeScale = GetTimeScaleFor(jumpAir, 1f);
            currentLocomotionAnim = jumpAir;
            entry.Complete += OnJumpAirComplete;
        }

        private void OnJumpAirComplete(TrackEntry entry)
        {
            if (entry.Animation.Name != jumpAir) return;
            if (playerState != null && playerState.IsGrounded) return;

            // Hold on last frame while airborne
            entry.TimeScale = 0f;
            entry.TrackTime = entry.AnimationEnd - 0.001f;
        }

        private void PlayLanding()
        {
            if (!HasAnimation(landing))
            {
                PlayLocomotion(idle, true);
                return;
            }

            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, landing, GetLoopFor(landing, false));
            entry.MixDuration = locomotionBlend;
            entry.TimeScale = GetTimeScaleFor(landing, 1f);
            currentLocomotionAnim = landing;

            // Queue idle after landing
            var idleEntry = skeletonAnimation.AnimationState.AddAnimation(locomotionTrack, idle, GetLoopFor(idle, true), 0f);
            idleEntry.TimeScale = GetTimeScaleFor(idle, 1f);
        }

        private void ApplyAnyStateFallbacks()
        {
            if (!playerState.IsAlive || playerState.IsStunned)
                return;

            if (playerState.IsDashing)
                return;

            if (attackActive || forceOverrideActive)
                return;

            string current = GetCurrentLocomotionName();

            if (playerState.IsDoubleJumping || current == doubleJump)
                return;

            // If airborne and falling, force jump air (except during landing)
            if (!playerState.IsGrounded && playerState.IsFalling)
            {
                if (current != jumpAir && current != landing)
                {
                    PlayJumpAir();
                }
                return;
            }

            // If grounded but in air animation, fix it
            if (playerState.IsGrounded)
            {
                if (IsPlayingTransientAnim())
                    return;

                if (current == jumpStart || current == jumpAir || current == doubleJump || current == wallSlide)
                {
                    float speed = GetSpeed();
                    PlayLocomotion(speed > speedThreshold ? run : idle, true);
                }
            }
        }

        private void UpdateLocomotionFromSpeed()
        {
            if (!playerState.IsGrounded || IsPlayingTransientAnim() || attackActive || forceOverrideActive)
                return;

            float speed = GetSpeed();
            string targetAnim = speed > speedThreshold ? run : idle;

            if (GetCurrentLocomotionName() != targetAnim)
                PlayLocomotion(targetAnim, true);
        }

        private void UpdateRunSpeed()
        {
            if (GetCurrentLocomotionName() != run || !playerState.IsGrounded)
                return;

            var entry = skeletonAnimation.AnimationState.GetCurrent(locomotionTrack);
            if (entry == null || entry.Animation.Name != run)
                return;

            float speed = GetSpeed();
            float scale = controller.MoveSpeed > 0f ? speed / controller.MoveSpeed : 1f;
            entry.TimeScale = Mathf.Clamp(scale, minRunTimeScale, maxRunTimeScale) * runTimeScale;
        }

        #endregion

        #region Utility

        private bool IsPlayingTransientAnim()
        {
            string current = GetCurrentLocomotionName();
            return current == dash ||
                   current == stun ||
                   current == landing ||
                   current == jumpStart ||
                   current == jumpAir ||
                   current == doubleJump ||
                   current == wallSlide ||
                   current == death;
        }

        private void ClearDoubleJumpFlag()
        {
            if (playerState != null && playerState.IsDoubleJumping)
                playerState.SetDoubleJumping(false);
        }

        private void SyncCurrentLocomotion()
        {
            string current = GetCurrentLocomotionName();
            if (!string.IsNullOrEmpty(current))
                currentLocomotionAnim = current;
        }

        private string GetCurrentLocomotionName()
        {
            if (skeletonAnimation == null) return currentLocomotionAnim;
            var entry = skeletonAnimation.AnimationState?.GetCurrent(locomotionTrack);
            return entry?.Animation?.Name ?? currentLocomotionAnim;
        }

        private float GetSpeed()
        {
            if (controller == null) return 0f;
            Vector3 v = controller.Velocity;
            return new Vector2(v.x, v.z).magnitude;
        }

        private bool HasAnimation(string animName)
        {
            if (string.IsNullOrEmpty(animName) || skeletonAnimation == null)
                return false;

            var data = skeletonAnimation.Skeleton?.Data;
            return data != null && data.FindAnimation(animName) != null;
        }

        private bool GetLoopFor(string animName, bool fallback)
        {
            if (string.IsNullOrEmpty(animName)) return fallback;
            if (animName == idle) return idleLoop;
            if (animName == run) return runLoop;
            if (animName == jumpStart) return jumpStartLoop;
            if (animName == jumpAir) return jumpAirLoop;
            if (animName == landing) return landingLoop;
            if (animName == doubleJump) return doubleJumpLoop;
            if (animName == wallSlide) return wallSlideLoop;
            if (animName == dash) return dashLoop;
            if (animName == stun) return stunLoop;
            if (animName == death) return deathLoop;
            return fallback;
        }

        private float GetTimeScaleFor(string animName, float fallback = 1f)
        {
            if (string.IsNullOrEmpty(animName)) return fallback;
            if (animName == idle) return idleTimeScale;
            if (animName == run) return runTimeScale;
            if (animName == jumpStart) return jumpStartTimeScale;
            if (animName == jumpAir) return jumpAirTimeScale;
            if (animName == landing) return landingTimeScale;
            if (animName == doubleJump) return doubleJumpTimeScale;
            if (animName == wallSlide) return wallSlideTimeScale;
            if (animName == dash) return dashTimeScale;
            if (animName == stun) return stunTimeScale;
            if (animName == death) return deathTimeScale;
            return fallback;
        }

        private void ConfigureMixes()
        {
            var data = skeletonAnimation.AnimationState?.Data;
            if (data == null) return;

            SafeSetMix(data, idle, run, locomotionBlend);
            SafeSetMix(data, run, idle, locomotionBlend);
            SafeSetMix(data, landing, idle, locomotionBlend);
            SafeSetMix(data, landing, run, locomotionBlend);
            SafeSetMix(data, jumpStart, jumpAir, 0f);
            SafeSetMix(data, jumpAir, landing, locomotionBlend);
        }

        private void SafeSetMix(Spine.AnimationStateData data, string from, string to, float duration)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
                return;

            var skeletonData = skeletonAnimation?.Skeleton?.Data;
            if (skeletonData == null) return;

            if (skeletonData.FindAnimation(from) != null && skeletonData.FindAnimation(to) != null)
                data.SetMix(from, to, duration);
        }

        private void HandleSpineEvent(TrackEntry trackEntry, Spine.Event e)
        {
            if (e != null && e.Data != null)
            {
                string eventName = e.Data.Name;
                if (eventName == "footstep" || eventName == "footstep_left" || eventName == "footstep_right")
                    audioHandler?.PlayFootstep();
            }

            if (footstepSource == null || footstepEvent == null) return;
            if (e.Data != footstepEvent.EventData) return;

            footstepSource.pitch = 1f + UnityEngine.Random.Range(-footstepPitchOffset, footstepPitchOffset);
            footstepSource.Play();
        }

        #endregion
    }
}
