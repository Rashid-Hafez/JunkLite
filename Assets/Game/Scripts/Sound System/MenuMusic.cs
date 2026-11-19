using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

namespace junklite
{
    /// <summary>
    /// Title screen music. The menu has no Game Root (and so no AudioManager) until a run starts, so it
    /// plays its own looping source: fades in on load, and when a level is chosen it survives the scene
    /// change just long enough to fade out under the loading screen, then removes itself.
    /// Put it on a root object in the menu scene.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class MenuMusic : MonoBehaviour
    {
        private static MenuMusic current;

        [SerializeField] private AudioClip music;
        [SerializeField, Range(0f, 1f)] private float volume = 0.44f;
        [Tooltip("Optional. Route through the game's Music mixer group so the music volume setting applies.")]
        [SerializeField] private AudioMixerGroup output;
        [SerializeField, Min(0f)] private float fadeInSeconds = 2.5f;
        [SerializeField, Min(0f)] private float fadeOutSeconds = 1.5f;

        private AudioSource source;
        private Coroutine fade;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.clip = music;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            if (output != null) source.outputAudioMixerGroup = output;
        }

        private void OnEnable() => current = this;

        private void OnDisable()
        {
            if (current == this) current = null;
        }

        private void Start()
        {
            if (music == null) return;
            source.volume = 0f;
            source.Play();
            fade = StartCoroutine(FadeTo(volume, fadeInSeconds, destroyAfter: false));
        }

        /// <summary>Fades the playing menu music out across the scene change (called when a level is chosen).</summary>
        public static void FadeOutCurrent()
        {
            if (current == null || !current.source.isPlaying) return;
            current.transform.SetParent(null);
            DontDestroyOnLoad(current.gameObject);
            if (current.fade != null) current.StopCoroutine(current.fade);
            current.fade = current.StartCoroutine(current.FadeTo(0f, current.fadeOutSeconds, destroyAfter: true));
        }

        // Unscaled: the loading screen freezes gameplay time while the fade is still running.
        private IEnumerator FadeTo(float target, float seconds, bool destroyAfter)
        {
            float start = source.volume;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                source.volume = Mathf.Lerp(start, target, t / seconds);
                yield return null;
            }
            source.volume = target;
            fade = null;
            if (destroyAfter) Destroy(gameObject);
        }
    }
}
