using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;

namespace junklite
{
    public class WeaponPickupUI : MonoBehaviour
    {
        #region Fields

        [Header("Presentation Root")]
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_FontAsset headingFont;
        [SerializeField] private TMP_FontAsset bodyFont;
        [SerializeField] private Font headingSourceFont;

        [Header("New Weapon (Ground)")]
        [SerializeField] private Image newWeaponIcon;
        [SerializeField] private TMP_Text newWeaponName;
        [SerializeField] private Image newWeaponDurabilityFill;

        [Header("Slot 1")]
        [SerializeField] private Button slot1Button;
        [SerializeField] private Image slot1Icon;
        [SerializeField] private TMP_Text slot1Name;
        [SerializeField] private TMP_Text slot1EmptyText;
        [SerializeField] private GameObject slot1Highlight;
        [SerializeField] private Image slot1DurabilityFill;

        [Header("Slot 2")]
        [SerializeField] private Button slot2Button;
        [SerializeField] private Image slot2Icon;
        [SerializeField] private TMP_Text slot2Name;
        [SerializeField] private TMP_Text slot2EmptyText;
        [SerializeField] private GameObject slot2Highlight;
        [SerializeField] private Image slot2DurabilityFill;

        [Header("Input Hints")]
        [SerializeField] private TMP_Text inputHintsText;

        [Header("Display Settings")]
        [SerializeField] private Color occupiedNameColor = Color.white;

        private static readonly Color Backdrop = new(0.012f, 0.018f, 0.042f, 0.78f);
        private static readonly Color Frame = new(0.032f, 0.045f, 0.087f, 0.985f);
        private static readonly Color Card = new(0.055f, 0.073f, 0.125f, 0.96f);
        private static readonly Color Cyan = new(0.24f, 0.91f, 0.98f, 1f);
        private static readonly Color Magenta = new(0.86f, 0.25f, 0.89f, 1f);
        private static readonly Color Paper = new(0.9f, 0.94f, 1f, 1f);
        private static readonly Color Muted = new(0.51f, 0.61f, 0.74f, 1f);
        private static readonly Color Line = new(0.18f, 0.27f, 0.38f, 0.75f);
        private static readonly Vector2 DesignSize = new(1180f, 680f);

        private WeaponManager weaponManager;
        private PlayerWeaponLoadout weaponLoadout;
        private WorldWeaponPickup pendingPickup;
        private int selectedIndex;
        private RectTransform frameTransform;
        private TMP_Text slot1ActionText;
        private TMP_Text slot2ActionText;
        private TMP_FontAsset generatedHeadingFont;

        private SlotHoverHelper slot1Hover;
        private SlotHoverHelper slot2Hover;

        public event Action<bool> OnClosed;

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

        public void Bind(WeaponManager wm, WorldWeaponPickup pickup)
        {
            weaponManager = wm;
            weaponLoadout = wm != null ? wm.Loadout : null;
            pendingPickup = pickup;
            selectedIndex = 0;

            GameInputManager.Instance?.SwitchToUIActionMap();

            RefreshDisplay();
            SubscribeInput();
            SetupButtons();
        }

        public void Unbind()
        {
            UnsubscribeInput();
            CleanupButtons();

            GameInputManager.Instance?.SwitchToPlayerActionMap();

            weaponManager = null;
            weaponLoadout = null;
            pendingPickup = null;
        }

        #endregion

        #region Display

        private void RefreshDisplay()
        {
            if (pendingPickup == null || pendingPickup.weaponInstance == null) return;

            var newWeapon = pendingPickup.weaponInstance;
            SetWeaponDisplay(newWeaponIcon, newWeaponName, newWeapon.weaponData);
            SetDurabilityBar(newWeaponDurabilityFill, newWeapon);

            WeaponInstance w1 = weaponLoadout?.WeaponSlot1;
            SetSlotDisplay(slot1Icon, slot1Name, slot1EmptyText, slot1ActionText,
                slot1DurabilityFill, w1);

            WeaponInstance w2 = weaponLoadout?.WeaponSlot2;
            SetSlotDisplay(slot2Icon, slot2Name, slot2EmptyText, slot2ActionText,
                slot2DurabilityFill, w2);

            UpdateHighlight();

            if (inputHintsText != null)
                inputHintsText.text =
                    "LEFT / RIGHT  SELECT     E / A / ENTER  ASSIGN     ESC / B  CANCEL";
        }

