using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Kinematic arc over a gap in the floor, to the landing point the brain took from
    /// <see cref="EnemyMovement.ProbeGap"/>. Shared by every ground enemy (registered by
    /// <see cref="EnemyBrain"/>). Airborne the body is kinematic and not interruptible:
    /// damage still applies, a stun waits for the landing. Reports back through
    /// <see cref="IGapJumper.OnGapJumpFinished"/>.
    /// </summary>
    public sealed class GapJumpState : EnemyStateBase
    {
        private enum Phase
        {
            Air,
            Land
        }

        private const float MinAirDuration = 0.2f;
        private const float LandDuration = 0.1f;

        private IGapJumper jumper;
        private EnemyMovement movement;
        private Rigidbody rb;

        private Phase phase;
        private float timer;
        private float airDuration;
        private float arcHeight;
        private Vector3 startPosition;
        private Vector3 destination;
        private bool kinematicApplied;
        private bool wasKinematic;
        private bool finished;

        public GapJumpState(EnemyCharacter enemy) : base(enemy) { }

        public override bool CanBeInterrupted => false;

        public override void Enter()
        {
            jumper = GetCapability<IGapJumper>();
            movement = enemy.Movement;
            rb = enemy.GetComponent<Rigidbody>();
            finished = false;
            kinematicApplied = false;

            if (jumper == null)
            {
                Debug.LogError($"{enemy.gameObject.name}: GapJumpState requires IGapJumper (EnemyBrain).");
                finished = true;
                return;
            }

            destination = jumper.GapLanding;
            startPosition = Transform.position;
            // Land with the feet on the floor: the root sits at the body centre.
            if (movement != null)
                destination.y += Transform.position.y - movement.GetFeetPosition().y;

            movement?.Stop();
            movement?.FaceTarget(destination);

            Vector3 flat = destination - startPosition;
            flat.y = 0f;
            float distance = flat.magnitude;
            airDuration = Mathf.Max(MinAirDuration, distance / Mathf.Max(0.01f, jumper.GapJumpSpeed));
            arcHeight = jumper.GapArcHeight(distance);

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

        public override void Update()
        {
            if (finished)
                return;

            timer -= Time.deltaTime;

            switch (phase)
            {
                case Phase.Air:
                    float progress = Mathf.Clamp01(1f - timer / airDuration);
                    Vector3 position = Vector3.Lerp(startPosition, destination, progress);
                    position.y += 4f * arcHeight * progress * (1f - progress);
                    Transform.position = position;
                    if (progress >= 1f)
                        BeginLand();
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
                jumper?.OnGapJumpFinished(false);
            }
        }

        private void BeginLand()
        {
            Transform.position = destination;
            RestoreRigidbody();

            phase = Phase.Land;
            timer = LandDuration;
            enemy.AnimationPresenter?.PlayLeapLand(LandDuration);

            if (HasTarget && movement != null)
                movement.FaceTarget(Target.position);
        }

        private void Complete()
        {
            if (finished)
                return;

            finished = true;
            jumper.OnGapJumpFinished(true);
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
