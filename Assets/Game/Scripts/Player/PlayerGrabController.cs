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
        // Hop away from the grabber on a successful escape (x along the lane, y up).
        private static readonly Vector2 EscapeKick = new(6f, 5f);
        private GameObject activeSource;
        private bool releaseRequested;
        private int releaseDirection;
        private Vector2 releaseForce;
        private float releaseDamage;
        private int escapePresses;
        private int escapePressesNeeded;

        public bool IsGrabbed { get; private set; }
        /// <summary>Jump presses so far toward breaking the current hold.</summary>
        public int EscapePresses => escapePresses;
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
            escapePresses = 0;
            escapePressesNeeded = 0;
            return true;
        }

        /// <summary>A jump press while held. Attack input never counts: only mashing jump breaks a hold.</summary>
        public void RegisterEscapePress()
        {
            if (IsGrabbed && escapePressesNeeded > 0)
                escapePresses++;
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
            escapePresses = 0;
            escapePressesNeeded = Mathf.Max(0, info.EscapePresses);
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

                if (escapePressesNeeded > 0 && escapePresses >= escapePressesNeeded)
                {
                    Escape(info);
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

        /// <summary>Broke free by mashing jump: released where she is, hops away, the grabber is told.</summary>
        private void Escape(GrabInfo info)
        {
            GameObject source = info.Source;
            Cancel();
            state?.ApplyStun(0f);

            if (controller != null)
            {
                float away = 1f;
                if (source != null)
                {
                    float along = Vector3.Dot(player.transform.position - source.transform.position, controller.MovementAxis);
                    away = Mathf.Abs(along) > 0.01f ? Mathf.Sign(along) : -info.ThrowDirection;
                }

                controller.StopAllVelocity();
                controller.ApplyExternalImpulse(controller.MovementAxis * (away * EscapeKick.x) + Vector3.up * EscapeKick.y);
            }

            info.OnEscaped?.Invoke();
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
            escapePresses = 0;
            escapePressesNeeded = 0;
            IsGrabbed = false;
        }
    }
}
