using UnityEngine;
using UnityEngine.EventSystems;
using System;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;

namespace junklite
{
    public class InventoryUI : MonoBehaviour
    {
        #region Fields
        [Header("Tabs")]
        [SerializeField] private MenuButton inventoryTabButton;
        [SerializeField] private MenuButton infoTabButton;
        [SerializeField] private MenuButton missionsTabButton;

        [Header("Tab Screens")]
        [SerializeField] private GameObject inventoryScreen;
        [SerializeField] private GameObject infoScreen;
        [SerializeField] private GameObject missionsScreen;

        [Header("Inventory Slots")]
        [SerializeField] private GameObject inventorySlotPrefab;
        [SerializeField] private Transform inventorySlotParent;

        [Header("Active Mod Slots")]
        [SerializeField] private GameObject activeModSlotPrefab;
        [SerializeField] private Transform activeModSlotParent;

        [Header("Passive Mod Slots")]
        [SerializeField] private GameObject passiveModSlotPrefab;
        [SerializeField] private Transform passiveModSlotParent;

        [Header("Weapon Slots")]
        [SerializeField] private InventoryWeaponSlotUI weaponSlot1;
        [SerializeField] private InventoryWeaponSlotUI weaponSlot2;

        [Header("Description Box")]
        [SerializeField] private ItemDescriptionUI descriptionUI;

        [Header("Presentation")]
        [SerializeField] private GameObject panel;
        [SerializeField] private RectTransform characterVisual;
        [SerializeField] private TMP_FontAsset headingFont;
        [SerializeField] private TMP_FontAsset bodyFont;
        [SerializeField] private Font headingSourceFont;

        private static readonly Color Backdrop = new(0.012f, 0.018f, 0.042f, 0.78f);
        private static readonly Color Frame = new(0.032f, 0.045f, 0.087f, 0.985f);
        private static readonly Color Card = new(0.055f, 0.073f, 0.125f, 0.96f);
        private static readonly Color Well = new(0.025f, 0.035f, 0.07f, 0.9f);
        private static readonly Color Hover = new(0.075f, 0.18f, 0.24f, 1f);
        private static readonly Color Cyan = new(0.24f, 0.91f, 0.98f, 1f);
        private static readonly Color Magenta = new(0.86f, 0.25f, 0.89f, 1f);
        private static readonly Color Paper = new(0.9f, 0.94f, 1f, 1f);
        private static readonly Color Muted = new(0.51f, 0.61f, 0.74f, 1f);
        private static readonly Color Line = new(0.18f, 0.27f, 0.38f, 0.75f);
        private static readonly Vector2 DesignSize = new(1440f, 820f);

        private InventoryComponent inventory;
        private WeaponManager weaponManager;
        private PlayerWeaponLoadout weaponLoadout;
        private ModManager modManager;

        private readonly List<ModSlotUI> inventorySlots = new();
        private readonly List<ModSlotUI> activeModSlots = new();
        private readonly List<ModSlotUI> passiveModSlots = new();

        private enum Tab { Inventory, Info, Missions }
        private Tab activeTab = Tab.Inventory;
        private const float NavigateDeadZone = 0.45f;
        private const float NavigateRepeatDelay = 0.16f;
        private float nextNavigateTime;
        private bool themedInterfaceBuilt;
        private RectTransform frameTransform;
        private TMP_FontAsset generatedHeadingFont;

        #endregion


        #region Unity

        private void Awake()
        {
            if (headingSourceFont != null)
            {
                generatedHeadingFont = TMP_FontAsset.CreateFontAsset(headingSourceFont);
                headingFont = generatedHeadingFont;
            }

            if (bodyFont == null)
                bodyFont = TMP_Settings.defaultFontAsset;
            if (headingFont == null)
                headingFont = bodyFont;

            BuildInterface();
        }

        private void LateUpdate()
        {
            if (panel != null && panel.activeInHierarchy)
                FitFrame();
        }

        #endregion


        #region Bind / Unbind

