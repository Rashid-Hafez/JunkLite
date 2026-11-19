using UnityEngine;

namespace junklite
{
    [CreateAssetMenu(menuName = "Junklite/Mods/Pogo")]
    public class PogoModData : PassiveModData
    {
        [Header("Pogo Effect")]
        public float pogoForce = 12f;

        public override void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy, float damageDealt)
        {
            if (player == null) return;

            var playerState = player.PlayerState;
            if (playerState == null || !playerState.IsDownAttackRequested) return;

            var controller = player.Controller;
            if (controller == null) return;

            playerState.NotifyAttackAnimationInterrupted();
            controller.ApplyPogoLaunch(pogoForce);
            playerState.ScheduleRefundAirAttackAfterDoubleJump();
        }
    }
}