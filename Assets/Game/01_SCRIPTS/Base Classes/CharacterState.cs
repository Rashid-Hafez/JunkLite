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
    /// Base class for all character states.
    /// </summary>
    public abstract class CharacterState : MonoBehaviour
    {
        [Header("Optional References")]
        [SerializeField] protected AttributeManager attributes;   // optional; safe to leave null


        public bool IsAlive => attributes != null ? attributes.IsAlive : true;

        public virtual bool CanMove => IsAlive && !IsStunned;
        public virtual bool CanJump => IsAlive && !IsStunned;
        public virtual bool CanAttack => IsAlive && !IsAttacking && !IsStunned;
        public bool CanTakeDamage => IsAlive && IsVulnerable;


        // ==== Core State Flags ====
        public bool IsGrounded { get; protected set; } = true;
        public bool IsMoving { get; protected set; }
        public bool IsAttacking { get; protected set; }
        public bool IsStunned { get; protected set; }
        public bool IsVulnerable { get; protected set; } = true;


        public bool IsJumping { get; protected set; }   // ANY upward launch
        public bool IsFalling { get; protected set; }   // ANY downward movement

        // Derived (not stored, auto-calculated)
        public bool IsAirborne => !IsGrounded;

        // ==== Events ====
        public event Action OnDeath;
        public event Action<bool> OnGroundedChanged;
        public event Action<bool> OnMovingChanged;
        public event Action<bool> OnAttackingChanged;
        public event Action<bool> OnStunnedChanged;
        public event Action<bool> OnVulnerableChanged;
        public event Action<bool> OnJumpStateChanged;
        public event Action<bool> OnFallStateChanged;

        // Protected invokers for derived classes
        protected void InvokeGroundedChanged(bool value) => OnGroundedChanged?.Invoke(value);
        protected void InvokeJumpStateChanged(bool value) => OnJumpStateChanged?.Invoke(value);
        protected void InvokeFallStateChanged(bool value) => OnFallStateChanged?.Invoke(value);

        // Timed internals
        protected Coroutine _stunCo, _iFrameCo;

        protected virtual void Awake()
        {
            if (attributes == null) TryGetComponent(out attributes);
        }

        protected virtual void OnEnable()
        {
            if (attributes != null)
                attributes.OnDeath += HandleDeathForward;
        }

        protected virtual void OnDisable()
        {
            if (attributes != null)
                attributes.OnDeath -= HandleDeathForward;

            if (_stunCo != null) StopCoroutine(_stunCo);
            if (_iFrameCo != null) StopCoroutine(_iFrameCo);
        }

        protected void HandleDeathForward() => OnDeath?.Invoke();

        // ===== Reset for Respawn =====
        public virtual void ResetForRespawn()
        {
            ClearTransient();
            SetGrounded(false);
            SetMoving(false);
            SetVulnerable(true);

            // movement flags
            SetJumping(false);
            SetFalling(false);
        }

        // ===== Clear momentary action flags =====
        public virtual void ClearTransient()
        {
            if (_stunCo != null) { StopCoroutine(_stunCo); _stunCo = null; }
            if (_iFrameCo != null) { StopCoroutine(_iFrameCo); _iFrameCo = null; }

            SetAttacking(false);

            // movement transient
            SetJumping(false);
            SetFalling(false);
        }

        // ===========================================================================
        //  STATE SETTERS
        // ===========================================================================

        public virtual void SetGrounded(bool grounded)
        {
            if (IsGrounded == grounded) return;
            IsGrounded = grounded;
            InvokeGroundedChanged(grounded);

            // auto update airborne-based states
            if (grounded)
            {
                SetJumping(false);
                SetFalling(false);
            }
        }

        public void SetMoving(bool moving)
        {
            if (IsMoving == moving) return;
            IsMoving = moving;
            OnMovingChanged?.Invoke(moving);
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

        /// <summary>
        /// Sets jumping state.
        /// </summary>
        public virtual void SetJumping(bool jumping)
        {
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

        /// <summary>
        /// Sets falling state. Cannot set falling to true while jumping (use velocity-based transition).
        /// </summary>
        public virtual void SetFalling(bool falling)
        {
            // Cannot set falling to true while actively jumping
            // The transition from jumping to falling is handled by velocity checks
            if (falling && IsJumping) return;

            if (IsFalling == falling) return;
            IsFalling = falling;
            InvokeFallStateChanged(falling);
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

        public virtual string GetStatusSummary()
        {
            var list = new List<string>();
            list.Add(IsAlive ? "ALIVE" : "DEAD");
            if (IsGrounded) list.Add("Grounded");
            if (IsMoving) list.Add("Moving");
            if (IsJumping) list.Add("Jumping");
            if (IsFalling) list.Add("Falling");
            if (IsAttacking) list.Add("Attacking");
            if (IsStunned) list.Add("Stunned");
            return string.Join(", ", list);
        }

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;

        protected virtual void OnGUI()
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