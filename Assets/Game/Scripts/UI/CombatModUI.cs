using UnityEngine;
using System.Collections.Generic;

namespace junklite
{
    /// <summary>
    /// Manages active and passive mod slot UIs. Visible only during Mod Combat.
    /// Rebuilds slots when unlock count changes.
    /// </summary>
    public class CombatModUI : MonoBehaviour
    {
        #region Fields

        [Header("Panel")]
        [SerializeField] private GameObject panel;

        [Header("Active Mod Slots")]
        [SerializeField] private Transform activeModParent;
        [SerializeField] private CombatModSlotUI modSlotPrefab;

        [Header("Passive Mod Slots")]
        [SerializeField] private Transform passiveModParent;
        [SerializeField] private CombatModSlotUI passiveSlotPrefab;

        [Header("Input Hints (Active Slots)")]
        [SerializeField] private string[] activeSlotHints = { "X+C", "Y+C", "X+V", "Y+V" };

        private ModManager _modManager;
        private WeaponManager _weaponManager;
        private readonly List<CombatModSlotUI> activeSlotUIs = new();
        private readonly List<CombatModSlotUI> passiveSlotUIs = new();

        private int lastActiveCount;
        private int lastPassiveCount;

        #endregion

        #region Bind / Unbind

        public void Bind(ModManager modManager, WeaponManager weaponManager)
        {
            Unbind();

            _modManager = modManager;
            _weaponManager = weaponManager;

            if (_modManager != null)
                _modManager.OnModSlotsChanged += Refresh;

            if (_weaponManager != null)
                _weaponManager.OnCombatModeChanged += OnCombatModeChanged;

            OnCombatModeChanged();
        }

        public void Unbind()
        {
            if (_modManager != null)
                _modManager.OnModSlotsChanged -= Refresh;

            if (_weaponManager != null)
                _weaponManager.OnCombatModeChanged -= OnCombatModeChanged;

            _modManager = null;
            _weaponManager = null;

            ClearSlots();
            SetVisible(false);
        }

        #endregion

        #region Combat Mode

        private void OnCombatModeChanged()
        {
            bool show = _weaponManager != null && _weaponManager.IsModCombat;
            SetVisible(show);

            if (show)
                RebuildAndRefresh();
            else
                ClearSlots();
        }

        #endregion

        #region Refresh

        public void Refresh()
        {
            if (_modManager == null) return;

            // Rebuild if slot count changed
            if (_modManager.UnlockedActiveSlots != lastActiveCount ||
                _modManager.UnlockedPassiveSlots != lastPassiveCount)
            {
                RebuildAndRefresh();
                return;
            }

            RefreshSlotContents();
        }

        private void RebuildAndRefresh()
        {
            if (_modManager == null) return;

            ClearSlots();
            BuildSlots();
            RefreshSlotContents();
        }

        private void BuildSlots()
        {
            lastActiveCount = _modManager.UnlockedActiveSlots;
            lastPassiveCount = _modManager.UnlockedPassiveSlots;

            // Active slots
            if (modSlotPrefab != null && activeModParent != null)
            {
                for (int i = 0; i < lastActiveCount; i++)
                {
                    var ui = Instantiate(modSlotPrefab, activeModParent);
                    activeSlotUIs.Add(ui);
                }
            }

            // Passive slots
            var passivePrefab = passiveSlotPrefab != null ? passiveSlotPrefab : modSlotPrefab;
            if (passivePrefab != null && passiveModParent != null)
            {
                for (int i = 0; i < lastPassiveCount; i++)
                {
                    var ui = Instantiate(passivePrefab, passiveModParent);
                    passiveSlotUIs.Add(ui);
                }
            }
        }

        private void RefreshSlotContents()
        {
            for (int i = 0; i < activeSlotUIs.Count; i++)
            {
                var mod = _modManager.GetActiveMod(i);
                string hint = i < activeSlotHints.Length ? activeSlotHints[i] : "";
                activeSlotUIs[i].Bind(mod, hint);
            }

            for (int i = 0; i < passiveSlotUIs.Count; i++)
            {
                var mod = _modManager.GetPassiveMod(i);
                passiveSlotUIs[i].Bind(mod);
            }
        }

        private void ClearSlots()
        {
            foreach (var ui in activeSlotUIs)
                if (ui != null) Destroy(ui.gameObject);
            activeSlotUIs.Clear();

            foreach (var ui in passiveSlotUIs)
                if (ui != null) Destroy(ui.gameObject);
            passiveSlotUIs.Clear();

            lastActiveCount = 0;
            lastPassiveCount = 0;
        }

        private void SetVisible(bool visible)
        {
            if (panel != null)
                panel.SetActive(visible);
        }

        #endregion
    }
}