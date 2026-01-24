using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Attach to Player. Listens to gameplay/state events and plays SFX through AudioManager.
    ///
    /// How it works:
    /// - Jump/DoubleJump/etc come from Character2D5Controller events
    /// - Attack woosh comes from PlayerState.OnAttackingChanged (fires when SetAttacking(true) is called)
    /// - All SFX ultimately go through AudioManager.PlaySpatial(entry, this AudioSource)
    /// </summary>

    [DefaultExecutionOrder(6)]
    [RequireComponent(typeof(AudioSource))]
    public class PlayerAudioHandler : MonoBehaviour
    {
        [Header("3D Sound Settings")]
        [SerializeField] private float minDistance = 1f;
        [SerializeField] private float maxDistance = 20f;

        private Character2D5Controller controller;
        private PlayerState state;
        private WeaponManager weaponManager;
        private AudioManager audioManager;
        private AudioSource source;

        void Awake()
        {
            controller = GetComponent<Character2D5Controller>();
            state = GetComponent<PlayerState>();
            weaponManager = GetComponentInParent<WeaponManager>() ?? GetComponent<WeaponManager>();
            source = GetComponent<AudioSource>();

            // Configure for 3D spatial sound
            source.playOnAwake = false;
            source.spatialBlend = 0f; 
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.rolloffMode = AudioRolloffMode.Linear;
        }

        void Start()
        {
            audioManager = AudioManager.Instance;

            // Assign mixer group
            if (audioManager != null && audioManager.SFXGroup != null)
                source.outputAudioMixerGroup = audioManager.SFXGroup;
        }

        void OnEnable()
        {
            if (controller != null)
            {
                // Movement SFX (controller-driven)
                controller.OnJumpStarted += OnJump;
                controller.OnDoubleJumpPerformed += OnDoubleJump;
                controller.OnWallJumped += OnWallJump;
                controller.OnFallEnded += OnLand;
                controller.OnDashStarted += OnDash;
                controller.OnWallSlideChanged += OnWallSlide;
            }

            if (state != null)
            {
                state.OnDeath += OnDeath;
                // Attack woosh: plays when attack actually starts (SetAttacking(true)), not just on input press.
                state.OnAttackingChanged += OnAttackingChanged;
            }

            if (weaponManager != null)
            {
                // Hit confirm SFX (only when damage is actually dealt)
                weaponManager.OnEnemyHit += OnEnemyHit;
            }
        }

        void OnDisable()
        {
            if (controller != null)
            {
                controller.OnJumpStarted -= OnJump;
                controller.OnDoubleJumpPerformed -= OnDoubleJump;
                controller.OnWallJumped -= OnWallJump;
                controller.OnFallEnded -= OnLand;
                controller.OnDashStarted -= OnDash;
                controller.OnWallSlideChanged -= OnWallSlide;
            }

            if (state != null)
            {
                state.OnDeath -= OnDeath;
                state.OnAttackingChanged -= OnAttackingChanged;
            }

            if (weaponManager != null)
            {
                weaponManager.OnEnemyHit -= OnEnemyHit;
            }
        }

        // Public methods for manual triggering
        public void PlayHurt() => Play(audioManager?.Player?.hurt);
        public void PlayAttack() => Play(GetVariantOrFallback(audioManager?.Player?.attackVariants, audioManager?.Player?.attack));
        public void PlayFootstep() => Play(GetVariantOrFallback(audioManager?.Player?.footstepVariants, audioManager?.Player?.footstep));

        // Event handlers
        private void OnJump() => Play(GetVariantOrFallback(audioManager?.Player?.jumpVariants, audioManager?.Player?.jump));
        private void OnDoubleJump() => Play(audioManager?.Player?.doubleJump);
        private void OnWallJump() => Play(audioManager?.Player?.wallJump);
        // Landing SFX is separate from jump: it fires when the controller reports fall ended.
        private void OnLand() => Play(audioManager?.Player?.land);
        private void OnDash() => Play(audioManager?.Player?.dash);
        private void OnDeath() => Play(audioManager?.Player?.death);
        private void OnAttackingChanged(bool attacking)
        {
            if (attacking) PlayAttack();
        }
        private void OnEnemyHit() => Play(audioManager?.Player?.hit);

        private void OnWallSlide(bool sliding)
        {
            if (sliding) Play(audioManager?.Player?.wallSlide);
        }

        private void Play(SoundEntry entry)
        {
            audioManager?.PlaySpatial(entry, source);
           // Debug.Log("Playing sound: " + entry.clip);
        }

        private static SoundEntry GetVariantOrFallback(SoundEntryGroup group, SoundEntry fallback)
        {
            if (group != null && group.HasEntries)
            {
                var entry = group.GetRandomEntry();
                if (entry != null && entry.IsValid)
                    return entry;
            }
            return fallback;
        }
    }
}