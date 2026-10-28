using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// The whole upgrade tree as one read-only asset in Resources. Nodes are embedded so designers edit
    /// one file and nothing references nodes by GUID. Runtime ownership lives in <see cref="PlayerUpgrades"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Junklite/Progression/Upgrade Tree", fileName = "UpgradeTreeConfig")]
    public sealed class UpgradeTreeConfig : ScriptableObject
    {
        public const string ResourceName = "UpgradeTreeConfig";

        [Tooltip("Upgrade points granted per earned level.")]
        [Min(0)] public int pointsPerLevel = 1;
        [Tooltip("Upgrade points every profile starts with, on top of level and bonus points. Not saved: changing it updates existing saves.")]
        [Min(0)] public int startingPoints;
        [Tooltip("Highest tier that can be bought in this build. Higher tiers show as Currently Unavailable. 0 = every tier.")]
        [Min(0)] public int maxAvailableTier;

        /// <summary>True when this build lets the player buy nodes of <paramref name="tier"/>.</summary>
        public bool IsTierAvailable(int tier) => maxAvailableTier <= 0 || tier <= maxAvailableTier;
        public List<UpgradeNode> nodes = new();

        private Dictionary<string, UpgradeNode> byId;

        public UpgradeNode Find(string id)
        {
            if (string.IsNullOrEmpty(id) || nodes == null) return null;
            if (byId == null || byId.Count != nodes.Count) RebuildIndex();
            return byId.TryGetValue(id, out UpgradeNode node) ? node : null;
        }

        /// <summary>Nodes of one branch sorted by tier (then by list order).</summary>
        public void GetBranch(UpgradeBranch branch, List<UpgradeNode> results)
        {
            results.Clear();
            if (nodes == null) return;
            foreach (UpgradeNode node in nodes)
                if (node != null && node.branch == branch) results.Add(node);
            results.Sort((a, b) => a.tier.CompareTo(b.tier));
        }

        public int MaxTier
        {
            get
            {
                int max = 0;
                if (nodes != null) foreach (UpgradeNode node in nodes) if (node != null) max = Mathf.Max(max, node.tier);
                return max;
            }
        }

        public bool Validate(out string error)
        {
            if (pointsPerLevel < 0) { error = "Points per level cannot be negative."; return false; }
            if (nodes == null || nodes.Count == 0) { error = "The tree has no nodes. Run Tools > JunkLite > Progression > Install Upgrade Tree."; return false; }
            var ids = new HashSet<string>();
            foreach (UpgradeNode node in nodes)
            {
                if (node == null) { error = "A node entry is empty."; return false; }
                if (string.IsNullOrWhiteSpace(node.id)) { error = $"Node '{node.displayName}' has no id."; return false; }
                if (!ids.Add(node.id)) { error = $"Duplicate node id '{node.id}'."; return false; }
                if (node.tier < 1 || node.cost < 1) { error = $"Node '{node.id}' needs tier and cost of at least 1."; return false; }
                if (!Enum.IsDefined(typeof(UpgradeBranch), node.branch)) { error = $"Node '{node.id}' has an unknown branch."; return false; }
                if (node.effects == null || node.effects.Length == 0) { error = $"Node '{node.id}' has no effect."; return false; }
                foreach (UpgradeEffect effect in node.effects)
                    if (!effect.IsValid(out string effectError)) { error = $"Node '{node.id}': {effectError}"; return false; }
            }
            RebuildIndex();
            foreach (UpgradeNode node in nodes)
            {
                if (node.prerequisiteIds == null) continue;
                foreach (string prerequisiteId in node.prerequisiteIds)
                {
                    UpgradeNode prerequisite = Find(prerequisiteId);
                    if (prerequisite == null) { error = $"Node '{node.id}' requires unknown node '{prerequisiteId}'."; return false; }
                    if (prerequisite == node) { error = $"Node '{node.id}' requires itself."; return false; }
                    if (prerequisite.tier >= node.tier) { error = $"Node '{node.id}' must sit above its prerequisite '{prerequisiteId}'."; return false; }
                }
            }
            error = null;
            return true;
        }

        private void RebuildIndex()
        {
            byId = new Dictionary<string, UpgradeNode>();
            if (nodes == null) return;
            foreach (UpgradeNode node in nodes)
                if (node != null && !string.IsNullOrEmpty(node.id)) byId[node.id] = node;
        }

        private void OnValidate() => byId = null;

        /// <summary>
        /// The shipped default tree. Used by the editor installer and as the runtime fallback when the
        /// Resources asset is missing. Names and numbers are design starting points; edit them on the asset.
        /// </summary>
        public static void PopulateDefaults(UpgradeTreeConfig config, WeaponData blades = null)
        {
            // Nodes and points per level only: Starting Points and Max Available Tier are build switches and stay as authored.
            config.pointsPerLevel = 1;
            config.nodes = new List<UpgradeNode>
            {
                Node("blades.honed", "Honed Edge", "Blade strikes deal 20% more damage.", UpgradeBranch.Blades, 1,
                    new[] { UpgradeEffect.Stat(PlayerStat.OutgoingDamage, percent: 20f) }, requiredWeapon: blades),
                Node("blades.quick", "Quick Draw", "Attack animations play 15% faster and combos chain sooner.", UpgradeBranch.Blades, 2,
                    new[] { UpgradeEffect.Stat(PlayerStat.AttackSpeed, percent: 15f) }, "blades.honed", blades),
                Node("blades.finisher", "Heavy Finisher", "Launching finishers throw enemies 50% harder.", UpgradeBranch.Blades, 3,
                    new[] { UpgradeEffect.Stat(PlayerStat.FinisherForce, percent: 50f) }, "blades.quick", blades),

                Node("vitality.frame1", "Reinforced Frame I", "+25 maximum health.", UpgradeBranch.Vitality, 1,
                    new[] { UpgradeEffect.Stat(PlayerStat.MaxHealth, flat: 25f) }),
                Node("vitality.plating1", "Plating I", "+2 armor: every hit taken is reduced by 2.", UpgradeBranch.Vitality, 2,
                    new[] { UpgradeEffect.Stat(PlayerStat.Armor, flat: 2f) }, "vitality.frame1"),
                Node("vitality.frame2", "Reinforced Frame II", "+25 maximum health.", UpgradeBranch.Vitality, 3,
                    new[] { UpgradeEffect.Stat(PlayerStat.MaxHealth, flat: 25f) }, "vitality.plating1"),
                Node("vitality.plating2", "Plating II", "+2 armor.", UpgradeBranch.Vitality, 4,
                    new[] { UpgradeEffect.Stat(PlayerStat.Armor, flat: 2f) }, "vitality.frame2"),

                Node("jump.spring", "Spring Legs", "Jump 15% higher.", UpgradeBranch.Jump, 1,
                    new[] { UpgradeEffect.Stat(PlayerStat.JumpForce, percent: 15f) }),
                Node("jump.double", "Double Jump", "Jump again in the air.", UpgradeBranch.Jump, 2,
                    new[] { UpgradeEffect.Unlock(PlayerAbility.DoubleJump) }, "jump.spring"),
                Node("jump.thrusters", "Thrusters", "The double jump launches 25% harder.", UpgradeBranch.Jump, 3,
                    new[] { UpgradeEffect.Stat(PlayerStat.DoubleJumpForce, percent: 25f) }, "jump.double"),

                Node("dash.unlock", "Dash", "Unlock the dash.", UpgradeBranch.Dash, 1,
                    new[] { UpgradeEffect.Unlock(PlayerAbility.Dash) }),
                Node("dash.strike", "Dash Strike", "Dashing through enemies damages them.", UpgradeBranch.Dash, 2,
                    new[] { UpgradeEffect.Unlock(PlayerAbility.DashStrike) }, "dash.unlock"),
                Node("dash.afterburner", "Afterburner", "Dash cooldown reduced by 30%.", UpgradeBranch.Dash, 3,
                    new[] { UpgradeEffect.Stat(PlayerStat.DashCooldown, percent: -30f) }, "dash.strike"),

                Node("mods.slot1", "Mod Slot I", "Unlock a second active mod slot.", UpgradeBranch.Mods, 1,
                    new[] { UpgradeEffect.Slot(ModSlotKind.Active) }),
                Node("mods.slot2", "Mod Slot II", "Unlock a second passive mod slot.", UpgradeBranch.Mods, 2,
                    new[] { UpgradeEffect.Slot(ModSlotKind.Passive) }, "mods.slot1"),
                Node("mods.slot3", "Mod Slot III", "Unlock a third active mod slot.", UpgradeBranch.Mods, 3,
                    new[] { UpgradeEffect.Slot(ModSlotKind.Active) }, "mods.slot2"),
                Node("mods.slot4", "Mod Slot IV", "Unlock a fourth active mod slot.", UpgradeBranch.Mods, 4,
                    new[] { UpgradeEffect.Slot(ModSlotKind.Active) }, "mods.slot3"),
            };
        }

        private static UpgradeNode Node(string id, string name, string description, UpgradeBranch branch, int tier,
            UpgradeEffect[] effects, string prerequisite = null, WeaponData requiredWeapon = null)
        {
            return new UpgradeNode
            {
                id = id,
                displayName = name,
                description = description,
                branch = branch,
                tier = tier,
                cost = 1,
                prerequisiteIds = prerequisite != null ? new[] { prerequisite } : Array.Empty<string>(),
                requiredWeapon = requiredWeapon,
                effects = effects,
            };
        }
    }
}