        public void Bind(InventoryComponent inv, WeaponManager wm)
        {
            Unbind();

            inventory = inv;
            weaponManager = wm;
            weaponLoadout = wm != null ? wm.Loadout : null;
            modManager = wm != null ? wm.GetComponent<ModManager>() : null;

            if (inventory != null)
                inventory.OnInventoryChanged += RefreshInventory;

            if (weaponLoadout != null)
                weaponLoadout.WeaponChanged += RefreshWeapons;

            if (modManager != null)
                modManager.OnModSlotsChanged += RefreshModSlots;

            ModSlotUI.OnModHovered += HandleModHovered;
            ModSlotUI.OnModHoverExit += HandleHoverExit;
            ModSlotUI.OnModSelected += HandleModSelected;
            InventoryWeaponSlotUI.OnWeaponHovered += HandleWeaponHovered;
            InventoryWeaponSlotUI.OnWeaponHoverExit += HandleHoverExit;

            if (GameInputManager.Instance != null)
                GameInputManager.Instance.OnUINavigate += HandleUINavigate;

            if (inventoryTabButton != null)
                inventoryTabButton.OnClick += ShowInventoryTab;

            if (infoTabButton != null)
                infoTabButton.OnClick += ShowInfoTab;

            if (missionsTabButton != null)
                missionsTabButton.OnClick += ShowMissionsTab;

            ShowInventoryTab();
            RefreshAll();
        }

        public void Unbind()
        {
            if (inventory != null)
                inventory.OnInventoryChanged -= RefreshInventory;

            if (weaponLoadout != null)
                weaponLoadout.WeaponChanged -= RefreshWeapons;

            if (modManager != null)
                modManager.OnModSlotsChanged -= RefreshModSlots;

            ModSlotUI.OnModHovered -= HandleModHovered;
            ModSlotUI.OnModHoverExit -= HandleHoverExit;
            ModSlotUI.OnModSelected -= HandleModSelected;
            InventoryWeaponSlotUI.OnWeaponHovered -= HandleWeaponHovered;
            InventoryWeaponSlotUI.OnWeaponHoverExit -= HandleHoverExit;

            if (GameInputManager.Instance != null)
                GameInputManager.Instance.OnUINavigate -= HandleUINavigate;

            if (inventoryTabButton != null)
                inventoryTabButton.OnClick -= ShowInventoryTab;

            if (infoTabButton != null)
                infoTabButton.OnClick -= ShowInfoTab;

            if (missionsTabButton != null)
                missionsTabButton.OnClick -= ShowMissionsTab;

            ClearSlots(inventorySlots);
            ClearSlots(activeModSlots);
            ClearSlots(passiveModSlots);
            ClearWeaponSlots();

            descriptionUI?.Clear();

            inventory = null;
            weaponManager = null;
            weaponLoadout = null;
            modManager = null;
        }

        public void RefreshAll()
        {
            RefreshInventory();
            RefreshWeapons();
            RefreshModSlots();
        }

        #endregion


        #region Tabs

        private void ShowInventoryTab()
        {
            activeTab = Tab.Inventory;

            inventoryTabButton?.SetSelected(true);
            infoTabButton?.SetSelected(false);
            missionsTabButton?.SetSelected(false);

            if (inventoryScreen != null) inventoryScreen.SetActive(true);
            if (infoScreen != null) infoScreen.SetActive(false);
            if (missionsScreen != null) missionsScreen.SetActive(false);

            descriptionUI?.Clear();
            TrySelectDefaultSlotIfGamepad();
        }

        private void ShowInfoTab()
        {
            activeTab = Tab.Info;

            inventoryTabButton?.SetSelected(false);
            infoTabButton?.SetSelected(true);
            missionsTabButton?.SetSelected(false);

            if (inventoryScreen != null) inventoryScreen.SetActive(false);
            if (infoScreen != null) infoScreen.SetActive(true);
            if (missionsScreen != null) missionsScreen.SetActive(false);

            descriptionUI?.Clear();
        }

        private void ShowMissionsTab()
        {
            activeTab = Tab.Missions;

            inventoryTabButton?.SetSelected(false);
            infoTabButton?.SetSelected(false);
            missionsTabButton?.SetSelected(true);

            if (inventoryScreen != null) inventoryScreen.SetActive(false);
            if (infoScreen != null) infoScreen.SetActive(false);
            if (missionsScreen != null) missionsScreen.SetActive(true);

            descriptionUI?.Clear();
        }

        #endregion


        #region Description Box Handlers

        private void HandleModHovered(ModInstance mod)
        {
            if (mod == null) descriptionUI?.Clear();
            else descriptionUI?.ShowMod(mod);
        }

        private void HandleWeaponHovered(WeaponInstance weapon)
        {
            if (weapon == null) descriptionUI?.Clear();
            else descriptionUI?.ShowWeapon(weapon);
        }

