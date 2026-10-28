using System.Collections.Generic;
using UnityEngine;
using Spine;
using Spine.Unity;

namespace junklite
{
    /// <summary>
    /// Controls how a Spine animation is played back.
    /// Set mixDuration = 0 and resetPoseFirst = true to hard-cut with no blending
    /// (prevents bone-rotation artifacts like 360° head spins).
    /// </summary>
    [System.Serializable]
    public struct AnimPlaySettings
    {
        [Tooltip("Reset skeleton to setup pose before playing. Eliminates 360° bone-rotation artifacts caused by blending.")]
        public bool resetPoseFirst;
        [Tooltip("Blend duration in seconds. 0 = instant hard cut with no interpolation.")]
        [Range(0f, 1f)] public float mixDuration;
    }

    public class EnemySpineAnimationController : EnemyAnimationPresenter
    {
        [Header("Spine")]
        [SerializeField] private SkeletonAnimation skeletonAnimation;

        [Header("Animation Names")]
        [SerializeField] private string idle = "Idle";
        [SerializeField] private string walk = "Run";
        [SerializeField] private string run = "Run";
        [SerializeField] private string attackWindUp = "";
        [SerializeField] private string attack = "Attack_1";
        [SerializeField] private string charge = "Charge";
        [SerializeField] private string dash = "Dash";
        [SerializeField] private string dodge = "JumpBack";
        [SerializeField] private string hurt = "Hurt";
        [SerializeField] private string death = "Death";

        [Header("Leap (vault / fall back)")]
        [SerializeField] private string leapStart = "Jump_Start";
        [SerializeField] private string leapAir = "Float";
        [SerializeField] private string leapLand = "Jump_End";

        [Header("Launch Hurtbox")]
        [Tooltip("While launched or knocked down the drawn body is tilted and offset from the upright physics capsule. This box follows the drawn body (the skeleton mesh bounds) so a weapon hit that visibly overlaps it lands. It is on the Damageable layer: player weapons hit it, mods and enemy AI queries do not, and it has no physical contact with anything.")]
        [SerializeField] private bool launchHurtbox = true;
        [Tooltip("Thickness of the hurtbox along the depth axis (the sprite itself is flat).")]
        [SerializeField, Min(0.1f)] private float hurtboxDepth = 1f;

        [Header("Overlap")]
        [Tooltip("Each enemy draws at a slightly different depth (by instance, 8 steps of this size along local Z) so bodies that overlap while passing through each other always draw in the same order instead of flickering.")]
        [SerializeField, Min(0f)] private float overlapDepthStep = 0.01f;

        [Header("Launch (combo finisher)")]
        [Tooltip("One-shot held on its last frame while flying. The grunt has no Launched clip; Hurt reads well.")]
        [SerializeField] private string launchAir = "Hurt";
        [Tooltip("One-shot held on its last frame while lying down (used only when Downed Uses Death Fall is off).")]
        [SerializeField] private string launchDowned = "Hurt";
        [Tooltip("Knocked down: play the authored Death fall and hold it before the dead face, instead of rotating the sprite. The animator's fall already rests on the floor, like the corpse. Getting up plays the same fall in reverse.")]
        [SerializeField] private bool downedUsesDeathFall = true;
        [Tooltip("Time in the Death clip to hold while lying: after the fall has finished, before the dead-face swap (grunt: bones end at 0.38 s, face swaps at 0.48 s).")]
        [SerializeField, Min(0.01f)] private float downedHoldTime = 0.4f;
        [Tooltip("Played while getting up, fitted to the get-up duration.")]
        [SerializeField] private string launchGetUp = "Jump_End";
        [Tooltip("Height (local units) of the point the body rotates around when tilted: roughly the capsule centre.")]
        [SerializeField, Min(0f)] private float tiltPivotHeight = 1.15f;
        [Tooltip("Half thickness of the lying body: how high above the floor its centre rests when downed. A first guess; Fit Tilted Body To Floor corrects it from the real sprite.")]
        [SerializeField, Min(0f)] private float tiltGroundClearance = 0.35f;
        [Tooltip("While tilted (launched, lying, getting up), measure the rendered sprite every frame and move it so its lowest point rests exactly where the feet stand. Stops the hurt pose (arms, weapon) clipping into the floor.")]
        [SerializeField] private bool fitTiltedBodyToFloor = true;
        [Tooltip("How far the lying body may sink below the floor line (local units). Body parts are rectangular images, so the lowest image edge sits a little outside the visible art: raise this if a gap remains under the lying body, lower it if the body sinks. The lying pose is the same every time, so tune it live in Play Mode while a grunt lies down.")]
        [SerializeField] private float lyingFloorInset = 0.05f;

        [Header("Locomotion Scaling")]
        [Tooltip("Lowest time scale applied to the walk clip while an enemy shuffles slowly in place. Keeps a Run clip from looking frozen.")]
        [SerializeField, Range(0.1f, 1f)] private float minLocomotionTimeScale = 0.35f;

