using UnityEngine;

namespace junklite
{
    public class DashState : EnemyStateBase
    {
        private IDasher dasher;
        private ILockedDashAim aimLock;
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
        private float lastProgress;
        private float lastProgressTime;

        private const float MAX_DASH_DURATION = 2f;
        // A locked dash that stops gaining ground for this long hit a wall.
        private const float STALL_TIME = 0.15f;

        public DashState(EnemyCharacter enemy) : base(enemy) { }

        /// <summary>A locked dash that stopped short against a wall (read in OnDashComplete).</summary>
        public bool EndedAtWall { get; private set; }

        public override void Enter()
        {
            hasStarted = false;
            dashComplete = false;
            EndedAtWall = false;
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
            aimLock = movement != null ? GetCapability<ILockedDashAim>() : null;

            if (aimLock != null)
            {
                // Locked aim: run the full distance along the facing the charge locked,
                // wherever the target is now. Dodging through to the other side beats it.
                dashTarget = Transform.position
                    + movement.MovementAxis * (movement.FacingDirection * aimLock.LockedDashDistance);
                if (vfx != null) vfx.SetActive(true);
                StartDash();
            }
            else if (HasTarget)
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
                    // Already in reach (the target closed in during the charge):
                    // strike in place. Skipping the attack would resolve as a whiff.
                    if (vfx != null) vfx.SetActive(true);
                    StrikeInPlace();
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
            lastProgress = 0f;
            lastProgressTime = Time.time;

            movement?.FaceTarget(dashTarget);
            movement?.DashTo(dashTarget, dasher.DashSpeed);
        }

        private void StrikeInPlace()
        {
            hasStarted = true;
            dashStartPosition = Transform.position;
            dashTarget = dashStartPosition;
            totalDashDistance = 0f;

            movement?.Stop();
            movement?.FaceTarget(Target.position);
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

                // A locked dash is only dangerous while it runs.
                if (aimLock != null && attackWindowActive)
                {
                    attackWindowActive = false;
                    hitbox?.Deactivate();
                }

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

            if (aimLock != null)
            {
                if (movement.GetAbsAxisDistance(Transform.position, dashTarget) <= 0.3f)
                    return true;

                EndedAtWall = IsStalled();
                return EndedAtWall;
            }

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

        private bool IsStalled()
        {
            float traveled = movement.GetAbsAxisDistance(dashStartPosition, Transform.position);
            if (traveled > lastProgress + 0.05f)
            {
                lastProgress = traveled;
                lastProgressTime = Time.time;
                return false;
            }

            return Time.time - lastProgressTime > STALL_TIME;
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