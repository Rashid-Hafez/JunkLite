using UnityEngine;
using TMPro;

namespace junklite
{
    /// <summary>
    /// Root HUD script for the player's gameplay UI.
    /// Binds health, armor, name, weapon UI, and inventory UI to the player.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerUI : MonoBehaviour
    {
        [Header("Auto-Bind")]
        [SerializeField] private bool autoBindToGameManager = true;
        [SerializeField] private bool hideOnDeath = false;

        [Header("References")]
        [SerializeField] private StatBarUI healthBar;
        [SerializeField] private StatBarUI armorBar;
        [SerializeField] private TMP_Text playerNameText;

        [Header("UI Extensions (Weapon + Inventory)")]
        [SerializeField] private WeaponUI weaponUI;               // new
        [SerializeField] private InventoryModsUI inventoryModsUI; // optional new UI

        // Runtime
        private CharacterBase player;
        private AttributeManager attributes;
        private WeaponHolder weaponHolder;
        private InventoryComponent inventory;

        // -----------------------------------------------------------------------

        public void BindToPlayer(CharacterBase target)
        {
            Unbind(); // clean before binding new target

            player = target;
            if (player == null)
            {
                SetVisible(false);
                return;
            }

            // --------- Bind Attributes ----------
            attributes = player.GetComponent<AttributeManager>();
            if (attributes == null)
            {
                Debug.LogWarning("[PlayerUI] Player has no AttributeManager.");
                SetVisible(false);
                return;
            }

            // Player name
            if (playerNameText != null && player.Stats != null)
            {
                playerNameText.text = string.IsNullOrEmpty(player.Stats.characterName)
                    ? player.gameObject.name
                    : player.Stats.characterName;
            }

            // Health bar
            if (healthBar != null)
                healthBar.Bind(attributes.Health);

            // Armor bar (if exists)
            if (armorBar != null)
            {
                var armorAttr = attributes.Get(AttributeType.Armor);
                if (armorAttr != null)
                    armorBar.Bind(armorAttr);
                else
                    armorBar.gameObject.SetActive(false);
            }

            // Handle death visibility
            attributes.OnDeath += HandlePlayerDeath;

            // --------- Bind Weapon Holder ----------
            weaponHolder = player.GetComponent<WeaponHolder>();
            if (weaponUI != null && weaponHolder != null)
            {
                weaponUI.Bind(weaponHolder);
                weaponUI.RefreshWeapon(); // initialize UI visibility & slots
            }

            // --------- Bind Inventory (if exists) ----------
            inventory = player.GetComponent<InventoryComponent>();
            if (inventoryModsUI != null && inventory != null)
            {
                inventoryModsUI.Bind(inventory);
            }

            SetVisible(true);
        }

        // -----------------------------------------------------------------------

        public void Unbind()
        {
            // Unsubscribe from attribute events
            if (attributes != null)
                attributes.OnDeath -= HandlePlayerDeath;

            // Unbind health + armor
            if (healthBar != null)
                healthBar.Unbind();

            if (armorBar != null)
                armorBar.Unbind();

            // Unbind weapon UI
            if (weaponUI != null)
                weaponUI.Unbind();

            // Unbind inventory UI
            if (inventoryModsUI != null)
                inventoryModsUI.Unbind();

            player = null;
            attributes = null;
            weaponHolder = null;
            inventory = null;
        }

        // -----------------------------------------------------------------------

        private void OnEnable()
        {
            if (autoBindToGameManager && GameManager.Instance != null)
            {
                if (GameManager.Instance.Player != null)
                    BindToPlayer(GameManager.Instance.Player);

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

        // -----------------------------------------------------------------------

        private void HandlePlayerSpawned(PlayerCharacter newPlayer)
        {
            BindToPlayer(newPlayer);
        }

        private void HandlePlayerDeath()
        {
            if (hideOnDeath)
                SetVisible(false);
        }

        // -----------------------------------------------------------------------

        private void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }
    }
}
