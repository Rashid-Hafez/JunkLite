using UnityEngine;

namespace junklite
{
    /// <summary>
    /// The lane characters move along in lane-only rooms. Waypoints are the child transforms,
    /// in hierarchy order; every segment must run along world X or world Z (see MAIN_MECHANICS.md).
    /// Corners between segments are where a CameraSwitchTrigger rotates the world.
    /// </summary>
    public class LanePath : MonoBehaviour
    {
        private const float AxisTolerance = 0.01f;

        public int PointCount => transform.childCount;
        public Vector3 GetPoint(int index) => transform.GetChild(index).position;

        /// <summary>
        /// Closest point on the path to <paramref name="worldPosition"/>, ignoring height.
        /// The returned point keeps the input Y. <paramref name="laneYRotation"/> is the Y rotation
        /// a character needs so its transform.right points along that segment.
        /// </summary>
        public bool TryGetClosestPoint(Vector3 worldPosition, out Vector3 point, out float laneYRotation)
        {
            point = worldPosition;
            laneYRotation = 0f;
            if (PointCount < 2) return false;

            float bestSqr = float.MaxValue;
            Vector2 p = new Vector2(worldPosition.x, worldPosition.z);

            for (int i = 0; i < PointCount - 1; i++)
            {
                Vector3 a3 = GetPoint(i);
                Vector3 b3 = GetPoint(i + 1);
                Vector2 a = new Vector2(a3.x, a3.z);
                Vector2 b = new Vector2(b3.x, b3.z);
                Vector2 ab = b - a;
                if (ab.sqrMagnitude < 0.0001f) continue;

                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                Vector2 c = a + ab * t;
                float sqr = (p - c).sqrMagnitude;
                if (sqr >= bestSqr) continue;

                bestSqr = sqr;
                point = new Vector3(c.x, worldPosition.y, c.y);
                laneYRotation = YRotationFor(ab);
            }

            return bestSqr < float.MaxValue;
        }

        /// <summary>Y rotation whose transform.right points along <paramref name="direction"/> (XZ).</summary>
        public static float YRotationFor(Vector2 direction)
        {
            // transform.right for Y rotation θ is (cos θ, 0, -sin θ).
            return Mathf.Round(Mathf.Atan2(-direction.y, direction.x) * Mathf.Rad2Deg / 90f) * 90f;
        }

        public bool IsSegmentAxisAligned(int index)
        {
            Vector3 d = GetPoint(index + 1) - GetPoint(index);
            return Mathf.Abs(d.x) < AxisTolerance || Mathf.Abs(d.z) < AxisTolerance;
        }

        private void OnDrawGizmos()
        {
            for (int i = 0; i < PointCount - 1; i++)
            {
                Gizmos.color = IsSegmentAxisAligned(i) ? Color.cyan : Color.red;
                Gizmos.DrawLine(GetPoint(i), GetPoint(i + 1));
            }

            Gizmos.color = Color.cyan;
            for (int i = 0; i < PointCount; i++)
                Gizmos.DrawWireSphere(GetPoint(i), 0.3f);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            for (int i = 0; i < PointCount - 1; i++)
            {
                if (!IsSegmentAxisAligned(i))
                    Debug.LogWarning($"[LanePath] '{name}' segment {i} is not along world X or Z; characters cannot follow it.", this);
            }
        }
#endif
    }
}
