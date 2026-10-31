using UnityEngine;

namespace junklite
{
    public class SceneSettings : MonoBehaviour
    {
        [Header("Player")]
        [Tooltip("Uncheck to prevent GameManager from spawning the player or creating the Player HUD in this scene. " +
                 "Useful for main menus, cutscene scenes, or any scene that manages its own character.")]
        [SerializeField] private bool spawnPlayer = true;
        [Tooltip("Override the player this scene spawns. Game Default uses the Game Manager prefab's Use New Player.")]
        [SerializeField] private LevelPlayerChoice playerChoice = LevelPlayerChoice.GameDefault;

        [Header("Music")]
        [SerializeField] private LevelMusicSettings levelMusic = new();

        public bool SpawnPlayer => spawnPlayer;
        public LevelPlayerChoice PlayerChoice => playerChoice;
        public LevelMusicSettings LevelMusic => levelMusic;
    }
}