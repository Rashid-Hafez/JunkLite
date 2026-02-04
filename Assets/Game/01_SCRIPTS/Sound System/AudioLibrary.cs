using UnityEngine;

namespace junklite
{
    [CreateAssetMenu(fileName = "AudioLibrary", menuName = "Junklite/Audio Library")]
    public class AudioLibrary : ScriptableObject
    {
        [Header("UI")]
        public UISounds ui;

        [Header("Music")]
        public MusicTracks music;

        [Header("Ambience")]
        public AmbienceTracks ambience;
    }
}