using UnityEngine;

namespace junklite
{
    [CreateAssetMenu(fileName = "AudioLibrary", menuName = "Junklite/Audio Library")]
    public class AudioLibrary : ScriptableObject
    {
        [Header("Player")]
        public PlayerSounds player;

        [Header("Enemies")]
        public EnemySounds robot;
        public EnemySounds hyena;
        public EnemySounds dummy;

        [Header("UI")]
        public UISounds ui;

        [Header("Music")]
        public MusicTracks music;

        [Header("Ambience")]
        public AmbienceTracks ambience;

        /// <summary>
        /// Get enemy sounds by type.
        /// </summary>
        public EnemySounds GetEnemy(EnemyType type)
        {
            return type switch
            {
                EnemyType.Robot => robot,
                EnemyType.Hyena => hyena,
                _ => null
            };
        }
    }
}