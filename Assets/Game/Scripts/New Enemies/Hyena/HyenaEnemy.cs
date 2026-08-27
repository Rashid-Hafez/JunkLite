using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Hyena identity plus migration bridge. Shared melee/chase logic lives in
    /// MeleeChaserBrain and Hyena-specific decisions live in HyenaBrain.
    /// </summary>
    [RequireComponent(typeof(HyenaBrain))]
    public sealed class HyenaEnemy : EnemyCharacter
    {
        [SerializeField, HideInInspector] private EnemySpineAnimationController spineController;
        [SerializeField, HideInInspector] private PatrolBehavior patrol = new();
        [SerializeField, HideInInspector] private ChaseBehavior chase = new();
        [SerializeField, HideInInspector] private float pursuitRadius = 15f;
        [SerializeField, HideInInspector] private MeleeAttackBehavior melee = new();
        [SerializeField, HideInInspector] private DodgeBehavior dodge = new();
        [SerializeField, HideInInspector, Range(0f, 1f)] private float dodgeChance = 0.3f;
        [SerializeField, HideInInspector] private float dodgeCheckRange = 4f;
        [SerializeField, HideInInspector] private float dodgeCooldown = 1f;
        [SerializeField, HideInInspector] private ChargeBehavior charge = new();
        [SerializeField, HideInInspector] private DashBehavior dash = new();
        [SerializeField, HideInInspector, Range(0f, 1f)] private float dashChance = 0.4f;
        [SerializeField, HideInInspector] private float whiffStunDuration = 1.5f;
        [SerializeField, HideInInspector] private float maxCounterDashRange = 8f;
        [SerializeField, HideInInspector] private StunBehavior stun = new();

        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.Hyena;

            HyenaBrain hyenaBrain = GetComponent<HyenaBrain>();
            if (hyenaBrain == null)
                hyenaBrain = gameObject.AddComponent<HyenaBrain>();

            hyenaBrain.ApplyLegacyHyenaConfiguration(
                spineController,
                patrol,
                chase,
                pursuitRadius,
                melee,
                dodge,
                dodgeChance,
                dodgeCheckRange,
                dodgeCooldown,
                charge,
                dash,
                dashChance,
                whiffStunDuration,
                maxCounterDashRange,
                stun);
        }
    }
}
