using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Grab state - enemy holds a grabbed target for a duration, then throws.
    /// 
    /// REQUIRES: Enemy must implement IGrabber
    /// 
    /// Pure ACTION state: waits for grab duration.
    /// Calls IGrabber.OnGrabComplete() when done - enemy decides what to do next.
    /// </summary>
    public class GrabState : EnemyStateBase
    {
        private IGrabber grabber;
        private float timer;
        private bool hasThrown;
        private GameObject activeVFX;

        public GrabState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            grabber = GetCapability<IGrabber>();
            if (grabber == null)
            {
                Debug.LogError($"{enemy.gameObject.name}: GrabState requires IGrabber interface!");
                return;
            }

            timer = 0f;
            hasThrown = false;

            enemy.Movement?.Stop();

            activeVFX = VFXPool.Get(grabber.GrabVFXPrefab, enemy.transform);
        }

        public override void Update()
        {
            if (grabber == null || hasThrown) return;

            timer += Time.deltaTime;

            if (timer >= grabber.GrabDuration)
            {
                hasThrown = true;
                grabber.OnGrabComplete();
            }
        }

        public override void Exit()
        {
            VFXPool.Release(ref activeVFX);
        }
    }
}
