using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Pure attribute container & regen updater.
    /// </summary>
    public class AttributeManager : MonoBehaviour
    {
        [Header("Config (optional)")]
        [Tooltip("If assigned, Initialize(stats) will auto-run in Start().")]
        [SerializeField] private CharacterStats stats;

        [Header("Runtime Attributes (read-only)")]
        [SerializeField] private List<Attribute> allRuntimeAttributes = new List<Attribute>(); // for inspector/UI
        private readonly Dictionary<AttributeType, Attribute> map = new Dictionary<AttributeType, Attribute>();
        private CharacterStats initializedSource;
        private bool isInitialized;

        // ---- Events ----
        public event Action OnDeath;

        // ---- Properties ----
        public bool IsAlive
        {
            get
            {
                if (map.TryGetValue(AttributeType.Health, out var health))
                    return health.IsAlive;
                return true; // if no health attribute defined, treat as alive (editor convenience)
            }
        }

        public Attribute Health => Get(AttributeType.Health);
        public bool IsInitialized => isInitialized;

        private void Start()
        {
            // Optional auto-initialize from serialized stats
            if (stats != null && map.Count == 0)
                Initialize(stats);
        }

        /// <summary>Builds runtime attributes from a CharacterStats ScriptableObject.</summary>
        public void Initialize(CharacterStats source)
        {
            if (isInitialized && initializedSource == source)
                return;

            UnhookHealthDeath();
            map.Clear();
            allRuntimeAttributes.Clear();

            if (source?.attributes != null)
            {
                foreach (var s in source.attributes)
                {
                    var runtime = new Attribute
                    {
                        name = s.name,
                        type = s.type,
                        maxValue = s.maxValue,
                        startingValue = s.startingValue,
                        hasRegeneration = s.hasRegeneration,
                        regenRate = s.regenRate,
                        regenDelay = s.regenDelay
                    };

                    runtime.Initialize();

                    // Track
                    map[s.type] = runtime;
                    allRuntimeAttributes.Add(runtime);
                }
            }

            initializedSource = source;
            isInitialized = true;
            HookHealthDeath();
        }

        private void Update()
        {
            // Per-frame regeneration tick
            var dt = Time.deltaTime;
            for (int i = 0; i < allRuntimeAttributes.Count; i++)
                allRuntimeAttributes[i].UpdateRegen(dt);
        }

        /// <summary>Typed getter. Returns null if the attribute isn't defined in stats.</summary>
        public Attribute Get(AttributeType type)
        {
            map.TryGetValue(type, out var attr);
            return attr;
        }

        /// <summary>Legacy helper for UI bindings that still pass names (case-insensitive).</summary>
        public Attribute GetAttribute(string name)
        {
            for (int i = 0; i < allRuntimeAttributes.Count; i++)
            {
                var a = allRuntimeAttributes[i];
                if (a.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return a;
            }
            return null;
        }

        #region Helper Methods
        /// <summary>Convenience heal for health only (optional).</summary>
        public void Heal(float amount)
        {
            if (amount <= 0f) return;
            Health?.Add(amount);
        }

        /// <summary>Applies clamped health damage and returns the amount actually removed.</summary>
        public float ApplyDamage(float amount)
        {
            var health = Health;
            if (health == null || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount))
                return 0f;

            float before = health.Current;
            health.Remove(amount);
            return Mathf.Max(0f, before - health.Current);
        }

        public void RestoreAllToMax()
        {
            for (int i = 0; i < allRuntimeAttributes.Count; i++)
                allRuntimeAttributes[i].SetToMax();
        }

        public void RestoreHealthToMax() => Health?.SetToMax();

        #endregion

        /// <summary>Returns a live list for UI binding (do not modify entries externally).</summary>
        public List<Attribute> GetAllAttributes() => allRuntimeAttributes;

        // ---- Internal: wire/unwire health death ----
        private void HookHealthDeath()
        {
            if (map.TryGetValue(AttributeType.Health, out var health))
                health.OnDeath += RaiseDeath;
        }

        private void UnhookHealthDeath()
        {
            if (map.TryGetValue(AttributeType.Health, out var health))
                health.OnDeath -= RaiseDeath;
        }

        private void RaiseDeath() => OnDeath?.Invoke();

        private void OnDestroy()
        {
            UnhookHealthDeath();
        }
    }
}
