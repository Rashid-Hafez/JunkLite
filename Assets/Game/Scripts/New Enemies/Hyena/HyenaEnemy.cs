using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Hyena identity. Shared melee/chase configuration and Hyena-specific
    /// decisions live in the required HyenaBrain component.
    /// </summary>
    [RequireComponent(typeof(HyenaBrain))]
    public sealed class HyenaEnemy : EnemyCharacter
    {
        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.Hyena;
        }
    }
}
