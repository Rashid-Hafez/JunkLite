using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Pure runtime state & capability gatekeeper.
    /// </summary>
    public class CharacterState : MonoBehaviour
    {
        [Header("Optional References")]
        [Tooltip("Optional: If present, used only to read IsAlive and forward OnDeath.")]
        [SerializeField] private AttributeManager attributes;   // optional; safe to leave null

        // ---- State flags ----
        public bool IsGrounded { get; private set; } = true;
        public bool IsMoving { get; private set; }
        public bool IsDashing { get; private set; }
        public bool IsAttacking { get; private set; }
        public bool IsStunned { get; private set; }
        public bool IsVulnerable { get; private set; } = true;
        public bool IsRolling { get; private set; }

        [SerializeField] private bool hasDrone; // backing field for HasDrone property
        /// <summary>
        /// Indicates if the character has unlocked the drone.
        /// Setting this property triggers OnHasDroneChanged event if the value changes.
        /// </summary>
        public bool HasDrone
        {
            get => hasDrone;
            set
            {
                if (hasDrone != value)
                {
                    hasDrone = value;
                    OnHasDroneChanged?.Invoke(hasDrone); // Notify listeners (e.g., SpawnDrone) of change
                }
            }
        }
        /// <summary>
        /// Event invoked whenever HasDrone changes. Used by SpawnDrone to spawn the drone when unlocked.
        /// </summary>
        public event Action<bool> OnHasDroneChanged;
        [SerializeField] private bool droneAttacking; // backing field for HasDrone property

        public bool DroneAttacking
        {
            get => droneAttacking;
            set
            {
                if (droneAttacking != value)
                {
                    droneAttacking = value;
                    OnDroneAttack?.Invoke(); // Notify listeners (e.g., PetDrone) of change
                }
            }
        }
        public event Action OnDroneAttack; // invoked by player, when player presses drone attack button

        // ---- Events ----
        public event Action OnDeath; // forwarded from attributes if available
        public event Action<bool> OnGroundedChanged;
        public event Action<bool> OnMovingChanged;
        public event Action<bool> OnDashingChanged;
        public event Action<bool> OnAttackingChanged;
        public event Action<bool> OnStunnedChanged;
        public event Action<bool> OnVulnerableChanged;
        public event Action<bool> OnRollingChanged;


        // ---- Capabilities ----
        // Alive is read from attributes if available; otherwise assumed true (editor convenience).
        public bool IsAlive => attributes != null ? attributes.IsAlive : true;

        public bool CanMove => IsAlive && !IsStunned;
        public bool CanJump => IsAlive && IsGrounded && !IsStunned;
        public bool CanDash => IsAlive && !IsDashing && !IsStunned;
        public bool CanAttack => IsAlive && !IsAttacking && !IsStunned;
        public bool CanTakeDamage => IsAlive && IsVulnerable; // state layer does not decide damage rules
        public bool CanRoll => IsAlive && !IsStunned && !IsRolling;

        private void Awake()
        {
            // Auto-wire optional attributes reference if left empty.
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
        }

        private void HandleDeathForward()
        {
            OnDeath?.Invoke();
        }

        
        public void ResetForRespawn()
        {
            SetGrounded(false);
            SetMoving(false);
            SetDashing(false);
            SetAttacking(false);
            SetStunned(false);
            SetRolling(false);
        }


        #region State Setters
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

        #region Timed Utilities
        public void ApplyStun(float duration)
        {
            if (duration <= 0f) return;
            SetStunned(true);
            CancelInvoke(nameof(RemoveStun));
            Invoke(nameof(RemoveStun), duration);
        }

        public void ApplyInvulnerability(float seconds)
        {
            CancelInvoke(nameof(EndInvulnerability));
            if (seconds <= 0f)
            {
                // if zero/negative, just ensure vulnerable
                SetVulnerable(true);
                return;
            }

            SetVulnerable(false);
            Invoke(nameof(EndInvulnerability), seconds);
        }
        private void EndInvulnerability() => SetVulnerable(true);
        private void RemoveStun() => SetStunned(false);

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

            GUILayout.BeginArea(new Rect(10, 10, 320, 140));
            GUILayout.Label($"=== {gameObject.name} (State) ===");
            GUILayout.Label($"States: {GetStatusSummary()}");
            GUILayout.Space(6);
            GUILayout.Label("Capabilities:");
            GUILayout.Label($"Move: {CanMove}, Jump: {CanJump}");
            GUILayout.Label($"Attack: {CanAttack}, Dash: {CanDash}");
            GUILayout.EndArea();
        }
        #endregion
    }
}
