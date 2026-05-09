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

        private WeaponManager weaponManager;
        private WorldWeaponPickup pendingPickup;
        private int selectedIndex;

        private SlotHoverHelper slot1Hover;
        private SlotHoverHelper slot2Hover;

        public event Action<bool> OnClosed;

        #endregion

        #region Bind / Unbind

        public void Bind(WeaponManager wm, WorldWeaponPickup pickup)
        {
            weaponManager = wm;
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

            WeaponInstance w1 = weaponManager != null ? weaponManager.WeaponSlot1 : null;
            SetSlotDisplay(slot1Icon, slot1Name, slot1EmptyText, slot1DurabilityFill, w1);

            WeaponInstance w2 = weaponManager != null ? weaponManager.WeaponSlot2 : null;
            SetSlotDisplay(slot2Icon, slot2Name, slot2EmptyText, slot2DurabilityFill, w2);

            UpdateHighlight();

            if (inputHintsText != null)
                inputHintsText.text = "[E] Confirm    [Esc] Cancel";
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

        private void SetSlotDisplay(Image icon, TMP_Text nameText, TMP_Text emptyText, Image durabilityFill, WeaponInstance weapon)
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

            SetDurabilityBar(durabilityFill, weapon);
        }

        private void SetDurabilityBar(Image fill, WeaponInstance weapon)
        {
            if (fill == null) return;

            if (weapon != null && weapon.weaponData != null && weapon.weaponData.maxWeaponDurability > 0)
            {
                fill.gameObject.SetActive(true);
                fill.fillAmount = weapon.CurrentDurability / weapon.weaponData.maxWeaponDurability;
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
        private void OnDestroy() => UnsubscribeInput();

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