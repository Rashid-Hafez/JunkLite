using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace junklite.Tests
{
    public sealed class BrokenModTests
    {
        private readonly System.Collections.Generic.List<Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [Test]
        public void ModBreaksAtZeroDurabilityAndCannotBeEquippedAgain()
        {
            var mod = Own(ScriptableObject.CreateInstance<EnergyWaveMod>());
            mod.maxDurability = 10f;
            mod.durabilityPerUse = 5f;
            var instance = new ModInstance(mod);
            instance.ConsumeDurability();
            instance.ConsumeDurability();
            Assert.That(instance.IsBroken, Is.True, "10 durability, 5 per use: two uses break it");

            var root = Own(new GameObject("Broken Mod Test"));
            root.SetActive(false);
            var manager = root.AddComponent<ModManager>();
            typeof(ModManager).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(manager, null);
            manager.ApplyUnlockedSlots(1, 0);

            Assert.That(manager.EquipModAt(instance, true, 1), Is.False, "a broken mod never enters a slot");
            Assert.That(manager.EquipModAny(instance), Is.False, "a broken mod never auto-equips");
            Assert.That(manager.GetActiveMod(1), Is.Null);
        }

        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
    }
}
