using UnityEngine;

namespace junklite
{
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

        private PlayerSoundProfile sounds;

        void Awake()
        {
            controller = GetComponent<Character2D5Controller>();
            state = GetComponent<PlayerState>();
            weaponManager = GetComponentInParent<WeaponManager>() ?? GetComponent<WeaponManager>();
            source = GetComponent<AudioSource>();

            var player = GetComponent<PlayerCharacter>() ?? GetComponentInParent<PlayerCharacter>();
            if (player != null)
                sounds = player.SoundProfile;

            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.rolloffMode = AudioRolloffMode.Linear;
        }

        void Start()
        {
            audioManager = AudioManager.Instance;

            if (audioManager != null && audioManager.SFXGroup != null)
                source.outputAudioMixerGroup = audioManager.SFXGroup;
        }

        void OnEnable()
        {
            if (controller != null)
            {
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
                state.OnAttackingChanged += OnAttackingChanged;
            }

            if (weaponManager != null)
            {
                weaponManager.OnEnemyHit += OnEnemyHit;
                weaponManager.OnEnvironmentHit += OnEnvironmentHit;
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
                weaponManager.OnEnvironmentHit -= OnEnvironmentHit;
            }
        }

        // Public for manual triggering
        public void PlayHurt() => Play(sounds?.hurt);
        public void PlayAttack() => Play(GetVariantOrFallback(sounds?.attackVariants, sounds?.attack));
        public void PlayFootstep() => Play(GetVariantOrFallback(sounds?.footstepVariants, sounds?.footstep));

        // Event handlers
        private void OnJump() => Play(GetVariantOrFallback(sounds?.jumpVariants, sounds?.jump));
        private void OnDoubleJump() => Play(sounds?.doubleJump);
        private void OnWallJump() => Play(sounds?.wallJump);
        private void OnLand() => Play(sounds?.land);
        private void OnDash() => Play(sounds?.dash);
        private void OnDeath() => Play(sounds?.death);
        private void OnEnemyHit(EnemyCharacter _, float __) => Play(sounds?.hit);
        private void OnEnvironmentHit() => Play(sounds?.environmentHit);
        private void OnAttackingChanged(bool attacking) { if (attacking) PlayAttack(); }
        private void OnWallSlide(bool sliding) { if (sliding) Play(sounds?.wallSlide); }

        private void Play(SoundEntry entry)
        {
            if (entry == null || audioManager == null) return;
            audioManager.PlaySpatial(entry, source);
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