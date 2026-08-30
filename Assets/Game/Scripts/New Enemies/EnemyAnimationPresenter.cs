using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Technology-neutral presentation boundary for enemy animation.
    /// Gameplay may request semantic action phases through this API, but it must
    /// never depend on Animator parameters, Spine animation names, or clip state.
    /// Continuous locomotion and other state presentation can be derived by the
    /// concrete presenter from EnemyMovement and StateMachine.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class EnemyAnimationPresenter : MonoBehaviour
    {
        /// <summary>Present the anticipation phase of a melee action.</summary>
        public abstract void PlayMeleeWindup(float gameplayDuration);

        /// <summary>Present the active swing phase of a melee action.</summary>
        public abstract void PlayMeleeAttack(float gameplayDuration);

        /// <summary>Pause or resume visual playback without changing gameplay state.</summary>
        public abstract void SetPlaybackPaused(bool paused);
    }
}
