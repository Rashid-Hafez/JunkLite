using UnityEngine;
using UnityEngine.InputSystem;
using System;

namespace junklite
{
    [DefaultExecutionOrder(1)]
    public class GameInputManager : MonoBehaviour
    {
        public static GameInputManager Instance { get; private set; }
        private InputSystem_Actions controls;

        // Gameplay events (gated by IsGameplayInputEnabled)
        public event Action<Vector2> OnMove = delegate { };
        public event Action OnJump = delegate { };
        public event Action OnJumpReleased = delegate { };
        public event Action OnAttack = delegate { };
        public event Action OnDash = delegate { };
        public event Action OnRoll = delegate { };

        // UI events (always active)
        public event Action OnInventoryToggle = delegate { };

        public Vector2 MoveDirection { get; private set; }
        public bool IsAttackHeld { get; private set; }
        public bool IsJumpHeld { get; private set; }

        /// <summary>
        /// When false, gameplay inputs (move, jump, attack, dash, roll) are blocked.
        /// UI inputs like inventory toggle remain active.
        /// </summary>
        public bool IsGameplayInputEnabled { get; private set; } = true;

        /// <summary>
        /// Enable or disable gameplay inputs. Use when opening menus/inventory.
        /// </summary>
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

            // === MOVE ===
            controls.Player.Move.performed += ctx =>
            {
                if (!IsGameplayInputEnabled) return; // Block input entirely
                MoveDirection = ctx.ReadValue<Vector2>();
                OnMove(MoveDirection);
            };
            controls.Player.Move.canceled += _ =>
            {
                if (!IsGameplayInputEnabled) return; // Block input entirely
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

            // === INVENTORY TOGGLE (Always active - UI input) ===
            controls.Player.Inventory.performed += _ => OnInventoryToggle();
        }

        void OnEnable() => controls.Enable();
        void OnDisable() => controls.Disable();
    }
}