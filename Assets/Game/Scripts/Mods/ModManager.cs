using UnityEngine;
using System;

namespace junklite
{
    public class ModManager : MonoBehaviour
    {
        #region Fields

        [Header("Slot Limits")]
        [SerializeField] private int maxActiveSlots = 4;
        [SerializeField] private int maxPassiveSlots = 2;

        [Header("Starting Slots")]
        [SerializeField] private int unlockedActiveSlots = 1;
        [SerializeField] private int unlockedPassiveSlots = 1;

        // Equipped mods
        private ModInstance[] activeSlots;
        private ModInstance[] passiveSlots;

        // References
        private WeaponManager weaponManager;
        private PlayerCharacter playerCharacter;

        private bool isActive;

        #endregion

        #region Properties

        public int UnlockedActiveSlots => unlockedActiveSlots;
        public int UnlockedPassiveSlots => unlockedPassiveSlots;
        public bool IsActive => isActive;

        public event Action OnModSlotsChanged;
        public event Action<int> OnActiveModActivated;
        public event Action<int> OnActiveModReady;

        #endregion

        #region Unity

        private void Awake()
        {
            weaponManager = GetComponent<WeaponManager>();
            playerCharacter = GetComponent<PlayerCharacter>();

            activeSlots = new ModInstance[maxActiveSlots];
            passiveSlots = new ModInstance[maxPassiveSlots];
        }

        private void OnEnable()
        {
            if (weaponManager != null)
            {
                weaponManager.OnCombatModeChanged += OnCombatModeChanged;
                weaponManager.OnEnemyHit += OnEnemyHit;
            }
        }

        private void OnDisable()
        {
            if (weaponManager != null)
            {
                weaponManager.OnCombatModeChanged -= OnCombatModeChanged;
                weaponManager.OnEnemyHit -= OnEnemyHit;
            }
        }

        #endregion

        #region Combat Mode

        private void OnCombatModeChanged()
        {
            if (weaponManager.IsModCombat)
                Activate();
            else
                Deactivate();
        }

        private void Activate()
        {
            isActive = true;

            for (int i = 0; i < unlockedPassiveSlots; i++)
            {
                var mod = passiveSlots[i];
                if (mod != null && !mod.IsBroken && mod.Data is PassiveModData passive)
                    passive.OnEquip(playerCharacter);
            }

            for (int i = 0; i < unlockedActiveSlots; i++)
            {
                var mod = activeSlots[i];
                if (mod != null && !mod.IsBroken && mod.Data is ActiveModData active)
                    active.OnEquip(playerCharacter);
            }

            OnModSlotsChanged?.Invoke();
        }

        private void Deactivate()
        {
            for (int i = 0; i < unlockedPassiveSlots; i++)
            {
                var mod = passiveSlots[i];
                if (mod != null && mod.Data is PassiveModData passive)
                    passive.OnUnequip(playerCharacter);
            }

            for (int i = 0; i < unlockedActiveSlots; i++)
            {
                var mod = activeSlots[i];
                if (mod != null && mod.Data is ActiveModData active)
                    active.OnUnequip(playerCharacter);
            }

            isActive = false;
            OnModSlotsChanged?.Invoke();
        }

        #endregion

        #region Hit Notification

        private void OnEnemyHit(EnemyCharacter enemy, float damageDealt)
        {
            if (!isActive) return;

            for (int i = 0; i < unlockedPassiveSlots; i++)
            {
                var mod = passiveSlots[i];
                if (mod == null || mod.IsBroken) continue;
                if (mod.Data is PassiveModData passive)
                {
                    passive.OnHitRegistered(mod, playerCharacter, enemy, damageDealt);

                    if (mod.IsBroken)
                    {
                        passive.OnUnequip(playerCharacter);
                        passiveSlots[i] = null;
                        Debug.Log($"[ModManager] Passive mod broke: {mod.Data.modName}");
                        OnModSlotsChanged?.Invoke();
                    }
                }
            }

            for (int i = 0; i < unlockedActiveSlots; i++)
            {
                var mod = activeSlots[i];
                if (mod == null || mod.IsBroken) continue;
                if (mod.Data is ActiveModData active)
                {
                    bool wasReady = active.CanActivate(mod, playerCharacter);
                    active.OnHitRegistered(mod, playerCharacter, enemy, damageDealt);
                    bool nowReady = active.CanActivate(mod, playerCharacter);

                    if (!wasReady && nowReady)
                        OnActiveModReady?.Invoke(i);
                }
            }
        }

        #endregion

        #region Activation

