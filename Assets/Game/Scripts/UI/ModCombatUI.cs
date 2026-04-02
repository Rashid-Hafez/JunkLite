using UnityEngine;
using System.Collections.Generic;

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

        [Header("Input Hints (Active Slots)")]
        [SerializeField] private string[] activeSlotHints = { "X+C", "Y+C", "X+V", "Y+V" };

        // Runtime
        private WeaponManager _weaponManager;
        private ModManager _modManager;
        private PlayerCharacter _player;

        private readonly List<CombatModSlotUI> activeSlotUIs = new();

        private int lastActiveCount;

        #endregion

        // -----------------------------------------------------------------------
        #region Bind / Unbind

        public void Bind(ModManager modManager, WeaponManager weaponManager)
        {
            Unbind();

            _weaponManager = weaponManager;
            _modManager = modManager;
            _player = _modManager != null ? _modManager.GetComponent<PlayerCharacter>() : null;

            if (_weaponManager != null)
            {
                _weaponManager.OnCombatModeChanged += OnCombatModeChanged;
                _weaponManager.OnWeaponChanged += RefreshWeapons;
                _weaponManager.OnEnemyHit += OnEnemyHitHandler;
            }

            if (_modManager != null)
                _modManager.OnModSlotsChanged += RefreshMods;

            OnCombatModeChanged();
        }

        public void Unbind()
        {
            if (_weaponManager != null)
            {
                _weaponManager.OnCombatModeChanged -= OnCombatModeChanged;
                _weaponManager.OnWeaponChanged -= RefreshWeapons;
                _weaponManager.OnEnemyHit -= OnEnemyHitHandler;
            }

            if (_modManager != null)
                _modManager.OnModSlotsChanged -= RefreshMods;

            _weaponManager = null;
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
            if (_weaponManager == null) return;

            if (slot1 != null)
            {
                var weapon1 = _weaponManager.WeaponSlot1;
                if (weapon1 != null) slot1.Bind(weapon1, true);
                else slot1.SetContentActive(false);
            }

            if (slot2 != null)
            {
                var weapon2 = _weaponManager.WeaponSlot2;
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
            if (_weaponManager == null) return;
            var active = _weaponManager.ActiveWeapon;
            slot1?.SetActive(active != null && active == _weaponManager.WeaponSlot1);
            slot2?.SetActive(active != null && active == _weaponManager.WeaponSlot2);
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

            if (modSlotPrefab != null && activeModParent != null)
            {
                for (int i = 0; i < lastActiveCount; i++)
                    activeSlotUIs.Add(Instantiate(modSlotPrefab, activeModParent));
            }
        }

        private void RefreshModSlotContents()
        {
            if (_modManager == null) return;

            for (int i = 0; i < activeSlotUIs.Count; i++)
            {
                var mod = _modManager.GetActiveMod(i);
                string hint = i < activeSlotHints.Length ? activeSlotHints[i] : "";
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
    }
}