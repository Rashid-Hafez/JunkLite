using System.Collections;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Performs attack-specific movement through Character2D5Controller. It never
    /// accesses the player Rigidbody and only owns motion for one attack execution.
    /// </summary>
    internal sealed class WeaponAttackMotion
    {
        private readonly Character2D5Controller controller;
        private readonly float defaultPushDuration;

        private int activeExecutionId = -1;
        private int horizontalMotionOwners;
        private int verticalMotionOwners;
        private bool gravityOverrideActive;

        public WeaponAttackMotion(
            Character2D5Controller controller,
            float defaultPushDuration)
        {
            this.controller = controller;
            this.defaultPushDuration = Mathf.Max(0.01f, defaultPushDuration);
        }

        public void BeginExecution(int executionId)
        {
            if (activeExecutionId >= 0 && activeExecutionId != executionId)
                EndExecution(activeExecutionId);

            activeExecutionId = executionId;
            horizontalMotionOwners = 0;
            verticalMotionOwners = 0;
            gravityOverrideActive = false;
        }

        public IEnumerator ApplyPush(
            int executionId,
            AttackDirection direction,
            float facing,
            float forwardImpulse,
            float verticalImpulse,
            float duration,
            AnimationCurve lungeCurve)
        {
            if (!IsCurrent(executionId) || controller == null)
                yield break;

            Vector3 right = controller.transform.right.normalized;
            Vector3 up = controller.transform.up.normalized;
            Vector3 peakVelocity = Vector3.zero;

            if (direction == AttackDirection.Side)
            {
                if (!Mathf.Approximately(forwardImpulse, 0f))
                    peakVelocity += right * (forwardImpulse * facing);
                if (!Mathf.Approximately(verticalImpulse, 0f))
                    peakVelocity += up * verticalImpulse;
            }
            else if (!Mathf.Approximately(verticalImpulse, 0f))
            {
                float signedVertical = direction == AttackDirection.Down
                    ? -verticalImpulse
                    : verticalImpulse;
                peakVelocity += up * signedVertical;
            }

            bool ownsHorizontal = !Mathf.Approximately(Vector3.Dot(peakVelocity, right), 0f);
            bool ownsVertical = !Mathf.Approximately(Vector3.Dot(peakVelocity, up), 0f);
            if (!ownsHorizontal && !ownsVertical)
                yield break;

            AcquireMotionAxes(ownsHorizontal, ownsVertical);

            float resolvedDuration = duration > 0f ? duration : defaultPushDuration;
            float elapsed = 0f;

            while (elapsed < resolvedDuration && IsCurrent(executionId))
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / resolvedDuration);
                float multiplier = lungeCurve != null && lungeCurve.length > 0
                    ? lungeCurve.Evaluate(t)
                    : 1f - (t * t);

                Vector3 velocity = controller.Velocity;
                float targetRight = Vector3.Dot(velocity, right);
                float targetUp = Vector3.Dot(velocity, up);
                float peakRight = Vector3.Dot(peakVelocity, right);
                float peakUp = Vector3.Dot(peakVelocity, up);

                if (ownsHorizontal)
                    targetRight = peakRight * multiplier;
                if (ownsVertical)
                    targetUp = peakUp * multiplier;

                Vector3 forward = controller.transform.forward.normalized;
                Vector3 forwardComponent = Vector3.Project(velocity, forward);
                controller.SetVelocity(right * targetRight + up * targetUp + forwardComponent);
                yield return null;
            }

            if (IsCurrent(executionId))
                ReleaseMotionAxes(ownsHorizontal, ownsVertical, right, up);
        }

        public IEnumerator ApplySmoothRecoil(
            int executionId,
            AttackDirection direction,
            float facing,
            float recoilMagnitude,
            float duration)
        {
            if (!IsCurrent(executionId) || controller == null ||
                Mathf.Approximately(recoilMagnitude, 0f))
            {
                yield break;
            }

            Vector3 recoilDirection = direction switch
            {
                AttackDirection.Down => Vector3.up,
                AttackDirection.Up => Vector3.down,
                _ => controller.transform.right.normalized * -facing
            };

            bool ownsHorizontal = direction == AttackDirection.Side;
            bool ownsVertical = !ownsHorizontal;
            AcquireMotionAxes(ownsHorizontal, ownsVertical);

            float resolvedDuration = duration > 0f ? duration : 0.1f;
            Vector3 peakVelocity = recoilDirection * recoilMagnitude;
            float elapsed = 0f;

            while (elapsed < resolvedDuration && IsCurrent(executionId))
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / resolvedDuration);
                float multiplier = 1f - (t * t);

                Vector3 velocity = controller.Velocity;
                Vector3 normalizedDirection = recoilDirection.normalized;
                float currentAlong = Vector3.Dot(velocity, normalizedDirection);
                float targetAlong = peakVelocity.magnitude * multiplier;
                velocity += normalizedDirection * (targetAlong - currentAlong);
                controller.SetVelocity(velocity);
                yield return null;
            }

            if (IsCurrent(executionId))
            {
                ReleaseMotionAxes(
                    ownsHorizontal,
                    ownsVertical,
                    controller.transform.right.normalized,
                    controller.transform.up.normalized);
            }
        }

        public IEnumerator HoldDownAttackFloat(int executionId, float duration)
        {
            if (!IsCurrent(executionId) || controller == null || duration <= 0f)
                yield break;

            Vector3 velocity = controller.Velocity;
            velocity.y = 0f;
            controller.SetVelocity(velocity);
            BeginGravityOverride(executionId, 0f);

            float elapsed = 0f;
            while (elapsed < duration && IsCurrent(executionId))
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            EndGravityOverride(executionId);
        }

        public IEnumerator ApplyHitstop(int executionId, float duration)
        {
            if (!IsCurrent(executionId) || controller == null || duration <= 0f)
                yield break;

            controller.StopAllVelocity();
            float elapsed = 0f;
            while (elapsed < duration && IsCurrent(executionId))
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        public void BeginHover(int executionId, float gravityMultiplier)
        {
            if (!IsCurrent(executionId) || controller == null)
                return;

            BeginGravityOverride(executionId, gravityMultiplier);
            Vector3 velocity = controller.Velocity;
            velocity.y = 0f;
            controller.SetVelocity(velocity);
        }

        public void EndHover(int executionId)
        {
            EndGravityOverride(executionId);
        }

        public void ApplyImmediateRecoil(
            int executionId,
            AttackDirection direction,
            float facing,
            float recoilMagnitude)
        {
            if (!IsCurrent(executionId) || controller == null ||
                direction != AttackDirection.Side || Mathf.Approximately(recoilMagnitude, 0f))
            {
                return;
            }

            controller.ApplyExternalImpulse(
                controller.transform.right.normalized * -facing * recoilMagnitude,
                ForceMode.Impulse,
                interruptSpecialMovement: false);
        }

        public void EndExecution(int executionId)
        {
            if (!IsCurrent(executionId))
                return;

            if (controller != null)
            {
                if (gravityOverrideActive)
                    controller.ClearGravityMultiplierOverride();

                if (horizontalMotionOwners > 0 || verticalMotionOwners > 0)
                {
                    ClearControlledVelocity(
                        horizontalMotionOwners > 0,
                        verticalMotionOwners > 0,
                        controller.transform.right.normalized,
                        controller.transform.up.normalized);
                }
            }

            gravityOverrideActive = false;
            horizontalMotionOwners = 0;
            verticalMotionOwners = 0;
            activeExecutionId = -1;
        }

        private bool IsCurrent(int executionId)
        {
            return activeExecutionId == executionId;
        }

        private void BeginGravityOverride(int executionId, float multiplier)
        {
            if (!IsCurrent(executionId) || controller == null)
                return;

            gravityOverrideActive = true;
            controller.SetGravityMultiplierOverride(multiplier);
        }

        private void EndGravityOverride(int executionId)
        {
            if (!IsCurrent(executionId) || controller == null || !gravityOverrideActive)
                return;

            controller.ClearGravityMultiplierOverride();
            gravityOverrideActive = false;
        }

        private void AcquireMotionAxes(bool horizontal, bool vertical)
        {
            if (horizontal)
                horizontalMotionOwners++;
            if (vertical)
                verticalMotionOwners++;
        }

        private void ReleaseMotionAxes(
            bool horizontal,
            bool vertical,
            Vector3 right,
            Vector3 up)
        {
            if (horizontal)
                horizontalMotionOwners = Mathf.Max(0, horizontalMotionOwners - 1);
            if (vertical)
                verticalMotionOwners = Mathf.Max(0, verticalMotionOwners - 1);

            ClearControlledVelocity(
                horizontal && horizontalMotionOwners == 0,
                vertical && verticalMotionOwners == 0,
                right,
                up);
        }

        private void ClearControlledVelocity(
            bool clearHorizontal,
            bool clearVertical,
            Vector3 right,
            Vector3 up)
        {
            if (controller == null || (!clearHorizontal && !clearVertical))
                return;

            Vector3 velocity = controller.Velocity;
            if (clearHorizontal)
                velocity -= right * Vector3.Dot(velocity, right);
            if (clearVertical)
                velocity -= up * Vector3.Dot(velocity, up);
            controller.SetVelocity(velocity);
        }
    }
}
