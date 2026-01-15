using UnityEngine;

namespace junklite
{
    // ============================================================
    // CAPABILITY INTERFACES
    // Enemies implement these to declare what they can do.
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
        float MeleeAttackDuration { get; }
        float AttackCooldown { get; }  // ADD THIS - time between slashes
        float MeleeDamage { get; }
        Vector2 MeleeKnockback { get; }
        Hitbox MeleeHitbox { get; }
        GameObject MeleeVFXPrefab { get; }

        // Callback when melee attack completes
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
}