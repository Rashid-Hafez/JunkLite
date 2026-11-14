using System;
using System.Collections.Generic;
using UnityEngine;

namespace Junklite.Inventory
{
    /// <summary>The six mod slots around Cas: four active ones bound to Z X C V, and two passive ones.</summary>
    public enum ModSlot
    {
        Z,
        X,
        C,
        V,
        Passive1,
        Passive2,
    }

    public enum EquipResult
    {
        Equipped,
        WrongSlotType,
        NotInInventory,
        SlotLocked,
    }

    /// <summary>
    /// Cas's mods: what's in storage and what's in each slot. Change it from code with
    /// Add, Remove, Equip and Unequip; every change raises <see cref="Changed"/> and the screens redraw.
    /// The lists set in the Inspector are the starting contents.
    /// </summary>
    public class ModInventory : MonoBehaviour
    {
        public const int SlotCount = 6;

        [Min(1)] [SerializeField] int capacity = 30;
        [SerializeField] List<ModDefinition> stored = new List<ModDefinition>();

        [Tooltip("In slot order: Z, X, C, V, Passive 1, Passive 2.")]
        [SerializeField] ModDefinition[] equipped = new ModDefinition[SlotCount];

        // Runtime only: slots the game hasn't unlocked yet. Locked slots keep their mod but can't be changed.
        readonly bool[] locked = new bool[SlotCount];

        public event Action Changed;

        public bool IsLocked(ModSlot slot) => locked[(int)slot];

        public void SetLocked(ModSlot slot, bool value)
        {
            if (locked[(int)slot] == value) return;
            locked[(int)slot] = value;
            Changed?.Invoke();
        }

        public int Capacity
        {
            get => capacity;
            set
            {
                capacity = Mathf.Max(1, value);
                Changed?.Invoke();
            }
        }

        public IReadOnlyList<ModDefinition> Stored => stored;
        public int StoredCount => stored.Count;
        public bool IsFull => stored.Count >= capacity;

        public int EquippedCount
        {
            get
            {
                int count = 0;
                foreach (var mod in equipped) if (mod != null) count++;
                return count;
            }
        }

        /// <summary>Every mod Cas owns, stored and equipped.</summary>
        public IEnumerable<ModDefinition> All
        {
            get
            {
                foreach (var mod in equipped) if (mod != null) yield return mod;
                foreach (var mod in stored) yield return mod;
            }
        }

        public static ModType SlotType(ModSlot slot) => slot <= ModSlot.V ? ModType.Active : ModType.Passive;

        public ModDefinition GetEquipped(ModSlot slot) => equipped[(int)slot];

        public ModSlot? FindSlot(ModDefinition mod)
        {
            if (mod == null) return null;
            for (int i = 0; i < SlotCount; i++)
            {
                if (equipped[i] == mod) return (ModSlot)i;
            }
            return null;
        }

        public bool IsEquipped(ModDefinition mod) => FindSlot(mod).HasValue;

        public bool Contains(ModDefinition mod) => mod != null && (stored.Contains(mod) || IsEquipped(mod));

        /// <summary>Puts a mod in storage. Returns false if storage is full.</summary>
        public bool Add(ModDefinition mod)
        {
            if (mod == null || IsFull) return false;
            stored.Add(mod);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Takes a mod out of storage, or out of its slot if it's equipped.</summary>
        public bool Remove(ModDefinition mod)
        {
            if (mod == null) return false;
            if (!stored.Remove(mod))
            {
                var slot = FindSlot(mod);
                if (!slot.HasValue) return false;
                equipped[(int)slot.Value] = null;
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>Removes the first mod with this id. Handy for save files and debug commands.</summary>
        public bool Remove(string id)
        {
            foreach (var mod in All)
            {
                if (mod.Id == id) return Remove(mod);
            }
            return false;
        }

        /// <summary>
        /// Puts a mod in a slot, from storage or from another slot. Whatever was in the slot goes back
        /// where the new mod came from, so nothing is ever lost and storage never overflows.
        /// </summary>
        public EquipResult Equip(ModDefinition mod, ModSlot slot)
        {
            if (mod == null || !Contains(mod)) return EquipResult.NotInInventory;
            if (mod.Type != SlotType(slot)) return EquipResult.WrongSlotType;

            var previous = equipped[(int)slot];
            if (previous == mod) return EquipResult.Equipped;

            var from = FindSlot(mod);
            if (locked[(int)slot] || (from.HasValue && locked[(int)from.Value])) return EquipResult.SlotLocked;

            if (from.HasValue)
            {
                equipped[(int)from.Value] = previous;
            }
            else
            {
                int index = stored.IndexOf(mod);
                stored.RemoveAt(index);
                if (previous != null) stored.Insert(index, previous);
            }

            equipped[(int)slot] = mod;
            Changed?.Invoke();
            return EquipResult.Equipped;
        }

        /// <summary>Equips into the first empty slot of the right type, or replaces the first one if all are full.</summary>
        public EquipResult Equip(ModDefinition mod)
        {
            if (mod == null) return EquipResult.NotInInventory;
            return Equip(mod, FirstSlotFor(mod.Type));
        }

        /// <summary>Moves a slot's mod back to storage. Returns false if the slot is empty or storage is full.</summary>
        public bool Unequip(ModSlot slot)
        {
            var mod = equipped[(int)slot];
            if (mod == null || IsFull || locked[(int)slot]) return false;
            equipped[(int)slot] = null;
            stored.Add(mod);
            Changed?.Invoke();
            return true;
        }

        /// <summary>Empties storage and every slot.</summary>
        public void Clear()
        {
            stored.Clear();
            Array.Clear(equipped, 0, SlotCount);
            Changed?.Invoke();
        }

        ModSlot FirstSlotFor(ModType type)
        {
            ModSlot? first = null;
            for (int i = 0; i < SlotCount; i++)
            {
                var slot = (ModSlot)i;
                if (SlotType(slot) != type || locked[i]) continue;
                if (equipped[i] == null) return slot;
                first ??= slot;
            }
            // Every slot of this type is locked: hand back the first one so Equip reports SlotLocked.
            return first ?? (type == ModType.Active ? ModSlot.Z : ModSlot.Passive1);
        }

        void OnValidate()
        {
            if (equipped == null || equipped.Length != SlotCount) Array.Resize(ref equipped, SlotCount);
        }
    }
}
