using UnityEngine;
using Spine;
using Spine.Unity;

namespace junklite
{
    /// <summary>
    /// Event-driven Spine animation controller.
    /// Jump Flow: Jump_1_Start -> Jump_2_Air (hold last frame) -> Jump_3_Land (on ground, auto to idle)
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

        [Header("Attack Buffer")]
        [Tooltip("How long before attack animation ends to allow buffering next attack (in seconds)")]
        [SerializeField] private float attackBufferWindow = 0.3f;
        [Tooltip("Maximum time a buffered attack can wait before expiring")]
        [SerializeField] private float maxBufferDuration = 0.5f;

        [Header("Footsteps")]
        [SerializeField] private EventDataReferenceAsset footstepEvent;
        [SerializeField] private AudioSource footstepSource;
        [SerializeField] private float footstepPitchOffset = 0.2f;

        [Header("Debug")]
        [SerializeField] private bool logStateChanges = false;
        [SerializeField] private bool logAttacks = false;

        private PlayerState playerState;
        private Character2D5Controller controller;
        
        // State tracking
        private bool wasAirborne = false;
        private string currentLocomotionAnim = "";
        private bool attackActive = false;
        private bool attackOverwriteActive = false;
        private float cachedAttackMixOut = 0.25f;
        private float cachedAttackMixOutDelay = 0.05f;
        private bool attackInputLockApplied = false;

        // Attack buffer
        private bool attackBuffered = false;
        private int bufferedComboIndex = -1;
        private float bufferTimer = 0f;
        private TrackEntry currentAttackEntry = null;

        private void Awake()
        {
            if (!skeletonAnimation)
                skeletonAnimation = GetComponentInChildren<SkeletonAnimation>();

            playerState = GetComponentInParent<PlayerState>();
            controller = GetComponentInParent<Character2D5Controller>();

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
            playerState.OnAttackBuffered += OnAttackBuffered;
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

            // ANY STATE fallbacks (similar to Unity Animator "Any State" transitions)
            ApplyAnyStateFallbacks();

            // Continuous idle/run blending based on speed
            UpdateLocomotionFromSpeed();

            // Update run speed scaling
            UpdateRunSpeed();

            // Handle attack buffer timer
            if (attackBuffered)
            {
                bufferTimer -= Time.deltaTime;
                if (bufferTimer <= 0f)
                {
                    // Buffer expired
                    attackBuffered = false;
                    bufferedComboIndex = -1;
                    if (logAttacks)
                        Debug.Log("[SpineAnim] Attack buffer expired", this);
                }
            }

            // Update buffer window state in PlayerState based on animation timing
            if (attackActive && currentAttackEntry != null && playerState != null)
            {
                float remainingTime = currentAttackEntry.AnimationEnd - currentAttackEntry.TrackTime;
                bool inBufferWindow = remainingTime <= attackBufferWindow && remainingTime > 0f;
                playerState.SetCanBufferAttack(inBufferWindow);
            }
            else if (playerState != null)
            {
                playerState.SetCanBufferAttack(false);
            }
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
                playerState.OnAttackBuffered -= OnAttackBuffered;
                playerState.OnDeath -= OnDeath;
            }

            if (controller != null)
                controller.OnDoubleJumpPerformed -= OnControllerDoubleJumpPerformed;

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.Event -= HandleSpineEvent;

