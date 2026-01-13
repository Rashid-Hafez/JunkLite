using UnityEngine;
using UnityEngine.Audio;

namespace junklite
{
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Setup")]
        [SerializeField] private AudioLibrary library;
        [SerializeField] private int poolSize = 5;

        [Header("Mixer Groups")]
        [SerializeField] private AudioMixerGroup masterGroup;
        [SerializeField] private AudioMixerGroup musicGroup;
        [SerializeField] private AudioMixerGroup sfxGroup;

        private const string MASTER_VOL = "MasterVolume";
        private const string MUSIC_VOL = "MusicVolume";
        private const string SFX_VOL = "SFXVolume";

        private AudioSource[] sfxPool;
        private int poolIndex;
        private AudioSource musicSource;

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
            DontDestroyOnLoad(gameObject);

            InitPool();
            InitMusicSource();
        }

        private void InitPool()
        {
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

        private void InitMusicSource()
        {
            var go = new GameObject("Music");
            go.transform.SetParent(transform);
            musicSource = go.AddComponent<AudioSource>();
            musicSource.loop = true;
            musicSource.playOnAwake = false;
            musicSource.outputAudioMixerGroup = musicGroup;
        }

        /// <summary>
        /// Play 2D sound (UI only). Uses pool.
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
        /// Play 3D spatial sound on a specific AudioSource (Player/Enemy).
        /// </summary>
        public void PlaySpatial(SoundEntry entry, AudioSource source)
        {
            if (entry == null || !entry.IsValid || source == null) return;

            source.clip = entry.clip;
            source.volume = entry.volume;
            source.pitch = entry.GetRandomPitch();
            source.Play();
        }

        /// <summary>
        /// Get enemy sounds by type.
        /// </summary>
        public EnemySounds GetEnemy(EnemyType type)
        {
            return library?.GetEnemy(type);
        }

        /// <summary>
        /// Play music.
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
    }
}