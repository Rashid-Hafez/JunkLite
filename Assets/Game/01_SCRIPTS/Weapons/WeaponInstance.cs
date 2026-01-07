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

        // Simplified mod system
        private readonly List<ActiveMod> activeMods = new();
        public System.Action OnModsChanged;

        public event System.Action<AttackDirection, WeaponComboData.ComboStep, int> OnAttack;

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
        // MAIN ATTACK ENTRY (CALLED BY WEAPON HOLDER)
        // ==================================================

        public void ExecuteAttack(AttackDirection dir)
        {
            if (weaponData == null || weaponData.comboData == null)
                return;

            if (!CanAttack)
                return;

            lastAttackTime = Time.time;

            WeaponComboData.ComboStep step;
            int comboIndex;

            if (dir == AttackDirection.Side)
            {
                comboIndex = sideComboIndex;
                step = weaponData.comboData.sideComboSteps[sideComboIndex];
                AdvanceSideCombo();
            }
            else
            {
                comboIndex = -1;
                ResetSideCombo();
                step = dir == AttackDirection.Up
                    ? weaponData.comboData.upAttack
                    : weaponData.comboData.downAttack;
            }

            OnAttack?.Invoke(dir, step, comboIndex);
        }

        // ==================================================
        // COMBO STEP SELECTION
        // ==================================================

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
        // MOD SYSTEM (Simplified)
        // ==================================================

        public int MaxModSlots => weaponData != null ? weaponData.maxActiveModSlots : 0;
        public bool HasFreeSlot => activeMods.Count < MaxModSlots;
        public IReadOnlyList<ActiveMod> GetMods() => activeMods;

        /// <summary>
        /// Add a mod to this weapon.
        /// </summary>
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

        /// <summary>
        /// Remove a mod from this weapon.
        /// </summary>
        public void RemoveMod(ActiveMod mod)
        {
            if (!activeMods.Contains(mod))
                return;

            mod.data.OnUnequip(this);
            activeMods.Remove(mod);
            OnModsChanged?.Invoke();

            Debug.Log($"[Weapon] Mod removed: {mod.data.modName}");
        }

        /// <summary>
        /// Called by WeaponManager when weapon hits something.
        /// Triggers all mod OnHit effects and consumes durability.
        /// </summary>
        public void TriggerModsOnHit(EnemyCharacter enemy, PlayerCharacter player)
        {
            // Iterate backwards in case a mod breaks and gets removed
            for (int i = activeMods.Count - 1; i >= 0; i--)
            {
                var mod = activeMods[i];

                // Only consume durability if effect was actually used
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