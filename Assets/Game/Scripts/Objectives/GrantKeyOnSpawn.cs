using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Scene-local: gives the player a keyring item as soon as they spawn
    /// (e.g. the Grapple Hook for a practice level). Does nothing if they already have it.
    /// </summary>
    public class GrantKeyOnSpawn : MonoBehaviour
    {
        [SerializeField] private string keyTag = PlayerKeyring.GrappleHook;

        private PlayerLifecycle subscribedLifecycle;

        private void OnEnable() => Rebind();

        private void Start()
        {
            Rebind();
            if (subscribedLifecycle != null && subscribedLifecycle.Player != null)
                Grant(subscribedLifecycle.Player);
        }

        private void OnDisable()
        {
            if (subscribedLifecycle != null) subscribedLifecycle.PlayerSpawned -= Grant;
            subscribedLifecycle = null;
        }

        private void Rebind()
        {
            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            if (lifecycle == subscribedLifecycle) return;
            if (subscribedLifecycle != null) subscribedLifecycle.PlayerSpawned -= Grant;
            subscribedLifecycle = lifecycle;
            if (subscribedLifecycle != null) subscribedLifecycle.PlayerSpawned += Grant;
        }

        private void Grant(PlayerCharacter player)
        {
            PlayerKeyring keyring = player != null ? player.GetComponentInParent<PlayerKeyring>() : null;
            if (keyring != null && !keyring.Has(keyTag)) keyring.Add(keyTag);
        }
    }
}
