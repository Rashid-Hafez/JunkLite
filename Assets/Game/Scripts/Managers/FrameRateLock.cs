using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Locks the game at 60 fps on boot, before any scene loads.
    /// 60/120/240 Hz screens use VSync with a divider (no tearing); other rates fall back to a plain 60 fps cap.
    /// </summary>
    public static class FrameRateLock
    {
        public const int TargetFps = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            int hz = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
            bool vsyncDivides = hz >= TargetFps && hz % TargetFps == 0 && hz / TargetFps <= 4;
            QualitySettings.vSyncCount = vsyncDivides ? hz / TargetFps : 0;
            // Builds ignore this while VSync is on. The Editor Game view does not VSync, so it caps there.
            Application.targetFrameRate = TargetFps;
        }
    }
}
