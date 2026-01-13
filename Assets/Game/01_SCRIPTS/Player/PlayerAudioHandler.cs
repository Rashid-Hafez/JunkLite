using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Attach to Player. Listens to player events and triggers spatial sounds.
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
        private AudioManager audio;
        private AudioSource source;

        void Awake()
        {
            controller = GetComponent<Character2D5Controller>();
            state = GetComponent<PlayerState>();
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
            audio = AudioManager.Instance;

            // Assign mixer group
            if (audio != null && audio.SFXGroup != null)
                source.outputAudioMixerGroup = audio.SFXGroup;
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
            }
        }

        // Public methods for manual triggering
        public void PlayHurt() => Play(audio?.Player?.hurt);
        public void PlayAttack() => Play(audio?.Player?.attack);

        // Event handlers
        private void OnJump() => Play(audio?.Player?.jump);
        private void OnDoubleJump() => Play(audio?.Player?.doubleJump);
        private void OnWallJump() => Play(audio?.Player?.wallJump);
        private void OnLand() => Play(audio?.Player?.land);
        private void OnDash() => Play(audio?.Player?.dash);
        private void OnDeath() => Play(audio?.Player?.death);

        private void OnWallSlide(bool sliding)
        {
            if (sliding) Play(audio?.Player?.wallSlide);
        }

        private void Play(SoundEntry entry)
        {
            audio?.PlaySpatial(entry, source);
            Debug.Log("Playing sound: " + entry.clip);
        }
    }
}