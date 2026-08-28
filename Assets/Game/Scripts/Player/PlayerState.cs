using System.Collections.Generic;
using UnityEngine;
using System;

namespace junklite
{
    /// <summary>
    /// Player state and capability checks.
    /// Attack gating is handled by WeaponManager's cooldown system.
    /// </summary>
    public class PlayerState : CharacterState
    {
        // Capability checks
        public override bool CanMove => IsAlive && !IsStunned && !IsActionBlocked(StatusActionBlock.Move) && !IsInputLocked && !IsAttacking && !IsParrying;
        public override bool CanJump => IsAlive && !IsStunned && !IsActionBlocked(StatusActionBlock.Jump) && !IsInputLocked && !IsParrying;
        public bool CanDash => IsAlive && !IsDashing && !IsStunned && !IsActionBlocked(StatusActionBlock.Dash) && !IsInputLocked;
        public override bool CanAttack => IsAlive && !IsStunned && !IsActionBlocked(StatusActionBlock.Attack) && !IsInputLocked && !IsWallSliding && !IsParrying; // No IsAttacking check - WeaponManager handles cooldown
        public bool CanRoll => IsAlive && !IsStunned && !IsActionBlocked(StatusActionBlock.Roll) && !IsRolling && !IsInputLocked;
        public override bool CanTakeDamage => base.CanTakeDamage && damageImmunityLocks.Count == 0;

        // State flags
        public bool IsDashing { get; private set; }
        public bool IsRolling { get; private set; }
        private bool legacyInputLocked;
        private int nextLockId;
        private readonly HashSet<int> inputLocks = new();
        private readonly HashSet<int> damageImmunityLocks = new();
        private StatusEffectHandler statusEffects;
        private StatusEffectSnapshot statusSnapshot = StatusEffectSnapshot.Clear;

        public bool IsInputLocked => legacyInputLocked || inputLocks.Count > 0;

        private bool isInvincible;
        public bool IsInvincible => isInvincible;
        public void SetInvincible(bool value) => isInvincible = value;


        // Movement states
        public bool IsWallSliding { get; private set; }
        public bool IsWallJumping { get; private set; }
        public bool IsDoubleJumping { get; private set; }

        // ledge detection
        public bool IsLedgeDetected { get; private set; }
        public event Action<bool> OnLedgeDetectedChanged;

        // parry state
        public bool IsParrying { get; private set; }
        public event Action<bool> OnParryChanged;

        /// <summary>True when player is allowed to initiate a parry (grounded, alive, not stunned/attacking/etc).</summary>
        public bool CanParry => IsAlive && IsGrounded && !IsFalling && !IsStunned && !IsActionBlocked(StatusActionBlock.Parry) && !IsInputLocked && !IsAttacking;

        /// <summary>How many air attacks have been used this air time.</summary>
        public int AirAttacksUsed { get; private set; }

        /// <summary>Max air attacks allowed this air time (base + mod bonuses).</summary>
        public int MaxAirAttacks { get; private set; } = 1;

        /// <summary>True when airborne and we haven't used all allowed air attacks this jump.</summary>
        public bool CanAirAttack => !IsGrounded && AirAttacksUsed < MaxAirAttacks;

        /// <summary>True when the current attack was initiated as a down attack.</summary>
        public bool IsDownAttackRequested { get; private set; }

        /// <summary>When true, one air attack will be refunded the next time the player double jumps (e.g. after pogo hit).</summary>
        private bool refundAirAttackAfterNextDoubleJump;

        // Events
        public event Action<bool> OnDashingChanged;
        public event Action<bool> OnRollingChanged;
        public event Action<bool> OnInputLockedChanged;
        public event Action<bool> OnWallSlideChanged;
        public event Action<bool> OnWallJumpChanged;
        public event Action<bool> OnDoubleJumpChanged;
        public event Action<int> OnComboAttackTriggered;
        public event Action<string> OnAttackAnimationRequested;
        public event Action OnAttackAnimationComplete;
        public event Action OnAttackAnimationInterrupted;

        // Drone
        [SerializeField] private bool hasDrone;
        public bool HasDrone
        {
            get => hasDrone;
            set { if (hasDrone != value) { hasDrone = value; OnHasDroneChanged?.Invoke(hasDrone); } }
        }
        public event Action<bool> OnHasDroneChanged;

