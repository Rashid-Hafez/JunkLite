using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Enemy-facing wrapper around Unity Animator parameters.
    /// States should call this via EnemyCharacter.Anim instead of talking to Animator directly.
    ///
    /// Optional: can subscribe to StateMachine.OnStateChanged and drive baseline animations automatically.
    /// </summary>
    public class EnemyAnimationController : MonoBehaviour
    {
        [Header("Animator")]
        [SerializeField] private Animator animator;
        [SerializeField] private bool autoFindAnimator = true;

        [Header("Parameter Names (convention)")]
        [SerializeField] private string speedParam = "Speed";
        [SerializeField] private string isMovingParam = "IsMoving";
        [SerializeField] private string chargeTrigger = "Charge";
        [SerializeField] private string dashTrigger = "Dash";
        [SerializeField] private string hurtTrigger = "Hurt";
        [SerializeField] private string dieTrigger = "Die";

        [Header("Optional: Drive from StateMachine transitions")]
        [SerializeField] private bool driveFromStateMachine = false;
        [SerializeField] private List<StateToAnimation> stateMappings = new List<StateToAnimation>();

        [Serializable]
        public class StateToAnimation
        {
            [Tooltip("Matches IState.GetType().Name, e.g. ChargeState, DashState, DeadState")]
            public string stateTypeName;

            [Tooltip("Animator trigger to fire on Enter of this state (optional).")]
            public string trigger;

            [Tooltip("Animator bool to set while in this state (optional).")]
            public string boolParam;

            [Tooltip("Value for boolParam while in this state.")]
            public bool boolValue = true;
        }

        private readonly Dictionary<string, int> hashCache = new Dictionary<string, int>();
        private StateMachine stateMachine;
        private IState lastState;

        private void Awake()
        {
            if (autoFindAnimator && animator == null)
                animator = GetComponentInChildren<Animator>(true);

            if (driveFromStateMachine)
                stateMachine = GetComponentInParent<StateMachine>();
        }

        private void OnEnable()
        {
            if (driveFromStateMachine && stateMachine != null)
                stateMachine.OnStateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (driveFromStateMachine && stateMachine != null)
                stateMachine.OnStateChanged -= HandleStateChanged;
        }

        private void HandleStateChanged(IState from, IState to)
        {
            lastState = to;
            if (to == null) return;

            string toName = to.GetType().Name;
            for (int i = 0; i < stateMappings.Count; i++)
            {
                var map = stateMappings[i];
                if (string.IsNullOrWhiteSpace(map.stateTypeName)) continue;
                if (!string.Equals(map.stateTypeName, toName, StringComparison.Ordinal)) continue;

                if (!string.IsNullOrWhiteSpace(map.boolParam))
                    SetBool(map.boolParam, map.boolValue);

                if (!string.IsNullOrWhiteSpace(map.trigger))
                    Trigger(map.trigger);
            }
        }

        public bool HasAnimator => animator != null;

        public void SetFloat(string paramName, float value)
        {
            if (animator == null || string.IsNullOrWhiteSpace(paramName)) return;
            animator.SetFloat(Hash(paramName), value);
        }

        public void SetBool(string paramName, bool value)
        {
            if (animator == null || string.IsNullOrWhiteSpace(paramName)) return;
            animator.SetBool(Hash(paramName), value);
        }

        public void Trigger(string triggerName)
        {
            if (animator == null || string.IsNullOrWhiteSpace(triggerName)) return;
            animator.SetTrigger(Hash(triggerName));
        }

        // --- Convenience wrappers (keep states clean) ---
        public void SetSpeed(float speed) => SetFloat(speedParam, speed);
        public void SetIsMoving(bool moving) => SetBool(isMovingParam, moving);
        public void TriggerCharge() => Trigger(chargeTrigger);
        public void TriggerDash() => Trigger(dashTrigger);
        public void TriggerHurt() => Trigger(hurtTrigger);
        public void TriggerDie() => Trigger(dieTrigger);

        private int Hash(string paramName)
        {
            if (hashCache.TryGetValue(paramName, out int h)) return h;
            h = Animator.StringToHash(paramName);
            hashCache[paramName] = h;
            return h;
        }
    }
}


