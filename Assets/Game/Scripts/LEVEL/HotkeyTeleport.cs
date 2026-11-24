using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace junklite
{
    /// <summary>
    /// Press the two keys together to move the player to this object, snapped onto the nearest lane with that
    /// lane's yaw, with corners and the lane camera synced as if she had walked there. Works in builds (a
    /// shortcut to a section for testing). Put it on an anchor object; the dev console's room buttons use
    /// the same <see cref="Teleport"/>.
    /// </summary>
    public sealed class HotkeyTeleport : MonoBehaviour
    {
        [SerializeField] private Key firstKey = Key.B;
        [SerializeField] private Key secondKey = Key.Digit9;

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || Time.timeScale <= 0f) return;

            var first = keyboard[firstKey];
            var second = keyboard[secondKey];
            if (!first.isPressed || !second.isPressed) return;
            if (!first.wasPressedThisFrame && !second.wasPressedThisFrame) return;

            PlayerLifecycle lifecycle = PlayerLifecycle.Instance;
            PlayerCharacter player = lifecycle != null ? lifecycle.Player : null;
            if (player != null && player.IsAlive) Teleport(player, transform);
        }

        /// <summary>Moves <paramref name="player"/> to <paramref name="anchor"/>. Returns a short result message.</summary>
        public static string Teleport(PlayerCharacter player, Transform anchor)
        {
            Vector3 target = anchor.position;
            float bestSqr = float.MaxValue;
            float yaw = 0f;
            foreach (LanePath lane in FindObjectsByType<LanePath>(FindObjectsSortMode.None))
            {
                if (!lane.TryGetClosestPoint(anchor.position, out Vector3 point, out float laneYaw))
                    continue;
                float sqr = (point - anchor.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                target = point;
                yaw = laneYaw;
            }
            if (bestSqr == float.MaxValue)
                return "No lane path in this scene";

            player.Controller.SnapToLane(target, yaw);

            CinemachineCamera laneCamera = null;
            foreach (CameraSwitchTrigger corner in FindObjectsByType<CameraSwitchTrigger>(FindObjectsSortMode.None))
                laneCamera = corner.SyncToLaneSide(yaw) ?? laneCamera;
            if (laneCamera != null && CameraManager.Instance != null)
                CameraManager.Instance.CutToLaneCamera(laneCamera);

            return $"Moved to {anchor.name}";
        }
    }
}
