using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    [CreateAssetMenu(fileName = "WeaponData", menuName = "Junklite/WeaponData")]
    public class WeaponData : ScriptableObject
    {
        public enum Rarity
        {
            Common,
            Uncommon,
            Rare,
            Epic,
            Legendary
        }
        [Header("Core")]
        public Rarity rarity;
        public WeaponComboData comboData;
        public string weaponId;
        public string displayName;
        public WeaponType type;
        public Sprite icon;

        [Header("Base Stats")]
        public float baseAttackSpeed = 1f;
        public int maxWeaponDurability = 100;
        public float baseDamage = 10f;
        public Vector2 knockbackForce = new Vector2(8f, 4f);

        [Header("Progression")]
        public int maxActiveModSlots = 2;
        public int maxReserveSlots = 4;

    }
}