        [Header("Step Walks (hold, shuffle, back-off, flinch)")]
        [Tooltip("Clip for slow steps toward the facing direction while holding a slot or creeping back in. Empty = Walk.")]
        [SerializeField] private string walkForward = "";
        [Tooltip("Clip for stepping backward while still facing the player (back-off, flinch, shuffling away), and for any backward slide in chase or patrol. Empty = the forward clip (plays forward while moving back).")]
        [SerializeField] private string walkBackward = "";
        [Tooltip("Lane speed the backward direction must reach before the clip switches, so a stop does not flicker between the two.")]
        [SerializeField, Min(0f)] private float directionSwitchSpeed = 0.25f;
        [Tooltip("Lane speed at which Walk Forward / Walk Backward play at normal speed (the clip's own stride). Slower steps slow the clip down, faster ones speed it up to 1.5x.")]
        [SerializeField, Min(0.1f)] private float stepWalkReferenceSpeed = 2.5f;

        [Header("Measured Locomotion")]
        [Tooltip("In chase, patrol, hold and back-off the clip follows the measured velocity along the lane, so an enemy held by a body or a wall shows Idle instead of running in place.")]
        [SerializeField] private bool measuredLocomotion = true;
        [Tooltip("Lane speed above which the walk/run clip starts.")]
        [SerializeField, Min(0f)] private float moveStartSpeed = 0.35f;
        [Tooltip("Lane speed below which the clip returns to Idle.")]
        [SerializeField, Min(0f)] private float moveStopSpeed = 0.2f;
        [Tooltip("Seconds of smoothing on the measured speed.")]
        [SerializeField, Min(0.01f)] private float speedSmoothing = 0.08f;

        [Header("Melee Timing")]
        [Tooltip("When enabled, wind-up and attack clips are time-scaled to the authoritative gameplay phase duration.")]
        [SerializeField] private bool fitMeleeAnimationsToGameplayDuration;

        [Header("Action Timing")]
        [Tooltip("When enabled, dodge and charge clips play once and are time-scaled to their authoritative gameplay durations.")]
        [SerializeField] private bool fitActionAnimationsToGameplayDuration;

        [Header("Stun")]
        [SerializeField] private string stunLoop = "";
        [Header("Parry")]
        [Tooltip("Spine animation to play when entering ParriedState (one-shot).")]
        [SerializeField] private string onParried = "Hurt";
        [SerializeField] private string parryStun = "Stunt";

        [Header("Playback Settings — Hurt")]
        [Tooltip("How to play the Hurt animation. Hard-cut (mixDuration=0, resetPose=true) avoids 360° bone spin.")]
        [SerializeField] private AnimPlaySettings hurtSettings = new AnimPlaySettings { resetPoseFirst = true, mixDuration = 0f };

        [Header("Playback Settings — Stun")]
        [Tooltip("How to play the Stun loop. Hard-cut (mixDuration=0, resetPose=true) avoids 360° bone spin.")]
        [SerializeField] private AnimPlaySettings stunSettings = new AnimPlaySettings { resetPoseFirst = true, mixDuration = 0f };

        [Header("Playback Settings — OnParried")]
        [Tooltip("How to play the OnParried animation. Hard-cut (mixDuration=0, resetPose=true) avoids 360° bone spin.")]
        [SerializeField] private AnimPlaySettings onParriedSettings = new AnimPlaySettings { resetPoseFirst = true, mixDuration = 0f };

        private StateMachine stateMachine;
        private EnemyCharacter enemyCharacter;
        private bool isDead;
        private float previousTimeScale = 1f;
        private bool playbackPaused;

        // Knockdown on the authored Death fall: Falling plays it forward to the hold
        // time, Holding freezes it, Rising drives it backward to the start (get up).
        private enum DownedPose { None, Falling, Holding, Rising }
        private DownedPose downedPose;
        private TrackEntry downedEntry;
        private float riseTimer;
        private float riseDuration;

        // Measured locomotion
        private EnemyMovement movement;
        private IChaser chaser;
        private bool locomotionState;
        private bool stepWalkState;
        private string locomotionClip;
        private float smoothedAxisSpeed;
        private float smoothedSignedSpeed;
        private bool measuredMoving;
        private bool movingBackward;

        // Body tilt (launch): rotates the skeleton object around a pivot on its spine.
        private Transform tiltTarget;
        private Quaternion tiltBaseRotation = Quaternion.identity;
        private Vector3 tiltBasePosition;
        private float tiltCurrent;
        private float tiltGoal;
        private float tiltSpeed = 750f;
        private bool tiltOnGround;
        private bool tiltActive;
        private Renderer bodyRenderer;
        private MeshFilter bodyMeshFilter;
        private BoxCollider visualHurtbox;
        private readonly List<Vector3> meshVertices = new(256);
        // Bottom of the physics capsule relative to the enemy root, sampled while
        // standing: where the body really rests, independent of the art.
        private float feetOffset = float.NaN;
        private float groundCorrection;

