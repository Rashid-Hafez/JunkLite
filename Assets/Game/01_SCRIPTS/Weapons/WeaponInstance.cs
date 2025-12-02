using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    public class WeaponInstance : MonoBehaviour
    {
        [Header("Data (Assigned in Prefab)")]
        public WeaponData weaponData;

        [Header("Stats")]
        public float baseDamage;
        public float baseAttackSpeed;

        [Header("Hitbox")]
        [SerializeField] private Transform hitOrigin;
        [SerializeField] private float hitRadius = 1.0f;
        [SerializeField] private LayerMask enemyMask;

        private readonly List<ModRuntimeInstance> activeMods = new();
        public System.Action OnModsChanged;

        public int MaxActiveSlots =>
            weaponData != null ? weaponData.maxActiveModSlots : 0;

        public bool HasFreeModSlot => activeMods.Count < MaxActiveSlots;

        void Start()
        {
            if (weaponData == null)
            {
                Debug.LogError($"WeaponInstance '{name}' has no WeaponData!");
                return;
            }

            baseDamage = weaponData.baseDamage;
            baseAttackSpeed = weaponData.baseAttackSpeed;
        }

        // ========= ATTACK =========
        public void Attack()
        {
            foreach (var mod in activeMods)
                mod.logic.OnAttackStart(this);

            PerformAttackHit();
        }

        void PerformAttackHit()
        {
            if (!hitOrigin) return;

            Collider[] results = Physics.OverlapSphere(
                hitOrigin.position, hitRadius, enemyMask);

            foreach (var col in results)
            {
                var enemy = col.GetComponent<Enemy>();
                if (!enemy) continue;

                DamageInfo dmg = new DamageInfo(baseDamage, gameObject);

                ApplyModsOnHit(enemy, ref dmg);

                // enemy.TakeDamage(dmg);   // when ready
            }
        }

        private void ApplyModsOnHit(Enemy enemy, ref DamageInfo dmg)
        {
            var mods = activeMods.ToArray(); // safe copy

            foreach (var mod in mods)
            {
                mod.logic.OnHit(this, enemy, ref dmg);

                ConsumeModDurability(mod, mod.data.durabilityCostPerHit);
            }
        }


        public void ConsumeModDurability(ModRuntimeInstance runtime, float amount)
        {
            if (!activeMods.Contains(runtime))
                return;

            runtime.Consume(amount);

            if (runtime.IsBroken)
            {
                RemoveMod(runtime);
                return;
            }

            OnModsChanged?.Invoke();
        }


        /// <summary>
        /// Try to equip a mod onto this weapon.
        /// Returns true if actually equipped.
        /// </summary>
        public bool TryAddMod(Mod_Data data)
        {
            if (!HasFreeModSlot)
            {
                Debug.Log("Weapon has no free mod slots.");
                return false;
            }

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

        public void NotifyModsChanged()
        {
            OnModsChanged?.Invoke();
        }


        public IReadOnlyList<ModRuntimeInstance> GetActiveMods() => activeMods;

        void OnDrawGizmosSelected()
        {
            if (!hitOrigin) return;
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(hitOrigin.position, hitRadius);
        }
    }

}
