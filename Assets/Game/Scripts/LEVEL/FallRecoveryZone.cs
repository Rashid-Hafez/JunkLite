using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Trigger at the bottom of a pit: puts the player back on the lane at returnPoint and deals
    /// fixed damage through the normal damage path. Lethal falls use DeathTrigger instead.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class FallRecoveryZone : MonoBehaviour
    {
        [Tooltip("Where the player is put back. Its Y rotation is the lane yaw.")]
        [SerializeField] private Transform returnPoint;
        [SerializeField] private float damage = 10f;

        private void Awake() => GetComponent<Collider>().isTrigger = true;

        private void OnTriggerEnter(Collider other)
        {
            if (returnPoint == null || !other.CompareTag("Player")) return;
            PlayerCharacter player = other.GetComponentInParent<PlayerCharacter>();
            if (player == null || player.Controller == null) return;

            player.Controller.SnapToLane(returnPoint.position, returnPoint.eulerAngles.y);
            if (damage > 0f)
                DamageReceiverUtility.Receive(player, DamageRequest.Forced(damage, gameObject));
        }
    }
}
