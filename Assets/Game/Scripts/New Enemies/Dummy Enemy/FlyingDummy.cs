using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Flying Dummy identity and compatibility bridge. The brain owns its
    /// patrol/follow policy and FlyingHoverController owns flight presentation.
    /// </summary>
    [RequireComponent(typeof(FlyingFollowerBrain))]
    [RequireComponent(typeof(FlyingHoverController))]
    public sealed class FlyingDummy : EnemyCharacter
    {
        [SerializeField, HideInInspector] private PatrolBehavior patrol = new();
        [SerializeField, HideInInspector] private ChaseBehavior chase = new();
        [SerializeField, HideInInspector] private float hoverBobAmount = 0.2f;
        [SerializeField, HideInInspector] private float hoverBobSpeed = 2f;

        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.FlyingDummy;

            FlyingFollowerBrain brain = GetComponent<FlyingFollowerBrain>();
            if (brain == null)
                brain = gameObject.AddComponent<FlyingFollowerBrain>();

            FlyingHoverController hover = GetComponent<FlyingHoverController>();
            if (hover == null)
                hover = gameObject.AddComponent<FlyingHoverController>();

            brain.ApplyLegacyConfiguration(patrol, chase);
            hover.ApplyLegacyConfiguration(hoverBobAmount, hoverBobSpeed, chase.ChaseSpeed);
        }
    }
}
