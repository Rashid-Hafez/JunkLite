using UnityEngine;

namespace junklite
{
    /// <summary>
    /// ScriptableObject for shared enemy configuration.
    /// Enemy-specific values go in the enemy class itself.
    /// </summary>
    [CreateAssetMenu(fileName = "New Enemy Config", menuName = "JunkLite/Enemy Config")]
    public class EnemyConfig : ScriptableObject
    {
        [Header("Patrol")]
        [Tooltip("Movement speed while patrolling")]
        public float patrolSpeed = 2f;

        [Header("Movement")]
        [Tooltip("Movement speed while chasing target")]
        public float chaseSpeed = 4f;

        [Header("Combat")]
        [Tooltip("Time between attacks in seconds")]
        public float attackCooldown = 1.5f;

        [Tooltip("Damage dealt per attack")]
        public float attackDamage = 10f;

        [Header("Aggro")]
        [Tooltip("Does this enemy lose aggro when target leaves range?")]
        public bool losesAggro = true;

        [Tooltip("Time before losing aggro when target is out of range")]
        public float aggroLossDelay = 3f;
    }
}