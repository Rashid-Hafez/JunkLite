using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Fixed-size inventory storage for unequipped mods.
    /// Uses a fixed array with null = empty slot, same pattern as ModManager.
    /// </summary>
    public class InventoryComponent : MonoBehaviour
    {
        [SerializeField] private int slotCount = 12;

        private ModInstance[] slots;

        public event System.Action OnInventoryChanged;

        public int SlotCount => slotCount;

        /// <summary>Read-only access to the backing array. Null entries = empty slots.</summary>
        public ModInstance[] Slots => slots;

        private void Awake()
        {
            slots = new ModInstance[slotCount];
        }

        #region Add / Remove

        /// <summary>Add a mod to the first available empty slot.</summary>
        public bool AddMod(ModInstance mod)
        {
            if (mod == null) return false;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                {
                    slots[i] = mod;
                    OnInventoryChanged?.Invoke();
                    return true;
                }
            }

            return false; // inventory full
        }

        /// <summary>Add a mod from data to the first available empty slot.</summary>
        public bool AddMod(ModData data)
        {
            if (data == null) return false;
            return AddMod(new ModInstance(data));
        }

        /// <summary>Place a mod at a specific slot index.</summary>
        public bool InsertMod(ModInstance mod, int index)
        {
            if (mod == null) return false;
            if (index < 0 || index >= slots.Length) return false;
            if (slots[index] != null) return false; // slot occupied

            slots[index] = mod;
            OnInventoryChanged?.Invoke();
            return true;
        }

        /// <summary>Remove a specific mod instance (finds it by reference).</summary>
        public bool RemoveMod(ModInstance mod)
        {
            if (mod == null) return false;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == mod)
                {
                    slots[i] = null;
                    OnInventoryChanged?.Invoke();
                    return true;
                }
            }

            return false;
        }

        /// <summary>Remove and return the mod at a given index.</summary>
        public ModInstance RemoveModAt(int index)
        {
            if (index < 0 || index >= slots.Length) return null;

            var mod = slots[index];
            if (mod == null) return null;

            slots[index] = null;
            OnInventoryChanged?.Invoke();
            return mod;
        }

        #endregion

        #region Query

        public ModInstance GetModAt(int index)
        {
            if (index < 0 || index >= slots.Length) return null;
            return slots[index];
        }

        public bool HasMod(ModInstance mod)
        {
            if (mod == null) return false;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == mod) return true;
            return false;
        }

        /// <summary>Number of non-null mods currently stored.</summary>
        public int StoredModCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i] != null) count++;
                return count;
            }
        }

        #endregion

        #region Swap

        public void SwapMods(int indexA, int indexB)
        {
            if (indexA < 0 || indexA >= slots.Length) return;
            if (indexB < 0 || indexB >= slots.Length) return;
            if (indexA == indexB) return;

            (slots[indexA], slots[indexB]) = (slots[indexB], slots[indexA]);
            OnInventoryChanged?.Invoke();
        }

        #endregion
    }
}