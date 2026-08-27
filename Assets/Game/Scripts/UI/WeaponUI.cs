using UnityEngine;
namespace junklite
{
    /// <summary>
    /// Weapon slot 1 is always visible: fists in regular mode, weapon in mod combat.
    /// Weapon slot 2 only appears in mod combat.
    /// </summary>
    public class WeaponUI : MonoBehaviour
    {
        #region Fields
        [Header("Weapon Slots (scene references, not prefabs)")]
        [SerializeField] private WeaponSlotUI slot1;
        [SerializeField] private WeaponSlotUI slot2;
        private WeaponManager _manager;
        private PlayerWeaponLoadout _loadout;
        #endregion
        #region Bind / Unbind
        public void Bind(WeaponManager manager)
        {
            Unbind();
            _manager = manager;
            _loadout = manager != null ? manager.Loadout : null;
            if (_manager != null)
            {
                _manager.OnCombatModeChanged += Refresh;
                _manager.OnEnemyHit += OnEnemyHitHandler;
            }
            if (_loadout != null)
                _loadout.WeaponChanged += Refresh;
            Refresh();
        }
        public void Unbind()
        {
            if (_manager != null)
            {
                _manager.OnCombatModeChanged -= Refresh;
                _manager.OnEnemyHit -= OnEnemyHitHandler;
            }
            if (_loadout != null)
                _loadout.WeaponChanged -= Refresh;
            _manager = null;
            _loadout = null;
            // Reset to default state
            if (slot1 != null) slot1.SetContentActive(false);
            if (slot2 != null) slot2.gameObject.SetActive(false);
        }
        #endregion
        #region Refresh
        public void Refresh()
        {
            if (_manager == null || _loadout == null) return;
            if (_manager.IsModCombat)
                RefreshModCombat();
            else
                RefreshRegular();
            UpdateActiveIndicators();
        }
        private void RefreshRegular()
        {
            // Slot 1: fist icon, no durability
            if (slot1 != null)
            {
                var fistData = _manager.FistWeaponData;
                if (fistData != null)
                    slot1.BindIcon(fistData.icon);
                else
                    slot1.SetContentActive(false);
            }
            // Slot 2: hidden entirely
            if (slot2 != null)
                slot2.gameObject.SetActive(false);
        }
        private void RefreshModCombat()
        {
            // Slot 1: weapon 1 with durability, or empty content if no weapon
            if (slot1 != null)
            {
                var weapon1 = _loadout.WeaponSlot1;
                if (weapon1 != null)
                    slot1.Bind(weapon1, true);
                else
                    slot1.SetContentActive(false);
            }
            // Slot 2: root active, content depends on weapon equipped
            if (slot2 != null)
            {
                slot2.gameObject.SetActive(true);
                var weapon2 = _loadout.WeaponSlot2;
                if (weapon2 != null)
                    slot2.Bind(weapon2, true);
                else
                    slot2.SetContentActive(false);
            }
        }
        private void OnEnemyHitHandler(EnemyCharacter _, float __) => UpdateActiveIndicators();
        private void UpdateActiveIndicators()
        {
            if (_manager == null || _loadout == null) return;
            bool inModCombat = _manager.IsModCombat;
            var active = _manager.ActiveWeapon;
            if (slot1 != null)
                slot1.SetActive(inModCombat && active != null && active == _loadout.WeaponSlot1);
            if (slot2 != null)
                slot2.SetActive(inModCombat && active != null && active == _loadout.WeaponSlot2);
        }
        #endregion
    }
}
