using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Grunt identity. Reusable melee/chase configuration and decisions live in
    /// the required MeleeChaserBrain component.
    /// </summary>
    [RequireComponent(typeof(MeleeChaserBrain))]
    public sealed class GruntEnemy : EnemyCharacter
    {
        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.Grunt;

            if (Movement != null)
                Movement.BlockTargetMovementAgainstEnemies = true;

            EnableEnemyBodyCollisions();
        }

        private void EnableEnemyBodyCollisions()
        {
            int enemiesLayer = LayerMask.NameToLayer("Enemies");
            Collider bodyCollider = GetComponent<Collider>();
            if (enemiesLayer < 0 || bodyCollider == null || bodyCollider.isTrigger)
                return;

            int excludeLayers = bodyCollider.excludeLayers;
            bodyCollider.excludeLayers = excludeLayers & ~(1 << enemiesLayer);
        }
    }
}
