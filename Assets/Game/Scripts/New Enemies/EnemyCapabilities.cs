using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Allows a composed component to expose nested runtime capabilities without
    /// forwarding every capability member through the component itself.
    /// </summary>
    public interface IEnemyCapabilityProvider
    {
        bool TryGetCapability<T>(out T capability) where T : class;
    }

    // ============================================================
    // CAPABILITY INTERFACES
    // Enemy actors or composed providers expose these to declare what they can do.
    // States check for these interfaces to access capability-specific data.
    // ============================================================

    /// <summary>
    /// Enemy can patrol back and forth.
    /// Used by: PatrolState
    /// </summary>
    public interface IPatroller
    {
        float PatrolDistance { get; }
        float PatrolSpeed { get; }
        Vector3 SpawnPosition { get; }
        int PatrolDirection { get; set; }

        bool IsWallAhead();
        /// <summary>No floor a step ahead: reverse instead of walking off the platform.</summary>
        bool IsLedgeAhead();
        bool IsAtPatrolBoundary();
        void ReverseDirection();
    }

    /// <summary>
    /// Enemy can perform a charge-up before attacking.
    /// Used by: ChargeState
    /// </summary>
    public interface ICharger
    {
        float ChargeTime { get; }
        GameObject ChargeVFXPrefab { get; }

        // Callback when charge completes - enemy decides what to do next
        void OnChargeComplete();
    }

    /// <summary>
    /// Enemy can perform a dash attack.
    /// Used by: DashState
    /// </summary>
    public interface IDasher
    {
        float DashSpeed { get; }
        float DashDamage { get; }
        Vector2 DashKnockback { get; }
        Hitbox DashHitbox { get; }
        float DashStopDistance { get; }
        GameObject DashVFXPrefab { get; }
        bool DashCanBeInterrupted { get; }
        float DashAttackStartNormalized { get; }
        float DashAttackActiveDuration { get; }
        float DashWhiffResolveDelay { get; }

        // Callback when dash completes - enemy decides what to do next
        void OnDashComplete();
    }

    /// <summary>
    /// Enemy can grab and throw targets.
    /// Used by: GrabState, and checked during dash hit
    /// </summary>
    public interface IGrabber
    {
        bool CanGrab { get; }
        float GrabChance { get; }
        float GrabDuration { get; }
        Vector3 GrabOffset { get; }
        Vector2 ThrowForce { get; }
        float ThrowDamage { get; }
        GameObject GrabVFXPrefab { get; }

        // Callback when grab/throw completes - enemy decides what to do next
        void OnGrabComplete();
    }

    /// <summary>
    /// Enemy can recover after an action (cooldown/stagger).
    /// Used by: RecoverState
    /// </summary>
    public interface IRecoverer
    {
        float RecoveryTime { get; }
        GameObject RecoveryVFXPrefab { get; }

        // Callback when recovery completes - enemy decides what to do next
        void OnRecoveryComplete();
    }

    /// <summary>
    /// Enemy can perform melee attacks.
    /// Used by: MeleeAttackState
    /// </summary>
    public interface IMeleeAttacker
    {
        float MeleeWindUpDuration { get; }
        float MeleeAttackDuration { get; }
        float MeleeHitStartNormalized { get; }
        float MeleeHitEndNormalized { get; }
        float MeleeAttackSpeed { get; }
        float MeleeDamage { get; }
        Vector2 MeleeKnockback { get; }
        Hitbox MeleeHitbox { get; }
        GameObject MeleeVFXPrefab { get; }
        void OnMeleeComplete();
    }

    /// <summary>
    /// Optional: enemy glides toward its target during the melee swing.
    /// Used by: MeleeAttackState (ignored when absent or speed is 0)
    /// </summary>
    public interface IMeleeLunge
    {
        float LungeSpeed { get; }
        float LungeDuration { get; }
    }

    /// <summary>
    /// Enemy can dodge/evade.
    /// Used by: DodgeState
    /// </summary>
    public interface IDodger
    {
        float DodgeDistance { get; }
        float DodgeDuration { get; }
        float DodgeHeight { get; }
        bool DodgeHasIFrames { get; }
        GameObject DodgeVFXPrefab { get; }
        LayerMask DodgeWallLayer { get; }
        float DodgeWallCheckBuffer { get; }
        float DodgeForwardChance { get; }

        // Callback when dodge completes
        void OnDodgeComplete();
    }

    /// <summary>
    /// Enemy can chase targets persistently.
    /// Used by: ChaseState
    /// </summary>
    public interface IChaser
    {
        Vector3 LastKnownTargetPosition { get; }
        bool HasLastKnownPosition { get; }
        float ChaseSpeed { get; }

        /// <summary>
        /// Distance at which to stop chasing (0 = use attack range instead).
        /// Useful for non-attacking enemies that just follow.
        /// </summary>
        float ChaseStopDistance { get; }

        // Called when enemy reaches target or destination
        void OnReachedTarget();

        // Update last known position
        void UpdateLastKnownPosition(Vector3 position);
    }

    /// <summary>
    /// Optional policy used by ChaseState when an enemy should approach an
    /// assigned engagement position instead of driving directly at the target.
    /// </summary>
    public interface IChaseDestinationProvider
    {
        bool TryGetChaseDestination(
            Vector3 targetPosition,
            float defaultStopDistance,
            out Vector3 destination,
            out float destinationStopDistance);

        void OnChaseDestinationReached();
    }

    /// <summary>
    /// Enemy can perform ranged attacks.
    /// Used by: RangedAttackState (future)
    /// </summary>
    public interface IRangedAttacker
    {
        float RangedAttackDuration { get; }
        float RangedDamage { get; }
        float ProjectileSpeed { get; }
        float RangedAttackRange { get; }
        GameObject ProjectilePrefab { get; }
        Transform ProjectileSpawnPoint { get; }
        GameObject RangedVFXPrefab { get; }

        // Callback when ranged attack completes
        void OnRangedAttackComplete();
    }

    public interface IStunnable
    {
        float StaggerDuration { get; }
        float ForcedStunDuration { get; set; }
        GameObject StunVFXObject { get; }

        // Callback when stun completes - enemy decides what to do next
        void OnStunComplete();
    }

    /// <summary>
    /// Enemy holds an engagement slot while staying visibly alive: follows the
    /// slot, shuffles around it, and may feint toward the player.
    /// Used by: HoldPositionState
    /// </summary>
    public interface IHoldPositioner
    {
        /// <summary>Speed used to walk back to the slot after drifting.</summary>
        float HoldSpeed { get; }
        /// <summary>Drift from the slot tolerated before walking back.</summary>
        float HoldTolerance { get; }
        /// <summary>Largest shuffle offset away from the player.</summary>
        float PaceAmplitude { get; }
        /// <summary>Largest shuffle offset toward the player.</summary>
        float PaceTowardLimit { get; }
        float PaceSpeed { get; }
        /// <summary>Min/max pause between shuffles, in seconds.</summary>
        Vector2 PacePauseRange { get; }
        /// <summary>-1 wary (shuffle away), 0 neutral, +1 bold (shuffle in). Updated by the brain.</summary>
        float PaceBias { get; }
        /// <summary>Extra distance added to the slot. Positive holds further from the player.</summary>
        float HoldOffset { get; }
        /// <summary>Per-instance multiplier on pause lengths so neighbours fall out of phase.</summary>
        float RhythmScale { get; }
        bool FeintAllowed { get; }
        /// <summary>Chance per pause to feint a step toward the player.</summary>
        float FeintChance { get; }
        float FeintDistance { get; }
        float FeintDuration { get; }
        /// <summary>Layers a shuffle or feint must not walk into.</summary>
        LayerMask ObstacleLayers { get; }
        /// <summary>False while the player is running: no shuffling, follow the slot tightly.</summary>
        bool PaceEnabled { get; }

        /// <summary>
        /// True once when the brain wants a flinch step back (an ally ahead was hit).
        /// The state then steps back, holds until <see cref="RecoilHoldUntil"/>, and creeps back in.
        /// </summary>
        bool TryConsumeRecoil(out float distance);
        /// <summary>Time until which a flinched enemy stays back. Extended while a combo continues.</summary>
        float RecoilHoldUntil { get; }
        float RecoilSpeed { get; }
        /// <summary>Slow speed for stepping back in after a flinch.</summary>
        float CreepSpeed { get; }
    }

    /// <summary>
    /// Optional movement filter that keeps lane spacing from the real bodies
    /// ahead and behind. States pass every voluntary move through it; it may pull
    /// the destination back and lower the speed (speed matching), so enemies never
    /// push each other. Used by: ChaseState, HoldPositionState, BackOffState.
    /// </summary>
    public interface ILaneSpacing
    {
        void ApplySpacing(ref Vector3 destination, ref float speed);
    }

    /// <summary>
    /// Brain-level listener for a lane-mate ahead taking a real hit.
    /// Implementations must only record the event (called from inside the director).
    /// </summary>
    public interface IAllyHitListener
    {
        void OnAllyHit(EnemyCharacter ally, int placesBehind);
    }

    /// <summary>
    /// Enemy can leap (kinematic arc) to a destination chosen by its brain.
    /// Used by: LeapState
    /// </summary>
    public interface ILeaper
    {
        /// <summary>Current landing point. Call <see cref="RefreshLeapDestination"/> first to re-aim.</summary>
        Vector3 LeapDestination { get; }
        float LeapSpeed { get; }
        float LeapHeight { get; }
        /// <summary>Grounded crouch before leaving the ground. A hit during this cancels the leap.</summary>
        float LeapTelegraph { get; }
        float LeapLandDuration { get; }
        bool LeapHasIFrames { get; }
        /// <summary>How fast (units/s) the landing point may slide while airborne. 0 = fixed landing.</summary>
        float LeapMaxCorrectionSpeed { get; }

        /// <summary>Re-aims the landing point at the target's current position (tracked leaps).</summary>
        void RefreshLeapDestination();
        /// <summary>Checked every telegraph frame. True = the situation changed, call it off.</summary>
        bool ShouldAbortLeap();

        // Callbacks - enemy decides what to do next
        void OnLeapComplete();
        /// <summary>The leap called itself off during the telegraph (<see cref="ShouldAbortLeap"/>).</summary>
        void OnLeapAborted();
        /// <summary>The leap was interrupted before landing (hit during telegraph, forced state).</summary>
        void OnLeapCancelled();
    }

    /// <summary>
    /// Optional patrol feel: pauses at the ends of a leg and an occasional look back.
    /// Used by: PatrolState (falls back to the plain loop without it)
    /// </summary>
    public interface IPatrolRhythm
    {
        /// <summary>Min/max pause at each end of a leg, in seconds.</summary>
        Vector2 PatrolEndPauseRange { get; }
        /// <summary>Chance per leg to stop mid-way and look back.</summary>
        float LookBackChance { get; }
        float LookBackDuration { get; }
        /// <summary>Per-instance multiplier on the pauses so neighbours fall out of phase.</summary>
        float RhythmScale { get; }
    }

    /// <summary>
    /// Enemy takes a beat on first sight before entering combat.
    /// Used by: AlertState
    /// </summary>
    public interface IAlerter
    {
        float AlertDuration { get; }
        void OnAlertComplete();
    }

    /// <summary>
    /// Enemy looks around where it lost the player before giving up.
    /// Used by: SearchState
    /// </summary>
    public interface ISearcher
    {
        /// <summary>Seconds spent facing each way.</summary>
        float SearchLookDuration { get; }
        void OnSearchComplete();
    }

    /// <summary>
    /// Enemy can give ground: backpedal to a distance while facing the player,
    /// then hold for a moment.
    /// Used by: BackOffState
    /// </summary>
    public interface IBackOff
    {
        /// <summary>Axis distance from the player to retreat to. The brain clamps it against the enemy behind.</summary>
        float BackOffTargetDistance { get; }
        float BackOffSpeed { get; }
        /// <summary>Min/max seconds to hold at the retreat distance.</summary>
        Vector2 BackOffHoldRange { get; }
        /// <summary>Give up walking back after this many seconds (blocked by a body or wall).</summary>
        float BackOffTimeout { get; }
        LayerMask BackOffObstacleLayers { get; }

        void OnBackOffComplete();
    }
}
