using UnityEngine;
using Spine;
using Spine.Unity;

namespace junklite
{
    /// <summary>
    /// Event-driven Spine animation controller.
    /// UPDATED: Removed duplicate buffer logic, now properly calls WeaponManager callbacks.
    /// </summary>
    public class SpineAnimationController : MonoBehaviour
    {
        [Header("Spine")]
        [SerializeField] private SkeletonAnimation skeletonAnimation;

        [Header("Tracks")]
        [SerializeField] private int locomotionTrack = 0;
        [SerializeField] private int overlayTrack = 1;

        [Header("Animations")]
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
        [SerializeField] private float[] comboAttackTimeScales;

        [Header("Locomotion")]
        [SerializeField] private float speedThreshold = 0.1f;
        [SerializeField] private float locomotionBlend = 0.1f;
        [SerializeField] private float minRunTimeScale = 0.8f;
        [SerializeField] private float maxRunTimeScale = 1.3f;
        [SerializeField] private float doubleJumpBlend = 0.05f;

        [Header("Attack Overlay")]
        [SerializeField] private string[] comboAttacks;
        [SerializeField] private string upAttackAnim = "attackUp";
        [SerializeField] private string downAttackAnim = "attackDown";
        [SerializeField] private string fallbackAttack = "attack";
        [SerializeField] private bool attackOverwrite = false;
        [SerializeField] private float attackMix = 0.1f;
        [SerializeField] private float attackMixAttachmentThreshold = 0f;
        [SerializeField] private float attackMixOut = 0.25f;
        [SerializeField] private float attackMixOutDelay = 0.05f;

        [Header("Attack Per-Entry Overrides (optional)")]
        [SerializeField] private bool[] attackEntryOverwrite;
        [SerializeField] private float[] attackEntryMix;
        [SerializeField] private float[] attackEntryAttachmentThreshold;
        [SerializeField] private float[] attackEntryMixOut;
        [SerializeField] private float[] attackEntryMixOutDelay;

        [Header("Attack Lock")]
        [SerializeField] private bool lockMovementDuringAttack = true;

        [Header("Footsteps")]
        [SerializeField] private EventDataReferenceAsset footstepEvent;
        [SerializeField] private AudioSource footstepSource;
        [SerializeField] private float footstepPitchOffset = 0.2f;

        [Header("Debug")]
        [SerializeField] private bool logStateChanges = false;
        [SerializeField] private bool logAttacks = false;

        private PlayerState playerState;
        private Character2D5Controller controller;
        private WeaponManager weaponManager;

        // State tracking
        private bool wasAirborne = false;
        private string currentLocomotionAnim = "";
        private bool attackActive = false;
        private bool attackOverwriteActive = false;
        private float cachedAttackMixOut = 0.25f;
        private float cachedAttackMixOutDelay = 0.05f;
        private bool attackInputLockApplied = false;
        private TrackEntry currentAttackEntry = null;

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

            // Subscribe to state events
            playerState.OnGroundedChanged += OnGroundedChanged;
            playerState.OnMovingChanged += OnMovingChanged;
            playerState.OnJumpStateChanged += OnJumpStateChanged;
            playerState.OnFallStateChanged += OnFallStateChanged;
            playerState.OnDashingChanged += OnDashingChanged;
            playerState.OnRollingChanged += OnRollingChanged;
            playerState.OnWallSlideChanged += OnWallSlideChanged;
            playerState.OnDoubleJumpChanged += OnDoubleJumpChanged;
            playerState.OnStunnedChanged += OnStunnedChanged;
            playerState.OnComboAttackTriggered += OnComboAttackTriggered;
            playerState.OnDeath += OnDeath;

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.Event += HandleSpineEvent;

            if (controller != null)
                controller.OnDoubleJumpPerformed += OnControllerDoubleJumpPerformed;

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
                playerState.OnComboAttackTriggered -= OnComboAttackTriggered;
                playerState.OnDeath -= OnDeath;
            }

            if (controller != null)
                controller.OnDoubleJumpPerformed -= OnControllerDoubleJumpPerformed;

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.Event -= HandleSpineEvent;