        private void HandleHoverExit()
        {
            descriptionUI?.Clear();
        }

        private void HandleModSelected(ModInstance selectedMod)
        {
            if (selectedMod == null) return;
            if (EventSystem.current == null) return;

            var preferredTarget = GetPreferredTargetSlotFor(selectedMod);
            if (preferredTarget == null) return;

            EventSystem.current.SetSelectedGameObject(preferredTarget.gameObject);
        }

        private void HandleUINavigate(Vector2 move)
        {
            if (activeTab != Tab.Inventory) return;
            if (EventSystem.current == null) return;
            if (move.sqrMagnitude < NavigateDeadZone * NavigateDeadZone) return;
            if (Time.unscaledTime < nextNavigateTime) return;

            nextNavigateTime = Time.unscaledTime + NavigateRepeatDelay;

            if (TryMoveSelection(move.normalized))
                return;

            TrySelectDefaultSlotIfGamepad();
        }

        #endregion


        #region Inventory Slots

        private void RefreshInventory()
        {
            ClearSlots(inventorySlots);

            if (inventory == null || inventorySlotParent == null ||
                (!themedInterfaceBuilt && inventorySlotPrefab == null)) return;

            for (int i = 0; i < inventory.SlotCount; i++)
            {
                ModInstance mod = inventory.GetModAt(i);

                ModSlotUI slot = themedInterfaceBuilt
                    ? CreateThemedModSlot(inventorySlotParent, Cyan)
                    : Instantiate(inventorySlotPrefab, inventorySlotParent).GetComponent<ModSlotUI>();

                if (slot != null)
                {
                    slot.Bind(mod, inventory, i);
                    ConfigureSlotNavigation(slot);
                    inventorySlots.Add(slot);
                }
            }

            TrySelectDefaultSlotIfGamepad();
        }

        #endregion


        #region Weapon Slots

        private void RefreshWeapons()
        {
            if (weaponManager == null || weaponLoadout == null) return;

            weaponSlot1?.Bind(weaponManager, 1);
            weaponSlot2?.Bind(weaponManager, 2);
        }

        private void ClearWeaponSlots()
        {
            weaponSlot1?.Unbind();
            weaponSlot2?.Unbind();
        }

        #endregion


        #region Mod Slots

        private void RefreshModSlots()
        {
            ClearSlots(activeModSlots);
            ClearSlots(passiveModSlots);

            if (modManager == null) return;

            if (activeModSlotParent != null && (themedInterfaceBuilt || activeModSlotPrefab != null))
            {
                for (int i = 0; i < modManager.MaxActiveSlots; i++)
                {
                    bool locked = i >= modManager.UnlockedActiveSlots;
                    ModSlotUI slot = themedInterfaceBuilt
                        ? CreateThemedModSlot(activeModSlotParent, Cyan)
                        : Instantiate(activeModSlotPrefab, activeModSlotParent).GetComponent<ModSlotUI>();
                    if (slot != null)
                    {
                        slot.Bind(modManager.GetActiveMod(i), modManager, inventory, i, true, locked);
                        ConfigureSlotNavigation(slot);
                        activeModSlots.Add(slot);
                    }
                }
            }

            if (passiveModSlotParent != null && (themedInterfaceBuilt || passiveModSlotPrefab != null))
            {
                for (int i = 0; i < modManager.MaxPassiveSlots; i++)
                {
                    bool locked = i >= modManager.UnlockedPassiveSlots;
                    ModSlotUI slot = themedInterfaceBuilt
                        ? CreateThemedModSlot(passiveModSlotParent, Magenta)
                        : Instantiate(passiveModSlotPrefab, passiveModSlotParent).GetComponent<ModSlotUI>();
                    if (slot != null)
                    {
                        slot.Bind(modManager.GetPassiveMod(i), modManager, inventory, i, false, locked);
                        ConfigureSlotNavigation(slot);
                        passiveModSlots.Add(slot);
                    }
                }
            }

            TrySelectDefaultSlotIfGamepad();
        }

        #endregion


        #region Presentation

