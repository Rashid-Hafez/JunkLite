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

            if (player.LastAttackDirection != AttackDirection.Down)
                return false;

            var controller = player.Controller;
            if (controller == null)
                return false;

            // Use the controller's external bounce system
            // This ensures the bounce has fixed height regardless of jump input
            controller.ApplyExternalBounce(pogoForce);

            return true;
        }
    }
}