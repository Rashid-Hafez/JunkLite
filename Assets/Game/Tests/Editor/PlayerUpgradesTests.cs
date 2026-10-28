using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace junklite.Tests
{
    /// <summary>Upgrade points, purchases, gating, persistence and the stat/slot/ability queries consumers rely on.</summary>
    public sealed class PlayerUpgradesTests
    {
        private readonly List<Object> owned = new();
        private PlayerProgressionConfig levels;
        private UpgradeTreeConfig tree;
        private PlayerProgression progression;
        private PlayerUpgrades upgrades;
        private MeleeWeaponData blades;

        [SetUp]
        public void SetUp()
        {
            levels = Own(ScriptableObject.CreateInstance<PlayerProgressionConfig>());
            levels.Generate();
            blades = Own(ScriptableObject.CreateInstance<MeleeWeaponData>());
            blades.weaponId = "blades";
            tree = Own(ScriptableObject.CreateInstance<UpgradeTreeConfig>());
            UpgradeTreeConfig.PopulateDefaults(tree, blades);
            // Inactive services never run Awake, so they never touch the real player's save.
            var root = Own(new GameObject("Upgrade Test Services"));
            root.SetActive(false);
            progression = root.AddComponent<PlayerProgression>();
            SetField(progression, "config", levels);
            upgrades = root.AddComponent<PlayerUpgrades>();
            SetField(upgrades, "config", tree);
            Call(upgrades, "Attach", progression);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
        private static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, args);
        private UpgradeNode Node(string id) => tree.Find(id);
        private void ReachLevel(int level) => progression.AddExperience((int)(levels.ExperienceToReach(level) - progression.TotalExperience));

        [Test]
        public void DefaultTreeValidatesAndCoversEveryBranch()
        {
            Assert.That(tree.Validate(out string error), Is.True, error);
            var branch = new List<UpgradeNode>();
            foreach (UpgradeBranch value in Enum.GetValues(typeof(UpgradeBranch)))
            {
                tree.GetBranch(value, branch);
                Assert.That(branch, Is.Not.Empty, value.ToString());
                Assert.That(branch[0].tier, Is.EqualTo(1), $"{value} must start at tier 1");
                for (int i = 1; i < branch.Count; i++) Assert.That(branch[i].tier, Is.GreaterThanOrEqualTo(branch[i - 1].tier));
            }
            Assert.That(Node("blades.honed").requiredWeapon, Is.SameAs(blades));
            Assert.That(Node("dash.unlock").effects[0].ability, Is.EqualTo(PlayerAbility.Dash));
            Assert.That(Node("jump.double").effects[0].ability, Is.EqualTo(PlayerAbility.DoubleJump));
            Assert.That(Node("dash.strike").effects[0].ability, Is.EqualTo(PlayerAbility.DashStrike));
            Assert.That(Node("mods.slot2").effects[0].slotKind, Is.EqualTo(ModSlotKind.Passive));
        }

        [Test]
        public void ValidationCatchesBrokenTrees()
        {
            tree.nodes[1].prerequisiteIds = new[] { "missing" };
            Assert.That(tree.Validate(out string error), Is.False);
            Assert.That(error, Does.Contain("missing"));
            UpgradeTreeConfig.PopulateDefaults(tree, blades);
            tree.nodes[1].id = tree.nodes[0].id;
            Assert.That(tree.Validate(out _), Is.False);
            UpgradeTreeConfig.PopulateDefaults(tree, blades);
            tree.nodes[0].effects = Array.Empty<UpgradeEffect>();
            Assert.That(tree.Validate(out _), Is.False);
            UpgradeTreeConfig.PopulateDefaults(tree, blades);
            tree.nodes[1].tier = 1; // same tier as its prerequisite
            Assert.That(tree.Validate(out _), Is.False);
        }

        [Test]
        public void PointsComeFromLevelsAndBonusesAndPurchasesSpendThem()
        {
            Assert.That(upgrades.AvailablePoints, Is.Zero);
            Assert.That(upgrades.CanPurchase(Node("vitality.frame1"), out var reason), Is.False);
            Assert.That(reason, Is.EqualTo(UpgradeLockReason.NoPoints));

            int changes = 0;
            upgrades.Changed += () => changes++;
            ReachLevel(3);
            Assert.That(upgrades.LevelPoints, Is.EqualTo(2));
            Assert.That(upgrades.AvailablePoints, Is.EqualTo(2));
            Assert.That(changes, Is.GreaterThanOrEqualTo(1), "level changes republish point totals");

            Assert.That(upgrades.TryPurchase(Node("vitality.frame1")), Is.True);
            Assert.That(upgrades.IsOwned("vitality.frame1"), Is.True);
            Assert.That(upgrades.SpentPoints, Is.EqualTo(1));
            Assert.That(upgrades.AvailablePoints, Is.EqualTo(1));
            Assert.That(upgrades.TryPurchase(Node("vitality.frame1")), Is.False, "owned nodes cannot be bought twice");
            Assert.That(upgrades.GetLockReason(Node("vitality.frame1")), Is.EqualTo(UpgradeLockReason.Owned));

            upgrades.AddBonusPoints(2);
            Assert.That(upgrades.EarnedPoints, Is.EqualTo(4));
            Assert.That(upgrades.AvailablePoints, Is.EqualTo(3));
            upgrades.AddBonusPoints(-5);
            Assert.That(upgrades.AvailablePoints, Is.EqualTo(3), "negative awards are ignored");
        }

        [Test]
        public void PrerequisitesAndWeaponsGateNodes()
        {
            upgrades.AddBonusPoints(10);
            Assert.That(upgrades.GetLockReason(Node("vitality.plating1")), Is.EqualTo(UpgradeLockReason.MissingPrerequisite));
            Assert.That(upgrades.TryPurchase(Node("vitality.plating1")), Is.False);
            Assert.That(upgrades.TryPurchase(Node("vitality.frame1")), Is.True);
            Assert.That(upgrades.TryPurchase(Node("vitality.plating1")), Is.True);

            Assert.That(upgrades.GetLockReason(Node("blades.honed")), Is.EqualTo(UpgradeLockReason.MissingWeapon));
            upgrades.GrantWeapon(blades);
            Assert.That(upgrades.OwnsWeapon(blades), Is.True);
            Assert.That(upgrades.GetLockReason(Node("blades.honed")), Is.EqualTo(UpgradeLockReason.None));
            Assert.That(upgrades.TryPurchase(Node("blades.honed")), Is.True);

            var foreign = new UpgradeNode { id = "not.in.tree", effects = new[] { UpgradeEffect.Stat(PlayerStat.Armor, 1) } };
            Assert.That(upgrades.GetLockReason(foreign), Is.EqualTo(UpgradeLockReason.Unknown));
            Assert.That(upgrades.TryPurchase(foreign), Is.False);
        }

        [Test]
        public void OwnedNodesAnswerStatAbilityAndSlotQueries()
        {
            upgrades.AddBonusPoints(20);
            upgrades.GrantWeapon(blades);
            foreach (string id in new[] { "vitality.frame1", "vitality.plating1", "vitality.frame2", "blades.honed", "blades.quick",
                         "jump.spring", "jump.double", "dash.unlock", "dash.strike", "dash.afterburner", "mods.slot1", "mods.slot2" })
                Assert.That(upgrades.TryPurchase(Node(id)), Is.True, id);

            upgrades.GetStatBonus(PlayerStat.MaxHealth, out float flat, out float percent);
            Assert.That(flat, Is.EqualTo(50f));
            Assert.That(percent, Is.Zero);
            upgrades.GetStatBonus(PlayerStat.OutgoingDamage, out flat, out percent);
            Assert.That(percent, Is.EqualTo(20f));
            upgrades.GetStatBonus(PlayerStat.DashCooldown, out _, out percent);
            Assert.That(percent, Is.EqualTo(-30f));
            Assert.That(upgrades.HasAbility(PlayerAbility.Dash), Is.True);
            Assert.That(upgrades.HasAbility(PlayerAbility.DoubleJump), Is.True);
            Assert.That(upgrades.HasAbility(PlayerAbility.DashStrike), Is.True);
            Assert.That(upgrades.ExtraModSlots(ModSlotKind.Active), Is.EqualTo(1));
            Assert.That(upgrades.ExtraModSlots(ModSlotKind.Passive), Is.EqualTo(1));

            upgrades.ResetUpgrades();
            Assert.That(upgrades.HasAbility(PlayerAbility.Dash), Is.False);
            Assert.That(upgrades.AvailablePoints, Is.EqualTo(20));
            Assert.That(upgrades.OwnsWeapon(blades), Is.True, "a refund keeps acquired weapons");
        }

        [Test]
        public void RuntimeStatsCombineLevelsAndUpgradesAndDriveSlots()
        {
            var source = Own(new GameObject("Player Stats Source"));
            source.SetActive(false);
            var stats = Own(ScriptableObject.CreateInstance<CharacterStats>());
            stats.armor = 5;
            stats.attributes.Add(new Attribute { type = AttributeType.Health, name = "Health", maxValue = 100, startingValue = 100 });
            var attributes = source.AddComponent<AttributeManager>();
            attributes.Initialize(stats);
            var mods = source.AddComponent<ModManager>();
            Call(mods, "Awake");
            var runtime = source.AddComponent<PlayerRuntimeStats>();
            runtime.Initialize(stats, attributes, progression, upgrades);

            Assert.That(attributes.Health.Max, Is.EqualTo(100));
            Assert.That(runtime.Armor, Is.EqualTo(5));
            Assert.That(runtime.ScaleDamage(100), Is.EqualTo(100));
            Assert.That(mods.UnlockedActiveSlots, Is.EqualTo(1));
            Assert.That(mods.UnlockedPassiveSlots, Is.EqualTo(1));

            upgrades.AddBonusPoints(10);
            upgrades.GrantWeapon(blades);
            attributes.Health.Remove(40);
            Assert.That(upgrades.TryPurchase(Node("vitality.frame1")), Is.True);
            Assert.That(attributes.Health.Max, Is.EqualTo(125));
            Assert.That(attributes.Health.Current, Is.EqualTo(85), "bought capacity is added as current health");
            Assert.That(upgrades.TryPurchase(Node("vitality.plating1")), Is.True);
            Assert.That(runtime.Armor, Is.EqualTo(7));
            Assert.That(runtime.ArmorDisplay.Current, Is.EqualTo(7));
            Assert.That(upgrades.TryPurchase(Node("blades.honed")), Is.True);
            Assert.That(runtime.ScaleDamage(100), Is.EqualTo(120).Within(0.001f));
            Assert.That(upgrades.TryPurchase(Node("mods.slot1")), Is.True);
            Assert.That(mods.UnlockedActiveSlots, Is.EqualTo(2));
            Assert.That(upgrades.TryPurchase(Node("mods.slot2")), Is.True);
            Assert.That(mods.UnlockedPassiveSlots, Is.EqualTo(2));
            Assert.That(runtime.Multiplier(PlayerStat.JumpForce), Is.EqualTo(1f));
            Assert.That(upgrades.TryPurchase(Node("jump.spring")), Is.True);
            Assert.That(runtime.Multiplier(PlayerStat.JumpForce), Is.EqualTo(1.15f).Within(0.001f));
            Assert.That(runtime.HasAbility(PlayerAbility.Dash), Is.False);
        }

        [Test]
        public void OneShotPointSourcesPayOncePerProfile()
        {
            Assert.That(upgrades.TryCollectPoints("service-floor/Brute Boss", 1), Is.True);
            Assert.That(upgrades.TryCollectPoints("service-floor/Brute Boss", 1), Is.False);
            Assert.That(upgrades.HasCollected("service-floor/Brute Boss"), Is.True);
            Assert.That(upgrades.BonusPoints, Is.EqualTo(1));
            Assert.That(upgrades.TryCollectPoints(null, 2), Is.True, "sources without an id always pay");
            Assert.That(upgrades.BonusPoints, Is.EqualTo(3));
        }

        [Test]
        public void SaveRoundTripsUpgradesAndLoadsVersionOneFiles()
        {
            upgrades.AddBonusPoints(3);
            upgrades.GrantWeapon(blades);
            upgrades.TryPurchase(Node("vitality.frame1"));
            upgrades.TryCollectPoints("rooftop/pickup", 1);
            var data = new ProgressionSaveData { totalExperience = 175, level = 2 };
            Call(upgrades, "WriteTo", data);
            Assert.That(data.version, Is.EqualTo(ProgressionSaveData.CurrentVersion));
            Assert.That(data.bonusUpgradePoints, Is.EqualTo(4));
            Assert.That(data.purchasedUpgrades, Is.EqualTo(new[] { "vitality.frame1" }));
            Assert.That(data.ownedWeapons, Is.EqualTo(new[] { "blades" }));
            Assert.That(data.collectedPointSources, Is.EqualTo(new[] { "rooftop/pickup" }));

            string directory = Path.Combine(Path.GetTempPath(), "JunkLite-Upgrades-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "save.json");
            try
            {
                var store = new ProgressionSaveStore(path);
                Assert.That(store.TrySave(data, out _), Is.True);
                Assert.That(store.TryLoad(out var loaded, out _), Is.True);
                var fresh = Own(new GameObject("Fresh Upgrades")).AddComponent<PlayerUpgrades>();
                fresh.gameObject.SetActive(false);
                SetField(fresh, "config", tree);
                Call(fresh, "LoadFrom", loaded);
                Assert.That(fresh.BonusPoints, Is.EqualTo(4));
                Assert.That(fresh.IsOwned("vitality.frame1"), Is.True);
                Assert.That(fresh.OwnsWeapon(blades), Is.True);
                Assert.That(fresh.HasCollected("rooftop/pickup"), Is.True);

                File.WriteAllText(path, "{\"version\":1,\"configRevision\":1,\"totalExperience\":700,\"level\":5,\"levelProgress\":0.0}");
                Assert.That(store.TryLoad(out var legacy, out _), Is.True, "version 1 saves still load");
                Assert.That(legacy.totalExperience, Is.EqualTo(700));
                Assert.That(legacy.purchasedUpgrades, Is.Empty);
                Assert.That(legacy.ownedWeapons, Is.Empty);
                Assert.That(legacy.version, Is.EqualTo(ProgressionSaveData.CurrentVersion));

                File.WriteAllText(path, "{\"version\":9,\"totalExperience\":1,\"level\":1}");
                Assert.That(store.TryLoad(out _, out _), Is.False, "unknown future versions are not loaded");
            }
            finally
            {
                foreach (string suffix in new[] { "", ".bak", ".tmp" })
                    if (File.Exists(path + suffix)) File.Delete(path + suffix);
                if (Directory.Exists(directory)) Directory.Delete(directory);
            }
        }

        [Test]
        public void ShippedProfileGrantsNoAutomaticStatsAndBruteAwardsAPoint()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<PlayerProgressionConfig>("Assets/Game/Resources/PlayerProgressionConfig.asset");
            Assert.That(shipped, Is.Not.Null);
            Assert.That(shipped.Validate(out string error), Is.True, error);
            Assert.That(shipped.revision, Is.GreaterThanOrEqualTo(2));
            Assert.That(shipped.Evaluate(PlayerStat.MaxHealth, 100, shipped.MaxLevel), Is.EqualTo(100));
            Assert.That(shipped.Evaluate(PlayerStat.OutgoingDamage, 100, shipped.MaxLevel), Is.EqualTo(100));
            var brute = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/ENEMIES/Brute/Brute Boss.prefab");
            Assert.That(brute.GetComponent<EnemyBase>().UpgradePointReward, Is.EqualTo(1));
            var dummy = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Enemies/Dummy.prefab");
            Assert.That(dummy.GetComponent<EnemyBase>().UpgradePointReward, Is.Zero);
        }

        [Test]
        public void StartingPointsAddToEveryOtherSource()
        {
            tree.startingPoints = 5;
            Assert.That(upgrades.StartingPoints, Is.EqualTo(5));
            Assert.That(upgrades.AvailablePoints, Is.EqualTo(5), "a fresh profile can spend the grant at once");
            ReachLevel(2);
            upgrades.AddBonusPoints(1);
            Assert.That(upgrades.EarnedPoints, Is.EqualTo(7));
            Assert.That(upgrades.TryPurchase(tree.Find("vitality.frame1")), Is.True);
            Assert.That(upgrades.AvailablePoints, Is.EqualTo(6));
        }

        [Test]
        public void TheBuildTierCapLeavesHigherTiersUnavailable()
        {
            tree.maxAvailableTier = 1;
            upgrades.AddBonusPoints(20);
            upgrades.GrantWeapon(blades);
            var tierOne = new List<UpgradeNode>();
            foreach (UpgradeNode node in tree.nodes)
            {
                if (node.tier == 1)
                {
                    tierOne.Add(node);
                    Assert.That(upgrades.GetLockReason(node), Is.EqualTo(UpgradeLockReason.None), node.id);
                }
                else
                {
                    Assert.That(upgrades.GetLockReason(node), Is.EqualTo(UpgradeLockReason.NotInBuild), node.id);
                }
            }
            Assert.That(tierOne.Count, Is.EqualTo(Enum.GetValues(typeof(UpgradeBranch)).Length), "one buyable node per branch");
            foreach (UpgradeNode node in tierOne) Assert.That(upgrades.TryPurchase(node), Is.True, node.id);
            Assert.That(upgrades.TryPurchase(tree.Find("vitality.plating1")), Is.False, "owning tier 1 does not open tier 2");
            Assert.That(upgrades.GetLockReason(tree.Find("vitality.plating1")), Is.EqualTo(UpgradeLockReason.NotInBuild));

            tree.maxAvailableTier = 0; // the full game
            Assert.That(upgrades.TryPurchase(tree.Find("vitality.plating1")), Is.True);
        }

        [Test]
        public void TheShippedTreeStartsWithFivePointsAndOnlyTierOne()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<UpgradeTreeConfig>("Assets/Game/Resources/UpgradeTreeConfig.asset");
            Assert.That(shipped, Is.Not.Null);
            Assert.That(shipped.Validate(out string error), Is.True, error);
            Assert.That(shipped.startingPoints, Is.EqualTo(5));
            Assert.That(shipped.maxAvailableTier, Is.EqualTo(1));
            Assert.That(shipped.IsTierAvailable(1), Is.True);
            Assert.That(shipped.IsTierAvailable(2), Is.False);
        }

        [Test]
        public void EnsureAddsTheServiceBesideProgression()
        {
            var root = Own(new GameObject("Root Services"));
            root.SetActive(false);
            root.AddComponent<PlayerProgression>();
            var ensured = PlayerUpgrades.Ensure(root.transform);
            Assert.That(ensured, Is.Not.Null);
            Assert.That(ensured.gameObject, Is.SameAs(root));
            Assert.That(PlayerUpgrades.Ensure(root.transform), Is.SameAs(ensured));
        }
    }
}
