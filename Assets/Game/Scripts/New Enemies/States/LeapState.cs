using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Optional on an <see cref="ILeaper"/>: how the flight looks. True holds the landing
    /// clip's pose in the air instead of the air clip, and the landing plays that clip.
    /// </summary>
    public interface ILeapAirPose
    {
        bool LeapAirHoldsLandPose { get; }
    }

    /// <summary>
    /// Kinematic arc to a destination chosen by the brain (vault over the line to
    /// the player's open side, or hop back behind the line). Three phases:
    /// grounded telegraph (interruptible, a hit cancels), airborne (kinematic,
    /// not interruptible), landing. Reports completion or cancellation through ILeaper.
    /// </summary>
    public sealed class LeapState : EnemyStateBase
    {
        private enum Phase
        {
            Telegraph,
            Air,
            Land
        }

        private const float MinAirDuration = 0.15f;
        // The arc is kinematic and ignores colliders, so every step is swept against level geometry: the
        // landing point can slide while she is airborne (tracked leaps) and must never carry her through a wall.
        private const float SweepHeight = 0.9f;
        private const float SweepStopBuffer = 0.35f;
        private static int sweepMask = -1;
        private Vector3 lastAirPosition;
        private ILeaper leaper;
        private EnemyMovement movement;
        private Rigidbody rb;

        private Phase phase;
        private float timer;
        private float airDuration;
        private Vector3 startPosition;
        private Vector3 destination;
        private bool kinematicApplied;
        private bool wasKinematic;
        private bool finished;

        public LeapState(EnemyCharacter enemy) : base(enemy) { }

        public override bool CanTakeDamage =>
            leaper == null || !leaper.LeapHasIFrames || phase != Phase.Air;

        public override bool CanBeInterrupted => phase == Phase.Telegraph;

        public override void Enter()
        {
            leaper = GetCapability<ILeaper>();
            movement = enemy.Movement;
            rb = enemy.GetComponent<Rigidbody>();
            finished = false;
            kinematicApplied = false;

            if (leaper == null)
            {
                Debug.LogError($"{enemy.gameObject.name}: LeapState requires ILeaper!");
                finished = true;
                return;
            }

            leaper.RefreshLeapDestination();
            destination = leaper.LeapDestination;
            movement?.Stop();
            if (movement != null)
                movement.FaceTarget(destination);

            phase = Phase.Telegraph;
            timer = Mathf.Max(0f, leaper.LeapTelegraph);
            enemy.AnimationPresenter?.PlayLeapTelegraph(timer);

            if (timer <= 0f)
                BeginAir();
        }

        public override void Update()
        {
            if (finished || leaper == null)
                return;

            timer -= Time.deltaTime;

            switch (phase)
            {
                case Phase.Telegraph:
                    // Still on the ground: call it off if the opening closed (the
                    // player ran, rolled, jumped), otherwise keep re-aiming.
                    if (leaper.ShouldAbortLeap())
                    {
                        Abort();
                        return;
                    }

                    leaper.RefreshLeapDestination();
                    destination = leaper.LeapDestination;
                    movement?.FaceTarget(destination);

                    if (timer <= 0f)
                        BeginAir();
                    break;

                case Phase.Air:
                    UpdateAir();
                    break;

                case Phase.Land:
                    if (timer <= 0f)
                        Complete();
                    break;
            }
        }

        public override void Exit()
        {
            RestoreRigidbody();
            movement?.Stop();

            if (!finished)
            {
                finished = true;
                leaper?.OnLeapCancelled();
            }
        }

        // ------------------------------------------------------------

        private void BeginAir()
        {
            startPosition = Transform.position;
            float distance = Vector3.Distance(startPosition, destination);
            float speed = Mathf.Max(0.01f, leaper.LeapSpeed);
            airDuration = Mathf.Max(MinAirDuration, distance / speed);

            if (rb != null)
            {
                wasKinematic = rb.isKinematic;
                rb.linearVelocity = Vector3.zero;
                rb.isKinematic = true;
                kinematicApplied = true;
            }

            phase = Phase.Air;
            timer = airDuration;
            lastAirPosition = startPosition;
            if (sweepMask < 0) sweepMask = LayerMask.GetMask("Default", "Ground", "Wall", EnemyBarrier.LayerName);

            if (leaper is ILeapAirPose pose && pose.LeapAirHoldsLandPose)
                enemy.AnimationPresenter?.PlayLeapAirHoldingLandPose();
            else
                enemy.AnimationPresenter?.PlayLeapAir();
        }

        private void UpdateAir()
        {
            TrackDestination();

            float progress = Mathf.Clamp01(1f - timer / airDuration);

            Vector3 position = Vector3.Lerp(startPosition, destination, progress);
            position.y += 4f * leaper.LeapHeight * progress * (1f - progress);

            if (BlockedByGeometry(lastAirPosition, position, out Vector3 stopPoint))
            {
                // Land where the wall stops her, on the ground level of the intended landing.
                destination = new Vector3(stopPoint.x, destination.y, stopPoint.z);
                BeginLand();
                return;
            }

            lastAirPosition = position;
            Transform.position = position;

            if (progress >= 1f)
                BeginLand();
        }

        /// <summary>Horizontal sweep of one air step at body height. False when nothing solid is in the way.</summary>
        private bool BlockedByGeometry(Vector3 from, Vector3 to, out Vector3 stopPoint) =>
            SweepBlocked(from, to, Mathf.Min(startPosition.y, destination.y) + SweepHeight, sweepMask, out stopPoint);

        /// <summary>
        /// Raycasts the horizontal move from <paramref name="from"/> to <paramref name="to"/> at <paramref name="rayHeight"/>.
        /// True with the last safe point (<see cref="SweepStopBuffer"/> before the hit) when a collider is in the way.
        /// </summary>
        public static bool SweepBlocked(Vector3 from, Vector3 to, float rayHeight, int mask, out Vector3 stopPoint)
        {
            stopPoint = to;
            Vector3 delta = to - from;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance < 0.001f) return false;

            Vector3 direction = delta / distance;
            Vector3 origin = new Vector3(from.x, rayHeight, from.z);
            // Reach a little past the step so a wall right at the end of it still counts.
            if (!Physics.Raycast(origin, direction, out RaycastHit hit, distance + SweepStopBuffer, mask, QueryTriggerInteraction.Ignore))
                return false;

            stopPoint = new Vector3(origin.x, from.y, origin.z) + direction * Mathf.Max(0f, hit.distance - SweepStopBuffer);
            return true;
        }

        private void BeginLand()
        {
            Transform.position = destination;
            RestoreRigidbody();

            phase = Phase.Land;
            timer = Mathf.Max(0f, leaper.LeapLandDuration);
            enemy.AnimationPresenter?.PlayLeapLand(timer);

            if (HasTarget && movement != null)
                movement.FaceTarget(Target.position);

            if (timer <= 0f)
                Complete();
        }

        /// <summary>
        /// Slides the landing point toward the leaper's re-aimed destination at a
        /// capped speed, so a moving target is followed without the arc snapping.
        /// Height stays at the take-off destination's ground level.
        /// </summary>
        private void TrackDestination()
        {
            float maxCorrection = leaper.LeapMaxCorrectionSpeed;
            if (maxCorrection <= 0f)
                return;

            leaper.RefreshLeapDestination();
            Vector3 desired = leaper.LeapDestination;
            desired.y = destination.y;
            destination = Vector3.MoveTowards(destination, desired, maxCorrection * Time.deltaTime);
        }

        private void Abort()
        {
            if (finished)
                return;

            finished = true;
            RestoreRigidbody();
            leaper.OnLeapAborted();
        }

        private void Complete()
        {
            if (finished)
                return;

            finished = true;
            leaper.OnLeapComplete();
        }

        private void RestoreRigidbody()
        {
            if (!kinematicApplied || rb == null)
                return;

            kinematicApplied = false;
            if (rb.isKinematic != wasKinematic)
            {
                rb.isKinematic = wasKinematic;
                rb.linearVelocity = Vector3.zero;
            }
        }
    }
}
