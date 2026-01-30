using UnityEngine;


namespace junklite
{
    [CreateAssetMenu(menuName = "Junklite/Combat/Weapon Combo Data")]
    public class WeaponComboData : ScriptableObject
    {
        [System.Serializable]
        public struct ComboStep
        {
            public float damageMultiplier;
            public GameObject slashPrefab;
            public float hitRadius;
            [Tooltip("Forward impulse applied during this attack (in facing direction)")]
            public float forwardImpulse;
        }


        [Header("Side Attack Combo")]
        public ComboStep[] sideComboSteps; // e.g., 3 steps

        [Header("Directional Attacks (always step 0)")]
        public ComboStep upAttack;
        public ComboStep downAttack;
    }

}
