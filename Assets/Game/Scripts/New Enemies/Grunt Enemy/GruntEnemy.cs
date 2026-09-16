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
        }
    }
}
