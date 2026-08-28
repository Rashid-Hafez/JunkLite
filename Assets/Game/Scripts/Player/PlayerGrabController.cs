using System;
using System.Collections;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Owns the player's grab/hold/throw transaction. PlayerCharacter remains the
    /// public IGrabbable facade and coroutine host.
    /// </summary>
    internal sealed class PlayerGrabController
    {
        private readonly PlayerCharacter player;
        private readonly PlayerState state;
        private readonly Character2D5Controller controller;

        private IDisposable movementLock;
        private IDisposable physicsLock;
        private IDisposable kinematicLock;

        public bool IsGrabbed { get; private set; }
        public bool CanBeGrabbed => player != null && player.IsActive && player.IsAlive && !IsGrabbed;

        public PlayerGrabController(
            PlayerCharacter player,
            PlayerState state,
            Character2D5Controller controller)
        {
            this.player = player;
            this.state = state;
            this.controller = controller;
        }

        public bool TryBegin()
        {
            if (!CanBeGrabbed)
                return false;

            IsGrabbed = true;
            return true;
        }

        public IEnumerator Execute(GrabInfo info)
        {
            if (!IsGrabbed || !player.IsAlive)
            {
                Cancel();
                yield break;
            }

            state?.ApplyStun(info.Duration + 0.5f);

            movementLock = controller?.AcquireMovementLock();
            physicsLock = controller?.AcquirePhysicsOverride();
            kinematicLock = controller?.AcquireKinematicLock();

            Transform enemyTransform = info.Source != null ? info.Source.transform : null;
            float timer = 0f;

            while (timer < info.Duration)
            {
                if (!IsGrabbed || !player.IsAlive)
                {
                    Cancel();
                    yield break;
                }

                timer += Time.deltaTime;
                if (enemyTransform != null)
                    player.transform.position = enemyTransform.position + info.GrabOffset;

                yield return null;
            }

            // Return physics ownership before damage and the throw impulse. The
            // movement lock remains until the transaction has completely finished.
            kinematicLock?.Dispose();
            kinematicLock = null;
            physicsLock?.Dispose();
            physicsLock = null;

            if (info.ThrowDamage > 0f)
            {
                player.ReceiveDamage(DamageRequest.Forced(info.ThrowDamage, info.Source)
                    .WithHitReaction(HitReactionRequest.None));
            }

            if (!IsGrabbed || !player.IsAlive)
            {
                Cancel();
                yield break;
            }

            if (controller != null && info.ThrowForce.sqrMagnitude > 0f)
            {
                Vector3 throwImpulse = controller.MovementAxis * info.ThrowDirection * info.ThrowForce.x
                                     + Vector3.up * info.ThrowForce.y;
                controller.ApplyExternalImpulse(throwImpulse);
            }

            Cancel();
        }

        public void Cancel()
        {
            kinematicLock?.Dispose();
            physicsLock?.Dispose();
            movementLock?.Dispose();

            kinematicLock = null;
            physicsLock = null;
            movementLock = null;
            IsGrabbed = false;
        }
    }
}
