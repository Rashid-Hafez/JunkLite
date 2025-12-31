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

        private readonly List<ModRuntimeInstance> activeMods = new();
        public System.Action OnModsChanged;
        internal object spriteRenderer;

        public event System.Action<AttackDirection, WeaponComboData.ComboStep> OnAttack;



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

            if (dir == AttackDirection.Side)
            {
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

            OnAttack?.Invoke(dir, step);
        }

        // ==================================================
        // COMBO STEP SELECTION
        // ==================================================
        private WeaponComboData.ComboStep GetComboStep(AttackDirection dir)
        {
            WeaponComboData combo = weaponData.comboData;

            if (dir == AttackDirection.Side)
            {
                int currentStep = sideComboIndex + 1; 
                Debug.Log($"[WEAPON COMBO] Side Attack Step: {currentStep}");

                WeaponComboData.ComboStep step =
                    combo.sideComboSteps[sideComboIndex];

                AdvanceSideCombo();
                return step;
            }

            // Up / Down → always single hit
            ResetSideCombo();

            return dir == AttackDirection.Up
                ? combo.upAttack
                : combo.downAttack;
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

#region mod system
        // ==================================================
        // MOD SYSTEM 
        // ==================================================
        public int MaxActiveSlots =>
            weaponData != null ? weaponData.maxActiveModSlots : 0;

        public bool HasFreeModSlot =>
            activeMods.Count < MaxActiveSlots;

        public IReadOnlyList<ModRuntimeInstance> GetActiveMods() =>
            activeMods;

        public bool TryAddMod(Mod_Data data)
        {
            if (!HasFreeModSlot)
                return false;

            var runtime = new ModRuntimeInstance(data);
            activeMods.Add(runtime);

            runtime.logic.OnEquip(this);
            OnModsChanged?.Invoke();
            return true;
        }

        public void RemoveMod(ModRuntimeInstance runtime)
        {
            runtime.logic.OnUnequip(this);
            activeMods.Remove(runtime);
            OnModsChanged?.Invoke();
        }

        public void ConsumeModDurability(ModRuntimeInstance runtime, float amount)
        {
            if (!activeMods.Contains(runtime))
                return;

            runtime.Consume(amount);

            if (runtime.IsBroken)
                RemoveMod(runtime);

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
#endregion