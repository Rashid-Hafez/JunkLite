using UnityEngine;
using System.Collections.Generic;

namespace junklite
{

    [System.Serializable]
    public class SoundEntry
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0.1f, 3f)] public float pitchMin = 1f;
        [Range(0.1f, 3f)] public float pitchMax = 1f;

        public float GetRandomPitch()
        {
            return Random.Range(pitchMin, pitchMax);
        }

        public bool IsValid => clip != null;
    }

    public enum SoundCategory
    {
        Player,
        Robot,
        Hyena,
        UI,
        Music,
        Environment
    }


    public enum SoundType
    {
        // Movement
        Jump,
        DoubleJump,
        Land,
        Dash,
        WallSlide,
        WallJump,
        Footstep,

        // Combat
        Attack,
        Hurt,
        Death,
        Hit,
        Block,

        // Interaction
        Pickup,
        Drop,

        // UI
        Click,
        Confirm,
        Cancel,
        Hover,

        // Music
        MainMenu,
        Gameplay,
        Boss
    }
}