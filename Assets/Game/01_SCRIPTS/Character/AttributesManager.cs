using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    public class AttributeManager : MonoBehaviour
    {
        [Header("Runtime Attributes")]
        [SerializeField] private List<Attribute> runtimeAttributes = new List<Attribute>();

        // Events
        public event Action OnDeath;

        // Properties
        public bool IsAlive
        {
            get
            {
                var health = GetAttribute("Health");
                return health?.IsAlive ?? true;
            }
        }

        /// <summary>Initialize from character stats</summary>
        public void Initialize(CharacterStats stats)
        {
            runtimeAttributes.Clear();

            if (stats?.attributes != null)
            {
                foreach (var statAttr in stats.attributes)
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

                    runtimeAttributes.Add(runtimeAttr);
                }
            }
        }

        private void Update()
        {
            // Update attribute regeneration
            foreach (var attr in runtimeAttributes)
            {
                attr.UpdateRegen(Time.deltaTime);
            }
        }

        /// <summary>Get runtime attribute by name</summary>
        public Attribute GetAttribute(string name)
        {
            return runtimeAttributes.Find(attr =>
                attr.name.Equals(name, System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Simple damage handling</summary>
        public void TakeDamage(float damage, float armor = 0f)
        {
            var health = GetAttribute("Health");
            if (health != null)
            {
                float finalDamage = Mathf.Max(1f, damage - armor);
                health.Remove(finalDamage);
            }
        }

        /// <summary>Simple healing</summary>
        public void Heal(float amount)
        {
            GetAttribute("Health")?.Add(amount);
        }

        // Convenience properties for common attributes
        public Attribute Health => GetAttribute("Health");
        public Attribute Mana => GetAttribute("Mana");
        public Attribute Stamina => GetAttribute("Stamina");

        /// <summary>Get all runtime attributes (for UI binding)</summary>
        public List<Attribute> GetAllAttributes()
        {
            return runtimeAttributes;
        }
    }
}