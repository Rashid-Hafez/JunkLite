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
                if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
                    return;

                PlayerLifecycle.Instance?.KillPlayer();
                return;
            }

            var enemy = other.GetComponentInParent<EnemyCharacter>();
            if (enemy != null && enemy.IsAlive)
                DamageReceiverUtility.Receive(
                    enemy,
                    DamageRequest.Forced(99999f, gameObject));
        }
    }
}
