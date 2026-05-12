using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Put this on the <b>same GameObject</b> as the lethal trigger collider so only that volume kills.
    /// (Unity does not report which collider fired when this script sits on a parent with several colliders.)
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class DeathTrigger : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerCharacter>() != null)
            {
                var gm = GameManager.Instance;
                if (gm == null || !gm.IsPlaying) return;
                gm.KillPlayer();
                return;
            }

            var enemy = other.GetComponentInParent<EnemyCharacter>();
            if (enemy != null && enemy.IsAlive)
                enemy.TakeDamage(new DamageInfo(99999f, gameObject));
        }
    }
}
