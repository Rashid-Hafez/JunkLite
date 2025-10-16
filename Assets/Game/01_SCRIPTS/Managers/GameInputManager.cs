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
        public event Action OnRoll = delegate { }; // <-- renamed from DownSlam

        public event Action OnDroneAttack = delegate { }; // <-- new event for drone attack

        public Vector2 MoveDirection { get; private set; }
        public bool IsAttackHeld { get; private set; }

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
            controls.Player.Move.canceled += _ =>
            {
                MoveDirection = Vector2.zero;
                OnMove(MoveDirection);
            };

            // === JUMP (Press Only) ===
            controls.Player.Jump.performed += _ => OnJump();

            // === ATTACK (tap/hold) ===
            controls.Player.Attack.performed += _ =>
            {
                IsAttackHeld = true;
                OnAttack();
            };
            controls.Player.Attack.canceled += _ => IsAttackHeld = false;

            // === DASH (Press Only) ===
            controls.Player.Dash.performed += _ => OnDash();

            // === ROLL (Press Only) ===
            controls.Player.Roll.performed += _ => OnRoll(); // <-- new action name
        }

        void OnEnable() => controls.Enable();
        void OnDisable() => controls.Disable();
    }
}
