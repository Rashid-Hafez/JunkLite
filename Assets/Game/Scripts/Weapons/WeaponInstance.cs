using UnityEngine;
using System.Collections.Generic;

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

        // =====================================================================
        // TIMING STATE
        // =====================================================================

        // Cooldown: time since attack ended, compared against attackCooldown
        private float cooldownTimer = 0f;
        private bool onCooldown = false;

        // Combo: time since attack ended, compared against comboWindow
        private int sideComboIndex = 0;
        private int airComboIndex = 0;
        private float comboTimer = 0f;
        private bool comboActive = false;

        private Rigidbody ownerRb;

        // Mod system
        private readonly List<ActiveMod> activeMods = new();
        public System.Action OnModsChanged;

        // =====================================================================
        // PUBLIC STATE
        // =====================================================================

        public int CurrentComboIndex => sideComboIndex;

        public int BonusAirAttacks
        {
            get
            {
                int bonus = 0;
                for (int i = 0; i < activeMods.Count; i++)
                {
                    var mod = activeMods[i]?.data;
                    if (mod != null)
                        bonus += mod.bonusAirAttacks;
                }
                return bonus;
            }
        }

        /// <summary>
        /// True if attack cooldown has expired and player can attack.
        /// </summary>
        public bool CanAttack => !onCooldown;

        public float CooldownRemaining => onCooldown ? Mathf.Max(0, AttackCooldown - cooldownTimer) : 0f;
        public float ComboTimeRemaining => comboActive ? Mathf.Max(0, ComboWindow - comboTimer) : 0f;

        // Timing values from WeaponData (with safe defaults)
        private float AttackCooldown => weaponData != null ? weaponData.attackCooldown : 0.2f;
        private float ComboWindow => weaponData != null ? weaponData.comboWindow : 0.5f;

        // =====================================================================
        // UNITY LIFECYCLE
        // =====================================================================

        private void Start()
        {
            if (weaponData == null)
            {
                Debug.LogError($"WeaponInstance '{name}' missing WeaponData!");
                return;
            }

            baseDamage = weaponData.baseDamage;
            baseAttackSpeed = weaponData.baseAttackSpeed;

            // Validate timing configuration
            if (weaponData.comboWindow <= weaponData.attackCooldown)
            {
                Debug.LogWarning($"[WeaponInstance] '{weaponData.displayName}': comboWindow ({weaponData.comboWindow}s) must be > attackCooldown ({weaponData.attackCooldown}s)! Combo input window is {weaponData.ComboInputWindow}s");
            }
        }

        private void Update()
        {
            if (!onCooldown && !comboActive)
                return;

            float dt = Time.deltaTime;

            // Both timers run in parallel after attack ends
            if (onCooldown)
            {
                cooldownTimer += dt;
                if (cooldownTimer >= AttackCooldown)
                {
                    onCooldown = false;
                    Log($"Cooldown ended ({AttackCooldown}s) - can attack");
                }
            }

            if (comboActive)
            {
                comboTimer += dt;
                if (comboTimer >= ComboWindow)
                {
                    Log($"Combo window expired ({ComboWindow}s) - resetting combo from {sideComboIndex} to 0");
                    ResetCombo();
                }
            }
        }

        public void SetOwnerRigidbody(Rigidbody rb)
        {
            ownerRb = rb;
        }

        // =====================================================================
        // COMBO STEP RETRIEVAL
        // =====================================================================

        /// <summary>
        /// Gets the current combo step and animation name.
        /// Caller should check CanAttack before calling this.
        /// </summary>
        public bool TryGetComboStep(AttackDirection dir, bool isGrounded, out WeaponData.ComboStep step, out int comboIndex, out string animationName)
        {
            step = default;
            comboIndex = -1;
            animationName = null;

            if (weaponData == null)
                return false;

            // Stop combo timer while attacking (restarts when attack completes)
            comboActive = false;

            if (dir == AttackDirection.Side)
            {
                var combo = isGrounded ? weaponData.sideCombo : weaponData.airSideCombo;
                if (combo == null || combo.Length == 0)
                    return false;

                if (isGrounded)
                {
                    if (sideComboIndex >= combo.Length)
                        sideComboIndex = 0;

                    comboIndex = sideComboIndex;
                    step = combo[sideComboIndex];
                    animationName = step.animationName;
                    Log($"Side attack - combo {sideComboIndex + 1}/{combo.Length}, anim: '{animationName}'");
                }
                else
                {
                    if (airComboIndex >= combo.Length)
                        airComboIndex = 0;

                    comboIndex = airComboIndex;
                    step = combo[airComboIndex];
                    animationName = step.animationName;
                    Log($"Air side attack - combo {airComboIndex + 1}/{combo.Length}, anim: '{animationName}'");
                }
            }
            else
            {
                // Up/Down attacks break combo
                if (sideComboIndex > 0 || airComboIndex > 0)
                {
                    Log($"{dir} attack - breaking combo from {sideComboIndex}");
                    ResetCombo();
                }

                step = dir == AttackDirection.Up ? weaponData.upAttack : weaponData.downAttack;
                animationName = step.animationName;

                Log($"{dir} attack, anim: '{animationName}'");
            }

            return !string.IsNullOrEmpty(animationName);
        }

        // =====================================================================
        // ATTACK CALLBACKS
        // =====================================================================

        /// <summary>
        /// Called when attack animation completes.
        /// Starts BOTH cooldown and combo window timers.
        /// </summary>
        public void OnAttackComplete(AttackDirection dir, bool wasGrounded)
        {
            // Advance combo for side attacks
            if (dir == AttackDirection.Side)
                AdvanceCombo(wasGrounded);

            // Start cooldown timer
            cooldownTimer = 0f;
            onCooldown = true;

            // Start combo window timer (runs in parallel)
            comboTimer = 0f;
            comboActive = true;

            Log($"Attack complete - cooldown: {AttackCooldown}s, combo window: {ComboWindow}s (input window: {weaponData.ComboInputWindow}s), next combo: {sideComboIndex}");
        }

        /// <summary>
        /// Called when attack is interrupted (dash, stun, etc).
        /// No cooldown penalty - player can act immediately.
        /// </summary>
        public void OnAttackInterrupted()
        {
            Log($"Attack interrupted - no cooldown, resetting combo");
            onCooldown = false;
            ResetCombo();
        }

        private void AdvanceCombo(bool wasGrounded)
        {
            var combo = wasGrounded ? weaponData?.sideCombo : weaponData?.airSideCombo;
            if (combo == null || combo.Length == 0)
                return;

            if (wasGrounded)
            {
                sideComboIndex++;
                if (sideComboIndex >= combo.Length)
                {
                    Log($"Combo finished! Wrapping to 0");
                    sideComboIndex = 0;
                }
            }
            else
            {
                airComboIndex++;
                if (airComboIndex >= combo.Length)
                {
                    Log($"Air combo finished! Wrapping to 0");
                    airComboIndex = 0;
                }
            }
        }

        private void ResetCombo()
        {
            sideComboIndex = 0;
            airComboIndex = 0;
            comboTimer = 0f;
            comboActive = false;
        }

        // =====================================================================
        // MOD SYSTEM
        // =====================================================================

        public int MaxModSlots => weaponData != null ? weaponData.maxActiveModSlots : 0;
        public bool HasFreeSlot => activeMods.Count < MaxModSlots;
        public IReadOnlyList<ActiveMod> GetMods() => activeMods;

        public bool TryAddMod(ModData modData)
        {
            if (modData == null || !HasFreeSlot)
                return false;

            var activeMod = new ActiveMod(modData);
            activeMods.Add(activeMod);

            modData.OnEquip(this);
            OnModsChanged?.Invoke();

            Debug.Log($"[Weapon] Mod added: {modData.modName}");
            return true;
        }

        public bool TryAddActiveMod(ActiveMod mod)
        {
            if (mod == null || !HasFreeSlot)
                return false;

            activeMods.Add(mod);
            mod.data.OnEquip(this);
            OnModsChanged?.Invoke();

            Debug.Log($"[Weapon] Mod equipped: {mod.data.modName}");
            return true;
        }

        public void RemoveMod(ActiveMod mod)
        {
            if (!activeMods.Contains(mod))
                return;

            mod.data.OnUnequip(this);
            activeMods.Remove(mod);
            OnModsChanged?.Invoke();

            Debug.Log($"[Weapon] Mod removed: {mod.data.modName}");
        }

        public void TriggerModsOnHit(EnemyCharacter enemy, PlayerCharacter player)
        {
            for (int i = activeMods.Count - 1; i >= 0; i--)
            {
                var mod = activeMods[i];
                bool effectUsed = mod.data.OnHit(this, enemy, player);

                if (effectUsed)
                {
                    mod.ConsumeDurability(mod.data.durabilityPerHit);

                    if (mod.IsBroken)
                    {
                        Debug.Log($"[Weapon] Mod broke: {mod.data.modName}");
                        RemoveMod(mod);
                    }
                }
            }

            OnModsChanged?.Invoke();
        }

        private void Log(string message)
        {
            if (logCombo)
                Debug.Log($"[WeaponInstance] {message}", this);
        }
    }

    public enum AttackHitResult
    {
        None,
        Enemy,
        Environment
    }
}