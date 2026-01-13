using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Attach to any enemy. Reads EnemyType from EnemyCharacter.
    /// Auto-plays sounds on state changes and damage events.
    /// If a sound isn't assigned in AudioLibrary, it simply won't play.
    /// </summary>
    [DefaultExecutionOrder(6)]
    [RequireComponent(typeof(AudioSource))]
    [RequireComponent(typeof(EnemyCharacter))]
    public class EnemyAudioHandler : MonoBehaviour
    {
        [Header("3D Sound Settings")]
        [SerializeField] private float minDistance = 1f;
        [SerializeField] private float maxDistance = 25f;

        private EnemyCharacter enemy;
        private StateMachine stateMachine;
        private Damageable damageable;
        private AttributeManager attributes;
        private AudioManager audio;
        private AudioSource source;
        private EnemySounds sounds;

        void Awake()
        {
            enemy = GetComponent<EnemyCharacter>();
            damageable = GetComponent<Damageable>();
            attributes = GetComponent<AttributeManager>();
            source = GetComponent<AudioSource>();

            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.rolloffMode = AudioRolloffMode.Linear;
        }

        void Start()
        {
            audio = AudioManager.Instance;
            stateMachine = enemy?.StateMachine;

            if (enemy != null)
                sounds = audio?.GetEnemy(enemy.EnemyType);

            if (audio != null && audio.SFXGroup != null)
                source.outputAudioMixerGroup = audio.SFXGroup;
        }

        void OnEnable()
        {
            if (damageable != null)
                damageable.OnDamaged += OnHurt;

            if (attributes != null)
                attributes.OnDeath += OnDeath;

            // Subscribe after Start, so we do it in a coroutine
            StartCoroutine(SubscribeToStateMachine());
        }

        void OnDisable()
        {
            if (damageable != null)
                damageable.OnDamaged -= OnHurt;

            if (attributes != null)
                attributes.OnDeath -= OnDeath;

            if (stateMachine != null)
                stateMachine.OnStateChanged -= OnStateChanged;
        }

        private System.Collections.IEnumerator SubscribeToStateMachine()
        {
            // Wait one frame for StateMachine to be ready
            yield return null;

            stateMachine = enemy?.StateMachine;
            if (stateMachine != null)
                stateMachine.OnStateChanged += OnStateChanged;
        }

        // Auto-trigger on state enter
        private void OnStateChanged(IState from, IState to)
        {
            switch (to)
            {
                case ChargeState:
                    Play(sounds?.charge);
                    break;
                case DashState:
                    Play(sounds?.dash);
                    break;
                case GrabState:
                    Play(sounds?.grab);
                    break;
            }
        }

        // Auto-triggered by damage events
        private void OnHurt(float damage, GameObject attacker) => Play(sounds?.hurt);
        private void OnDeath() => Play(sounds?.death);

        // Manual methods (if needed for special cases)
        public void PlayAttack() => Play(sounds?.attack);
        public void PlayFootstep() => Play(sounds?.footstep);

        private void Play(SoundEntry entry)
        {
            audio?.PlaySpatial(entry, source);
        }
    }
}