using UnityEngine;
using UnityEngine.InputSystem;
using System;

namespace junklite
{
    [DefaultExecutionOrder(1)]
    public class GameInputManager : MonoBehaviour
    {
        public static GameInputManager Instance { get; private set; }
        public InputSystem_Actions controls;

        // Gameplay events (gated by IsGameplayInputEnabled)
        public event Action<Vector2> OnMove = delegate { };
        public event Action OnJump = delegate { };
        public event Action OnJumpReleased = delegate { };
        public event Action OnAttack = delegate { };
        public event Action OnDash = delegate { };
        public event Action OnRoll = delegate { };
        public event Action OnSpecialAttack = delegate { };
        public event Action OnParry = delegate { };
        public event Action OnInteract = delegate { };

        // Combat mode events (gated by IsGameplayInputEnabled)
        public event Action OnCombatModeToggle = delegate { };
        public event Action OnWeapon1Attack = delegate { };
        public event Action OnWeapon2Attack = delegate { };
        public event Action OnModActivate1 = delegate { };
        public event Action OnModActivate2 = delegate { };
        public event Action OnModActivate3 = delegate { };
        public event Action OnModActivate4 = delegate { };

        // UI events (always active when UI action map is enabled)
        public event Action OnInventoryToggle = delegate { };
        public event Action<Vector2> OnUINavigate = delegate { };
        public event Action OnUISubmit = delegate { };
        public event Action OnUICancel = delegate { };


        public Vector2 MoveDirection { get; private set; }
        public bool IsAttackHeld { get; private set; }
        public bool IsJumpHeld { get; private set; }

        /// <summary>
        /// When false, gameplay inputs (move, jump, attack, dash, roll) are blocked.
        /// UI inputs like inventory toggle remain active.
        /// </summary>
        public bool IsGameplayInputEnabled { get; private set; } = true;
        public bool IsParryOnlyInputEnabled { get; private set; }
        public bool IsUsingGamepad { get; private set; }

  
        public string GetModActivateHint(int slotIndex)
        {
            return slotIndex switch
            {
                0 => controls.Player.ModActivate1.GetBindingDisplayString(),
                1 => controls.Player.ModActivate2.GetBindingDisplayString(),
                2 => controls.Player.ModActivate3.GetBindingDisplayString(),
                3 => controls.Player.ModActivate4.GetBindingDisplayString(),
                _ => ""
            };
        }
        public void SetGameplayInputEnabled(bool enabled)
        {
            IsGameplayInputEnabled = enabled;

            // Clear held states when disabling
            if (!enabled)
            {
                MoveDirection = Vector2.zero;
                IsAttackHeld = false;
                IsJumpHeld = false;

                // Notify listeners that movement stopped
                OnMove(Vector2.zero);
            }
        }

        public void SetParryOnlyInputEnabled(bool enabled)
        {
            IsParryOnlyInputEnabled = enabled;
            if (enabled)
                SetGameplayInputEnabled(false);
        }


        public void SwitchToUIActionMap()
        {
            controls.Player.Disable();
            // Keep inventory toggle available while UI map is active
            // so keyboard I / controller Select-Touchpad can close inventory.
            controls.Player.Inventory.Enable();
            controls.UI.Enable();

            // Clear held states
            MoveDirection = Vector2.zero;
            IsAttackHeld = false;
            IsJumpHeld = false;
        }

        /// <summary>
        /// Switch back to the Player action map. Disables UI actions.
        /// </summary>
        public void SwitchToPlayerActionMap()
        {
            controls.UI.Disable();
            controls.Player.Enable();
        }

        private void TrackInputDevice(InputAction.CallbackContext ctx)
        {
            var device = ctx.control?.device;
            if (device == null) return;

            IsUsingGamepad = device is Gamepad || device is Joystick;
        }

        // -----------------------------------------------------------------------

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            controls = new InputSystem_Actions();

            // ===================================================================
            // PLAYER ACTION MAP
            // ===================================================================

            // === MOVE ===
            controls.Player.Move.performed += ctx =>
            {
                TrackInputDevice(ctx);
                if (!IsGameplayInputEnabled) return;
                MoveDirection = ctx.ReadValue<Vector2>();
                OnMove(MoveDirection);
            };
            controls.Player.Move.canceled += ctx =>
            {
                TrackInputDevice(ctx);
                if (!IsGameplayInputEnabled) return;
                MoveDirection = Vector2.zero;
                OnMove(MoveDirection);
            };

