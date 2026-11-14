using System;
using System.Collections.Generic;
using Junklite.Inventory;
using UnityEngine;
using UnityEngine.EventSystems;

namespace junklite
{
    /// <summary>
    /// Shows the player's real mods on the UI Toolkit inventory screen (Assets/Junklite/Inventory).
    /// The game owns the data: <see cref="InventoryComponent"/> is storage, <see cref="ModManager"/> is the
    /// slots. This mirrors both into the screen's <see cref="ModInventory"/> on open, and pushes every change
    /// the player makes on the screen back through ModManager's equip/unequip API, so install/remove hooks run
    /// exactly as before. Slot Z X C V = active slots 0-3, Passive 1/2 = passive slots 0-1.
    ///
    /// Lives on the inventory screen prefab (inactive). <see cref="PlayerUI"/> calls Open/Close.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InventoryScreenBridge : MonoBehaviour
    {
        const int ActiveSlots = 4;

        [Serializable]
        private struct IconOverride
        {
            public ModData mod;
            [Tooltip("Large icon to show on the inventory screen instead of the mod's sprite.")]
            public Texture2D icon;
        }

        [SerializeField] private InventoryScreen screen;
        [SerializeField] private ModInventory mods;

        [Tooltip("Optional: per-mod icons for the inventory screen. Mods not listed use their own icon sprite.")]
        [SerializeField] private List<IconOverride> iconOverrides = new();

        // One display definition per runtime mod, kept for the screen's lifetime so the screen's
        // selection and "seen" state survive re-syncs.
        private readonly Dictionary<ModInstance, ModDefinition> definitions = new();
        private readonly Dictionary<ModDefinition, ModInstance> instances = new();
        private readonly ModInstance[] desired = new ModInstance[ModInventory.SlotCount];
        private readonly List<ModInstance> loose = new();
        private readonly HashSet<ModInstance> scratch = new();

        private InventoryComponent storage;
        private ModManager modManager;
        private AttributeManager attributes;
        private Attribute armor;
        private bool syncing;

        public bool IsOpen => gameObject.activeSelf;

        private void Awake()
        {
            if (screen == null) screen = GetComponent<InventoryScreen>();
            if (mods == null) mods = GetComponent<ModInventory>();
        }

        // -----------------------------------------------------------------------

        public void Open(PlayerCharacter player)
        {
            if (player == null || mods == null) return;
            Unsubscribe();

            storage =player.GetComponent<InventoryComponent>();
            modManager = player.GetComponent<ModManager>();
            attributes = player.GetComponent<AttributeManager>();
            armor = player.RuntimeStats?.ArmorDisplay ?? attributes?.Get(AttributeType.Armor);

            if (storage == null || modManager == null)
            {
                Debug.LogWarning("[InventoryScreenBridge] Player has no InventoryComponent or ModManager.", this);
                return;
            }

            // Fill the model before the screen enables, so its first draw is already correct.
            Pull();
            mods.Changed += HandleScreenChanged;
            storage.OnInventoryChanged += HandleGameChanged;
            modManager.OnModSlotsChanged += HandleGameChanged;

            // uGUI may still hold a selection; UI Toolkit owns focus while this is open.
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            gameObject.SetActive(true);
            // Always open on the mods; Upgrades is opened on purpose with ShowTab.
            if (screen != null && screen.Tab != InventoryTab.Inventory) screen.ShowTab(InventoryTab.Inventory);
            RefreshVitals();
        }

        /// <summary>Switches the open screen to a tab (e.g. Upgrades after a level-up or a story beat).</summary>
        public void ShowTab(InventoryTab tab)
        {
            if (IsOpen && screen != null) screen.ShowTab(tab);
        }

        public void Close()
        {
            Unsubscribe();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Fills the screen's model from the player without opening it (the pause screen's build strip).
        /// While the inventory is open the model is already live.
        /// </summary>
        public ModInventory SyncForDisplay(PlayerCharacter player)
        {
            if (IsOpen || player == null || mods == null) return mods;
            storage = player.GetComponent<InventoryComponent>();
            modManager = player.GetComponent<ModManager>();
            if (storage != null && modManager != null) Pull();
            storage = null;
            modManager = null;
            return mods;
        }

        /// <summary>Esc / back: lets the screen drop a picked-up mod or leave Missions first. True if it used the press.</summary>
        public bool HandleBack() => IsOpen && screen != null && screen.HandleBack();

        // Covers the HUD being destroyed or disabled while the screen is open.
        private void OnDisable() => Unsubscribe();

        private void Unsubscribe()
        {
            if (mods != null) mods.Changed -= HandleScreenChanged;
            if (storage != null) storage.OnInventoryChanged -= HandleGameChanged;
            if (modManager != null) modManager.OnModSlotsChanged -= HandleGameChanged;

            storage = null;
            modManager = null;
            attributes = null;
            armor = null;
        }

        private void OnDestroy()
        {
            foreach (var definition in definitions.Values)
                if (definition != null) Destroy(definition);
            definitions.Clear();
            instances.Clear();
        }

        // -----------------------------------------------------------------------
        // Game -> screen

        private void HandleGameChanged()
        {
            if (syncing || Matches()) return;
            Pull();
            RefreshVitals();
        }

        private void Pull()
        {
            syncing = true;
            try
            {
                mods.Clear();
                for (int i = 0; i < ModInventory.SlotCount; i++) mods.SetLocked((ModSlot)i, false);

                // Room for the equipped mods to pass through storage while they're placed.
                mods.Capacity = storage.SlotCount + ModInventory.SlotCount;

                ModInstance[] stored = storage.Slots;
                for (int i = 0; i < stored.Length; i++)
                    if (stored[i] != null) mods.Add(DefinitionFor(stored[i]));

                for (int i = 0; i < ModInventory.SlotCount; i++)
                {
                    ModInstance equipped = GameSlot(i);
                    if (equipped == null) continue;
                    ModDefinition definition = DefinitionFor(equipped);
                    mods.Add(definition);
                    mods.Equip(definition, (ModSlot)i);
                }

                mods.Capacity = storage.SlotCount;
                for (int i = 0; i < ModInventory.SlotCount; i++)
                    mods.SetLocked((ModSlot)i, !IsUnlocked(i));
            }
            finally
            {
                syncing = false;
            }
            PruneDefinitions();
        }

        // -----------------------------------------------------------------------
        // Screen -> game

        private void HandleScreenChanged()
        {
            if (syncing || storage == null || modManager == null) return;

            syncing = true;
            try
            {
                Push();
            }
            finally
            {
                syncing = false;
            }

            // A rejected move (a locked or invalid slot) leaves the game as it was; show the truth.
            if (!Matches()) Pull();
        }

        private void Push()
        {
            loose.Clear();
            for (int i = 0; i < ModInventory.SlotCount; i++)
            {
                ModDefinition definition = mods.GetEquipped((ModSlot)i);
                desired[i] = definition != null && instances.TryGetValue(definition, out var instance) ? instance : null;
            }

            // 1. Take out every mod that is leaving its slot (runs OnRemoved).
            for (int i = 0; i < ModInventory.SlotCount; i++)
            {
                if (!IsUnlocked(i)) continue;
                ModInstance current = GameSlot(i);
                if (current == null || current == desired[i]) continue;
                ModInstance removed = modManager.UnequipMod(IsActiveSlot(i), LocalIndex(i));
                if (removed != null) loose.Add(removed);
            }

            // 2. Install every mod that is arriving (runs OnInstalled).
            for (int i = 0; i < ModInventory.SlotCount; i++)
            {
                ModInstance wanted = desired[i];
                if (wanted == null || !IsUnlocked(i) || GameSlot(i) != null) continue;

                bool fromStorage = storage.RemoveMod(wanted);
                loose.Remove(wanted);
                if (!modManager.EquipModAt(wanted, IsActiveSlot(i), LocalIndex(i)))
                {
                    if (fromStorage || !loose.Contains(wanted)) loose.Add(wanted);
                }
            }

            // 3. Whatever came out and didn't go back in returns to storage.
            for (int i = 0; i < loose.Count; i++)
            {
                ModInstance mod = loose[i];
                if (IsInAnyGameSlot(mod) || storage.HasMod(mod)) continue;
                if (!storage.AddMod(mod))
                    Debug.LogWarning($"[InventoryScreenBridge] Storage full; {mod.Data.modName} could not be stored.", this);
            }
            loose.Clear();
        }

        // -----------------------------------------------------------------------
        // Helpers

        /// <summary>True when the screen shows exactly what the game holds (storage order ignored).</summary>
        private bool Matches()
        {
            if (storage == null || modManager == null) return true;

            for (int i = 0; i < ModInventory.SlotCount; i++)
            {
                ModInstance game = GameSlot(i);
                ModDefinition shown = mods.GetEquipped((ModSlot)i);
                if ((game == null) != (shown == null)) return false;
                if (game != null && (!definitions.TryGetValue(game, out var definition) || definition != shown)) return false;
                if (mods.IsLocked((ModSlot)i) == IsUnlocked(i)) return false;
            }

            scratch.Clear();
            foreach (ModInstance mod in storage.Slots)
                if (mod != null) scratch.Add(mod);
            if (scratch.Count != mods.StoredCount) return false;
            foreach (ModDefinition definition in mods.Stored)
                if (!instances.TryGetValue(definition, out var instance) || !scratch.Contains(instance)) return false;
            return true;
        }

        private ModDefinition DefinitionFor(ModInstance instance)
        {
            if (definitions.TryGetValue(instance, out var existing)) return existing;

            ModData data = instance.Data;
            float cooldown = data is ActiveModData active ? active.cooldown : 0f;
            string damage = data.baseDamage > 0f ? data.baseDamage.ToString("0.#") : null;

            ModDefinition definition = ModDefinition.Create(
                id: data.name,
                displayName: string.IsNullOrEmpty(data.modName) ? data.name : data.modName,
                rarity: (ModRarity)Mathf.Clamp((int)data.rarity, 0, (int)ModRarity.Elite),
                type: instance.IsPassive ? ModType.Passive : ModType.Active,
                cooldown: cooldown,
                damage: damage,
                description: data.description,
                icon: IconFor(data));
            definition.hideFlags = HideFlags.DontSave;

            definitions[instance] = definition;
            instances[definition] = instance;
            return definition;
        }

        private Texture2D IconFor(ModData data)
        {
            for (int i = 0; i < iconOverrides.Count; i++)
                if (iconOverrides[i].mod == data && iconOverrides[i].icon != null) return iconOverrides[i].icon;
            return data.icon != null ? data.icon.texture : null;
        }

        /// <summary>Drops definitions for mods the player no longer owns (broken, removed).</summary>
        private void PruneDefinitions()
        {
            scratch.Clear();
            foreach (ModInstance mod in storage.Slots)
                if (mod != null) scratch.Add(mod);
            for (int i = 0; i < ModInventory.SlotCount; i++)
            {
                ModInstance equipped = GameSlot(i);
                if (equipped != null) scratch.Add(equipped);
            }
            if (scratch.Count == definitions.Count) return;

            loose.Clear();
            foreach (ModInstance owned in definitions.Keys)
                if (!scratch.Contains(owned)) loose.Add(owned);
            foreach (ModInstance gone in loose)
            {
                ModDefinition definition = definitions[gone];
                definitions.Remove(gone);
                instances.Remove(definition);
                Destroy(definition);
            }
            loose.Clear();
        }

        private void RefreshVitals()
        {
            if (screen == null || !IsOpen) return;
            Attribute health = attributes != null ? attributes.Health : null;
            if (health != null) screen.SetHealth(health.Current, health.Max);
            else screen.ClearVitals();
            if (armor != null) screen.SetArmor(Mathf.RoundToInt(armor.Current));
        }

        private static bool IsActiveSlot(int slot) => slot < ActiveSlots;
        private static int LocalIndex(int slot) => slot < ActiveSlots ? slot : slot - ActiveSlots;

        private bool IsUnlocked(int slot) => IsActiveSlot(slot)
            ? LocalIndex(slot) < modManager.UnlockedActiveSlots
            : LocalIndex(slot) < modManager.UnlockedPassiveSlots;

        private ModInstance GameSlot(int slot) => IsActiveSlot(slot)
            ? modManager.GetActiveMod(LocalIndex(slot))
            : modManager.GetPassiveMod(LocalIndex(slot));

        private bool IsInAnyGameSlot(ModInstance mod)
        {
            for (int i = 0; i < ModInventory.SlotCount; i++)
                if (GameSlot(i) == mod) return true;
            return false;
        }
    }
}