        private void Awake()
        {
            if (skeletonAnimation == null)
                skeletonAnimation = GetComponentInChildren<SkeletonAnimation>(true);

            stateMachine = GetComponentInParent<StateMachine>();
            enemyCharacter = GetComponentInParent<EnemyCharacter>();

            if (skeletonAnimation != null)
            {
                tiltTarget = skeletonAnimation.transform;
                tiltBaseRotation = tiltTarget.localRotation;
                tiltBasePosition = tiltTarget.localPosition;

                // Stable draw order between overlapping enemies.
                int slot = (enemyCharacter != null ? enemyCharacter.GetInstanceID() : GetInstanceID()) & 7;
                tiltBasePosition.z += slot * overlapDepthStep;
                tiltTarget.localPosition = tiltBasePosition;
                bodyRenderer = skeletonAnimation.GetComponent<Renderer>();
                bodyMeshFilter = skeletonAnimation.GetComponent<MeshFilter>();
            }
        }

        private void Update()
        {
            UpdateMeasuredLocomotion();
            UpdateDownedPose();
            UpdateTilt();
        }

        private void LateUpdate()
        {
            FitVisualHurtbox();
        }

        public override void SetVisualHurtboxActive(bool active)
        {
            if (!launchHurtbox || tiltTarget == null)
                return;

            if (active && visualHurtbox == null)
            {
                int layer = LayerMask.NameToLayer("Damageable");
                var go = new GameObject("Launch Hurtbox");
                go.layer = layer >= 0 ? layer : (enemyCharacter != null ? enemyCharacter.gameObject.layer : 0);
                go.transform.SetParent(tiltTarget, false);
                visualHurtbox = go.AddComponent<BoxCollider>();
                visualHurtbox.isTrigger = false;
                // Hit queries only: no contact with anything, so it never pushes,
                // blocks or lands the body.
                visualHurtbox.excludeLayers = ~0;
            }

            if (visualHurtbox == null)
                return;

            visualHurtbox.enabled = active;
            if (active)
                FitVisualHurtbox();
        }

        public override bool TryGetBodyCenter(out Vector3 center)
        {
            // Mesh bounds are local, so they stay on the body when it is tilted.
            Mesh mesh = bodyMeshFilter != null ? bodyMeshFilter.sharedMesh : null;
            if (mesh != null && mesh.vertexCount > 0)
            {
                center = bodyMeshFilter.transform.TransformPoint(mesh.bounds.center);
                return true;
            }
            center = bodyRenderer != null ? bodyRenderer.bounds.center : default;
            return bodyRenderer != null;
        }

        private void FitVisualHurtbox()
        {
            if (visualHurtbox == null || !visualHurtbox.enabled)
                return;

            Mesh mesh = bodyMeshFilter != null ? bodyMeshFilter.sharedMesh : null;
            if (mesh == null || mesh.vertexCount == 0)
                return;

            Bounds bounds = mesh.bounds;
            visualHurtbox.center = bounds.center;
            visualHurtbox.size = new Vector3(
                Mathf.Max(0.1f, bounds.size.x),
                Mathf.Max(0.1f, bounds.size.y),
                Mathf.Max(hurtboxDepth, bounds.size.z));
        }

        private void UpdateDownedPose()
        {
            if (downedPose == DownedPose.None || isDead)
                return;

            if (downedEntry == null || skeletonAnimation == null
                || skeletonAnimation.AnimationState.GetCurrent(0) != downedEntry)
            {
                downedPose = DownedPose.None;
                downedEntry = null;
                return;
            }

            float hold = HoldTime;
            switch (downedPose)
            {
                case DownedPose.Falling:
                    if (downedEntry.TrackTime >= hold)
                    {
                        downedEntry.TimeScale = 0f;
                        downedEntry.TrackTime = hold;
                        downedPose = DownedPose.Holding;
                    }
                    break;

                case DownedPose.Rising:
                    riseTimer += Time.deltaTime;
                    float t = Mathf.Clamp01(riseTimer / Mathf.Max(0.01f, riseDuration));
                    downedEntry.TrackTime = Mathf.Lerp(hold, 0f, t);
                    if (t >= 1f)
                    {
                        downedPose = DownedPose.None;
                        downedEntry = null;
                        var idleEntry = skeletonAnimation.AnimationState.SetAnimation(0, idle, true);
                        if (idleEntry != null)
                            idleEntry.MixDuration = 0.08f;
                    }
                    break;
            }
        }

        private float HoldTime
        {
            get
            {
                float end = downedEntry?.Animation != null ? downedEntry.Animation.Duration : downedHoldTime;
                return Mathf.Clamp(downedHoldTime, 0f, end);
            }
        }