        private void BuildInterface()
        {
            if (characterVisual == null && inventoryScreen != null)
            {
                RectTransform[] candidates = inventoryScreen.GetComponentsInChildren<RectTransform>(true);
                foreach (RectTransform candidate in candidates)
                {
                    if (candidate.name == "CHARACTER")
                    {
                        characterVisual = candidate;
                        break;
                    }
                }
            }

            if (characterVisual != null)
                characterVisual.SetParent(transform, false);

            if (panel == null && inventoryScreen != null && inventoryScreen.transform.parent != null)
                panel = inventoryScreen.transform.parent.gameObject;
            if (panel == null && transform.childCount > 0)
                panel = transform.GetChild(0).gameObject;
            if (panel == null)
                panel = CreateRect("Inventory Panel", transform).gameObject;

            RectTransform panelRect = (RectTransform)panel.transform;
            Stretch(panelRect);
            panelRect.localScale = Vector3.one;

            for (int i = panel.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = panel.transform.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            Image backdrop = panel.GetComponent<Image>();
            if (backdrop == null)
                backdrop = panel.AddComponent<Image>();
            backdrop.color = Backdrop;
            backdrop.sprite = null;
            backdrop.raycastTarget = true;

            Image frame = CreateImage("Inventory Interface", panel.transform, Frame);
            frameTransform = frame.rectTransform;
            frameTransform.anchorMin = frameTransform.anchorMax = new Vector2(0.5f, 0.5f);
            frameTransform.pivot = new Vector2(0.5f, 0.5f);
            frameTransform.anchoredPosition = Vector2.zero;
            frameTransform.sizeDelta = DesignSize;
            frame.raycastTarget = false;
            AddBorder(frameTransform, Line);

            for (int i = 1; i < 12; i++)
                RectImage("Grid Line", frameTransform,
                    new Color(Cyan.r, Cyan.g, Cyan.b, 0.022f),
                    i * 120f, 0f, 1f, DesignSize.y);

            RectImage("Cyan Edge", frameTransform, Cyan, 0f, 0f, 390f, 3f);
            RectImage("Magenta Edge", frameTransform, Magenta, 1190f, 817f, 250f, 3f);
            RectImage("Left Mark", frameTransform, Cyan, 0f, 0f, 3f, 28f);
            RectImage("Right Mark", frameTransform, Magenta, 1437f, 792f, 3f, 28f);

            BuildHeader();
            BuildPages();
            BuildFooter();
            themedInterfaceBuilt = true;
            FitFrame();
        }

        private void BuildHeader()
        {
            TextAt("Brand", frameTransform, "JUNKLITE  /  STORAGE LINK",
                16f, Cyan, 40f, 24f, 620f, 24f, false, FontStyles.Bold);
            TextAt("Title", frameTransform, "INVENTORY",
                48f, Paper, 38f, 52f, 650f, 64f, true, FontStyles.Bold);

            inventoryTabButton = CreateTab("INVENTORY", 914f, 76f, 148f);
            infoTabButton = CreateTab("CODEX", 1074f, 76f, 130f);
            missionsTabButton = CreateTab("MISSIONS", 1216f, 76f, 174f);

            RectImage("Header Rule", frameTransform, Line, 40f, 146f, 1360f, 1f);
            RectImage("Header Signal", frameTransform, Magenta, 40f, 146f, 96f, 3f);
        }

        private MenuButton CreateTab(string label, float x, float y, float width)
        {
            Image background = RectImage(label, frameTransform, Card, x, y, width, 46f);
            background.raycastTarget = true;
            AddBorder(background.rectTransform, Line);
            TMP_Text text = TextAt("Label", background.transform, label,
                14f, Paper, 12f, 0f, width - 24f, 46f, false, FontStyles.Bold);
            text.horizontalAlignment = HorizontalAlignmentOptions.Center;

            MenuButton button = background.gameObject.AddComponent<MenuButton>();
            button.Configure(text, background, Card, Paper,
                new Color(0.08f, 0.21f, 0.29f, 1f), Cyan,
                Hover, new Color(0.12f, 0.29f, 0.36f, 1f), 14f, 14f);
            return button;
        }

        private void BuildPages()
        {
            inventoryScreen = CreateRect("Inventory Page", frameTransform).gameObject;
            Place((RectTransform)inventoryScreen.transform, 40f, 170f, 1360f, 568f);
            BuildInventoryPage((RectTransform)inventoryScreen.transform);

            infoScreen = CreateRect("Codex Page", frameTransform).gameObject;
            Place((RectTransform)infoScreen.transform, 40f, 170f, 1360f, 568f);
            BuildEmptyPage((RectTransform)infoScreen.transform, "CODEX", "NO CODEX DATA");

            missionsScreen = CreateRect("Missions Page", frameTransform).gameObject;
            Place((RectTransform)missionsScreen.transform, 40f, 170f, 1360f, 568f);
            BuildEmptyPage((RectTransform)missionsScreen.transform, "MISSIONS", "NO ACTIVE MISSIONS");

            infoScreen.SetActive(false);
            missionsScreen.SetActive(false);
        }

        private void BuildInventoryPage(RectTransform page)
        {
            Image loadoutCard = RectImage("Loadout", page, Card, 0f, 0f, 320f, 568f);
            AddBorder(loadoutCard.rectTransform, Line);
            RectImage("Loadout Accent", loadoutCard.transform, Magenta, 0f, 0f, 3f, 568f);
            TextAt("Loadout Label", loadoutCard.transform, "LOADOUT", 14f, Magenta,
                16f, 12f, 180f, 24f, false, FontStyles.Bold);

            if (characterVisual != null)
            {
                characterVisual.SetParent(loadoutCard.transform, false);
                Place(characterVisual, 126f, 50f, 178f, 494f);
                characterVisual.localScale = Vector3.one;
                characterVisual.gameObject.SetActive(true);
                Image characterImage = characterVisual.GetComponent<Image>();
                if (characterImage != null)
                {
                    characterImage.preserveAspect = true;
                    characterImage.raycastTarget = false;
                }
            }

            weaponSlot1 = CreateWeaponSlot(loadoutCard.transform, "SLOT 01", 16f, 62f, Cyan);
            weaponSlot2 = CreateWeaponSlot(loadoutCard.transform, "SLOT 02", 16f, 246f, Magenta);

            Image equippedCard = RectImage("Equipped Mods", page, Card, 338f, 0f, 300f, 568f);
            AddBorder(equippedCard.rectTransform, Line);
            RectImage("Equipped Accent", equippedCard.transform, Cyan, 0f, 0f, 3f, 568f);
            TextAt("Equipped Label", equippedCard.transform, "EQUIPPED MODS", 14f, Cyan,
                16f, 12f, 220f, 24f, false, FontStyles.Bold);

            TextAt("Active Label", equippedCard.transform, "ACTIVE", 12f, Paper,
                16f, 52f, 120f, 20f, false, FontStyles.Bold);
            activeModSlotParent = CreateRect("Active Mods", equippedCard.transform);
            Place((RectTransform)activeModSlotParent, 16f, 80f, 268f, 188f);
            ConfigureGrid(activeModSlotParent, 76f, 76f, 13f, 13f, 3);

            RectImage("Mod Divider", equippedCard.transform, Line, 16f, 284f, 268f, 1f);
            TextAt("Passive Label", equippedCard.transform, "PASSIVE", 12f, Paper,
                16f, 302f, 120f, 20f, false, FontStyles.Bold);
            passiveModSlotParent = CreateRect("Passive Mods", equippedCard.transform);
            Place((RectTransform)passiveModSlotParent, 16f, 330f, 268f, 188f);
            ConfigureGrid(passiveModSlotParent, 76f, 76f, 13f, 13f, 3);

            Image inventoryCard = RectImage("Stored Mods", page, Card, 656f, 0f, 360f, 568f);
            AddBorder(inventoryCard.rectTransform, Line);
            RectImage("Inventory Accent", inventoryCard.transform, Cyan, 0f, 0f, 3f, 568f);
            TextAt("Inventory Label", inventoryCard.transform, "STORED MODS", 14f, Cyan,
                16f, 12f, 220f, 24f, false, FontStyles.Bold);
            inventorySlotParent = CreateRect("Inventory Grid", inventoryCard.transform);
            Place((RectTransform)inventorySlotParent, 16f, 52f, 328f, 496f);
            ConfigureGrid(inventorySlotParent, 72f, 72f, 13f, 13f, 4);

            BuildDescriptionCard(page);
        }

        private InventoryWeaponSlotUI CreateWeaponSlot(
            Transform parent, string label, float x, float y, Color accent)
        {
            Image card = RectImage(label, parent, Well, x, y, 96f, 160f);
            card.raycastTarget = true;
            AddBorder(card.rectTransform, new Color(accent.r, accent.g, accent.b, 0.3f));
            TextAt("Slot Label", card.transform, label, 11f, accent,
                10f, 8f, 76f, 18f, false, FontStyles.Bold);

            Image icon = CreateImage("Weapon Icon", card.transform, Color.white);
            Place(icon.rectTransform, 13f, 36f, 70f, 70f);
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            TMP_Text empty = TextAt("Empty", card.transform, "EMPTY", 12f, Muted,
                10f, 59f, 76f, 24f, false, FontStyles.Bold);
            empty.horizontalAlignment = HorizontalAlignmentOptions.Center;

            Image track = RectImage("Durability Track", card.transform, Line,
                13f, 125f, 70f, 6f);
            Image fill = CreateFill(track.transform, accent);

            Image highlight = CreateImage("Swap Target", card.transform,
                new Color(accent.r, accent.g, accent.b, 0.15f));
            Stretch(highlight.rectTransform);
            highlight.raycastTarget = false;
            highlight.enabled = false;

            InventoryWeaponSlotUI slot = card.gameObject.AddComponent<InventoryWeaponSlotUI>();
            slot.Configure(icon, fill, highlight, track.gameObject, empty);
            return slot;
        }

        private void BuildDescriptionCard(RectTransform page)
        {
            Image card = RectImage("Item Details", page, Card, 1034f, 0f, 326f, 568f);
            AddBorder(card.rectTransform, Line);
            RectImage("Details Accent", card.transform, Magenta, 0f, 0f, 3f, 568f);
            TextAt("Details Label", card.transform, "ITEM DETAILS", 14f, Magenta,
                16f, 12f, 220f, 24f, false, FontStyles.Bold);

            Image iconWell = RectImage("Icon Well", card.transform, Well, 20f, 54f, 92f, 92f);
            AddBorder(iconWell.rectTransform, new Color(Cyan.r, Cyan.g, Cyan.b, 0.25f));
            Image icon = CreateImage("Item Icon", iconWell.transform, Color.white);
            Place(icon.rectTransform, 10f, 10f, 72f, 72f);
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            TMP_Text name = TextAt("Item Name", card.transform, "", 23f, Paper,
                20f, 158f, 286f, 40f, true, FontStyles.Bold);
            TMP_Text description = TextAt("Description", card.transform, "", 14f, Muted,
                20f, 204f, 286f, 108f);
            description.textWrappingMode = TextWrappingModes.Normal;
            description.verticalAlignment = VerticalAlignmentOptions.Top;

            RectImage("Details Rule", card.transform, Line, 20f, 328f, 286f, 1f);
            TMP_Text stats = TextAt("Stats", card.transform, "", 13f, Paper,
                20f, 344f, 286f, 180f);
            stats.textWrappingMode = TextWrappingModes.Normal;
            stats.verticalAlignment = VerticalAlignmentOptions.Top;

            TMP_Text emptyText = TextAt("Empty", card.transform, "SELECT AN ITEM", 14f, Muted,
                20f, 246f, 286f, 32f, false, FontStyles.Bold);
            emptyText.horizontalAlignment = HorizontalAlignmentOptions.Center;

            descriptionUI = card.gameObject.AddComponent<ItemDescriptionUI>();
            descriptionUI.Configure(icon, name, description, stats, emptyText.gameObject);
            descriptionUI.Clear();
        }

        private ModSlotUI CreateThemedModSlot(Transform parent, Color accent)
        {
            RectTransform root = CreateRect("Mod Slot", parent);
            root.sizeDelta = new Vector2(72f, 72f);
            LayoutElement element = root.gameObject.AddComponent<LayoutElement>();
            element.minWidth = element.preferredWidth = 72f;
            element.minHeight = element.preferredHeight = 72f;

            Image background = root.gameObject.AddComponent<Image>();
            background.color = Well;
            background.raycastTarget = true;
            AddBorder(root, new Color(accent.r, accent.g, accent.b, 0.24f));

            Button button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.None;

            Image icon = CreateImage("Icon", root, Color.white);
            Place(icon.rectTransform, 10f, 12f, 52f, 47f);
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            TMP_Text inputHint = TextAt("Input Hint", root, "", 10f, Paper,
                5f, 2f, 62f, 16f, false, FontStyles.Bold);
            inputHint.horizontalAlignment = HorizontalAlignmentOptions.Right;

            Image track = RectImage("Durability Track", root, Line, 9f, 63f, 54f, 4f);
            Image durability = CreateFill(track.transform, accent);

            Image blocked = CreateImage("Blocked", root,
                new Color(Magenta.r, Magenta.g, Magenta.b, 0.18f));
            Stretch(blocked.rectTransform);
            blocked.raycastTarget = false;
            blocked.enabled = false;

            TMP_Text locked = TextAt("Locked", root, "×", 28f, Magenta,
                5f, 17f, 62f, 36f, false, FontStyles.Bold);
            locked.horizontalAlignment = HorizontalAlignmentOptions.Center;
            locked.gameObject.SetActive(false);

            GameObject hover = CreateRect("Hover", root).gameObject;
            Stretch((RectTransform)hover.transform);
            Image hoverTint = hover.AddComponent<Image>();
            hoverTint.color = new Color(accent.r, accent.g, accent.b, 0.1f);
            hoverTint.raycastTarget = false;
            AddBorder((RectTransform)hover.transform, accent);
            hover.SetActive(false);

            Image validTarget = CreateImage("Valid Target", root,
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.18f));
            Stretch(validTarget.rectTransform);
            validTarget.raycastTarget = false;
            validTarget.enabled = false;

            ModSlotUI slot = root.gameObject.AddComponent<ModSlotUI>();
            slot.Configure(icon, durability, background, blocked, validTarget,
                hover, inputHint, track.gameObject, null, locked);
            return slot;
        }

