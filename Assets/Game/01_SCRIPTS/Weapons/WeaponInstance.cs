using UnityEngine;
using System.Collections.Generic;

namespace junklite
{
    public class WeaponInstance : MonoBehaviour
    {
        [Header("Data (Assigned in Prefab)")]
        public WeaponData weaponData;

        [Header("Stats")]
        public float baseDamage;
        public float baseAttackSpeed;

        [Header("Attack Timing")]
        [SerializeField] private float attackCooldown = 0.5f;

        private float lastAttackTime = -999f;
        public bool CanAttack => Time.time >= lastAttackTime + attackCooldown;

        [Header("Combo Settings")]
        [SerializeField] private float comboResetTime = 0.45f;

        private int sideComboIndex = 0;
        private float comboTimer = 0f;

        private Rigidbody ownerRb;

        // Mod system
        private readonly List<ActiveMod> activeMods = new();
        public System.Action OnModsChanged;

        private void Start()
        {
            if (weaponData == null || weaponData.comboData == null)
            {
                Debug.LogError($"WeaponInstance '{name}' missing WeaponData / ComboData");
                return;
            }

            baseDamage = weaponData.baseDamage;
            baseAttackSpeed = weaponData.baseAttackSpeed;
        }

        private void Update()
        {
            if (sideComboIndex > 0)
            {
                comboTimer += Time.deltaTime;
                if (comboTimer >= comboResetTime)
                    ResetSideCombo();
            }
        }

        public void SetOwnerRigidbody(Rigidbody rb)
        {
            ownerRb = rb;
        }

        // ==================================================
        // COMBO STEP RETRIEVAL
        // ==================================================

        /// <summary>
        /// Gets the current combo step for the attack direction.
        /// Returns the step data and combo index (-1 for up/down attacks).
        /// Returns false if attack is on cooldown.
        /// </summary>
        public bool TryGetComboStep(AttackDirection dir, out WeaponComboData.ComboStep step, out int comboIndex)
        {
            step = default;
            comboIndex = -1;

            if (weaponData == null || weaponData.comboData == null)
                return false;

            if (!CanAttack)
                return false;

            lastAttackTime = Time.time;

            if (dir == AttackDirection.Side)
            {
                comboIndex = sideComboIndex;
                step = weaponData.comboData.sideComboSteps[sideComboIndex];
                AdvanceSideCombo();
            }
            else
            {
                ResetSideCombo();
                step = dir == AttackDirection.Up
                    ? weaponData.comboData.upAttack
                    : weaponData.comboData.downAttack;
            }

            return true;
        }

        private void AdvanceSideCombo()
        {
            comboTimer = 0f;
            sideComboIndex++;

            if (sideComboIndex >= weaponData.comboData.sideComboSteps.Length)
                sideComboIndex = 0;
        }

        private void ResetSideCombo()
        {
            sideComboIndex = 0;
            comboTimer = 0f;
        }

        // ==================================================
        // MOD SYSTEM
        // ==================================================

        public int MaxModSlots => weaponData != null ? weaponData.maxActiveModSlots : 0;
        public bool HasFreeSlot => activeMods.Count < MaxModSlots;
        public IReadOnlyList<ActiveMod> GetMods() => activeMods;

        public bool TryAddMod(ModData modData)
        {
            if (modData == null || !HasFreeSlot)
                return false;

            var activeMod = new ActiveMod(modData);
            activeMods.Add(activeMod);

            modData.OnEquip(this);
            OnModsChanged?.Invoke();

            Debug.Log($"[Weapon] Mod added: {modData.modName}");
            return true;
        }

        public bool TryAddActiveMod(ActiveMod mod)
        {
            if (mod == null || !HasFreeSlot)
                return false;

            activeMods.Add(mod);

            mod.data.OnEquip(this);
            OnModsChanged?.Invoke();

            Debug.Log($"[Weapon] Mod equipped: {mod.data.modName} (durability: {mod.DurabilityPercent:P0})");
            return true;
        }

        public void RemoveMod(ActiveMod mod)
        {
            if (!activeMods.Contains(mod))
                return;

            mod.data.OnUnequip(this);
            activeMods.Remove(mod);
            OnModsChanged?.Invoke();

            Debug.Log($"[Weapon] Mod removed: {mod.data.modName}");
        }

        public void TriggerModsOnHit(EnemyCharacter enemy, PlayerCharacter player)
        {
            for (int i = activeMods.Count - 1; i >= 0; i--)
            {
                var mod = activeMods[i];

                bool effectUsed = mod.data.OnHit(this, enemy, player);

                if (effectUsed)
                {
                    mod.ConsumeDurability(mod.data.durabilityPerHit);

                    if (mod.IsBroken)
                    {
                        Debug.Log($"[Weapon] Mod broke: {mod.data.modName}");
                        RemoveMod(mod);
                    }
                }
            }

            OnModsChanged?.Invoke();
        }
    }

    public enum AttackHitResult
    {
        None,
        Enemy,
        Environment
    }
}