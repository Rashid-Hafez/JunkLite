using System.Collections;
using UnityEngine;

namespace junklite
{
    [CreateAssetMenu(menuName = "Junklite/Mods/Pogo")]
    public class PogoModData : PassiveModData
    {
        [Header("Pogo Effect")]
        public float pogoForce = 12f;

        [Tooltip("How long the player floats (zero gravity, zero vertical velocity) before the bounce fires.")]
        public float floatDuration = 0.08f;

        public override void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy, float damageDealt)
        {
            if (player == null) return;

            var playerState = player.PlayerState;
            if (playerState == null || !playerState.IsDownAttackRequested) return;

            var controller = player.Controller;
            if (controller == null) return;

            var rb = player.GetComponent<Rigidbody>();
            if (rb == null) return;

            playerState.NotifyAttackAnimationInterrupted();
            player.StartCoroutine(CoPogoFloat(controller, playerState, rb));
        }

        private IEnumerator CoPogoFloat(Character2D5Controller controller, PlayerState playerState, Rigidbody rb)
        {
            controller.SetGravityMultiplierOverride(0f);

            float elapsed = 0f;
            while (elapsed < floatDuration)
            {
                // Zero vertical velocity every frame to cancel any still-running attack push coroutine.
                var v = rb.linearVelocity;
                rb.linearVelocity = new Vector3(v.x, 0f, v.z);
                elapsed += Time.deltaTime;
                yield return null;
            }

            controller.ClearGravityMultiplierOverride();
            controller.ApplyExternalBounce(pogoForce);
            playerState.ScheduleRefundAirAttackAfterDoubleJump();
        }
    }
}