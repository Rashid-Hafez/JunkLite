using UnityEngine;
using Spine;
using Spine.Unity;

namespace junklite
{
    /// <summary>
    /// Event-driven Spine animation controller.
    /// REFACTORED: Pure animation playback - WeaponManager provides animation names.
    /// Properly applies input lock during attacks to prevent movement.
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
        [SerializeField] private string roll = "roll";
        [SerializeField] private string stun = "stun";
        [SerializeField] private string death = "death";

        [Header("Animation Looping")]
        [SerializeField] private bool idleLoop = true;
        [SerializeField] private bool runLoop = true;
        [SerializeField] private bool jumpStartLoop = false;
        [SerializeField] private bool jumpAirLoop = false;
        [SerializeField] private bool landingLoop = false;
        [SerializeField] private bool doubleJumpLoop = false;
        [SerializeField] private bool wallSlideLoop = true;
        [SerializeField] private bool dashLoop = false;
        [SerializeField] private bool rollLoop = false;
        [SerializeField] private bool stunLoop = true;
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
        [SerializeField, Range(0.1f, 3f)] private float rollTimeScale = 1f;
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
        [SerializeField] private float attackMixAttachmentThreshold = 0f;
        [SerializeField] private float attackMixOut = 0.25f;
        [SerializeField] private float attackMixOutDelay = 0.05f;
        [SerializeField, Range(0.1f, 3f)] private float attackTimeScale = 1f;

        [Header("Attack Input Lock")]
        [SerializeField] private bool lockMovementDuringAttack = true;

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
        private WeaponManager weaponManager;

        // State tracking
        private bool wasAirborne = false;
        private string currentLocomotionAnim = "";
        private bool attackActive = false;
        private bool attackOverwriteActive = false;
        private bool attackInputLockApplied = false;
        private TrackEntry currentAttackEntry = null;

        // Public state
        public bool IsAttacking => attackActive;

        #region Unity Lifecycle

        private void Awake()
        {
            if (!skeletonAnimation)
                skeletonAnimation = GetComponentInChildren<SkeletonAnimation>();

            playerState = GetComponentInParent<PlayerState>();
            controller = GetComponentInParent<Character2D5Controller>();
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

            // Subscribe to state events (locomotion only - NO attack events)
            playerState.OnGroundedChanged += OnGroundedChanged;
            playerState.OnMovingChanged += OnMovingChanged;
            playerState.OnJumpStateChanged += OnJumpStateChanged;
            playerState.OnFallStateChanged += OnFallStateChanged;
            playerState.OnDashingChanged += OnDashingChanged;
            playerState.OnRollingChanged += OnRollingChanged;
            playerState.OnWallSlideChanged += OnWallSlideChanged;
            playerState.OnDoubleJumpChanged += OnDoubleJumpChanged;
            playerState.OnStunnedChanged += OnStunnedChanged;
            playerState.OnDeath += OnDeath;

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.Event += HandleSpineEvent;

            if (controller != null)
            {
                controller.OnDoubleJumpPerformed += OnControllerDoubleJumpPerformed;
                controller.OnJumpStarted += OnControllerJumpStarted;
            }

            // Set initial idle
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
                playerState.OnRollingChanged -= OnRollingChanged;
                playerState.OnWallSlideChanged -= OnWallSlideChanged;
                playerState.OnDoubleJumpChanged -= OnDoubleJumpChanged;
                playerState.OnStunnedChanged -= OnStunnedChanged;
                playerState.OnDeath -= OnDeath;
            }

            if (controller != null)
            {
                controller.OnDoubleJumpPerformed -= OnControllerDoubleJumpPerformed;
                controller.OnJumpStarted -= OnControllerJumpStarted;
            }

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.Event -= HandleSpineEvent;

