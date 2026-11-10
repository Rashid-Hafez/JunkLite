using NUnit.Framework;
using UnityEngine;

namespace Junklite.Inventory.Tests
{
    public class ModInventoryTests
    {
        ModInventory _inventory;
        ModDefinition _strike, _dash, _blink, _metal, _pogo;
        int _changes;

        [SetUp]
        public void CreateInventory()
        {
            _inventory = new GameObject("Mods").AddComponent<ModInventory>();
            _inventory.Capacity = 4;
            _strike = ModDefinition.Create("phantom-strike", "Phantom Strike", ModRarity.Elite, ModType.Active, 25f);
            _dash = ModDefinition.Create("dash-attack", "Dash Attack", ModRarity.Standard, ModType.Active, 5f);
            _blink = ModDefinition.Create("dont-blink", "Don't Blink", ModRarity.Classified, ModType.Active, 10f);
            _metal = ModDefinition.Create("heavy-metal", "Heavy Metal", ModRarity.Junk, ModType.Passive);
            _pogo = ModDefinition.Create("pogo", "Pogo", ModRarity.Standard, ModType.Passive);
            _changes = 0;
            _inventory.Changed += () => _changes++;
        }

        [TearDown]
        public void DestroyInventory()
        {
            Object.DestroyImmediate(_inventory.gameObject);
            foreach (var mod in new[] { _strike, _dash, _blink, _metal, _pogo }) Object.DestroyImmediate(mod);
        }

        [Test]
        public void Add_PutsModInStorage()
        {
            Assert.That(_inventory.Add(_strike), Is.True);
            CollectionAssert.AreEqual(new[] { _strike }, _inventory.Stored);
            Assert.That(_changes, Is.EqualTo(1));
        }

        [Test]
        public void Add_IsRefusedWhenStorageIsFull()
        {
            foreach (var mod in new[] { _strike, _dash, _blink, _metal }) _inventory.Add(mod);
            Assert.That(_inventory.Add(_pogo), Is.False);
            Assert.That(_inventory.StoredCount, Is.EqualTo(4));
        }

        [Test]
        public void Remove_TakesModOutOfStorageOrItsSlot()
        {
            _inventory.Add(_strike);
            _inventory.Add(_metal);
            _inventory.Equip(_metal, ModSlot.Passive1);

            Assert.That(_inventory.Remove(_strike), Is.True);
            Assert.That(_inventory.Remove("heavy-metal"), Is.True);
            Assert.That(_inventory.GetEquipped(ModSlot.Passive1), Is.Null);
            CollectionAssert.IsEmpty(_inventory.Stored);
            Assert.That(_inventory.Remove(_pogo), Is.False);
        }

        [Test]
        public void Equip_FromStorage_PutsPreviousModWhereTheNewOneWas()
        {
            _inventory.Add(_dash);
            _inventory.Add(_strike);
            _inventory.Add(_blink);
            _inventory.Equip(_dash, ModSlot.Z);

            Assert.That(_inventory.Equip(_blink, ModSlot.Z), Is.EqualTo(EquipResult.Equipped));
            Assert.That(_inventory.GetEquipped(ModSlot.Z), Is.EqualTo(_blink));
            CollectionAssert.AreEqual(new[] { _strike, _dash }, _inventory.Stored);
        }

        [Test]
        public void Equip_BetweenSlots_Swaps()
        {
            _inventory.Add(_dash);
            _inventory.Add(_blink);
            _inventory.Equip(_dash, ModSlot.Z);
            _inventory.Equip(_blink, ModSlot.X);

            _inventory.Equip(_dash, ModSlot.X);
            Assert.That(_inventory.GetEquipped(ModSlot.X), Is.EqualTo(_dash));
            Assert.That(_inventory.GetEquipped(ModSlot.Z), Is.EqualTo(_blink));
        }

        [Test]
        public void Equip_IntoTheWrongKindOfSlot_IsRefused()
        {
            _inventory.Add(_metal);
            Assert.That(_inventory.Equip(_metal, ModSlot.Z), Is.EqualTo(EquipResult.WrongSlotType));
            Assert.That(_inventory.GetEquipped(ModSlot.Z), Is.Null);
            CollectionAssert.AreEqual(new[] { _metal }, _inventory.Stored);
        }

        [Test]
        public void Equip_AModCasDoesNotOwn_IsRefused()
        {
            Assert.That(_inventory.Equip(_strike, ModSlot.Z), Is.EqualTo(EquipResult.NotInInventory));
        }

        [Test]
        public void Equip_WithoutASlot_UsesTheFirstEmptyOne()
        {
            _inventory.Add(_dash);
            _inventory.Add(_blink);
            _inventory.Equip(_dash, ModSlot.Z);

            _inventory.Equip(_blink);
            Assert.That(_inventory.GetEquipped(ModSlot.X), Is.EqualTo(_blink));
        }

        [Test]
        public void Unequip_ReturnsModToStorage_UnlessStorageIsFull()
        {
            _inventory.Add(_strike);
            _inventory.Equip(_strike, ModSlot.V);
            Assert.That(_inventory.Unequip(ModSlot.V), Is.True);
            CollectionAssert.AreEqual(new[] { _strike }, _inventory.Stored);

            _inventory.Equip(_strike, ModSlot.V);
            foreach (var mod in new[] { _dash, _blink, _metal, _pogo }) _inventory.Add(mod);
            Assert.That(_inventory.Unequip(ModSlot.V), Is.False);
            Assert.That(_inventory.GetEquipped(ModSlot.V), Is.EqualTo(_strike));
        }

        [Test]
        public void Clear_EmptiesEverything()
        {
            _inventory.Add(_strike);
            _inventory.Add(_metal);
            _inventory.Equip(_metal, ModSlot.Passive2);
            _inventory.Clear();

            CollectionAssert.IsEmpty(_inventory.All);
        }
    }
}