        private static void ConfigureGrid(
            Transform parent,
            float cellWidth,
            float cellHeight,
            float spacingX,
            float spacingY,
            int columns)
        {
            GridLayoutGroup grid = parent.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(cellWidth, cellHeight);
            grid.spacing = new Vector2(spacingX, spacingY);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.childAlignment = TextAnchor.UpperLeft;
        }

        private void BuildEmptyPage(RectTransform page, string title, string emptyMessage)
        {
            Image card = RectImage(title, page, Card, 0f, 0f, 1360f, 568f);
            AddBorder(card.rectTransform, Line);
            RectImage("Page Accent", card.transform, Magenta, 0f, 0f, 3f, 568f);
            TextAt("Page Title", card.transform, title, 36f, Paper,
                40f, 34f, 700f, 54f, true, FontStyles.Bold);
            RectImage("Page Rule", card.transform, Line, 40f, 112f, 1280f, 1f);
            TMP_Text empty = TextAt("Empty", card.transform, emptyMessage, 17f, Muted,
                40f, 242f, 1280f, 42f, false, FontStyles.Bold);
            empty.horizontalAlignment = HorizontalAlignmentOptions.Center;
        }

        private void BuildFooter()
        {
            RectImage("Footer Rule", frameTransform, Line, 40f, 760f, 1360f, 1f);
            TextAt("Input Hints", frameTransform,
                "DRAG / CLICK  MOVE MOD     HOVER  DETAILS     I / ESC  CLOSE",
                13f, Muted, 40f, 775f, 1100f, 24f);
        }