            ReleaseAttackInputLock();
        }

        // ========== EVENT HANDLERS ==========

        private void OnGroundedChanged(bool grounded)
        {
            if (grounded)
            {
                ClearDoubleJumpFlag();
                // Landed: play landing animation if we were airborne
                if (wasAirborne)
                {
                    PlayLanding();
                    wasAirborne = false;
                }
                else
                {
                    // Just became grounded (initial spawn, etc) - go to idle/run
                    float speed = GetSpeed();
                    PlayLocomotion(speed > speedThreshold ? run : idle, true);
                }
            }
            else
            {
                // Left ground
                wasAirborne = true;
            }

            Log($"Grounded={grounded}, wasAirborne={wasAirborne}");
        }

        private void OnMovingChanged(bool moving)
        {
            // Movement state changed - UpdateLocomotionFromSpeed() in Update() handles the actual animation
            Log($"Moving={moving}");
        }

        private void OnJumpStateChanged(bool jumping)
        {
            if (jumping)
            {
                // Start jump sequence: Jump_1_Start -> Jump_2_Air
                PlayJumpStart();
            }

            Log($"Jumping={jumping}");
        }

        private void OnFallStateChanged(bool falling)
        {
            // Falling state changed (not used in current flow since Jump_2_Air handles it)
            Log($"Falling={falling}");
        }

        private void OnDashingChanged(bool dashing)
        {
            if (dashing)
            {
                InterruptAttack("dash");
                PlayLocomotion(dash, false);
                Log($"Dash started");
            }
            else
            {
                // Dash ended - return to appropriate state
                if (playerState.IsGrounded)
                {
                    float speed = GetSpeed();
                    PlayLocomotion(speed > speedThreshold ? run : idle, true);
                }
                else
                {
                    // Ended dash in air - go to jump air
                    PlayJumpAir();
                    Log($"Dash ended (airborne) -> {jumpAir}");
                }
            }
        }

        private void OnRollingChanged(bool rolling)
        {
            if (rolling)
            {
                PlayLocomotion(roll, false);
                Log($"Roll started");
            }
            else
            {
                // Roll ended - return to appropriate state
                if (playerState.IsGrounded)
                {
                    float speed = GetSpeed();
                    PlayLocomotion(speed > speedThreshold ? run : idle, true);
                }
                else
                {
                    // Ended roll in air - go to jump air
                    PlayJumpAir();
                    Log($"Roll ended (airborne) -> {jumpAir}");
                }
            }
        }

        private void OnWallSlideChanged(bool sliding)
        {
            if (sliding)
            {
                PlayLocomotion(wallSlide, true);
                Log($"Wall slide started");
            }
            else
            {
                // Wall slide ended
                if (playerState.IsGrounded)
                {
                    float speed = GetSpeed();
                    PlayLocomotion(speed > speedThreshold ? run : idle, true);
                    Log($"Wall slide ended (grounded) -> locomotion");
                }
                else
                {
                    // Still airborne - go to jump air
                    PlayJumpAir();
                    Log($"Wall slide ended (airborne) -> {jumpAir}");
                }
            }
        }

        private void OnDoubleJumpChanged(bool doubleJumping)
        {
            if (!doubleJumping)
                return;

            // Double jump can only happen after we've jumped (in air)
            if (!playerState.IsJumping && !playerState.IsFalling)
            {
                Log($"Double jump ignored - not in jump state");
                return;
            }

            if (!HasAnimation(doubleJump))
            {
                // No double jump anim, just go to jump air
                PlayJumpAir();
                Log($"Double jump (no anim) -> {jumpAir}");
                ClearDoubleJumpFlag();
                return;
            }

            // Play double jump, then transition to jump air
            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, doubleJump, GetLoopFor(doubleJump, false));
            entry.MixDuration = doubleJumpBlend;
            entry.MixBlend = MixBlend.Replace; // avoid blended rotations during flip
            entry.TimeScale = GetTimeScaleFor(doubleJump, 1f);
            currentLocomotionAnim = doubleJump;
            entry.Complete += _ => ClearDoubleJumpFlag();
            entry.Interrupt += _ => ClearDoubleJumpFlag();
            entry.End += _ => ClearDoubleJumpFlag();

            // After double jump completes, play jump air and hold
            var airEntry = skeletonAnimation.AnimationState.AddAnimation(locomotionTrack, jumpAir, GetLoopFor(jumpAir, false), 0f);
            airEntry.TimeScale = GetTimeScaleFor(jumpAir, 1f);
            airEntry.Complete += OnJumpAirComplete;

            Log($"Double jump: {doubleJump} -> {jumpAir}");
        }

        private void OnControllerDoubleJumpPerformed()
        {
            // Controller-level event: always play double jump, regardless of current state
            if (!HasAnimation(doubleJump))
            {
                PlayJumpAir();
                Log($"Double jump (controller, no anim) -> {jumpAir}");
                return;
            }

            string current = GetCurrentLocomotionName();
            if (current == doubleJump)
                return;

            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, doubleJump, GetLoopFor(doubleJump, false));
            entry.MixDuration = doubleJumpBlend;
            entry.MixBlend = MixBlend.Replace;
            entry.TimeScale = GetTimeScaleFor(doubleJump, 1f);
            currentLocomotionAnim = doubleJump;
            entry.Complete += _ => ClearDoubleJumpFlag();
            entry.Interrupt += _ => ClearDoubleJumpFlag();
            entry.End += _ => ClearDoubleJumpFlag();

            var airEntry = skeletonAnimation.AnimationState.AddAnimation(locomotionTrack, jumpAir, GetLoopFor(jumpAir, false), 0f);
            airEntry.TimeScale = GetTimeScaleFor(jumpAir, 1f);
            airEntry.Complete += OnJumpAirComplete;

            Log($"Double jump (controller): {doubleJump} -> {jumpAir}");
        }

        private void OnStunnedChanged(bool stunned)
        {
            if (stunned)
            {
                InterruptAttack("stun");
                if (HasAnimation(stun))
                {
                    PlayLocomotion(stun, true);
                    Log($"Stun started");
                }
            }
            else
            {
                // Stun ended - return to appropriate state
                if (playerState.IsGrounded)
                {
                    float speed = GetSpeed();
                    PlayLocomotion(speed > speedThreshold ? run : idle, true);
                    Log($"Stun ended (grounded) -> locomotion");
                }
                else
                {
                    // Ended stun in air - go to jump air
                    PlayJumpAir();
                    Log($"Stun ended (airborne) -> {jumpAir}");
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
                Log($"Death animation triggered");
            }
        }

        private void OnComboAttackTriggered(int comboIndex)
        {
            string anim = fallbackAttack;
            int entryIndex = comboIndex;
            if (comboAttacks != null && comboAttacks.Length > 0)
            {
                int idx = Mathf.Clamp(entryIndex, 0, comboAttacks.Length - 1);
                if (!string.IsNullOrEmpty(comboAttacks[idx]))
                {
                    anim = comboAttacks[idx];
                    entryIndex = idx;
                }
            }

            StartAttack(anim, entryIndex);
        }

        /// <summary>
        /// Handles attack buffered event from PlayerState.
        /// </summary>
        private void OnAttackBuffered(int comboIndex)
        {
            if (!attackActive)
                return;

            // Buffer the attack
            attackBuffered = true;
            bufferedComboIndex = comboIndex;
            bufferTimer = maxBufferDuration;
            
            if (logAttacks)
                Debug.Log($"[SpineAnim] Attack buffered: comboIndex={comboIndex}", this);
        }

        private void StartAttack(string anim, int entryIndex = -1)
        {
            if (!HasAnimation(anim)) return;
            if (attackActive)
                return; // wait for completion unless interrupted by dash/stun/death

            attackActive = true;
            ApplyAttackInputLock();
            if (playerState != null)
            {
                playerState.SetAttacking(true);
                playerState.SetCanBufferAttack(false); // Clear buffer state when new attack starts
            }
            bool useOverwrite = GetEntryBool(attackEntryOverwrite, entryIndex, attackOverwrite);
            float mixIn = GetEntryFloat(attackEntryMix, entryIndex, attackMix);
            float mixOut = GetEntryFloat(attackEntryMixOut, entryIndex, attackMixOut);
            float mixOutDelay = GetEntryFloat(attackEntryMixOutDelay, entryIndex, attackMixOutDelay);
            float attachmentThreshold = GetEntryFloat(attackEntryAttachmentThreshold, entryIndex, attackMixAttachmentThreshold);

            // Get timing for this attack
            float timeScale = GetAttackTimeScale(entryIndex);

            attackOverwriteActive = useOverwrite;

            if (useOverwrite)
            {
                var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, anim, false);
                entry.MixDuration = mixIn;
                entry.MixBlend = MixBlend.Replace;
                entry.TimeScale = timeScale;
                entry.Complete += _ => FinishAttackOverwrite();
                entry.Interrupt += _ => FinishAttackImmediate();
                entry.End += _ => FinishAttackImmediate();
                currentAttackEntry = entry;

                if (logAttacks)
                    Debug.Log($"[SpineAnim] Attack (overwrite): {anim} on track {locomotionTrack}, timeScale={timeScale}", this);
            }
            else
            {
                var entry = skeletonAnimation.AnimationState.SetAnimation(overlayTrack, anim, false);
                entry.MixDuration = mixIn;
                entry.MixBlend = MixBlend.Replace;
                entry.MixAttachmentThreshold = attachmentThreshold;
                entry.TimeScale = timeScale;
                entry.Complete += _ => FinishAttackOverlay();
                entry.Interrupt += _ => FinishAttackImmediate();
                entry.End += _ => FinishAttackImmediate();
                currentAttackEntry = entry;

                if (logAttacks)
                    Debug.Log($"[SpineAnim] Attack (overlay): {anim} on track {overlayTrack}, timeScale={timeScale}", this);
            }

            cachedAttackMixOut = mixOut;
            cachedAttackMixOutDelay = mixOutDelay;
        }

        private void FinishAttackOverlay()
        {
            if (!attackActive) return;

            attackActive = false;
            attackOverwriteActive = false;
            currentAttackEntry = null;
            ReleaseAttackInputLock();
            if (playerState != null)
            {
                playerState.SetAttacking(false);
                playerState.SetCanBufferAttack(false);
            }

            // Check for buffered attack BEFORE blending out
            if (attackBuffered && bufferedComboIndex >= 0)
            {
                int bufferedIndex = bufferedComboIndex;
                attackBuffered = false;
                bufferedComboIndex = -1;
                bufferTimer = 0f;

                if (logAttacks)
                    Debug.Log($"[SpineAnim] Executing buffered attack: comboIndex={bufferedIndex}", this);

                // Trigger the buffered attack
                if (playerState != null)
                    playerState.TriggerComboAttack(bufferedIndex);
            }
            else
            {
            // Blend overlay track out after the attack completes
            skeletonAnimation.AnimationState.AddEmptyAnimation(overlayTrack, cachedAttackMixOut, cachedAttackMixOutDelay);
            }
        }

        private void FinishAttackOverwrite()
        {
            if (!attackActive) return;

            attackActive = false;
            attackOverwriteActive = false;
            currentAttackEntry = null;
            ReleaseAttackInputLock();
            if (playerState != null)
            {
                playerState.SetAttacking(false);
                playerState.SetCanBufferAttack(false);
            }

            // Check for buffered attack
            if (attackBuffered && bufferedComboIndex >= 0)
            {
                int bufferedIndex = bufferedComboIndex;
                attackBuffered = false;
                bufferedComboIndex = -1;
                bufferTimer = 0f;

                if (logAttacks)
                    Debug.Log($"[SpineAnim] Executing buffered attack: comboIndex={bufferedIndex}", this);

                // Trigger the buffered attack
                if (playerState != null)
                    playerState.TriggerComboAttack(bufferedIndex);
            }
            else
            {
            // Restore appropriate state after overwrite attack
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

        private void FinishAttackImmediate()
        {
            if (!attackActive) return;

            attackActive = false;
            attackOverwriteActive = false;
            currentAttackEntry = null;
            attackBuffered = false;
            bufferedComboIndex = -1;
            bufferTimer = 0f;
            ReleaseAttackInputLock();
            if (playerState != null)
            {
                playerState.SetAttacking(false);
                playerState.SetCanBufferAttack(false);
            }

            skeletonAnimation.AnimationState.ClearTrack(overlayTrack);
        }

        private void InterruptAttack(string reason)
        {
            if (!attackActive) return;

            if (!attackOverwrite)
                skeletonAnimation.AnimationState.ClearTrack(overlayTrack);

            attackActive = false;
            attackOverwriteActive = false;
            currentAttackEntry = null;
            attackBuffered = false;
            bufferedComboIndex = -1;
            bufferTimer = 0f;
            ReleaseAttackInputLock();
            if (playerState != null)
            {
                playerState.SetAttacking(false);
                playerState.SetCanBufferAttack(false);
            }

            if (logAttacks)
                Debug.Log($"[SpineAnim] Attack interrupted: {reason}", this);
        }

        // ========== ANIMATION PLAYBACK ==========

        private void PlayJumpStart()
        {
            if (!HasAnimation(jumpStart) || !HasAnimation(jumpAir))
            {
                Debug.LogWarning($"[SpineAnim] Missing jump animations!", this);
                return;
            }

            // Play Jump_1_Start, then immediately queue Jump_2_Air
            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, jumpStart, GetLoopFor(jumpStart, false));
            entry.MixDuration = locomotionBlend;
            entry.TimeScale = GetTimeScaleFor(jumpStart, 1f);
            currentLocomotionAnim = jumpStart;

            // Queue Jump_2_Air after Jump_1_Start
            var airEntry = skeletonAnimation.AnimationState.AddAnimation(locomotionTrack, jumpAir, GetLoopFor(jumpAir, false), 0f);
            airEntry.TimeScale = GetTimeScaleFor(jumpAir, 1f);
            airEntry.Complete += OnJumpAirComplete;

            Log($"Jump sequence: {jumpStart} -> {jumpAir}");
        }

        private void PlayJumpAir()
        {
            if (!HasAnimation(jumpAir)) return;

            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, jumpAir, GetLoopFor(jumpAir, false));
            entry.TimeScale = GetTimeScaleFor(jumpAir, 1f);
            entry.Complete += OnJumpAirComplete;
            currentLocomotionAnim = jumpAir;

            Log($"Playing {jumpAir}");
        }

        private void OnJumpAirComplete(TrackEntry entry)
        {
            // Hold on last frame while in air
            if (!playerState.IsGrounded)
            {
                entry.TrackTime = entry.AnimationEnd;
                entry.TimeScale = 0f;
                Log($"{jumpAir} complete, holding last frame");
            }
        }

        private void PlayLanding()
        {
            if (!HasAnimation(landing))
            {
                // No landing animation, go straight to idle/run based on speed
                float speed = GetSpeed();
                PlayLocomotion(speed > speedThreshold ? run : idle, true);
                return;
            }

            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, landing, GetLoopFor(landing, false));
            entry.MixDuration = locomotionBlend;
            entry.TimeScale = GetTimeScaleFor(landing, 1f);
            currentLocomotionAnim = landing;

            // Check speed during landing to queue run or idle
            float currentSpeed = GetSpeed();
            if (currentSpeed > speedThreshold)
            {
                var runEntry = skeletonAnimation.AnimationState.AddAnimation(locomotionTrack, run, GetLoopFor(run, true), 0f);
                runEntry.TimeScale = GetTimeScaleFor(run, 1f);
                Log($"Landing: {landing} -> {run}");
            }
            else
            {
                var idleEntry = skeletonAnimation.AnimationState.AddAnimation(locomotionTrack, idle, GetLoopFor(idle, true), 0f);
                idleEntry.TimeScale = GetTimeScaleFor(idle, 1f);
                Log($"Landing: {landing} -> {idle}");
            }
        }

        private void PlayLocomotion(string animName, bool loop)
        {
            if (!HasAnimation(animName) || GetCurrentLocomotionName() == animName)
                return;

            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, animName, GetLoopFor(animName, loop));
            entry.MixDuration = locomotionBlend;
            entry.TimeScale = GetTimeScaleFor(animName, 1f);
            currentLocomotionAnim = animName;

            Log($"Locomotion: {animName} (loop={loop}, timeScale={entry.TimeScale})");
        }

        /// <summary>
        /// Any State fallbacks - ensures we're always in a valid animation state.
        /// Similar to Unity Animator's "Any State" transitions.
        /// </summary>
        private void ApplyAnyStateFallbacks()
        {
            // Never override death or stun
            if (!playerState.IsAlive || playerState.IsStunned)
                return;

            // Dash can interrupt any state; never override it here
            if (playerState.IsDashing)
                return;

            // Attack overwrite owns locomotion track
            if (attackOverwriteActive)
                return;

            string current = GetCurrentLocomotionName();

            // Don't interrupt the double jump animation
            if (playerState.IsDoubleJumping || current == doubleJump)
                return;

            // Priority 1: If airborne and falling, force Jump_2_Air (from ANY state except landing)
            if (!playerState.IsGrounded && playerState.IsFalling)
            {
                // Don't interrupt landing animation or if already in jump air
                if (current != jumpAir && current != landing)
                {
                    PlayJumpAir();
                    Log($"[Fallback] Not grounded + falling -> {jumpAir}");
                }
                return; // Don't check grounded fallback
            }

            // Priority 2: If grounded and no special state, ensure we're in idle/run (from ANY state)
            if (playerState.IsGrounded)
            {
                // Don't interrupt special animations (dash, roll, landing, etc.)
                if (IsPlayingTransientAnim())
                    return;

                // If we're in an airborne animation while grounded, fix it
                if (current == jumpStart || current == jumpAir || 
                    current == doubleJump || current == wallSlide)
                {
                    float speed = GetSpeed();
                    string targetAnim = speed > speedThreshold ? run : idle;
                    PlayLocomotion(targetAnim, true);
                    Log($"[Fallback] Grounded + air anim -> {targetAnim}");
                }
            }
        }

        private void UpdateLocomotionFromSpeed()
        {
            // Only update idle/run if grounded and not in a transient animation
            if (!playerState.IsGrounded || IsPlayingTransientAnim() || attackOverwriteActive)
                return;

            float speed = GetSpeed();
            string targetAnim = speed > speedThreshold ? run : idle;

            // Only switch if different from current
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
            scale = Mathf.Clamp(scale, minRunTimeScale, maxRunTimeScale);
            // Multiply by base timeScale from inspector
            entry.TimeScale = scale * runTimeScale;
        }

        // ========== UTILITY ==========

        private bool IsPlayingTransientAnim()
        {
            string current = GetCurrentLocomotionName();
            // Transient animations that shouldn't be interrupted by normal locomotion updates
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
            SafeSetMix(data, jumpStart, jumpAir, 0f); // Instant transition
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
    }
}
