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
        [SerializeField, Range(0.1f, 3f)] private float attackTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float downAttackTimeScale = 1.4f;

        [Header("Force Override (e.g. GroundPound)")]
        [Tooltip("When grounded, wait this long after the override animation ends before returning to idle/run")]
        [SerializeField] private float forceOverrideGroundedHoldDuration = 0.4f;

        [Header("Footsteps")]
        [SerializeField] private EventDataReferenceAsset footstepEvent;
        [SerializeField] private AudioSource footstepSource;
        [SerializeField] private float footstepPitchOffset = 0.2f;

        [Header("Debug")]
        [SerializeField] private bool logStateChanges = false;
        [SerializeField] private bool logAttacks = false;

        // References
        private PlayerState playerState;
        private Character2D5Controller controller;
        private PlayerAudioHandler audioHandler;

        // State tracking
        private bool wasAirborne = false;
        private string currentLocomotionAnim = "";
        private bool attackActive = false;
        private bool attackOverwriteActive = false;
        private TrackEntry currentAttackEntry = null;
        private bool forceOverrideActive = false;

        // when an attack animation finishes during a parry, we defer the end until parry state clears
        private bool waitingForParryEnd = false;

        #region Unity Lifecycle

        private void Awake()
        {
            if (!skeletonAnimation)
                skeletonAnimation = GetComponentInChildren<SkeletonAnimation>();

            playerState = GetComponentInParent<PlayerState>();
            controller = GetComponentInParent<Character2D5Controller>();
            audioHandler = GetComponentInParent<PlayerAudioHandler>() ?? GetComponent<PlayerAudioHandler>();

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
            playerState.OnMovingChanged += OnMovingChanged;
            playerState.OnJumpStateChanged += OnJumpStateChanged;
            playerState.OnFallStateChanged += OnFallStateChanged;
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
                playerState.OnMovingChanged -= OnMovingChanged;
                playerState.OnJumpStateChanged -= OnJumpStateChanged;
                playerState.OnFallStateChanged -= OnFallStateChanged;
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
                LogAttack("PlayAttackAnimation: null/empty name - completing immediately");
                NotifyPlayerAttackComplete();
                return;
            }

            if (!HasAnimation(animationName))
            {
                LogAttack($"Animation not found: '{animationName}' - completing immediately");
                NotifyPlayerAttackComplete();
                return;
            }

            if (attackActive)
            {
                LogAttack("Already attacking - forcing previous complete");
                ForceFinishAttack();
            }

            attackActive = true;

            LogAttack($"Playing attack: '{animationName}'");

            float timeScale = attackTimeScale;
            if (playerState != null && playerState.IsDownAttackRequested)
                timeScale = downAttackTimeScale;

            if (attackOverwrite)
            {
                attackOverwriteActive = true;
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
                attackOverwriteActive = false;
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

            LogAttack("Attack overlay complete");

            // hold last frame if parry still active
            if (playerState != null && playerState.IsParrying)
            {
                LogAttack("Holding overlay because parry still active");
                waitingForParryEnd = true;
                return;
            }

            attackActive = false;
            attackOverwriteActive = false;
            currentAttackEntry = null;

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.AddEmptyAnimation(overlayTrack, attackMixOut, attackMixOutDelay);

            NotifyPlayerAttackComplete();
        }

        private void FinishAttackOverwrite()
        {
            if (!attackActive) return;

            LogAttack("Attack overwrite complete");

            // hold last frame if parry still active
            if (playerState != null && playerState.IsParrying)
            {
                LogAttack("Holding overwrite because parry still active");
                waitingForParryEnd = true;
                return;
            }

            attackActive = false;
            attackOverwriteActive = false;
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

            LogAttack("Attack interrupted");

            attackActive = false;
            attackOverwriteActive = false;
            currentAttackEntry = null;

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.ClearTrack(overlayTrack);

            if (playerState != null)
                playerState.NotifyAttackAnimationInterrupted();
        }

        private void ForceFinishAttack()
        {
            attackActive = false;
            attackOverwriteActive = false;
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
                Log($"ForcePlayOverride: animation '{animationName}' not found");
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

        private void InterruptAttack(string reason)
        {
            if (!attackActive) return;

            LogAttack($"Attack interrupted by: {reason}");

            attackActive = false;
            attackOverwriteActive = false;
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

            Log($"Grounded={grounded}");
        }

        private void OnMovingChanged(bool moving)
        {
            Log($"Moving={moving}");
        }

        private void OnJumpStateChanged(bool jumping)
        {
            if (jumping)
                PlayJumpStart();

            Log($"Jumping={jumping}");
        }

        private void OnFallStateChanged(bool falling)
        {
            // Falling handled by ApplyAnyStateFallbacks
            Log($"Falling={falling}");
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

            if (attackOverwriteActive || forceOverrideActive)
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
            if (!playerState.IsGrounded || IsPlayingTransientAnim() || attackOverwriteActive || forceOverrideActive)
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

        private void Log(string message)
        {
            if (logStateChanges)
                Debug.Log($"[SpineAnim] {message}", this);
        }

        private void LogAttack(string message)
        {
            if (logAttacks)
                Debug.Log($"[SpineAnim] {message}", this);
        }

        #endregion
    }
}