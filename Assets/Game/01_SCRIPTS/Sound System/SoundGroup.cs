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
        public List<SoundEffectConfig> effects = new();

        public float GetRandomPitch()
        {
            return Random.Range(pitchMin, pitchMax);
        }

        public bool IsValid => clip != null;

        public SoundEffectConfig GetRandomEffect()
        {
            if (effects == null || effects.Count == 0) return null;

            float totalWeight = 0f;
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i] != null && effects[i].weight > 0f)
                    totalWeight += effects[i].weight;
            }

            if (totalWeight <= 0f) return null;

            float roll = Random.Range(0f, totalWeight);
            float running = 0f;
            for (int i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                if (effect == null || effect.weight <= 0f) continue;

                running += effect.weight;
                if (roll <= running)
                    return effect;
            }

            return null;
        }
    }

    [System.Serializable]
    public class SoundEntryGroup
    {
        public SoundEntry[] entries;

        public SoundEntry GetRandomEntry()
        {
            if (entries == null || entries.Length == 0) return null;
            int index = Random.Range(0, entries.Length);
            return entries[index];
        }

        public bool HasEntries => entries != null && entries.Length > 0;
    }

    public enum SoundEffectType
    {
        Reverb,
        Distortion
    }

    [System.Serializable]
    public class SoundEffectConfig
    {
        public SoundEffectType type = SoundEffectType.Reverb;
        [Range(0f, 10f)] public float weight = 1f;

        // Reverb settings
        public AudioReverbPreset reverbPreset = AudioReverbPreset.Off;

        // Distortion settings
        [Range(0f, 1f)] public float distortionLevel = 0.5f;
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
        AttackHitResult,
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