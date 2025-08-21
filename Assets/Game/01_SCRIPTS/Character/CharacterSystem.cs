

using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    public class CharacterSystem : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private CharacterStats baseStats;

        // Runtime attributes
        private Dictionary<string, Attribute> runtimeAttributes = new Dictionary<string, Attribute>();

        // State properties
        public bool IsGrounded { get; private set; } = true;
        public bool IsMoving { get; private set; }
        public bool IsDashing { get; private set; }
        public bool IsAttacking { get; private set; }
        public bool IsStunned { get; private set; }

        // Events
        public event Action OnDeath;
        public event Action<bool> OnGroundedChanged;
        public event Action<bool> OnMovingChanged;
        public event Action<bool> OnDashingChanged;
        public event Action<bool> OnAttackingChanged;
        public event Action<bool> OnStunnedChanged;

        // Properties
        public bool IsAlive => GetAttribute("Health")?.IsAlive ?? true;
        public CharacterStats Stats => baseStats;

        // Convenience properties for common attributes
        public Attribute Health => GetAttribute("Health");
        public Attribute Mana => GetAttribute("Mana");
        public Attribute Stamina => GetAttribute("Stamina");

        // Capability checks
        public bool CanMove => IsAlive && !IsStunned;
        public bool CanJump => IsAlive && IsGrounded && !IsStunned;
        public bool CanDash => IsAlive && !IsDashing && !IsStunned;
        public bool CanAttack => IsAlive && !IsAttacking && !IsStunned;
        public bool CanTakeDamage => IsAlive;

        public void Initialize(CharacterStats stats)
        {
            baseStats = stats;

            // Copy and initialize attributes
            runtimeAttributes.Clear();
            if (baseStats?.attributes != null)
            {
                foreach (var statAttr in baseStats.attributes)
                {
                    var runtimeAttr = new Attribute
                    {
                        name = statAttr.name,
                        type = statAttr.type,
                        maxValue = statAttr.maxValue,
                        startingValue = statAttr.startingValue,
                        hasRegeneration = statAttr.hasRegeneration,
                        regenRate = statAttr.regenRate,
                        regenDelay = statAttr.regenDelay
                    };

                    runtimeAttr.Initialize();

                    // Subscribe to death event for health attributes
                    if (runtimeAttr.type == AttributeType.Health)
                    {
                        runtimeAttr.OnDeath += () => OnDeath?.Invoke();
                    }

                    runtimeAttributes[runtimeAttr.name] = runtimeAttr;
                }
            }
        }

        private void Update()
        {
            // Update attribute regeneration
            foreach (var attr in runtimeAttributes.Values)
            {
                attr.UpdateRegen(Time.deltaTime);
            }
        }

        /// <summary>Get runtime attribute by name</summary>
        public Attribute GetAttribute(string name)
        {
            runtimeAttributes.TryGetValue(name, out Attribute attr);
            return attr;
        }

        /// <summary>Core damage calculation and application - called internally by CharacterBase</summary>
        internal void ApplyDamageToHealth(DamageInfo info)
        {
            var health = GetAttribute("Health");
            if (health != null)
            {
                float finalDamage = Mathf.Max(1f, info.Amount - baseStats.armor);
                health.Remove(finalDamage);

                Debug.Log($"{baseStats.characterName} took {finalDamage} damage! Health: {health.Current}/{health.Max}");
            }
        }

        /// <summary>Simple healing</summary>
        public void Heal(float amount)
        {
            GetAttribute("Health")?.Add(amount);
        }

        // State management methods
        public void SetGrounded(bool grounded)
        {
            if (IsGrounded != grounded)
            {
                IsGrounded = grounded;
                OnGroundedChanged?.Invoke(grounded);
            }
        }

        public void SetMoving(bool moving)
        {
            if (IsMoving != moving)
            {
                IsMoving = moving;
                OnMovingChanged?.Invoke(moving);
            }
        }

        public void SetDashing(bool dashing)
        {
            if (IsDashing != dashing)
            {
                IsDashing = dashing;
                OnDashingChanged?.Invoke(dashing);
            }
        }

        public void SetAttacking(bool attacking)
        {
            if (IsAttacking != attacking)
            {
                IsAttacking = attacking;
                OnAttackingChanged?.Invoke(attacking);
            }
        }

        public void SetStunned(bool stunned)
        {
            if (IsStunned != stunned)
            {
                IsStunned = stunned;
                OnStunnedChanged?.Invoke(stunned);
            }
        }

        // Utility methods
        public void ApplyStun(float duration)
        {
            SetStunned(true);
            CancelInvoke(nameof(RemoveStun));
            Invoke(nameof(RemoveStun), duration);
        }

        private void RemoveStun()
        {
            SetStunned(false);
        }

        // Utility method for debug
        public string GetStatusSummary()
        {
            var states = new List<string>();

            if (!IsAlive) states.Add("DEAD");
            else states.Add("ALIVE");

            if (IsGrounded) states.Add("Grounded");
            if (IsMoving) states.Add("Moving");
            if (IsDashing) states.Add("Dashing");
            if (IsAttacking) states.Add("Attacking");
            if (IsStunned) states.Add("Stunned");

            return string.Join(", ", states);
        }

        #region Debug
        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;

        private void OnGUI()
        {
            if (!showDebugInfo) return;

            GUILayout.BeginArea(new Rect(10, 10, 300, 200));
            GUILayout.Label($"=== {gameObject.name} ===");

            // Show all attributes
            foreach (var attr in runtimeAttributes.Values)
            {
                GUILayout.Label($"{attr.name}: {attr.Current:F0}/{attr.Max:F0} ({attr.Percentage:P0})");
            }

            GUILayout.Space(10);
            GUILayout.Label($"States: {GetStatusSummary()}");

            GUILayout.Space(10);
            GUILayout.Label("Capabilities:");
            GUILayout.Label($"Move: {CanMove}, Jump: {CanJump}");
            GUILayout.Label($"Attack: {CanAttack}, Dash: {CanDash}");

            GUILayout.EndArea();
        }
        #endregion
    }
}