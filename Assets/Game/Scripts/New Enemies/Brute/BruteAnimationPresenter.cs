using Spine;
using Spine.Unity;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Spine presentation for the Brute (Enemy_3 skeleton). Reuses the existing
    /// clips by holding single frames instead of authoring new animations:
    /// charge holds the end of Land_SmashGround, dash plays Grab then holds
    /// Attack_Melee, stuns hold the first frame of StandUp.
    /// </summary>
    public sealed class BruteAnimationPresenter : EnemyAnimationPresenter, IBruteGrabPresenter
    {
        [Header("Spine")]
        [SerializeField] private SkeletonAnimation skeletonAnimation;

        [Header("Clips")]
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string idle = "1_Idle";
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string chargeHold = "3_4_Land_SmashGround";
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string standUp = "5_StandUp";
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string taunt = "6_Point";
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string attack = "7_Attack_Melee";
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string grab = "8_Grab";
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string throwClip = "9_Throw";

        [Header("Timing")]
        [Tooltip("Scale the attack and throw clips to their gameplay durations.")]
        [SerializeField] private bool fitClipsToGameplay = true;
        [SerializeField] private float mixDuration = 0.08f;

        private StateMachine stateMachine;
        private float previousTimeScale = 1f;
        private bool playbackPaused;
        private bool isDead;

        private void Awake()
        {
            if (skeletonAnimation == null)
                skeletonAnimation = GetComponentInChildren<SkeletonAnimation>(true);

            stateMachine = GetComponentInParent<StateMachine>();
        }

        private void OnEnable()
        {
            if (stateMachine != null)
                stateMachine.OnStateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (stateMachine != null)
                stateMachine.OnStateChanged -= HandleStateChanged;
        }

        private void Start()
        {
            if (stateMachine != null && stateMachine.CurrentState != null)
                HandleStateChanged(null, stateMachine.CurrentState);
        }

        private void HandleStateChanged(IState from, IState to)
        {
            if (skeletonAnimation == null || to == null || isDead)
                return;

            switch (to)
            {
                case DeadState:
                    isDead = true;
                    HoldFrame(idle, atEnd: false);
                    break;

                // These states drive their own phases through the presenter API.
                case MeleeAttackState:
                case BruteGrabState:
                    break;

                case ChargeState:
                    HoldFrame(chargeHold, atEnd: true);
                    break;

                case DashState:
                    Play(grab, loop: false);
                    // Queued clip holds its last frame once it finishes.
                    skeletonAnimation.AnimationState.AddAnimation(0, attack, false, 0f);
                    break;

                case RecoverState:
                    Play(standUp, loop: false);
                    break;

                case ParriedState:
                case StunnedState:
                    HoldFrame(standUp, atEnd: false);
                    break;

                default: // Idle, Chase: no walk clip, he glides on idle.
                    Play(idle, loop: true);
                    break;
            }
        }

        // =============================================================
        // EnemyAnimationPresenter
        // =============================================================

        public override void PlayMeleeWindup(float gameplayDuration)
        {
            // Taunt: point, then hold the pose for the rest of the wind-up.
            Play(taunt, loop: false);
        }

        public override void PlayMeleeAttack(float gameplayDuration)
        {
            TrackEntry entry = Play(attack, loop: false);
            Fit(entry, gameplayDuration);
        }

        public override void SetPlaybackPaused(bool paused)
        {
            if (skeletonAnimation == null || playbackPaused == paused)
                return;

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

        // =============================================================
        // IBruteGrabPresenter
        // =============================================================

        public void PlayGrabWarning(float duration) => HoldFrame(grab, atEnd: false);
        public void PlayGrabReach() => Play(grab, loop: false);
        public void PlayGrabHold() => HoldFrame(grab, atEnd: true);

        public void PlayThrow(float gameplayDuration)
        {
            TrackEntry entry = Play(throwClip, loop: false);
            Fit(entry, gameplayDuration);
        }

        // =============================================================
        // HELPERS
        // =============================================================

        private TrackEntry Play(string clip, bool loop)
        {
            if (skeletonAnimation == null || string.IsNullOrEmpty(clip))
                return null;

            TrackEntry entry = skeletonAnimation.AnimationState.SetAnimation(0, clip, loop);
            if (entry != null)
                entry.MixDuration = mixDuration;
            return entry;
        }

        /// <summary>Freezes the track on the first or last frame of a clip.</summary>
        private void HoldFrame(string clip, bool atEnd)
        {
            TrackEntry entry = Play(clip, loop: false);
            if (entry == null)
                return;

            entry.TrackTime = atEnd ? entry.AnimationEnd : 0f;
            entry.TimeScale = 0f;
        }

        private void Fit(TrackEntry entry, float gameplayDuration)
        {
            if (!fitClipsToGameplay || entry?.Animation == null || gameplayDuration <= 0f)
                return;

            entry.TimeScale = entry.Animation.Duration / gameplayDuration;
        }
    }
}
