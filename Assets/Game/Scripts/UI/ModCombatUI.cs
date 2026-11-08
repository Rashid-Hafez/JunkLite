using UnityEngine;
using System.Collections.Generic;

namespace junklite
{
    /// <summary>
    /// Active-mod HUD bar at the bottom of the screen. Mods are always live, so the bar is
    /// visible whenever a player is bound. One slot per unlocked active mod slot, with
    /// device-aware input hints.
    ///
    /// The bar is authored UI: <c>Active Mods Bar.prefab</c>, built once by
    /// <c>Tools &gt; JunkLite &gt; UI &gt; Build Active Mods Bar Prefab</c> and edited in the
    /// Inspector afterwards. This component only copies the inactive slot template, binds
    /// mods to the copies and shows or hides the panel. It draws nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public class ModCombatUI : MonoBehaviour
    {
        #region Fields

        [Header("Authored bar")]
        [Tooltip("Child object shown while a player is bound.")]
        [SerializeField] private GameObject panel;
        [Tooltip("Layout parent the slot copies go under.")]
        [SerializeField] private Transform activeModParent;
        [Tooltip("Inactive authored slot inside the bar; one copy is made per unlocked slot.")]
        [SerializeField] private CombatModSlotUI slotTemplate;
        [Tooltip("Fallback slot prefab for a HUD that has no template authored.")]
        [SerializeField] private CombatModSlotUI modSlotPrefab;

        // Runtime
        private ModManager _modManager;
        private PlayerCharacter _player;
        private GameInputManager subscribedInputManager;

        private readonly List<CombatModSlotUI> activeSlotUIs = new();

        private int lastActiveCount;

        #endregion

        private void Awake()
        {
            if (slotTemplate != null && slotTemplate.gameObject.activeSelf)
                slotTemplate.gameObject.SetActive(false);
        }

        // -----------------------------------------------------------------------
        #region Bind / Unbind

        public void Bind(ModManager modManager, WeaponManager weaponManager)
        {
            Unbind();

            _modManager = modManager;
            _player = _modManager != null ? _modManager.GetComponent<PlayerCharacter>() : null;

            if (_modManager != null)
                _modManager.OnModSlotsChanged += RefreshMods;

            subscribedInputManager = GameInputManager.Instance;
            if (subscribedInputManager != null)
                subscribedInputManager.OnInputDeviceChanged += HandleInputDeviceChanged;

            SetVisible(_modManager != null);
            RebuildAndRefreshMods();
        }

        public void Unbind()
        {
            if (_modManager != null)
                _modManager.OnModSlotsChanged -= RefreshMods;

            if (subscribedInputManager != null)
            {
                subscribedInputManager.OnInputDeviceChanged -= HandleInputDeviceChanged;
                subscribedInputManager = null;
            }

            _modManager = null;
            _player = null;

            ClearModSlots();
            SetVisible(false);
        }

        #endregion

        private void SetVisible(bool visible)
        {
            if (panel != null)
                panel.SetActive(visible);
        }

        private void HandleInputDeviceChanged(bool _)
        {
            if (_modManager != null)
                RefreshModSlotContents();
        }

        // -----------------------------------------------------------------------
        #region Mod Slots

        private void RefreshMods()
        {
            if (_modManager == null) return;

            int previousCount = lastActiveCount;
            bool activeCountChanged = _modManager.UnlockedActiveSlots != previousCount;

            if (activeCountChanged)
            {
                RebuildAndRefreshMods();
                // A bought Mod Slot upgrade: flash the slots that just appeared.
                if (lastActiveCount > previousCount)
                    HighlightUnlockedSlots(previousCount, lastActiveCount - previousCount, new Color(0.86f, 0.25f, 0.89f, 1f));
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

            CombatModSlotUI source = slotTemplate != null ? slotTemplate : modSlotPrefab;
            if (activeModParent == null || source == null)
            {
                if (lastActiveCount > 0)
                    Debug.LogWarning($"{name}: no authored slot template or prefab. Run Tools > JunkLite > UI > Build Active Mods Bar Prefab.", this);
                return;
            }

            for (int i = 0; i < lastActiveCount; i++)
            {
                CombatModSlotUI slot = Instantiate(source, activeModParent);
                slot.name = $"Mod Slot {i + 1}";
                slot.gameObject.SetActive(true);
                activeSlotUIs.Add(slot);
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

        public void HighlightUnlockedSlots(int first, int count, Color color)
        {
            for (int i = Mathf.Max(0, first); i < Mathf.Min(first + count, activeSlotUIs.Count); i++)
                activeSlotUIs[i].FlashUnlock(color);
        }

        #endregion

        private void OnDestroy() => Unbind();
    }
}
