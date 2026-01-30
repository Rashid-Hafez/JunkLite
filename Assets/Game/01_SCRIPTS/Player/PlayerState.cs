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
        public override bool CanMove => IsAlive && !IsStunned && !IsInputLocked;
        public override bool CanJump => IsAlive && !IsStunned && !IsInputLocked;
        public bool CanDash => IsAlive && !IsDashing && !IsStunned && !IsInputLocked;
        public override bool CanAttack => IsAlive && !IsStunned && !IsInputLocked; // No IsAttacking check - WeaponManager handles cooldown
        public bool CanRoll => IsAlive && !IsStunned && !IsRolling && !IsInputLocked;

        // State flags
        public bool IsDashing { get; private set; }
        public bool IsRolling { get; private set; }
        public bool IsInputLocked { get; private set; }

        // Movement states
        public bool IsWallSliding { get; private set; }
        public bool IsWallJumping { get; private set; }
        public bool IsDoubleJumping { get; private set; }

        // Events
        public event Action<bool> OnDashingChanged;
        public event Action<bool> OnRollingChanged;
        public event Action<bool> OnInputLockedChanged;
        public event Action<bool> OnWallSlideChanged;
        public event Action<bool> OnWallJumpChanged;
        public event Action<bool> OnDoubleJumpChanged;
        public event Action<int> OnComboAttackTriggered;

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
            base.ResetForRespawn();
            SetWallSliding(false);
            SetWallJumping(false);
            SetDoubleJumping(false);
            SetInputLocked(false);
        }

        public override void ClearTransient()
        {
            base.ClearTransient();
            SetDashing(false);
            SetRolling(false);
            SetWallJumping(false);
            SetDoubleJumping(false);
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
            }
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
            if (IsInputLocked == locked) return;
            IsInputLocked = locked;
            OnInputLockedChanged?.Invoke(locked);
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
            if (IsDashing) list.Add("Dashing");
            if (IsAttacking) list.Add("Attacking");
            if (IsRolling) list.Add("Rolling");
            if (IsStunned) list.Add("Stunned");
            if (IsInputLocked) list.Add("InputLocked");
            return string.Join(", ", list);
        }
    }
}