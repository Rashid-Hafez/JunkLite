using UnityEngine;
using System.Collections.Generic;

namespace junklite
{
    /// <summary>
    /// Attach to blood particle system to spawn splatter decals on ground collision.
    /// </summary>
    public class BloodSplatter : MonoBehaviour
    {
        public LayerMask groundLayer;
        public Vector3 spawnOffset;

        private ParticleSystem ps;
        private List<ParticleCollisionEvent> collisionEvents = new List<ParticleCollisionEvent>();

        void Start()
        {
            ps = GetComponent<ParticleSystem>();
        }

        void OnParticleCollision(GameObject other)
        {
            if (((1 << other.layer) & groundLayer) == 0) return;

            int count = ps.GetCollisionEvents(other, collisionEvents);

            for (int i = 0; i < count; i++)
            {
                Vector3 pos = collisionEvents[i].intersection + spawnOffset;
                Vector3 normal = collisionEvents[i].normal;

                CombatEffectsManager.Instance?.SpawnBloodSplatter(pos, normal);
            }
        }
    }
}