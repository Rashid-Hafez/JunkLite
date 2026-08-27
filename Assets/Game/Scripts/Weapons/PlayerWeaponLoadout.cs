using System;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Player-owned weapon equipment state. This component owns slot contents,
    /// their paired world pickups, attachment, visibility, and break removal.
    /// Attack timing and execution remain the responsibility of WeaponManager.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerWeaponLoadout : MonoBehaviour
    {
        [Header("Weapon Attachment")]
        [SerializeField] private Transform weaponHolder;

        private Rigidbody ownerRigidbody;
        private WeaponInstance weaponSlot1;
        private WeaponInstance weaponSlot2;
        private WorldWeaponPickup storedPickup1;
        private WorldWeaponPickup storedPickup2;

        public WeaponInstance WeaponSlot1 => weaponSlot1;
        public WeaponInstance WeaponSlot2 => weaponSlot2;
        public bool HasAnyWeapon => weaponSlot1 != null || weaponSlot2 != null;
        public bool HasEmptySlot => weaponSlot1 == null || weaponSlot2 == null;

        public event Action WeaponChanged;
        public event Action<int> WeaponBroken;

        /// <summary>
        /// Supports existing player prefabs while the component is being serialized
        /// onto the current player prefab. New configuration belongs here.
        /// </summary>
        public void ApplyDefaultsIfMissing(Transform fallbackWeaponHolder)
        {
            if (weaponHolder == null)
                weaponHolder = fallbackWeaponHolder;
        }

        public void Initialize(Rigidbody ownerBody)
        {
            ownerRigidbody = ownerBody;
        }

        public WeaponInstance GetWeapon(int slot)
        {
            return slot switch
            {
                1 => weaponSlot1,
                2 => weaponSlot2,
                _ => null
            };
        }

        private WorldWeaponPickup GetStoredPickup(int slot)
        {
            return slot switch
            {
                1 => storedPickup1,
                2 => storedPickup2,
                _ => null
            };
        }

        public bool TryEquipWeapon(
            int slot,
            WorldWeaponPickup pickup,
            Vector3 displacedWeaponDropPosition,
            bool weaponsVisible)
        {
            if (!IsValidSlot(slot) || pickup == null || pickup.weaponInstance == null)
                return false;

            // Replacement is one atomic loadout change. The displaced weapon still
            // returns to its paired pickup, but consumers refresh only after the new
            // slot content is installed.
            if (GetWeapon(slot) != null &&
                !TryDropWeaponInternal(slot, displacedWeaponDropPosition, false))
                return false;

            WeaponInstance weapon = pickup.weaponInstance;
            bool isRanged = weapon.weaponData is RangedWeaponData;

            pickup.gameObject.SetActive(false);
            weapon.gameObject.SetActive(true);
            weapon.transform.SetParent(isRanged ? transform : ResolveWeaponHolder(), false);

            SpriteRenderer rootRenderer = weapon.GetComponent<SpriteRenderer>();
            if (rootRenderer != null)
                rootRenderer.sortingOrder = 11;

            weapon.SetOwnerRigidbody(ownerRigidbody);
            ApplySocketTransform(weapon, isRanged);
            SetSlot(slot, weapon, pickup);
            SetWeaponVisible(weapon, weaponsVisible);
            WeaponChanged?.Invoke();
            return true;
        }

        public bool TryDropWeapon(int slot, Vector3 dropPosition)
        {
            return TryDropWeaponInternal(slot, dropPosition, true);
        }

        public bool TrySwapSlots()
        {
            if (weaponSlot1 == weaponSlot2)
                return false;

            (weaponSlot1, weaponSlot2) = (weaponSlot2, weaponSlot1);
            (storedPickup1, storedPickup2) = (storedPickup2, storedPickup1);

            weaponSlot1?.ResetCombo();
            weaponSlot2?.ResetCombo();
            WeaponChanged?.Invoke();
            return true;
        }

        public void SetWeaponsVisible(bool visible)
        {
            SetWeaponVisible(weaponSlot1, visible);
            SetWeaponVisible(weaponSlot2, visible);
        }

        private bool TryDropWeaponInternal(int slot, Vector3 dropPosition, bool notify)
        {
            if (!IsValidSlot(slot))
                return false;

            WeaponInstance weapon = GetWeapon(slot);
            WorldWeaponPickup pickup = GetStoredPickup(slot);
            if (weapon == null || pickup == null)
                return false;

            UnsubscribeFromBrokenWeapon(weapon);
            weapon.transform.SetParent(pickup.transform, false);
            weapon.gameObject.SetActive(false);

            pickup.transform.position = dropPosition;
            pickup.gameObject.SetActive(true);
            ClearSlot(slot);

            if (notify)
                WeaponChanged?.Invoke();

            return true;
        }

        private void SetSlot(int slot, WeaponInstance weapon, WorldWeaponPickup pickup)
        {
            if (slot == 1)
            {
                weaponSlot1 = weapon;
                storedPickup1 = pickup;
            }
            else
            {
                weaponSlot2 = weapon;
                storedPickup2 = pickup;
            }

            weapon.OnWeaponBroken -= HandleEquippedWeaponBroken;
            weapon.OnWeaponBroken += HandleEquippedWeaponBroken;
        }

        private void ClearSlot(int slot)
        {
            if (slot == 1)
            {
                weaponSlot1 = null;
                storedPickup1 = null;
            }
            else
            {
                weaponSlot2 = null;
                storedPickup2 = null;
            }
        }

        private void HandleEquippedWeaponBroken()
        {
            int brokenSlot = weaponSlot1 != null && weaponSlot1.IsBroken
                ? 1
                : weaponSlot2 != null && weaponSlot2.IsBroken
                    ? 2
                    : 0;

            if (brokenSlot == 0)
                return;

            WeaponInstance weapon = GetWeapon(brokenSlot);
            UnsubscribeFromBrokenWeapon(weapon);
            weapon.gameObject.SetActive(false);
            ClearSlot(brokenSlot);

            WeaponChanged?.Invoke();
            WeaponBroken?.Invoke(brokenSlot);
        }

        private void ApplySocketTransform(WeaponInstance weapon, bool isRanged)
        {
            if (isRanged)
            {
                weapon.transform.localPosition = Vector3.zero;
                weapon.transform.localRotation = Quaternion.identity;
                weapon.transform.localScale = Vector3.one;
                return;
            }

            if (weapon.weaponData == null)
            {
                weapon.transform.localPosition = Vector3.zero;
                weapon.transform.localRotation = Quaternion.Euler(0f, 0f, -30f);
                weapon.transform.localScale = Vector3.one;
                return;
            }

            WeaponData.WeaponSocketOffset socketOffset = weapon.weaponData.socketOffset;
            weapon.transform.localPosition = socketOffset.localPositionOffset;
            weapon.transform.localRotation =
                Quaternion.Euler(0f, 0f, -30f) *
                Quaternion.Euler(socketOffset.localRotationOffsetEuler);

            Vector3 weaponScale = Vector3.one;
            if (socketOffset.flipLocalScaleX) weaponScale.x = -1f;
            if (socketOffset.flipLocalScaleY) weaponScale.y = -1f;
            weapon.transform.localScale = weaponScale;
        }

        private Transform ResolveWeaponHolder()
        {
            return weaponHolder != null ? weaponHolder : transform;
        }

        private static bool IsValidSlot(int slot)
        {
            return slot == 1 || slot == 2;
        }

        private static void SetWeaponVisible(WeaponInstance weapon, bool visible)
        {
            if (weapon == null)
                return;

            if (weapon.weaponData is RangedWeaponData)
                visible = false;

            SpriteRenderer[] renderers = weapon.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (SpriteRenderer renderer in renderers)
            {
                if (renderer != null)
                    renderer.enabled = visible;
            }
        }

        private void UnsubscribeFromBrokenWeapon(WeaponInstance weapon)
        {
            if (weapon != null)
                weapon.OnWeaponBroken -= HandleEquippedWeaponBroken;
        }

        private void OnDestroy()
        {
            if (weaponSlot1 != null)
                weaponSlot1.OnWeaponBroken -= HandleEquippedWeaponBroken;
            if (weaponSlot2 != null && weaponSlot2 != weaponSlot1)
                weaponSlot2.OnWeaponBroken -= HandleEquippedWeaponBroken;
        }
    }
}
