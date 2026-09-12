using UnityEngine;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>
    /// Merged weapon + mod combat HUD. Visible only during Mod Combat state.
    /// Manages two weapon slots (with mouse button visuals) and dynamic mod slots.
    /// </summary>
    [DisallowMultipleComponent]
    public class ModCombatUI : MonoBehaviour
    {
        #region Fields

        [Header("Panel (child object to show/hide)")]
        [SerializeField] private GameObject panel;

        [Header("Weapon Slots")]
        [SerializeField] private WeaponSlotUI slot1; // Left click / Weapon 1
        [SerializeField] private WeaponSlotUI slot2; // Right click / Weapon 2

        [Header("Active Mod Slots")]
        [SerializeField] private Transform activeModParent;
        [SerializeField] private CombatModSlotUI modSlotPrefab;

        [Header("Presentation")]
        [SerializeField] private TMP_FontAsset bodyFont;

        private static readonly Color Frame = new(0.032f, 0.045f, 0.087f, 0.97f);
        private static readonly Color Card = new(0.055f, 0.073f, 0.125f, 0.96f);
        private static readonly Color Well = new(0.025f, 0.035f, 0.07f, 0.9f);
        private static readonly Color Cyan = new(0.24f, 0.91f, 0.98f, 1f);
        private static readonly Color Magenta = new(0.86f, 0.25f, 0.89f, 1f);
        private static readonly Color Paper = new(0.9f, 0.94f, 1f, 1f);
        private static readonly Color Muted = new(0.51f, 0.61f, 0.74f, 1f);
        private static readonly Color Line = new(0.18f, 0.27f, 0.38f, 0.75f);

        // Runtime
        private WeaponManager _weaponManager;
        private PlayerWeaponLoadout _weaponLoadout;
        private ModManager _modManager;
        private PlayerCharacter _player;

        private readonly List<CombatModSlotUI> activeSlotUIs = new();

        private int lastActiveCount;
        private bool themedInterfaceBuilt;

        #endregion

        private void Awake()
        {
            if (bodyFont == null)
                bodyFont = TMP_Settings.defaultFontAsset;

            BuildInterface();
        }

        // -----------------------------------------------------------------------
        #region Bind / Unbind

        public void Bind(ModManager modManager, WeaponManager weaponManager)
        {
            Unbind();

            _weaponManager = weaponManager;
            _weaponLoadout = weaponManager != null ? weaponManager.Loadout : null;
            _modManager = modManager;
            _player = _modManager != null ? _modManager.GetComponent<PlayerCharacter>() : null;

            if (_weaponManager != null)
            {
                _weaponManager.OnCombatModeChanged += OnCombatModeChanged;
                _weaponManager.OnEnemyHit += OnEnemyHitHandler;
            }
            if (_weaponLoadout != null)
                _weaponLoadout.WeaponChanged += RefreshWeapons;

            if (_modManager != null)
                _modManager.OnModSlotsChanged += RefreshMods;

            OnCombatModeChanged();
        }

        public void Unbind()
        {
            if (_weaponManager != null)
            {
                _weaponManager.OnCombatModeChanged -= OnCombatModeChanged;
                _weaponManager.OnEnemyHit -= OnEnemyHitHandler;
            }
            if (_weaponLoadout != null)
                _weaponLoadout.WeaponChanged -= RefreshWeapons;

            if (_modManager != null)
                _modManager.OnModSlotsChanged -= RefreshMods;

            _weaponManager = null;
            _weaponLoadout = null;
            _modManager = null;
            _player = null;

            ClearModSlots();
            ResetWeaponSlots();
            SetVisible(false);
        }

        #endregion

        // -----------------------------------------------------------------------
        #region Combat Mode Visibility

        private void OnCombatModeChanged()
        {
            bool show = _weaponManager != null && _weaponManager.IsModCombat;
            SetVisible(show);

            if (show)
            {
                RefreshWeapons();
                RebuildAndRefreshMods();
            }
            else
            {
                ClearModSlots();
                ResetWeaponSlots();
            }
        }

        private void SetVisible(bool visible)
        {
            if (panel != null)
                panel.SetActive(visible);
        }

        #endregion

        // -----------------------------------------------------------------------
        #region Weapon Slots

        private void RefreshWeapons()
        {
            if (_weaponManager == null || _weaponLoadout == null) return;

            if (slot1 != null)
            {
                var weapon1 = _weaponLoadout.WeaponSlot1;
                if (weapon1 != null) slot1.Bind(weapon1, true);
                else slot1.SetContentActive(false);
            }

            if (slot2 != null)
            {
                var weapon2 = _weaponLoadout.WeaponSlot2;
                if (weapon2 != null) slot2.Bind(weapon2, true);
                else slot2.SetContentActive(false);
            }

            UpdateActiveIndicators();
        }

        private void ResetWeaponSlots()
        {
            if (slot1 != null) slot1.SetContentActive(false);
            if (slot2 != null) slot2.SetContentActive(false);
        }

        private void UpdateActiveIndicators()
        {
            if (_weaponManager == null || _weaponLoadout == null) return;
            var active = _weaponManager.ActiveWeapon;
            slot1?.SetActive(active != null && active == _weaponLoadout.WeaponSlot1);
            slot2?.SetActive(active != null && active == _weaponLoadout.WeaponSlot2);
        }

        private void OnEnemyHitHandler(EnemyCharacter _, float __) => UpdateActiveIndicators();

        #endregion

        // -----------------------------------------------------------------------
        #region Mod Slots

        private void RefreshMods()
        {
            if (_modManager == null) return;

            bool activeCountChanged = _modManager.UnlockedActiveSlots != lastActiveCount;

            if (activeCountChanged)
            {
                RebuildAndRefreshMods();
                return;
            }

            RefreshModSlotContents();
        }

        private void RebuildAndRefreshMods()
        {
            ClearModSlots();
            BuildModSlots();
            RefreshModSlotContents();
        }

        private void BuildModSlots()
        {
            if (_modManager == null) return;

            lastActiveCount = _modManager.UnlockedActiveSlots;

            if (activeModParent != null && (themedInterfaceBuilt || modSlotPrefab != null))
            {
                for (int i = 0; i < lastActiveCount; i++)
                {
                    CombatModSlotUI slot = themedInterfaceBuilt
                        ? CreateThemedModSlot(i)
                        : Instantiate(modSlotPrefab, activeModParent);
                    activeSlotUIs.Add(slot);
                }
            }
        }

        private void RefreshModSlotContents()
        {
            if (_modManager == null) return;

            for (int i = 0; i < activeSlotUIs.Count; i++)
            {
                var mod = _modManager.GetActiveMod(i);
                string hint = GameInputManager.Instance != null
                    ? GameInputManager.Instance.GetModActivateHint(i)
                    : "";
                activeSlotUIs[i].Bind(mod, _player, hint);
            }
        }

        private void ClearModSlots()
        {
            foreach (var ui in activeSlotUIs)
                if (ui != null) Destroy(ui.gameObject);
            activeSlotUIs.Clear();

            lastActiveCount = 0;
        }

        #endregion


        #region Presentation

        private void BuildInterface()
        {
            RectTransform root = (RectTransform)transform;
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.anchoredPosition = new Vector2(0f, 92f);
            root.sizeDelta = new Vector2(980f, 144f);
            root.localScale = Vector3.one;

            if (panel == null)
                panel = CreateRect("Panel", transform).gameObject;

            Stretch((RectTransform)panel.transform);
            panel.transform.localScale = Vector3.one;

            for (int i = panel.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = panel.transform.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            Image frame = CreateImage("Combat Interface", panel.transform, Frame);
            Stretch(frame.rectTransform);
            frame.raycastTarget = false;
            AddBorder(frame.rectTransform, Line);
            RectImage("Cyan Edge", frame.transform, Cyan, 0f, 0f, 260f, 3f);
            RectImage("Magenta Edge", frame.transform, Magenta, 780f, 141f, 200f, 3f);

            slot1 = CreateWeaponSlot(frame.transform, "SLOT 01", "LMB", 18f, Cyan);
            slot2 = CreateWeaponSlot(frame.transform, "SLOT 02", "RMB", 788f, Magenta);

            Image modsCard = RectImage("Active Mods", frame.transform, Card, 212f, 18f, 556f, 108f);
            AddBorder(modsCard.rectTransform, Line);
            RectImage("Mods Accent", modsCard.transform, Cyan, 0f, 0f, 3f, 108f);
            TextAt("Mods Label", modsCard.transform, "ACTIVE MODS", 13f, Cyan,
                16f, 8f, 180f, 20f, FontStyles.Bold);

            RectTransform modParent = CreateRect("Mod Slots", modsCard.transform);
            Place(modParent, 14f, 32f, 528f, 66f);
            HorizontalLayoutGroup layout = modParent.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 10f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            activeModParent = modParent;

            themedInterfaceBuilt = true;
        }

        private WeaponSlotUI CreateWeaponSlot(
            Transform parent,
            string slotLabel,
            string inputHint,
            float x,
            Color accent)
        {
            Image card = RectImage(slotLabel, parent, Card, x, 18f, 174f, 108f);
            AddBorder(card.rectTransform, Line);
            RectImage("Slot Accent", card.transform, accent, 0f, 0f, 3f, 108f);

            TextAt("Slot Label", card.transform, slotLabel, 13f, accent,
                14f, 7f, 82f, 20f, FontStyles.Bold);
            TMP_Text hint = TextAt("Input Hint", card.transform, inputHint, 12f, Muted,
                112f, 7f, 46f, 20f, FontStyles.Bold);
            hint.horizontalAlignment = HorizontalAlignmentOptions.Right;

            Image iconWell = RectImage("Icon Well", card.transform, Well, 14f, 32f, 62f, 62f);
            AddBorder(iconWell.rectTransform, new Color(accent.r, accent.g, accent.b, 0.28f));

            Image icon = CreateImage("Weapon Icon", iconWell.transform, Color.white);
            Place(icon.rectTransform, 7f, 7f, 48f, 48f);
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            TMP_Text empty = TextAt("Empty", card.transform, "EMPTY", 14f, Muted,
                88f, 41f, 70f, 22f, FontStyles.Bold);

            Image durabilityTrack = RectImage("Durability Track", card.transform, Line,
                88f, 76f, 70f, 5f);
            Image durability = CreateImage("Durability Fill", durabilityTrack.transform, accent);
            Stretch(durability.rectTransform);
            durability.type = Image.Type.Filled;
            durability.fillMethod = Image.FillMethod.Horizontal;
            durability.fillOrigin = 0;
            durability.raycastTarget = false;

            GameObject active = CreateRect("Active Indicator", card.transform).gameObject;
            Stretch((RectTransform)active.transform);
            Image activeTint = active.AddComponent<Image>();
            activeTint.color = new Color(accent.r, accent.g, accent.b, 0.08f);
            activeTint.raycastTarget = false;
            AddBorder((RectTransform)active.transform, accent);
            active.SetActive(false);

            WeaponSlotUI slot = card.gameObject.AddComponent<WeaponSlotUI>();
            slot.Configure(icon, durability, active, durabilityTrack.gameObject, empty);
            return slot;
        }

        private CombatModSlotUI CreateThemedModSlot(int index)
        {
            RectTransform root = CreateRect($"Mod Slot {index + 1}", activeModParent);
            root.sizeDelta = new Vector2(66f, 66f);
            LayoutElement layout = root.gameObject.AddComponent<LayoutElement>();
            layout.minWidth = layout.preferredWidth = 66f;
            layout.minHeight = layout.preferredHeight = 66f;

            Image background = root.gameObject.AddComponent<Image>();
            background.color = Well;
            background.raycastTarget = false;
            AddBorder(root, new Color(Cyan.r, Cyan.g, Cyan.b, 0.24f));

            Image icon = CreateImage("Icon", root, Color.white);
            Place(icon.rectTransform, 9f, 12f, 48f, 43f);
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            Image cooldown = CreateImage("Cooldown", root, new Color(0.01f, 0.02f, 0.045f, 0.76f));
            Place(cooldown.rectTransform, 7f, 7f, 52f, 52f);
            cooldown.type = Image.Type.Filled;
            cooldown.fillMethod = Image.FillMethod.Radial360;
            cooldown.fillOrigin = 2;
            cooldown.fillClockwise = false;
            cooldown.raycastTarget = false;

            TMP_Text hint = TextAt("Input Hint", root, "", 11f, Paper,
                4f, 2f, 58f, 18f, FontStyles.Bold);
            hint.horizontalAlignment = HorizontalAlignmentOptions.Right;

            Image durabilityTrack = RectImage("Durability Track", root, Line, 7f, 58f, 52f, 4f);
            Image durability = CreateImage("Durability Fill", durabilityTrack.transform, Magenta);
            Stretch(durability.rectTransform);
            durability.type = Image.Type.Filled;
            durability.fillMethod = Image.FillMethod.Horizontal;
            durability.fillOrigin = 0;
            durability.raycastTarget = false;

            CombatModSlotUI slot = root.gameObject.AddComponent<CombatModSlotUI>();
            slot.Configure(icon, durability, hint, cooldown, Color.white,
                new Color(Muted.r, Muted.g, Muted.b, 0.55f));
            return slot;
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

        #endregion
    }
}
