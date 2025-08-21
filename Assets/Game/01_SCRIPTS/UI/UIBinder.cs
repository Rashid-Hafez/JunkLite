using System.Collections.Generic;
using UnityEngine;

namespace junklite.UI
{
    /// <summary>
    /// Simple UI Binder for CharacterSystem - single source of truth
    /// </summary>
    [DefaultExecutionOrder(4)]
    public class UIBinder : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private PlayerCharacter targetPlayer;

        [Header("UI Bindings")]
        [SerializeField] private UIBinding[] bindings;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs = false;

        private CharacterSystem characterSystem;
        private Dictionary<string, UIBinding> bindingLookup;
        private bool isConnected = false;

        private void Awake()
        {
            // Create binding lookup
            bindingLookup = new Dictionary<string, UIBinding>();
            foreach (var binding in bindings)
            {
                if (!string.IsNullOrEmpty(binding.bindingName))
                {
                    bindingLookup[binding.bindingName] = binding;
                }
            }
        }

        public void SetTarget(PlayerCharacter player)
        {
            // Disconnect from previous target
            if (isConnected)
            {
                DisconnectFromTarget();
            }

            // Connect to new target
            targetPlayer = player;
            if (targetPlayer != null)
            {
                characterSystem = targetPlayer.System;
                if (characterSystem != null)
                {
                    ConnectToTarget();
                }
            }
        }

        private void ConnectToTarget()
        {
            if (characterSystem == null) return;

            // Subscribe to attribute events
            if (characterSystem.Health != null)
                characterSystem.Health.OnValueChanged += OnHealthChanged;

            if (characterSystem.Mana != null)
                characterSystem.Mana.OnValueChanged += OnManaChanged;

            if (characterSystem.Stamina != null)
                characterSystem.Stamina.OnValueChanged += OnStaminaChanged;

            // Subscribe to death
            characterSystem.OnDeath += OnDeath;

            isConnected = true;

            // Initialize UI with current values
            InitializeBindings();

            if (enableDebugLogs)
                Debug.Log($"UI Binder connected to {targetPlayer.name}");
        }

        private void DisconnectFromTarget()
        {
            if (characterSystem != null)
            {
                // Unsubscribe from events
                if (characterSystem.Health != null)
                    characterSystem.Health.OnValueChanged -= OnHealthChanged;

                if (characterSystem.Mana != null)
                    characterSystem.Mana.OnValueChanged -= OnManaChanged;

                if (characterSystem.Stamina != null)
                    characterSystem.Stamina.OnValueChanged -= OnStaminaChanged;

                characterSystem.OnDeath -= OnDeath;
            }

            // Clear UI
            foreach (var binding in bindings)
            {
                if (binding.targetComponent != null)
                {
                    binding.targetComponent.Clear();
                }
            }

            isConnected = false;
        }

        private void InitializeBindings()
        {
            if (characterSystem == null) return;

            // Initialize health
            if (characterSystem.Health != null)
                UpdateBinding("Health", characterSystem.Health.Current, characterSystem.Health.Max);

            // Initialize mana
            if (characterSystem.Mana != null)
                UpdateBinding("Mana", characterSystem.Mana.Current, characterSystem.Mana.Max);

            // Initialize stamina
            if (characterSystem.Stamina != null)
                UpdateBinding("Stamina", characterSystem.Stamina.Current, characterSystem.Stamina.Max);
        }

        // Event handlers
        private void OnHealthChanged(float current, float max)
        {
            UpdateBinding("Health", current, max);
        }

        private void OnManaChanged(float current, float max)
        {
            UpdateBinding("Mana", current, max);
        }

        private void OnStaminaChanged(float current, float max)
        {
            UpdateBinding("Stamina", current, max);
        }

        private void OnDeath()
        {
            UpdateBinding("IsDead", true);
        }

        private void UpdateBinding(string bindingName, float value, float maxValue = 1f)
        {
            if (bindingLookup.TryGetValue(bindingName, out UIBinding binding))
            {
                if (binding.targetComponent != null)
                {
                    if (binding.useCurrentAndMax)
                    {
                        binding.targetComponent.UpdateValue(value, maxValue, binding);
                    }
                    else
                    {
                        float normalizedValue = maxValue > 0 ? value / maxValue : 0f;
                        binding.targetComponent.UpdateValue(normalizedValue, binding);
                    }

                    if (enableDebugLogs)
                        Debug.Log($"Updated {bindingName}: {value}/{maxValue}");
                }
            }
        }

        private void UpdateBinding(string bindingName, bool state)
        {
            if (bindingLookup.TryGetValue(bindingName, out UIBinding binding))
            {
                if (binding.targetComponent != null)
                {
                    binding.targetComponent.UpdateState(state, binding);
                }
            }
        }

        private void OnDestroy()
        {
            DisconnectFromTarget();
        }

        #region Public API - for external scripts that need manual UI updates
        /// <summary>
        /// Manually update a binding with current/max values
        /// </summary>
        public void UpdateCustomBinding(string bindingName, float value, float maxValue = 1f)
        {
            UpdateBinding(bindingName, value, maxValue);
        }

        /// <summary>
        /// Manually update a binding with boolean state
        /// </summary>
        public void UpdateCustomBinding(string bindingName, bool state)
        {
            UpdateBinding(bindingName, state);
        }

        /// <summary>
        /// Check if a binding exists
        /// </summary>
        public bool HasBinding(string bindingName)
        {
            return bindingLookup.ContainsKey(bindingName);
        }

        /// <summary>
        /// Force refresh all bindings
        /// </summary>
        public void RefreshAllBindings()
        {
            if (isConnected)
            {
                InitializeBindings();
            }
        }
        #endregion
    }
}