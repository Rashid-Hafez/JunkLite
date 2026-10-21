using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Kills the player on contact (pits, kill-Y slab below the level). Enemies are never killed here:
    /// they only die from combat damage.
    /// Put this on the <b>same GameObject</b> as the lethal trigger collider so only that volume kills.
    /// (Unity does not report which collider fired when this script sits on a parent with several colliders.)
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class DeathTrigger : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerCharacter>() == null) return;
            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return;

            PlayerLifecycle.Instance?.KillPlayer();
        }
    }
}