        public override void ResetForRespawn()
        {
            statusEffects?.ClearAllEffects();
            base.ResetForRespawn();
            SetWallSliding(false);
            SetWallJumping(false);
            SetDoubleJumping(false);
            ClearAbilityLocks();
            SetInputLocked(false);
            SetLedgeDetected(false);
            SetParrying(false);
            SetAttacking(false);
            SetDashing(false);
            SetRolling(false);
            AirAttacksUsed = 0;
            refundAirAttackAfterNextDoubleJump = false;
        }

        public void BindStatusEffects(StatusEffectHandler handler)
        {
            statusEffects = handler;
            ApplyStatusEffectSnapshot(handler != null
                ? handler.CurrentSnapshot
                : StatusEffectSnapshot.Clear);
        }

        public void ApplyStatusEffectSnapshot(StatusEffectSnapshot snapshot)
        {
            statusSnapshot = snapshot;
            SetStatusStunned(snapshot.IsCrowdControlled);
        }

        public bool IsActionBlocked(StatusActionBlock action)
        {
            return (statusSnapshot.BlockedActions & action) != 0;
        }

        public override void ApplyStun(float duration)
        {
            if (statusEffects == null)
                statusEffects = GetComponent<StatusEffectHandler>();

            if (statusEffects != null)
            {
                if (duration <= 0f)
                    statusEffects.Remove(StatusEffectType.Stun);
                else
                    statusEffects.ApplyStun(duration, gameObject);
                return;
            }

            base.ApplyStun(duration);
        }

        public override void ClearTransient()
        {
            base.ClearTransient();
            SetDashing(false);
            SetRolling(false);
            SetWallJumping(false);
            SetDoubleJumping(false);
            SetParrying(false);
        }

        // State setters
        public override void SetGrounded(bool grounded)
        {
            if (IsGrounded == grounded) return;
            IsGrounded = grounded;
            InvokeGroundedChanged(grounded);

            if (grounded)
            {
                SetJumping(false);
                SetFalling(false);
                SetWallSliding(false);
                SetWallJumping(false);
                SetDoubleJumping(false);
                AirAttacksUsed = 0;
                refundAirAttackAfterNextDoubleJump = false;
            }
        }

        /// <summary>Schedule one air attack to be refunded the next time the player double jumps. Used by pogo so you get another pogo only after double jump.</summary>
        public void ScheduleRefundAirAttackAfterDoubleJump()
        {
            refundAirAttackAfterNextDoubleJump = true;
        }

        /// <summary>Call when the player has just performed a double jump. If a refund was scheduled (e.g. from pogo hit), refunds one air attack.</summary>
        public void TryRefundAirAttackAfterDoubleJump()
        {
            if (!refundAirAttackAfterNextDoubleJump) return;
            refundAirAttackAfterNextDoubleJump = false;
            AirAttacksUsed = Mathf.Max(0, AirAttacksUsed - 1);
        }

        /// <summary>Call when starting an air attack (e.g. down attack). Consumes one air attack.</summary>
        public void MarkAirAttackUsed()
        {
            AirAttacksUsed = Mathf.Min(AirAttacksUsed + 1, MaxAirAttacks);
        }

        /// <summary>Set maximum air attacks allowed while airborne.</summary>
        public void SetMaxAirAttacks(int maxAirAttacks)
        {
            MaxAirAttacks = Mathf.Max(1, maxAirAttacks);
            AirAttacksUsed = Mathf.Min(AirAttacksUsed, MaxAirAttacks);
        }

        /// <summary>Set when an attack starts to indicate a down-attack input.</summary>
        public void SetDownAttackRequested(bool isDownAttack)
        {
            IsDownAttackRequested = isDownAttack;
        }

        public void SetDashing(bool dashing)
        {
            if (IsDashing == dashing) return;
            IsDashing = dashing;
            SetVulnerable(!dashing);
            OnDashingChanged?.Invoke(dashing);
        }

        public void SetRolling(bool rolling)
        {
            if (IsRolling == rolling) return;
            IsRolling = rolling;
            OnRollingChanged?.Invoke(rolling);
        }

        public void SetInputLocked(bool locked)
        {
            bool wasLocked = IsInputLocked;
            legacyInputLocked = locked;
            NotifyInputLockChanged(wasLocked);
        }

        public IDisposable AcquireInputLock()
        {
            bool wasLocked = IsInputLocked;
            int id = ++nextLockId;
            inputLocks.Add(id);
            NotifyInputLockChanged(wasLocked);
            return new StateLock(() => ReleaseInputLock(id));
        }

        public IDisposable AcquireDamageImmunity()
        {
            int id = ++nextLockId;
            damageImmunityLocks.Add(id);
            return new StateLock(() => damageImmunityLocks.Remove(id));
        }

