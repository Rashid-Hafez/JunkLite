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
        void OnMeleeWindUp();
        void OnMeleeAttack();
        void OnMeleeComplete();
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
}
