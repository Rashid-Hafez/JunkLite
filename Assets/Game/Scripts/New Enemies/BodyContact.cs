using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Body-touch tests for contact rules. Player and enemy bodies do not collide
    /// (layer matrix), so contact is measured instead of reported by physics.
    /// Used by: EnemyContactDamage (shove), BruteBrain (grab on touch).
    /// </summary>
    public static class BodyContact
    {
        /// <summary>
        /// True when the two solid bodies overlap at least <paramref name="minDepth"/> metres.
        /// The exact test (<see cref="Physics.ComputePenetration"/>) runs only when the bounds meet.
        /// </summary>
        public static bool IsOverlapping(Collider a, Collider b, float minDepth)
        {
            if (a == null || b == null || !a.enabled || !b.enabled || !a.bounds.Intersects(b.bounds))
                return false;

            Transform ta = a.transform;
            Transform tb = b.transform;
            return Physics.ComputePenetration(
                       a, ta.position, ta.rotation,
                       b, tb.position, tb.rotation,
                       out _, out float depth)
                   && depth >= minDepth;
        }

        /// <summary>The first non-trigger collider on the object itself, or null.</summary>
        public static Collider FindSolidCollider(Component owner)
        {
            foreach (Collider candidate in owner.GetComponents<Collider>())
            {
                if (!candidate.isTrigger)
                    return candidate;
            }

            return null;
        }
    }
}
