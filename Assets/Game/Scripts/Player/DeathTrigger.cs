using UnityEngine;

namespace junklite
{
    public class DeathTrigger : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            if (other.CompareTag("Player"))
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