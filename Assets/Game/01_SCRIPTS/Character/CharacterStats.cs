using UnityEngine;


namespace junklite
{
    [CreateAssetMenu(fileName = "New Character Stats", menuName = "Character/Stats")]
    public class CharacterStats : ScriptableObject
    {
        [Header("Health & Combat")]
        public float maxHealth = 100f;
        public float armor = 0f;
        public float damage = 10f;

        [Header("Movement")]
        public float moveSpeed = 5f;
        public float jumpForce = 10f;

        [Header("Info")]
        public string characterName = "Character";
    }
}