        private void FitFrame()
        {
            if (panel == null || frameTransform == null)
                return;

            Rect available = ((RectTransform)panel.transform).rect;
            float scale = Mathf.Min(
                available.width / (DesignSize.x + 100f),
                available.height / (DesignSize.y + 80f));
            frameTransform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
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
            string name,
            Transform parent,
            Color color,
            float x,
            float y,
            float width,
            float height)
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
            string name,
            Transform parent,
            string value,
            float size,
            Color color,
            float x,
            float y,
            float width,
            float height,
            bool heading = false,
            FontStyles style = FontStyles.Normal)
        {
            TextMeshProUGUI text = CreateRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = heading ? headingFont : bodyFont;
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
            RectTransform rect,
            float x,
            float y,
            float width,
            float height)
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

        #endregion


        #region Helpers

        private void ClearSlots(List<ModSlotUI> slots)
        {
            foreach (var slot in slots)
            {
                if (slot == null) continue;
                slot.gameObject.SetActive(false);
                Destroy(slot.gameObject);
            }
            slots.Clear();
        }

        private void OnDisable()
        {
            ClearSlots(activeModSlots);
            ClearSlots(passiveModSlots);
        }

        private void OnDestroy()
        {
            Unbind();

            if (generatedHeadingFont == null)
                return;

            foreach (Texture2D atlas in generatedHeadingFont.atlasTextures)
                if (atlas != null)
                    Destroy(atlas);
            if (generatedHeadingFont.material != null)
                Destroy(generatedHeadingFont.material);
            Destroy(generatedHeadingFont);
        }