        private void ReleaseInputLock(int id)
        {
            bool wasLocked = IsInputLocked;
            inputLocks.Remove(id);
            NotifyInputLockChanged(wasLocked);
        }

        private void ClearAbilityLocks()
        {
            bool wasLocked = IsInputLocked;
            inputLocks.Clear();
            damageImmunityLocks.Clear();
            NotifyInputLockChanged(wasLocked);
        }

        private void NotifyInputLockChanged(bool wasLocked)
        {
            if (wasLocked != IsInputLocked)
                OnInputLockedChanged?.Invoke(IsInputLocked);
        }

        private sealed class StateLock : IDisposable
        {
            private Action release;

            public StateLock(Action releaseAction)
            {
                release = releaseAction;
            }

            public void Dispose()
            {
                Action releaseAction = release;
                release = null;
                releaseAction?.Invoke();
            }
        }

        public void SetWallSliding(bool sliding)
        {
            if (IsWallSliding == sliding) return;

            if (sliding)
            {
                if (IsJumping) { IsJumping = false; InvokeJumpStateChanged(false); }
                if (IsFalling) { IsFalling = false; InvokeFallStateChanged(false); }
            }

            IsWallSliding = sliding;
            OnWallSlideChanged?.Invoke(sliding);
        }

        public void SetLedgeDetected(bool detected)
        {
            if (IsLedgeDetected == detected) return;
            IsLedgeDetected = detected;
            OnLedgeDetectedChanged?.Invoke(detected);
        }

        /// <summary>Set whether the player is currently in a parry animation/window.</summary>
        public void SetParrying(bool parrying)
        {
            if (IsParrying == parrying) return;
            IsParrying = parrying;
            OnParryChanged?.Invoke(parrying);
        }

        public void SetWallJumping(bool jumping)
        {
            if (IsWallJumping == jumping) return;

            if (jumping && IsWallSliding)
            {
                IsWallSliding = false;
                OnWallSlideChanged?.Invoke(false);
            }

            IsWallJumping = jumping;
            OnWallJumpChanged?.Invoke(jumping);
        }

        public void SetDoubleJumping(bool jumping)
        {
            if (IsDoubleJumping == jumping) return;
            IsDoubleJumping = jumping;
            OnDoubleJumpChanged?.Invoke(jumping);
        }

        public override void SetJumping(bool jumping)
        {
            if (jumping && IsWallSliding) return;
            if (IsJumping == jumping) return;

            if (jumping && IsFalling)
            {
                IsFalling = false;
                InvokeFallStateChanged(false);
            }

            IsJumping = jumping;
            InvokeJumpStateChanged(jumping);
        }

        /// <summary>
        /// Triggers attack animation. comboIndex: 0+ for side combo, -1 for up/down.
        /// </summary>
        public void TriggerComboAttack(int comboIndex)
        {
            OnComboAttackTriggered?.Invoke(comboIndex);
        }

        /// <summary>Request an attack animation by name (Spine/Animator listeners can respond).</summary>
        public void RequestAttackAnimation(string animationName)
        {
            OnAttackAnimationRequested?.Invoke(animationName);
        }

        /// <summary>Notify that the attack animation completed.</summary>
        public void NotifyAttackAnimationComplete()
        {
            OnAttackAnimationComplete?.Invoke();
        }

        /// <summary>Notify that the attack animation was interrupted.</summary>
        public void NotifyAttackAnimationInterrupted()
        {
            OnAttackAnimationInterrupted?.Invoke();
        }

        public override string GetStatusSummary()
        {
            var list = new List<string> { IsAlive ? "ALIVE" : "DEAD" };
            if (IsGrounded) list.Add("Grounded");
            if (IsMoving) list.Add("Moving");
            if (IsJumping) list.Add("Jumping");
            if (IsFalling) list.Add("Falling");
            if (IsWallSliding) list.Add("WallSliding");
            if (IsWallJumping) list.Add("WallJumping");
            if (IsDoubleJumping) list.Add("DoubleJumping");
            if (IsLedgeDetected) list.Add("LedgeDetected");
            if (IsParrying) list.Add("Parrying");
            if (IsDashing) list.Add("Dashing");
            if (IsAttacking) list.Add("Attacking");
            if (IsRolling) list.Add("Rolling");
            if (IsStunned) list.Add("Stunned");
            if (IsInputLocked) list.Add("InputLocked");
            return string.Join(", ", list);
        }
    }
}
