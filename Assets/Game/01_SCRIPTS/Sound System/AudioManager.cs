using UnityEngine;
using UnityEngine.Audio;

namespace junklite
{
    public class AudioManager : MonoBehaviour
    {
        // CENTRAL AUDIO HUB (Singleton)
        // - Holds a reference to an AudioLibrary (ScriptableObject) that contains all SoundEntry clips/settings
        // - Plays MUSIC on one dedicated looping AudioSource
        // - Plays UI/2D SFX on a pooled set of AudioSources (so multiple SFX can overlap)
        // - Plays gameplay SFX (player/enemy) on the caller-provided AudioSource via PlaySpatial(...)
        public static AudioManager Instance { get; private set; }

        [Header("Setup")]
        [SerializeField] private AudioLibrary library;
        [SerializeField] private int poolSize = 5;
        [SerializeField] private int spatialPoolSize = 8;

        [Header("Mixer Groups")]
        [SerializeField] private AudioMixerGroup masterGroup;
        [SerializeField] private AudioMixerGroup musicGroup;
        [SerializeField] private AudioMixerGroup sfxGroup;

        // These must match EXPOSED parameters in your AudioMixer.
        // We set them in dB internally, but public API uses 0..1 sliders.
        private const string MASTER_VOL = "MasterVolume";
        private const string MUSIC_VOL = "MusicVolume";
        private const string SFX_VOL = "SFXVolume";

        private AudioSource[] sfxPool;
        private int poolIndex;
        private AudioSource musicSource;
        private AudioSource[] spatialPool;
        private int spatialPoolIndex;

        // Direct access to library sections
        public PlayerSounds Player => library?.player;
        public UISounds UI => library?.ui;
        public MusicTracks Music => library?.music;
        public AudioMixerGroup SFXGroup => sfxGroup;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            // Keep audio alive across scene loads
            DontDestroyOnLoad(gameObject);

            InitPool();
            InitSpatialPool();
            InitMusicSource();
        }

        private void InitPool()
        {
            // Pooled 2D SFX sources (used by PlayUI)
            sfxPool = new AudioSource[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                var go = new GameObject($"SFX_{i}");
                go.transform.SetParent(transform);
                sfxPool[i] = go.AddComponent<AudioSource>();
                sfxPool[i].playOnAwake = false;
                sfxPool[i].spatialBlend = 0f; // 2D for UI
                sfxPool[i].outputAudioMixerGroup = sfxGroup;
            }
        }

        private void InitSpatialPool()
        {
            // Pooled spatial sources for overlapping gameplay SFX
            spatialPool = new AudioSource[Mathf.Max(1, spatialPoolSize)];
            for (int i = 0; i < spatialPool.Length; i++)
            {
                var go = new GameObject($"SFX_Spatial_{i}");
                go.transform.SetParent(transform);
                spatialPool[i] = go.AddComponent<AudioSource>();
                spatialPool[i].playOnAwake = false;
                spatialPool[i].outputAudioMixerGroup = sfxGroup;
            }
        }

        private void InitMusicSource()
        {
            // Dedicated music source (looping)
            var go = new GameObject("Music");
            go.transform.SetParent(transform);
            musicSource = go.AddComponent<AudioSource>();
            musicSource.loop = true;
            musicSource.playOnAwake = true;
            musicSource.outputAudioMixerGroup = musicGroup;
        }

        /// <summary>
        /// Play 2D/UI sound. Uses pool so clicks/wooshes can overlap.
        /// </summary>
        public void PlayUI(SoundEntry entry)
        {
            if (entry == null || !entry.IsValid) return;

            var source = GetNextSource();
            source.clip = entry.clip;
            source.volume = entry.volume;
            source.pitch = entry.GetRandomPitch();
            source.Play();
        }

        /// <summary>
        /// Play gameplay sound on a specific AudioSource (Player/Enemy).
        /// NOTE: whether it's 2D or 3D depends on that source's spatialBlend settings.
        /// </summary>
        public void PlaySpatial(SoundEntry entry, AudioSource source)
        {
            if (entry == null || !entry.IsValid || source == null) return;

            var spatial = GetNextSpatialSource();
            if (spatial == null) return;

            // Match caller's spatial settings so it behaves like their AudioSource.
            spatial.transform.position = source.transform.position;
            spatial.transform.rotation = source.transform.rotation;
            spatial.spatialBlend = source.spatialBlend;
            spatial.minDistance = source.minDistance;
            spatial.maxDistance = source.maxDistance;
            spatial.rolloffMode = source.rolloffMode;
            spatial.outputAudioMixerGroup = source.outputAudioMixerGroup ?? sfxGroup;

            spatial.volume = entry.volume * source.volume;
            spatial.pitch = entry.GetRandomPitch() * source.pitch;
            spatial.PlayOneShot(entry.clip, entry.volume);
        }

        /// <summary>
        /// Get enemy sounds by type.
        /// </summary>
        public EnemySounds GetEnemy(EnemyType type)
        {
            return library?.GetEnemy(type);
        }

        /// <summary>
        /// Play music (replaces current music clip).
        /// </summary>
        public void PlayMusic(SoundEntry entry)
        {
            if (entry == null || !entry.IsValid) return;

            musicSource.clip = entry.clip;
            musicSource.volume = entry.volume;
            musicSource.Play();
        }

        public void StopMusic()
        {
            musicSource.Stop();
        }

        // Volume control (0 to 1)
        public void SetMasterVolume(float volume) => SetMixerVolume(MASTER_VOL, volume);
        public void SetMusicVolume(float volume) => SetMixerVolume(MUSIC_VOL, volume);
        public void SetSFXVolume(float volume) => SetMixerVolume(SFX_VOL, volume);

        public float GetMasterVolume() => GetMixerVolume(MASTER_VOL);
        public float GetMusicVolume() => GetMixerVolume(MUSIC_VOL);
        public float GetSFXVolume() => GetMixerVolume(SFX_VOL);

        private void SetMixerVolume(string param, float volume)
        {
            if (masterGroup == null || masterGroup.audioMixer == null) return;
            // Convert linear (0..1) -> dB (0 => -80dB "silent")
            float dB = volume > 0.0001f ? Mathf.Log10(volume) * 20f : -80f;
            masterGroup.audioMixer.SetFloat(param, dB);
        }

        private float GetMixerVolume(string param)
        {
            if (masterGroup == null || masterGroup.audioMixer == null) return 1f;
            if (masterGroup.audioMixer.GetFloat(param, out float dB))
                return Mathf.Pow(10f, dB / 20f);
            return 1f;
        }

        private AudioSource GetNextSource()
        {
            var source = sfxPool[poolIndex];
            poolIndex = (poolIndex + 1) % poolSize;
            return source;
        }

        private AudioSource GetNextSpatialSource()
        {
            if (spatialPool == null || spatialPool.Length == 0)
                return null;

            // Find a free source; fallback to round-robin if all are busy.
            for (int i = 0; i < spatialPool.Length; i++)
            {
                int index = (spatialPoolIndex + i) % spatialPool.Length;
                if (!spatialPool[index].isPlaying)
                {
                    spatialPoolIndex = (index + 1) % spatialPool.Length;
                    return spatialPool[index];
                }
            }

            var fallback = spatialPool[spatialPoolIndex];
            spatialPoolIndex = (spatialPoolIndex + 1) % spatialPool.Length;
            return fallback;
        }
    }
}