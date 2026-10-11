using UnityEngine;

namespace junklite
{
    /// <summary>Replaces the progression contribution; never mutates shared character/weapon assets.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerRuntimeStats : MonoBehaviour
    {
        public PlayerProgression Progression { get; private set; }
        public float Armor { get; private set; }
        public Attribute ArmorDisplay { get; private set; }
        private CharacterStats baseStats;
        private AttributeManager attributes;
        private ModManager mods;
        private float baseHealth;
        private float appliedHealthBonus;
        private Attribute boundHealth;

        public void Initialize(CharacterStats stats, AttributeManager attributeManager, PlayerProgression progression)
        {
            if (Progression != null) Progression.Changed -= OnProgressionChanged;
            baseStats = stats;
            attributes = attributeManager;
            mods = GetComponent<ModManager>();
            baseHealth = stats?.attributes?.Find(a => a != null && a.type == AttributeType.Health)?.maxValue
                ?? attributes?.Health?.Max ?? 100f;
            Progression = progression;
            if (Progression != null) Progression.Changed += OnProgressionChanged;
            Refresh(false);
        }

        public float ScaleDamage(float baseDamage) => Progression != null
            ? Progression.Config.Evaluate(ProgressionStat.OutgoingDamage, baseDamage, Progression.Level) : baseDamage;

        private void OnProgressionChanged(ProgressionChange change) => Refresh(change.LeveledUp);

        public void Refresh(bool earnedLevel)
        {
            if (Progression == null || attributes == null) return;
            var profile = Progression.Config;
            Armor = profile.Evaluate(ProgressionStat.Armor, baseStats != null ? baseStats.armor : 0, Progression.Level);
            if (ArmorDisplay == null)
            {
                ArmorDisplay = new Attribute { name = "Armor", type = AttributeType.Armor, maxValue = Armor, startingValue = Armor };
                ArmorDisplay.Initialize();
            }
            else
            {
                ArmorDisplay.SetMax(Armor, false);
                ArmorDisplay.SetToMax();
            }
            Attribute health = attributes.Health;
            if (health != null)
            {
                if (boundHealth != health) { boundHealth = health; appliedHealthBonus = 0f; }
                float bonus = profile.Evaluate(ProgressionStat.MaxHealth, baseHealth, Progression.Level) - baseHealth;
                float nextMax = Mathf.Max(1f, health.Max - appliedHealthBonus + bonus);
                ApplyHealthMaximum(health, nextMax, earnedLevel, profile.healthPolicy);
                appliedHealthBonus = bonus;
            }
            mods?.ApplyProgressionSlots(profile.ActiveSlotsAt(Progression.Level));
        }

        public static void ApplyHealthMaximum(Attribute health, float nextMax, bool earnedLevel, LevelUpHealthPolicy policy)
        {
            float previousMax = health.Max;
            bool alive = health.IsAlive;
            health.SetMax(nextMax, earnedLevel && policy == LevelUpHealthPolicy.KeepPercentage);
            if (!earnedLevel || !alive) return;
            if (policy == LevelUpHealthPolicy.AddCapacity) health.Add(Mathf.Max(0, nextMax - previousMax));
            else if (policy == LevelUpHealthPolicy.FullHeal) health.SetToMax();
        }

        private void OnDestroy()
        {
            if (Progression != null) Progression.Changed -= OnProgressionChanged;
        }
    }
}
