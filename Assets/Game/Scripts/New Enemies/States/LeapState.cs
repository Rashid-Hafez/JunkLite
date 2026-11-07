using UnityEngine;

namespace junklite
{
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
            enemy.AnimationPresenter?.PlayLeapAir();
        }

        private void UpdateAir()
        {
            TrackDestination();

            float progress = Mathf.Clamp01(1f - timer / airDuration);

            Vector3 position = Vector3.Lerp(startPosition, destination, progress);
            position.y += 4f * leaper.LeapHeight * progress * (1f - progress);
            Transform.position = position;

            if (progress >= 1f)
                BeginLand();
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
