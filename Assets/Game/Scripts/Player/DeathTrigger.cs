using UnityEngine;

namespace junklite
{
    public class DeathTrigger : MonoBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;

            var gm = GameManager.Instance;
            if (gm == null || !gm.IsPlaying) return;

            gm.RestartLevel();
        }
    }
}