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

        private bool logCombo;

        #region Properties

        public WeaponData Data => data;
        public int CurrentComboIndex => sideComboIndex;
        public bool CanAttack => !onCooldown;
        public float CooldownRemaining => onCooldown ? Mathf.Max(0, AttackCooldown - cooldownTimer) : 0f;
        public float ComboTimeRemaining => comboActive ? Mathf.Max(0, ComboWindow - comboTimer) : 0f;

        private float AttackCooldown => data != null ? data.attackCooldown : 0.2f;
        private float ComboWindow => data != null ? data.comboWindow : 0.5f;

        #endregion

        public CombatState(WeaponData weaponData, bool enableLogging = false)
        {
            data = weaponData;
            logCombo = enableLogging;
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
                    Log($"Cooldown ended ({AttackCooldown}s)");
                }
            }

            if (comboActive)
            {
                comboTimer += dt;
                if (comboTimer >= ComboWindow)
                {
                    Log($"Combo window expired ({ComboWindow}s) - resetting from {sideComboIndex}");
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

                if (isGrounded)
                {
                    if (sideComboIndex >= comboLength) sideComboIndex = 0;
                    comboIndex = sideComboIndex;
                    Log($"Side attack - combo {sideComboIndex + 1}/{comboLength}");
                }
                else
                {
                    if (airComboIndex >= comboLength) airComboIndex = 0;
                    comboIndex = airComboIndex;
                    Log($"Air side attack - combo {airComboIndex + 1}/{comboLength}");
                }
            }
            else
            {
                // Directional attacks (Up/Down) break any active side combo
                if (sideComboIndex > 0 || airComboIndex > 0)
                {
                    Log($"{dir} attack - breaking combo from {sideComboIndex}");
                    ResetCombo();
                }
                comboIndex = 0;
                Log($"{dir} attack");
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

            int nextIndex = wasGrounded ? sideComboIndex : airComboIndex;
            Log($"Attack complete - cooldown: {AttackCooldown}s, combo window: {ComboWindow}s, next index: {nextIndex}");
        }

        public void OnAttackInterrupted()
        {
            Log("Attack interrupted - no cooldown");
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

        private void Log(string msg)
        {
            if (logCombo) Debug.Log($"[CombatState] {msg}");
        }
    }
}