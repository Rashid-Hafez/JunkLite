using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Completes the ObjectiveLink on this enemy when it dies (e.g. DEFEAT THE BRUTE),
    /// so doors can wait on a kill through DoorAccessRule.requiredObjective.
    /// Optionally also grants a keyring item (e.g. the grapple hook) before the objective completes.
    /// </summary>
    [RequireComponent(typeof(ObjectiveLink))]
    public class ObjectiveOnDeath : MonoBehaviour
    {
        [Tooltip("Optional. Key/item tag added to the player's keyring on death (e.g. Grapple Hook).")]
        [SerializeField] private string grantKey;

        private AttributeManager attributes;
        private ObjectiveLink objectiveLink;

        private void Awake()
        {
            attributes = GetComponentInParent<AttributeManager>();
            objectiveLink = GetComponent<ObjectiveLink>();
        }

        private void OnEnable()
        {
            if (attributes != null) attributes.OnDeath += HandleDeath;
        }

        private void OnDisable()
        {
            if (attributes != null) attributes.OnDeath -= HandleDeath;
        }

        private void HandleDeath()
        {
            if (!string.IsNullOrEmpty(grantKey))
            {
                PlayerCharacter player = PlayerLifecycle.Instance != null ? PlayerLifecycle.Instance.Player : null;
                PlayerKeyring keyring = player != null ? player.GetComponentInParent<PlayerKeyring>() : null;
                if (keyring != null) keyring.Add(grantKey);
            }
            objectiveLink.TryComplete();
        }
    }
}
