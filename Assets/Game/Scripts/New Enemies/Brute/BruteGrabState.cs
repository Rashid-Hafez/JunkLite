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
    /// Brute close-range grab: warning, reach (unparryable hitbox), hold, throw. The hold
    /// can be broken by mashing jump (EscapeJumpPresses). A grab on touch (caught by the
    /// brain) skips the warning and the hitbox: the grab clip plays as he scoops her up,
    /// he throws after TouchThrowDelay (from the touch), and it can't be broken.
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
        private bool lunging;
        private bool touchGrab;

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
            lunging = false;
            touchGrab = false;
            grab.LastGrabEscaped = false;

            movement?.Stop();
            if (HasTarget)
                movement?.FaceTarget(Target.position);

            grab.Caught += HandleCaught;

            // Grab on touch: the brain already caught her, so go straight to the hold.
            IGrabbable touched = grab.TakeTouchCatch();
            if (touched != null)
            {
                touchGrab = true;
                SetPhase(Phase.Reach);
                HandleCaught(touched);
                return;
            }

            enemy.ShowAttackWarningImmediate(grab.WarningTime);
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
                    if (lunging && (!HasTarget || IsWithinReachStop()))
                        EndReachLunge();
                    if (timer >= grab.ReachActiveTime)
                    {
                        EndReachLunge();
                        grab.GrabHitbox?.Deactivate();
                        SetPhase(Phase.Missed);
                    }
                    break;

                case Phase.Hold:
                    if (timer >= (touchGrab ? grab.TouchThrowDelay : grab.HoldTime))
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
            BeginReachLunge();
        }

        // The reach steps toward a player who backed off during the warning; the
        // hitbox rides along, so the grab lands instead of swiping air.
        private void BeginReachLunge()
        {
            if (movement == null || !HasTarget || grab.ReachLungeSpeed <= 0f || IsWithinReachStop())
                return;

            float signed = movement.GetSignedAxisDistance(Transform.position, Target.position);
            movement.MoveInDirection(movement.MovementAxis * Mathf.Sign(signed), grab.ReachLungeSpeed);
            lunging = true;
        }

        private bool IsWithinReachStop() =>
            HasTarget && movement != null
            && movement.GetAbsAxisDistance(Transform.position, Target.position) <= grab.ReachLungeStopDistance;

        private void EndReachLunge()
        {
            if (!lunging)
                return;

            lunging = false;
            movement?.Stop();
        }

        private void HandleCaught(IGrabbable target)
        {
            if (phase != Phase.Reach)
                return;

            EndReachLunge();
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
                EscapePresses = touchGrab ? 0 : grab.EscapeJumpPresses,
                OnEscaped = HandleEscaped,
            };

            target.GetGrabbed(info);
            held = target;
            caught = true;
            enemy.GetComponentInChildren<EnemyAudioHandler>()?.PlayGrab();
            // A hitbox catch happens mid-reach, so the clip is already playing: freeze its
            // end pose. A touch catch comes from idle: play the reach so the grab reads,
            // then the clip holds its last frame until the throw (TouchThrowDelay).
            if (touchGrab)
                presenter?.PlayGrabReach();
            else
                presenter?.PlayGrabHold();
            SetPhase(Phase.Hold);
        }

        /// <summary>She mashed out of the hold: nothing to throw, and he staggers (brain reads LastGrabEscaped).</summary>
        private void HandleEscaped()
        {
            if (phase != Phase.Hold && phase != Phase.Throw)
                return;

            held = null;
            caught = false;
            grab.LastGrabEscaped = true;
            Finish();
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
            EndReachLunge();
            grab.GrabHitbox?.Deactivate();
            grab.StartTouchCooldown();
            enemy.HideAttackWarning();

            // Interrupted mid-hold (stun, death, disable): free the player.
            held?.CancelGrab(enemy.gameObject);
            held = null;
            phase = Phase.Done;
        }
    }
}
