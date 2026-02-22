using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Tracks combo progression and attack cooldowns for a WeaponData.
    /// Plain C# class — no MonoBehaviour. Call Tick() each frame.
    /// Used by WeaponInstance for world weapons and directly by WeaponManager for fists.
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

        public bool TryGetComboStep(AttackDirection dir, bool isGrounded, out WeaponData.ComboStep step, out int comboIndex, out string animationName)
        {
            step = default;
            comboIndex = -1;
            animationName = null;

            if (data == null) return false;

            comboActive = false;

            if (dir == AttackDirection.Side)
            {
                var combo = isGrounded ? data.sideCombo : data.airSideCombo;
                if (combo == null || combo.Length == 0) return false;

                if (isGrounded)
                {
                    if (sideComboIndex >= combo.Length) sideComboIndex = 0;
                    comboIndex = sideComboIndex;
                    step = combo[sideComboIndex];
                    animationName = step.animationName;
                    Log($"Side attack - combo {sideComboIndex + 1}/{combo.Length}");
                }
                else
                {
                    if (airComboIndex >= combo.Length) airComboIndex = 0;
                    comboIndex = airComboIndex;
                    step = combo[airComboIndex];
                    animationName = step.animationName;
                    Log($"Air side attack - combo {airComboIndex + 1}/{combo.Length}");
                }
            }
            else
            {
                if (sideComboIndex > 0 || airComboIndex > 0)
                {
                    Log($"{dir} attack - breaking combo from {sideComboIndex}");
                    ResetCombo();
                }

                step = dir == AttackDirection.Up ? data.upAttack : data.downAttack;
                animationName = step.animationName;
                Log($"{dir} attack");
            }

            return !string.IsNullOrEmpty(animationName);
        }

        public void OnAttackComplete(AttackDirection dir, bool wasGrounded)
        {
            if (dir == AttackDirection.Side)
                AdvanceCombo(wasGrounded);

            cooldownTimer = 0f;
            onCooldown = true;
            comboTimer = 0f;
            comboActive = true;

            Log($"Attack complete - cooldown: {AttackCooldown}s, combo window: {ComboWindow}s, next: {sideComboIndex}");
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

        private void AdvanceCombo(bool wasGrounded)
        {
            var combo = wasGrounded ? data?.sideCombo : data?.airSideCombo;
            if (combo == null || combo.Length == 0) return;

            if (wasGrounded)
            {
                sideComboIndex++;
                if (sideComboIndex >= combo.Length)
                    sideComboIndex = 0;
            }
            else
            {
                airComboIndex++;
                if (airComboIndex >= combo.Length)
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