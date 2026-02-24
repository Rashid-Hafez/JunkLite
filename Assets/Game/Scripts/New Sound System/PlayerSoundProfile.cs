using UnityEngine;

namespace junklite
{
    [CreateAssetMenu(menuName = "Junklite/Audio/Player Sound Profile")]
    public class PlayerSoundProfile : ScriptableObject
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
        public SoundEntry hit;
        public SoundEntry environmentHit;
        public SoundEntry parryStart;
        public SoundEntry parrySuccess;
        public SoundEntry hurt;
        public SoundEntry death;
    }
}