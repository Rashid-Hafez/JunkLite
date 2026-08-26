using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace junklite
{
    [DisallowMultipleComponent]
    public class HorizBotPanelUI : MonoBehaviour
    {
        [System.Serializable]
        private class ModSlotReference
        {
            [SerializeField] private GameObject root;
            [SerializeField] private Image iconImage;

            public GameObject Root => root;
            public Image IconImage => iconImage;
        }

        [Header("Auto-Bind")]
        [FormerlySerializedAs("autoBindToGameManager")]
        [SerializeField] private bool autoBindToPlayerLifecycle = true;

        [Header("Panel")]
        [SerializeField] private GameObject panel;

        [Header("Weapon Slots")]
        [SerializeField] private WeaponSlotUI slot1;
        [SerializeField] private WeaponSlotUI slot2;

        [Header("Active Mod Slots")]
        [SerializeField] private ModSlotReference mod1;
        [SerializeField] private ModSlotReference mod2;
        [SerializeField] private ModSlotReference mod3;
        [SerializeField] private ModSlotReference mod4;

        private PlayerCharacter player;
        private WeaponManager weaponManager;
        private ModManager modManager;
        private PlayerLifecycle subscribedPlayerLifecycle;

        private ModSlotReference[] modSlots;

        private void Awake()
        {
            modSlots = new[] { mod1, mod2, mod3, mod4 };
        }

        private void OnEnable()
        {
            RebindPlayerLifecycle();

            if (GameInputManager.Instance != null)
            {
                GameInputManager.Instance.OnWeapon1Attack += RefreshWeapons;
                GameInputManager.Instance.OnWeapon2Attack += RefreshWeapons;
            }

            Refresh();
        }

        private void Start()
        {
            RebindPlayerLifecycle();
        }

        private void OnDisable()
        {
            UnsubscribeFromPlayerLifecycle();

            if (GameInputManager.Instance != null)
            {
                GameInputManager.Instance.OnWeapon1Attack -= RefreshWeapons;
                GameInputManager.Instance.OnWeapon2Attack -= RefreshWeapons;
            }

            Unbind();
        }

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

        public void BindToPlayer(PlayerCharacter target)
        {
            Unbind();

            player = target;
            if (player == null)
            {
                Refresh();
                return;
            }

            weaponManager = player.GetComponent<WeaponManager>();
            modManager = player.GetComponent<ModManager>();

            if (weaponManager != null)
            {
                weaponManager.OnCombatModeChanged += Refresh;
                weaponManager.OnWeaponChanged += RefreshWeapons;
            }

            if (modManager != null)
                modManager.OnModSlotsChanged += RefreshMods;

            Refresh();
        }

        public void Unbind()
        {
            if (weaponManager != null)
            {
                weaponManager.OnCombatModeChanged -= Refresh;
                weaponManager.OnWeaponChanged -= RefreshWeapons;
            }

            if (modManager != null)
                modManager.OnModSlotsChanged -= RefreshMods;

            player = null;
            weaponManager = null;
            modManager = null;

            SetVisible(false);
            ClearWeapons();
            ClearMods();
        }

        private void HandlePlayerSpawned(PlayerCharacter newPlayer)
        {
            BindToPlayer(newPlayer);
        }

        private void Refresh()
        {
            bool show = weaponManager != null && weaponManager.IsModCombat;
            SetVisible(show);

            if (!show)
            {
                ClearWeapons();
                ClearMods();
                return;
            }

            RefreshWeapons();
            RefreshMods();
        }

        private void RefreshWeapons()
        {
            if (weaponManager == null) return;

            if (slot1 != null)
            {
                var weapon1 = weaponManager.WeaponSlot1;
                if (weapon1 != null)
                    slot1.Bind(weapon1, true);
                else
                    slot1.SetContentActive(false);

                slot1.SetActive(weaponManager.ActiveWeapon != null && weaponManager.ActiveWeapon == weaponManager.WeaponSlot1);
            }

            if (slot2 != null)
            {
                var weapon2 = weaponManager.WeaponSlot2;
                if (weapon2 != null)
                    slot2.Bind(weapon2, true);
                else
                    slot2.SetContentActive(false);

                slot2.SetActive(weaponManager.ActiveWeapon != null && weaponManager.ActiveWeapon == weaponManager.WeaponSlot2);
            }
        }

        private void RefreshMods()
        {
            if (modSlots == null) return;

            for (int i = 0; i < modSlots.Length; i++)
            {
                var slotRef = modSlots[i];
                if (slotRef == null) continue;

                bool unlocked = modManager != null && i < modManager.UnlockedActiveSlots;
                if (slotRef.Root != null)
                    slotRef.Root.SetActive(unlocked);

                if (!unlocked)
                {
                    SetSlotIcon(slotRef, null);
                    continue;
                }

                ModInstance mod = modManager.GetActiveMod(i);
                Sprite icon = mod != null && !mod.IsBroken && mod.Data != null ? mod.Data.icon : null;
                SetSlotIcon(slotRef, icon);
            }
        }

        private void SetSlotIcon(ModSlotReference slotRef, Sprite icon)
        {
            if (slotRef.IconImage == null) return;

            slotRef.IconImage.sprite = icon;
            slotRef.IconImage.enabled = icon != null;
        }

        private void ClearWeapons()
        {
            if (slot1 != null)
            {
                slot1.SetContentActive(false);
                slot1.SetActive(false);
            }

            if (slot2 != null)
            {
                slot2.SetContentActive(false);
                slot2.SetActive(false);
            }
        }

        private void ClearMods()
        {
            if (modSlots == null) return;

            for (int i = 0; i < modSlots.Length; i++)
            {
                var slotRef = modSlots[i];
                if (slotRef == null) continue;

                if (slotRef.Root != null)
                    slotRef.Root.SetActive(false);

                SetSlotIcon(slotRef, null);
            }
        }

        private void SetVisible(bool visible)
        {
            if (panel != null)
                panel.SetActive(visible);
        }
    }
}
