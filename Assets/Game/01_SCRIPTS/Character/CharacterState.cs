using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

namespace junklite
{
    /// <summary>
    /// Pure runtime state & capability gatekeeper.
    /// No motion/ability timing lives here.
    /// </summary>
    public class CharacterState : MonoBehaviour
    {
        [Header("Optional References")]
        [Tooltip("Optional: If present, used only to read IsAlive and forward OnDeath.")]
        [SerializeField] private AttributeManager attributes;   // optional; safe to leave null

        // ---- State flags (single source of truth for gates) ----
        public bool IsGrounded { get; private set; } = true;
        public bool IsMoving { get; private set; }
        public bool IsDashing { get; private set; }
        public bool IsAttacking { get; private set; }
        public bool IsStunned { get; private set; }
        public bool IsVulnerable { get; private set; } = true;
        public bool IsRolling { get; private set; }

        // ---- Events ----
        public event Action OnDeath; // forwarded from attributes if available
        public event Action<bool> OnGroundedChanged;
        public event Action<bool> OnMovingChanged;
        public event Action<bool> OnDashingChanged;
        public event Action<bool> OnAttackingChanged;
        public event Action<bool> OnStunnedChanged;
        public event Action<bool> OnVulnerableChanged;
        public event Action<bool> OnRollingChanged;

        // ---- Capabilities (derived gates) ----
        // Alive is read from attributes if available; otherwise assumed true (editor convenience).
        public bool IsAlive => attributes != null ? attributes.IsAlive : true;

        public bool CanMove => IsAlive && !IsStunned;
        public bool CanJump => IsAlive && IsGrounded && !IsStunned;
        public bool CanDash => IsAlive && !IsDashing && !IsStunned;
        public bool CanAttack => IsAlive && !IsAttacking && !IsStunned;
        public bool CanTakeDamage => IsAlive && IsVulnerable;
        public bool CanRoll => IsAlive && !IsStunned && !IsRolling;

        // ---- Internals (coroutines) ----
        Coroutine _stunCo, _iFrameCo;

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

            // stop timers to avoid lingering flags after disable/destroy
            if (_stunCo != null) StopCoroutine(_stunCo);
            if (_iFrameCo != null) StopCoroutine(_iFrameCo);
        }

        private void HandleDeathForward() => OnDeath?.Invoke();

        public void ResetForRespawn()
        {
            ClearTransient();
            SetGrounded(false);
            SetMoving(false);
            SetVulnerable(true);
        }

        /// <summary>Clears momentary action flags (dash/attack/roll/stun).</summary>
        public void ClearTransient()
        {
            if (_stunCo != null) { StopCoroutine(_stunCo); _stunCo = null; }
            if (_iFrameCo != null) { StopCoroutine(_iFrameCo); _iFrameCo = null; }

            SetDashing(false);
            SetAttacking(false);
            SetRolling(false);
            SetStunned(false);
        }

        #region State Setters (single-writer, event-synced)
        public void SetGrounded(bool grounded)
        {
            if (IsGrounded == grounded) return;
            IsGrounded = grounded;
            OnGroundedChanged?.Invoke(grounded);
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

        public void SetCanTakeDamage(bool canTake) => SetVulnerable(canTake);

        public void SetRolling(bool rolling)
        {
            if (IsRolling == rolling) return;
            IsRolling = rolling;
            OnRollingChanged?.Invoke(rolling);
        }
        #endregion

        #region Timed Utilities (coroutine-based)
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
        #endregion

        // ---- Debug helpers ----
        public string GetStatusSummary()
        {
            var states = new List<string>();
            states.Add(IsAlive ? "ALIVE" : "DEAD");
            if (IsGrounded) states.Add("Grounded");
            if (IsMoving) states.Add("Moving");
            if (IsDashing) states.Add("Dashing");
            if (IsAttacking) states.Add("Attacking");
            if (IsStunned) states.Add("Stunned");
            if (IsRolling) states.Add("Rolling");
            return string.Join(", ", states);
        }

        #region Debug GUI
        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;

        private void OnGUI()
        {
            if (!showDebugInfo) return;

            GUILayout.BeginArea(new Rect(10, 10, 320, 160));
            GUILayout.Label($"=== {gameObject.name} (State) ===");
            GUILayout.Label($"States: {GetStatusSummary()}");
            GUILayout.Space(6);
            GUILayout.Label("Capabilities:");
            GUILayout.Label($"Move: {CanMove}, Jump: {CanJump}");
            GUILayout.Label($"Attack: {CanAttack}, Dash: {CanDash}, Roll: {CanRoll}");
            GUILayout.EndArea();
        }
        #endregion
    }
}
