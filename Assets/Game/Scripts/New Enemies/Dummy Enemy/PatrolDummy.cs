using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Patrol Dummy identity and compatibility bridge. PassiveEnemyBrain owns
    /// its state machine and deliberately ignores player perception.
    /// </summary>
    [RequireComponent(typeof(PassiveEnemyBrain))]
    public sealed class PatrolEnemy : EnemyCharacter
    {
        [SerializeField, HideInInspector] private PatrolBehavior patrol = new();

        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.Dummy;

            PassiveEnemyBrain brain = GetComponent<PassiveEnemyBrain>();
            if (brain == null)
                brain = gameObject.AddComponent<PassiveEnemyBrain>();

            brain.ApplyLegacyConfiguration(true, patrol);
        }
    }
}
