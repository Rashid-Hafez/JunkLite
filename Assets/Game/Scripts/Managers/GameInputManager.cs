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

        [Header("Gamepad Tuning (deadzone/actuation)")]
        [SerializeField] private float gamepadDeadzone = 0.15f;
        [SerializeField] private float gamepadActuation = 0.6f;
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
        public event Action OnPauseToggle = delegate { };
        public event Action<bool> OnInputDeviceChanged = delegate { };


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
                0 => GetBindingHint(controls.Player.ModActivate1),
                1 => GetBindingHint(controls.Player.ModActivate2),
                2 => GetBindingHint(controls.Player.ModActivate3),
                3 => GetBindingHint(controls.Player.ModActivate4),
                _ => ""
            };
        }

        public string ResolveBindingTokens(string text)
        {
            if (string.IsNullOrEmpty(text) || controls == null)
                return text;

            const string tokenPrefix = "{input:";
            int searchIndex = 0;

            while (searchIndex < text.Length)
            {
                int tokenStart = text.IndexOf(tokenPrefix, searchIndex, StringComparison.OrdinalIgnoreCase);
                if (tokenStart < 0)
                    break;

                int tokenEnd = text.IndexOf('}', tokenStart + tokenPrefix.Length);
                if (tokenEnd < 0)
                    break;

                int actionNameStart = tokenStart + tokenPrefix.Length;
                string actionName = text.Substring(actionNameStart, tokenEnd - actionNameStart).Trim();
                InputAction action = controls.asset.FindAction(actionName, throwIfNotFound: false);
                string bindingHint = GetBindingHint(action);

                if (string.IsNullOrEmpty(bindingHint))
                {
                    searchIndex = tokenEnd + 1;
                    continue;
                }

                text = text.Remove(tokenStart, tokenEnd - tokenStart + 1)
                    .Insert(tokenStart, bindingHint);
                searchIndex = tokenStart + bindingHint.Length;
            }

            return text;
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

        private string GetBindingHint(InputAction action)
        {
            if (action == null)
                return string.Empty;

            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isComposite)
                    continue;

                string path = binding.effectivePath;
                bool matchesDevice = IsUsingGamepad
                    ? IsGamepadBinding(path)
                    : IsKeyboardAndMouseBinding(path);
                if (string.IsNullOrEmpty(path) || !matchesDevice)
                    continue;

                return action.GetBindingDisplayString(i);
            }

            return action.GetBindingDisplayString();
        }

        private static bool IsGamepadBinding(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return path.IndexOf("Gamepad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("Joystick", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsKeyboardAndMouseBinding(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return path.IndexOf("Keyboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf("Mouse", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void HandleActionChange(object changedObject, InputActionChange change)
        {
            if (change != InputActionChange.ActionPerformed ||
                changedObject is not InputAction action ||
                action.actionMap?.asset != controls?.asset ||
                !IsMeaningfulDeviceInput(action))
            {
                return;
            }

            SetInputDevice(action.activeControl?.device);
        }

        private bool IsMeaningfulDeviceInput(InputAction action)
        {
            InputDevice device = action.activeControl?.device;
            if (device is not Gamepad && device is not Joystick)
                return true;

            if (action.type != InputActionType.Value)
                return true;

            if (string.Equals(action.expectedControlType, "Vector2", StringComparison.Ordinal))
                return action.ReadValue<Vector2>().sqrMagnitude >= gamepadDeadzone * gamepadDeadzone;

            if (string.Equals(action.expectedControlType, "Axis", StringComparison.Ordinal))
                return Mathf.Abs(action.ReadValue<float>()) >= gamepadDeadzone;

            return true;
        }

        private void SetInputDevice(InputDevice device)
        {
            bool? useGamepad = null;
            if (device is Gamepad || device is Joystick)
                useGamepad = true;
            else if (device is Keyboard || device is Mouse)
                useGamepad = false;

            if (!useGamepad.HasValue || IsUsingGamepad == useGamepad.Value)
                return;

            IsUsingGamepad = useGamepad.Value;
            OnInputDeviceChanged(IsUsingGamepad);
        }

        // -----------------------------------------------------------------------

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // This component may share the persistent GameRoot object with
                // GameManager and other services. Only remove the duplicate service.
                enabled = false;
                Destroy(this);
                return;
            }
            Instance = this;
            // DontDestroyOnLoad(gameObject);

            controls = new InputSystem_Actions();

            // ===================================================================
            // PLAYER ACTION MAP
            // ===================================================================

            // === MOVE ===
            controls.Player.Move.performed += ctx =>
            {
                if (!IsGameplayInputEnabled) return;

                Vector2 raw = ctx.ReadValue<Vector2>();
                if (raw.sqrMagnitude >= gamepadDeadzone * gamepadDeadzone)
                    SetInputDevice(ctx.control?.device);

                // Apply hard actuation cut for gamepad/joystick devices so analogue sticks either on or off
                bool isGamepadInput = ctx.control?.device is Gamepad || ctx.control?.device is Joystick;
                MoveDirection = InputHelpers.ApplyDeadzoneAndActuation(raw, gamepadDeadzone, gamepadActuation, isGamepadInput);
                OnMove(MoveDirection);
            };
            controls.Player.Move.canceled += ctx =>
            {
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
            controls.Player.Attack.performed += ctx =>
            {
                if (!IsGameplayInputEnabled) return;
                if (IsDevConsoleClick(ctx)) return;
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
                parryAction.performed += _ =>
                {
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
                if (!IsGameplayInputEnabled) return;
                OnInteract();
            };

            // === INVENTORY TOGGLE (Always active - UI input) ===
            controls.Player.Inventory.performed += _ => OnInventoryToggle();
            controls.Player.Pause.performed += _ => OnPauseToggle();

            // === COMBAT MODE TOGGLE ===
            controls.Player.CombatMode.performed += _ =>
            {
                if (!IsGameplayInputEnabled) return;
                OnCombatModeToggle();
            };

            // === WEAPON 1 ATTACK ===
            controls.Player.Weapon1Attack.performed += ctx =>
            {
                if (!IsGameplayInputEnabled) return;
                if (IsDevConsoleClick(ctx)) return;
                OnWeapon1Attack();
            };

            // === WEAPON 2 ATTACK ===
            controls.Player.Weapon2Attack.performed += ctx =>
            {
                if (!IsGameplayInputEnabled) return;
                if (IsDevConsoleClick(ctx)) return;
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
                OnUINavigate(ctx.ReadValue<Vector2>());
            };

            controls.UI.Submit.performed += ctx =>
            {
                OnUISubmit();
            };

            controls.UI.Cancel.performed += ctx =>
            {
                OnUICancel();
            };


        }

        private static bool IsDevConsoleClick(InputAction.CallbackContext context)
        {
#if UNITY_EDITOR
            return context.control?.device is Mouse && EditorRuntimeDevToolsPanel.ContainsPointer();
#else
            return false;
#endif
        }

        void OnEnable()
        {
            if (controls != null)
            {
                InputSystem.onActionChange += HandleActionChange;
                // Start with Player map active, UI map disabled
                controls.Player.Enable();
                controls.UI.Disable();
            }
        }

        void OnDisable()
        {
            InputSystem.onActionChange -= HandleActionChange;
            if (controls != null)
                controls.Disable();
        }
    }
}