        private void TrySelectDefaultSlotIfGamepad()
        {
            if (activeTab != Tab.Inventory) return;
            if (GameInputManager.Instance == null || !GameInputManager.Instance.IsUsingGamepad) return;
            if (EventSystem.current == null) return;

            var currentSelected = EventSystem.current.currentSelectedGameObject;
            if (currentSelected != null &&
                currentSelected.activeInHierarchy &&
                currentSelected.GetComponent<ModSlotUI>() != null)
            {
                return;
            }

            ModSlotUI defaultSlot = GetFirstSelectableModSlot();
            if (defaultSlot == null) return;

            EventSystem.current.SetSelectedGameObject(defaultSlot.gameObject);
        }

        private ModSlotUI GetFirstSelectableModSlot()
        {
            foreach (var slot in activeModSlots)
            {
                if (IsSlotSelectable(slot)) return slot;
            }

            foreach (var slot in passiveModSlots)
            {
                if (IsSlotSelectable(slot)) return slot;
            }

            foreach (var slot in inventorySlots)
            {
                if (IsSlotSelectable(slot)) return slot;
            }

            return null;
        }

        private ModSlotUI GetPreferredTargetSlotFor(ModInstance selectedMod)
        {
            if (selectedMod == null) return null;

            foreach (var slot in activeModSlots)
            {
                if (IsCompatibleEquipTarget(slot, selectedMod)) return slot;
            }

            foreach (var slot in passiveModSlots)
            {
                if (IsCompatibleEquipTarget(slot, selectedMod)) return slot;
            }

            return null;
        }

