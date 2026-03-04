using UnityEngine;

namespace junklite
{
    public class WeaponInstance : MonoBehaviour
    {
        [Header("Data")]
        public WeaponData weaponData;

        [Header("Runtime Stats")]
        public float baseDamage;
        public float baseAttackSpeed;

        [Header("Debug")]
        [SerializeField] private bool logCombo = false;

        #region State

        private CombatState combatState;

        private float currentDurability;
        public float CurrentDurability => currentDurability;
        public float MaxDurability => weaponData != null ? weaponData.maxWeaponDurability : 0f;
        public bool IsBroken => currentDurability <= 0f;
        public event System.Action OnWeaponBroken;

        /// <summary>
        /// Set true at runtime by mods/abilities to override per-step piercing defaults.
        /// Set back to false when the effect expires.
        /// Only meaningful on melee weapons — ranged weapons ignore this.
        /// </summary>
        private bool piercingOverride;
        public bool PiercingOverride
        {
            get => piercingOverride;
            set => piercingOverride = value;
        }

        #endregion

        #region Public Accessors

        public CombatState Combat => combatState;
        public int CurrentComboIndex => combatState?.CurrentComboIndex ?? 0;
        public bool CanAttack => combatState != null && combatState.CanAttack;
        public float CooldownRemaining => combatState?.CooldownRemaining ?? 0f;
        public float ComboTimeRemaining => combatState?.ComboTimeRemaining ?? 0f;

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            if (weaponData == null)
            {
                Debug.LogError($"WeaponInstance '{name}' missing WeaponData!");
                return;
            }

            baseDamage = weaponData.baseDamage;
            baseAttackSpeed = weaponData.baseAttackSpeed;
            currentDurability = weaponData.maxWeaponDurability;
            combatState = new CombatState(weaponData, logCombo);

            if (weaponData.comboWindow <= weaponData.attackCooldown)
            {
                Debug.LogWarning($"[WeaponInstance] '{weaponData.displayName}': comboWindow " +
                                 $"({weaponData.comboWindow}s) must be > attackCooldown ({weaponData.attackCooldown}s)!");
            }
        }

        private void Update()
        {
            combatState?.Tick(Time.deltaTime);
        }

        #endregion

        #region Combo Delegation

        /// <summary>
        /// Delegates to CombatState.TryBeginAttack. Returns the resolved combo index and
        /// animation name. WeaponManager uses the index to separately fetch the typed step.
        /// </summary>
        public bool TryBeginAttack(AttackDirection dir, bool isGrounded, out int comboIndex, out string animationName)
        {
            comboIndex = -1;
            animationName = null;
            if (combatState == null || weaponData == null) return false;
            return combatState.TryBeginAttack(dir, isGrounded, weaponData, out comboIndex, out animationName);
        }

        public void OnAttackComplete(AttackDirection dir, bool wasGrounded)
        {
            combatState?.OnAttackComplete(dir, wasGrounded, weaponData);
        }

        public void OnAttackInterrupted()
        {
            combatState?.OnAttackInterrupted();
        }

        public void ResetCombo()
        {
            combatState?.ResetCombo();
        }

        public void SetOwnerRigidbody(Rigidbody rb) { }

        #endregion

        #region Durability

        public bool ConsumeDurability()
        {
            if (IsBroken || weaponData == null) return false;

            currentDurability = Mathf.Max(0f, currentDurability - weaponData.durabilityPerHit);

            if (IsBroken)
            {
                Debug.Log($"[WeaponInstance] Weapon broke: {weaponData.displayName}");
                OnWeaponBroken?.Invoke();
                return true;
            }

            return false;
        }

        #endregion
    }

    public enum AttackHitResult
    {
        None,
        Enemy,
        Environment
    }
}