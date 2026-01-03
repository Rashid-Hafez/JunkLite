using UnityEngine;
using System.Collections.Generic;

namespace junklite
{
    /// <summary>
    /// Manages all status effects on an enemy.
    /// </summary>
    public class StatusEffectHandler : MonoBehaviour
    {
        [Header("VFX References (Disabled by Default)")]
        [SerializeField] private GameObject burnVFX;
        [SerializeField] private GameObject poisonVFX;
        [SerializeField] private GameObject bleedVFX;
        [SerializeField] private GameObject electricVFX;

        [Header("Debug")]
        [SerializeField] private bool showDebugLogs = false;

        // Active effects - one per type (refresh stacking)
        private Dictionary<StatusEffectType, StatusEffectInstance> activeEffects = new();

        // Cached references
        private EnemyCharacter enemy;
        private EnemyMovement movement;
        private float originalSpeed;
        private bool hasSpeedModifier;

        // Events for UI/Audio hooks
        public event System.Action<StatusEffectType> OnEffectApplied;
        public event System.Action<StatusEffectType> OnEffectRemoved;
        public event System.Action<StatusEffectType, float> OnEffectTick; // type, damage dealt

        // Public accessors
        public bool HasEffect(StatusEffectType type) => activeEffects.ContainsKey(type);
        public int ActiveEffectCount => activeEffects.Count;

        private void Awake()
        {
            enemy = GetComponent<EnemyCharacter>();
            movement = GetComponent<EnemyMovement>();

            if (enemy == null)
            {
                Debug.LogError($"StatusEffectHandler on {name} requires EnemyCharacter component!");
                enabled = false;
            }
        }

        private void Start()
        {
            // Cache original speed
            if (movement != null)
                originalSpeed = movement.MoveSpeed;
        }

        private void Update()
        {
            if (!enemy.IsAlive)
            {
                ClearAllEffects();
                return;
            }

            TickEffects();
        }

        /// <summary>
        /// Apply a status effect. If same type exists, refreshes duration.
        /// </summary>
        public void Apply(StatusEffectInstance effect)
        {
            if (effect == null || effect.Type == StatusEffectType.None)
                return;

            if (!enemy.IsAlive)
                return;

            // Check if effect of this type already exists
            if (activeEffects.TryGetValue(effect.Type, out var existing))
            {
                // Refresh existing effect
                existing.RefreshWith(effect);

                if (showDebugLogs)
                    Debug.Log($"[StatusEffect] Refreshed {effect.Type} on {name}");
            }
            else
            {
                // Add new effect
                activeEffects[effect.Type] = effect;

                // Enable VFX
                SetVFXActive(effect.Type, true);

                // Apply speed modifier if any
                ApplySpeedModifier();

                // Fire event
                OnEffectApplied?.Invoke(effect.Type);

                if (showDebugLogs)
                    Debug.Log($"[StatusEffect] Applied {effect.Type} to {name} (duration: {effect.Duration}s)");
            }
        }

        /// <summary>
        /// Remove a specific effect type.
        /// </summary>
        public void Remove(StatusEffectType type)
        {
            if (!activeEffects.ContainsKey(type))
                return;

            activeEffects.Remove(type);

            // Disable VFX
            SetVFXActive(type, false);

            // Recalculate speed modifiers
            ApplySpeedModifier();

            // Fire event
            OnEffectRemoved?.Invoke(type);

            if (showDebugLogs)
                Debug.Log($"[StatusEffect] Removed {type} from {name}");
        }

        /// <summary>
        /// Clear all active effects.
        /// </summary>
        public void ClearAllEffects()
        {
            var types = new List<StatusEffectType>(activeEffects.Keys);
            foreach (var type in types)
            {
                Remove(type);
            }
        }

        /// <summary>
        /// Get remaining duration for an effect type.
        /// </summary>
        public float GetRemainingDuration(StatusEffectType type)
        {
            if (activeEffects.TryGetValue(type, out var effect))
                return effect.RemainingDuration;
            return 0f;
        }

        private void TickEffects()
        {
            if (activeEffects.Count == 0)
                return;

            float deltaTime = Time.deltaTime;
            var expiredEffects = new List<StatusEffectType>();

            foreach (var kvp in activeEffects)
            {
                var effect = kvp.Value;

                // Update duration
                effect.RemainingDuration -= deltaTime;

                // Update tick timer
                effect.TickTimer += deltaTime;

                // Check if it's time to tick damage
                if (effect.TickTimer >= effect.TickInterval)
                {
                    effect.TickTimer -= effect.TickInterval;
                    ApplyTickDamage(effect);
                }

                // Check if expired
                if (effect.IsExpired)
                {
                    expiredEffects.Add(kvp.Key);
                }
            }

            // Remove expired effects
            foreach (var type in expiredEffects)
            {
                Remove(type);
            }
        }

        private void ApplyTickDamage(StatusEffectInstance effect)
        {
            if (!enemy.IsAlive)
                return;

            // Create damage info - goes through proper damage system!
            var damageInfo = new DamageInfo(
                effect.DamagePerTick,
                effect.Source,
                effect.DamageType,
                Vector2.zero // No knockback for DoT
            );

            // Apply damage through the enemy's damage system
            enemy.TakeDamage(damageInfo);

            // Fire tick event
            OnEffectTick?.Invoke(effect.Type, effect.DamagePerTick);

            if (showDebugLogs)
                Debug.Log($"[StatusEffect] {effect.Type} tick: {effect.DamagePerTick} damage to {name}");
        }

        private void ApplySpeedModifier()
        {
            if (movement == null)
                return;

            // Find the strongest speed modifier among active effects
            float lowestModifier = 1f;

            foreach (var effect in activeEffects.Values)
            {
                if (effect.SpeedModifier < lowestModifier)
                    lowestModifier = effect.SpeedModifier;
            }

            // Apply the modifier
            if (lowestModifier < 1f)
            {
                movement.MoveSpeed = originalSpeed * lowestModifier;
                hasSpeedModifier = true;
            }
            else if (hasSpeedModifier)
            {
                // Restore original speed
                movement.MoveSpeed = originalSpeed;
                hasSpeedModifier = false;
            }
        }

        private void SetVFXActive(StatusEffectType type, bool active)
        {
            GameObject vfx = GetVFXForType(type);
            if (vfx != null)
                vfx.SetActive(active);
        }

        private GameObject GetVFXForType(StatusEffectType type)
        {
            return type switch
            {
                StatusEffectType.Burn => burnVFX,
                StatusEffectType.Poison => poisonVFX,
                StatusEffectType.Bleed => bleedVFX,
                StatusEffectType.Electric => electricVFX,
                _ => null
            };
        }

        private void OnDisable()
        {
            // Clean up VFX when disabled
            foreach (var type in activeEffects.Keys)
            {
                SetVFXActive(type, false);
            }
        }

        private void OnDestroy()
        {
            ClearAllEffects();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Auto-disable VFX references in editor
            if (burnVFX != null && burnVFX.activeSelf) burnVFX.SetActive(false);
            if (poisonVFX != null && poisonVFX.activeSelf) poisonVFX.SetActive(false);
            if (bleedVFX != null && bleedVFX.activeSelf) bleedVFX.SetActive(false);
            if (electricVFX != null && electricVFX.activeSelf) electricVFX.SetActive(false);
        }
#endif
    }


    public enum StatusEffectType
    {
        None,
        Burn,
        Poison,
        Bleed,
        Electric
    }
}