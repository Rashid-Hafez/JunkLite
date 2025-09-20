using UnityEngine;
using TMPro;

namespace junklite
{
    /// <summary>
    /// Root HUD script for the player's gameplay UI.
    /// Binds child StatBarUIs (Health, Armor, etc.) to the player's AttributeManager.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerUI : MonoBehaviour
    {
        [Header("Auto-Bind")]
        [Tooltip("If true, auto-binds to GameManager.Instance.Player on Enable and re-binds on respawn.")]
        [SerializeField] private bool autoBindToGameManager = true;

        [Tooltip("Hide this UI root when the player dies.")]
        [SerializeField] private bool hideOnDeath = false;

        [Header("References")]
        [SerializeField] private StatBarUI healthBar;
        [SerializeField] private StatBarUI armorBar;     // Optional but recommended: add Armor to AttributeType & CharacterStats
        [SerializeField] private TMP_Text playerNameText;// Optional

        // Runtime
        [SerializeField]private CharacterBase player;
        private AttributeManager attributes;

        // ---------- Public API ----------

        /// <summary>
        /// Bind this UI to a specific player character.
        /// </summary>
        public void BindToPlayer(CharacterBase target)
        {
            Unbind();

            player = target;
            if (player == null)
            {
                SetVisible(false);
                return;
            }

            attributes = player.GetComponent<AttributeManager>();
            if (attributes == null)
            {
                Debug.LogWarning("[PlayerUI] Target has no AttributeManager. UI will be hidden.");
                SetVisible(false);
                return;
            }

            // Name (optional)
            if (playerNameText != null && player.Stats != null)
                playerNameText.text = string.IsNullOrEmpty(player.Stats.characterName)
                    ? player.gameObject.name
                    : player.Stats.characterName;

            // Bind bars (event-driven)
            if (healthBar != null) healthBar.Bind(attributes.Health);

            if (armorBar != null)
            {
                // Prefer Armor as a real Attribute for live updates:
                var armorAttr = attributes.Get(AttributeType.Armor); // make sure AttributeType.Armor exists + CharacterStats includes it
                if (armorAttr != null)
                {
                    armorBar.Bind(armorAttr);
                }
                else
                {
                    // If you don't model Armor as an Attribute yet, just hide the bar (or show a static label elsewhere)
                    armorBar.Unbind();
                    armorBar.gameObject.SetActive(false);
                }
            }

            
            // Handle death visibility (optional)
            attributes.OnDeath += HandlePlayerDeath;

            SetVisible(true);
        }

        /// <summary>
        /// Unbinds current player & attributes and detaches UI listeners.
        /// </summary>
        public void Unbind()
        {
            if (attributes != null)
                attributes.OnDeath -= HandlePlayerDeath;

            // Unbind bars
            if (healthBar != null) healthBar.Unbind();
            if (armorBar != null) armorBar.Unbind();
           

            attributes = null;
            player = null;
        }

        // ---------- Unity lifecycle ----------

        private void OnEnable()
        {
            if (autoBindToGameManager && GameManager.Instance != null)
            {
                // Bind immediately if a player exists
                if (GameManager.Instance.Player != null)
                    BindToPlayer(GameManager.Instance.Player);

                // Re-bind automatically on respawn
                GameManager.Instance.OnPlayerSpawned += HandlePlayerSpawned;
            }
        }

        private void OnDisable()
        {
            if (autoBindToGameManager && GameManager.Instance != null)
                GameManager.Instance.OnPlayerSpawned -= HandlePlayerSpawned;

            Unbind();
        }

        private void OnDestroy()
        {
            Unbind();
        }

        // ---------- Handlers ----------

        private void HandlePlayerSpawned(PlayerCharacter newPlayer)
        {
            // Auto-rebind on respawn
            BindToPlayer(newPlayer);
        }

        private void HandlePlayerDeath()
        {
            if (hideOnDeath)
                SetVisible(false);
        }

        // ---------- Helpers ----------

        private void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }
    }
}
