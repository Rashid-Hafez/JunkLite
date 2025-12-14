using UnityEngine;


namespace junklite
{
    [CreateAssetMenu(menuName = "Junklite/Combat/Weapon Combo Data")]
    public class WeaponComboData : ScriptableObject
    {
        [System.Serializable]
        public struct ComboStep
        {
            public float damageMultiplier;  // 1.0 = base damage
            public GameObject slashPrefab;  // visual slash
            public float hitRadius;         // per-hit hitbox size
        }

        [Header("Side Attack Combo")]
        public ComboStep[] sideComboSteps; // e.g., 3 steps

        [Header("Directional Attacks (always step 0)")]
        public ComboStep upAttack;
        public ComboStep downAttack;
    }

}
