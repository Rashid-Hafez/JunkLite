using System;
using System.Collections.Generic;
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
        [Tooltip("Optional. Shown while walking toward the camera (free-roam depth movement). Leave empty to keep the side view.")]
        [SerializeField] private SkeletonAnimation frontSkeleton;
        [Tooltip("Optional. Shown while walking away from the camera (free-roam depth movement). Leave empty to keep the side view.")]
        [SerializeField] private SkeletonAnimation backSkeleton;
        [Tooltip("Followers moved onto the shown skeleton (weapon socket, muzzle). Both skeletons need each follower's bone.")]
        [SerializeField] private BoneFollower[] boneFollowers;
        [Tooltip("Slots cleared on every skeleton after animations apply (e.g. Demo_Sword, the art's baked-in sword). Rigs without the slot ignore it.")]
        [SerializeField] private string[] hiddenSlots;

        [Tooltip("Local forward offset of the Spine artwork during a wall slide. Aligns the hand pose without moving the physics capsule.")]
        [SerializeField] private float wallPoseForwardOffset;
        private Vector3 appliedWallPoseOffset;

        [Header("Tracks")]
        [SerializeField] private int locomotionTrack = 0;
        [SerializeField] private int overlayTrack = 1;

        [Header("Locomotion Animations")]
        [SerializeField] private string idle = "idle";
        [SerializeField] private string run = "run";
        [Tooltip("One-shot played when she stops running, or turns around mid-run, then Idle or Run. Leave empty to go straight to Idle. A rig without the clip skips it.")]
        [SerializeField] private string runStop = "";
        [SerializeField] private string jumpStart = "Jump_1_Start";
        [SerializeField] private string jumpAir = "Jump_2_Air";
        [SerializeField] private string landing = "Jump_3_Land";
        [SerializeField] private string doubleJump = "doubleJump";
        [SerializeField, InspectorName("Wall Slide")] private string wallSlide = "wallSlide";
        [SerializeField] private string dash = "dash";
        [Tooltip("Played on every applied hit, and held for as long as a stun lasts.")]
        [SerializeField] private string stun = "stun";
        [SerializeField] private string death = "death";
        [Tooltip("Knocked down (grab throw, big hit). A rig without it uses the Death clip.")]
        [SerializeField] private string knockdown = "Death_New";
        [Tooltip("Looped while a grabber holds her. A rig without the clip keeps the stun pose.")]
        [SerializeField] private string grabbed = "Grabbed";
        [Tooltip("Played once when an interaction happens, standing on the ground. Moving cancels it into Run.")]
        [SerializeField] private string interact = "Interact";

        [Header("Animation Looping")]
        [SerializeField] private bool idleLoop = true;
        [SerializeField] private bool runLoop = true;
        [SerializeField] private bool jumpStartLoop = false;
        [SerializeField] private bool jumpAirLoop = false;
        [SerializeField] private bool landingLoop = false;
        [SerializeField] private bool doubleJumpLoop = false;
        [SerializeField, InspectorName("Wall Slide Loop")] private bool wallSlideLoop = false;
        [SerializeField] private bool dashLoop = false;
        [SerializeField] private bool stunLoop = false;
        [SerializeField] private bool deathLoop = false;

        [Header("Animation Timing (TimeScale)")]
        [SerializeField, Range(0.1f, 3f)] private float idleTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float runTimeScale = 1f;
        [Tooltip("Extra run clip speed while the front/back rig is shown (running toward or away from the camera). Side runs ignore it.")]
        [SerializeField, Range(0.1f, 3f)] private float depthRunTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float runStopTimeScale = 1.6f;
        [Tooltip("Crossfade seconds from Run Stop into Idle.")]
        [SerializeField, Min(0f)] private float runStopToIdleBlend = 0.3f;
        [SerializeField, Range(0f, 1f), Tooltip("How far through Run Stop (0..1) the fade into Idle starts.")]
        private float runStopFadeStart = 0.6f;
        [SerializeField, Range(0.1f, 3f), Tooltip("Run Stop speed during a reversal slide.")]
        private float turnSlideClipTimeScale = 1.2f;

        [SerializeField, Tooltip("Upward speed at or below which Jump Start hands over to Jump Air: the apex of the jump.")]
        private float jumpAirApexSpeed = 1.05f;
        [Tooltip("Turning around mid-run: the share of the Run Stop clip shown before Run takes over.")]
        [SerializeField, Range(0f, 1f)] private float turnStopPortion = 0.35f;
        [Tooltip("Blend seconds from Run Stop back into Run (after a turn, or pressing a direction during the stop).")]
        [SerializeField, Min(0f)] private float turnStopBlend = 0.1f;
        [Tooltip("Run must have played this long before a turn or a stop shows the Run Stop clip. Keeps tapping and direction spam from stuttering.")]
        [SerializeField, Min(0f)] private float turnStopMinRunTime = 0.5f;
        [Tooltip("Seconds after one turn clip before another can play. Inside it she just keeps running.")]
        [SerializeField, Min(0f)] private float turnStopCooldown = 0.35f;
        [SerializeField, Range(0.1f, 3f)] private float jumpStartTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float jumpAirTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float landingTimeScale = 1f;
        [Tooltip("Landing while moving: the share of the Land clip shown before Run takes over.")]
        [SerializeField, Range(0f, 1f)] private float landIntoRunPortion = 0.35f;
        [Tooltip("Blend seconds from Land into Run (landing while moving, or starting to move mid-land).")]
        [SerializeField, Min(0f)] private float landToRunBlend = 0.12f;
        [SerializeField, Range(0.1f, 3f)] private float doubleJumpTimeScale = 1f;
        [SerializeField, InspectorName("Wall Slide Time Scale"), Range(0.1f, 3f)] private float wallSlideTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float dashTimeScale = 1f;
        [Tooltip("Blend seconds out of Dash when the dash ends. The clip is longer than the dash, so it never reaches its end pose.")]
        [SerializeField, Min(0f)] private float dashExitBlend = 0.1f;
        [SerializeField, Range(0.1f, 3f)] private float stunTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float deathTimeScale = 1f;

        [Header("Locomotion Settings")]
        [SerializeField] private float speedThreshold = 0.1f;
        [SerializeField, Range(0f, 1f), Tooltip("Front/back rig stays while sideways speed is under this fraction of depth speed. Analog sticks are never perfectly straight.")]
        private float depthRigMaxLateralRatio = 0.5f;
        [SerializeField, Tooltip("Seconds speed must stay under the threshold before Run switches to Idle. Hides brief stops (door blockers, bumps).")]
        private float idleDelay = 0.1f;
        [SerializeField, Tooltip("Mix seconds between movement clips (idle, run, jump, wall, land). 0 = instant switch, no blending.")] private float locomotionBlend = 0f;
        [SerializeField] private float minRunTimeScale = 0.8f;
        [SerializeField] private float maxRunTimeScale = 1.3f;
        [SerializeField] private float doubleJumpBlend = 0f;

        [Header("Attack Animation Settings")]
        [SerializeField] private bool attackOverwrite = false;
        [SerializeField] private float attackMix = 0.1f;
        [Tooltip("Blend seconds from one attack into the next (a chained combo). Starting from idle/run uses Attack Mix.")]
        [SerializeField, Min(0f)] private float attackToAttackBlend = 0.067f;
        [Tooltip("Seconds to blend from a finished attack back to idle/run.")]
        [SerializeField] private float attackMixOut = 0.25f;
        [SerializeField] private float attackMixOutDelay = 0.05f;
        [Tooltip("Attack clip speed while the arm still carries the default weapon (fists).")]
        [SerializeField, Range(0.1f, 3f)] private float fistAttackTimeScale = 1f;
        [Tooltip("Attack clip speed once an arm weapon (blades) has been acquired.")]
        [SerializeField, Range(0.1f, 3f)] private float weaponAttackTimeScale = 1f;
        [SerializeField, Range(0.1f, 3f)] private float downAttackTimeScale = 1.4f;
        [Tooltip("Seconds an attack clip pauses at its 'Hold' Spine event. The hit window waits for it. 0 = no pause.")]
        [SerializeField, Min(0f)] private float attackHoldDuration = 0.12f;
        [Tooltip("Attack clip speed multiplier from its 'Release' Spine event to the end of the clip.")]
        [SerializeField, Range(0.1f, 3f)] private float attackReleaseSpeed = 1.35f;

        [Header("Force Override (e.g. GroundPound)")]
        [Tooltip("When grounded, wait this long after the override animation ends before returning to idle/run")]
        [SerializeField] private float forceOverrideGroundedHoldDuration = 0.4f;

        [Header("Weapon Acquired")]
        [Tooltip("Spine animation played when Cassidy acquires a new arm weapon. Leave empty to skip.")]
        [SerializeField] private string modCombatEntryAnim = "Attack_WolverinePose";
        [Tooltip("VFX prefab spawned at the player when a weapon is acquired (same ripple as parry).")]
        [SerializeField] private GameObject modEntryVFXPrefab;
        [Tooltip("Fallback animation if the configured acquisition animation is missing on the skeleton.")]
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
        private ParryHandler parryHandler;
        private GameInputManager interactInput;

        // Spine event names on attack clips.
        private const string AttackHoldEvent = "Hold";
        private const string AttackReleaseEvent = "Release";

        // State tracking
        private bool wasAirborne = false;
        private string currentLocomotionAnim = "";
        private float stoppedTime;
        private bool attackActive = false;
        // Lunge/recoil velocity after a swing is not running: hold the stance until it fades or the player steers.
        private bool settlingFromAttack;
        private TrackEntry currentAttackEntry = null;
        private bool forceOverrideActive = false;
        private bool grabbedPoseActive;
        private float attackBaseTimeScale = 1f;
        private float attackHoldRemaining;
        // A finished swing still mixing out counts as chaining into the next one.
        private float attackChainUntil = float.NegativeInfinity;
        private bool lastFacingRight;
        private float runTime;
        private float lastTurnClipTime = float.NegativeInfinity;
        private bool stoppingDepthRun;

        // when an attack animation finishes during a parry, we defer the end until parry state clears
        private bool waitingForParryEnd = false;
        private bool parryClipShowing;

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
            parryHandler = GetComponentInParent<ParryHandler>();

            if (HasSidedSkeletons)
            {
                bool facingRight = controller == null || controller.IsFacingRight;
                skeletonAnimation = facingRight ? rightSkeleton : leftSkeleton;
                InitializeSkeleton(rightSkeleton);
                InitializeSkeleton(leftSkeleton);
                rightSkeleton.gameObject.SetActive(facingRight);
                leftSkeleton.gameObject.SetActive(!facingRight);
                if (frontSkeleton != null) { InitializeSkeleton(frontSkeleton); frontSkeleton.gameObject.SetActive(false); }
                if (backSkeleton != null) { InitializeSkeleton(backSkeleton); backSkeleton.gameObject.SetActive(false); }
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
            HideSlots(target);
        }

        // Animations key attachments, so clear the slot after they apply and before the mesh is built.
        private void HideSlots(SkeletonAnimation target)
        {
            if (hiddenSlots == null || hiddenSlots.Length == 0) return;

            var slots = new List<Spine.Slot>(hiddenSlots.Length);
            foreach (string slotName in hiddenSlots)
            {
                Spine.Slot slot = target.Skeleton.FindSlot(slotName);
                if (slot != null) slots.Add(slot);
            }
            if (slots.Count == 0) return;

            target.UpdateComplete += _ =>
            {
                for (int i = 0; i < slots.Count; i++) slots[i].Attachment = null;
            };
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
            playerState.OnWallSlidingChanged += OnWallSlideChanged;
            playerState.OnLedgeDetectedChanged += OnLedgeDetectedChanged;
            playerState.OnParryChanged += OnParryChanged;
            playerState.OnDoubleJumpChanged += OnDoubleJumpChanged;
            playerState.OnStunnedChanged += OnStunnedChanged;
            playerState.OnDeath += OnDeath;
            playerState.OnAttackingChanged += OnAttackingChanged;

            playerState.OnAttackAnimationRequested += OnAttackAnimationRequested; // new

            if (playerCharacter != null)
            {
                playerCharacter.OnRevived += OnRevived;
                playerCharacter.OnHurt += HandleHurt;
            }

            interactInput = GameInputManager.Instance;
            if (interactInput != null)
                interactInput.OnInteractionPerformed += PlayInteract;

            lastFacingRight = controller != null && controller.IsFacingRight;

            // Only the shown skeleton updates, so subscribing both never doubles an event.
            if (HasSidedSkeletons)
            {
                rightSkeleton.AnimationState.Event += HandleSpineEvent;
                leftSkeleton.AnimationState.Event += HandleSpineEvent;
                if (frontSkeleton != null) frontSkeleton.AnimationState.Event += HandleSpineEvent;
                if (backSkeleton != null) backSkeleton.AnimationState.Event += HandleSpineEvent;
            }
            else if (skeletonAnimation != null)
            {
                skeletonAnimation.AnimationState.Event += HandleSpineEvent;
            }

            if (controller != null)
                controller.OnDoubleJumpPerformed += OnControllerDoubleJumpPerformed;

            if (weaponManager != null && weaponManager.Armament != null)
                weaponManager.Armament.WeaponAcquired += HandleWeaponAcquired;

            PlayLocomotion(idle, true);
        }

        private void Update()
        {
            if (skeletonAnimation == null || controller == null || playerState == null)
                return;

            UpdateAttackHold();

            bool facingRight = controller.IsFacingRight;
            bool flipped = facingRight != lastFacingRight;
            lastFacingRight = facingRight;

            if (UpdateGrabbedPose())
                return;

            SyncSkeletonToFacing();

            if (playerState.IsWallSliding && playerState.IsAlive && !playerState.IsStunned)
            {
                PlayWallSlide();
                return;
            }

            SyncCurrentLocomotion();
            ApplyAnyStateFallbacks();
            UpdateLocomotionFromSpeed(flipped);
            UpdateRunSpeed();
        }

        private void LateUpdate()
        {
            if (skeletonAnimation == null || skeletonAnimation.transform == transform) return;
            bool wallPose = playerState != null && playerState.IsAlive && !playerState.IsStunned &&
                           playerState.IsWallSliding;
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
                playerState.OnWallSlidingChanged -= OnWallSlideChanged;
                playerState.OnLedgeDetectedChanged -= OnLedgeDetectedChanged;
                playerState.OnParryChanged -= OnParryChanged;
                playerState.OnDoubleJumpChanged -= OnDoubleJumpChanged;
                playerState.OnStunnedChanged -= OnStunnedChanged;
                playerState.OnDeath -= OnDeath;
                playerState.OnAttackingChanged -= OnAttackingChanged;
                playerState.OnAttackAnimationRequested -= OnAttackAnimationRequested;
            }

            if (playerCharacter != null)
            {
                playerCharacter.OnRevived -= OnRevived;
                playerCharacter.OnHurt -= HandleHurt;
            }

            if (interactInput != null)
                interactInput.OnInteractionPerformed -= PlayInteract;

            if (controller != null)
                controller.OnDoubleJumpPerformed -= OnControllerDoubleJumpPerformed;

            if (weaponManager != null && weaponManager.Armament != null)
                weaponManager.Armament.WeaponAcquired -= HandleWeaponAcquired;

            if (HasSidedSkeletons)
            {
                if (rightSkeleton.AnimationState != null) rightSkeleton.AnimationState.Event -= HandleSpineEvent;
                if (leftSkeleton.AnimationState != null) leftSkeleton.AnimationState.Event -= HandleSpineEvent;
                if (frontSkeleton != null && frontSkeleton.AnimationState != null) frontSkeleton.AnimationState.Event -= HandleSpineEvent;
                if (backSkeleton != null && backSkeleton.AnimationState != null) backSkeleton.AnimationState.Event -= HandleSpineEvent;
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

            SkeletonAnimation shown = PickSkeleton();
            if (shown != skeletonAnimation)
                SwapSkeleton(shown);
        }

        // Side view by default. Walking straight in depth on the ground shows the front/back rig:
        // forward (away from the camera) is the back view, backward is the front view.
        private SkeletonAnimation PickSkeleton()
        {
            SkeletonAnimation side = controller.IsFacingRight ? rightSkeleton : leftSkeleton;
            // Land counts as locomotion here so a landing in depth already shows the front/back rig.
            // Run Stop stays on the side rigs: the older front/back clip sets don't have it.
            string current = GetCurrentLocomotionName();
            if ((frontSkeleton == null && backSkeleton == null) || !controller.IsGrounded ||
                attackActive || (IsPlayingTransientAnim() && current != landing) || IsRunStop(current) || IsDead)
                return side;

            // Clear diagonals use the side view: the front/back run cycle read as bent legs there.
            // Small sideways drift (analog stick) still counts as straight depth.
            Vector3 v = controller.Velocity;
            float depth = Vector3.Dot(v, controller.transform.forward);
            float lateral = Mathf.Abs(Vector3.Dot(v, controller.transform.right));
            if (Mathf.Abs(depth) <= speedThreshold ||
                (lateral > speedThreshold && lateral > Mathf.Abs(depth) * depthRigMaxLateralRatio)) return side;

            SkeletonAnimation depthRig = depth > 0f ? backSkeleton : frontSkeleton;
            return depthRig != null ? depthRig : side;
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
            // Fresh tracks have no "from" entry to reset keys the old clip set (bones, mesh deform),
            // so a rig shown again kept its last clip's bent feet. Start it from the setup pose.
            next.Skeleton.SetToSetupPose();

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
                // Events up to here already fired on the old rig; firing them again would pause a
                // swing twice at its Hold. TrackTime 0 = never applied, so a Hold at 0 is still to come.
                if (source.TrackTime > 0f)
                    copy.AnimationLast = source.AnimationTime;
            }
            else
            {
                copy.MixDuration = source.MixDuration;
                copy.Delay = source.Delay;
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

            ShowSideRigIfMissing(animationName);
            if (!HasAnimation(animationName))
            {
                NotifyPlayerAttackComplete();
                return;
            }

            bool chained = attackActive || Time.time <= attackChainUntil;
            if (attackActive)
            {
                // The weapon manager only requests a new attack after it has finished
                // or cancelled the previous one, so the old clip is just still playing.
                // Drop it silently: reporting it complete here would complete the NEW
                // attack (advance the combo and cancel its hit).
                ClearCurrentAttack();
                waitingForParryEnd = false;
            }

            // Parry clips (start and hit) play alone on the base track: no overlay on top,
            // no blend in or out, no wind-up. Partial keys must not mix with another pose.
            if (IsParryClip(animationName))
            {
                attackActive = true;
                var parryState = skeletonAnimation.AnimationState;
                parryState.ClearTrack(overlayTrack);
                skeletonAnimation.Skeleton.SetBonesToSetupPose();
                var parryEntry = parryState.SetAnimation(locomotionTrack, animationName, false);
                parryEntry.MixDuration = 0f;
                parryEntry.MixBlend = MixBlend.Replace;
                parryEntry.TimeScale = GetTimeScaleFor(animationName, 1f);
                HookAttackEntry(parryEntry, false);
                attackBaseTimeScale = parryEntry.TimeScale;
                currentAttackEntry = parryEntry;
                attackHoldRemaining = 0f;
                timedHoldAt = -1f;
                releaseFastAfterHold = false;
                parryClipShowing = true;
                return;
            }

            attackActive = true;

            float timeScale = ResolveAttackTimeScale();
            int track = attackOverwrite ? locomotionTrack : overlayTrack;
            var entry = skeletonAnimation.AnimationState.SetAnimation(track, animationName, false);
            entry.MixDuration = chained ? attackToAttackBlend : attackMix;
            entry.MixBlend = MixBlend.Replace;
            entry.TimeScale = timeScale;
            HookAttackEntry(entry, !attackOverwrite);
            attackBaseTimeScale = timeScale;
            currentAttackEntry = entry;
            releaseFastAfterHold = false;
            timedHoldAt = attackHoldAtOverride >= 0f && WindupAppliesTo(animationName) && !HasHoldEvents(animationName)
                ? attackHoldAtOverride : -1f;
        }

        private bool IsParryHit(string animationName) =>
            parryHandler != null && string.Equals(animationName, parryHandler.ParryHitClip, StringComparison.OrdinalIgnoreCase);

        private bool IsParryClip(string animationName) =>
            IsParryHit(animationName) ||
            (parryHandler != null && string.Equals(animationName, parryHandler.ParryStartClip, StringComparison.OrdinalIgnoreCase));

        // A parry clip just ended: the next clip cuts in with no blend.
        private void SnapAfterParryClip()
        {
            if (!parryClipShowing) return;
            parryClipShowing = false;
            TrackEntry next = skeletonAnimation != null ? skeletonAnimation.AnimationState.GetCurrent(locomotionTrack) : null;
            if (next != null) next.MixDuration = 0f;
        }

        private float ResolveAttackTimeScale()
        {
            bool isWeaponState = weaponManager != null && weaponManager.Armament != null &&
                                 weaponManager.Armament.HasUpgradedWeapon;
            float timeScale = isWeaponState ? weaponAttackTimeScale : fistAttackTimeScale;
            if (weaponManager != null) timeScale *= weaponManager.AttackSpeedMultiplier;
            if (playerState != null && playerState.IsDownAttackRequested)
                timeScale = downAttackTimeScale;
            return timeScale;
        }

        // The front/back rigs have an older clip set: a clip only the side rigs have shows the side view first.
        private void ShowSideRigIfMissing(string animationName)
        {
            if (!HasSidedSkeletons || HasAnimation(animationName)) return;

            SkeletonAnimation side = controller != null && !controller.IsFacingRight ? leftSkeleton : rightSkeleton;
            if (side != skeletonAnimation && side.Skeleton?.Data?.FindAnimation(animationName) != null)
                SwapSkeleton(side);
        }

        // Only the attack that is current may finish or interrupt it. A finished
        // clip stays on the base track when locomotion does not replace it (idle
        // before and after), so the next attack replacing it fires an Interrupt
        // for the OLD clip while the new one is already active; a clip still
        // mixing out can also fire a late Complete. Either used to end the new
        // swing and reset the combo to the first hit. A left/right skeleton swap
        // re-hooks the copied entry and makes it current, so the guard still holds.
        private void HookAttackEntry(TrackEntry entry, bool finishAsOverlay)
        {
            if (finishAsOverlay) entry.Complete += e => { if (e == currentAttackEntry) FinishAttackOverlay(); };
            else entry.Complete += e => { if (e == currentAttackEntry) FinishAttackOverwrite(); };
            entry.Interrupt += e => { if (e == currentAttackEntry) OnAttackInterrupted(); };
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

            ClearCurrentAttack();
            attackChainUntil = Time.time + attackMixOut + attackMixOutDelay;

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

            ClearCurrentAttack();
            attackChainUntil = Time.time + attackMixOut + attackMixOutDelay;

            // Return to appropriate locomotion
            if (playerState != null)
            {
                if (playerState.IsGrounded)
                {
                    settlingFromAttack = true;
                    PlayLocomotion(GetSpeed() > speedThreshold && IsSteering ? run : idle, true);
                    // Ease out of the finished swing instead of snapping to the stance.
                    TrackEntry back = skeletonAnimation.AnimationState.GetCurrent(locomotionTrack);
                    if (back != null && back.MixingFrom != null) back.MixDuration = attackMixOut;
                }
                else
                {
                    PlayJumpAir();
                }
                SnapAfterParryClip();
            }

            NotifyPlayerAttackComplete();
        }

        private void OnAttackInterrupted()
        {
            if (!attackActive) return;

            ClearCurrentAttack();

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.ClearTrack(overlayTrack);

            if (playerState != null)
                playerState.NotifyAttackAnimationInterrupted();
        }

        private void ForceFinishAttack()
        {
            ClearCurrentAttack();

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.ClearTrack(overlayTrack);

            NotifyPlayerAttackComplete();
        }

        private void NotifyPlayerAttackComplete()
        {
            if (playerState != null)
                playerState.NotifyAttackAnimationComplete();
        }

        // Every way an attack ends comes through here, so a paused Hold can't outlive its swing.
        private void ClearCurrentAttack()
        {
            if (attackHoldRemaining > 0f && currentAttackEntry != null)
                currentAttackEntry.TimeScale = attackBaseTimeScale;
            attackHoldRemaining = 0f;
            attackActive = false;
            currentAttackEntry = null;
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
        // The knockdown clip this rig has (Death_New), else the death clip.
        private string KnockdownClip => !string.IsNullOrEmpty(knockdown) && HasAnimation(knockdown) ? knockdown : death;

        public void PlayKnockdown()
        {
            string clip = KnockdownClip;
            if (skeletonAnimation == null || !HasAnimation(clip))
                return;

            InterruptAttack("knockdown");
            forceOverrideActive = true;
            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, clip, false);
            entry.MixDuration = 0f;
            entry.TimeScale = GetTimeScaleFor(clip, 1f);
            currentLocomotionAnim = clip;
        }

        /// <summary>Real seconds of the knockdown clip on the shown rig.</summary>
        public bool TryGetKnockdownDuration(out float duration)
        {
            duration = 0f;
            var clip = skeletonAnimation?.Skeleton?.Data?.FindAnimation(KnockdownClip);
            if (clip == null) return false;
            duration = clip.Duration / Mathf.Max(0.01f, GetTimeScaleFor(KnockdownClip, 1f));
            return duration > 0f;
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

        #region Weapon Acquired

        private void HandleWeaponAcquired(WeaponData weapon)
        {
            if (playerState != null && !playerState.IsAlive)
                return;

            GetComponentInParent<ScreenGlitchEffect>()?.Play();

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

            ClearCurrentAttack();

            if (skeletonAnimation != null)
                skeletonAnimation.AnimationState.ClearTrack(overlayTrack);

            if (playerState != null)
                playerState.NotifyAttackAnimationInterrupted();
        }

        #endregion

        #region Attack Hold / Release

        private float attackHoldOverride;
        private float attackHoldAtOverride = -1f;
        // Clip seconds of a timed wind-up pause on the current swing (clips with no Hold event); negative = none.
        private float timedHoldAt = -1f;
        private bool releaseFastAfterHold;
        private float BaseAttackHold => attackHoldOverride > 0f ? attackHoldOverride : attackHoldDuration;
        private float ScaledAttackHold => BaseAttackHold / (weaponManager != null ? weaponManager.AttackSpeedMultiplier : 1f);

        /// <summary>
        /// Wind-up for the swing about to start (a combo step's windupHold / windupAt). Hold 0 = attackHoldDuration
        /// at the clip's own Hold event. With a hold, a clip without Hold events pauses at <paramref name="holdAt"/>.
        /// </summary>
        public void SetAttackWindup(float holdSeconds, float holdAt, string clip = null)
        {
            attackHoldOverride = Mathf.Max(0f, holdSeconds);
            attackHoldAtOverride = attackHoldOverride > 0f ? Mathf.Max(0f, holdAt) : -1f;
            windupClip = clip;
        }

        // The wind-up belongs to this clip only; any other clip (push, parry) ignores it.
        private string windupClip;
        private bool WindupAppliesTo(string clip) =>
            string.IsNullOrEmpty(windupClip) || string.Equals(windupClip, clip, StringComparison.Ordinal);

        /// <summary>Real seconds the swing clip runs, with its wind-up pause and fast release. 0 when unknown.</summary>
        public float GetAttackRealDuration(string animationName)
        {
            SkeletonAnimation rig = HasSidedSkeletons ? rightSkeleton : skeletonAnimation;
            Spine.Animation clip = rig != null && !string.IsNullOrEmpty(animationName)
                ? rig.Skeleton?.Data?.FindAnimation(animationName)
                : null;
            float scale = ResolveAttackTimeScale();
            return clip != null && scale > 0f ? WarpAttackDelay(animationName, clip.Duration / scale) : 0f;
        }

        /// <summary>Real seconds from the clip start until the wind-up pause ends. 0 when the clip has none.</summary>
        public float GetWindupReleaseTime(string animationName)
        {
            if (!TryGetAttackHoldMarks(animationName, out float holdAt, out _) || holdAt < 0f) return 0f;
            float scale = ResolveAttackTimeScale();
            return (scale > 0f ? holdAt / scale : 0f) + ScaledAttackHold;
        }

        /// <summary>Seconds the clip pauses at its Hold event. 0 when it has none.</summary>
        public float GetAttackHoldDuration(string animationName) =>
            TryGetAttackHoldMarks(animationName, out float holdAt, out _) && holdAt >= 0f ? ScaledAttackHold : 0f;

        /// <summary>Real seconds until the swing reaches the moment <paramref name="delay"/> points at on the plain clip.</summary>
        public float WarpAttackDelay(string animationName, float delay)
        {
            if (!TryGetAttackHoldMarks(animationName, out float holdAt, out float releaseAt))
                return delay;

            return AttackHoldTiming.WarpDelay(delay, ResolveAttackTimeScale(), holdAt, releaseAt,
                ScaledAttackHold, attackReleaseSpeed);
        }

        // Clip seconds of the clip's Hold and Release events; negative when absent.
        private bool TryGetAttackHoldMarks(string animationName, out float holdAt, out float releaseAt)
        {
            holdAt = releaseAt = -1f;
            SkeletonAnimation rig = HasSidedSkeletons ? rightSkeleton : skeletonAnimation;
            Spine.Animation clip = rig != null && !string.IsNullOrEmpty(animationName)
                ? rig.Skeleton?.Data?.FindAnimation(animationName)
                : null;
            if (clip == null) return false;

            Timeline[] timelines = clip.Timelines.Items;
            for (int i = 0; i < clip.Timelines.Count; i++)
            {
                if (timelines[i] is not EventTimeline eventTimeline) continue;

                Spine.Event[] events = eventTimeline.Events;
                for (int e = 0; e < events.Length; e++)
                {
                    string eventName = events[e].Data.Name;
                    if (eventName == AttackHoldEvent) holdAt = events[e].Time;
                    else if (eventName == AttackReleaseEvent) releaseAt = events[e].Time;
                }
                break;
            }

            // No Hold/Release events: a timed wind-up pauses there, then releases fast.
            if (holdAt < 0f && releaseAt < 0f && attackHoldAtOverride >= 0f && WindupAppliesTo(animationName))
                holdAt = releaseAt = Mathf.Min(attackHoldAtOverride, clip.Duration);

            return holdAt >= 0f || releaseAt >= 0f;
        }

        private bool HasHoldEvents(string animationName)
        {
            float saved = attackHoldAtOverride;
            attackHoldAtOverride = -1f;
            bool has = TryGetAttackHoldMarks(animationName, out _, out _);
            attackHoldAtOverride = saved;
            return has;
        }

        private void BeginAttackHold(TrackEntry entry)
        {
            if (entry != currentAttackEntry || BaseAttackHold <= 0f) return;

            entry.TimeScale = 0f;
            attackHoldRemaining = ScaledAttackHold;
        }

        private void ReleaseAttackHold(TrackEntry entry)
        {
            if (entry != currentAttackEntry) return;

            attackHoldRemaining = 0f;
            entry.TimeScale = attackBaseTimeScale * attackReleaseSpeed;
        }

        // Gameplay delta: hit-stop and slow motion stretch the pause exactly as they stretch the clip.
        // A skeleton swap re-points currentAttackEntry and copies the paused time scale.
        private void UpdateAttackHold()
        {
            if (timedHoldAt >= 0f && currentAttackEntry != null && currentAttackEntry.TrackTime >= timedHoldAt)
            {
                timedHoldAt = -1f;
                releaseFastAfterHold = true;
                BeginAttackHold(currentAttackEntry);
            }

            if (attackHoldRemaining <= 0f) return;

            attackHoldRemaining -= Time.deltaTime;
            if (attackHoldRemaining <= 0f && currentAttackEntry != null)
                currentAttackEntry.TimeScale = attackBaseTimeScale * (releaseFastAfterHold ? attackReleaseSpeed : 1f);
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

            // A dash keeps its clip through touching the ground; the dash's end picks the pose.
            if (playerState.IsDashing)
            {
                wasAirborne = !grounded;
                return;
            }

            if (grounded)
            {
                ClearDoubleJumpFlag();

                // A hit that lifts her off the floor keeps its Hurt clip through the touchdown.
                TrackEntry hurtEntry = skeletonAnimation.AnimationState.GetCurrent(locomotionTrack);
                if (hurtEntry != null && hurtEntry.Animation.Name == stun && !hurtEntry.IsComplete)
                {
                    wasAirborne = false;
                    return;
                }

                float speed = GetSpeed();
                if (speed > speedThreshold)
                {
                    // Moving - show the start of the landing, blended into the run
                    if (wasAirborne) PlayLanding(HasMoveInput);
                    else PlayLocomotion(run, true);
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
                // A stun ended the dash: Hurt already owns the pose.
                if (playerState.IsStunned)
                    return;

                if (playerState.IsGrounded)
                {
                    float speed = GetSpeed();
                    PlayLocomotion(speed > speedThreshold ? run : idle, true);
                }
                else
                {
                    PlayJumpAir();
                }

                // Ease out of the held dash pose instead of snapping to the stance.
                TrackEntry back = skeletonAnimation.AnimationState.GetCurrent(locomotionTrack);
                if (back != null && back.MixingFrom != null) back.MixDuration = dashExitBlend;
            }
        }

        private void PlayWallSlide()
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
            // throughout the slide without restarting it.
            entry.Loop = wallSlideLoop;
            entry.TimeScale = wallSlideTimeScale;
            currentLocomotionAnim = wallSlide;
        }

        private void OnWallSlideChanged(bool sliding)
        {
            if (!playerState.IsAlive || playerState.IsStunned) return;
            if (sliding)
            {
                PlayWallSlide();
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
                    SnapAfterParryClip();
                }
                else
                {
                    PlayJumpAir();
                    SnapAfterParryClip();
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

            // The grabbed loop owns the pose; ending the hold restores it from the stun state then.
            if (grabbedPoseActive) return;

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
            ClearCurrentAttack();
            forceOverrideActive = false;
            grabbedPoseActive = false;
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

        /// <summary>
        /// Applied damage (PlayerCharacter.OnHurt), stunning or not. The stun that may follow finds this
        /// clip already current and leaves it alone, so one hit never restarts it twice. Whatever owns the
        /// locomotion track keeps it: replacing an attack clip there would interrupt the swing's gameplay,
        /// so Hurt shows on the overlay instead and clears itself, while the owner's clip runs on underneath.
        /// </summary>
        private void HandleHurt()
        {
            if (IsDead || skeletonAnimation == null || GetCurrentLocomotionName() == death || !HasAnimation(stun))
                return;

            var state = skeletonAnimation.AnimationState;
            if (attackActive || forceOverrideActive || playerState.IsDashing || playerState.IsWallSliding)
            {
                if (currentAttackEntry != null && currentAttackEntry.TrackIndex == overlayTrack)
                    return;

                var overlay = state.SetAnimation(overlayTrack, stun, false);
                overlay.MixDuration = 0f;
                overlay.TimeScale = GetTimeScaleFor(stun, 1f);
                state.AddEmptyAnimation(overlayTrack, attackMixOut, 0f);
                return;
            }

            var entry = state.SetAnimation(locomotionTrack, stun, GetLoopFor(stun, false));
            entry.MixDuration = locomotionBlend;
            entry.TimeScale = GetTimeScaleFor(stun, 1f);
            currentLocomotionAnim = stun;
        }

        /// <summary>
        /// True while the grabbed loop owns the pose. Polled, so every way out of a hold (throw, escape,
        /// the grabber being stunned or killed, her death) restores her without its own hook.
        /// </summary>
        private bool UpdateGrabbedPose()
        {
            bool held = playerState.IsAlive && playerCharacter != null && playerCharacter.IsHeldByGrabber;
            if (held)
            {
                ShowSideRigIfMissing(grabbed);
                held = HasAnimation(grabbed);
            }

            if (!held)
            {
                if (grabbedPoseActive) EndGrabbedPose();
                return false;
            }

            if (!grabbedPoseActive)
            {
                grabbedPoseActive = true;
                InterruptAttack("grabbed");
            }

            // Blocks the landing, facing and locomotion updates while she is carried.
            forceOverrideActive = true;
            if (GetCurrentLocomotionName() != grabbed)
            {
                var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, grabbed, true);
                entry.MixDuration = locomotionBlend;
                currentLocomotionAnim = grabbed;
            }
            return true;
        }

        private void EndGrabbedPose()
        {
            grabbedPoseActive = false;
            forceOverrideActive = false;
            if (IsDead) return;

            if (playerState.IsStunned) PlayLocomotion(stun, true);
            else ReturnToLocomotion();
        }

        private void PlayInteract()
        {
            if (IsDead || skeletonAnimation == null || playerState == null || attackActive || forceOverrideActive ||
                !playerState.IsGrounded || playerState.IsAttacking || playerState.IsStunned || playerState.IsDashing ||
                GetSpeed() > speedThreshold)
                return;

            ShowSideRigIfMissing(interact);
            if (!HasAnimation(interact)) return;

            var state = skeletonAnimation.AnimationState;
            var entry = state.SetAnimation(locomotionTrack, interact, false);
            entry.MixDuration = locomotionBlend;
            currentLocomotionAnim = interact;

            var idleEntry = state.AddAnimation(locomotionTrack, idle, GetLoopFor(idle, true), 0f);
            idleEntry.TimeScale = GetTimeScaleFor(idle, 1f);
        }

        #endregion

        #region Locomotion

        private void PlayLocomotion(string animName, bool loop)
        {
            if (IsDead && animName != death) return;
            if (playerState.IsWallSliding && playerState.IsAlive && !playerState.IsStunned)
            {
                PlayWallSlide();
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
            // Holds its last frame through the rise; ApplyAnyStateFallbacks plays Jump Air at the apex.
        }

        private void PlayJumpAir()
        {
            if (IsDead) return;
            if (playerState.IsWallSliding && playerState.IsAlive && !playerState.IsStunned)
            {
                PlayWallSlide();
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

        private void PlayLanding(bool intoRun = false)
        {
            if (IsDead) return;
            if (!HasAnimation(landing))
            {
                PlayLocomotion(intoRun ? run : idle, true);
                return;
            }

            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, landing, GetLoopFor(landing, false));
            entry.MixDuration = locomotionBlend;
            entry.TimeScale = GetTimeScaleFor(landing, 1f);
            currentLocomotionAnim = landing;

            if (intoRun && HasAnimation(run))
            {
                // Delay is in the Land clip's own time: Run takes over part-way through it.
                QueueRunAfterLanding(entry);
            }
            else
            {
                var idleEntry = skeletonAnimation.AnimationState.AddAnimation(locomotionTrack, idle, GetLoopFor(idle, true), 0f);
                idleEntry.TimeScale = GetTimeScaleFor(idle, 1f);
            }

            // Land on the rig for the direction being run in (side, or front/back in depth).
            SyncSkeletonToFacing();
        }

        private void QueueRunAfterLanding(TrackEntry landEntry)
        {
            var runEntry = skeletonAnimation.AnimationState.AddAnimation(locomotionTrack, run, GetLoopFor(run, true),
                Mathf.Max(0.01f, landEntry.Animation.Duration * landIntoRunPortion));
            runEntry.MixDuration = landToRunBlend;
            runEntry.TimeScale = GetTimeScaleFor(run, 1f);
        }

        // While Land plays: moving blends into Run now; stopping before a queued Run swaps it back to Idle.
        private void UpdateLandingExit(float speed)
        {
            var state = skeletonAnimation.AnimationState;
            TrackEntry current = state.GetCurrent(locomotionTrack);
            if (current == null) return;

            bool runQueued = current.Next != null && current.Next.Animation != null && current.Next.Animation.Name == run;
            bool running = speed > speedThreshold && HasMoveInput;
            if (running && !runQueued && HasAnimation(run))
            {
                var runEntry = state.SetAnimation(locomotionTrack, run, GetLoopFor(run, true));
                runEntry.MixDuration = landToRunBlend;
                runEntry.TimeScale = GetTimeScaleFor(run, 1f);
                currentLocomotionAnim = run;
            }
            else if (!running && runQueued)
            {
                state.ClearNext(current);
                var idleEntry = state.AddAnimation(locomotionTrack, idle, GetLoopFor(idle, true), 0f);
                idleEntry.TimeScale = GetTimeScaleFor(idle, 1f);
            }
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

            // Hurt from a hit that didn't stun is a one-shot (a stun returned above and holds it).
            if (current == stun)
            {
                TrackEntry hurtEntry = skeletonAnimation.AnimationState.GetCurrent(locomotionTrack);
                if (hurtEntry != null && hurtEntry.IsComplete) ReturnToLocomotion();
                return;
            }

            if (playerState.IsDoubleJumping || current == doubleJump)
                return;

            // Jump Start owns the rise; Jump Air takes over at the apex.
            if (!playerState.IsGrounded && current == jumpStart && controller != null &&
                controller.Velocity.y <= jumpAirApexSpeed)
            {
                PlayJumpAir();
                return;
            }

            // If airborne and falling, force jump air (except during landing)
            if (!playerState.IsGrounded && playerState.IsFalling)
            {
                if (current != jumpAir && current != landing)
                {
                    PlayJumpAir();
                }
                return;
            }

            // Grounded but still in an air pose (e.g. the landing event was missed): fix it.
            // This used to sit behind IsPlayingTransientAnim(), which lists these same clips,
            // so it never ran and a held Jump Air / Wall Slide frame stuck after landing.
            // Jump Start is left alone: it plays on the ground for the take-off frame.
            if (playerState.IsGrounded && !playerState.IsJumping &&
                (current == jumpAir || current == doubleJump || current == wallSlide))
            {
                float speed = GetSpeed();
                PlayLocomotion(speed > speedThreshold ? run : idle, true);
            }
        }

        private void UpdateLocomotionFromSpeed(bool flipped)
        {
            float speed = GetSpeed();
            bool moving = speed > speedThreshold;
            stoppedTime = moving ? 0f : stoppedTime + Time.deltaTime;

            string current = GetCurrentLocomotionName();
            bool hasInput = HasMoveInput;
            // A front or back run has no stop clip: it goes straight to Idle, on whichever rig stops it.
            bool onDepthRig = skeletonAnimation == frontSkeleton || skeletonAnimation == backSkeleton;
            if (onDepthRig) stoppingDepthRun = true;
            else if (moving) stoppingDepthRun = false;
            if (current != run || onDepthRig) runTime = 0f;
            else if (hasInput) runTime += Time.deltaTime;

            // IsAttacking also covers the lead before the clip starts, when the lunge is already moving her.
            bool attacking = attackActive || forceOverrideActive || (weaponManager != null && weaponManager.IsAttacking);
            if (playerState.IsGrounded && !attacking && current == landing)
            {
                UpdateLandingExit(speed);
                return;
            }

            if (!playerState.IsGrounded || attacking)
                return;

            if (current == interact)
            {
                if (moving) BlendIntoRun();
                return;
            }

            if (IsPlayingTransientAnim())
                return;

            if (settlingFromAttack)
            {
                if (moving && !IsSteering) return;
                settlingFromAttack = false;
            }

            if (IsRunStop(current))
            {
                UpdateRunStopExit(moving, hasInput, flipped);
                return;
            }

            // Let go of a real run: the stop clip starts now, while speed is still sliding down.
            bool ranLongEnough = current == run && runTime >= turnStopMinRunTime && HasAnimation(runStop);
            if (ranLongEnough && !hasInput)
            {
                PlayRunStop();
                return;
            }

            // A long run's reversal slides first: the stop clip plays on the old facing, and the flip
            // that ends the slide blends it into Run (UpdateRunStopExit).
            if (controller.IsTurnSliding && current == run && HasAnimation(runStop))
            {
                StartRunStopClip(skeletonAnimation.AnimationState).TimeScale = turnSlideClipTimeScale;
                lastTurnClipTime = Time.time;
                return;
            }

            if (flipped && current == run && moving && hasInput && HasAnimation(runStop) &&
                ShouldPlayTurnClip(runTime, Time.time - lastTurnClipTime, turnStopMinRunTime, turnStopCooldown))
            {
                PlayTurnStop();
                return;
            }

            // A tap or a turn is not a run: only one unbroken hold can earn the stop clip.
            if (!hasInput || flipped) runTime = 0f;

            // Easing out of a run after letting go is not running; anything else moving her still shows Run.
            bool sliding = !hasInput && controller.IsGroundStopSliding;
            string targetAnim = moving && !sliding ? run : idle;

            // A stopped front or back run skips the side Run frames the idle delay would hold.
            if (stoppingDepthRun && !moving && current == run)
            {
                PlayLocomotion(idle, true);
                return;
            }

            // Keep running through brief stops so Run doesn't restart.
            if (targetAnim == idle && current == run && stoppedTime < idleDelay)
                return;

            if (targetAnim == idle && ranLongEnough)
            {
                PlayRunStop();
                return;
            }

            // Most of the visible stop is Idle crossfading out of the clip: moving there blends too.
            if (targetAnim == run && current == idle && IsRunStop(FadingFromName()))
                BlendIntoRun();
            else if (current != targetAnim)
                PlayLocomotion(targetAnim, true);
        }

        private string FadingFromName() =>
            skeletonAnimation.AnimationState.GetCurrent(locomotionTrack)?.MixingFrom?.Animation?.Name;

        /// <summary>
        /// Turn-clip guard against direction spam: she must have run long enough and the last turn clip
        /// must be old enough. Otherwise she keeps Run, which the skeleton swap carries over without a restart.
        /// </summary>
        public static bool ShouldPlayTurnClip(float runTime, float sinceLastTurnClip, float minRunTime, float cooldown) =>
            runTime >= minRunTime && sinceLastTurnClip >= cooldown;

        private void PlayRunStop()
        {
            var state = skeletonAnimation.AnimationState;
            StartRunStopClip(state);
            QueueIdleAfterRunStop(state);
        }

        // Turning mid-run: part of the stop clip, then back into Run. The delay is in the clip's own
        // time, and the queued Run is copied with its delay if the rig swaps during the clip.
        private void PlayTurnStop()
        {
            var state = skeletonAnimation.AnimationState;
            TrackEntry entry = StartRunStopClip(state);
            lastTurnClipTime = Time.time;

            var runEntry = state.AddAnimation(locomotionTrack, run, GetLoopFor(run, true), 0f);
            runEntry.SetMixDuration(turnStopBlend, Mathf.Max(0.01f, entry.Animation.Duration * turnStopPortion));
            runEntry.TimeScale = GetTimeScaleFor(run, 1f);
        }

        private TrackEntry StartRunStopClip(Spine.AnimationState state)
        {
            var entry = state.SetAnimation(locomotionTrack, runStop, false);
            entry.MixDuration = locomotionBlend;
            entry.TimeScale = GetTimeScaleFor(runStop, 1f);
            currentLocomotionAnim = runStop;
            return entry;
        }

        private void QueueIdleAfterRunStop(Spine.AnimationState state)
        {
            var idleEntry = state.AddAnimation(locomotionTrack, idle, GetLoopFor(idle, true), 0f);
            // The delay is in the clip's own time. Start the fade part-way in so the clip reads first.
            float clipLength = skeletonAnimation.Skeleton.Data.FindAnimation(runStop).Duration;
            idleEntry.SetMixDuration(runStopToIdleBlend, Mathf.Max(0.01f, clipLength * runStopFadeStart));
            idleEntry.TimeScale = GetTimeScaleFor(idle, 1f);
        }

        // While Run Stop plays. Letting go during a turn ends it in Idle instead of Run. Moving on purpose
        // cancels a stop at once, and flipping again cuts a turn short, so spam never restarts the clip.
        private void UpdateRunStopExit(bool moving, bool hasInput, bool flipped)
        {
            var state = skeletonAnimation.AnimationState;
            TrackEntry current = state.GetCurrent(locomotionTrack);
            if (current == null) return;

            bool runQueued = current.Next?.Animation?.Name == run;
            if (!hasInput)
            {
                if (runQueued)
                {
                    state.ClearNext(current);
                    QueueIdleAfterRunStop(state);
                }
                else if (current.Next == null)
                {
                    QueueIdleAfterRunStop(state);
                }
                return;
            }

            // Both keys held: the stop clip already started, so turn it into the slide's clip.
            if (controller.IsTurnSliding)
            {
                state.ClearNext(current);
                current.TimeScale = turnSlideClipTimeScale;
                return;
            }

            // Wait one physics step: this may be the start of a slide, not a return to Run.
            if (controller.IsInputAgainstLaneSpeed && !flipped)
                return;

            if (moving && (!runQueued || flipped))
                BlendIntoRun();
        }

        private void BlendIntoRun()
        {
            if (!HasAnimation(run)) return;

            var entry = skeletonAnimation.AnimationState.SetAnimation(locomotionTrack, run, GetLoopFor(run, true));
            entry.MixDuration = turnStopBlend;
            entry.TimeScale = GetTimeScaleFor(run, 1f);
            currentLocomotionAnim = run;
        }

        private void ReturnToLocomotion()
        {
            if (playerState.IsGrounded)
                PlayLocomotion(GetSpeed() > speedThreshold ? run : idle, true);
            else
                PlayJumpAir();
        }

        private bool IsRunStop(string animName) => !string.IsNullOrEmpty(runStop) && animName == runStop;

        private void UpdateRunSpeed()
        {
            if (GetCurrentLocomotionName() != run || !playerState.IsGrounded)
                return;

            var entry = skeletonAnimation.AnimationState.GetCurrent(locomotionTrack);
            if (entry == null || entry.Animation.Name != run)
                return;

            float speed = GetSpeed();
            float scale = controller.MoveSpeed > 0f ? speed / controller.MoveSpeed : 1f;
            bool depthRig = skeletonAnimation == frontSkeleton || skeletonAnimation == backSkeleton;
            entry.TimeScale = Mathf.Clamp(scale, minRunTimeScale, maxRunTimeScale) * runTimeScale * (depthRig ? depthRunTimeScale : 1f);
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
                   current == grabbed ||
                   current == interact ||
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

        private bool IsSteering => controller != null && Mathf.Abs(controller.HorizontalInput) > 0.1f;
        // Running on purpose (lane or depth input), not just sliding from knockback.
        private bool HasMoveInput => controller != null && (IsSteering || Mathf.Abs(controller.MoveInput.z) > 0.1f);

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
            if (animName == runStop) return runStopTimeScale;
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
            SafeSetMix(data, runStop, idle, locomotionBlend);
            SafeSetMix(data, runStop, run, locomotionBlend);
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
                if (eventName == AttackHoldEvent)
                {
                    BeginAttackHold(trackEntry);
                    return;
                }
                if (eventName == AttackReleaseEvent)
                {
                    ReleaseAttackHold(trackEntry);
                    return;
                }

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
