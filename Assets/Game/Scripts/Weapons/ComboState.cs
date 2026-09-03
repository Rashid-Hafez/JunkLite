using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Tracks combo progression and attack cooldowns for a WeaponData.
    /// Plain C# class — no MonoBehaviour. Call Tick() each frame.
    /// Used by WeaponInstance for world weapons and directly by WeaponManager for fists.
    ///
    /// CombatState is deliberately type-agnostic. It only needs combo length and animation
    /// names from the weapon data, accessed through the abstract WeaponData interface.
    /// The actual step data (MeleeComboStep, RangedComboStep etc) is fetched separately
    /// by WeaponManager after it has the resolved combo index.
    /// </summary>
    public class CombatState
    {
        private readonly WeaponData data;

        private int sideComboIndex;
        private int airComboIndex;
        private float comboTimer;
        private bool comboActive;

        private float cooldownTimer;
        private bool onCooldown;

        private float attackCooldown = 0.15f;
        private float comboWindow = 0.6f;

        #region Properties

        public WeaponData Data => data;
        public int CurrentComboIndex => sideComboIndex;
        public bool CanAttack => !onCooldown;
        public float CooldownRemaining => onCooldown ? Mathf.Max(0, AttackCooldown - cooldownTimer) : 0f;
        public float ComboTimeRemaining => comboActive ? Mathf.Max(0, ComboWindow - comboTimer) : 0f;

        public float AttackCooldown
        {
            get => attackCooldown;
            set => attackCooldown = Mathf.Max(0f, value);
        }

        public float ComboWindow
        {
            get => comboWindow;
            set => comboWindow = Mathf.Max(0f, value);
        }

        #endregion

        public CombatState(WeaponData weaponData)
        {
            data = weaponData;
        }

        public CombatState(WeaponData weaponData, float attackCooldown, float comboWindow)
        {
            data = weaponData;
            this.attackCooldown = Mathf.Max(0f, attackCooldown);
            this.comboWindow = Mathf.Max(0f, comboWindow);
        }

        public void SetTiming(float cooldown, float window)
        {
            AttackCooldown = cooldown;
            ComboWindow = window;
        }

        #region Tick

        public void Tick(float dt)
        {
            if (!onCooldown && !comboActive) return;

            if (onCooldown)
            {
                cooldownTimer += dt;
                if (cooldownTimer >= AttackCooldown)
                {
                    onCooldown = false;
                }
            }

            if (comboActive)
            {
                comboTimer += dt;
                if (comboTimer >= ComboWindow)
                {
                    ResetCombo();
                }
            }
        }

        #endregion

        #region Combo

        /// <summary>
        /// Resolves the current combo index and animation name for the given attack direction.
        /// Does NOT advance the index — call OnAttackComplete to advance.
        /// WeaponManager uses the returned comboIndex to separately fetch the typed step data
        /// from the weapon's concrete subclass (TryGetMeleeStep, TryGetRangedStep, etc).
        /// </summary>
        public bool TryBeginAttack(AttackDirection dir, bool isGrounded, WeaponData weaponData,
            out int comboIndex, out string animationName)
        {
            comboIndex = -1;
            animationName = null;
            if (weaponData == null) return false;

            comboActive = false;

            if (dir == AttackDirection.Side)
            {
                int comboLength = weaponData.GetComboLength(dir, isGrounded);
                if (comboLength == 0) return false;

                // Option B: allow the first air side attack after a grounded side hit
                // to continue the ground combo index instead of restarting the air chain.
                bool continuingFromGround =
                    !isGrounded &&
                    comboActive &&          // combo window is still open
                    sideComboIndex > 0;     // we have progressed at least one step on ground

                if (continuingFromGround)
                {
                    if (sideComboIndex >= comboLength) sideComboIndex = 0;
                    comboIndex = sideComboIndex;
                    airComboIndex = sideComboIndex; // sync air track to continuation point
                }
                else if (isGrounded)
                {
                    if (sideComboIndex >= comboLength) sideComboIndex = 0;
                    comboIndex = sideComboIndex;
                }
                else
                {
                    if (airComboIndex >= comboLength) airComboIndex = 0;
                    comboIndex = airComboIndex;
                }
            }
            else
            {
                // Directional attacks (Up/Down) break any active side combo
                if (sideComboIndex > 0 || airComboIndex > 0)
                {
                    ResetCombo();
                }
                comboIndex = 0;
            }

            return weaponData.TryGetAnimationName(dir, comboIndex, isGrounded, out animationName);
        }

        /// <summary>
        /// Called when an attack animation completes. Advances the combo index and starts
        /// the cooldown + combo window timers.
        /// </summary>
        public void OnAttackComplete(AttackDirection dir, bool wasGrounded, WeaponData weaponData)
        {
            if (dir == AttackDirection.Side)
                AdvanceCombo(wasGrounded, weaponData);

            cooldownTimer = 0f;
            onCooldown = true;
            comboTimer = 0f;
            comboActive = true;
        }

        public void OnAttackInterrupted()
        {
            onCooldown = false;
            ResetCombo();
        }

        public void ResetCombo()
        {
            sideComboIndex = 0;
            airComboIndex = 0;
            comboTimer = 0f;
            comboActive = false;
        }

        private void AdvanceCombo(bool wasGrounded, WeaponData weaponData)
        {
            int comboLength = weaponData?.GetComboLength(AttackDirection.Side, wasGrounded) ?? 0;
            if (comboLength == 0) return;

            if (wasGrounded)
            {
                sideComboIndex++;
                if (sideComboIndex >= comboLength)
                    sideComboIndex = 0;
            }
            else
            {
                airComboIndex++;
                if (airComboIndex >= comboLength)
                    airComboIndex = 0;
            }
        }

        #endregion
    }
}
