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

    public class EnemySpineAnimationController : MonoBehaviour
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

        [Header("Debug")]
        [SerializeField] private bool debugLog = false;

        private StateMachine stateMachine;
        private EnemyCharacter enemyCharacter;
        private bool isDead;
        private float previousTimeScale = 1f;

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
                if (debugLog)
                    Debug.Log($"[AnimCtrl] Syncing initial state: {stateMachine.CurrentState.GetType().Name}");
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
            if (debugLog)
                Debug.Log($"[AnimCtrl] State: {from?.GetType().Name ?? "null"} -> {to?.GetType().Name ?? "null"}");

            if (skeletonAnimation == null || to == null) return;

            if (to is DeadState)
            {
                PlayDeath();
                return;
            }

            if (isDead) return;

            var state = skeletonAnimation.AnimationState;
            skeletonAnimation.Skeleton.SetToSetupPose();
            state.ClearTrack(0);

            if (to is IdleState)
                state.SetAnimation(0, idle, true);
            else if (to is PatrolState)
                state.SetAnimation(0, walk, true);
            else if (to is ChaseState)
                state.SetAnimation(0, run, true);
            else if (to is MeleeAttackState)
            {
                // Don't play anything here � the enemy will call
                // PlayWindUpAnimation() and PlayAttackAnimation()
                // at the right moments via OnMeleeWindUp / OnMeleeAttack.
            }
            else if (to is ChargeState)
                state.SetAnimation(0, charge, true);
            else if (to is DashState)
                state.SetAnimation(0, dash, false);
            else if (to is DodgeState)
                state.SetAnimation(0, dodge, false);
            else if (to is ParriedState)
            {
                // Hard-cut to 'onParried' one-shot, then queue the parry-stun loop.
                string parriedAnim = string.IsNullOrEmpty(onParried) ? idle : onParried;
                string stunAnim    = string.IsNullOrEmpty(parryStun)
                    ? (string.IsNullOrEmpty(stunLoop) ? hurt : stunLoop)
                    : parryStun;

                var parriedEntry = PlayWithSettings(0, parriedAnim, false, onParriedSettings);
                if (parriedEntry != null)
                {
                    var queuedStun = state.AddAnimation(0, stunAnim, true, 0f);
                    if (queuedStun != null) queuedStun.MixDuration = stunSettings.mixDuration;
                }
            }
            else if (to is StunnedState)
            {
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

        public void SetPlaybackPaused(bool paused)
        {
            if (skeletonAnimation == null) return;

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

        public void PlayWindUpAnimation()
        {
            if (isDead || skeletonAnimation == null) return;

            var state = skeletonAnimation.AnimationState;
            skeletonAnimation.Skeleton.SetToSetupPose();
            state.ClearTrack(0);

            if (!string.IsNullOrEmpty(attackWindUp))
                state.SetAnimation(0, attackWindUp, false);
            else
                state.SetAnimation(0, idle, true);
        }

        public void PlayAttackAnimation()
        {
            if (isDead || skeletonAnimation == null) return;

            var state = skeletonAnimation.AnimationState;
            var entry = state.SetAnimation(0, attack, false);
            if (entry != null) entry.MixDuration = 0f;
        }

        public void PlayStunLoop(float duration)
        {
            if (skeletonAnimation == null) return;
            string anim = string.IsNullOrEmpty(stunLoop) ? hurt : stunLoop;
            PlayWithSettings(0, anim, true, stunSettings);
            StartCoroutine(ClearStunAfter(duration));
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

        private System.Collections.IEnumerator ClearStunAfter(float duration)
        {
            yield return new WaitForSeconds(duration);
            if (stateMachine != null && stateMachine.CurrentState != null)
                HandleStateChanged(null, stateMachine.CurrentState);
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
    }
}