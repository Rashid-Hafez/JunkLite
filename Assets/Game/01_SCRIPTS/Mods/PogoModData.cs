using UnityEngine;

namespace junklite
{
    [CreateAssetMenu(menuName = "Junklite/Mods/Pogo")]
    public class PogoModData : ModData
    {
        [Header("Pogo Effect")]
        public float pogoForce = 12f;

        public override bool OnHit(WeaponInstance weapon, EnemyCharacter enemy, PlayerCharacter player)
        {
            if (player == null)
                return false;

            var playerState = player.PlayerState;
            if (playerState == null || !playerState.IsDownAttackRequested)
                return false;

            var controller = player.Controller;
            if (controller == null)
                return false;

            // Use the controller's external bounce system
            // This ensures the bounce has fixed height regardless of jump input
            controller.ApplyExternalBounce(pogoForce);

            // Double jump is already reset in controller.ApplyExternalBounce (airJumpCount = 0).
            // Schedule refund of one air attack so player can pogo again only AFTER they use the double jump.
            playerState.ScheduleRefundAirAttackAfterDoubleJump();

            // Cancel attack lock so jump input can trigger after pogo
            playerState.NotifyAttackAnimationInterrupted();

            return true;
        }
    }
}