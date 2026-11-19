using UnityEngine;
using TMPro;
using UnityEngine.Serialization;
using UnityEngine.UI;

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

        [Header("Clean Screen")]
        [Tooltip("Hides the HUD elements below while playing (capture/demo builds). The I-menu still opens.")]
        [SerializeField] private bool hideGameplayHud;
        [Tooltip("HUD children hidden by Hide Gameplay Hud: vitals, mod bar, objective tracker.")]
        [SerializeField] private GameObject[] gameplayHudElements;

        [Header("Gameplay HUD Presentation")]
        [SerializeField] private TMP_FontAsset bodyFont;

        private static readonly Color Frame = new(0.032f, 0.045f, 0.087f, 0.97f);
        private static readonly Color Well = new(0.025f, 0.035f, 0.07f, 0.9f);
        private static readonly Color Cyan = new(0.24f, 0.91f, 0.98f, 1f);
        private static readonly Color Magenta = new(0.86f, 0.25f, 0.89f, 1f);
        private static readonly Color Paper = new(0.9f, 0.94f, 1f, 1f);
        private static readonly Color Muted = new(0.51f, 0.61f, 0.74f, 1f);
        private static readonly Color Line = new(0.18f, 0.27f, 0.38f, 0.75f);

        [Header("Inventory Screen (UI Toolkit)")]
        [Tooltip("The Junklite inventory screen (Prefabs/UI/Inventory Screen), nested inactive in this HUD. " +
                 "When set, the Inventory input opens it instead of the legacy panel below.")]
        [SerializeField] private InventoryScreenBridge inventoryScreen;

        [Header("Legacy Inventory Panel (still hosts the UPGRADES tab)")]
        [SerializeField] private GameObject inventoryPanel;
        [SerializeField] private InventoryUI inventoryUI;

        // Runtime
        private PlayerCharacter player;
        private AttributeManager attributes;
        private WeaponManager _weaponManager;
        private ModManager _modManager;
        private InventoryComponent inventory;
        private bool isInventoryOpen;
        private PlayerLifecycle subscribedPlayerLifecycle;
        private PlayerLevelUI levelUI;

        // -----------------------------------------------------------------------

        public bool IsInventoryOpen => isInventoryOpen;

        public void HighlightUnlockedModSlots(int first, int count, Color color) =>
            modCombatUI?.HighlightUnlockedSlots(first, count, color);

        // -----------------------------------------------------------------------

        private void Awake()
        {
            if (bodyFont == null)
                bodyFont = UIFonts.HudBody;

            BuildVitalsInterface();
        }

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
                var armorAttr = player.RuntimeStats?.ArmorDisplay ?? attributes.Get(AttributeType.Armor);
                if (armorAttr != null)
                {
                    armorBar.gameObject.SetActive(true);
                    armorBar.Bind(armorAttr);
                }
                else
                    armorBar.gameObject.SetActive(false);
            }

            attributes.OnDeath += HandlePlayerDeath;
            levelUI?.Bind(player);

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
            levelUI?.Unbind();
            if (attributes != null)
                attributes.OnDeath -= HandlePlayerDeath;

            if (healthBar != null) healthBar.Unbind();
            if (healthIconFeedback != null) healthIconFeedback.Unbind();
            if (armorBar != null) armorBar.Unbind();
            if (modCombatUI != null) modCombatUI.Unbind();

            player = null;
            attributes = null;
            _weaponManager = null;
            _modManager = null;
            inventory = null;
        }

        // -----------------------------------------------------------------------

        private void OnEnable()
        {
            RebindPlayerLifecycle();

            var input = GameInputManager.Instance;
            if (input != null)
            {
                input.OnInventoryToggle += HandleInventoryToggle;
                input.OnUICancel += HandleUICancel;
            }

            CloseInventory();
            ApplyGameplayHudVisibility();
        }

        private void ApplyGameplayHudVisibility()
        {
            if (gameplayHudElements == null) return;
            foreach (GameObject element in gameplayHudElements)
                if (element != null) element.SetActive(!hideGameplayHud);
        }

        private void Start()
        {
            // Covers scene-authored HUDs that enabled before the persistent root awoke.
            RebindPlayerLifecycle();
        }

        private void OnDisable()
        {
            UnsubscribeFromPlayerLifecycle();

            var input = GameInputManager.Instance;
            if (input != null)
            {
                input.OnInventoryToggle -= HandleInventoryToggle;
                input.OnUICancel -= HandleUICancel;
            }

            if (isInventoryOpen && GameInputManager.Instance != null)
                GameInputManager.Instance.SetGameplayInputEnabled(true);

            Unbind();
        }

        private void OnDestroy()
        {
            UnsubscribeFromPlayerLifecycle();

            if (isInventoryOpen && GameInputManager.Instance != null)
                GameInputManager.Instance.SetGameplayInputEnabled(true);

            Unbind();
        }

        // -----------------------------------------------------------------------

        private void RebindPlayerLifecycle()
        {
            PlayerLifecycle lifecycle = autoBindToPlayerLifecycle
                ? PlayerLifecycle.Instance
                : null;

            if (subscribedPlayerLifecycle == lifecycle)
                return;

            UnsubscribeFromPlayerLifecycle();
            subscribedPlayerLifecycle = lifecycle;

            if (subscribedPlayerLifecycle == null)
                return;

            subscribedPlayerLifecycle.PlayerSpawned += HandlePlayerSpawned;

            if (subscribedPlayerLifecycle.Player != null)
                BindToPlayer(subscribedPlayerLifecycle.Player);
        }

        private void UnsubscribeFromPlayerLifecycle()
        {
            if (subscribedPlayerLifecycle == null)
                return;

            subscribedPlayerLifecycle.PlayerSpawned -= HandlePlayerSpawned;
            subscribedPlayerLifecycle = null;
        }

        private void HandlePlayerSpawned(PlayerCharacter newPlayer) => BindToPlayer(newPlayer);

        private void HandlePlayerDeath()
        {
            if (isInventoryOpen) CloseInventory();
            if (hideOnDeath) SetVisible(false);
        }

        // -----------------------------------------------------------------------
        // INVENTORY
        // -----------------------------------------------------------------------

        private void HandleInventoryToggle()
        {
            if (isInventoryOpen) CloseInventory();
            else OpenInventory();
        }

        private void HandleUICancel()
        {
            if (!isInventoryOpen) return;
            CloseInventoryOrBack();
        }

        /// <summary>
        /// Back / Esc while the inventory is open: the screen gets first use of the press (drop a picked-up
        /// mod, leave Missions), otherwise the inventory closes. Returns true if the press was used.
        /// </summary>
        public bool CloseInventoryOrBack()
        {
            if (!isInventoryOpen) return false;
            if (inventoryScreen != null && inventoryScreen.HandleBack()) return true;
            CloseInventory();
            return true;
        }

        public void OpenInventory() => OpenInventory(useLegacyPanel: inventoryScreen == null);

        private void OpenInventory(bool useLegacyPanel)
        {
            if (isInventoryOpen) return;

            isInventoryOpen = true;

            if (useLegacyPanel)
            {
                if (inventoryPanel != null)
                    inventoryPanel.SetActive(true);

                if (inventoryUI != null && player != null)
                    inventoryUI.Bind(inventory, _weaponManager);
            }
            else if (player != null)
            {
                inventoryScreen.Open(player);
            }

            if (GameInputManager.Instance != null)
            {
                GameInputManager.Instance.SetGameplayInputEnabled(false);
                GameInputManager.Instance.SwitchToUIActionMap();
            }
        }

        /// <summary>
        /// Opens the inventory on its UPGRADES tab (level-up toast, scripted level beats, dev tools).
        /// Uses the UI Toolkit screen's Upgrades tab; the legacy menu only when no screen is assigned.
        /// </summary>
        public void OpenUpgrades()
        {
            if (inventoryScreen != null)
            {
                // A legacy panel left open would keep isInventoryOpen set; close it first.
                if (isInventoryOpen && !inventoryScreen.IsOpen) CloseInventory();
                OpenInventory(useLegacyPanel: false);
                inventoryScreen.ShowTab(Junklite.Inventory.InventoryTab.Upgrades);
                return;
            }

            if (isInventoryOpen && inventoryPanel != null && !inventoryPanel.activeSelf)
                CloseInventory();

            OpenInventory(useLegacyPanel: true);
            if (inventoryUI != null) inventoryUI.OpenUpgradesTab();
        }

        public void CloseInventory()
        {
            if (!isInventoryOpen && inventoryScreen != null && inventoryScreen.IsOpen)
                inventoryScreen.Close();

            if (!isInventoryOpen && inventoryPanel != null && !inventoryPanel.activeSelf)
                return;

            isInventoryOpen = false;

            if (inventoryScreen != null && inventoryScreen.IsOpen) inventoryScreen.Close();
            if (inventoryUI != null) inventoryUI.Unbind();
            if (inventoryPanel != null) inventoryPanel.SetActive(false);

            if (GameInputManager.Instance != null)
            {
                GameInputManager.Instance.SetGameplayInputEnabled(true);
                GameInputManager.Instance.SwitchToPlayerActionMap();
            }
        }

        // -----------------------------------------------------------------------

        private void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
                gameObject.SetActive(visible);
        }

        private void BuildVitalsInterface()
        {
            if (healthIconFeedback == null)
                return;

            RectTransform root = healthIconFeedback.transform as RectTransform;
            if (root == null)
                return;

            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = Vector2.zero;
            root.anchoredPosition = new Vector2(34f, 34f);
            root.sizeDelta = new Vector2(340f, 112f);
            root.localScale = Vector3.one;

            Image legacyBackground = root.GetComponent<Image>();
            if (legacyBackground != null)
            {
                legacyBackground.enabled = false;
                legacyBackground.raycastTarget = false;
            }

            Image frame = CreateImage("Vitals Frame", root, Frame);
            Stretch(frame.rectTransform);
            frame.transform.SetAsFirstSibling();
            frame.raycastTarget = false;
            AddBorder(frame.rectTransform, Line);
            RectImage("Vitals Accent", frame.transform, Magenta, 0f, 0f, 3f, 112f);
            RectImage("Vitals Signal", frame.transform, Cyan, 3f, 0f, 96f, 3f);

            Image iconWell = RectImage("Health Icon Well", root, Well, 16f, 22f, 66f, 66f);
            iconWell.transform.SetSiblingIndex(1);
            AddBorder(iconWell.rectTransform, new Color(Magenta.r, Magenta.g, Magenta.b, 0.3f));

            Image healthIcon = null;
            Image[] images = healthIconFeedback.GetComponentsInChildren<Image>(true);
            foreach (Image image in images)
            {
                if (image != legacyBackground && image != frame && image != iconWell &&
                    image.transform.IsChildOf(root) && image.sprite != null)
                {
                    healthIcon = image;
                    break;
                }
            }

            if (healthIcon != null)
            {
                Place(healthIcon.rectTransform, 23f, 29f, 52f, 52f);
                healthIcon.preserveAspect = true;
                healthIcon.raycastTarget = false;
                healthIcon.transform.SetAsLastSibling();
            }

            levelUI = PlayerLevelUI.Create(iconWell.rectTransform, (RectTransform)transform);

            RectTransform healthRow = CreateRect("Health", root);
            Place(healthRow, 98f, 18f, 220f, 42f);
            TextAt("Health Label", healthRow, "HEALTH", 13f, Cyan,
                0f, 0f, 100f, 20f, FontStyles.Bold);
            TMP_Text healthValue = TextAt("Health Value", healthRow, "-- / --", 13f, Paper,
                102f, 0f, 118f, 20f, FontStyles.Bold);
            healthValue.horizontalAlignment = HorizontalAlignmentOptions.Right;
            Image healthTrack = RectImage("Health Track", healthRow, Line, 0f, 27f, 220f, 7f);
            Image healthFill = CreateFill(healthTrack.transform, Cyan);
            healthBar = healthRow.gameObject.AddComponent<StatBarUI>();
            healthBar.Configure(healthFill, healthValue);

            RectTransform armorRow = CreateRect("Armor", root);
            Place(armorRow, 98f, 69f, 220f, 25f);
            TextAt("Armor Label", armorRow, "ARMOR", 11f, Muted,
                0f, 0f, 55f, 18f, FontStyles.Bold);
            Image armorTrack = RectImage("Armor Track", armorRow, Line, 61f, 7f, 91f, 5f);
            Image armorFill = CreateFill(armorTrack.transform, Magenta);
            TMP_Text armorValue = TextAt("Armor Value", armorRow, "-- / --", 11f, Muted,
                158f, 0f, 62f, 18f, FontStyles.Bold);
            armorValue.horizontalAlignment = HorizontalAlignmentOptions.Right;
            armorBar = armorRow.gameObject.AddComponent<StatBarUI>();
            armorBar.Configure(armorFill, armorValue);
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            Image image = CreateRect(name, parent).gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static Image RectImage(
            string name, Transform parent, Color color,
            float x, float y, float width, float height)
        {
            Image image = CreateImage(name, parent, color);
            Place(image.rectTransform, x, y, width, height);
            image.raycastTarget = false;
            return image;
        }

        private static Image CreateFill(Transform parent, Color color)
        {
            Image fill = CreateImage("Fill", parent, color);
            Stretch(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.raycastTarget = false;
            return fill;
        }

        private TMP_Text TextAt(
            string name, Transform parent, string value, float size, Color color,
            float x, float y, float width, float height,
            FontStyles style = FontStyles.Normal)
        {
            TextMeshProUGUI text = CreateRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = bodyFont;
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.verticalAlignment = VerticalAlignmentOptions.Middle;
            Place(text.rectTransform, x, y, width, height);
            return text;
        }

        private static void AddBorder(RectTransform parent, Color color)
        {
            Image top = CreateImage("Top Border", parent, color);
            Stretch(top.rectTransform);
            top.rectTransform.anchorMin = new Vector2(0f, 1f);
            top.rectTransform.sizeDelta = new Vector2(0f, 1f);

            Image bottom = CreateImage("Bottom Border", parent, color);
            Stretch(bottom.rectTransform);
            bottom.rectTransform.anchorMax = new Vector2(1f, 0f);
            bottom.rectTransform.sizeDelta = new Vector2(0f, 1f);

            Image left = CreateImage("Left Border", parent, color);
            Stretch(left.rectTransform);
            left.rectTransform.anchorMax = new Vector2(0f, 1f);
            left.rectTransform.sizeDelta = new Vector2(1f, 0f);

            Image right = CreateImage("Right Border", parent, color);
            Stretch(right.rectTransform);
            right.rectTransform.anchorMin = new Vector2(1f, 0f);
            right.rectTransform.sizeDelta = new Vector2(1f, 0f);

            foreach (Image edge in new[] { top, bottom, left, right })
                edge.raycastTarget = false;
        }

        private static void Place(
            RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
