using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Presentation hooks for the Brute grab phases. Semantic only; the concrete
    /// presenter owns the Spine clip names.
    /// </summary>
    public interface IBruteGrabPresenter
    {
        void PlayGrabWarning(float duration);
        void PlayGrabReach();
        void PlayGrabHold();
        void PlayThrow(float gameplayDuration);
    }

    /// <summary>
    /// Brute close-range grab: warning, reach (unparryable hitbox), hold, throw.
    /// The boss owns the timing: the player stays held until ReleaseGrab, and Exit
    /// always cancels a grab that is still held (stun, death, disable).
    /// </summary>
    public sealed class BruteGrabState : EnemyStateBase
    {
        private enum Phase { Warning, Reach, Hold, Throw, Missed, Done }

        private BruteGrabBehavior grab;
        private IBruteGrabPresenter presenter;
        private EnemyMovement movement;
        private IGrabbable held;
        private Phase phase;
        private float timer;
        private bool released;
        private bool caught;

        public BruteGrabState(EnemyCharacter enemy) : base(enemy) { }

        public override bool CanBeInterrupted => false;

        public override void Enter()
        {
            grab = GetCapability<BruteGrabBehavior>();
            if (grab == null)
            {
                Debug.LogError($"{enemy.gameObject.name}: BruteGrabState requires BruteGrabBehavior.");
                return;
            }

            presenter = enemy.AnimationPresenter as IBruteGrabPresenter;
            movement = enemy.Movement;
            held = null;
            released = false;
            caught = false;

            movement?.Stop();
            if (HasTarget)
                movement?.FaceTarget(Target.position);

            grab.Caught += HandleCaught;
            enemy.ShowAttackWarningImmediate();
            presenter?.PlayGrabWarning(grab.WarningTime);
            SetPhase(Phase.Warning);
        }

        public override void Update()
        {
            if (grab == null || phase == Phase.Done)
                return;

            timer += Time.deltaTime;

            switch (phase)
            {
                case Phase.Warning:
                    if (HasTarget)
                        movement?.FaceTarget(Target.position);
                    if (timer >= grab.WarningTime)
                        BeginReach();
                    break;

                case Phase.Reach:
                    if (timer >= grab.ReachActiveTime)
                    {
                        grab.GrabHitbox?.Deactivate();
                        SetPhase(Phase.Missed);
                    }
                    break;

                case Phase.Hold:
                    if (timer >= grab.HoldTime)
                    {
                        presenter?.PlayThrow(grab.ThrowDuration);
                        SetPhase(Phase.Throw);
                    }
                    break;

                case Phase.Throw:
                    if (!released && timer >= grab.ThrowDuration * grab.ThrowReleaseNormalized)
                        Release();
                    if (timer >= grab.ThrowDuration)
                    {
                        if (!released)
                            Release();
                        Finish();
                    }
                    break;

                case Phase.Missed:
                    if (timer >= grab.MissRecoveryTime)
                        Finish();
                    break;
            }
        }

        private void BeginReach()
        {
            enemy.HideAttackWarning();
            presenter?.PlayGrabReach();
            grab.GrabHitbox?.Activate();
            SetPhase(Phase.Reach);
        }

        private void HandleCaught(IGrabbable target)
        {
            if (phase != Phase.Reach)
                return;

            grab.GrabHitbox?.Deactivate();

            int facing = movement != null ? movement.FacingDirection : 1;
            var info = new GrabInfo(
                enemy.gameObject,
                grab.HoldTimeout,
                grab.GrabOffset,
                grab.ThrowForce,
                grab.ThrowDamage,
                facing)
            {
                Anchor = grab.GrabAnchor,
                HoldUntilReleased = true,
            };

            target.GetGrabbed(info);
            held = target;
            caught = true;
            enemy.GetComponentInChildren<EnemyAudioHandler>()?.PlayGrab();
            presenter?.PlayGrabHold();
            SetPhase(Phase.Hold);
        }

        private void Release()
        {
            released = true;
            if (held == null)
                return;

            int facing = movement != null ? movement.FacingDirection : 1;
            held.ReleaseGrab(enemy.gameObject, facing, grab.ThrowForce, grab.ThrowDamage);
            held = null;
        }

        private void Finish()
        {
            if (phase == Phase.Done)
                return;

            phase = Phase.Done;
            grab.OnGrabComplete(caught);
        }

        private void SetPhase(Phase next)
        {
            phase = next;
            timer = 0f;
        }

        public override void Exit()
        {
            if (grab == null)
                return;

            grab.Caught -= HandleCaught;
            grab.GrabHitbox?.Deactivate();
            enemy.HideAttackWarning();

            // Interrupted mid-hold (stun, death, disable): free the player.
            held?.CancelGrab(enemy.gameObject);
            held = null;
            phase = Phase.Done;
        }
    }
}
