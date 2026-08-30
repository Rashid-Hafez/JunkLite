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

        private void Awake()
        {
            if (skeletonAnimation == null)
                skeletonAnimation = GetComponentInChildren<SkeletonAnimation>(true);

            stateMachine = GetComponentInParent<StateMachine>();
            enemyCharacter = GetComponentInParent<EnemyCharacter>();
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

            // MeleeAttackState owns two gameplay phases. Those phases call the
            // presenter directly, so the state-change observer must not clear
            // the animation that was just selected during Enter().
            if (to is MeleeAttackState)
                return;

            var state = skeletonAnimation.AnimationState;
            skeletonAnimation.Skeleton.SetToSetupPose();
            state.ClearTrack(0);

            if (to is IdleState)
                state.SetAnimation(0, idle, true);
            else if (to is PatrolState)
                state.SetAnimation(0, walk, true);
            else if (to is ChaseState)
                state.SetAnimation(0, run, true);
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
            skeletonAnimation.Skeleton.SetToSetupPose();
            state.ClearTracks();
            var entry = state.SetAnimation(0, death, false);

            if (entry != null)
                entry.MixDuration = 0f;
            else
                Debug.LogError($"[AnimCtrl] Failed to play '{death}' - check animation name in Spine!");
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
