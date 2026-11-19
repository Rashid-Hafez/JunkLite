using UnityEngine;

namespace junklite
{
    [CreateAssetMenu(menuName = "Junklite/VFX/Pulse Barrier Visual Profile")]
    public sealed class PulseBarrierVisualProfile : ScriptableObject
    {
        public Material arcMaterial;
        [ColorUsage(false, true)] public Color color = new Color(0.05f, 1.8f, 2.7f, 1f);
        [Tooltip("Horizontal/vertical radii, relative to half the player's collider height.")]
        public Vector2 radiusScale = new Vector2(1.05f, 1.3f);
        [Range(0.03f, 0.35f)] public float ribbonWidth = 0.18f;
        [Range(0f, 3f)] public float orbitSpeed = 1f;
        [Range(0f, 1f)] public float opacity = 1f;
        [Min(0.05f)] public float activationDuration = 0.25f;
        [Min(0.05f)] public float breakDuration = 0.5f;
        [Min(0.05f)] public float expiryDuration = 0.3f;
    }
}