        private void SetWeaponDisplay(Image icon, TMP_Text nameText, WeaponData data)
        {
            if (icon != null)
            {
                bool hasIcon = data != null && data.icon != null;
                icon.gameObject.SetActive(hasIcon);
                if (hasIcon)
                {
                    icon.sprite = data.icon;
                    icon.color = Color.white;
                }
            }
            if (nameText != null)
            {
                nameText.text = data != null ? data.displayName : "";
                nameText.color = occupiedNameColor;
            }
        }

        private void SetSlotDisplay(
            Image icon,
            TMP_Text nameText,
            TMP_Text emptyText,
            TMP_Text actionText,
            Image durabilityFill,
            WeaponInstance weapon)
        {
            bool hasWeapon = weapon != null && weapon.weaponData != null;

            if (icon != null)
            {
                icon.gameObject.SetActive(hasWeapon);
                if (hasWeapon)
                {
                    icon.sprite = weapon.weaponData.icon;
                    icon.color = Color.white;
                }
            }

            if (nameText != null)
            {
                nameText.gameObject.SetActive(hasWeapon);
                if (hasWeapon)
                {
                    nameText.text = weapon.weaponData.displayName;
                    nameText.color = occupiedNameColor;
                }
            }

            if (emptyText != null)
                emptyText.gameObject.SetActive(!hasWeapon);

            if (actionText != null)
                actionText.text = hasWeapon ? "REPLACE" : "EQUIP";

            SetDurabilityBar(durabilityFill, weapon);
        }

        private void SetDurabilityBar(Image fill, WeaponInstance weapon)
        {
            if (fill == null)
                return;

            bool show = weapon != null &&
                        weapon.weaponData != null &&
                        weapon.weaponData.maxWeaponDurability > 0;
            if (fill.transform.parent != null)
                fill.transform.parent.gameObject.SetActive(show);

            if (show)
            {
                fill.gameObject.SetActive(true);
                float amount = Mathf.Clamp01(
                    weapon.CurrentDurability / weapon.weaponData.maxWeaponDurability);
                RectTransform fillRect = fill.rectTransform;
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = new Vector2(amount, 1f);
                fillRect.offsetMin = fillRect.offsetMax = Vector2.zero;
            }
            else
            {
                fill.gameObject.SetActive(false);
            }
        }

        private void UpdateHighlight()
        {
            if (slot1Highlight != null) slot1Highlight.SetActive(selectedIndex == 0);
            if (slot2Highlight != null) slot2Highlight.SetActive(selectedIndex == 1);
            if (slot1ActionText != null) slot1ActionText.color = selectedIndex == 0 ? Cyan : Muted;
            if (slot2ActionText != null) slot2ActionText.color = selectedIndex == 1 ? Cyan : Muted;
        }

        #endregion

        #region Input

        private void SubscribeInput()
        {
            var input = GameInputManager.Instance;
            if (input == null) return;

            input.OnUINavigate += HandleNavigate;
            input.OnUISubmit += HandleSubmit;
            input.OnUICancel += HandleCancel;
        }

        private void UnsubscribeInput()
        {
            var input = GameInputManager.Instance;
            if (input == null) return;

            input.OnUINavigate -= HandleNavigate;
            input.OnUISubmit -= HandleSubmit;
            input.OnUICancel -= HandleCancel;
        }

        private void HandleNavigate(Vector2 dir)
        {
            if (dir.x > 0.3f || dir.y < -0.3f) selectedIndex = 1;
            else if (dir.x < -0.3f || dir.y > 0.3f) selectedIndex = 0;

            UpdateHighlight();
        }

