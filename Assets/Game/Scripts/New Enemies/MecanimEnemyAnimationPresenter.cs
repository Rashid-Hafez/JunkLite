using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Mecanim implementation of the enemy animation presentation boundary.
    /// Animator parameter names and state-to-trigger mapping stay local to this
    /// component; enemy gameplay only emits semantic action phases.
    /// </summary>
    public sealed class MecanimEnemyAnimationPresenter : EnemyAnimationPresenter
    {
        [Header("Animator")]
        [SerializeField] private Animator animator;

        [Header("Continuous Locomotion")]
        [SerializeField] private bool driveMovementSpeed = true;
        [SerializeField] private string speedParameter = "Speed";
        [SerializeField, Min(0f)] private float speedDampTime = 0.08f;
        [Tooltip("If enabled, speed is divided by EnemyMovement.MoveSpeed before being sent to the Animator.")]
        [SerializeField] private bool normalizeMovementSpeed;

        [Header("Melee Triggers")]
        [Tooltip("Optional. Leave empty when the controller has no separate wind-up state.")]
        [SerializeField] private string meleeWindupTrigger = "";
        [SerializeField] private string meleeAttackTrigger = "Attack";

        [Header("Shared State Triggers")]
        [SerializeField] private string stunnedTrigger = "Hurt";
        [SerializeField] private string parriedTrigger = "Hurt";
        [SerializeField] private string deathTrigger = "Die";

        [Header("Optional Action Triggers")]
        [SerializeField] private string chargeTrigger = "Charge";
        [SerializeField] private string dashTrigger = "Dash";
        [SerializeField] private string dodgeTrigger = "Dodge";
        [SerializeField] private string grabTrigger = "Grab";
        [SerializeField] private string recoverTrigger = "Recover";

        private readonly HashSet<int> availableFloatParameters = new();
        private readonly HashSet<int> availableTriggerParameters = new();

        private StateMachine stateMachine;
        private EnemyMovement movement;
        private int speedHash;
        private int meleeWindupHash;
        private int meleeAttackHash;
        private int stunnedHash;
        private int parriedHash;
        private int deathHash;
        private int chargeHash;
        private int dashHash;
        private int dodgeHash;
        private int grabHash;
        private int recoverHash;
        private float previousAnimatorSpeed = 1f;
        private bool playbackPaused;
        private bool isDead;

        private void Awake()
        {
            ResolveReferences();
            CacheParameters();
        }

        private void OnEnable()
        {
            if (stateMachine != null)
                stateMachine.OnStateChanged += HandleStateChanged;
        }

        private void Start()
        {
            StartCoroutine(SyncInitialState());
        }

        private void Update()
        {
            if (!driveMovementSpeed || isDead || animator == null || movement == null)
                return;
            if (!availableFloatParameters.Contains(speedHash))
                return;

            float speed = movement.CurrentSpeed;
            if (normalizeMovementSpeed && movement.MoveSpeed > 0f)
                speed /= movement.MoveSpeed;

            animator.SetFloat(speedHash, speed, speedDampTime, Time.deltaTime);
        }

        private void OnDisable()
        {
            if (stateMachine != null)
                stateMachine.OnStateChanged -= HandleStateChanged;
        }

        public override void PlayMeleeWindup(float gameplayDuration)
        {
            if (isDead)
                return;

            SetTrigger(meleeWindupHash);
        }

        public override void PlayMeleeAttack(float gameplayDuration)
        {
            if (isDead)
                return;

            SetTrigger(meleeAttackHash);
        }

        public override void SetPlaybackPaused(bool paused)
        {
            if (animator == null || playbackPaused == paused)
                return;

            playbackPaused = paused;
            if (paused)
            {
                previousAnimatorSpeed = animator.speed;
                animator.speed = 0f;
            }
            else
            {
                animator.speed = previousAnimatorSpeed;
            }
        }

        private IEnumerator SyncInitialState()
        {
            yield return null;

            if (stateMachine?.CurrentState != null)
                HandleStateChanged(null, stateMachine.CurrentState);
        }

        private void HandleStateChanged(IState from, IState to)
        {
            if (animator == null || to == null)
                return;

            if (to is DeadState)
            {
                isDead = true;
                SetTrigger(deathHash);
                return;
            }

            if (isDead)
                return;

            if (to is StunnedState)
                SetTrigger(stunnedHash);
            else if (to is ParriedState)
                SetTrigger(parriedHash);
            else if (to is ChargeState)
                SetTrigger(chargeHash);
            else if (to is DashState)
                SetTrigger(dashHash);
            else if (to is DodgeState)
                SetTrigger(dodgeHash);
            else if (to is GrabState)
                SetTrigger(grabHash);
            else if (to is RecoverState)
                SetTrigger(recoverHash);
        }

        private void ResolveReferences()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>(true);

            stateMachine = GetComponentInParent<StateMachine>();
            movement = GetComponentInParent<EnemyMovement>();
        }

        private void CacheParameters()
        {
            availableFloatParameters.Clear();
            availableTriggerParameters.Clear();
            if (animator != null)
            {
                foreach (AnimatorControllerParameter parameter in animator.parameters)
                {
                    if (parameter.type == AnimatorControllerParameterType.Float)
                        availableFloatParameters.Add(parameter.nameHash);
                    else if (parameter.type == AnimatorControllerParameterType.Trigger)
                        availableTriggerParameters.Add(parameter.nameHash);
                }
            }

            speedHash = GetHash(speedParameter);
            meleeWindupHash = GetHash(meleeWindupTrigger);
            meleeAttackHash = GetHash(meleeAttackTrigger);
            stunnedHash = GetHash(stunnedTrigger);
            parriedHash = GetHash(parriedTrigger);
            deathHash = GetHash(deathTrigger);
            chargeHash = GetHash(chargeTrigger);
            dashHash = GetHash(dashTrigger);
            dodgeHash = GetHash(dodgeTrigger);
            grabHash = GetHash(grabTrigger);
            recoverHash = GetHash(recoverTrigger);
        }

        private void SetTrigger(int hash)
        {
            if (animator != null && availableTriggerParameters.Contains(hash))
                animator.SetTrigger(hash);
        }

        private static int GetHash(string parameterName)
        {
            return string.IsNullOrWhiteSpace(parameterName)
                ? 0
                : Animator.StringToHash(parameterName);
        }

