using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Robot identity and compatibility bridge. RobotBrain owns decisions and
    /// capabilities; these hidden fields keep older scene instances working.
    /// </summary>
    [RequireComponent(typeof(RobotBrain))]
    public sealed class RobotEnemy : EnemyCharacter
    {
        [SerializeField, HideInInspector] private PatrolBehavior patrol = new();
        [SerializeField, HideInInspector] private ChargeBehavior charge = new();
        [SerializeField, HideInInspector] private DashBehavior dash = new();
        [SerializeField, HideInInspector] private GrabBehavior grab = new();
        [SerializeField, HideInInspector] private RecoveryBehavior recovery = new();

        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.Robot;

            RobotBrain brain = GetComponent<RobotBrain>();
            if (brain == null)
                brain = gameObject.AddComponent<RobotBrain>();

            brain.ApplyLegacyConfiguration(patrol, charge, dash, grab, recovery);
        }
    }
}