        private void HandleSubmit() => ConfirmSelection();

        private void HandleCancel() => OnClosed?.Invoke(false);

        private void ConfirmSelection()
        {
            if (weaponManager == null || pendingPickup == null) return;

            weaponManager.PickupWeaponToSlot(selectedIndex + 1, pendingPickup);
            OnClosed?.Invoke(true);
        }

        private void OnDisable() => UnsubscribeInput();

        private void OnDestroy()
        {
            UnsubscribeInput();

            if (generatedHeadingFont == null)
                return;

            foreach (Texture2D atlas in generatedHeadingFont.atlasTextures)
                if (atlas != null)
                    Destroy(atlas);
            if (generatedHeadingFont.material != null)
                Destroy(generatedHeadingFont.material);
            Destroy(generatedHeadingFont);
        }

        #endregion

        #region Buttons

        private void SetupButtons()
        {
            if (slot1Button != null)
            {
                slot1Button.onClick.AddListener(OnSlot1Clicked);
                slot1Hover = GetOrAddHoverHelper(slot1Button.gameObject, 0);
            }

            if (slot2Button != null)
            {
                slot2Button.onClick.AddListener(OnSlot2Clicked);
                slot2Hover = GetOrAddHoverHelper(slot2Button.gameObject, 1);
            }
        }

        private void CleanupButtons()
        {
            if (slot1Button != null)
                slot1Button.onClick.RemoveListener(OnSlot1Clicked);

            if (slot2Button != null)
                slot2Button.onClick.RemoveListener(OnSlot2Clicked);

            if (slot1Hover != null) Destroy(slot1Hover);
            if (slot2Hover != null) Destroy(slot2Hover);

            slot1Hover = null;
            slot2Hover = null;
        }

        private void OnSlot1Clicked()
        {
            selectedIndex = 0;
            ConfirmSelection();
        }

        private void OnSlot2Clicked()
        {
            selectedIndex = 1;
            ConfirmSelection();
        }

        private SlotHoverHelper GetOrAddHoverHelper(GameObject go, int index)
        {
            var helper = go.GetComponent<SlotHoverHelper>();
            if (helper == null)
                helper = go.AddComponent<SlotHoverHelper>();

            helper.Init(index, this);
            return helper;
        }

        public void OnSlotHovered(int index)
        {
            selectedIndex = index;
            UpdateHighlight();
        }

        #endregion

        #region Presentation

        private void BuildInterface()
        {
            Stretch((RectTransform)transform);
            transform.localScale = Vector3.one;
            occupiedNameColor = Paper;

            if (panel == null && transform.childCount > 0)
                panel = transform.GetChild(0).gameObject;
            if (panel == null)
                panel = CreateRect("Panel", transform).gameObject;

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

            Image frame = CreateImage("Weapon Assignment Interface", panel.transform, Frame);
            frameTransform = frame.rectTransform;
            frameTransform.anchorMin = frameTransform.anchorMax = new Vector2(0.5f, 0.5f);
            frameTransform.pivot = new Vector2(0.5f, 0.5f);
            frameTransform.anchoredPosition = Vector2.zero;
            frameTransform.sizeDelta = DesignSize;
            AddBorder(frameTransform, Line);

            for (int i = 1; i < 12; i++)
                RectImage("Grid Line", frameTransform,
                    new Color(Cyan.r, Cyan.g, Cyan.b, 0.025f), i * 100f, 0f, 1f, 680f);

            RectImage("Cyan Edge", frameTransform, Cyan, 0f, 0f, 340f, 3f);
            RectImage("Magenta Edge", frameTransform, Magenta, 1000f, 677f, 180f, 3f);
            RectImage("Left Mark", frameTransform, Cyan, 0f, 0f, 3f, 28f);
            RectImage("Right Mark", frameTransform, Magenta, 1177f, 652f, 3f, 28f);

            BuildHeader();
            BuildIncomingWeaponCard();
            BuildSlotCards();
            BuildFooter();
            FitFrame();
        }

