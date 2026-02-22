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
        [SerializeField] private WeaponUI weaponUI;


        [Header("Mod UIs")]
        [SerializeField] private PhantomStrikeUI phantomStrikeUI;
        [SerializeField] private CombatModUI combatModUI;

        [Header("Inventory Panel")]
        [Tooltip("The root GameObject of the inventory panel to show/hide")]
        [SerializeField] private GameObject inventoryPanel;
        [Tooltip("The InventoryUI component (usually on the inventory panel or its parent)")]
        [SerializeField] private InventoryUI inventoryUI;

        // Runtime
        private CharacterBase player;
        private AttributeManager attributes;
        private WeaponManager _weaponManager;
        private ModManager _modManager;
        private InventoryComponent inventory;
        private bool isInventoryOpen = false;

        // -----------------------------------------------------------------------

        public bool IsInventoryOpen => isInventoryOpen;

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

            // --------- Bind Weapon UI ----------
            _weaponManager = player.GetComponent<WeaponManager>();
            if (weaponUI != null && _weaponManager != null)
                weaponUI.Bind(_weaponManager);

            // --------- Bind Inventory ----------
            inventory = player.GetComponent<InventoryComponent>();

            // --------- Bind Mod Manager + Combat Mod UI ----------
            _modManager = player.GetComponent<ModManager>();
            if (combatModUI != null && _modManager != null && _weaponManager != null)
                combatModUI.Bind(_modManager, _weaponManager);

            // --------- Bind Phantom Strike UI ----------
            if (phantomStrikeUI != null && player is PlayerCharacter pc)
                phantomStrikeUI.Bind(pc);

            // --------- Subscribe to combat mode for mod UI refresh ----------
            if (_weaponManager != null)
                _weaponManager.OnCombatModeChanged += RefreshModUIs;

            SetVisible(true);
        }

        private void RefreshModUIs()
        {
            if (phantomStrikeUI != null)
                phantomStrikeUI.RefreshTracker();
        }

        // -----------------------------------------------------------------------

        public void Unbind()
        {
            if (attributes != null)
                attributes.OnDeath -= HandlePlayerDeath;

            if (_weaponManager != null)
                _weaponManager.OnCombatModeChanged -= RefreshModUIs;

            if (healthBar != null) healthBar.Unbind();
            if (armorBar != null) armorBar.Unbind();
            if (weaponUI != null) weaponUI.Unbind();
            if (combatModUI != null) combatModUI.Unbind();
            if (phantomStrikeUI != null) phantomStrikeUI.Unbind();

            player = null;
            attributes = null;
            _weaponManager = null;
            _modManager = null;
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

            // Subscribe to inventory toggle
            if (GameInputManager.Instance != null)
                GameInputManager.Instance.OnInventoryToggle += HandleInventoryToggle;

            // Ensure inventory starts closed
            CloseInventory();
        }

        private void OnDisable()
        {
            if (autoBindToGameManager && GameManager.Instance != null)
                GameManager.Instance.OnPlayerSpawned -= HandlePlayerSpawned;

            // Unsubscribe from inventory toggle
            if (GameInputManager.Instance != null)
                GameInputManager.Instance.OnInventoryToggle -= HandleInventoryToggle;

            // Re-enable gameplay input if we're disabled while inventory is open
            if (isInventoryOpen && GameInputManager.Instance != null)
                GameInputManager.Instance.SetGameplayInputEnabled(true);

            Unbind();
        }

        private void OnDestroy()
        {
            // Re-enable gameplay input if destroyed while inventory is open
            if (isInventoryOpen && GameInputManager.Instance != null)
                GameInputManager.Instance.SetGameplayInputEnabled(true);

            Unbind();
        }

        // -----------------------------------------------------------------------

        private void HandlePlayerSpawned(PlayerCharacter newPlayer)
        {
            BindToPlayer(newPlayer);
        }

        private void HandlePlayerDeath()
        {
            // Close inventory on death
            if (isInventoryOpen)
                CloseInventory();

            if (hideOnDeath)
                SetVisible(false);
        }

        // -----------------------------------------------------------------------
        // INVENTORY TOGGLE
        // -----------------------------------------------------------------------

        private void HandleInventoryToggle()
        {
            if (isInventoryOpen)
                CloseInventory();
            else
                OpenInventory();
        }

        /// <summary>
        /// Opens the inventory panel and pauses gameplay input.
        /// </summary>
        public void OpenInventory()
        {
            if (isInventoryOpen) return;

            isInventoryOpen = true;

            // Show inventory panel
            if (inventoryPanel != null)
                inventoryPanel.SetActive(true);

            // Bind InventoryUI to player's inventory and weapon manager
            if (inventoryUI != null && player != null)
            {
                inventoryUI.Bind(inventory, _weaponManager);
            }

            // Pause gameplay input
            if (GameInputManager.Instance != null)
                GameInputManager.Instance.SetGameplayInputEnabled(false);

            // Debug.Log("[PlayerUI] Inventory opened - gameplay input paused");
        }

        /// <summary>
        /// Closes the inventory panel and resumes gameplay input.
        /// </summary>
        public void CloseInventory()
        {
            if (!isInventoryOpen && inventoryPanel != null && !inventoryPanel.activeSelf)
            {
                // Just ensure it's hidden on initial setup
                inventoryPanel.SetActive(false);
                return;
            }

            isInventoryOpen = false;

            // Unbind InventoryUI
            if (inventoryUI != null)
                inventoryUI.Unbind();

            // Hide inventory panel
            if (inventoryPanel != null)
                inventoryPanel.SetActive(false);

            // Resume gameplay input
            if (GameInputManager.Instance != null)
                GameInputManager.Instance.SetGameplayInputEnabled(true);

            // Debug.Log("[PlayerUI] Inventory closed - gameplay input resumed");
        }

        // -----------------------------------------------------------------------

        private void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }
    }
}