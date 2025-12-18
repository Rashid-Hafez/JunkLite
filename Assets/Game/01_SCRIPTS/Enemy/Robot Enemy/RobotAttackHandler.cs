using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Attack handler for Robot Enemy.
    /// Simple melee attack for now.
    /// Future: Spin attack, dash attack, grenade
    /// </summary>
    public class RobotAttackHandler : EnemyAttackHandler
    {
        [Header("Robot Attack")]
        [SerializeField] private Transform attackPoint;
        [SerializeField] private float attackRadius = 1f;

        // Cache for overlap detection
        private Collider[] hitBuffer = new Collider[8];

        protected override void DoAttack()
        {
            if (attackPoint == null) return;

            // Find targets in attack range
            int hitCount = Physics.OverlapSphereNonAlloc(
                attackPoint.position,
                attackRadius,
                hitBuffer,
                targetLayer
            );

            // Damage each target
            for (int i = 0; i < hitCount; i++)
            {
                var target = hitBuffer[i].GetComponent<IDamageable>();
                if (target != null)
                {
                    DamageInfo info = new DamageInfo(damage, enemy.gameObject);
                    target.TakeDamage(info);
                }
            }
        }

        // ================= DEBUG =================
        private void OnDrawGizmosSelected()
        {
            if (attackPoint == null) return;

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(attackPoint.position, attackRadius);
        }
    }
}