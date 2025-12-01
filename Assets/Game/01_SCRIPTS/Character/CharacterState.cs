using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using UnityEngine.Splines;

namespace junklite
{
    /// <summary>
    /// Pure runtime state & capability gatekeeper.
    /// Zero physics or timing logic lives here.
    /// Extended to support Hollow Knight�style movement states.
    /// </summary>
    public class CharacterState : MonoBehaviour
    {
        [Header("Optional References")]
        [SerializeField] private AttributeManager attributes;   // optional; safe to leave null


        public bool IsAlive => attributes != null ? attributes.IsAlive : true;

        public bool CanMove => IsAlive && !IsStunned;
        public bool CanJump => IsAlive && !IsStunned;
        public bool CanDash => IsAlive && !IsDashing && !IsStunned;
        public bool CanAttack => IsAlive && !IsAttacking && !IsStunned;
        public bool CanTakeDamage => IsAlive && IsVulnerable;
        public bool CanRoll => IsAlive && !IsStunned && !IsRolling;


        // ==== Core State Flags ====
        public bool IsGrounded { get; private set; } = true;
        public bool IsMoving { get; private set; }
        public bool IsDashing { get; private set; }
        public bool IsAttacking { get; private set; }
        public bool IsStunned { get; private set; }
        public bool IsVulnerable { get; private set; } = true;
        public bool IsRolling { get; private set; }

        // ==== New Movement States ====
        public bool IsWallSliding { get; private set; }
        public bool IsWallJumping { get; private set; }
        public bool IsDoubleJumping { get; private set; }

        
        public bool IsJumping { get; private set; }   // ANY upward launch
        public bool IsFalling { get; private set; }   // ANY downward movement

        // Derived (not stored, auto-calculated)
        public bool IsAirborne => !IsGrounded;

        // ==== Events ====
        public event Action OnDeath;
        public event Action<bool> OnGroundedChanged;
        public event Action<bool> OnMovingChanged;
        public event Action<bool> OnDashingChanged;
        public event Action<bool> OnAttackingChanged;
        public event Action<bool> OnStunnedChanged;
        public event Action<bool> OnVulnerableChanged;
        public event Action<bool> OnRollingChanged;

        // New movement state events
        public event Action<bool> OnWallSlideChanged;
        public event Action<bool> OnWallJumpChanged;
        public event Action<bool> OnDoubleJumpChanged;
        public event Action<bool> OnJumpStateChanged;
        public event Action<bool> OnFallStateChanged;

        // Timed internals
        Coroutine _stunCo, _iFrameCo;

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

        private void Awake()
        {
            if (attributes == null) TryGetComponent(out attributes);
        }

        private void OnEnable()
        {
            if (attributes != null)
                attributes.OnDeath += HandleDeathForward;
        }

        private void OnDisable()
        {
            if (attributes != null)
                attributes.OnDeath -= HandleDeathForward;

            if (_stunCo != null) StopCoroutine(_stunCo);
            if (_iFrameCo != null) StopCoroutine(_iFrameCo);
        }

        private void HandleDeathForward() => OnDeath?.Invoke();

        // ===== Reset for Respawn =====
        public void ResetForRespawn()
        {
            ClearTransient();
            SetGrounded(false);
            SetMoving(false);
            SetVulnerable(true);

            // movement flags
            SetWallSliding(false);
            SetWallJumping(false);
            SetDoubleJumping(false);
            SetJumping(false);
            SetFalling(false);
        }

        // ===== Clear momentary action flags =====
        public void ClearTransient()
        {
            if (_stunCo != null) { StopCoroutine(_stunCo); _stunCo = null; }
            if (_iFrameCo != null) { StopCoroutine(_iFrameCo); _iFrameCo = null; }

            SetDashing(false);
            SetAttacking(false);
            SetRolling(false);

            // movement transient
            SetWallJumping(false);
            SetDoubleJumping(false);
            SetJumping(false);
            SetFalling(false);
        }

        // ===========================================================================
        //  STATE SETTERS (only allow state updates from PlayerCharacter / Controller)
        // ===========================================================================

