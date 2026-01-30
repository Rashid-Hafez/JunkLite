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

        [Header("Combo Settings")]
        [Tooltip("Time after attack ENDS before combo resets (the combo input window)")]
        [SerializeField] private float comboWindow = 0.6f;

        [Header("Debug")]
        [SerializeField] private bool logCombo = false;

        private int sideComboIndex = 0;
        private float comboTimer = 0f;
        private bool comboTimerActive = false;

        private Rigidbody ownerRb;

        // Mod system
        private readonly List<ActiveMod> activeMods = new();
        public System.Action OnModsChanged;

        // Public combo state for debugging
        public int CurrentComboIndex => sideComboIndex;
        public float ComboTimeRemaining => comboTimerActive ? Mathf.Max(0, comboWindow - comboTimer) : 0f;

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
            // Only tick combo timer when it's active (after attack completes)
            if (comboTimerActive)
            {
                comboTimer += Time.deltaTime;
                if (comboTimer >= comboWindow)
                {
                    Log($"Combo window expired - resetting from {sideComboIndex} to 0");
                    ResetSideCombo();
                }
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
        /// </summary>
        public bool TryGetComboStep(AttackDirection dir, out WeaponComboData.ComboStep step, out int comboIndex)
        {
            step = default;
            comboIndex = -1;

            if (weaponData == null || weaponData.comboData == null)
                return false;

            // Stop combo timer while attacking
            comboTimerActive = false;

            if (dir == AttackDirection.Side)
            {
                var sideSteps = weaponData.comboData.sideComboSteps;
                if (sideSteps == null || sideSteps.Length == 0)
                    return false;

                // Clamp index in case combo data changed
                if (sideComboIndex >= sideSteps.Length)
                    sideComboIndex = 0;

                comboIndex = sideComboIndex;
                step = sideSteps[sideComboIndex];

                Log($"Side attack - combo {sideComboIndex + 1}/{sideSteps.Length}");
            }
            else
            {
                // Up/Down attacks reset side combo
                if (sideComboIndex > 0)
                {
                    Log($"Up/Down attack - resetting side combo from {sideComboIndex}");
                    ResetSideCombo();
                }

                step = dir == AttackDirection.Up
                    ? weaponData.comboData.upAttack
                    : weaponData.comboData.downAttack;
            }

            return true;
        }

        /// <summary>
        /// Called by WeaponManager when attack animation completes.
        /// This advances the combo and starts the combo window timer.
        /// </summary>
        public void OnAttackComplete(AttackDirection dir)
        {
            if (dir == AttackDirection.Side)
            {
                AdvanceSideCombo();
            }

            // Start combo window timer
            comboTimer = 0f;
            comboTimerActive = true;

            Log($"Attack complete - combo window started ({comboWindow}s), next combo index: {sideComboIndex}");
        }

        /// <summary>
        /// Called when attack is interrupted (dash, stun, etc.)
        /// Resets combo state.
        /// </summary>
        public void OnAttackInterrupted()
        {
            Log($"Attack interrupted - resetting combo from {sideComboIndex}");
            ResetSideCombo();
        }

        private void AdvanceSideCombo()
        {
            var sideSteps = weaponData?.comboData?.sideComboSteps;
            if (sideSteps == null || sideSteps.Length == 0)
                return;

            sideComboIndex++;

            // Wrap around to beginning
            if (sideComboIndex >= sideSteps.Length)
            {
                Log($"Combo complete! Wrapping from {sideComboIndex} to 0");
                sideComboIndex = 0;
            }
        }

        private void ResetSideCombo()
        {
            sideComboIndex = 0;
            comboTimer = 0f;
            comboTimerActive = false;
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

        private void Log(string message)
        {
            if (logCombo)
                Debug.Log($"[WeaponInstance] {message}", this);
        }
    }

    public enum AttackHitResult
    {
        None,
        Enemy,
        Environment
    }
}