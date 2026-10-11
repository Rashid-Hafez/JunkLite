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

        private const float MaxFlightTime = 2f;
        private const float GetUpDelay = 0.5f;
        private GameObject activeSource;
        private bool releaseRequested;
        private int releaseDirection;
        private Vector2 releaseForce;
        private float releaseDamage;

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

            activeSource = info.Source;
            releaseRequested = false;
            state?.ApplyStun(info.Duration + 0.5f);

            movementLock = controller?.AcquireMovementLock();
            physicsLock = controller?.AcquirePhysicsOverride();
            kinematicLock = controller?.AcquireKinematicLock();

            Transform follow = info.Anchor != null
                ? info.Anchor
                : info.Source != null ? info.Source.transform : null;
            float timer = 0f;

            // Timed grabs throw when Duration ends. Grabber-owned grabs wait for
            // ReleaseGrab; Duration is then only a safety timeout.
            while (!releaseRequested && timer < info.Duration)
            {
                if (!IsGrabbed || !player.IsAlive)
                {
                    Cancel();
                    yield break;
                }

                timer += Time.deltaTime;
                if (follow != null)
                    player.transform.position = follow.position + info.GrabOffset;

                yield return null;
            }

            if (!IsGrabbed || !player.IsAlive)
            {
                Cancel();
                yield break;
            }

            if (releaseRequested)
            {
                yield return Knockdown(info.Source);
                yield break;
            }

            int throwDirection = info.ThrowDirection;
            Vector2 throwForce = info.ThrowForce;
            float throwDamage = info.ThrowDamage;
            // Return physics ownership before damage and the throw impulse. The
            // movement lock remains until the transaction has completely finished.
            kinematicLock?.Dispose();
            kinematicLock = null;
            physicsLock?.Dispose();
            physicsLock = null;

            if (throwDamage > 0f)
            {
                player.ReceiveDamage(DamageRequest.Forced(throwDamage, info.Source)
                    .WithHitReaction(HitReactionRequest.None));
            }

            if (!IsGrabbed || !player.IsAlive)
            {
                Cancel();
                yield break;
            }

            if (controller != null && throwForce.sqrMagnitude > 0f)
            {
                Vector3 throwImpulse = controller.MovementAxis * throwDirection * throwForce.x
                                     + Vector3.up * throwForce.y;
                controller.ApplyExternalImpulse(throwImpulse);
            }

            Cancel();
        }

        // Grabber-owned release: fly on the impulse, land, damage, lie down, get up.
        private IEnumerator Knockdown(GameObject source)
        {
            var anim = player.GetComponentInChildren<SpineAnimationController>();

            // Stunned skips player air drag, so the impulse carries the flight.
            state?.ApplyStun(10f);
            kinematicLock?.Dispose();
            kinematicLock = null;
            physicsLock?.Dispose();
            physicsLock = null;
            controller?.StopAllVelocity();
            if (controller != null)
            {
                controller.ApplyExternalImpulse(controller.MovementAxis * releaseDirection * releaseForce.x
                                                + Vector3.up * releaseForce.y);
            }

            // Leave the ground check a moment to clear, then wait for the landing.
            float flight = 0f;
            while (flight < MaxFlightTime && (flight < 0.1f || controller == null || !controller.IsGrounded))
            {
                if (!IsGrabbed || !player.IsAlive)
                {
                    Cancel();
                    yield break;
                }

                flight += Time.deltaTime;
                yield return null;
            }

            controller?.StopAllVelocity();
            if (releaseDamage > 0f)
            {
                player.ReceiveDamage(DamageRequest.Forced(releaseDamage, source)
                    .WithHitReaction(HitReactionRequest.None));
            }

            if (!IsGrabbed || !player.IsAlive)
            {
                Cancel();
                yield break;
            }

            anim?.PlayKnockdown();
            float lieDown = anim != null && anim.TryGetDeathAnimationDuration(out float deathTime) ? deathTime : 0f;
            yield return new WaitForSeconds(lieDown + GetUpDelay);

            anim?.EndKnockdown();
            state?.ApplyStun(0f);
            Cancel();
        }

        /// <summary>Requests the throw for a grab held by source.</summary>
        public void RequestRelease(GameObject source, int throwDirection, Vector2 throwForce, float throwDamage)
        {
            if (!IsGrabbed || source != activeSource)
                return;

            releaseDirection = throwDirection;
            releaseForce = throwForce;
            releaseDamage = throwDamage;
            releaseRequested = true;
        }

        /// <summary>True when source currently holds the player.</summary>
        public bool IsHeldBy(GameObject source) => IsGrabbed && source != null && source == activeSource;

        public void Cancel()
        {
            kinematicLock?.Dispose();
            physicsLock?.Dispose();
            movementLock?.Dispose();

            kinematicLock = null;
            physicsLock = null;
            movementLock = null;
            activeSource = null;
            releaseRequested = false;
            IsGrabbed = false;
        }
    }
}