        /// <summary>
        /// Locomotion states play the clip the body earns: measured lane speed with
        /// hysteresis, so braking behind another grunt or a wall reads as standing.
        /// </summary>
        private void UpdateMeasuredLocomotion()
        {
            if (!measuredLocomotion || !locomotionState || isDead || playbackPaused || skeletonAnimation == null)
                return;

            if (movement == null)
            {
                movement = enemyCharacter != null ? enemyCharacter.Movement : null;
                if (movement == null)
                    return;
            }

            // A hit's knockback slide is not walking; the hit state owns the pose.
            if (movement.IsInKnockback)
                return;

            float signedSpeed = Vector3.Dot(movement.Velocity, movement.MovementAxis);
            float blend = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, speedSmoothing));
            smoothedSignedSpeed = Mathf.Lerp(smoothedSignedSpeed, signedSpeed, blend);
            smoothedAxisSpeed = Mathf.Abs(smoothedSignedSpeed);

            measuredMoving = measuredMoving
                ? smoothedAxisSpeed > moveStopSpeed
                : smoothedAxisSpeed > moveStartSpeed;

            // Backward = moving against the way the body faces (scale flip).
            float alongFacing = smoothedSignedSpeed * movement.FacingDirection;
            if (alongFacing < -directionSwitchSpeed)
                movingBackward = true;
            else if (alongFacing > directionSwitchSpeed)
                movingBackward = false;

