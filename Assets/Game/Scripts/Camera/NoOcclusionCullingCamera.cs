using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Marker for a Cinemachine camera: while it is live, the rendering camera skips baked occlusion
    /// culling (<see cref="CameraOcclusionFader"/> applies it). Use it on cameras that sit outside the
    /// baked occlusion data, where culling is wrong and walls flicker as the camera moves (BasementCam
    /// sits at z -44, outside it). Costs a few extra draw calls only while that camera is live.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NoOcclusionCullingCamera : MonoBehaviour
    {
    }
}
