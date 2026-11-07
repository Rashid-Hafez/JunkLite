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

        /// <summary>
        /// Continuous locomotion for states that move without a dedicated clip
        /// (holding a slot, backing off). 0 = standing, 1 = full chase speed.
        /// Presenters that already derive locomotion from EnemyMovement may ignore it.
        /// </summary>
        public virtual void SetLocomotionSpeed01(float speed01) { }

        /// <summary>Present the grounded crouch before a leap.</summary>
        public virtual void PlayLeapTelegraph(float gameplayDuration) { }

        /// <summary>Present the airborne phase of a leap.</summary>
        public virtual void PlayLeapAir() { }

        /// <summary>Present the landing of a leap, then return to idle.</summary>
        public virtual void PlayLeapLand(float gameplayDuration) { }

        /// <summary>Thrown into the air by a launching hit (hold a hurt pose).</summary>
        public virtual void PlayLaunched() { }

        /// <summary>First ground contact after a launch, bouncing back up.</summary>
        public virtual void PlayLaunchBounce() { }

        /// <summary>
        /// Lying on the ground after a launch. Returns true when the presenter lays the
        /// body down with an authored fall (no body tilt needed); false when the caller
        /// should tilt the body flat.
        /// </summary>
        public virtual bool PlayDowned() => false;

        /// <summary>How long the authored fall takes, when <see cref="PlayDowned"/> returned true.</summary>
        public virtual float DownedFallSeconds => 0.3f;

        /// <summary>Getting up after being downed, then return to idle.</summary>
        public virtual void PlayGetUp(float gameplayDuration) { }

        /// <summary>
        /// Tilt the body toward horizontal (0 = upright, 90 = flat), feet toward the
        /// facing direction, blending over blendSeconds. onGround keeps the tilted
        /// body on the floor.
        /// </summary>
        public virtual void SetBodyTilt(float degrees, bool onGround, float blendSeconds) { }

        /// <summary>
        /// While launched or knocked down, let weapon hits test against the drawn body
        /// instead of only the upright physics capsule.
        /// </summary>
        public virtual void SetVisualHurtboxActive(bool active) { }

        /// <summary>Middle of the drawn body in world space, for hit VFX. False when the presenter can't tell.</summary>
        public virtual bool TryGetBodyCenter(out Vector3 center)
        {
            center = default;
            return false;
        }
    }
}
