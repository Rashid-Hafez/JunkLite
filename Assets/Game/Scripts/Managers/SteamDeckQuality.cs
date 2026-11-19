using System;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Switches to the Medium quality level on Steam Deck at boot (PC builds default to Ultra).
    /// Runs before FrameRateLock (BeforeSceneLoad), because changing the quality level resets VSync.
    /// </summary>
    public static class SteamDeckQuality
    {
        public const string DeckQualityLevel = "Medium";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void Apply()
        {
            if (!IsSteamDeck()) return;

            int level = Array.IndexOf(QualitySettings.names, DeckQualityLevel);
            if (level >= 0)
                QualitySettings.SetQualityLevel(level, true);
        }

        private static bool IsSteamDeck()
        {
            // SteamOS sets SteamDeck=1; DMI names are Jupiter (LCD) / Galileo (OLED); the GPU ids cover Proton (Windows builds).
            if (Environment.GetEnvironmentVariable("SteamDeck") == "1") return true;

            string model = SystemInfo.deviceModel ?? string.Empty;
            if (model.Contains("Jupiter") || model.Contains("Galileo")) return true;

            string gpu = SystemInfo.graphicsDeviceName ?? string.Empty;
            return gpu.Contains("AMD Custom GPU 0405") || gpu.Contains("AMD Custom GPU 0932");
        }
    }
}