            ReleaseAttackInputLock();
        }

        // ========== ATTACK HANDLING ==========

        /// <summary>
        /// Called by WeaponManager to play attack animation.
        /// Handles all attack directions: Side (combo), Up, Down.
        /// </summary>
        public void PlayAttack(AttackDirection dir, int comboIndex)
        {
            string anim;
            int entryIndex = comboIndex;

            switch (dir)
            {
                case AttackDirection.Up:
                    anim = !string.IsNullOrEmpty(upAttackAnim) && HasAnimation(upAttackAnim)
                        ? upAttackAnim
                        : fallbackAttack;
                    entryIndex = -1; // Use default timing for up attacks
                    break;

                case AttackDirection.Down:
                    anim = !string.IsNullOrEmpty(downAttackAnim) && HasAnimation(downAttackAnim)
                        ? downAttackAnim
                        : fallbackAttack;
                    entryIndex = -1; // Use default timing for down attacks
                    break;

                default: // Side combo
                    anim = fallbackAttack;
                    if (comboAttacks != null && comboAttacks.Length > 0 && comboIndex >= 0)
                    {
                        int idx = Mathf.Clamp(comboIndex, 0, comboAttacks.Length - 1);
                        if (!string.IsNullOrEmpty(comboAttacks[idx]))
                        {
                            anim = comboAttacks[idx];
                            entryIndex = idx;
                        }
                    }
                    break;
            }

            LogAttack($"PlayAttack: dir={dir}, anim={anim}, entryIndex={entryIndex}");
            StartAttack(anim, entryIndex);
        }

        /// <summary>
        /// Called by PlayerState when combo attack is triggered (legacy support).
        /// Now primarily used for side attacks triggered via TriggerComboAttack.
        /// </summary>
        private void OnComboAttackTriggered(int comboIndex)
        {
            // NOTE: WeaponManager now calls PlayAttack() directly, so this is kept
            // for backwards compatibility but should not trigger duplicate animations.
            // If attackActive is already true, we skip since PlayAttack was already called.
            if (attackActive)
            {
                LogAttack($"OnComboAttackTriggered ignored (already attacking): comboIndex={comboIndex}");
                return;
            }

            // Fallback: if somehow triggered without PlayAttack, play the animation
            LogAttack($"OnComboAttackTriggered fallback: comboIndex={comboIndex}");
            PlayAttack(AttackDirection.Side, comboIndex);
        }

        private void StartAttack(string anim, int entryIndex = -1)
        {
            if (!HasAnimation(anim))
            {
                LogAttack($"Animation not found: {anim} - completing attack immediately");
                // No animation exists - notify WeaponManager to complete attack
                NotifyWeaponManagerAttackComplete();
                return;
            }

            // If already attacking, this is an error state - notify WeaponManager
            if (attackActive)
            {
                LogAttack($"StartAttack called while already attacking - forcing complete");
                ForceFinishAttack();
            }

            attackActive = true;
            ApplyAttackInputLock();

            bool useOverwrite = GetEntryBool(attackEntryOverwrite, entryIndex, attackOverwrite);
            float mixIn = GetEntryFloat(attackEntryMix, entryIndex, attackMix);
            float mixOut = GetEntryFloat(attackEntryMixOut, entryIndex, attackMixOut);
            float mixOutDelay = GetEntryFloat(attackEntryMixOutDelay, entryIndex, attackMixOutDelay);
            float attachmentThreshold = GetEntryFloat(attackEntryAttachmentThreshold, entryIndex, attackMixAttachmentThreshold);
            float timeScale = GetAttackTimeScale(entryIndex);

            attackOverwriteActive = useOverwrite;
            cachedAttackMixOut = mixOut;
            cachedAttackMixOutDelay = mixOutDelay;

            if (useOverwrite)
            {
                var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, anim, false);
                entry.MixDuration = mixIn;
                entry.MixBlend = MixBlend.Replace;
                entry.TimeScale = timeScale;
                entry.Complete += _ => FinishAttackOverwrite();
                entry.Interrupt += _ => OnAttackInterrupted();
                currentAttackEntry = entry;

                LogAttack($"Attack (overwrite): {anim}, timeScale={timeScale}");
            }
            else
            {
                var entry = skeletonAnimation.AnimationState.SetAnimation(overlayTrack, anim, false);
                entry.MixDuration = mixIn;
                entry.MixBlend = MixBlend.Replace;
                entry.MixAttachmentThreshold = attachmentThreshold;
                entry.TimeScale = timeScale;
                entry.Complete += _ => FinishAttackOverlay();
                entry.Interrupt += _ => OnAttackInterrupted();
                currentAttackEntry = entry;

                LogAttack($"Attack (overlay): {anim}, timeScale={timeScale}");
            }
        }

        private void FinishAttackOverlay()
        {
            if (!attackActive) return;

            LogAttack("Attack overlay complete");

            attackActive = false;
            attackOverwriteActive = false;
            currentAttackEntry = null;
            ReleaseAttackInputLock();

            // Clear overlay track with blend out
            if (skeletonAnimation != null)
            {
                skeletonAnimation.AnimationState.AddEmptyAnimation(overlayTrack, cachedAttackMixOut, cachedAttackMixOutDelay);
            }

            // Notify WeaponManager - this is the key callback!
            NotifyWeaponManagerAttackComplete();
        }

        private void FinishAttackOverwrite()
        {
            if (!attackActive) return;

            LogAttack("Attack overwrite complete");

            attackActive = false;
            attackOverwriteActive = false;
            currentAttackEntry = null;
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

            LogAttack("Attack interrupted");

            attackActive = false;
            attackOverwriteActive = false;
            currentAttackEntry = null;
            ReleaseAttackInputLock();

            // Clear overlay if it was an overlay attack
            if (skeletonAnimation != null && !attackOverwriteActive)
            {
                skeletonAnimation.AnimationState.ClearTrack(overlayTrack);
            }

            // Notify WeaponManager of interruption
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
        /// Called when dash/stun/death interrupts attack
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

            // Notify WeaponManager
            if (weaponManager != null)
                weaponManager.OnAttackInterrupted();
        }

        // ========== STATE EVENT HANDLERS ==========

        private void OnGroundedChanged(bool grounded)
        {
            if (grounded)
            {
                ClearDoubleJumpFlag();
                if (wasAirborne)
                {
                    PlayLanding();
                    wasAirborne = false;
                }
                else
                {
                    float speed = GetSpeed();
                    PlayLocomotion(speed > speedThreshold ? run : idle, true);
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
            if (jumping)
                PlayJumpStart();
            Log($"Jumping={jumping}");
        }

        private void OnFallStateChanged(bool falling)
        {
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

        private void OnRollingChanged(bool rolling)
        {
            if (rolling)
            {
                InterruptAttack("roll");
                PlayLocomotion(roll, false);
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
                PlayLocomotion(wallSlide, true);
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

        private void OnDoubleJumpChanged(bool doubleJumping)
        {
            if (!doubleJumping) return;

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

        // ========== LOCOMOTION ==========

        private void PlayLocomotion(string animName, bool loop)
        {
            if (skeletonAnimation == null || string.IsNullOrEmpty(animName)) return;
            if (!HasAnimation(animName)) return;

            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, animName, loop);
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
                float currentSpeed = GetSpeed();
                PlayLocomotion(currentSpeed > speedThreshold ? run : idle, true);
                return;
            }

            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, landing, GetLoopFor(landing, false));
            entry.MixDuration = locomotionBlend;
            entry.TimeScale = GetTimeScaleFor(landing, 1f);
            currentLocomotionAnim = landing;

            // Queue idle/run after landing
            float speed = GetSpeed();
            string next = speed > speedThreshold ? run : idle;
            var nextEntry = skeletonAnimation.AnimationState.AddAnimation(locomotionTrack, next, true, 0f);
            nextEntry.MixDuration = locomotionBlend;
        }

        private void UpdateLocomotionFromSpeed()
        {
            if (IsPlayingTransientAnim()) return;
            if (playerState == null || !playerState.IsGrounded) return;
            if (attackActive) return;

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
            entry.TimeScale = Mathf.Clamp(scale, minRunTimeScale, maxRunTimeScale);
        }

        private void ApplyAnyStateFallbacks()
        {
            // Safety: ensure we're in a valid state
            if (playerState == null) return;

            string current = GetCurrentLocomotionName();

            // If grounded but still showing air animation
            if (playerState.IsGrounded && (current == jumpAir || current == jumpStart || current == doubleJump))
            {
                if (!attackActive)
                {
                    float speed = GetSpeed();
                    PlayLocomotion(speed > speedThreshold ? run : idle, true);
                }
            }
        }

        // ========== UTILITY ==========

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

        private bool GetEntryBool(bool[] values, int index, bool fallback)
        {
            if (values == null || index < 0 || index >= values.Length) return fallback;
            return values[index];
        }

        private float GetEntryFloat(float[] values, int index, float fallback)
        {
            if (values == null || index < 0 || index >= values.Length) return fallback;
            float value = values[index];
            return value > 0f ? value : fallback;
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

        private float GetAttackTimeScale(int entryIndex)
        {
            if (comboAttackTimeScales != null && entryIndex >= 0 && entryIndex < comboAttackTimeScales.Length)
            {
                float scale = comboAttackTimeScales[entryIndex];
                return scale > 0f ? scale : 1f;
            }
            return 1f;
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

        private void ApplyAttackInputLock()
        {
            if (!lockMovementDuringAttack || playerState == null || attackInputLockApplied)
                return;

            if (!playerState.IsInputLocked)
            {
                playerState.SetInputLocked(true);
                if (controller != null)
                    controller.StopAllVelocity();
                attackInputLockApplied = true;
            }
        }

        private void ReleaseAttackInputLock()
        {
            if (!lockMovementDuringAttack || playerState == null || !attackInputLockApplied)
                return;

            playerState.SetInputLocked(false);
            attackInputLockApplied = false;
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
    }
}