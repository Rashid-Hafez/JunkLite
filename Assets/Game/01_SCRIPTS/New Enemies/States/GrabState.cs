using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Grab state - enemy holds a grabbed target for a duration, then throws.
    /// Calls enemy.OnGrabComplete() when done - enemy DECIDES what to do next.
    /// </summary>
    public class GrabState : EnemyStateBase
    {
        private float grabDuration;
        private float timer;
        private bool hasThrown;

        public GrabState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            timer = 0f;
            hasThrown = false;

            // Get grab duration from enemy config
            if (enemy is RobotEnemy robot)
            {
                grabDuration = robot.GrabDuration;
            }
            else
            {
                grabDuration = 0.5f; // Default fallback
            }

            // Stop movement during grab
            enemy.Movement?.Stop();

            Debug.Log($"{enemy.gameObject.name}: Grabbing! (duration: {grabDuration}s)");
        }

        public override void Update()
        {
            if (hasThrown) return;

            timer += Time.deltaTime;

            if (timer >= grabDuration)
            {
                hasThrown = true;
                enemy.OnGrabComplete();
            }
        }

        public override void Exit()
        {
            // Nothing to clean up
        }
    }
}