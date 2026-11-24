using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Holds an engagement slot while staying visibly alive. The slot comes from
    /// the brain's IChaseDestinationProvider; this state follows it as the player
    /// moves, shuffles a little around it, pauses, occasionally feints a step
    /// toward the player, and flinches back when an ally ahead is hit (then
    /// creeps back in). Every move passes through ILaneSpacing when present, so
    /// the enemy never walks into the body ahead or behind it.
    /// The brain keeps evaluating and decides when to leave.
    /// Replaces the static WaitForOpeningState for group-aware enemies.
    /// </summary>
    public sealed class HoldPositionState : EnemyStateBase
    {
        private enum Phase
        {
            Pause,
            Shuffle,
            FeintIn,
            FeintOut,
            Recoil,
            RecoilHold,
            Return
        }

        private const float ArriveTolerance = 0.08f;
        private const float ShuffleTimeout = 0.8f;
        private const float ReapproachSlack = 0.1f;
        private const float MinMoveSpeed = 0.05f;
        private const float SeparationThreshold = 0.05f;

        private IHoldPositioner hold;
        private IChaser chaser;
        private IChaseDestinationProvider destinationProvider;
        private ILaneSpacing spacing;
        private EnemyMovement movement;

        private Phase phase;
        private float timer;
        private Vector3 movePoint;
        private Vector3 feintOrigin;
        private bool reapproaching;

        public HoldPositionState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            hold = GetCapability<IHoldPositioner>();
            chaser = GetCapability<IChaser>();
            destinationProvider = GetCapability<IChaseDestinationProvider>();
            spacing = GetCapability<ILaneSpacing>();
            movement = enemy.Movement;
            reapproaching = false;

            movement?.Stop();
            FaceTarget();
            BeginPause();
        }

        public override void Update()
        {
            if (!HasTarget || movement == null)
            {
                SetLocomotion(0f);
                return;
            }

            if (hold == null)
            {
                // No pacing capability: behave like the static wait.
                movement.Stop();
                FaceTarget();
                return;
            }

            if (!TryGetSlot(out Vector3 slot, out int towardSign))
            {
                movement.Stop();
                FaceTarget();
                SetLocomotion(0f);
                return;
            }

            // A flinch request interrupts anything except a flinch already in progress.
            if (phase != Phase.Recoil && phase != Phase.RecoilHold
                && hold.TryConsumeRecoil(out float recoilDistance))
            {
                BeginRecoil(towardSign, recoilDistance);
            }

            timer -= Time.deltaTime;

            switch (phase)
            {
                case Phase.Recoil:
                    // Keep trying for the whole step: the enemy behind flinches a beat
                    // later and opens the room this step may be waiting for.
                    Move(movePoint, hold.RecoilSpeed);
                    if (Reached(movePoint) || timer <= 0f)
                        BeginRecoilHold();
                    return;

                case Phase.RecoilHold:
                    StandStill();
                    if (Time.time >= hold.RecoilHoldUntil)
                        phase = Phase.Return;
                    return;

                case Phase.Return:
                    Move(slot, hold.CreepSpeed);
                    if (Reached(slot) || IsHeldInPlace(slot, hold.CreepSpeed))
                        BeginPause();
                    return;
            }

            float drift = movement.GetAbsAxisDistance(Transform.position, slot);
            float maxDrift = hold.PaceEnabled
                ? hold.HoldTolerance + hold.PaceAmplitude + ReapproachSlack
                : hold.HoldTolerance;
            if (drift > maxDrift || (reapproaching && drift > hold.HoldTolerance))
            {
                Reapproach(slot);
                return;
            }

            reapproaching = false;

            switch (phase)
            {
                case Phase.Pause:
                    if (TrySeparate())
                        break;

                    StandStill();
                    if (timer <= 0f && hold.PaceEnabled)
                        ChooseNextMove(slot, towardSign);
                    break;

                case Phase.Shuffle:
                    Move(movePoint, hold.PaceSpeed);
                    if (Reached(movePoint) || timer <= 0f)
                        BeginPause();
                    break;

                case Phase.FeintIn:
                    Move(movePoint, hold.FeintDistance / Mathf.Max(0.01f, hold.FeintDuration));
                    if (Reached(movePoint) || timer <= 0f)
                    {
                        movePoint = feintOrigin;
                        timer = hold.FeintDuration * 1.5f;
                        phase = Phase.FeintOut;
                    }
                    break;

                case Phase.FeintOut:
                    Move(movePoint, hold.PaceSpeed);
                    if (Reached(movePoint) || timer <= 0f)
                        BeginPause();
                    break;
            }
        }

        public override void Exit()
        {
            movement?.Stop();
            SetLocomotion(0f);
        }

        // ------------------------------------------------------------

        private bool TryGetSlot(out Vector3 slot, out int towardSign)
        {
            Vector3 targetPosition = Target.position;
            float signed = movement.GetSignedAxisDistance(Transform.position, targetPosition);
            towardSign = signed >= 0f ? 1 : -1;

            float stopDistance = chaser != null && chaser.ChaseStopDistance > 0f
                ? chaser.ChaseStopDistance
                : enemy.AttackRange;

            Vector3 destination = targetPosition;
            float destinationStop = stopDistance;
            bool assigned = destinationProvider != null
                && destinationProvider.TryGetChaseDestination(
                    targetPosition, stopDistance, out destination, out destinationStop);

            Vector3 axis = movement.MovementAxis;
            bool isFrontSlot = !assigned
                || movement.GetAbsAxisDistance(destination, targetPosition) <= 0.001f;

            if (isFrontSlot)
            {
                // Front: the slot is the attack distance on my side of the player.
                float holdDistance = stopDistance + hold.HoldOffset;
                slot = Transform.position + axis * (signed - towardSign * holdDistance);
            }
            else
            {
                // Queued: the director already placed me; just apply the mood offset.
                float along = Vector3.Dot(destination - Transform.position, axis) - towardSign * hold.HoldOffset;
                slot = Transform.position + axis * along;
            }

            // The slot assumes the enemy ahead stands exactly in its own slot. Pull it
            // back to where that body really is, so I never aim into it.
            if (spacing != null)
            {
                float ignoredSpeed = 0f;
                spacing.ApplySpacing(ref slot, ref ignoredSpeed);
            }

            return true;
        }

        private void Reapproach(Vector3 slot)
        {
            if (!reapproaching)
            {
                reapproaching = true;
                phase = Phase.Pause;
                timer = RandomPause();
            }

            Move(slot, hold.HoldSpeed);
        }

        private void ChooseNextMove(Vector3 slot, int towardSign)
        {
            Vector3 axis = movement.MovementAxis;

            if (hold.FeintAllowed && hold.FeintChance > 0f && Random.value <= hold.FeintChance)
            {
                Vector3 point = Transform.position + axis * (towardSign * hold.FeintDistance);
                if (IsPathClear(point))
                {
                    feintOrigin = Transform.position;
                    movePoint = point;
                    timer = hold.FeintDuration;
                    phase = Phase.FeintIn;
                    return;
                }
            }

            float t = Mathf.Clamp01(Random.value + hold.PaceBias * 0.35f);
            float offset = Mathf.Lerp(-hold.PaceAmplitude, hold.PaceTowardLimit, t);
            Vector3 shuffleTo = slot + axis * (towardSign * offset);

            if (movement.GetAbsAxisDistance(Transform.position, shuffleTo) < ArriveTolerance * 2f
                || !IsPathClear(shuffleTo))
            {
                BeginPause();
                return;
            }

            movePoint = shuffleTo;
            timer = ShuffleTimeout;
            phase = Phase.Shuffle;
        }

        private void BeginRecoil(int towardSign, float distance)
        {
            reapproaching = false;
            Vector3 point = Transform.position - movement.MovementAxis * (towardSign * distance);
            if (!IsPathClear(point))
            {
                BeginRecoilHold();
                return;
            }

            movePoint = point;
            timer = distance / Mathf.Max(0.1f, hold.RecoilSpeed) + 0.3f;
            phase = Phase.Recoil;
        }

        private void BeginRecoilHold()
        {
            phase = Phase.RecoilHold;
            StandStill();
        }

        private void BeginPause()
        {
            phase = Phase.Pause;
            timer = RandomPause();
            StandStill();
        }

        /// <summary>
        /// Safety net: if something (knockback, a flinching leader) ended up inside my
        /// comfort gap, step back out of it, limited by whoever stands behind me.
        /// </summary>
        private bool TrySeparate()
        {
            if (spacing == null)
                return false;

            Vector3 here = Transform.position;
            Vector3 spaced = here;
            float speed = hold.PaceSpeed;
            spacing.ApplySpacing(ref spaced, ref speed);

            if (speed < MinMoveSpeed || movement.GetAbsAxisDistance(here, spaced) <= SeparationThreshold)
                return false;

            movement.MoveTo(spaced, speed);
            FaceTarget();
            SetLocomotion(Normalize(speed));
            return true;
        }

        private void Move(Vector3 point, float speed)
        {
            spacing?.ApplySpacing(ref point, ref speed);

            if (speed < MinMoveSpeed || Reached(point))
            {
                StandStill();
                return;
            }

            movement.MoveTo(point, speed);
            FaceTarget();
            SetLocomotion(Normalize(speed));
        }

        /// <summary>True when spacing leaves no room to move toward the point at all.</summary>
        private bool IsHeldInPlace(Vector3 point, float speed)
        {
            if (spacing == null)
                return false;

            spacing.ApplySpacing(ref point, ref speed);
            return speed < MinMoveSpeed || Reached(point);
        }

        private void StandStill()
        {
            movement.Stop();
            FaceTarget();
            SetLocomotion(0f);
        }

        private float RandomPause()
        {
            Vector2 range = hold != null ? hold.PacePauseRange : new Vector2(0.4f, 1.2f);
            float scale = hold != null && hold.RhythmScale > 0f ? hold.RhythmScale : 1f;
            return Random.Range(range.x, range.y) * scale;
        }

        private bool Reached(Vector3 point)
        {
            return movement.GetAbsAxisDistance(Transform.position, point) <= ArriveTolerance;
        }

        private bool IsPathClear(Vector3 point)
        {
            LayerMask mask = hold.ObstacleLayers;
            if (mask.value == 0)
                return true;

            Vector3 origin = Transform.position + Vector3.up * 0.5f;
            Vector3 delta = point - Transform.position;
            float distance = delta.magnitude;
            if (distance <= 0.001f)
                return true;

            return !Physics.Raycast(origin, delta / distance, distance, mask, QueryTriggerInteraction.Ignore);
        }

        private float Normalize(float speed)
        {
            float reference = chaser != null && chaser.ChaseSpeed > 0f
                ? chaser.ChaseSpeed
                : Mathf.Max(0.01f, movement.MoveSpeed);
            return Mathf.Clamp01(speed / reference);
        }

        private void SetLocomotion(float speed01)
        {
            enemy.AnimationPresenter?.SetLocomotionSpeed01(speed01);
        }

        private void FaceTarget()
        {
            if (HasTarget)
                movement?.FaceTarget(Target.position);
        }
    }
}
