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
        public SoundEntry doubleJump;
        public SoundEntry land;
        public SoundEntry dash;
        public SoundEntry wallSlide;
        public SoundEntry wallJump;
        public SoundEntry footstep;

        [Header("Combat")]
        public SoundEntry attack;
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
        public SoundEntry mainMenu;
        public SoundEntry gameplay;
        public SoundEntry boss;
        public SoundEntry victory;
        public SoundEntry defeat;
    }
}