            string clip = ResolveMovingClip();
            bool dedicatedStep = clip == walkForward || clip == walkBackward;
            float reference = dedicatedStep ? stepWalkReferenceSpeed : LocomotionReferenceSpeed();
            float maxScale = dedicatedStep ? 1.5f : 1f;
            float speed01 = measuredMoving ? Mathf.Clamp(smoothedAxisSpeed / reference, 0f, maxScale) : 0f;
            ApplyLocomotion(speed01, clip, maxScale);
        }

        private string ResolveMovingClip()
        {
            string forward = stepWalkState && !string.IsNullOrEmpty(walkForward) ? walkForward : locomotionClip;
            if (movingBackward && !string.IsNullOrEmpty(walkBackward))
                return walkBackward;
            return forward;
        }

        private float LocomotionReferenceSpeed()
        {
            if (chaser == null && enemyCharacter != null)
                chaser = enemyCharacter.GetCapability<IChaser>();

            if (chaser != null && chaser.ChaseSpeed > 0f)
                return chaser.ChaseSpeed;
            return Mathf.Max(0.01f, movement != null ? movement.MoveSpeed : 1f);
        }

        private void BeginLocomotionState(string clip, bool startMoving, bool stepWalk = false)
        {
            locomotionState = true;
            stepWalkState = stepWalk;
            locomotionClip = string.IsNullOrEmpty(clip) ? walk : clip;
            measuredMoving = startMoving;
            movingBackward = false;
            smoothedAxisSpeed = startMoving ? moveStartSpeed : 0f;
            smoothedSignedSpeed = 0f;
        }

        private void UpdateTilt()
        {
            if (!tiltActive || tiltTarget == null)
            {
                SampleFeetOffset();
                return;
            }

            tiltCurrent = Mathf.MoveTowards(tiltCurrent, tiltGoal, tiltSpeed * Time.deltaTime);
            ApplyTilt(tiltCurrent);

            if (Mathf.Approximately(tiltCurrent, tiltGoal) && tiltGoal <= 0f)
            {
                tiltActive = false;
                groundCorrection = 0f;
                tiltTarget.localRotation = tiltBaseRotation;
                tiltTarget.localPosition = tiltBasePosition;
            }
        }

        private void SampleFeetOffset()
        {
            if (!fitTiltedBodyToFloor || isDead || enemyCharacter == null)
                return;

            EnemyMovement body = enemyCharacter.Movement;
            if (body == null || !body.IsGrounded || body.IsLaunched)
                return;

            feetOffset = body.GetFeetPosition().y - enemyCharacter.transform.position.y;
        }

        /// <summary>
        /// Lowest point of the actual skeleton mesh in world space (its vertices, not
        /// the renderer's box, which over-reaches whenever the body is rotated).
        /// </summary>
        private float LowestRenderedY()
        {
            Mesh mesh = bodyMeshFilter != null ? bodyMeshFilter.sharedMesh : null;
            if (mesh == null || mesh.vertexCount == 0)
                return bodyRenderer != null ? bodyRenderer.bounds.min.y : float.NaN;

            mesh.GetVertices(meshVertices);
            Matrix4x4 toWorld = tiltTarget.localToWorldMatrix;
            float lowest = float.PositiveInfinity;
            for (int i = 0; i < meshVertices.Count; i++)
            {
                float y = toWorld.MultiplyPoint3x4(meshVertices[i]).y;
                if (y < lowest)
                    lowest = y;
            }

            return lowest;
        }

        /// <summary>
        /// Positive Z rotation sends the head away from the facing direction (the
        /// root scale flip mirrors it for the other facing). The body rotates
        /// around the pivot height; on the ground it is also dropped so the
        /// rotated body lies on the floor instead of hovering at pivot height.
        /// </summary>
        private void ApplyTilt(float degrees)
        {
            Quaternion tilt = Quaternion.Euler(0f, 0f, degrees);
            Vector3 pivot = Vector3.up * tiltPivotHeight;
            Vector3 offset = pivot - tilt * pivot;
            if (tiltOnGround)
            {
                float radians = degrees * Mathf.Deg2Rad;
                float drop = tiltPivotHeight * (1f - Mathf.Cos(radians)) - tiltGroundClearance * Mathf.Sin(radians);
                offset += Vector3.down * Mathf.Max(0f, drop);
            }

            tiltTarget.localRotation = tiltBaseRotation * tilt;
            tiltTarget.localPosition = tiltBasePosition + offset + Vector3.up * groundCorrection;

            if (!tiltOnGround || !fitTiltedBodyToFloor || float.IsNaN(feetOffset) || enemyCharacter == null)
            {
                groundCorrection = 0f;
                return;
            }

            float lowest = LowestRenderedY();
            if (float.IsNaN(lowest) || float.IsInfinity(lowest))
            {
                groundCorrection = 0f;
                return;
            }

            // The transform already carries last frame's correction and the vertices
            // are last frame's pose (static while held), so adding the error to the
            // previous correction lands exactly. The floor is the capsule's feet (the
            // body physically rests there); the inset lets the image edges dip just
            // below it so the visible art touches. Fades out near upright so the
            // get-up ends exactly on the authored pose.
            float parentScale = tiltTarget.parent != null ? Mathf.Abs(tiltTarget.parent.lossyScale.y) : 1f;
            float weight = Mathf.Clamp01(Mathf.Sin(degrees * Mathf.Deg2Rad) * 2f);
            float floorY = enemyCharacter.transform.position.y + feetOffset - lyingFloorInset * parentScale * weight;
            float error = (floorY - lowest) / Mathf.Max(0.0001f, parentScale);
            groundCorrection = Mathf.Clamp((groundCorrection + error) * weight, -2f, 2f);
            tiltTarget.localPosition = tiltBasePosition + offset + Vector3.up * groundCorrection;
        }

        private void OnEnable()
        {
            if (stateMachine != null)
                stateMachine.OnStateChanged += HandleStateChanged;
        }

        private void Start()
        {
            StartCoroutine(SyncInitialState());
        }

        private System.Collections.IEnumerator SyncInitialState()
        {
            yield return null;

            if (stateMachine != null && stateMachine.CurrentState != null)
            {
                HandleStateChanged(null, stateMachine.CurrentState);
            }
        }

        private void OnDisable()
        {
            if (stateMachine != null)
                stateMachine.OnStateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(IState from, IState to)
        {
            if (skeletonAnimation == null || to == null) return;

            if (to is DeadState)
            {
                PlayDeath();
                return;
            }

            if (isDead) return;

            // Only the locomotion states below re-enable the measured clip.
            locomotionState = false;

            // Leaving the launch ends any knockdown pose (the next state plays its own clip).
            if (!(to is LaunchedState))
            {
                downedPose = DownedPose.None;
                downedEntry = null;
            }

            // MeleeAttackState owns two gameplay phases. Those phases call the
            // presenter directly, so the state-change observer must not clear
            // the animation that was just selected during Enter().
            if (to is MeleeAttackState)
                return;

            // LeapState and LaunchedState drive their phases through the presenter API.
            if (to is LeapState || to is LaunchedState)
                return;

            var state = skeletonAnimation.AnimationState;
            skeletonAnimation.Skeleton.SetToSetupPose();
            state.ClearTrack(0);

            // Locomotion states: the first clip is the expected one, then the measured
            // lane speed takes over (see UpdateMeasuredLocomotion).
            if (to is IdleState || to is WaitForOpeningState || to is HoldPositionState || to is BackOffState
                || to is AlertState || to is SearchState)
            {
                state.SetAnimation(0, idle, true);
                if (to is HoldPositionState || to is BackOffState)
                    BeginLocomotionState(walk, false, stepWalk: true);
            }
            else if (to is PatrolState)
            {
                state.SetAnimation(0, walk, true);
                BeginLocomotionState(walk, true);
            }
            else if (to is ChaseState)
            {
                state.SetAnimation(0, run, true);
                BeginLocomotionState(run, true);
            }
            else if (to is ChargeState)
            {
                // Charge is an anticipation phase, not a looping locomotion state.
                // Fit the one-shot clip to the authoritative gameplay duration so
                // a short clip cannot restart before the dash begins.
                var entry = state.SetAnimation(0, charge, !fitActionAnimationsToGameplayDuration);
                if (fitActionAnimationsToGameplayDuration)
                    FitActionToGameplayDuration(entry, enemyCharacter?.GetCapability<ICharger>()?.ChargeTime ?? 0f);
            }
            else if (to is DashState)
                state.SetAnimation(0, dash, false);
            else if (to is DodgeState)
            {
                var entry = state.SetAnimation(0, dodge, false);
                if (fitActionAnimationsToGameplayDuration)
                    FitActionToGameplayDuration(entry, enemyCharacter?.GetCapability<IDodger>()?.DodgeDuration ?? 0f);
            }
            else if (to is ParriedState)
            {
                PlayOnParriedThenStunLoop();
            }
            else if (to is StunnedState)
            {
                if (from is ParriedState)
                {
                    PlayOnParriedThenStunLoop();
                    return;
                }

                // Hard-cut to hurt, then hard-cut to stun loop on completion.
                var hurtEntry = PlayWithSettings(0, hurt, false, hurtSettings);
                if (hurtEntry != null)
                {
                    hurtEntry.Complete += _ =>
                    {
                        if (stateMachine.CurrentState is StunnedState)
                        {
                            string stunAnim = string.IsNullOrEmpty(stunLoop) ? idle : stunLoop;
                            PlayWithSettings(0, stunAnim, true, stunSettings);
                        }
                    };
                }
            }

        }

        #region Public API

        public override void SetPlaybackPaused(bool paused)
        {
            if (skeletonAnimation == null || playbackPaused == paused) return;

            playbackPaused = paused;
            if (paused)
            {
                previousTimeScale = skeletonAnimation.timeScale;
                skeletonAnimation.timeScale = 0f;
            }
            else
            {
                skeletonAnimation.timeScale = previousTimeScale;
            }
        }

        public override void PlayMeleeWindup(float gameplayDuration)
        {
            if (isDead || skeletonAnimation == null) return;

            var state = skeletonAnimation.AnimationState;
            skeletonAnimation.Skeleton.SetToSetupPose();
            state.ClearTrack(0);

            bool hasDistinctWindup = !string.IsNullOrEmpty(attackWindUp)
                && !string.Equals(attackWindUp, attack, System.StringComparison.Ordinal);

            if (hasDistinctWindup)
            {
                var entry = state.SetAnimation(0, attackWindUp, false);
                FitToGameplayDuration(entry, gameplayDuration);
            }
            else
                state.SetAnimation(0, idle, true);
        }

        public override void PlayMeleeAttack(float gameplayDuration)
        {
            if (isDead || skeletonAnimation == null) return;

            var state = skeletonAnimation.AnimationState;
            var entry = state.SetAnimation(0, attack, false);
            if (entry != null)
            {
                entry.MixDuration = 0f;
                FitToGameplayDuration(entry, gameplayDuration);
            }
        }

        public override void SetLocomotionSpeed01(float speed01)
        {
            // A commanded hint from a state. When the measured speed drives the clip
            // it wins, because the command may be blocked by a body or a wall.
            if (measuredLocomotion && locomotionState)
                return;

            ApplyLocomotion(speed01, walk);
        }

        private void ApplyLocomotion(float speed01, string movingClip, float maxTimeScale = 1f)
        {
            if (isDead || skeletonAnimation == null) return;

            var state = skeletonAnimation.AnimationState;
            bool moving = speed01 > 0.01f;
            string desired = moving ? movingClip : idle;
            if (string.IsNullOrEmpty(desired)) return;

            TrackEntry current = state.GetCurrent(0);
            bool needsSwap = current == null
                || current.Animation == null
                || !current.Loop
                || !string.Equals(current.Animation.Name, desired, System.StringComparison.Ordinal);

            if (needsSwap)
            {
                current = state.SetAnimation(0, desired, true);
                if (current != null)
                    current.MixDuration = 0.1f;
            }

            if (current != null)
                current.TimeScale = moving ? Mathf.Clamp(speed01, minLocomotionTimeScale, maxTimeScale) : 1f;
        }

        public override void PlayLeapTelegraph(float gameplayDuration)
        {
            if (isDead || skeletonAnimation == null) return;

            var state = skeletonAnimation.AnimationState;
            skeletonAnimation.Skeleton.SetToSetupPose();
            state.ClearTrack(0);

            string clip = string.IsNullOrEmpty(leapStart) ? idle : leapStart;
            var entry = state.SetAnimation(0, clip, false);
            if (entry != null && fitActionAnimationsToGameplayDuration)
                FitActionToGameplayDuration(entry, gameplayDuration);
        }

        public override void PlayLeapAir()
        {
            if (isDead || skeletonAnimation == null) return;

            string clip = string.IsNullOrEmpty(leapAir) ? idle : leapAir;
            var entry = skeletonAnimation.AnimationState.SetAnimation(0, clip, true);
            if (entry != null)
                entry.MixDuration = 0.05f;
        }

        public override void PlayLaunched()
        {
            if (isDead || skeletonAnimation == null) return;

            // One-shot, no queued idle: Spine holds the last frame.
            PlayWithSettings(0, string.IsNullOrEmpty(launchAir) ? hurt : launchAir, false, hurtSettings);
        }

        public override void PlayLaunchBounce()
        {
            if (isDead || skeletonAnimation == null) return;

            // Restart the hurt pose so the bounce reads as a second impact.
            PlayWithSettings(0, string.IsNullOrEmpty(launchAir) ? hurt : launchAir, false, hurtSettings);
        }

        public override bool PlayDowned()
        {
            if (isDead || skeletonAnimation == null) return false;

            if (downedUsesDeathFall && !string.IsNullOrEmpty(death)
                && skeletonAnimation.Skeleton.Data.FindAnimation(death) != null)
            {
                // Knocked down again while rising: fall forward from where it is.
                if (downedEntry != null && skeletonAnimation.AnimationState.GetCurrent(0) == downedEntry)
                {
                    downedEntry.TimeScale = 1f;
                    downedPose = DownedPose.Falling;
                    return true;
                }

                downedEntry = PlayWithSettings(0, death, false, hurtSettings);
                if (downedEntry != null)
                {
                    downedEntry.TimeScale = 1f;
                    downedPose = DownedPose.Falling;
                    return true;
                }
            }

            string clip = string.IsNullOrEmpty(launchDowned) ? hurt : launchDowned;
            TrackEntry current = skeletonAnimation.AnimationState.GetCurrent(0);
            if (current?.Animation != null && string.Equals(current.Animation.Name, clip, System.StringComparison.Ordinal))
                return false; // already holding this pose from the flight

            PlayWithSettings(0, clip, false, hurtSettings);
            return false;
        }

        public override float DownedFallSeconds => Mathf.Max(0.05f, downedHoldTime);

        public override void PlayGetUp(float gameplayDuration)
        {
            if (isDead || skeletonAnimation == null) return;

            // Lying on the authored fall: get up by playing it backward.
            if (downedEntry != null && downedPose != DownedPose.None
                && skeletonAnimation.AnimationState.GetCurrent(0) == downedEntry)
            {
                downedEntry.TimeScale = 0f;
                downedEntry.TrackTime = Mathf.Min(downedEntry.TrackTime, HoldTime);
                riseTimer = 0f;
                riseDuration = Mathf.Max(0.05f, gameplayDuration);
                downedPose = DownedPose.Rising;
                return;
            }

            var state = skeletonAnimation.AnimationState;
            string clip = string.IsNullOrEmpty(launchGetUp) ? idle : launchGetUp;
            var entry = PlayWithSettings(0, clip, false, hurtSettings);
            if (entry != null)
                FitActionToGameplayDuration(entry, gameplayDuration);

            state.AddAnimation(0, idle, true, 0f);
        }

        public override void SetBodyTilt(float degrees, bool onGround, float blendSeconds)
        {
            if (tiltTarget == null) return;

            tiltGoal = Mathf.Clamp(degrees, 0f, 90f);
            tiltOnGround = onGround;
            tiltSpeed = Mathf.Abs(tiltGoal - tiltCurrent) / Mathf.Max(0.01f, blendSeconds);
            tiltActive = true;
        }

        public override void PlayLeapLand(float gameplayDuration)
        {
            if (isDead || skeletonAnimation == null) return;

            var state = skeletonAnimation.AnimationState;
            string clip = string.IsNullOrEmpty(leapLand) ? idle : leapLand;
            var entry = state.SetAnimation(0, clip, false);
            if (entry != null)
            {
                entry.MixDuration = 0f;
                if (fitActionAnimationsToGameplayDuration)
                    FitActionToGameplayDuration(entry, gameplayDuration);
            }

            state.AddAnimation(0, idle, true, 0f);
        }

        #endregion

        /// <summary>
        /// Plays a Spine animation using the supplied <see cref="AnimPlaySettings"/>.
        /// When <c>resetPoseFirst</c> is true the skeleton is snapped to its setup pose
        /// and the track is cleared before the new animation is set, preventing any
        /// cross-animation bone interpolation (e.g. 360° head spins).
        /// </summary>
        private TrackEntry PlayWithSettings(int track, string animName, bool loop, AnimPlaySettings settings)
        {
            if (skeletonAnimation == null || string.IsNullOrEmpty(animName)) return null;

            var state = skeletonAnimation.AnimationState;

            if (settings.resetPoseFirst)
            {
                skeletonAnimation.Skeleton.SetToSetupPose();
                state.ClearTrack(track);
            }

            var entry = state.SetAnimation(track, animName, loop);
            if (entry != null) entry.MixDuration = settings.mixDuration;
            return entry;
        }

        private void PlayOnParriedThenStunLoop()
        {
            var state = skeletonAnimation.AnimationState;
            string parriedAnim = string.IsNullOrEmpty(onParried) ? idle : onParried;
            string stunAnim = string.IsNullOrEmpty(parryStun)
                ? (string.IsNullOrEmpty(stunLoop) ? hurt : stunLoop)
                : parryStun;

            var parriedEntry = PlayWithSettings(0, parriedAnim, false, onParriedSettings);
            if (parriedEntry != null)
            {
                var queuedStun = state.AddAnimation(0, stunAnim, true, 0f);
                if (queuedStun != null) queuedStun.MixDuration = stunSettings.mixDuration;
                return;
            }

            PlayWithSettings(0, stunAnim, true, stunSettings);
        }

        private void FitToGameplayDuration(TrackEntry entry, float gameplayDuration)
        {
            if (!fitMeleeAnimationsToGameplayDuration || entry?.Animation == null || gameplayDuration <= 0f)
                return;

            FitActionToGameplayDuration(entry, gameplayDuration);
        }

        private static void FitActionToGameplayDuration(TrackEntry entry, float gameplayDuration)
        {
            if (entry?.Animation == null || entry.Animation.Duration <= 0f || gameplayDuration <= 0f)
                return;

            entry.TimeScale = entry.Animation.Duration / gameplayDuration;
        }

        private void PlayDeath()
        {
            if (isDead) return;
            isDead = true;

            var state = skeletonAnimation.AnimationState;

            // Killed while on the knockdown fall (lying or rising): it is already the
            // death clip, so just let it run forward to the end from where it is.
            if (downedEntry != null && state.GetCurrent(0) == downedEntry)
            {
                downedEntry.TimeScale = 1f;
                downedPose = DownedPose.None;
                downedEntry = null;
                return;
            }
            skeletonAnimation.Skeleton.SetToSetupPose();
            state.ClearTracks();
            var entry = state.SetAnimation(0, death, false);

            if (entry != null)
                entry.MixDuration = 0f;
            else
                Debug.LogError($"[AnimCtrl] Failed to play '{death}' - check animation name in Spine!");

            // Killed while tilted (launched or downed): the death clip lays the body
            // down by itself, so hand the rotation over to it instead of adding the
            // two together, which would stand the corpse on its head.
            if (tiltTarget != null && (tiltActive || tiltCurrent > 0f))
            {
                float handover = entry?.Animation != null ? Mathf.Max(0.05f, entry.Animation.Duration) : 0.3f;
                SetBodyTilt(0f, tiltOnGround, handover);
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Validate Animation Configuration")]
        private void ValidateAnimationConfiguration()
        {
            if (skeletonAnimation == null)
            {
                Debug.LogError($"[{name}] Spine presenter requires a SkeletonAnimation.", this);
                return;
            }

            SkeletonData data = skeletonAnimation.SkeletonDataAsset?.GetSkeletonData(true);
            if (data == null)
            {
                Debug.LogError($"[{name}] Spine presenter could not load skeleton data.", this);
                return;
            }

            ValidateAnimation(data, idle, nameof(idle), true);
            ValidateAnimation(data, walk, nameof(walk), false);
            ValidateAnimation(data, run, nameof(run), false);
            ValidateAnimation(data, attackWindUp, nameof(attackWindUp), false);
            ValidateAnimation(data, attack, nameof(attack), false);
            ValidateAnimation(data, charge, nameof(charge), false);
            ValidateAnimation(data, dash, nameof(dash), false);
            ValidateAnimation(data, dodge, nameof(dodge), false);
            ValidateAnimation(data, hurt, nameof(hurt), false);
            ValidateAnimation(data, death, nameof(death), false);
            ValidateAnimation(data, stunLoop, nameof(stunLoop), false);
            ValidateAnimation(data, onParried, nameof(onParried), false);
            ValidateAnimation(data, parryStun, nameof(parryStun), false);
            ValidateAnimation(data, leapStart, nameof(leapStart), false);
            ValidateAnimation(data, leapAir, nameof(leapAir), false);
            ValidateAnimation(data, leapLand, nameof(leapLand), false);
            ValidateAnimation(data, walkForward, nameof(walkForward), false);
            ValidateAnimation(data, walkBackward, nameof(walkBackward), false);
            ValidateAnimation(data, launchAir, nameof(launchAir), false);
            ValidateAnimation(data, launchDowned, nameof(launchDowned), false);
            ValidateAnimation(data, launchGetUp, nameof(launchGetUp), false);

            if (!string.IsNullOrEmpty(attackWindUp)
                && string.Equals(attackWindUp, attack, System.StringComparison.Ordinal))
            {
                Debug.LogWarning(
                    $"[{name}] Wind-up and attack both use '{attack}'. " +
                    "The wind-up phase will show idle so the attack clip only plays once.",
                    this);
            }
        }

        private void ValidateAnimation(SkeletonData data, string animationName, string fieldName, bool required)
        {
            if (string.IsNullOrWhiteSpace(animationName))
            {
                if (required)
                    Debug.LogError($"[{name}] Required Spine animation field '{fieldName}' is empty.", this);
                return;
            }

            if (data.FindAnimation(animationName) == null)
            {
                Debug.LogWarning(
                    $"[{name}] Spine animation '{animationName}' configured in '{fieldName}' was not found.",
                    this);
            }
        }

        private void OnValidate()
        {
            if (skeletonAnimation == null)
                skeletonAnimation = GetComponentInChildren<SkeletonAnimation>(true);
        }
#endif
    }
}
