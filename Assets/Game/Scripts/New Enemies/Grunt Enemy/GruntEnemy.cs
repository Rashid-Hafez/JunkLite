using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Grunt identity. Reusable melee/chase configuration lives in the inherited
    /// MeleeChaserBrain loop; grunt group tactics (hold, vault, pressure) live in
    /// the required GruntBrain component.
    /// </summary>
    [RequireComponent(typeof(GruntBrain))]
    public sealed class GruntEnemy : EnemyCharacter
    {
        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.Grunt;
        }
    }
}
