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

        [Header("Left / Right Skeletons (SkeletonSwap player)")]
        [Tooltip("Shown while facing right. Leave both empty on the old single-skeleton prefab.")]
        [SerializeField] private SkeletonAnimation rightSkeleton;
        [Tooltip("Shown while facing left. Authored facing left, never mirrored by scale.")]
        [SerializeField] private SkeletonAnimation leftSkeleton;
        [Tooltip("Followers moved onto the shown skeleton (weapon socket, muzzle). Both skeletons need each follower's bone.")]
        [SerializeField] private BoneFollower[] boneFollowers;

        [Tooltip("Local forward offset of the Spine artwork during a wall hold/slide. Aligns the hand pose without moving the physics capsule.")]
        [SerializeField] private float wallPoseForwardOffset;
        private Vector3 appliedWallPoseOffset;

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
        [SerializeField, InspectorName("Wall Attach")] private string wallSlide = "wallSlide";
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
        [SerializeField, InspectorName("Wall Attach Loop")] private bool wallSlideLoop = false;
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
        [SerializeField, InspectorName("Wall Attach Time Scale"), Range(0.1f, 3f)] private float wallSlideTimeScale = 1f;
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
        private PlayerCharacter playerCharacter;

        // State tracking
        private bool wasAirborne = false;
        private string currentLocomotionAnim = "";
        private bool attackActive = false;
        private TrackEntry currentAttackEntry = null;
        private bool forceOverrideActive = false;

        // when an attack animation finishes during a parry, we defer the end until parry state clears
        private bool waitingForParryEnd = false;

        public TrackEntry CurrentAttackEntry => currentAttackEntry;

        /// <summary>The skeleton currently shown (the facing side on a SkeletonSwap player).</summary>
        public SkeletonAnimation ActiveSkeleton => skeletonAnimation;

        private bool HasSidedSkeletons => rightSkeleton != null && leftSkeleton != null;

        // Once dead, only the death animation may own the skeleton: late attack/jump/landing
        // requests (queued combo hits, Spine events) would otherwise replace the death pose.
        private bool IsDead => playerState != null && !playerState.IsAlive;

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
            playerState = GetComponentInParent<PlayerState>();
            controller = GetComponentInParent<Character2D5Controller>();
            audioHandler = GetComponentInParent<PlayerAudioHandler>() ?? GetComponent<PlayerAudioHandler>();
            weaponManager = GetComponentInParent<WeaponManager>();
            playerCharacter = GetComponentInParent<PlayerCharacter>();

            if (HasSidedSkeletons)
            {
                bool facingRight = controller == null || controller.IsFacingRight;
                skeletonAnimation = facingRight ? rightSkeleton : leftSkeleton;
                InitializeSkeleton(rightSkeleton);
                InitializeSkeleton(leftSkeleton);
                rightSkeleton.gameObject.SetActive(facingRight);
                leftSkeleton.gameObject.SetActive(!facingRight);
                SetBoneFollowerTarget(skeletonAnimation);
                return;
            }

            if (!skeletonAnimation)
                skeletonAnimation = GetComponentInChildren<SkeletonAnimation>();

            InitializeSkeleton(skeletonAnimation);
        }

        private void InitializeSkeleton(SkeletonAnimation target)
        {
            if (target == null) return;
            target.Initialize(false);
            ConfigureMixes(target.AnimationState?.Data);
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
            playerState.OnWallAttachedChanged += OnWallAttachedChanged;
            playerState.OnWallSlidingChanged += OnWallAttachedChanged;
            playerState.OnLedgeDetectedChanged += OnLedgeDetectedChanged;
            playerState.OnParryChanged += OnParryChanged;
            playerState.OnDoubleJumpChanged += OnDoubleJumpChanged;
            playerState.OnStunnedChanged += OnStunnedChanged;
            playerState.OnDeath += OnDeath;
            playerState.OnAttackingChanged += OnAttackingChanged;

            playerState.OnAttackAnimationRequested += OnAttackAnimationRequested; // new

            if (playerCharacter != null)
                playerCharacter.OnRevived += OnRevived;

            // Only the shown skeleton updates, so subscribing both never doubles an event.
            if (HasSidedSkeletons)
            {
                rightSkeleton.AnimationState.Event += HandleSpineEvent;
                leftSkeleton.AnimationState.Event += HandleSpineEvent;
            }
            else if (skeletonAnimation != null)
            {
                skeletonAnimation.AnimationState.Event += HandleSpineEvent;
            }

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

            SyncSkeletonToFacing();

            if ((playerState.IsWallAttached || playerState.IsWallSliding) && playerState.IsAlive && !playerState.IsStunned)
            {
                PlayWallAttachment();
                return;
            }

            SyncCurrentLocomotion();
            ApplyAnyStateFallbacks();
            UpdateLocomotionFromSpeed();
            UpdateRunSpeed();
        }

        private void LateUpdate()
        {
            if (skeletonAnimation == null || skeletonAnimation.transform == transform) return;
            bool wallPose = playerState != null && playerState.IsAlive && !playerState.IsStunned &&
                           (playerState.IsWallAttached || playerState.IsWallSliding);
            // A sided skeleton is never mirrored, so the forward offset follows facing itself.
            float forward = HasSidedSkeletons && controller != null ? controller.FacingSign : 1f;
            Vector3 offset = wallPose ? Vector3.right * (wallPoseForwardOffset * forward) : Vector3.zero;
            skeletonAnimation.transform.localPosition += offset - appliedWallPoseOffset;
            appliedWallPoseOffset = offset;
        }

        private void OnDisable()
        {
            if (skeletonAnimation != null) skeletonAnimation.transform.localPosition -= appliedWallPoseOffset;
            appliedWallPoseOffset = Vector3.zero;
        }

        private void OnDestroy()
        {
            if (playerState != null)
            {
                playerState.OnGroundedChanged -= OnGroundedChanged;
                playerState.OnJumpStateChanged -= OnJumpStateChanged;
                playerState.OnDashingChanged -= OnDashingChanged;
                playerState.OnWallAttachedChanged -= OnWallAttachedChanged;
                playerState.OnWallSlidingChanged -= OnWallAttachedChanged;
                playerState.OnLedgeDetectedChanged -= OnLedgeDetectedChanged;
                playerState.OnParryChanged -= OnParryChanged;
                playerState.OnDoubleJumpChanged -= OnDoubleJumpChanged;
                playerState.OnStunnedChanged -= OnStunnedChanged;
                playerState.OnDeath -= OnDeath;
                playerState.OnAttackingChanged -= OnAttackingChanged;
                playerState.OnAttackAnimationRequested -= OnAttackAnimationRequested;
            }

            if (playerCharacter != null)
                playerCharacter.OnRevived -= OnRevived;

            if (controller != null)
                controller.OnDoubleJumpPerformed -= OnControllerDoubleJumpPerformed;

            if (weaponManager != null)
                weaponManager.OnCombatModeChanged -= HandleCombatModeChanged;

            if (HasSidedSkeletons)
            {
                if (rightSkeleton.AnimationState != null) rightSkeleton.AnimationState.Event -= HandleSpineEvent;
                if (leftSkeleton.AnimationState != null) leftSkeleton.AnimationState.Event -= HandleSpineEvent;
            }
            else if (skeletonAnimation != null)
            {
                skeletonAnimation.AnimationState.Event -= HandleSpineEvent;
            }
        }

        #endregion

        #region Left / Right Skeleton Swap

        private void SyncSkeletonToFacing()
        {
            // A force override's restore callback lives on its track entry, so wait until it ends.
            if (!HasSidedSkeletons || forceOverrideActive) return;

            SkeletonAnimation shown = controller.IsFacingRight ? rightSkeleton : leftSkeleton;
            if (shown != skeletonAnimation)
                SwapSkeleton(shown);
        }

        private void SwapSkeleton(SkeletonAnimation next)
        {
            SkeletonAnimation previous = skeletonAnimation;
            previous.transform.localPosition -= appliedWallPoseOffset;
            appliedWallPoseOffset = Vector3.zero;

            next.gameObject.SetActive(true);
            Spine.AnimationState from = previous.AnimationState;
            Spine.AnimationState to = next.AnimationState;
            to.ClearTracks();

            var tracks = from.Tracks;
            for (int track = 0; track < tracks.Count; track++)
            {
                TrackEntry source = tracks.Items[track];
                if (source == null) continue;

                CopyTrackEntry(to, track, source, true);
                for (TrackEntry queued = source.Next; queued != null; queued = queued.Next)
                    CopyTrackEntry(to, track, queued, false);
            }

            // ClearTracks fires End only (never Complete/Interrupt), so no callback runs twice.
            from.ClearTracks();
            previous.gameObject.SetActive(false);
            skeletonAnimation = next;
            SetBoneFollowerTarget(next);

            // Pose and rebuild the mesh now so the swap frame never shows a stale body.
            next.Update(0f);
            next.LateUpdate();
        }

        private void CopyTrackEntry(Spine.AnimationState to, int track, TrackEntry source, bool current)
        {
            Spine.Animation animation = to.Data.SkeletonData.FindAnimation(source.Animation.Name);
            TrackEntry copy;
            if (animation == null) // empty animation, or one this skeleton lacks
                copy = current ? to.SetEmptyAnimation(track, 0f) : to.AddEmptyAnimation(track, source.MixDuration, source.Delay);
            else
                copy = current ? to.SetAnimation(track, animation, source.Loop) : to.AddAnimation(track, animation, source.Loop, source.Delay);

            copy.TimeScale = source.TimeScale;
            copy.Alpha = source.Alpha;
            copy.MixBlend = source.MixBlend;
            copy.HoldPrevious = source.HoldPrevious;
            if (current)
            {
                copy.TrackTime = source.TrackTime;
                copy.MixDuration = 0f; // same pose on the other side: nothing to blend from
            }
            else
            {
                copy.MixDuration = source.MixDuration;
            }

            RehookCallbacks(track, source, copy);
        }

        // Track entry callbacks do not carry over, so re-add the ones this controller relies on.
        private void RehookCallbacks(int track, TrackEntry source, TrackEntry copy)
        {
            if (source == currentAttackEntry)
            {
                HookAttackEntry(copy, track == overlayTrack && !IsParryHit(source.Animation.Name));
                currentAttackEntry = copy;
                return;
            }

            string animationName = source.Animation.Name;
            if (animationName == jumpAir)
            {
                copy.Complete += OnJumpAirComplete;
            }
            else if (animationName == doubleJump)
            {
                copy.Complete += _ => ClearDoubleJumpFlag();
                copy.Interrupt += _ => ClearDoubleJumpFlag();
            }
        }

        private void SetBoneFollowerTarget(SkeletonAnimation target)
        {
            if (boneFollowers == null) return;
            foreach (BoneFollower follower in boneFollowers)
            {
                if (follower != null)
                    follower.SkeletonRenderer = target;
            }
        }

        #endregion

        #region Attack Animation (Public API)

        /// <summary>
        /// Plays an attack animation by name. Called via PlayerState request.
        /// </summary>
        public void PlayAttackAnimation(string animationName)
        {
            if (IsDead) { NotifyPlayerAttackComplete(); return; }

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
            if (IsParryHit(animationName))
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
                    HookAttackEntry(entry, false);
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
                HookAttackEntry(entry, false);
                currentAttackEntry = entry;
            }
            else
            {
                var entry = skeletonAnimation.AnimationState.SetAnimation(overlayTrack, animationName, false);
                entry.MixDuration = attackMix;
                entry.MixBlend = MixBlend.Replace;
                entry.TimeScale = timeScale;
                HookAttackEntry(entry, true);
                currentAttackEntry = entry;
            }
        }

        // Perry_2 fully replaces the pose on the overlay track but finishes like an overwrite attack.
        private static bool IsParryHit(string animationName) =>
            string.Equals(animationName, "Perry_2", StringComparison.OrdinalIgnoreCase);

        private void HookAttackEntry(TrackEntry entry, bool finishAsOverlay)
        {
            if (finishAsOverlay) entry.Complete += _ => FinishAttackOverlay();
            else entry.Complete += _ => FinishAttackOverwrite();
            entry.Interrupt += _ => OnAttackInterrupted();
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
            if (IsDead) { onComplete?.Invoke(); return false; }

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

        /// <summary>Knocked down: plays death once and holds the last frame until EndKnockdown.</summary>
        public void PlayKnockdown()
        {
            if (skeletonAnimation == null || !HasAnimation(death))
                return;

            InterruptAttack("knockdown");
            forceOverrideActive = true;
            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, death, false);
            entry.MixDuration = 0f;
            entry.TimeScale = GetTimeScaleFor(death, 1f);
            currentLocomotionAnim = death;
        }

        public void EndKnockdown()
        {
            forceOverrideActive = false;
            if (playerState != null && playerState.IsAlive)
                PlayLocomotion(idle, true);
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
            // Dying mid-air (e.g. thrown by the Brute) must not land into idle over the death pose.
            if (!playerState.IsAlive)
                return;

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
            if (!playerState.IsAlive)
                return;

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

        private void PlayWallAttachment()
        {
            if (IsDead) return;
            if (!HasAnimation(wallSlide)) return;
            var entry = skeletonAnimation.AnimationState.GetCurrent(locomotionTrack);
            if (entry?.Animation?.Name != wallSlide)
            {
                entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, wallSlide, wallSlideLoop);
                entry.MixDuration = locomotionBlend;
            }
            // Non-looping Spine entries keep their final pose. Reuse this entry
            // throughout the hold (including re-aiming), without restarting it.
            entry.Loop = wallSlideLoop;
            entry.TimeScale = wallSlideTimeScale;
            currentLocomotionAnim = wallSlide;
        }

        private void OnWallAttachedChanged(bool attached)
        {
            if (!playerState.IsAlive || playerState.IsStunned) return;
            if (playerState.IsWallAttached || playerState.IsWallSliding)
            {
                PlayWallAttachment();
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
            // Airborne is the real precondition. The previous guard checked
            // IsJumping/IsFalling, which could both be momentarily false during a
            // falling double jump (state-set ordering), causing the animation to be
            // dropped intermittently. Grounded is the only state that should skip it.
            if (playerState.IsGrounded || IsDead)
            {
                ClearDoubleJumpFlag(); // not airborne - don't leave flag set without playing
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
            if (IsDead) return;
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

        /// <summary>
        /// Re-primes the presentation layer when the player respawns. The controller
        /// listens for OnDeath but nothing previously told it the player revived, so
        /// transient flags and the frozen death animation stayed stuck - which
        /// swallowed subsequent double-jump requests. This clears that state.
        /// </summary>
        private void OnRevived()
        {
            // Clear transient presentation flags
            attackActive = false;
            currentAttackEntry = null;
            forceOverrideActive = false;
            waitingForParryEnd = false;
            wasAirborne = false;

            if (skeletonAnimation != null)
            {
                // Drop any overlay attack and unfreeze the locomotion track
                // (OnJumpAirComplete may have set TimeScale = 0 to hold a frame).
                skeletonAnimation.AnimationState.ClearTrack(overlayTrack);

                var current = skeletonAnimation.AnimationState.GetCurrent(locomotionTrack);
                if (current != null)
                    current.TimeScale = 1f;
            }

            // Restore a clean idle baseline so locomotion fallbacks take over normally.
            currentLocomotionAnim = "";
            PlayLocomotion(idle, true);
        }

        #endregion

        #region Locomotion

        private void PlayLocomotion(string animName, bool loop)
        {
            if (IsDead && animName != death) return;
            if ((playerState.IsWallAttached || playerState.IsWallSliding) && playerState.IsAlive && !playerState.IsStunned)
            {
                PlayWallAttachment();
                return;
            }
            if (!HasAnimation(animName) || GetCurrentLocomotionName() == animName)
                return;

            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, animName, GetLoopFor(animName, loop));
            entry.MixDuration = locomotionBlend;
            entry.TimeScale = GetTimeScaleFor(animName, 1f);
            currentLocomotionAnim = animName;
        }

        private void PlayJumpStart()
        {
            if (IsDead) return;
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
            if (IsDead) return;
            if ((playerState.IsWallAttached || playerState.IsWallSliding) && playerState.IsAlive && !playerState.IsStunned)
            {
                PlayWallAttachment();
                return;
            }
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
            if (IsDead) return;
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

        private void ConfigureMixes(Spine.AnimationStateData data)
        {
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

            var skeletonData = data.SkeletonData;
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
