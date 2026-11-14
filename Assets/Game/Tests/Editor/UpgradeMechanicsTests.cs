using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace junklite.Tests
{
    /// <summary>Owned upgrade nodes change the mechanics: ability locks, movement stats, attack speed, mod slots, dash strike.</summary>
    public sealed class UpgradeMechanicsTests
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
            blades.baseDamage = 20f;
            tree = Own(ScriptableObject.CreateInstance<UpgradeTreeConfig>());
            UpgradeTreeConfig.PopulateDefaults(tree, blades);
            var root = Own(new GameObject("Upgrade Test Services"));
            root.SetActive(false);
            progression = root.AddComponent<PlayerProgression>();
            SetField(progression, "config", levels);
            upgrades = root.AddComponent<PlayerUpgrades>();
            SetField(upgrades, "config", tree);
            Call(upgrades, "Attach", progression);
            upgrades.AddBonusPoints(20);
            upgrades.GrantWeapon(blades);
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
        private static T GetField<T>(object target, string name) => (T)target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static object Call(object target, string name, params object[] args) => target.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, args);
        private void Buy(string id) => Assert.That(upgrades.TryPurchase(tree.Find(id)), Is.True, id);

        private PlayerCharacter MakePlayer(out Character2D5Controller controller, out WeaponManager weapons, out PlayerRuntimeStats runtime)
        {
            var go = Own(new GameObject("Upgrade Player"));
            go.SetActive(false);
            go.layer = 16;
            go.AddComponent<Rigidbody>().useGravity = false;
            go.AddComponent<CapsuleCollider>();
            go.AddComponent<PlayerState>();
            controller = go.AddComponent<Character2D5Controller>();
            Call(controller, "Awake");
            controller.DoubleJumpForce = 10f;
            controller.DashCooldown = 1f;
            controller.MaxAirJumps = 1;
            var stats = Own(ScriptableObject.CreateInstance<CharacterStats>());
            stats.moveSpeed = 5f;
            stats.jumpForce = 12f;
            stats.dashForce = 40f;
            stats.dashDuration = 0.2f;
            stats.attributes.Add(new Attribute { type = AttributeType.Health, name = "Health", maxValue = 100, startingValue = 100 });
            var attributes = go.AddComponent<AttributeManager>();
            attributes.Initialize(stats);
            var armament = go.AddComponent<PlayerArmament>();
            var fists = Own(ScriptableObject.CreateInstance<MeleeWeaponData>());
            fists.baseDamage = 10f;
            armament.ApplyDefaultsIfMissing(fists, null);
            weapons = go.AddComponent<WeaponManager>();
            SetField(weapons, "fistWeaponData", fists);
            Call(weapons, "Awake");
            runtime = go.AddComponent<PlayerRuntimeStats>();
            runtime.Initialize(stats, attributes, progression, upgrades);
            var player = go.AddComponent<PlayerCharacter>();
            SetField(player, "controller", controller);
            SetField(player, "baseStats", stats);
            SetField(player, "_weaponManager", weapons);
            typeof(PlayerCharacter).GetProperty(nameof(PlayerCharacter.RuntimeStats)).SetValue(player, runtime);
            var strike = go.AddComponent<PlayerDashStrike>();
            Call(strike, "Awake");
            typeof(PlayerCharacter).GetProperty(nameof(PlayerCharacter.DashStrike)).SetValue(player, strike);
            runtime.Refreshed += player.ApplyUpgrades;
            Call(player, "UpdateControllerStats");
            return player;
        }

        [Test]
        public void DashAndDoubleJumpAlwaysAvailable()
        {
            MakePlayer(out var controller, out _, out _);
            Assert.That(controller.DashEnabled, Is.True);
            Assert.That(controller.MaxAirJumps, Is.EqualTo(1));
        }

        [Test]
        public void StatNodesScaleMovementAttackSpeedAndFinisherForce()
        {
            MakePlayer(out var controller, out var weapons, out _);
            Assert.That(controller.JumpForce, Is.EqualTo(12f));
            Assert.That(controller.DashCooldown, Is.EqualTo(1f));
            Assert.That(weapons.AttackSpeedMultiplier, Is.EqualTo(1f));

            Buy("jump.spring");
            Assert.That(controller.JumpForce, Is.EqualTo(12f * 1.15f).Within(0.001f));
            Buy("jump.thrusters");
            Assert.That(controller.DoubleJumpForce, Is.EqualTo(12.5f).Within(0.001f));

            Buy("dash.strike");
            Buy("dash.afterburner");
            Assert.That(controller.DashCooldown, Is.EqualTo(0.7f).Within(0.001f));

            Buy("blades.honed");
            Buy("blades.quick");
            Assert.That(weapons.AttackSpeedMultiplier, Is.EqualTo(1.15f).Within(0.001f));
            Buy("blades.finisher");
            Assert.That(weapons.FinisherForceMultiplier, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(controller.MoveSpeed, Is.EqualTo(5f), "no node touches move speed yet");
        }

        [Test]
        public void DashStrikeHitsEachEnemyOncePerDashAndChargesMods()
        {
            var player = MakePlayer(out var controller, out var weapons, out _);
            var strike = player.DashStrike;
            Assert.That(strike.Unlocked, Is.False);
            Buy("dash.strike");
            Assert.That(strike.Unlocked, Is.True);

            var enemyLayer = LayerMask.NameToLayer("Enemies");
            Assert.That(enemyLayer, Is.GreaterThanOrEqualTo(0), "the project's enemy layer is named 'Enemies'");
            SetField(weapons, "enemyLayer", (LayerMask)(1 << enemyLayer));
            var target = Own(new GameObject("Dash Target"));
            target.layer = enemyLayer;
            target.transform.position = new Vector3(2f, 1f, 0f);
            var box = target.AddComponent<BoxCollider>();
            var stats = Own(ScriptableObject.CreateInstance<CharacterStats>());
            stats.attributes.Add(new Attribute { type = AttributeType.Health, name = "Health", maxValue = 100, startingValue = 100 });
            var attributes = target.AddComponent<AttributeManager>();
            attributes.Initialize(stats);
            var receiver = target.AddComponent<Damageable>();
            receiver.Bind(stats, attributes, null);
            // Damageable is not an IDamageReceiver; enemies forward to it. Stand in for EnemyBase.
            target.AddComponent<DashStrikeTestReceiver>().Damageable = receiver;
            Physics.SyncTransforms();

            float reported = 0f;
            weapons.OnEnemyHit += (_, damage) => reported += damage;
            player.gameObject.SetActive(true);
            try
            {
                Call(strike, "HandleDashStarted");
                int applied = strike.Sweep(Vector3.zero, new Vector3(4f, 0f, 0f));
                Assert.That(applied, Is.EqualTo(1));
                Assert.That(attributes.Health.Current, Is.EqualTo(100f - 10f * 0.75f).Within(0.001f), "0.75 × fist damage, no armor");
                Assert.That(reported, Is.EqualTo(7.5f).Within(0.001f), "applied damage is reported so mods charge");
                Assert.That(strike.Sweep(Vector3.zero, new Vector3(4f, 0f, 0f)), Is.Zero, "one hit per enemy per dash");
                Call(strike, "HandleDashStarted");
                Assert.That(strike.Sweep(Vector3.zero, new Vector3(4f, 0f, 0f)), Is.EqualTo(1), "a new dash hits again");
                strike.Unlocked = false;
                Call(strike, "HandleDashStarted");
                Assert.That(GetField<bool>(strike, "dashing"), Is.False, "a locked strike never arms");
            }
            finally { player.gameObject.SetActive(false); }
        }

        [Test]
        public void StrikeTrailEmitsOnlyWhileDashingWithDashStrikeUnlocked()
        {
            var player = MakePlayer(out _, out _, out _);
            var strike = player.DashStrike;
            var trail = Own(new GameObject("Strike Trail")).AddComponent<TrailRenderer>();
            SetField(strike, "strikeTrail", trail);

            Call(strike, "HandleDashStarted");
            Assert.That(trail.emitting, Is.False, "a locked strike shows no trail");
            Buy("dash.strike");
            Call(strike, "HandleDashStarted");
            Assert.That(trail.emitting, Is.True);
            Call(strike, "HandleDashEnded");
            Assert.That(trail.emitting, Is.False);
        }

        [Test]
        public void HitSlashSpawnsThroughTheHitPointAlongTheFlattenedDash()
        {
            var player = MakePlayer(out _, out _, out _);
            var strike = player.DashStrike;
            var template = Own(new GameObject("Slash Template"));
            SetField(strike, "hitSlashPrefab", template);
            SetField(strike, "hitSlashTilt", 0f);

            var point = new Vector3(3f, 1.2f, -2f);
            Call(strike, "SpawnSlash", point, new Vector3(-2f, 0.5f, 0f));
            var slash = GameObject.Find("Slash Template(Clone)");
            Assert.That(slash, Is.Not.Null);
            Own(slash);
            Assert.That(Vector3.Distance(slash.transform.position, point), Is.LessThan(0.001f));
            Assert.That(Vector3.Dot(slash.transform.right, Vector3.left), Is.GreaterThan(0.999f), "the line runs along the dash, flattened");
            Assert.That(Mathf.Abs(slash.transform.up.y), Is.GreaterThan(0.999f), "no tilt when the tilt range is zero");
        }

        [Test]
        public void ModSlotNodesUnlockSlotsInOrder()
        {
            var go = Own(new GameObject("Slot Player"));
            go.SetActive(false);
            var stats = Own(ScriptableObject.CreateInstance<CharacterStats>());
            stats.attributes.Add(new Attribute { type = AttributeType.Health, name = "Health", maxValue = 100, startingValue = 100 });
            var attributes = go.AddComponent<AttributeManager>();
            attributes.Initialize(stats);
            var mods = go.AddComponent<ModManager>();
            Call(mods, "Awake");
            var runtime = go.AddComponent<PlayerRuntimeStats>();
            runtime.Initialize(stats, attributes, progression, upgrades);
            Assert.That(mods.UnlockedActiveSlots, Is.EqualTo(1));
            Assert.That(mods.UnlockedPassiveSlots, Is.EqualTo(1));
            Buy("mods.slot1");
            Assert.That(mods.UnlockedActiveSlots, Is.EqualTo(2));
            Buy("mods.slot2");
            Assert.That(mods.UnlockedPassiveSlots, Is.EqualTo(2));
            Buy("mods.slot3");
            Buy("mods.slot4");
            Assert.That(mods.UnlockedActiveSlots, Is.EqualTo(4));
            Assert.That(mods.UnlockedPassiveSlots, Is.EqualTo(2), "passive slots are capped by the prefab maximum");
        }
    }

    /// <summary>Minimal enemy-side receiver for tests: forwards to Damageable like EnemyBase does.</summary>
    public sealed class DashStrikeTestReceiver : MonoBehaviour, IDamageReceiver
    {
        public Damageable Damageable;
        public bool IsAlive => GetComponent<AttributeManager>().IsAlive;
        public DamageResult ReceiveDamage(DamageRequest request) => Damageable.ReceiveDamage(request);
    }
}
