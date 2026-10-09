using UnityEngine;

namespace junklite
{
    [CreateAssetMenu(menuName = "JunkLite/Traversal/Sword Grapple Settings")]
    public sealed class SwordGrappleSettings : ScriptableObject
    {
        [Header("Targeting")]
        [Min(0.5f)] public float maximumRange = 10f;
        [Min(0.1f)] public float minimumThrowDistance = 0.7f;
        [Min(0.1f)] public float minimumChainDistance = 1f;
        public LayerMask attachableLayers = 1 << 12;
        [Tooltip("Allow trigger colliders on the attachable layers to receive the sword. Other triggers are ignored.")]
        public bool allowTriggerAnchors = true;
        public LayerMask obstructionLayers = (1 << 0) | (1 << 11) | (1 << 12) | (1 << 13) | (1 << 14);
        [Range(0f, 0.7f)] public float maximumWallUpDot = 0.35f;
        [Tooltip("Gap in world units between the player capsule and the wall collider, not the rendered wall mesh.")]
        [Min(0.01f)] public float wallClearance = 0.025f;
        [Min(0f)] public float handHeightAboveCenter = 0.5f;

        [Header("Flight and Pull")]
        [Min(1f)] public float throwSpeed = 48f;
        [Tooltip("Seconds between sword impact and the start of the pull. Zero pulls immediately. An existing wall hold stays active during this delay.")]
        [Min(0f)] public float impactToPullDelay = 0.1f;
        [Min(1f)] public float pullSpeed = 22f;
        [Tooltip("Distance at which the wall-attach pose starts. The hold still settles all the way to the calculated capsule position.")]
        [Min(0.01f)] public float arrivalDistance = 0.06f;
        [Min(0.5f)] public float maximumPullDuration = 3f;

        [Header("Wall Hold, Slide and Jump")]
        [Tooltip("Maximum gap from the capsule surface for ordinary directional wall contact. Does not change grapple hold distance.")]
        [Min(0.01f)] public float wallContactDistance = 0.08f;
        [Min(0.1f)] public float wallSlideSpeed = 2f;
        [Min(0f)] public float wallJumpAwaySpeed = 6f;
        [Min(1f)] public float wallJumpUpSpeed = 12f;
        [Tooltip("Briefly preserve the jump's outward speed so holding toward the wall cannot immediately cancel it.")]
        [Min(0.02f)] public float wallJumpPushOffTime = 0.15f;

        [Header("Presentation")]
        [Tooltip("Edit this prefab's renderers, materials, rope width/tiling, marker and sword presentation.")]
        public SwordGrappleVisuals visualsPrefab;
    }
}
