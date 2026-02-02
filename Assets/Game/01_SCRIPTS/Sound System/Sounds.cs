using UnityEngine;

namespace junklite
{

    public class Sounds : MonoBehaviour
    {

        
    }

    [System.Serializable]
    public class PlayerSounds
    {
        [Header("Movement")]
        public SoundEntry jump;
        public SoundEntryGroup jumpVariants;
        public SoundEntry doubleJump;
        public SoundEntry land;
        public SoundEntry dash;
        public SoundEntry wallSlide;
        public SoundEntry wallJump;
        public SoundEntry footstep;
        public SoundEntryGroup footstepVariants;

        [Header("Combat")]
        public SoundEntry attack;
        public SoundEntryGroup attackVariants;
        public SoundEntry hit; // plays when an attack actually connects with an enemy
        public SoundEntry hurt;
        public SoundEntry death;
    }

    [System.Serializable]
    public class EnemySounds
    {
        [Header("Combat")]
        public SoundEntry attack;
        public SoundEntry hurt;
        public SoundEntry death;
        public SoundEntry charge;
        public SoundEntry grab;

        [Header("Movement")]
        public SoundEntry dash;
        public SoundEntry footstep;
    }

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
        [Header("Level / Combat / Boss (switch between these in-game)")]
        public SoundEntry level;
        public SoundEntry combat;
        public SoundEntry boss;

        [Header("Other")]
        public SoundEntry mainMenu;
        public SoundEntry gameplay; // legacy; use level for new content
        public SoundEntry victory;
        public SoundEntry defeat;
    }

    [System.Serializable]
    public class AmbienceTracks
    {
        [Tooltip("Plays on loop at all times. Assign in AudioLibrary.")]
        public SoundEntry ambience;
    }
}