        private static bool IsCompatibleEquipTarget(ModSlotUI slot, ModInstance mod)
        {
            if (!IsSlotSelectable(slot) || slot == null || !slot.IsModSlot || slot.IsLocked || mod == null)
                return false;

            if (slot.Type == ModSlotUI.SlotType.ActiveMod)
                return mod.IsActive;

            if (slot.Type == ModSlotUI.SlotType.PassiveMod)
                return mod.IsPassive;

            return false;
        }

        private bool TryMoveSelection(Vector2 direction)
        {
            var currentObj = EventSystem.current.currentSelectedGameObject;
            var currentSlot = currentObj != null ? currentObj.GetComponent<ModSlotUI>() : null;
            var allSlots = GetAllNavigableSlots();
            if (allSlots.Count == 0) return false;

            if (currentSlot == null || !IsSlotSelectable(currentSlot))
            {
                EventSystem.current.SetSelectedGameObject(allSlots[0].gameObject);
                return true;
            }

            var currentPos = (Vector2)currentSlot.transform.position;
            ModSlotUI bestCandidate = null;
            float bestScore = float.NegativeInfinity;

            foreach (var candidate in allSlots)
            {
                if (candidate == currentSlot) continue;

                Vector2 toCandidate = (Vector2)candidate.transform.position - currentPos;
                float distance = toCandidate.magnitude;
                if (distance <= 0.001f) continue;

                Vector2 dir = toCandidate / distance;
                float alignment = Vector2.Dot(direction, dir);
                if (alignment <= 0.2f) continue;

                float score = (alignment * 1000f) - distance;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCandidate = candidate;
                }
            }

            if (bestCandidate == null) return false;

            EventSystem.current.SetSelectedGameObject(bestCandidate.gameObject);
            return true;
        }

        private List<ModSlotUI> GetAllNavigableSlots()
        {
            var result = new List<ModSlotUI>(activeModSlots.Count + passiveModSlots.Count + inventorySlots.Count);
            AddNavigableSlots(activeModSlots, result);
            AddNavigableSlots(passiveModSlots, result);
            AddNavigableSlots(inventorySlots, result);
            return result;
        }

        private static void AddNavigableSlots(List<ModSlotUI> source, List<ModSlotUI> target)
        {
            foreach (var slot in source)
            {
                if (IsSlotSelectable(slot))
                    target.Add(slot);
            }
        }

        private static bool IsSlotSelectable(ModSlotUI slot)
        {
            if (slot == null || !slot.gameObject.activeInHierarchy) return false;
            var selectable = slot.GetComponent<Selectable>();
            return selectable == null || selectable.IsInteractable();
        }

        private static void ConfigureSlotNavigation(ModSlotUI slot)
        {
            if (slot == null) return;
            var selectable = slot.GetComponent<Selectable>();
            if (selectable == null) return;

            var navigation = selectable.navigation;
            navigation.mode = Navigation.Mode.Automatic;
            selectable.navigation = navigation;
        }

        #endregion
    }
}