            ReleaseAttackInputLock();
        }

        #endregion Unity Lifecycle

        #region Attack Animation (Public API)

        /// <summary>
        /// Plays an attack animation by name. Called by WeaponManager.
        /// Applies input lock and notifies WeaponManager when complete.
        /// </summary>
        public void PlayAttackAnimation(string animationName)
        {
            LogAttack($"PlayAttackAnimation called: '{animationName}'");

            // Validate animation
            if (string.IsNullOrEmpty(animationName))
            {
                LogAttack("Animation name is null/empty - completing immediately");
                NotifyWeaponManagerAttackComplete();
                return;
            }

            if (!HasAnimation(animationName))
            {
                LogAttack($"Animation not found: '{animationName}' - completing immediately");
                NotifyWeaponManagerAttackComplete();
                return;
            }

            // If already attacking, force finish previous
            if (attackActive)
            {
                LogAttack("Already attacking - forcing previous attack complete");
                ForceFinishAttack();
            }

            // Set attack state FIRST
            attackActive = true;
            attackOverwriteActive = attackOverwrite;

            // Apply input lock IMMEDIATELY
            ApplyAttackInputLock();

            LogAttack($"Starting attack: '{animationName}', overwrite={attackOverwrite}, inputLock={lockMovementDuringAttack}");

            // Play animation
            if (attackOverwrite)
            {
                // Overwrite mode: play on locomotion track (replaces run/idle)
                var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, animationName, false);
                entry.MixDuration = attackMix;
                entry.MixBlend = MixBlend.Replace;
                entry.TimeScale = attackTimeScale;
                entry.Complete += _ => FinishAttackOverwrite();
                entry.Interrupt += _ => OnAttackInterrupted();
                currentAttackEntry = entry;

                LogAttack($"Attack (overwrite) started on track {locomotionTrack}");
            }
            else
            {
                // Overlay mode: play on overlay track (on top of locomotion)
                // CRITICAL: Force locomotion to idle to prevent leg animation conflicts
                FreezeLocomotionDuringAttack();

                var entry = skeletonAnimation.AnimationState.SetAnimation(overlayTrack, animationName, false);
                entry.MixDuration = attackMix;
                entry.MixBlend = MixBlend.Replace;
                entry.MixAttachmentThreshold = attackMixAttachmentThreshold;
                entry.TimeScale = attackTimeScale;
                entry.Complete += _ => FinishAttackOverlay();
                entry.Interrupt += _ => OnAttackInterrupted();
                currentAttackEntry = entry;

                LogAttack($"Attack (overlay) started on track {overlayTrack}");
            }
        }

        /// <summary>
        /// Freezes locomotion to idle during overlay attacks to prevent leg animation conflicts.
        /// When attacking in overlay mode, the locomotion track continues playing.
        /// This forces it to idle so legs don't run while attacking.
        /// </summary>
        private void FreezeLocomotionDuringAttack()
        {
            if (skeletonAnimation == null) return;

            var locomotionEntry = skeletonAnimation.AnimationState.GetCurrent(locomotionTrack);
            if (locomotionEntry == null) return;

            // If currently running or any non-idle animation, switch to idle
            if (locomotionEntry.Animation.Name != idle && HasAnimation(idle))
            {
                var idleEntry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, idle, true);
                idleEntry.MixDuration = 0.05f; // Quick blend to idle
                idleEntry.TimeScale = 1f;
                currentLocomotionAnim = idle;

                LogAttack("Froze locomotion to idle for overlay attack");
            }
        }

        private void FinishAttackOverlay()
        {
            if (!attackActive) return;

            LogAttack("Attack overlay complete");

            attackActive = false;
            attackOverwriteActive = false;
            currentAttackEntry = null;

            // Release input lock
            ReleaseAttackInputLock();

            // Clear overlay track with blend out
            if (skeletonAnimation != null)
            {
                skeletonAnimation.AnimationState.AddEmptyAnimation(overlayTrack, attackMixOut, attackMixOutDelay);
            }

            // Notify WeaponManager
            NotifyWeaponManagerAttackComplete();
        }

        private void FinishAttackOverwrite()
        {
            if (!attackActive) return;

            LogAttack("Attack overwrite complete");

            attackActive = false;
            attackOverwriteActive = false;
            currentAttackEntry = null;

            // Release input lock
            ReleaseAttackInputLock();

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

            // Notify WeaponManager
            NotifyWeaponManagerAttackComplete();
        }

        private void OnAttackInterrupted()
        {
            if (!attackActive) return;

            LogAttack("Attack interrupted by animation system");

            attackActive = false;
            currentAttackEntry = null;

            // Release input lock
            ReleaseAttackInputLock();

            // Clear overlay track
            if (skeletonAnimation != null && !attackOverwriteActive)
            {
                skeletonAnimation.AnimationState.ClearTrack(overlayTrack);
            }

            attackOverwriteActive = false;

            // Notify WeaponManager
            if (weaponManager != null)
                weaponManager.OnAttackInterrupted();
        }

        private void ForceFinishAttack()
        {
            attackActive = false;
            attackOverwriteActive = false;
            currentAttackEntry = null;
            ReleaseAttackInputLock();

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.ClearTrack(overlayTrack);

            NotifyWeaponManagerAttackComplete();
        }

        private void NotifyWeaponManagerAttackComplete()
        {
            if (weaponManager != null)
                weaponManager.OnAttackAnimationComplete();
        }

        /// <summary>
        /// Called when dash/stun/death interrupts attack.
        /// </summary>
        private void InterruptAttack(string reason)
        {
            if (!attackActive) return;

            LogAttack($"Attack interrupted by: {reason}");

            attackActive = false;
            attackOverwriteActive = false;
            currentAttackEntry = null;
            ReleaseAttackInputLock();

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.ClearTrack(overlayTrack);

            if (weaponManager != null)
                weaponManager.OnAttackInterrupted();
        }

        #endregion Attack Animation

        #region Input Lock

        private void ApplyAttackInputLock()
        {
            if (!lockMovementDuringAttack)
            {
                LogAttack("Input lock SKIPPED (disabled in settings)");
                return;
            }

            if (playerState == null)
            {
                LogAttack("Input lock FAILED (PlayerState is null)");
                return;
            }

            if (attackInputLockApplied)
            {
                LogAttack("Input lock already applied");
                return;
            }

            playerState.SetInputLocked(true);

            if (controller != null)
                controller.StopAllVelocity();

            attackInputLockApplied = true;
            LogAttack("Input lock APPLIED - movement disabled");
        }

        private void ReleaseAttackInputLock()
        {
            if (!attackInputLockApplied)
                return;

            if (playerState != null)
                playerState.SetInputLocked(false);

            attackInputLockApplied = false;
            LogAttack("Input lock RELEASED - movement enabled");
        }

        #endregion Input Lock

        #region State Event Handlers

        private void OnGroundedChanged(bool grounded)
        {
            if (grounded)
            {
                ClearDoubleJumpFlag();
                wasAirborne = false;

                if (attackActive)
                    return;

                float speed = GetSpeed();
                if (speed > speedThreshold)
                {
                    PlayLocomotion(run, true);
                }
                else
                {
                    PlayLanding();
                }
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
            if (jumping && !attackActive)
                PlayJumpStart();
            Log($"Jumping={jumping}");
        }

        private void OnControllerJumpStarted()
        {
            if (!attackActive)
                PlayJumpStart();
        }

        private void OnFallStateChanged(bool falling)
        {
            if (falling && !attackActive && !playerState.IsWallSliding)
            {
                // Don't override jump animations - only play fall when walking off ledge
                string current = GetCurrentLocomotionName();
                if (current != jumpStart && current != doubleJump)
                {
                    PlayJumpAir();
                }
            }
            Log($"Falling={falling}");
        }

        private void OnDashingChanged(bool dashing)
        {
            if (dashing)
            {
                InterruptAttack("dash");
                PlayLocomotion(dash, false);
            }
            else if (!attackActive)
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

        private void OnRollingChanged(bool rolling)
        {
            if (rolling)
            {
                InterruptAttack("roll");
                PlayLocomotion(roll, false);
            }
            else if (!attackActive)
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
                InterruptAttack("wallSlide");
                PlayLocomotion(wallSlide, false);
            }
            else if (!attackActive)
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

        private void OnDoubleJumpChanged(bool doubleJumping)
        {
            if (!doubleJumping) return;
            if (attackActive) return;

            if (!playerState.IsJumping && !playerState.IsFalling)
                return;

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
            if (attackActive) return;

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
            if (stunned)
            {
                InterruptAttack("stun");
                if (HasAnimation(stun))
                    PlayLocomotion(stun, true);
            }
            else if (!attackActive)
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

        #endregion State Event Handlers

        #region Locomotion

        private void PlayLocomotion(string animName, bool loop)
        {
            if (skeletonAnimation == null || string.IsNullOrEmpty(animName)) return;
            if (!HasAnimation(animName)) return;

            // Don't change locomotion during attack (unless it's an interrupt like dash/stun)
            // This is handled by the caller checking attackActive

            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, animName, loop);
            entry.MixDuration = locomotionBlend;
            entry.TimeScale = GetTimeScaleFor(animName, 1f);
            currentLocomotionAnim = animName;
        }

        private void PlayJumpStart()
        {
            if (attackActive) return;

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
            if (attackActive) return;
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

            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, landing, false);
            entry.MixDuration = locomotionBlend;
            entry.TimeScale = GetTimeScaleFor(landing, 1f);
            currentLocomotionAnim = landing;

            // Queue idle after landing
            var nextEntry = skeletonAnimation.AnimationState.AddAnimation(locomotionTrack, idle, true, 0f);
            nextEntry.MixDuration = locomotionBlend;
        }

        private void UpdateLocomotionFromSpeed()
        {
            if (IsPlayingTransientAnim()) return;
            if (playerState == null || !playerState.IsGrounded) return;
            if (attackActive) return;  // DON'T change locomotion during attack

            float speed = GetSpeed();
            string targetAnim = speed > speedThreshold ? run : idle;

            if (GetCurrentLocomotionName() != targetAnim)
                PlayLocomotion(targetAnim, true);
        }

        private void UpdateRunSpeed()
        {
            if (attackActive) return;  // Don't update run speed during attack
            if (GetCurrentLocomotionName() != run || !playerState.IsGrounded)
                return;

            var entry = skeletonAnimation.AnimationState.GetCurrent(locomotionTrack);
            if (entry == null || entry.Animation.Name != run)
                return;

            float speed = GetSpeed();
            float scale = controller.MoveSpeed > 0f ? speed / controller.MoveSpeed : 1f;
            entry.TimeScale = Mathf.Clamp(scale, minRunTimeScale, maxRunTimeScale);
        }

        private void ApplyAnyStateFallbacks()
        {
            if (playerState == null) return;
            if (attackActive) return;  // Don't apply fallbacks during attack

            string current = GetCurrentLocomotionName();

            // If grounded but still showing air animation
            if (playerState.IsGrounded && (current == jumpAir || current == jumpStart || current == doubleJump))
            {
                float speed = GetSpeed();
                PlayLocomotion(speed > speedThreshold ? run : idle, true);
            }
        }

        #endregion Locomotion

        #region Utility

        private bool IsPlayingTransientAnim()
        {
            string current = GetCurrentLocomotionName();
            return current == dash ||
                   current == roll ||
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
            if (animName == roll) return rollLoop;
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
            if (animName == roll) return rollTimeScale;
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
            if (footstepSource == null || footstepEvent == null) return;
            if (e.Data != footstepEvent.EventData) return;

            footstepSource.pitch = 1f + Random.Range(-footstepPitchOffset, footstepPitchOffset);
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

        #endregion Utility
    }
}