        /// <summary>
        /// Try to activate an active mod by slot index. Called from player input.
        /// </summary>
        public bool TryActivateMod(int activeSlotIndex)
        {
            if (!isActive) return false;
            if (activeSlotIndex < 0 || activeSlotIndex >= unlockedActiveSlots) return false;
            var mod = activeSlots[activeSlotIndex];
            if (mod == null || mod.IsBroken) return false;
            if (mod.Data is not ActiveModData active) return false;

            bool used = active.TryActivate(mod, playerCharacter);
            if (used)
            {
                mod.ConsumeDurability();
                OnActiveModActivated?.Invoke(activeSlotIndex);
                if (mod.IsBroken)
                {
                    active.OnUnequip(playerCharacter);
                    activeSlots[activeSlotIndex] = null;
                    Debug.Log($"[ModManager] Active mod broke: {mod.Data.modName}");
                }
                OnModSlotsChanged?.Invoke();
            }
            return used;
        }

        #endregion

        #region Equip / Unequip

        /// <summary>
        /// Place a mod directly into a specific slot. Validates mod type matches slot type.
        /// </summary>
        public bool EquipModAt(ModInstance mod, bool isActiveSlot, int slotIndex)
        {
            if (mod == null) return false;

            // Type restriction: active mods → active slots, passive mods → passive slots
            if (isActiveSlot && !mod.IsActive) return false;
            if (!isActiveSlot && !mod.IsPassive) return false;

            var slots = isActiveSlot ? activeSlots : passiveSlots;
            int max = isActiveSlot ? unlockedActiveSlots : unlockedPassiveSlots;

            if (slotIndex < 0 || slotIndex >= max) return false;
            if (slots[slotIndex] != null) return false; // slot occupied

            slots[slotIndex] = mod;

            // Fire equip callbacks if combat is active
            if (isActive)
            {
                if (mod.Data is ActiveModData active) active.OnEquip(playerCharacter);
                else if (mod.Data is PassiveModData passive) passive.OnEquip(playerCharacter);
            }

            OnModSlotsChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Place a mod in the first available slot matching its type.
        /// Active mods → active slots only. Passive mods → passive slots only.
        /// Returns false if no compatible slot is available (mod should go to inventory).
        /// </summary>
        public bool EquipModAny(ModInstance mod)
        {
            if (mod == null) return false;

            if (mod.IsActive)
            {
                for (int i = 0; i < unlockedActiveSlots; i++)
                {
                    if (activeSlots[i] == null)
                        return EquipModAt(mod, true, i);
                }
            }
            else if (mod.IsPassive)
            {
                for (int i = 0; i < unlockedPassiveSlots; i++)
                {
                    if (passiveSlots[i] == null)
                        return EquipModAt(mod, false, i);
                }
            }

            return false; // no compatible slot — caller should put in inventory
        }

        /// <summary>
        /// Keep old name working for any code that calls TryEquipMod.
        /// </summary>
        public bool TryEquipMod(ModInstance mod) => EquipModAny(mod);

        public ModInstance UnequipMod(bool isActiveSlot, int slotIndex)
        {
            var slots = isActiveSlot ? activeSlots : passiveSlots;
            int max = isActiveSlot ? unlockedActiveSlots : unlockedPassiveSlots;

            if (slotIndex < 0 || slotIndex >= max) return null;

            ModInstance mod = slots[slotIndex];
            if (mod == null) return null;

            // Fire unequip callbacks if combat is active
            if (isActive)
            {
                if (mod.Data is ActiveModData active) active.OnUnequip(playerCharacter);
                else if (mod.Data is PassiveModData passive) passive.OnUnequip(playerCharacter);
            }

            slots[slotIndex] = null;
            OnModSlotsChanged?.Invoke();
            return mod;
        }

        public ModInstance GetActiveMod(int index)
        {
            if (index < 0 || index >= activeSlots.Length) return null;
            return activeSlots[index];
        }

        public ModInstance GetPassiveMod(int index)
        {
            if (index < 0 || index >= passiveSlots.Length) return null;
            return passiveSlots[index];
        }

        #endregion

        #region Slot Management

        public void SwapSlots(bool isActive, int indexA, int indexB)
        {
            var slots = isActive ? activeSlots : passiveSlots;
            int max = isActive ? unlockedActiveSlots : unlockedPassiveSlots;

            if (indexA < 0 || indexA >= max) return;
            if (indexB < 0 || indexB >= max) return;

            (slots[indexA], slots[indexB]) = (slots[indexB], slots[indexA]);
            OnModSlotsChanged?.Invoke();
        }

        public void UnlockActiveSlot()
        {
            if (unlockedActiveSlots < maxActiveSlots)
            {
                unlockedActiveSlots++;
                OnModSlotsChanged?.Invoke();
            }
        }

        public void UnlockPassiveSlot()
        {
            if (unlockedPassiveSlots < maxPassiveSlots)
            {
                unlockedPassiveSlots++;
                OnModSlotsChanged?.Invoke();
            }
        }

        #endregion

        #region Helpers

        public bool HasEmptyActiveSlot()
        {
            for (int i = 0; i < unlockedActiveSlots; i++)
                if (activeSlots[i] == null) return true;
            return false;
        }

        public bool HasEmptyPassiveSlot()
        {
            for (int i = 0; i < unlockedPassiveSlots; i++)
                if (passiveSlots[i] == null) return true;
            return false;
        }

        #endregion
    }
}