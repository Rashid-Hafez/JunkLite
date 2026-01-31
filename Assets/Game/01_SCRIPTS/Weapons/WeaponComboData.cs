using UnityEngine;


namespace junklite
{
    [CreateAssetMenu(menuName = "Junklite/Combat/Weapon Combo Data")]
    public class WeaponComboData : ScriptableObject
    {
        [System.Serializable]
        public struct ComboStep
        {
            [Header("Animation")]
            public string animationName;

            public float damageMultiplier;
            public GameObject slashPrefab;
            public float hitRadius;
        }


        [Header("Side Attack Combo")]
        public ComboStep[] sideComboSteps; // e.g., 3 steps

        [Header("Directional Attacks (always step 0)")]
        public ComboStep upAttack;
        public ComboStep downAttack;
    }

}
