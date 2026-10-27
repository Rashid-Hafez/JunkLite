using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace junklite.Tests
{
    /// <summary>The arm-weapon model: fists by default, permanent upgrade on acquisition, authored held visual.</summary>
    public sealed class PlayerArmamentTests
    {
        private readonly List<Object> owned = new();

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

        private PlayerArmament MakeArmament(out MeleeWeaponData fists, out MeleeWeaponData blades, out GameObject bladesVisual)
        {
            var player = Own(new GameObject("Armament Player"));
            player.SetActive(false);
            var holder = new GameObject("Weaponsocket").transform;
            holder.SetParent(player.transform, false);
            bladesVisual = new GameObject("Blades Visual", typeof(SpriteRenderer));
            bladesVisual.transform.SetParent(holder, false);
            bladesVisual.SetActive(false);

            fists = Own(ScriptableObject.CreateInstance<MeleeWeaponData>());
            fists.weaponId = "fists";
            blades = Own(ScriptableObject.CreateInstance<MeleeWeaponData>());
            blades.weaponId = "blades";
            blades.enablesSwordGrapple = true;

            var armament = player.AddComponent<PlayerArmament>();
            SetField(armament, "defaultWeapon", fists);
            SetField(armament, "upgradePath", new WeaponData[] { blades });
            SetField(armament, "weaponHolder", holder);
            SetField(armament, "weaponSortingOrder", 11);
            SetField(armament, "weaponSortingOrderFacingLeft", -1);
            var entryType = typeof(PlayerArmament).GetNestedType("HeldVisual", BindingFlags.NonPublic);
            var entry = System.Activator.CreateInstance(entryType);
            entryType.GetField("weapon").SetValue(entry, blades);
            entryType.GetField("visual").SetValue(entry, bladesVisual);
            var visuals = System.Array.CreateInstance(entryType, 1);
            visuals.SetValue(entry, 0);
            SetField(armament, "heldVisuals", visuals);
            armament.ApplyDefaultsIfMissing(null, null);
            return armament;
        }

        [Test]
        public void DefaultWeaponIsFistsWithNoHeldVisual()
        {
            var armament = MakeArmament(out var fists, out _, out var visual);
            Assert.That(armament.Current, Is.SameAs(fists));
            Assert.That(armament.HasUpgradedWeapon, Is.False);
            Assert.That(armament.Owns(fists), Is.True);
            Assert.That(visual.activeSelf, Is.False);
        }

        [Test]
        public void AcquiringBladesIsPermanentAndNotifiesOnce()
        {
            var armament = MakeArmament(out _, out var blades, out var visual);
            int changed = 0, acquired = 0;
            armament.WeaponChanged += () => changed++;
            armament.WeaponAcquired += w => { Assert.That(w, Is.SameAs(blades)); acquired++; };

            Assert.That(armament.TryAcquire(blades), Is.True);
            Assert.That(armament.Current, Is.SameAs(blades));
            Assert.That(armament.HasUpgradedWeapon, Is.True);
            Assert.That(armament.Owns(blades), Is.True);
            Assert.That(visual.activeSelf, Is.True, "the authored held visual shows while the weapon is current");
            Assert.That(visual.GetComponent<SpriteRenderer>().sortingOrder, Is.EqualTo(11));
            Assert.That(changed, Is.EqualTo(1));
            Assert.That(acquired, Is.EqualTo(1));

            Assert.That(armament.TryAcquire(blades), Is.False, "re-acquiring the current weapon is a no-op");
            Assert.That(armament.TryAcquire(null), Is.False);
            Assert.That(changed, Is.EqualTo(1));
        }

        [Test]
        public void GrappleHidingAndVisibilityOnlyToggleRenderers()
        {
            var armament = MakeArmament(out _, out var blades, out var visual);
            armament.TryAcquire(blades);
            var renderer = visual.GetComponent<SpriteRenderer>();
            armament.SetGrappleWeaponHidden(true);
            Assert.That(renderer.enabled, Is.False);
            Assert.That(visual.activeSelf, Is.True);
            armament.SetGrappleWeaponHidden(false);
            Assert.That(renderer.enabled, Is.True);
            armament.SetVisible(false);
            Assert.That(renderer.enabled, Is.False);
            armament.SetVisible(true);
            Assert.That(renderer.enabled, Is.True);
        }

        [Test]
        public void RevertToDefaultForgetsAcquiredWeapons()
        {
            var armament = MakeArmament(out var fists, out var blades, out var visual);
            armament.TryAcquire(blades);
            armament.RevertToDefault();
            Assert.That(armament.Current, Is.SameAs(fists));
            Assert.That(armament.Owns(blades), Is.False);
            Assert.That(visual.activeSelf, Is.False);
        }

        [Test]
        public void SpawnRestoresStrongestOwnedWeaponFromProfile()
        {
            var service = Own(new GameObject("Upgrades Service"));
            service.SetActive(false);
            var upgrades = service.AddComponent<PlayerUpgrades>();
            typeof(PlayerUpgrades).GetProperty(nameof(PlayerUpgrades.Instance)).SetValue(null, upgrades);
            try
            {
                var armament = MakeArmament(out _, out var blades, out var visual);
                upgrades.GrantWeapon(blades);
                Call(armament, "RestoreFromProfile");
                Assert.That(armament.Current, Is.SameAs(blades));
                Assert.That(visual.activeSelf, Is.True);
            }
            finally
            {
                typeof(PlayerUpgrades).GetProperty(nameof(PlayerUpgrades.Instance)).SetValue(null, null);
            }
        }

        [Test]
        public void PickupGrantsTheWeaponAndRetires()
        {
            var armament = MakeArmament(out _, out var blades, out _);
            var pickupObject = Own(new GameObject("Blades Pickup"));
            pickupObject.SetActive(false);
            var pickup = pickupObject.AddComponent<WeaponUpgradePickup>();
            SetField(pickup, "weaponData", blades);
            pickupObject.SetActive(true);
            armament.gameObject.SetActive(true);
            var collider = armament.gameObject.AddComponent<CapsuleCollider>();
            SetField(pickup, "playerInside", collider);

            Call(pickup, "HandleInteract");

            Assert.That(armament.Current, Is.SameAs(blades));
            Assert.That(pickupObject.activeSelf, Is.False, "a taken pickup retires itself");
        }

        [Test]
        public void WeaponManagerAttacksWithTheCurrentWeaponAndRebuildsItsCombo()
        {
            var armament = MakeArmament(out var fists, out var blades, out _);
            var player = armament.gameObject;
            player.AddComponent<CapsuleCollider>();
            var weapons = player.AddComponent<WeaponManager>();
            SetField(weapons, "fistWeaponData", fists);
            Call(weapons, "Awake");
            Assert.That(weapons.CurrentWeapon, Is.SameAs(fists));
            var fistCombat = weapons.GetType().GetField("combat", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(weapons) as CombatState;
            Assert.That(fistCombat, Is.Not.Null);
            Assert.That(fistCombat.Data, Is.SameAs(fists));

            armament.TryAcquire(blades);
            Assert.That(weapons.CurrentWeapon, Is.SameAs(blades));
            var bladeCombat = weapons.GetType().GetField("combat", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(weapons) as CombatState;
            Assert.That(bladeCombat, Is.Not.SameAs(fistCombat));
            Assert.That(bladeCombat.Data, Is.SameAs(blades));

            weapons.AttackSpeedMultiplier = 2f;
            Assert.That(bladeCombat.AttackCooldown, Is.EqualTo(0.15f / 2f).Within(0.0001f));
            Assert.That(bladeCombat.ComboWindow, Is.EqualTo(0.6f / 2f).Within(0.0001f));
        }

        [Test]
        public void ShippedPlayerPrefabsAndBladesPickupAreWired()
        {
            var fists = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/Game/Scripts/Weapons/Fist.asset");
            var blades = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/Game/Scripts/Weapons/Sword.asset");
            Assert.That(fists, Is.Not.Null);
            Assert.That(blades, Is.Not.Null);
            Assert.That(blades.enablesSwordGrapple, Is.True);

            foreach (string path in new[] { "Assets/Game/Prefabs/PLAYER/Player_Cass.prefab", "Assets/Game/Prefabs/PLAYER/Player_2.2.prefab" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var armament = prefab.GetComponent<PlayerArmament>();
                Assert.That(armament, Is.Not.Null, path);
                var serialized = new SerializedObject(armament);
                Assert.That(serialized.FindProperty("defaultWeapon").objectReferenceValue, Is.SameAs(fists), path);
                var upgradePath = serialized.FindProperty("upgradePath");
                Assert.That(upgradePath.arraySize, Is.EqualTo(1), path);
                Assert.That(upgradePath.GetArrayElementAtIndex(0).objectReferenceValue, Is.SameAs(blades), path);
                var holder = serialized.FindProperty("weaponHolder").objectReferenceValue as Transform;
                Assert.That(holder, Is.Not.Null, path);
                var visuals = serialized.FindProperty("heldVisuals");
                Assert.That(visuals.arraySize, Is.EqualTo(1), path);
                var entry = visuals.GetArrayElementAtIndex(0);
                Assert.That(entry.FindPropertyRelative("weapon").objectReferenceValue, Is.SameAs(blades), path);
                var visual = entry.FindPropertyRelative("visual").objectReferenceValue as GameObject;
                Assert.That(visual, Is.Not.Null, path);
                Assert.That(visual.activeSelf, Is.False, "the held visual is authored inactive");
                Assert.That(visual.transform.parent, Is.SameAs(holder), "the held visual sits under the weapon holder");
                Assert.That(visual.GetComponent<SpriteRenderer>(), Is.Not.Null);
                Assert.That(prefab.GetComponent<WeaponManager>(), Is.Not.Null);
                Assert.That(prefab.GetComponent<ScreenGlitchEffect>(), Is.Not.Null);
            }

            var pickupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/New Weapons/sword prefab (2).prefab");
            var pickup = pickupPrefab.GetComponent<WeaponUpgradePickup>();
            Assert.That(pickup, Is.Not.Null);
            Assert.That(pickup.WeaponData, Is.SameAs(blades));
        }
    }
}
