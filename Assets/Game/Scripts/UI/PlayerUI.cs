using UnityEngine;
using TMPro;
using UnityEngine.Serialization;

namespace junklite
{
    [DisallowMultipleComponent]
    public class PlayerUI : MonoBehaviour
    {
        [Header("Auto-Bind")]
        [FormerlySerializedAs("autoBindToGameManager")]
        [SerializeField] private bool autoBindToPlayerLifecycle = true;
        [SerializeField] private bool hideOnDeath = false;

        [Header("References")]
        [SerializeField] private StatBarUI healthBar;
        [SerializeField] private StatBarUI armorBar;
        [SerializeField] private TMP_Text playerNameText;
        [SerializeField] private HealthIcon_Damaged healthIconFeedback;

        [Header("Combat HUD")]
        [SerializeField] private ModCombatUI modCombatUI;

        [Header("Inventory Panel")]
        [SerializeField] private GameObject inventoryPanel;
        [SerializeField] private InventoryUI inventoryUI;

        [Header("Weapon Pickup Panel")]
        [SerializeField] private GameObject weaponPickupPanel;
        [SerializeField] private WeaponPickupUI weaponPickupUI;

        // Runtime
        private PlayerCharacter player;
        private AttributeManager attributes;
        private WeaponManager _weaponManager;
        private ModManager _modManager;
        private InventoryComponent inventory;
        private bool isInventoryOpen;
        private bool isWeaponPickupOpen;
        private WeaponPickupInteractable activeInteractable;

        // -----------------------------------------------------------------------

        public bool IsInventoryOpen => isInventoryOpen;

        // -----------------------------------------------------------------------

        public void BindToPlayer(PlayerCharacter target)
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
                Debug.LogWarning("[PlayerUI] Player has no AttributeManager.");
                SetVisible(false);
                return;
            }

            if (playerNameText != null && player.Stats != null)
            {
                playerNameText.text = string.IsNullOrEmpty(player.Stats.characterName)
                    ? player.gameObject.name
                    : player.Stats.characterName;
            }

            if (healthBar != null)
                healthBar.Bind(attributes.Health);

            if (healthIconFeedback != null)
                healthIconFeedback.Bind(attributes.Health);

            if (armorBar != null)
            {
                var armorAttr = attributes.Get(AttributeType.Armor);
                if (armorAttr != null)
                    armorBar.Bind(armorAttr);
                else
                    armorBar.gameObject.SetActive(false);
            }

            attributes.OnDeath += HandlePlayerDeath;

            _weaponManager = player.GetComponent<WeaponManager>();
            _modManager = player.GetComponent<ModManager>();
            inventory = player.GetComponent<InventoryComponent>();

            if (modCombatUI != null && _modManager != null && _weaponManager != null)
                modCombatUI.Bind(_modManager, _weaponManager);