        private void BuildHeader()
        {
            TextAt("Brand", frameTransform, "JUNKLITE  /  LOADOUT LINK",
                17f, Cyan, 40, 24, 650, 26);
            TextAt("Title", frameTransform, "ASSIGN WEAPON",
                46f, Paper, 38, 55, 720, 66, true);

            TMP_Text prompt = TextAt("Prompt", frameTransform, "SELECT A SLOT",
                15f, Magenta, 850, 73, 290, 30);
            prompt.horizontalAlignment = HorizontalAlignmentOptions.Right;

            RectImage("Header Rule", frameTransform, Line, 40, 148, 1100, 1);
            RectImage("Header Signal", frameTransform, Magenta, 40, 148, 92, 3);
        }

        private void BuildIncomingWeaponCard()
        {
            Image card = RectImage("Incoming Weapon", frameTransform, Card, 40, 178, 310, 380);
            card.raycastTarget = false;
            AddBorder(card.rectTransform, Line);
            RectImage("Incoming Accent", card.transform, Magenta, 0, 0, 3, 380);

            TextAt("Incoming Label", card.transform, "INCOMING WEAPON",
                15f, Magenta, 24, 20, 262, 28);

            Image iconWell = RectImage("Icon Well", card.transform,
                new Color(0.025f, 0.035f, 0.07f, 0.82f), 60, 69, 190, 190);
            AddBorder(iconWell.rectTransform, new Color(Cyan.r, Cyan.g, Cyan.b, 0.25f));

            newWeaponIcon = CreateImage("Weapon Icon", iconWell.transform, Color.white);
            Place(newWeaponIcon.rectTransform, 20, 20, 150, 150);
            newWeaponIcon.preserveAspect = true;
            newWeaponIcon.raycastTarget = false;
            newWeaponIcon.gameObject.SetActive(false);

            newWeaponName = TextAt("Weapon Name", card.transform, "--",
                25f, Paper, 22, 272, 266, 42, true);
            newWeaponName.horizontalAlignment = HorizontalAlignmentOptions.Center;

            TextAt("Durability Label", card.transform, "DURABILITY",
                13f, Muted, 24, 324, 262, 20);
            newWeaponDurabilityFill = CreateDurabilityBar(card.transform, 24, 352, 262, Magenta);
        }

        private void BuildSlotCards()
        {
            SlotView slot1 = CreateSlotCard("Loadout Slot 1", "SLOT 01", 382, 178);
            slot1Button = slot1.Button;
            slot1Icon = slot1.Icon;
            slot1Name = slot1.Name;
            slot1EmptyText = slot1.Empty;
            slot1Highlight = slot1.Highlight;
            slot1DurabilityFill = slot1.DurabilityFill;
            slot1ActionText = slot1.Action;

            SlotView slot2 = CreateSlotCard("Loadout Slot 2", "SLOT 02", 382, 373);
            slot2Button = slot2.Button;
            slot2Icon = slot2.Icon;
            slot2Name = slot2.Name;
            slot2EmptyText = slot2.Empty;
            slot2Highlight = slot2.Highlight;
            slot2DurabilityFill = slot2.DurabilityFill;
            slot2ActionText = slot2.Action;
        }

