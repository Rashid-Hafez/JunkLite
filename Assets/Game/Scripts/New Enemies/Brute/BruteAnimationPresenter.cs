using Spine;
using Spine.Unity;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Spine presentation for the Brute (Enemy_3 skeleton). A one-shot clip holds its
    /// last frame until the next one replaces it: Charge holds until it blends into
    /// ChargeAttack (the dash), staggers (stun, parry) and death play Hurt and hold.
    /// Idle and Chase walk whenever he is moving, idle otherwise.
    /// </summary>
    public sealed class BruteAnimationPresenter : EnemyAnimationPresenter, IBruteGrabPresenter
    {
        [Header("Spine")]
        [SerializeField] private SkeletonAnimation skeletonAnimation;

        [Header("Clips")]
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string idle = "1_Idle";
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string walk = "Walk";
        [Tooltip("Charge-up: plays once, then holds its last frame until the dash.")]
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string charge = "Charge";
        [Tooltip("The dash. Blends in from the held Charge pose, then holds its last frame.")]
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string chargeAttack = "ChargeAttack";
        [Tooltip("Stagger (stun, parry) and death: plays once, then holds its last frame.")]
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string hurt = "Hurt";
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string standUp = "5_StandUp";
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string taunt = "6_Point";
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string attack = "7_Attack_Melee";
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string grab = "8_Grab";
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string throwClip = "9_Throw";
        [Tooltip("Pose held (first frame) while knocked down. Defaults to the stun pose until a dedicated clip exists.")]
        [SpineAnimation(dataField: nameof(skeletonAnimation))] [SerializeField] private string knockedDown = "5_StandUp";

        [Header("Timing")]
        [Tooltip("Scale the attack and throw clips to their gameplay durations.")]
        [SerializeField] private bool fitClipsToGameplay = true;
        [SerializeField] private float mixDuration = 0.08f;
        [Tooltip("Blend from the held Charge pose into ChargeAttack.")]
        [SerializeField, Min(0f)] private float chargeAttackMix = 0.15f;

        private StateMachine stateMachine;
        private BruteEnemy brute;
        private EnemyMovement movement;
        // Idle/Chase: the loop picks walk or idle from movement every frame.
        private bool locomotion;
        private string locomotionClip;
        private float previousTimeScale = 1f;
        private bool playbackPaused;
        private bool isDead;

        private void Awake()
        {
            if (skeletonAnimation == null)
                skeletonAnimation = GetComponentInChildren<SkeletonAnimation>(true);

            stateMachine = GetComponentInParent<StateMachine>();
            brute = GetComponentInParent<BruteEnemy>();
            movement = GetComponentInParent<EnemyMovement>();
        }

        private void Update()
        {
            if (locomotion && !isDead)
                UpdateLocomotion();
        }

        private void UpdateLocomotion()
        {
            string clip = movement != null && movement.IsMoving && movement.CurrentSpeed > 0.01f ? walk : idle;
            if (clip == locomotionClip)
                return;

            locomotionClip = clip;
            Play(clip, loop: true);
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

            locomotion = false;
            locomotionClip = null;

            switch (to)
            {
                case DeadState:
                    // Holds the last frame until the body is removed (EnemyCharacter.deathLinger).
                    isDead = true;
                    Play(hurt, loop: false);
                    break;

                // These states drive their own phases through the presenter API.
                case MeleeAttackState:
                case BruteGrabState:
                    break;

                case ChargeState:
                    Play(charge, loop: false);
                    break;

                case DashState:
                    TrackEntry dash = Play(chargeAttack, loop: false);
                    if (dash != null)
                        dash.MixDuration = chargeAttackMix;
                    break;

                case RecoverState:
                    Play(standUp, loop: false);
                    break;

                case StunnedState when brute != null && brute.IsKnockedDown:
                    HoldFrame(knockedDown, atEnd: false);
                    break;

                // Staggered (incl. the parried dash slide): Hurt, then hold its last frame.
                case ParriedState:
                case StunnedState:
                    Play(hurt, loop: false);
                    break;

                default: // Idle, Chase: walk while moving, idle while standing.
                    locomotion = true;
                    UpdateLocomotion();
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
