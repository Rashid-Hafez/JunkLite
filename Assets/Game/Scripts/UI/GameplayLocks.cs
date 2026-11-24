using UnityEngine;
using UnityEngine.Playables;

namespace junklite
{
    /// <summary>
    /// When menus (the inventory, the pause screen) may open, for the UI to check. Read-only: it only looks at what the
    /// game is doing.
    ///   InCutscene    a cutscene Timeline is running: a PlayableDirector that's playing, isn't set to loop, and hasn't
    ///                 reached its end (a Timeline set to Hold keeps "playing" on its last frame forever after, as
    ///                 Service Floor's Impact Cinematic does, so that doesn't count), or the demo's ability intro is up
    ///   InDialogue    a dialogue sequence is running
    ///   LevelFinished the level is complete (the results screen is up or on its way)
    /// </summary>
    public static class GameplayLocks
    {
        public static bool InCutscene => DemoAbilityIntro.IsShowing || RunningTimeline() != null;

        public static bool InDialogue => DialogueManager.instance != null && DialogueManager.instance.IsInDialogue;

        public static bool LevelFinished =>
            LevelStatsTracker.Instance != null && LevelStatsTracker.Instance.State == LevelState.Completed;

        /// <summary>
        /// The player may open the inventory: the game isn't paused or over, the player isn't dead, and there's no
        /// cutscene, dialogue or finished level. <paramref name="reason"/> says what's in the way.
        /// </summary>
        public static bool CanOpenInventory(PlayerCharacter player, out string reason)
        {
            GameManager game = GameManager.Instance;
            if (game != null && game.CurrentState == GameManager.GameState.Paused) { reason = "the game is paused"; return false; }
            if (game != null && game.CurrentState == GameManager.GameState.GameOver) { reason = "the game is over"; return false; }
            if (player != null && !player.IsAlive) { reason = "the player is dead"; return false; }
            if (DemoAbilityIntro.IsShowing) { reason = "the ability intro is up"; return false; }
            var timeline = RunningTimeline();
            if (timeline != null) { reason = $"the cutscene '{timeline.name}' is playing"; return false; }
            if (InDialogue) { reason = "a dialogue is running"; return false; }
            if (LevelFinished) { reason = "the level is finished"; return false; }
            reason = null;
            return true;
        }

        // A cutscene Timeline that's actually running: playing, not looping, and not yet at its end.
        static PlayableDirector RunningTimeline()
        {
            foreach (var director in Object.FindObjectsByType<PlayableDirector>(FindObjectsSortMode.None))
            {
                if (!director.isActiveAndEnabled || director.playableAsset == null) continue;
                if (director.state != PlayState.Playing || director.extrapolationMode == DirectorWrapMode.Loop) continue;
                double duration = director.duration;
                if (double.IsInfinity(duration) || duration <= 0d || director.time >= duration - 0.05d) continue;
                return director;
            }
            return null;
        }
    }
}
