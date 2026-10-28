using UnityEngine;

namespace junklite
{
    /// <summary>
    /// The player's single stat evaluator: base value from the character asset, plus level-table bonuses
    /// (normally none) plus owned upgrade-tree bonuses, as (base + Σflat) * (1 + Σpercent / 100).
    /// Applies health capacity, armor and the mod slot counts; never mutates shared assets.
    /// Movement, dash and attack-speed stats are read by their owners through <see cref="Evaluate"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerRuntimeStats : MonoBehaviour
    {
        public PlayerProgression Progression { get; private set; }
        public PlayerUpgrades Upgrades { get; private set; }
        public float Armor { get; private set; }
        public Attribute ArmorDisplay { get; private set; }
        /// <summary>Raised after any progression or upgrade change was applied to health, armor and slots.</summary>
        public event System.Action Refreshed;
        private CharacterStats baseStats;
        private AttributeManager attributes;
        private ModManager mods;
        private float baseHealth;
        private float appliedHealthBonus;
        private Attribute boundHealth;

        public void Initialize(CharacterStats stats, AttributeManager attributeManager, PlayerProgression progression,
            PlayerUpgrades upgrades = null)
        {
            if (Progression != null) Progression.Changed -= OnProgressionChanged;
            if (Upgrades != null) Upgrades.Changed -= OnUpgradesChanged;
            baseStats = stats;
            attributes = attributeManager;
            mods = GetComponent<ModManager>();
            baseHealth = stats?.attributes?.Find(a => a != null && a.type == AttributeType.Health)?.maxValue
                ?? attributes?.Health?.Max ?? 100f;
            Progression = progression;
            Upgrades = upgrades != null ? upgrades : PlayerUpgrades.Instance;
            if (Progression != null) Progression.Changed += OnProgressionChanged;
            if (Upgrades != null) Upgrades.Changed += OnUpgradesChanged;
            Refresh(false);
        }

        /// <summary>Full stat formula for any stat. Safe before Initialize (returns the base value).</summary>
        public float Evaluate(PlayerStat stat, float baseValue)
        {
            float flat = 0f, percent = 0f;
            if (Progression != null && Progression.Config != null)
            {
                ProgressionStatBonus bonus = Progression.Config.BonusAt(stat, Progression.Level);
                flat += bonus.flat;
                percent += bonus.percent;
            }
            if (Upgrades != null)
            {
                Upgrades.GetStatBonus(stat, out float upgradeFlat, out float upgradePercent);
                flat += upgradeFlat;
                percent += upgradePercent;
            }
            return Mathf.Max(0f, (baseValue + flat) * (1f + percent * 0.01f));
        }

        /// <summary>Multiplier form of <see cref="Evaluate"/> for stats whose base lives on another component (speeds, cooldowns).</summary>
        public float Multiplier(PlayerStat stat) => Evaluate(stat, 1f);

        public bool HasAbility(PlayerAbility ability) => Upgrades != null && Upgrades.HasAbility(ability);

        public float ScaleDamage(float baseDamage) => Evaluate(PlayerStat.OutgoingDamage, baseDamage);

        private void OnProgressionChanged(ProgressionChange change) => Refresh(change.LeveledUp);
        private void OnUpgradesChanged() => Refresh(false);

        public void Refresh(bool earnedLevel)
        {
            if (attributes == null) return;
            Armor = Evaluate(PlayerStat.Armor, baseStats != null ? baseStats.armor : 0);
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
                float bonus = Evaluate(PlayerStat.MaxHealth, baseHealth) - baseHealth;
                float nextMax = Mathf.Max(1f, health.Max - appliedHealthBonus + bonus);
                LevelUpHealthPolicy policy = Progression != null && Progression.Config != null
                    ? Progression.Config.healthPolicy : LevelUpHealthPolicy.AddCapacity;
                // A bought health node behaves like an earned level: the new capacity is added as current health.
                bool grant = earnedLevel || bonus > appliedHealthBonus;
                ApplyHealthMaximum(health, nextMax, grant, policy);
                appliedHealthBonus = bonus;
            }
            if (mods != null)
            {
                int extraActive = Upgrades != null ? Upgrades.ExtraModSlots(ModSlotKind.Active) : 0;
                int extraPassive = Upgrades != null ? Upgrades.ExtraModSlots(ModSlotKind.Passive) : 0;
                mods.ApplyUnlockedSlots(extraActive, extraPassive);
            }
            Refreshed?.Invoke();
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
            if (Upgrades != null) Upgrades.Changed -= OnUpgradesChanged;
        }
    }
}
