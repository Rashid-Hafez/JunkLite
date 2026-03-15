using UnityEngine;

namespace junklite
{
    public class DashState : EnemyStateBase
    {
        private IDasher dasher;
        private EnemyMovement movement;
        private Hitbox hitbox;

        private Vector3 dashTarget;
        private Vector3 dashStartPosition;
        private float stopDistance;
        private float totalDashDistance;
        private bool hasStarted;
        private bool dashComplete;
        private bool attackTriggered;
        private bool attackWindowActive;
        private bool movementFinished;
        private float dashStartTime;
        private float attackWindowTimer;
        private float resolveTimer;
        private GameObject vfx;

        private const float MAX_DASH_DURATION = 2f;

        public DashState(EnemyCharacter enemy) : base(enemy) { }

        public override void Enter()
        {
            hasStarted = false;
            dashComplete = false;
            attackTriggered = false;
            attackWindowActive = false;
            movementFinished = false;
            dashStartTime = Time.time;
            attackWindowTimer = 0f;
            resolveTimer = 0f;

            dasher = GetCapability<IDasher>();
            if (dasher == null)
            {
                Debug.LogError($"{enemy.gameObject.name}: DashState requires IDasher interface!");
                return;
            }

            movement = enemy.Movement;
            hitbox = dasher.DashHitbox;
            stopDistance = dasher.DashStopDistance;
            vfx = dasher.DashVFXPrefab;

            if (HasTarget)
            {
                float distanceToTarget = movement.GetAbsAxisDistance(Transform.position, Target.position);

                if (distanceToTarget > stopDistance)
                {
                    float sign = Mathf.Sign(movement.GetSignedAxisDistance(Transform.position, Target.position));
                    Vector3 horizontalDir = movement.MovementAxis * sign;

                    dashTarget = Target.position - horizontalDir * stopDistance;
                    dashTarget.y = Transform.position.y;

                    Vector3 depthAxis = Vector3.Cross(Vector3.up, movement.MovementAxis).normalized;
                    float depthOffset = Vector3.Dot(dashTarget - Transform.position, depthAxis);
                    dashTarget -= depthAxis * depthOffset;

                    if (vfx != null) vfx.SetActive(true);
                    StartDash();
                }
                else
                {
                    dasher.OnDashComplete();
                }
            }
            else
            {
                dasher.OnDashComplete();
            }
        }

        private void StartDash()
        {
            hasStarted = true;
            dashStartPosition = Transform.position;
            totalDashDistance = movement != null
                ? movement.GetAbsAxisDistance(dashStartPosition, dashTarget)
                : 0f;

            movement?.FaceTarget(dashTarget);
            movement?.DashTo(dashTarget, dasher.DashSpeed);
        }

        public override void Update()
        {
            if (dasher == null || !hasStarted || dashComplete) return;

            if (!attackTriggered && ShouldTriggerAttack())
            {
                TriggerAttackWindow();
            }

            if (attackWindowActive)
            {
                attackWindowTimer -= Time.deltaTime;
                if (attackWindowTimer <= 0f)
                {
                    attackWindowActive = false;
                    hitbox?.Deactivate();
                }
            }

            if (!movementFinished && HasMovementFinished())
            {
                movementFinished = true;
                movement?.Stop();

                if (!attackTriggered)
                    TriggerAttackWindow();

                resolveTimer = Mathf.Max(0f, dasher.DashWhiffResolveDelay);
            }

            if (movementFinished)
            {
                if (resolveTimer > 0f)
                    resolveTimer -= Time.deltaTime;

                if (!attackWindowActive && resolveTimer <= 0f)
                    CompleteDash();
            }
        }

        private bool ShouldTriggerAttack()
        {
            if (movement == null) return true;
            if (totalDashDistance <= 0.01f) return true;

            float traveled = movement.GetAbsAxisDistance(dashStartPosition, Transform.position);
            float progress = Mathf.Clamp01(traveled / totalDashDistance);
            return progress >= Mathf.Clamp01(dasher.DashAttackStartNormalized);
        }

        private void TriggerAttackWindow()
        {
            attackTriggered = true;
            attackWindowActive = true;
            attackWindowTimer = Mathf.Max(0.01f, dasher.DashAttackActiveDuration);
            hitbox?.Activate();
        }

        private bool HasMovementFinished()
        {
            if (Time.time - dashStartTime > MAX_DASH_DURATION)
                return true;

            if (movement != null && movement.HasReachedDestination)
                return true;

            if (HasTarget && movement != null)
            {
                float distanceToPlayer = movement.GetAbsAxisDistance(Transform.position, Target.position);
                if (distanceToPlayer <= stopDistance)
                    return true;
            }

            if (movement != null)
            {
                float distanceToDashTarget = movement.GetAbsAxisDistance(Transform.position, dashTarget);
                if (distanceToDashTarget <= 0.3f)
                    return true;
            }

            return false;
        }

        private void CompleteDash()
        {
            if (dashComplete) return;
            dashComplete = true;

            hitbox?.Deactivate();
            movement?.Stop();
            if (vfx != null) vfx.SetActive(false);

            dasher.OnDashComplete();
        }

        public override bool CanBeInterrupted => dasher?.DashCanBeInterrupted ?? true;

        public override void Exit()
        {
            dashComplete = true;
            hitbox?.Deactivate();
            if (vfx != null) vfx.SetActive(false);
            movement?.Stop();
        }
    }
}