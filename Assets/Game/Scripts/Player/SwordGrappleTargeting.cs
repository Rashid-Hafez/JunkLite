using UnityEngine;

namespace junklite
{
    public readonly struct SwordGrappleTarget
    {
        public readonly Collider Collider;
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        public readonly Vector3 PlayerPosition;

        public SwordGrappleTarget(Collider collider, Vector3 point, Vector3 normal, Vector3 playerPosition)
        {
            Collider = collider;
            Point = point;
            Normal = normal;
            PlayerPosition = playerPosition;
        }
    }

    /// <summary>Shared by the preview and throw so a marker cannot promise an unreachable anchor.</summary>
    public static class SwordGrappleTargeting
    {
        // Keep trigger anchors separate from solid obstacles. A single Collide
        // query on the obstruction mask would also hit camera/gameplay volumes.
        public static bool TryRaycast(Vector3 origin, Vector3 direction, float distance,
            SwordGrappleSettings settings, out RaycastHit hit)
        {
            int mask = settings.obstructionLayers | settings.attachableLayers;
            bool found = Physics.Raycast(origin, direction, out hit, distance,
                mask, QueryTriggerInteraction.Ignore);
            if (settings.allowTriggerAnchors && Physics.Raycast(origin, direction, out var anchorHit,
                    distance, settings.attachableLayers, QueryTriggerInteraction.Collide) &&
                (!found || anchorHit.distance < hit.distance))
            {
                hit = anchorHit;
                found = true;
            }
            return found;
        }

        public static bool TryResolve(CapsuleCollider body, Vector3 movementAxis, Vector3 direction,
            SwordGrappleSettings settings, out SwordGrappleTarget target, out Vector3 previewEnd)
        {
            return TryResolve(body, movementAxis, direction, settings, out target, out previewEnd, out _);
        }

        public static bool TryResolve(CapsuleCollider body, Vector3 movementAxis, Vector3 direction,
            SwordGrappleSettings settings, out SwordGrappleTarget target, out Vector3 previewEnd, out string failure)
        {
            target = default;
            failure = "Aim away from the player.";
            // Rigidbody interpolation can leave the rendered Transform behind the
            // physics pose. Validate travel from the pose that will actually move.
            Vector3 rootPosition = body.attachedRigidbody != null
                ? body.attachedRigidbody.position : body.transform.position;
            GetCapsule(body, rootPosition, out Vector3 top, out Vector3 bottom, out float radius);
            Vector3 center = (top + bottom) * 0.5f;
            Vector3 origin = center + Vector3.up * settings.handHeightAboveCenter;
            Vector3 planeNormal = Vector3.Cross(movementAxis, Vector3.up).normalized;
            direction = Vector3.ProjectOnPlane(direction, planeNormal).normalized;
            previewEnd = origin + direction * settings.maximumRange;
            if (direction.sqrMagnitude < 0.5f) return false;

            // Blockers and valid anchors are deliberately different masks.
            int mask = settings.obstructionLayers | settings.attachableLayers;
            failure = "No wall within the maximum grapple range.";
            if (!TryRaycast(origin, direction, settings.maximumRange, settings, out RaycastHit hit)) return false;
            previewEnd = hit.point;
            target = new SwordGrappleTarget(hit.collider, hit.point, hit.normal, rootPosition);
            failure = "The ray hit the player's own collision geometry.";
            if (hit.collider.transform.IsChildOf(body.transform)) return false;
            failure = "The target is too close to throw at.";
            if (hit.distance < settings.minimumThrowDistance) return false;
            failure = "The first collider is not on an attachable layer.";
            if ((settings.attachableLayers.value & (1 << hit.collider.gameObject.layer)) == 0) return false;
            failure = "The wall has a Rigidbody; this prototype requires a static anchor.";
            if (hit.rigidbody != null) return false;
            failure = "Aim at the vertical side of the wall, rather than its top or underside.";
            if (Mathf.Abs(Vector3.Dot(hit.normal, Vector3.up)) > settings.maximumWallUpDot) return false;
            failure = "The wall faces outside the player's current movement plane.";
            if (Mathf.Abs(Vector3.Dot(hit.normal, planeNormal)) > 0.15f) return false;

            Vector3 normal = Vector3.ProjectOnPlane(hit.normal, planeNormal).normalized;
            // Account for capsule height on sloped walls, not just its radius.
            float support = radius + Vector3.Distance(top, bottom) * 0.5f * Mathf.Abs(normal.y);
            // Lower the capsule beneath the hand along the wall's tangent. A
            // purely vertical offset changes clearance on a sloped wall.
            Vector3 handOffset = Vector3.ProjectOnPlane(Vector3.up * settings.handHeightAboveCenter, normal);
            Vector3 desiredCenter = hit.point + normal * (support + settings.wallClearance) - handOffset;
            Vector3 destination = rootPosition + desiredCenter - center;
            destination -= planeNormal * Vector3.Dot(destination - rootPosition, planeNormal);
            GetCapsule(body, destination, out top, out bottom, out radius);
            failure = "There is not enough room for the player at that point. Aim higher or away from the ceiling.";
            if (Physics.CheckCapsule(top, bottom, radius * 0.98f, mask,
                    QueryTriggerInteraction.Ignore)) return false;

            Vector3 travel = destination - rootPosition;
            GetCapsule(body, rootPosition, out top, out bottom, out radius);
            failure = "Solid collision geometry blocks the player's path to the wall.";
            if (travel.sqrMagnitude > 0.0001f && Physics.CapsuleCast(top, bottom, radius * 0.98f,
                    travel.normalized, out _, travel.magnitude, mask, QueryTriggerInteraction.Ignore))
                return false;

            target = new SwordGrappleTarget(hit.collider, hit.point, normal, destination);
            failure = null;
            return true;
        }

        public static void GetCapsule(CapsuleCollider body, Vector3 rootPosition,
            out Vector3 top, out Vector3 bottom, out float radius)
        {
            // The player capsule is vertical; root rotations only turn around Y.
            Vector3 scale = body.transform.lossyScale;
            radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            float halfSegment = Mathf.Max(0f, body.height * Mathf.Abs(scale.y) * 0.5f - radius);
            Vector3 center = body.transform.TransformPoint(body.center) + rootPosition - body.transform.position;
            top = center + Vector3.up * halfSegment;
            bottom = center - Vector3.up * halfSegment;
        }

        public static bool TryGetNearbyWall(CapsuleCollider capsule, Vector3 movementAxis, float input,
            SwordGrappleSettings settings, out RaycastHit hit)
        {
            hit = default;
            if (settings == null || capsule == null || Mathf.Abs(input) <= 0.1f) return false;
            Vector3 position = capsule.attachedRigidbody != null ? capsule.attachedRigidbody.position : capsule.transform.position;
            GetCapsule(capsule, position, out var top, out var bottom, out float radius);
            Vector3 direction = movementAxis * Mathf.Sign(input);
            if (!TryRaycast((top + bottom) * 0.5f, direction, radius + settings.wallContactDistance, settings, out hit))
                return false;
            return !hit.collider.transform.IsChildOf(capsule.transform) && hit.rigidbody == null &&
                   (settings.attachableLayers.value & (1 << hit.collider.gameObject.layer)) != 0 &&
                   Mathf.Abs(hit.normal.y) <= settings.maximumWallUpDot && Vector3.Dot(hit.normal, -direction) > 0.9f;
        }
    }
}