        private SlotView CreateSlotCard(string name, string slotLabel, float x, float y)
        {
            Image background = CreateImage(name, frameTransform, Card);
            Place(background.rectTransform, x, y, 758, 165);
            background.raycastTarget = true;

            Button button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.22f, 1.22f, 1.22f, 1f);
            colors.pressedColor = new Color(0.72f, 0.86f, 1f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.06f;
            button.colors = colors;

            Image highlight = RectImage("Selected", background.transform,
                new Color(Cyan.r, Cyan.g, Cyan.b, 0.13f), 0, 0, 758, 165);
            AddBorder(background.rectTransform, Line);
            RectImage("Slot Accent", background.transform, Cyan, 0, 0, 3, 165);

            TextAt("Slot Label", background.transform, slotLabel,
                14f, Cyan, 20, 14, 140, 24);

            Image iconWell = RectImage("Icon Well", background.transform,
                new Color(0.025f, 0.035f, 0.07f, 0.82f), 22, 48, 92, 92);
            AddBorder(iconWell.rectTransform, new Color(Cyan.r, Cyan.g, Cyan.b, 0.2f));

            Image icon = CreateImage("Weapon Icon", iconWell.transform, Color.white);
            Place(icon.rectTransform, 10, 10, 72, 72);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.gameObject.SetActive(false);

            TMP_Text weaponName = TextAt("Weapon Name", background.transform, "--",
                25f, Paper, 142, 43, 420, 42, true);
            TMP_Text empty = TextAt("Empty", background.transform, "EMPTY SLOT",
                23f, Muted, 142, 43, 420, 42, true);
            TMP_Text action = TextAt("Action", background.transform, "EQUIP",
                16f, Muted, 592, 43, 140, 42);
            action.horizontalAlignment = HorizontalAlignmentOptions.Right;

            TextAt("Durability Label", background.transform, "DURABILITY",
                12f, Muted, 142, 99, 420, 18);
            Image durability = CreateDurabilityBar(background.transform, 142, 126, 420, Cyan);

            return new SlotView(button, icon, weaponName, empty,
                highlight.gameObject, durability, action);
        }

        private void BuildFooter()
        {
            RectImage("Footer Rule", frameTransform, Line, 40, 602, 1100, 1);
            inputHintsText = TextAt("Input Hints", frameTransform,
                "LEFT / RIGHT  SELECT     E / A / ENTER  ASSIGN     ESC / B  CANCEL",
                14f, Muted, 40, 620, 1100, 30);
        }

        private Image CreateDurabilityBar(
            Transform parent,
            float x,
            float y,
            float width,
            Color fillColor)
        {
            Image track = RectImage("Durability Track", parent, Line, x, y, width, 7);
            Image fill = CreateImage("Fill", track.transform, fillColor);
            Stretch(fill.rectTransform);
            fill.raycastTarget = false;
            track.gameObject.SetActive(false);
            return fill;
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
            var go = new GameObject(name, typeof(RectTransform));
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
            bool heading = false)
        {
            TextMeshProUGUI text = CreateRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = heading ? headingFont : bodyFont;
            text.text = value;
            text.fontSize = size;
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
            RectImage("Top Border", parent, color, 0, 0, parent.rect.width, 1);
            RectImage("Bottom Border", parent, color, 0, parent.rect.height - 1,
                parent.rect.width, 1);
            RectImage("Left Border", parent, color, 0, 0, 1, parent.rect.height);
            RectImage("Right Border", parent, color, parent.rect.width - 1, 0,
                1, parent.rect.height);
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

        private readonly struct SlotView
        {
            public SlotView(
                Button button,
                Image icon,
                TMP_Text name,
                TMP_Text empty,
                GameObject highlight,
                Image durabilityFill,
                TMP_Text action)
            {
                Button = button;
                Icon = icon;
                Name = name;
                Empty = empty;
                Highlight = highlight;
                DurabilityFill = durabilityFill;
                Action = action;
            }

            public Button Button { get; }
            public Image Icon { get; }
            public TMP_Text Name { get; }
            public TMP_Text Empty { get; }
            public GameObject Highlight { get; }
            public Image DurabilityFill { get; }
            public TMP_Text Action { get; }
        }

        #endregion
    }

    /// <summary>
    /// Tiny helper that sits on each slot Button to detect mouse hover.
    /// </summary>
    public class SlotHoverHelper : MonoBehaviour, IPointerEnterHandler
    {
        private int slotIndex;
        private WeaponPickupUI owner;

        public void Init(int index, WeaponPickupUI ui)
        {
            slotIndex = index;
            owner = ui;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (owner != null)
                owner.OnSlotHovered(slotIndex);
        }
    }
}
