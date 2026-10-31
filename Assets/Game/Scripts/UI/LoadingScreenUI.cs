using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace junklite
{
    public class LoadingScreenUI : MonoBehaviour
    {
        #region Fields

        [SerializeField] private GameObject panel;
        [SerializeField] private VideoPlayer videoPlayer;

        [Header("Progress (optional, authored in the prefab)")]
        [Tooltip("Filled Image whose fillAmount follows scene-load progress.")]
        [SerializeField] private Image progressFill;
        [Tooltip("Label showing the load percentage.")]
        [SerializeField] private TMP_Text progressLabel;
        [SerializeField] private string progressFormat = "LOADING  {0}%";

        private int shownPercent = -1;

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

            if (progressLabel != null)
                UIFonts.ApplyHudBody(progressLabel);

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
            shownPercent = -1;
            SetProgress(0f);

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

        /// <summary>Binds normalized (0-1) load progress to the authored bar and label.</summary>
        public void SetProgress(float normalized)
        {
            normalized = Mathf.Clamp01(normalized);
            if (progressFill != null)
                progressFill.fillAmount = normalized;

            // Only rebuild the label mesh when the visible percentage changes.
            int percent = Mathf.RoundToInt(normalized * 100f);
            if (progressLabel == null || percent == shownPercent) return;
            shownPercent = percent;
            progressLabel.SetText(progressFormat, percent);
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
