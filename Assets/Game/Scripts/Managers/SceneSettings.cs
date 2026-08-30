using UnityEngine;

namespace junklite
{
    public class SceneSettings : MonoBehaviour
    {
        [Header("Player")]
        [Tooltip("Uncheck to prevent GameManager from spawning the player or creating the Player HUD in this scene. " +
                 "Useful for main menus, cutscene scenes, or any scene that manages its own character.")]
        [SerializeField] private bool spawnPlayer = true;

        public bool SpawnPlayer => spawnPlayer;
    }
}