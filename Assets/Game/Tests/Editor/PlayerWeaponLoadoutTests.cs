using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace junklite.Tests
{
    public sealed class PlayerWeaponLoadoutTests
    {
        private const string PlayerPrefabPath =
            "Assets/Game/Prefabs/PLAYER/Player_2.2.prefab";

        private readonly List<Object> cleanup = new();
        private GameObject player;
        private Transform holder;
        private PlayerWeaponLoadout loadout;

        [SetUp]
        public void SetUp()
        {
            player = Track(new GameObject("Loadout Test Player"));
            holder = Track(new GameObject("Weapon Holder")).transform;
            holder.SetParent(player.transform, false);

            Rigidbody body = player.AddComponent<Rigidbody>();
            loadout = player.AddComponent<PlayerWeaponLoadout>();
            loadout.ApplyDefaultsIfMissing(holder);
            loadout.Initialize(body);
        }

        [TearDown]
        public void TearDown()
        {
            for (int index = cleanup.Count - 1; index >= 0; index--)
            {
                if (cleanup[index] != null)
                    Object.DestroyImmediate(cleanup[index]);
            }
            cleanup.Clear();
        }

        [Test]
        public void EquipPlacesWeaponInRequestedSlotAndNotifiesOnce()
        {
            WorldWeaponPickup pickup = CreatePickup("Sword", 10);
            int changedCount = 0;
            loadout.WeaponChanged += () => changedCount++;

            bool equipped = loadout.TryEquipWeapon(
                1,
                pickup,
                new Vector3(4f, 0f, 0f),
                true);

            Assert.That(equipped, Is.True);
            Assert.That(loadout.WeaponSlot1, Is.SameAs(pickup.weaponInstance));
            Assert.That(pickup.gameObject.activeSelf, Is.False);
            Assert.That(pickup.weaponInstance.gameObject.activeSelf, Is.True);
            Assert.That(pickup.weaponInstance.transform.parent, Is.SameAs(holder));
            Assert.That(changedCount, Is.EqualTo(1));
        }

        [Test]
        public void ReplacingWeaponDropsPreviousPickupAsOneLoadoutChange()
        {
            WorldWeaponPickup first = CreatePickup("First", 10);
            WorldWeaponPickup second = CreatePickup("Second", 10);
            loadout.TryEquipWeapon(1, first, Vector3.zero, false);

            int changedCount = 0;
            loadout.WeaponChanged += () => changedCount++;
            Vector3 dropPosition = new(3f, 2f, 1f);

            bool equipped = loadout.TryEquipWeapon(
                1,
                second,
                dropPosition,
                false);

            Assert.That(equipped, Is.True);
            Assert.That(loadout.WeaponSlot1, Is.SameAs(second.weaponInstance));
            Assert.That(first.gameObject.activeSelf, Is.True);
            Assert.That(first.transform.position, Is.EqualTo(dropPosition));
            Assert.That(first.weaponInstance.transform.parent, Is.SameAs(first.transform));
            Assert.That(first.weaponInstance.gameObject.activeSelf, Is.False);
            Assert.That(changedCount, Is.EqualTo(1));
        }

        [Test]
        public void SwapKeepsEachWeaponPairedWithItsWorldPickup()
        {
            WorldWeaponPickup first = CreatePickup("First", 10);
            WorldWeaponPickup second = CreatePickup("Second", 10);
            loadout.TryEquipWeapon(1, first, Vector3.zero, false);
            loadout.TryEquipWeapon(2, second, Vector3.zero, false);

            int changedCount = 0;
            loadout.WeaponChanged += () => changedCount++;

            bool swapped = loadout.TrySwapSlots();

            Assert.That(swapped, Is.True);
            Assert.That(loadout.WeaponSlot1, Is.SameAs(second.weaponInstance));
            Assert.That(loadout.WeaponSlot2, Is.SameAs(first.weaponInstance));
            Assert.That(changedCount, Is.EqualTo(1));

            Vector3 firstDropPosition = new(1f, 2f, 3f);
            Vector3 secondDropPosition = new(4f, 5f, 6f);
            loadout.TryDropWeapon(1, firstDropPosition);
            loadout.TryDropWeapon(2, secondDropPosition);

            Assert.That(second.gameObject.activeSelf, Is.True);
            Assert.That(second.transform.position, Is.EqualTo(firstDropPosition));
            Assert.That(first.gameObject.activeSelf, Is.True);
            Assert.That(first.transform.position, Is.EqualTo(secondDropPosition));
        }

        [Test]
        public void BrokenWeaponRemovesItselfAndPublishesSlot()
        {
            WorldWeaponPickup pickup = CreatePickup("Fragile", 1);
            loadout.TryEquipWeapon(1, pickup, Vector3.zero, true);

            int changedCount = 0;
            int brokenSlot = 0;
            loadout.WeaponChanged += () => changedCount++;
            loadout.WeaponBroken += slot => brokenSlot = slot;

            bool broke = pickup.weaponInstance.ConsumeDurability();

            Assert.That(broke, Is.True);
            Assert.That(loadout.WeaponSlot1, Is.Null);
            Assert.That(pickup.weaponInstance.gameObject.activeSelf, Is.False);
            Assert.That(changedCount, Is.EqualTo(1));
            Assert.That(brokenSlot, Is.EqualTo(1));
        }

        [Test]
        public void ActivePlayerPrefabSerializesConfiguredLoadout()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            PlayerWeaponLoadout prefabLoadout = prefab.GetComponent<PlayerWeaponLoadout>();
            Assert.That(prefabLoadout, Is.Not.Null);

            var serializedLoadout = new SerializedObject(prefabLoadout);
            Assert.That(
                serializedLoadout.FindProperty("weaponHolder").objectReferenceValue,
                Is.Not.Null);
        }

        private WorldWeaponPickup CreatePickup(string objectName, int durability)
        {
            MeleeWeaponData data = Track(ScriptableObject.CreateInstance<MeleeWeaponData>());
            data.displayName = objectName;
            data.maxWeaponDurability = durability;
            data.durabilityPerHit = 1f;

            GameObject pickupObject = Track(new GameObject($"{objectName} Pickup"));
            GameObject weaponObject = new($"{objectName} Weapon");
            weaponObject.transform.SetParent(pickupObject.transform, false);

            WeaponInstance weapon = weaponObject.AddComponent<WeaponInstance>();
            weapon.weaponData = data;

            WorldWeaponPickup pickup = pickupObject.AddComponent<WorldWeaponPickup>();
            pickup.weaponInstance = weapon;
            weaponObject.SetActive(false);
            return pickup;
        }

        private T Track<T>(T instance) where T : Object
        {
            cleanup.Add(instance);
            return instance;
        }
    }
}
