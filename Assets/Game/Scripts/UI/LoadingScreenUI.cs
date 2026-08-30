using UnityEngine;
using UnityEngine.Video;

namespace junklite
{
    public class LoadingScreenUI : MonoBehaviour
    {
        #region Fields

        [SerializeField] private GameObject panel;
        [SerializeField] private VideoPlayer videoPlayer;

        private AudioSource videoAudioSource;
        private bool videoFinished;
        private bool prepareComplete;

        // Exposed through GameUIManager so scene activation can wait for presentation.
        public bool IsVideoFinished => videoFinished || videoPlayer == null;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);

            if (videoPlayer != null)
            {
                videoAudioSource = GetComponent<AudioSource>();
                if (videoAudioSource == null)
                    videoAudioSource = gameObject.AddComponent<AudioSource>();

                videoPlayer.isLooping   = false;
                videoPlayer.playOnAwake = false;

                // Bind audio now, before Prepare(). Unity requires this before Prepare() or after Stop().
                videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
                videoPlayer.EnableAudioTrack(0, true);
                videoPlayer.SetTargetAudioSource(0, videoAudioSource);

                videoPlayer.prepareCompleted += OnVideoPrepared;
                videoPlayer.loopPointReached += OnVideoFinished;
                videoPlayer.errorReceived    += OnVideoError;

                // Pre-prepare once at startup so Show() can call Play() immediately
                // without a Prepare() delay, which was causing the visual freeze.
                videoPlayer.Prepare();
            }

            Hide();
        }

        private void OnDestroy()
        {
            if (videoPlayer == null) return;
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.loopPointReached -= OnVideoFinished;
            videoPlayer.errorReceived    -= OnVideoError;
        }

        #endregion

        #region Public API

        public void Show()
        {
            videoFinished = false;
            panel?.SetActive(true);

            if (videoPlayer == null)
            {
                videoFinished = true;
                return;
            }

            // Re-bind audio after Stop() — Unity requires the binding to be refreshed
            // each time the VideoPlayer is stopped and restarted.
            videoPlayer.Stop();
            videoPlayer.time  = 0;
            videoPlayer.frame = 0;
            videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            videoPlayer.EnableAudioTrack(0, true);
            videoPlayer.SetTargetAudioSource(0, videoAudioSource);

            if (prepareComplete)
            {
                // Already prepared — play immediately with no gap for the buffer to overflow.
                videoPlayer.Play();
            }
            else
            {
                // Still preparing (e.g. Show() called very quickly after scene start).
                // OnVideoPrepared will call Play() once ready.
                videoPlayer.Prepare();
            }
        }

        public void Hide()
        {
            panel?.SetActive(false);
            videoPlayer?.Stop();
        }

        #endregion

        #region VideoPlayer Callbacks

        private void OnVideoPrepared(VideoPlayer vp)
        {
            prepareComplete = true;

            // Only auto-play if Show() is waiting on preparation.
            // If Show() already called Play() directly, do nothing.
            if (!videoFinished && panel != null && panel.activeSelf && !vp.isPlaying)
                vp.Play();
        }

        private void OnVideoFinished(VideoPlayer _)
        {
            videoFinished = true;
            // Re-prepare for the next transition so it's ready instantly again.
            prepareComplete = false;
            videoPlayer?.Prepare();
        }

        private void OnVideoError(VideoPlayer _, string message)
        {
            Debug.LogWarning($"[LoadingScreenUI] Video error: {message}");
            videoFinished = true; // never block the game on a video failure
        }

        #endregion
    }
}
