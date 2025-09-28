using UnityEngine;

namespace junklite
{
    [DisallowMultipleComponent]
    public class AnimationController : MonoBehaviour
    {
        [Header("Animator")]
        [SerializeField] private Animator animator;
        [SerializeField] private bool autoFindAnimator = true;

        [Header("Animator Parameters (create these in your Animator Controller)")]
        [SerializeField] private string speedParam = "Speed";        // float
        [SerializeField] private string isGroundedParam = "IsGrounded";   // bool
        [SerializeField] private string isJumpingParam = "IsJumping";    // bool
        [SerializeField] private string isDashingParam = "IsDashing";    // bool
        [SerializeField] private string isAttackingParam = "IsAttacking";  // bool
        [SerializeField] private string isStunnedParam = "IsStunned";    // bool

        [Header("Triggers (optional)")]
        [SerializeField] private string landTrigger = "Land";
        [SerializeField] private string attackTrigger = "Attack";
        [SerializeField] private string dashTrigger = "Dash";
        [SerializeField] private string hurtTrigger = "Hurt";
        [SerializeField] private string deathTrigger = "Death";

        [Header("Tuning")]
        [Tooltip("If true, speed uses sqrt(x^2+z^2). If false, abs(x) only.")]
        [SerializeField] private bool includeZInSpeed = false;
        [Tooltip("Damp time for speed parameter (seconds).")]
        [SerializeField] private float speedDamp = 0.1f;

        // Hashes
        int hSpeed, hGrounded, hJumping, hDashing, hAttacking, hStunned;
        int hLandTrig, hAttackTrig, hDashTrig, hHurtTrig, hDeathTrig;

        // Refs (prefer CharacterBase as context hub)
        CharacterBase ctx;
        CharacterState state;
        Character2D5Controller controller;

        bool wasGrounded;

        void Awake()
        {
            if (autoFindAnimator && animator == null)
                animator = GetComponentInChildren<Animator>();

            ctx = GetComponentInParent<CharacterBase>();
            if (ctx != null)
            {
                state = ctx.State;
                controller = ctx.Controller;
            }
            else
            {
                state = GetComponentInParent<CharacterState>();
                controller = GetComponentInParent<Character2D5Controller>();
            }

            if (animator == null)
                Debug.LogError($"[{nameof(AnimationController)}] No Animator found on {name}.");

            // Cache hashes
            hSpeed = Animator.StringToHash(speedParam);
            hGrounded = Animator.StringToHash(isGroundedParam);
            hJumping = Animator.StringToHash(isJumpingParam);
            hDashing = Animator.StringToHash(isDashingParam);
            hAttacking = Animator.StringToHash(isAttackingParam);
            hStunned = Animator.StringToHash(isStunnedParam);

            hLandTrig = string.IsNullOrEmpty(landTrigger) ? 0 : Animator.StringToHash(landTrigger);
            hAttackTrig = string.IsNullOrEmpty(attackTrigger) ? 0 : Animator.StringToHash(attackTrigger);
            hDashTrig = string.IsNullOrEmpty(dashTrigger) ? 0 : Animator.StringToHash(dashTrigger);
            hHurtTrig = string.IsNullOrEmpty(hurtTrigger) ? 0 : Animator.StringToHash(hurtTrigger);
            hDeathTrig = string.IsNullOrEmpty(deathTrigger) ? 0 : Animator.StringToHash(deathTrigger);
        }

        void OnEnable()
        {
            if (state != null)
            {
                state.OnGroundedChanged += OnGroundedChanged;
                state.OnDashingChanged += OnDashingChanged;
                state.OnAttackingChanged += OnAttackingChanged;
                state.OnStunnedChanged += OnStunnedChanged;
                state.OnDeath += OnDeath;
                wasGrounded = state.IsGrounded;
            }
        }

        void OnDisable()
        {
            if (state != null)
            {
                state.OnGroundedChanged -= OnGroundedChanged;
                state.OnDashingChanged -= OnDashingChanged;
                state.OnAttackingChanged -= OnAttackingChanged;
                state.OnStunnedChanged -= OnStunnedChanged;
                state.OnDeath -= OnDeath;
            }
        }

        void Update()
        {
            if (animator == null || state == null || controller == null) return;

            // Speed (damped)
            float speed = includeZInSpeed
                ? new Vector2(controller.Velocity.x, controller.Velocity.z).magnitude
                : Mathf.Abs(controller.Velocity.x);
            animator.SetFloat(hSpeed, speed, speedDamp, Time.deltaTime);

            // State flags
            animator.SetBool(hGrounded, state.IsGrounded);
            animator.SetBool(hJumping, !state.IsGrounded); // simple jump flag (refine with y vel if you like)
            animator.SetBool(hDashing, state.IsDashing);
            animator.SetBool(hAttacking, state.IsAttacking);
            animator.SetBool(hStunned, state.IsStunned);
        }

        // ---- Event-driven one-shots / edges ----

        void OnGroundedChanged(bool grounded)
        {
            if (grounded && !wasGrounded && hLandTrig != 0)
                animator.SetTrigger(hLandTrig);
            wasGrounded = grounded;
        }

        void OnDashingChanged(bool dashing)
        {
            if (dashing && hDashTrig != 0)
                animator.SetTrigger(hDashTrig);
        }

        void OnAttackingChanged(bool attacking)
        {
            if (attacking && hAttackTrig != 0)
                animator.SetTrigger(hAttackTrig);
        }

        void OnStunnedChanged(bool stunned)
        {
            if (stunned && hHurtTrig != 0)
                animator.SetTrigger(hHurtTrig);
        }

        void OnDeath()
        {
            if (hDeathTrig != 0)
                animator.SetTrigger(hDeathTrig);
        }
    }
}
