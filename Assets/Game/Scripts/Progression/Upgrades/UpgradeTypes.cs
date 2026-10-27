using System;
using UnityEngine;

namespace junklite
{
    /// <summary>Stats the player's runtime layer can scale. Values 0-2 match the old ProgressionStat order (serialized as int).</summary>
    public enum PlayerStat
    {
        MaxHealth = 0,
        OutgoingDamage = 1,
        Armor = 2,
        MoveSpeed = 3,
        JumpForce = 4,
        DoubleJumpForce = 5,
        DashCooldown = 6,
        AttackSpeed = 7,
        FinisherForce = 8,
    }

    /// <summary>Abilities that start locked and are unlocked by an upgrade node.</summary>
    public enum PlayerAbility
    {
        Dash,
        DoubleJump,
        DashStrike,
    }

    public enum UpgradeBranch
    {
        Blades,
        Vitality,
        Jump,
        Dash,
        Mods,
    }

    public enum ModSlotKind
    {
        Active,
        Passive,
    }

    public enum UpgradeEffectKind
    {
        StatBonus,
        UnlockAbility,
        ModSlot,
    }

    /// <summary>
    /// One thing a node does when owned. A plain struct so the default Inspector can edit it;
    /// only the fields that match <see cref="kind"/> are read.
    /// </summary>
    [Serializable]
    public struct UpgradeEffect
    {
        public UpgradeEffectKind kind;

        [Header("Stat Bonus")]
        public PlayerStat stat;
        public float flat;
        [Tooltip("Additive percentage points: 20 means +20%. Negative values are allowed for cooldowns.")]
        public float percent;

        [Header("Unlock Ability")]
        public PlayerAbility ability;

        [Header("Mod Slot")]
        public ModSlotKind slotKind;

        public static UpgradeEffect Stat(PlayerStat stat, float flat = 0f, float percent = 0f) =>
            new() { kind = UpgradeEffectKind.StatBonus, stat = stat, flat = flat, percent = percent };
        public static UpgradeEffect Unlock(PlayerAbility ability) =>
            new() { kind = UpgradeEffectKind.UnlockAbility, ability = ability };
        public static UpgradeEffect Slot(ModSlotKind slotKind) =>
            new() { kind = UpgradeEffectKind.ModSlot, slotKind = slotKind };

        public string Summary => kind switch
        {
            UpgradeEffectKind.StatBonus => $"{stat} {(flat != 0 ? $"{flat:+0.##;-0.##}" : "")}{(flat != 0 && percent != 0 ? ", " : "")}{(percent != 0 ? $"{percent:+0.##;-0.##}%" : "")}",
            UpgradeEffectKind.UnlockAbility => $"Unlock {ability}",
            _ => $"+1 {slotKind} mod slot",
        };

        public bool IsValid(out string error)
        {
            error = null;
            if (!Enum.IsDefined(typeof(UpgradeEffectKind), kind)) { error = "Unknown effect kind."; return false; }
            if (kind == UpgradeEffectKind.StatBonus)
            {
                if (!Enum.IsDefined(typeof(PlayerStat), stat)) { error = "Unknown stat."; return false; }
                if (!float.IsFinite(flat) || !float.IsFinite(percent)) { error = "Stat bonus must be finite."; return false; }
                if (flat == 0f && percent == 0f) { error = "Stat bonus does nothing."; return false; }
                if (percent <= -100f) { error = "A -100% or lower bonus zeroes the stat."; return false; }
            }
            else if (kind == UpgradeEffectKind.UnlockAbility && !Enum.IsDefined(typeof(PlayerAbility), ability))
            { error = "Unknown ability."; return false; }
            else if (kind == UpgradeEffectKind.ModSlot && !Enum.IsDefined(typeof(ModSlotKind), slotKind))
            { error = "Unknown slot kind."; return false; }
            return true;
        }
    }

    /// <summary>
    /// One node of the upgrade tree. Lives inside <see cref="UpgradeTreeConfig"/> (read-only design data).
    /// Prerequisites are node ids so the whole tree is one asset. Nothing here changes at runtime;
    /// ownership is in <see cref="PlayerUpgrades"/>.
    /// </summary>
    [Serializable]
    public sealed class UpgradeNode
    {
        [Tooltip("Stable id written to the player profile when bought. Never change it on a shipped tree.")]
        public string id;
        public string displayName;
        [TextArea(2, 4)] public string description;
        public Sprite icon;
        public UpgradeBranch branch;
        [Min(1), Tooltip("Row in the branch column. Tier 1 is the first node.")]
        public int tier = 1;
        [Min(1)] public int cost = 1;
        [Tooltip("Ids of nodes that must be owned first. Usually the node one tier below.")]
        public string[] prerequisiteIds = Array.Empty<string>();
        [Tooltip("Optional: the arm weapon the player must have acquired (e.g. the blades for the Blades branch).")]
        public WeaponData requiredWeapon;
        public UpgradeEffect[] effects = Array.Empty<UpgradeEffect>();

        public string Title => string.IsNullOrEmpty(displayName) ? id : displayName;
    }
}
