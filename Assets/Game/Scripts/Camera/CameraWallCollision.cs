using Unity.Cinemachine;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Camera collision for the fixed-angle cameras (Follow only, no Aim). When a collider on the
    /// CameraCollision layer sits between the Follow target and the camera, the camera is pulled in
    /// along that line to just in front of it. Position only: the rotation is never touched.
    /// Use this instead of CinemachineDeoccluder on cameras without an Aim component. The Deoccluder
    /// re-aims at LookAt after a pull, and with no Aim component that rotation is written back into
    /// the camera's transform and builds up (the camera ends up facing the ceiling or away from Cass).
    /// To block the camera, put a BoxCollider on the CameraCollision layer (Colliders/Camera).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Cinemachine/JunkLite Camera Wall Collision")]
    public sealed class CameraWallCollision : CinemachineExtension
    {
        public const string LayerName = "CameraCollision";

        [Tooltip("Layers that block the camera. Defaults to CameraCollision.")]
        [SerializeField] private LayerMask collideAgainst;
        [Tooltip("How far the camera stays from a blocking surface.")]
        [SerializeField, Min(0.01f)] private float cameraRadius = 0.3f;
        [Tooltip("Cast origin, relative to the Follow target (the pivot is at the feet).")]
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1f, 0f);

        private int mask;

        private void Reset() => collideAgainst = LayerMask.GetMask(LayerName);

        protected override void OnEnable()
        {
            base.OnEnable();
            mask = collideAgainst.value != 0 ? collideAgainst.value : LayerMask.GetMask(LayerName);
        }

        protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
            CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Body || mask == 0) return;
            Transform follow = vcam.Follow;
            if (follow == null) return;

            Vector3 origin = follow.position + targetOffset;
            Vector3 cameraPosition = state.GetCorrectedPosition();
            Vector3 toCamera = cameraPosition - origin;
            float distance = toCamera.magnitude;
            if (distance < 0.001f) return;

            Vector3 direction = toCamera / distance;
            if (Physics.SphereCast(origin, cameraRadius, direction, out RaycastHit hit, distance, mask,
                    QueryTriggerInteraction.Ignore))
                state.PositionCorrection += origin + direction * hit.distance - cameraPosition;
        }
    }
}
