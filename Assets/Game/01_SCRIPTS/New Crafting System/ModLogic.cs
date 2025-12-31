using junklite;
using UnityEngine;

namespace junklite
{
    public abstract class ModLogic : ScriptableObject
    {
        // Called when weapon hits an enemy
        public virtual void OnHit(WeaponInstance weapon, EnemyCharacter enemy, ref DamageInfo dmg) { }

        // Called when player starts an attack
        public virtual void OnAttackStart(WeaponInstance weapon) { }

        // Called when mod is equipped
        public virtual void OnEquip(WeaponInstance weapon) { }

        // Called when mod is removed or breaks
        public virtual void OnUnequip(WeaponInstance weapon) { }
    }
}
