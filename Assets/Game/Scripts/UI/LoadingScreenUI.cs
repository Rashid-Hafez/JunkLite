using UnityEngine;
using UnityEngine.Video;

namespace junklite
{
    public class LoadingScreenUI : MonoBehaviour
    {
        #region Fields

        [SerializeField] private GameObject panel;
        [SerializeField] private VideoPlayer videoPlayer;

        private bool videoFinished;

        // Polled by GameManager each frame to decide when to activate the incoming scene
        public bool IsVideoFinished => videoFinished || videoPlayer == null;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);

            if (videoPlayer != null)
            {
                videoPlayer.isLooping = false;
                videoPlayer.loopPointReached += OnVideoFinished;
                videoPlayer.errorReceived += OnVideoError;
            }

            Hide();
        }

        private void OnDestroy()
        {
            if (videoPlayer == null) return;
            videoPlayer.loopPointReached -= OnVideoFinished;
            videoPlayer.errorReceived -= OnVideoError;
        }

        #endregion

        #region Public API

        public void Show()
        {
            videoFinished = false;
            panel?.SetActive(true);

            if (videoPlayer != null)
            {
                videoPlayer.Stop();
                videoPlayer.time = 0;
                videoPlayer.frame = 0;
                videoPlayer.Play();
            }
            else
            {
                videoFinished = true; // no video, unblock immediately
            }
        }

        public void Hide()
        {
            panel?.SetActive(false);
            videoPlayer?.Stop();
        }

        #endregion

        #region Callbacks

        private void OnVideoFinished(VideoPlayer _) => videoFinished = true;

        private void OnVideoError(VideoPlayer _, string message)
        {
            Debug.LogWarning($"[LoadingScreenUI] Video error: {message}");
            videoFinished = true; // never block the game on a video failure
        }

        #endregion
    }
}