            SetVisible(true);
        }

        // -----------------------------------------------------------------------

        public void Unbind()
        {
            if (attributes != null)
                attributes.OnDeath -= HandlePlayerDeath;

            if (healthBar != null) healthBar.Unbind();
            if (healthIconFeedback != null) healthIconFeedback.Unbind();
            if (armorBar != null) armorBar.Unbind();
            if (modCombatUI != null) modCombatUI.Unbind();

            if (weaponPickupUI != null)
            {
                weaponPickupUI.OnClosed -= HandleWeaponPickupClosed;
                weaponPickupUI.Unbind();
            }

            player = null;
            attributes = null;
            _weaponManager = null;
            _modManager = null;
            inventory = null;
        }

        // -----------------------------------------------------------------------

        private void OnEnable()
        {
            if (autoBindToPlayerLifecycle && PlayerLifecycle.Instance != null)
            {
                if (PlayerLifecycle.Instance.Player != null)
                    BindToPlayer(PlayerLifecycle.Instance.Player);

                PlayerLifecycle.Instance.PlayerSpawned += HandlePlayerSpawned;
            }

            var input = GameInputManager.Instance;
            if (input != null)
            {
                input.OnInventoryToggle += HandleInventoryToggle;
                input.OnInteract += HandleInteract;
                input.OnUICancel += HandleUICancel;
            }

            CloseInventory();
            CloseWeaponPickup(false);
        }

        private void OnDisable()
        {
            if (autoBindToPlayerLifecycle && PlayerLifecycle.Instance != null)
                PlayerLifecycle.Instance.PlayerSpawned -= HandlePlayerSpawned;

            var input = GameInputManager.Instance;
            if (input != null)
            {
                input.OnInventoryToggle -= HandleInventoryToggle;
                input.OnInteract -= HandleInteract;
                input.OnUICancel -= HandleUICancel;
            }

            if (isInventoryOpen && GameInputManager.Instance != null)
                GameInputManager.Instance.SetGameplayInputEnabled(true);

            if (isWeaponPickupOpen && GameInputManager.Instance != null)
                GameInputManager.Instance.SwitchToPlayerActionMap();

            Unbind();
        }

        private void OnDestroy()
        {
            if (isInventoryOpen && GameInputManager.Instance != null)
                GameInputManager.Instance.SetGameplayInputEnabled(true);

            if (isWeaponPickupOpen && GameInputManager.Instance != null)
                GameInputManager.Instance.SwitchToPlayerActionMap();

            Unbind();
        }

        // -----------------------------------------------------------------------

        private void HandlePlayerSpawned(PlayerCharacter newPlayer) => BindToPlayer(newPlayer);

        private void HandlePlayerDeath()
        {
            if (isInventoryOpen) CloseInventory();
            if (isWeaponPickupOpen) CloseWeaponPickup(false);
            if (hideOnDeath) SetVisible(false);
        }

        // -----------------------------------------------------------------------
        // INTERACT
        // -----------------------------------------------------------------------

        private void HandleInteract()
        {
            if (isWeaponPickupOpen || isInventoryOpen) return;
            if (player == null || _weaponManager == null) return;

            var interactable = WeaponPickupInteractable.Current;
            if (interactable == null || interactable.WeaponPickup == null) return;

            activeInteractable = interactable;
            OpenWeaponPickup(interactable.WeaponPickup);
        }

        // -----------------------------------------------------------------------
        // INVENTORY
        // -----------------------------------------------------------------------

        private void HandleInventoryToggle()
        {
            if (isWeaponPickupOpen) return;

            if (isInventoryOpen) CloseInventory();
            else OpenInventory();
        }

        private void HandleUICancel()
        {
            if (isWeaponPickupOpen) return;
            if (!isInventoryOpen) return;
            CloseInventory();
        }

        public void OpenInventory()
        {
            if (isInventoryOpen) return;

            isInventoryOpen = true;

            if (inventoryPanel != null)
                inventoryPanel.SetActive(true);

            if (inventoryUI != null && player != null)
                inventoryUI.Bind(inventory, _weaponManager);

            if (GameInputManager.Instance != null)
            {
                GameInputManager.Instance.SetGameplayInputEnabled(false);
                GameInputManager.Instance.SwitchToUIActionMap();
            }
        }

        public void CloseInventory()
        {
            if (!isInventoryOpen && inventoryPanel != null && !inventoryPanel.activeSelf)
            {
                inventoryPanel.SetActive(false);
                return;
            }

            isInventoryOpen = false;

            if (inventoryUI != null) inventoryUI.Unbind();
            if (inventoryPanel != null) inventoryPanel.SetActive(false);

            if (GameInputManager.Instance != null)
            {
                GameInputManager.Instance.SetGameplayInputEnabled(true);
                GameInputManager.Instance.SwitchToPlayerActionMap();
            }
        }

        // -----------------------------------------------------------------------
        // WEAPON PICKUP
        // -----------------------------------------------------------------------

        private void OpenWeaponPickup(WorldWeaponPickup pickup)
        {
            if (isWeaponPickupOpen) return;
            isWeaponPickupOpen = true;

            if (weaponPickupPanel != null)
                weaponPickupPanel.SetActive(true);

            if (weaponPickupUI != null)
            {
                weaponPickupUI.Bind(_weaponManager, pickup);
                weaponPickupUI.OnClosed += HandleWeaponPickupClosed;
            }

            if (GameInputManager.Instance != null)
                GameInputManager.Instance.SwitchToUIActionMap();
        }

        private void HandleWeaponPickupClosed(bool pickedUp)
        {
            CloseWeaponPickup(!pickedUp);
        }

        private void CloseWeaponPickup(bool reEnablePrompt)
        {
            if (!isWeaponPickupOpen && weaponPickupPanel != null && !weaponPickupPanel.activeSelf)
            {
                weaponPickupPanel.SetActive(false);
                return;
            }

            isWeaponPickupOpen = false;

            if (weaponPickupUI != null)
            {
                weaponPickupUI.OnClosed -= HandleWeaponPickupClosed;
                weaponPickupUI.Unbind();
            }

            if (weaponPickupPanel != null)
                weaponPickupPanel.SetActive(false);

            if (GameInputManager.Instance != null)
                GameInputManager.Instance.SwitchToPlayerActionMap();

            if (reEnablePrompt && activeInteractable != null)
                activeInteractable.ReEnablePrompt();

            activeInteractable = null;
        }

        // -----------------------------------------------------------------------

        private void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }
    }
}
