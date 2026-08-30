using UnityEngine;

namespace junklite
{
    [System.Serializable]
    public class UISounds
    {
        public SoundEntry click;
        public SoundEntry confirm;
        public SoundEntry cancel;
        public SoundEntry hover;
        public SoundEntry back;
    }

    [System.Serializable]
    public class MusicTracks
    {
        [Header("Level / Combat / Boss")]
        public SoundEntry level;
        public SoundEntry combat;
        public SoundEntry boss;

        [Header("Other")]
        public SoundEntry mainMenu;
        public SoundEntry gameplay;
        public SoundEntry victory;
        public SoundEntry defeat;
    }

    [System.Serializable]
    public class AmbienceTracks
    {
        public SoundEntry ambience;
    }
}