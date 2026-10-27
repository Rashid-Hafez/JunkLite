using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// The player's arm: exactly one current <see cref="WeaponData"/> (fists by default) that is
    /// replaced permanently when a better weapon is acquired. Toggles the authored held visual
    /// under the weapon holder and publishes <see cref="WeaponChanged"/>. Attack timing and
    /// execution stay in <see cref="WeaponManager"/>. Ownership is remembered in
    /// <see cref="PlayerUpgrades"/> when that service exists, so respawns and later sessions keep the upgrade.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerArmament : MonoBehaviour
    {
        [Serializable]
        private struct HeldVisual
        {
            [Tooltip("Weapon this visual belongs to.")]
            public WeaponData weapon;
            [Tooltip("Authored child of the weapon holder, inactive in the prefab. Shown while the weapon is current.")]
            public GameObject visual;
        }

        [Header("Weapons")]
        [Tooltip("The weapon the player spawns with. Fists.")]
        [SerializeField] private WeaponData defaultWeapon;
        [Tooltip("Acquirable weapons, weakest first. On spawn the strongest owned one is restored from the profile.")]
        [SerializeField] private WeaponData[] upgradePath = Array.Empty<WeaponData>();

        [Header("Held Visuals")]
        [SerializeField] private Transform weaponHolder;
        [Tooltip("One authored visual per weapon that shows something in the hand. Fists need none.")]
        [SerializeField] private HeldVisual[] heldVisuals = Array.Empty<HeldVisual>();

        [Header("Weapon Draw Order")]
        [Tooltip("Sorting order of the held visual's root sprite.")]
        [SerializeField] private int weaponSortingOrder = 11;
        [Tooltip("Left/right skeleton player: order while facing left, when the right (weapon) hand is the far arm. " +
                 "Set below the body's order to draw behind it. Same as Weapon Sorting Order = no change.")]
        [SerializeField] private int weaponSortingOrderFacingLeft = 11;

        private Character2D5Controller controller;
        private WeaponData current;
        private GameObject heldVisual;
        private SpriteRenderer[] heldRenderers = Array.Empty<SpriteRenderer>();
        private bool visible = true;
        private bool grappleHidden;
        private readonly HashSet<string> sessionOwned = new();

        /// <summary>The weapon the player attacks with right now. Never null once a default is assigned.</summary>
        public WeaponData Current => current != null ? current : defaultWeapon;
        public WeaponData DefaultWeapon => defaultWeapon;
        public Transform WeaponHolder => weaponHolder != null ? weaponHolder : transform;
        /// <summary>True once the player carries anything better than the default weapon.</summary>
        public bool HasUpgradedWeapon => Current != null && Current != defaultWeapon;

        /// <summary>Raised after <see cref="Current"/> changes (including the restore on spawn).</summary>
        public event Action WeaponChanged;
        /// <summary>Raised when a new weapon is acquired through <see cref="TryAcquire"/>; presentation hooks listen here.</summary>
        public event Action<WeaponData> WeaponAcquired;

        private void Awake()
        {
            controller = GetComponent<Character2D5Controller>();
            foreach (HeldVisual entry in heldVisuals)
                if (entry.visual != null) entry.visual.SetActive(false);
        }

        private void OnEnable()
        {
            if (controller != null)
                controller.FacingChanged += HandleFacingChanged;
            RestoreFromProfile();
        }

        private void OnDisable()
        {
            if (controller != null)
                controller.FacingChanged -= HandleFacingChanged;
        }

        /// <summary>Supports player prefabs that predate the serialized default. New configuration belongs in the Inspector.</summary>
        public void ApplyDefaultsIfMissing(WeaponData fallbackDefault, Transform fallbackHolder)
        {
            if (defaultWeapon == null) defaultWeapon = fallbackDefault;
            if (weaponHolder == null) weaponHolder = fallbackHolder;
            if (current == null) SetCurrent(defaultWeapon, notify: false);
        }

        public bool Owns(WeaponData weapon)
        {
            if (weapon == null) return false;
            if (weapon == defaultWeapon) return true;
            PlayerUpgrades upgrades = PlayerUpgrades.Instance;
            return upgrades != null ? upgrades.OwnsWeapon(weapon) : sessionOwned.Contains(weapon.ProfileId);
        }

        /// <summary>
        /// Permanently upgrades to <paramref name="weapon"/>. Returns false when it is null or
        /// already current. The acquisition is recorded in the profile and <see cref="WeaponAcquired"/> fires once.
        /// </summary>
        public bool TryAcquire(WeaponData weapon)
        {
            if (weapon == null || weapon == Current) return false;

            PlayerUpgrades upgrades = PlayerUpgrades.Instance;
            if (upgrades != null) upgrades.GrantWeapon(weapon);
            else sessionOwned.Add(weapon.ProfileId);

            SetCurrent(weapon, notify: true);
            WeaponAcquired?.Invoke(weapon);
            return true;
        }

        /// <summary>Developer tooling: forget every acquired weapon and return to the default.</summary>
        public void RevertToDefault()
        {
            sessionOwned.Clear();
            PlayerUpgrades.Instance?.RevokeWeapons();
            SetCurrent(defaultWeapon, notify: true);
        }

        /// <summary>Hides the held visual while the sword is thrown by the grapple.</summary>
        public void SetGrappleWeaponHidden(bool hidden)
        {
            if (grappleHidden == hidden) return;
            grappleHidden = hidden;
            ApplyVisibility();
        }

        /// <summary>Follows the player's own renderer visibility (death, cinematics).</summary>
        public void SetVisible(bool isVisible)
        {
            visible = isVisible;
            ApplyVisibility();
        }

        private void RestoreFromProfile()
        {
            WeaponData best = current != null ? current : defaultWeapon;
            if (upgradePath != null)
            {
                for (int i = upgradePath.Length - 1; i >= 0; i--)
                {
                    if (upgradePath[i] != null && Owns(upgradePath[i])) { best = upgradePath[i]; break; }
                }
            }

            SetCurrent(best, notify: current != best);
        }

        private void SetCurrent(WeaponData weapon, bool notify)
        {
            WeaponData next = weapon != null ? weapon : defaultWeapon;
            bool changed = current != next;
            current = next;
            ShowVisualFor(current);
            if (changed && notify) WeaponChanged?.Invoke();
        }

        private void ShowVisualFor(WeaponData weapon)
        {
            heldVisual = null;
            heldRenderers = Array.Empty<SpriteRenderer>();
            foreach (HeldVisual entry in heldVisuals)
            {
                if (entry.visual == null) continue;
                bool match = weapon != null && entry.weapon == weapon;
                entry.visual.SetActive(match);
                if (match) heldVisual = entry.visual;
            }

            if (heldVisual == null) return;
            heldRenderers = heldVisual.GetComponentsInChildren<SpriteRenderer>(true);
            ApplySortingOrder();
            ApplyVisibility();
        }

        private void HandleFacingChanged(bool facingRight) => ApplySortingOrder();

        private void ApplySortingOrder()
        {
            if (heldVisual == null || !heldVisual.TryGetComponent(out SpriteRenderer rootRenderer)) return;
            bool facingLeft = controller != null && !controller.IsFacingRight;
            rootRenderer.sortingOrder = facingLeft ? weaponSortingOrderFacingLeft : weaponSortingOrder;
        }

        private void ApplyVisibility()
        {
            bool show = visible && !grappleHidden;
            foreach (SpriteRenderer renderer in heldRenderers)
                if (renderer != null) renderer.enabled = show;
        }
    }
}
