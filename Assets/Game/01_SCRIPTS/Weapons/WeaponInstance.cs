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
        [SerializeField] private float comboResetTime = 0.45f;

        private int sideComboIndex = 0;
        private float comboTimer = 0f;

        private Rigidbody ownerRb;

        private readonly List<ModRuntimeInstance> activeMods = new();
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
        // MAIN ATTACK ENTRY (CALLED BY WEAPON HOLDER)
        // ==================================================

       public AttackHitResult TryAttack(AttackDirection dir, Vector3 hitPosition, float radius, LayerMask enemyLayer, LayerMask environmentLayer, float facing)
       {
            if (weaponData == null || weaponData.comboData == null)
                return AttackHitResult.None;
            
            WeaponComboData.ComboStep step = GetComboStep(dir);
            float finalRadius = step.hitRadius > 0f ? step.hitRadius: radius;

            Collider[] hits = Physics.OverlapSphere(hitPosition, finalRadius, enemyLayer | environmentLayer);

            AttackHitResult result = AttackHitResult.None;
            Vector3 contactPoint = Vector3.zero;
            float closestDist = float.MaxValue;

            foreach (var col in hits)
            {
                int layerMask = 1 << col.gameObject.layer;

                // Find closest contact point
                Vector3 point = col.ClosestPoint(hitPosition);
                float dist = Vector3.SqrMagnitude(point - hitPosition);

                if (dist < closestDist)
                {
                    closestDist = dist;
                    contactPoint = point;
                }

                if ((layerMask & enemyLayer) != 0)
                {
                    result = AttackHitResult.Enemy;
                    break; // enemy priority
                }

                if ((layerMask & environmentLayer) != 0)
                {
                    result = AttackHitResult.Environment;
                }
            }

            // =========================
            // SLASH + HIT PARTICLES
            // =========================
            WeaponHolder holder = GetComponentInParent<WeaponHolder>();
            
            if (holder != null && step.slashPrefab != null)
            {
               Transform anchor = holder.GetAttackTransform(dir);

            if (result != AttackHitResult.None)
            {
                // Hit → spawn slash at contact point + hit effect
                holder.PlaySlashAt(step.slashPrefab, anchor, contactPoint);

                holder.PlayHitEffect(contactPoint);
            }
            else
            {
            // No hit → normal slash
            holder.PlaySlash(
                step.slashPrefab,
                anchor
            );
            }
            }

            return result;
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
