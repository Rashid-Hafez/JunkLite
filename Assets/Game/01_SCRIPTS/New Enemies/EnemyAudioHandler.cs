using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Attach to any enemy. Hurt/death auto-wired via events.
    /// FSM states call public methods for attack, charge, grab, dash, footstep.
    /// </summary>
    [DefaultExecutionOrder(6)]
    [RequireComponent(typeof(AudioSource))]
    public class EnemyAudioHandler : MonoBehaviour
    {
        [Header("3D Sound Settings")]
        [SerializeField] private float minDistance = 1f;
        [SerializeField] private float maxDistance = 25f;

        private EnemyCharacter enemy;
        private Damageable damageable;
        private AttributeManager attributes;
        private AudioManager audioManager;
        private AudioSource source;

        private EnemySoundProfile sounds;

        void Awake()
        {
            enemy = GetComponent<EnemyCharacter>() ?? GetComponentInParent<EnemyCharacter>();
            damageable = GetComponent<Damageable>() ?? GetComponentInParent<Damageable>();
            attributes = GetComponent<AttributeManager>() ?? GetComponentInParent<AttributeManager>();
            source = GetComponent<AudioSource>();

            if (enemy != null)
                sounds = enemy.SoundProfile;

            source.playOnAwake = false;
            source.spatialBlend = 1f;
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
            if (damageable != null)
                damageable.OnDamaged += OnDamaged;

            if (attributes != null)
                attributes.OnDeath += OnDeath;
        }

        void OnDisable()
        {
            if (damageable != null)
                damageable.OnDamaged -= OnDamaged;

            if (attributes != null)
                attributes.OnDeath -= OnDeath;
        }

        // Auto-wired via events
        private void OnDamaged(float damage, GameObject src) => Play(sounds?.hurt);
        private void OnDeath() => Play(sounds?.death);

        // Public — called by FSM states
        public void PlayAttack() => Play(sounds?.attack);
        public void PlayCharge() => Play(sounds?.charge);
        public void PlayGrab() => Play(sounds?.grab);
        public void PlayDash() => Play(sounds?.dash);
        public void PlayFootstep() => Play(sounds?.footstep);

        private void Play(SoundEntry entry)
        {
            if (entry == null || audioManager == null) return;
            audioManager.PlaySpatial(entry, source);
        }
    }
}