            // === JUMP ===
            controls.Player.Jump.performed += _ =>
            {
                if (!IsGameplayInputEnabled) return;
                OnJump();
                IsJumpHeld = true;
            };

            controls.Player.Jump.canceled += _ =>
            {
                if (!IsGameplayInputEnabled) return;
                IsJumpHeld = false;
                OnJumpReleased();
            };

            // === ATTACK (tap/hold) ===
            controls.Player.Attack.performed += _ =>
            {
                if (!IsGameplayInputEnabled) return;
                IsAttackHeld = true;
                OnAttack();
            };
            controls.Player.Attack.canceled += _ =>
            {
                if (!IsGameplayInputEnabled) return;
                IsAttackHeld = false;
            };

            // === DASH (Press Only) ===
            controls.Player.Dash.performed += _ =>
            {
                if (!IsGameplayInputEnabled) return;
                OnDash();
            };

            // === ROLL (Press Only) ===
            controls.Player.Roll.performed += _ =>
            {
                if (!IsGameplayInputEnabled) return;
                OnRoll();
            };

            // === SPECIAL ATTACK (Press Only) ===
            controls.Player.SpecialAttack.performed += _ =>
            {
                if (!IsGameplayInputEnabled) return;
                OnSpecialAttack();
            };

            // === PARRY (Press Only) ===
            var parryAction = controls.FindAction("Parry", throwIfNotFound: false);
            if (parryAction != null)
            {
                Debug.Log("[Input] Parry action found and bound");
                parryAction.performed += ctx =>
                {
                    Debug.Log("[Input] Parry performed, gameplay enabled? " + IsGameplayInputEnabled + ", parry only? " + IsParryOnlyInputEnabled);
                    if (!IsGameplayInputEnabled && !IsParryOnlyInputEnabled) return;
                    OnParry();
                };
            }
            else
            {
                Debug.LogWarning("[Input] Parry action not found on controls. Make sure the input asset defines it.");
            }

            // === INTERACT ===z
            // You must add an "Interact" action to the Player action map in your Input Actions asset.
            // Bind it to the F key (or your preferred key).
            controls.Player.Interact.performed += _ =>
            {
                Debug.Log("[Input] on interact was performed");
                if (!IsGameplayInputEnabled) return;
                OnInteract();
            };

            // === INVENTORY TOGGLE (Always active - UI input) ===
            controls.Player.Inventory.performed += ctx =>
            {
                TrackInputDevice(ctx);
                OnInventoryToggle();
            };

            // === COMBAT MODE TOGGLE ===
            controls.Player.CombatMode.performed += _ =>
            {
                if (!IsGameplayInputEnabled) return;
                OnCombatModeToggle();
            };

            // === WEAPON 1 ATTACK ===
            controls.Player.Weapon1Attack.performed += _ =>
            {
                if (!IsGameplayInputEnabled) return;
                OnWeapon1Attack();
            };

            // === WEAPON 2 ATTACK ===
            controls.Player.Weapon2Attack.performed += _ =>
            {
                if (!IsGameplayInputEnabled) return;
                OnWeapon2Attack();
            };

            // === MOD ACTIVATIONS ===
            controls.Player.ModActivate1.performed += _ =>
            {
                if (!IsGameplayInputEnabled) return;
                OnModActivate1();
            };

            controls.Player.ModActivate2.performed += _ =>
            {
                if (!IsGameplayInputEnabled) return;
                OnModActivate2();
            };

            controls.Player.ModActivate3.performed += _ =>
            {
                if (!IsGameplayInputEnabled) return;
                OnModActivate3();
            };

            controls.Player.ModActivate4.performed += _ =>
            {
                if (!IsGameplayInputEnabled) return;
                OnModActivate4();
            };

            // ===================================================================
            // UI ACTION MAP
            // ===================================================================

            controls.UI.Navigate.performed += ctx =>
            {
                TrackInputDevice(ctx);
                OnUINavigate(ctx.ReadValue<Vector2>());
            };

            controls.UI.Submit.performed += ctx =>
            {
                TrackInputDevice(ctx);
                OnUISubmit();
            };

            controls.UI.Cancel.performed += ctx =>
            {
                TrackInputDevice(ctx);
                OnUICancel();
            };


        }

        void OnEnable()
        {
            if (controls != null)
            {
                // Start with Player map active, UI map disabled
                controls.Player.Enable();
                controls.UI.Disable();
            }
        }

        void OnDisable()
        {
            if (controls != null)
                controls.Disable();
        }
    }
}