using UnityEngine;

namespace junklite
{
    [CreateAssetMenu(menuName = "Junklite/Audio/Enemy Sound Profile")]
    public class EnemySoundProfile : ScriptableObject
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
}