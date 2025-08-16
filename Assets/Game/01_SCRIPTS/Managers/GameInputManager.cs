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

        public event Action<Vector2> OnMove = delegate { };
        public event Action OnJump = delegate { };
        public event Action OnAttack = delegate { };
        public event Action OnDash = delegate { };

        public Vector2 MoveDirection { get; private set; }
        public bool IsJumpPressed { get; private set; }
        public bool IsAttackHeld { get; private set; }
        public bool IsDashPressed { get; private set; }

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
                MoveDirection = ctx.ReadValue<Vector2>();
                OnMove(MoveDirection);
            };
            controls.Player.Move.canceled += ctx =>
            {
                MoveDirection = Vector2.zero;
                OnMove(MoveDirection);
            };

            // === JUMP ===
            controls.Player.Jump.performed += _ =>
            {
                IsJumpPressed = true;
                OnJump();
            };
            controls.Player.Jump.canceled += _ => IsJumpPressed = false;

            // === ATTACK ===
            controls.Player.Attack.performed += _ =>
            {
                IsAttackHeld = true;
                OnAttack();
            };
            controls.Player.Attack.canceled += _ => IsAttackHeld = false;

            // === DASH ===
            controls.Player.Dash.performed += _ =>
            {
                IsDashPressed = true;
                OnDash();
            };
            controls.Player.Dash.canceled += _ => IsDashPressed = false;
        }

        void OnEnable() => controls.Enable();
        void OnDisable() => controls.Disable();
    }
}