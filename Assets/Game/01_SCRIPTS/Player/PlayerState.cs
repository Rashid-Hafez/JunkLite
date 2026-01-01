using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.Splines;

namespace junklite
{
    /// <summary>
    /// Player-specific runtime state & capability gatekeeper.
    /// Extended to support Hollow Knight�style movement states.
    /// </summary>
    public class PlayerState : CharacterState
    {
        public override bool CanMove => IsAlive && !IsStunned;
        public override bool CanJump => IsAlive && !IsStunned;
        public bool CanDash => IsAlive && !IsDashing && !IsStunned;
        public override bool CanAttack => IsAlive && !IsAttacking && !IsStunned;
        public bool CanRoll => IsAlive && !IsStunned && !IsRolling;


        // ==== Player State Flags ====
        public bool IsDashing { get; private set; }
        public bool IsRolling { get; private set; }

        // ==== Movement States ====
        public bool IsWallSliding { get; private set; }
        public bool IsWallJumping { get; private set; }
        public bool IsDoubleJumping { get; private set; }

        // ==== Events ====
        public event Action<bool> OnDashingChanged;
        public event Action<bool> OnRollingChanged;

        // Movement state events
        public event Action<bool> OnWallSlideChanged;
        public event Action<bool> OnWallJumpChanged;
        public event Action<bool> OnDoubleJumpChanged;

        // Combo attack event (for animation binding)
        public event Action<int> OnComboAttackTriggered;

        // Drone feature (existing)
        [SerializeField] private bool hasDrone;
        public bool HasDrone
        {
            get => hasDrone;
            set
            {
                if (hasDrone != value)
                {
                    hasDrone = value;
                    OnHasDroneChanged?.Invoke(hasDrone);
                }
            }
        }
        public event Action<bool> OnHasDroneChanged;

        // ===== Reset for Respawn =====
        public override void ResetForRespawn()
        {
            base.ResetForRespawn();

            // player movement flags
            SetWallSliding(false);
            SetWallJumping(false);
            SetDoubleJumping(false);
        }

        // ===== Clear momentary action flags =====
        public override void ClearTransient()
        {
            base.ClearTransient();

            SetDashing(false);
            SetRolling(false);

            // player movement transient
            SetWallJumping(false);
            SetDoubleJumping(false);
        }

        // ===========================================================================
        //  STATE SETTERS
        // ===========================================================================

        public override void SetGrounded(bool grounded)
        {
            if (IsGrounded == grounded) return;
            IsGrounded = grounded;
            InvokeGroundedChanged(grounded);

            // auto update airborne-based states
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
            OnDashingChanged?.Invoke(dashing);
        }

        public void SetRolling(bool rolling)
        {
            if (IsRolling == rolling) return;
            IsRolling = rolling;
            OnRollingChanged?.Invoke(rolling);
        }

        // ===== Player movement states =====

        /// <summary>
        /// Sets wall sliding state. Automatically clears jumping state when sliding starts.
        /// </summary>
        public void SetWallSliding(bool sliding)
        {
            if (IsWallSliding == sliding) return;

            // When starting to wall slide, clear jump states first
            if (sliding)
            {
                if (IsJumping)
                {
                    IsJumping = false;
                    InvokeJumpStateChanged(false);
                }
                if (IsFalling)
                {
                    IsFalling = false;
                    InvokeFallStateChanged(false);
                }
            }

            IsWallSliding = sliding;
            OnWallSlideChanged?.Invoke(sliding);
        }

        /// <summary>
        /// Sets wall jumping state. Automatically clears wall sliding when wall jump starts.
        /// </summary>
        public void SetWallJumping(bool jumping)
        {
            if (IsWallJumping == jumping) return;

            // When starting wall jump, clear wall sliding first
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

        // ===== Combo Attack (for animation) =====

        /// <summary>
        /// Triggers combo attack event for animation binding.
        /// Called by WeaponManager when a side combo attack is performed.
        /// </summary>
        public void TriggerComboAttack(int comboIndex)
        {
            OnComboAttackTriggered?.Invoke(comboIndex);
        }

        /// <summary>
        /// Sets jumping state. Wall sliding prevents jumping from being set to true.
        /// </summary>
        public override void SetJumping(bool jumping)
        {
            // Cannot set jumping to true while wall sliding
            if (jumping && IsWallSliding) return;

            if (IsJumping == jumping) return;

            // When starting to jump, clear falling
            if (jumping && IsFalling)
            {
                IsFalling = false;
                InvokeFallStateChanged(false);
            }

            IsJumping = jumping;
            InvokeJumpStateChanged(jumping);
        }

        // ===========================================================================
        //  DEBUG
        // ===========================================================================

        public override string GetStatusSummary()
        {
            var list = new List<string>();
            list.Add(IsAlive ? "ALIVE" : "DEAD");
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
            return string.Join(", ", list);
        }
    }
}