#if UNITY_EDITOR
        [ContextMenu("Validate Animation Configuration")]
        private void ValidateAnimationConfiguration()
        {
            if (animator == null)
            {
                Debug.LogError($"[{name}] Mecanim presenter requires an Animator.", this);
                return;
            }

            if (animator.runtimeAnimatorController == null)
            {
                Debug.LogError($"[{name}] Mecanim presenter Animator has no controller assigned.", this);
                return;
            }

            ValidateParameter(speedParameter, AnimatorControllerParameterType.Float, driveMovementSpeed);
            ValidateParameter(meleeWindupTrigger, AnimatorControllerParameterType.Trigger, false);
            ValidateParameter(meleeAttackTrigger, AnimatorControllerParameterType.Trigger, false);
            ValidateParameter(stunnedTrigger, AnimatorControllerParameterType.Trigger, false);
            ValidateParameter(parriedTrigger, AnimatorControllerParameterType.Trigger, false);
            ValidateParameter(deathTrigger, AnimatorControllerParameterType.Trigger, false);
            ValidateParameter(chargeTrigger, AnimatorControllerParameterType.Trigger, false);
            ValidateParameter(dashTrigger, AnimatorControllerParameterType.Trigger, false);
            ValidateParameter(dodgeTrigger, AnimatorControllerParameterType.Trigger, false);
            ValidateParameter(grabTrigger, AnimatorControllerParameterType.Trigger, false);
            ValidateParameter(recoverTrigger, AnimatorControllerParameterType.Trigger, false);
        }

        private void ValidateParameter(
            string parameterName,
            AnimatorControllerParameterType expectedType,
            bool required)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                if (required)
                    Debug.LogError($"[{name}] A required {expectedType} Animator parameter is not configured.", this);
                return;
            }

            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.name == parameterName && parameter.type == expectedType)
                    return;
            }

            Debug.LogWarning(
                $"[{name}] Animator controller is missing {expectedType} parameter '{parameterName}'. " +
                "Add it to the controller or clear the unused presenter field.",
                this);
        }

        private void OnValidate()
        {
            if (animator == null)
                animator = GetComponentInChildren<Animator>(true);

            if (animator == null)
            {
                Debug.LogWarning($"[{name}] Mecanim presenter requires an Animator.", this);
                return;
            }

            if (animator.runtimeAnimatorController == null)
                Debug.LogWarning($"[{name}] Mecanim presenter Animator has no controller assigned.", this);
        }
#endif
    }
}
