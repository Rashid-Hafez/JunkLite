using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Patrol state - enemy walks back and forth within patrol distance.
    /// Reverses direction at boundaries or when hitting walls.
    ///
    /// REQUIRES: Enemy must implement IPatroller
    /// OPTIONAL: IPatrolRhythm adds a pause at each end and an occasional
    /// mid-leg look back, so a patrol reads as a person and not a conveyor.
    ///
    /// Pure ACTION state: just handles movement.
    /// Detection is handled by DetectionZone trigger events.
    /// </summary>
    public class PatrolState : EnemyStateBase
    {
        private enum Phase
        {
            Walk,
            EndPause,
            LookBack
        }

        private IPatroller patroller;
        private IPatrolRhythm rhythm;
        private EnemyMovement movement;
        private Phase phase;
        private float timer;
        private float lookBackAt;

        public PatrolState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            patroller = GetCapability<IPatroller>();
            if (patroller == null)
            {
                Debug.LogError($"{enemy.gameObject.name}: PatrolState requires IPatroller interface!");
                return;
            }

            rhythm = GetCapability<IPatrolRhythm>();
            movement = enemy.Movement;

            if (movement != null)
                movement.MoveSpeed = patroller.PatrolSpeed;

            StartLeg();
        }

        public override void Update()
        {
            if (patroller == null) return;

            switch (phase)
            {
                case Phase.Walk:
                    UpdateWalk();
                    break;

                case Phase.EndPause:
                    timer -= Time.deltaTime;
                    if (timer <= 0f)
                        StartLeg();
                    break;

                case Phase.LookBack:
                    timer -= Time.deltaTime;
                    if (timer <= 0f)
                    {
                        movement.FlipFacing();
                        phase = Phase.Walk;
                        lookBackAt = -1f;
                        StartMoving();
                    }
                    break;
            }
        }

        public override void Exit()
        {
            movement?.Stop();
            enemy.AnimationPresenter?.SetLocomotionSpeed01(0f);
        }

        // ------------------------------------------------------------

        private void UpdateWalk()
        {
            // Resume movement after knockback or any interruption
            if (!movement.IsMoving && !movement.IsInKnockback)
            {
                StartMoving();
                return;
            }

            if (patroller.IsWallAhead() || patroller.IsLedgeAhead() || patroller.IsAtPatrolBoundary())
            {
                patroller.ReverseDirection();
                EndLeg();
                return;
            }

            if (lookBackAt > 0f && Time.time >= lookBackAt)
            {
                lookBackAt = -1f;
                BeginLookBack();
            }
        }

        private void StartLeg()
        {
            phase = Phase.Walk;
            lookBackAt = -1f;

            if (rhythm != null && rhythm.LookBackChance > 0f && Random.value <= rhythm.LookBackChance)
                lookBackAt = Time.time + Random.Range(0.4f, 1.5f) * Mathf.Max(0.1f, rhythm.RhythmScale);

            StartMoving();
        }

        private void EndLeg()
        {
            if (rhythm == null)
            {
                StartMoving();
                return;
            }

            Vector2 range = rhythm.PatrolEndPauseRange;
            float pause = Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y))
                * Mathf.Max(0.1f, rhythm.RhythmScale);
            if (pause <= 0f)
            {
                StartLeg();
                return;
            }

            // Stop still facing the way it was walking, wait, then turn and walk:
            // turning first and then standing read as a stutter.
            phase = Phase.EndPause;
            timer = pause;
            movement.Stop();
            enemy.AnimationPresenter?.SetLocomotionSpeed01(0f);
        }

        private void BeginLookBack()
        {
            phase = Phase.LookBack;
            timer = Mathf.Max(0.1f, rhythm.LookBackDuration);
            movement.Stop();
            movement.FlipFacing();
            enemy.AnimationPresenter?.SetLocomotionSpeed01(0f);
        }

        private void StartMoving()
        {
            if (movement == null || patroller == null) return;

            Vector3 axis = Transform.right;
            Vector3 direction = patroller.PatrolDirection > 0 ? axis : -axis;
            movement.MoveInDirection(direction);
            enemy.AnimationPresenter?.SetLocomotionSpeed01(1f);
        }
    }
}