        public void SetGrounded(bool grounded)
        {
            if (IsGrounded == grounded) return;
            IsGrounded = grounded;
            OnGroundedChanged?.Invoke(grounded);

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

        public void SetMoving(bool moving)
        {
            if (IsMoving == moving) return;
            IsMoving = moving;
            OnMovingChanged?.Invoke(moving);
        }

        public void SetDashing(bool dashing)
        {
            if (IsDashing == dashing) return;
            IsDashing = dashing;
            OnDashingChanged?.Invoke(dashing);
        }

        public void SetAttacking(bool attacking)
        {
            if (IsAttacking == attacking) return;
            IsAttacking = attacking;
            OnAttackingChanged?.Invoke(attacking);
        }

        public void SetStunned(bool stunned)
        {
            if (IsStunned == stunned) return;
            IsStunned = stunned;
            OnStunnedChanged?.Invoke(stunned);
        }

        public void SetVulnerable(bool vulnerable)
        {
            if (IsVulnerable == vulnerable) return;
            IsVulnerable = vulnerable;
            OnVulnerableChanged?.Invoke(vulnerable);
        }

        public void SetRolling(bool rolling)
        {
            if (IsRolling == rolling) return;
            IsRolling = rolling;
            OnRollingChanged?.Invoke(rolling);
        }

        // ===== NEW movement states =====

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
                    OnJumpStateChanged?.Invoke(false);
                }
                if (IsFalling)
                {
                    IsFalling = false;
                    OnFallStateChanged?.Invoke(false);
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

        /// <summary>
        /// Sets jumping state. Wall sliding prevents jumping from being set to true.
        /// </summary>
        public void SetJumping(bool jumping)
        {
            // Cannot set jumping to true while wall sliding
            if (jumping && IsWallSliding) return;
            
            if (IsJumping == jumping) return;
            
            // When starting to jump, clear falling
            if (jumping && IsFalling)
            {
                IsFalling = false;
                OnFallStateChanged?.Invoke(false);
            }
            
            IsJumping = jumping;
            OnJumpStateChanged?.Invoke(jumping);
        }

        /// <summary>
        /// Sets falling state. Cannot set falling to true while jumping (use velocity-based transition).
        /// </summary>
        public void SetFalling(bool falling)
        {
            // Cannot set falling to true while actively jumping
            // The transition from jumping to falling is handled by velocity checks
            if (falling && IsJumping) return;
            
            if (IsFalling == falling) return;
            IsFalling = falling;
            OnFallStateChanged?.Invoke(falling);
        }

        // ===========================================================================
        //  TIMED UTILS
        // ===========================================================================

        public void ApplyStun(float duration)
        {
            if (duration <= 0f) { SetStunned(false); return; }
            if (_stunCo != null) StopCoroutine(_stunCo);
            _stunCo = StartCoroutine(StunFor(duration));
        }

        IEnumerator StunFor(float t)
        {
            SetStunned(true);
            yield return new WaitForSeconds(t);
            SetStunned(false);
            _stunCo = null;
        }

        public void ApplyInvulnerability(float seconds)
        {
            if (_iFrameCo != null) StopCoroutine(_iFrameCo);

            if (seconds <= 0f)
            {
                SetVulnerable(true);
                _iFrameCo = null;
                return;
            }

            _iFrameCo = StartCoroutine(InvulnFor(seconds));
        }

        IEnumerator InvulnFor(float t)
        {
            SetVulnerable(false);
            yield return new WaitForSeconds(t);
            SetVulnerable(true);
            _iFrameCo = null;
        }

        // ===========================================================================
        //  DEBUG
        // ===========================================================================

        public string GetStatusSummary()
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

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;

        private void OnGUI()
        {
            if (!showDebugInfo) return;

            GUILayout.BeginArea(new Rect(10, 10, 320, 200));
            GUILayout.Label($"=== {gameObject.name} (State) ===");
            GUILayout.Label($"States: {GetStatusSummary()}");
            GUILayout.Label($"Airborne: {IsAirborne}");
            GUILayout.EndArea();
        }
    }
}
