using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Grunt identity plus migration bridge. Reusable melee/chase decisions live in
    /// MeleeChaserBrain; these hidden fields only preserve older prefab data.
    /// </summary>
    [RequireComponent(typeof(MeleeChaserBrain))]
    public sealed class GruntEnemy : EnemyCharacter
    {
        [SerializeField, HideInInspector] private EnemySpineAnimationController spineController;
        [SerializeField, HideInInspector] private ChaseBehavior chase = new();
        [SerializeField, HideInInspector] private float pursuitRadius = 12f;
        [SerializeField, HideInInspector] private MeleeAttackBehavior melee = new();
        [SerializeField, HideInInspector] private StunBehavior stun = new();

        protected override void Awake()
        {
            base.Awake();
            enemyType = EnemyType.Grunt;

            MeleeChaserBrain meleeBrain = GetComponent<MeleeChaserBrain>();
            if (meleeBrain == null)
                meleeBrain = gameObject.AddComponent<MeleeChaserBrain>();

            meleeBrain.ApplyLegacyConfiguration(
                spineController,
                false,
                null,
                chase,
                pursuitRadius,
                melee,
                stun);
        }
    }
}
