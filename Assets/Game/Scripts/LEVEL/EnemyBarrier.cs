using UnityEngine;

namespace junklite
{
    /// <summary>
    /// An invisible wall that only enemies collide with. Put it across a doorway to keep enemies out of a
    /// free-move (Z plane) room or inside a boss room, however open the door is. The player, projectiles and
    /// everything else pass through. Needs a BoxCollider.
    /// It lives on its own layer (<see cref="LayerName"/>): the player controller's wall and ledge probes and the
    /// enemy brains' obstacle probes use masks that don't include it, so only enemy bodies feel it. The layer
    /// collision overrides are set at runtime so nothing has to be authored in the physics matrix.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class EnemyBarrier : MonoBehaviour
    {
        public const string LayerName = "EnemyBarrier";

        // The player controller builds its wall sweep from the layer matrix and queries ignore a collider's
        // exclude layers, so the matrix itself must say that only enemy bodies touch this layer. Done before
        // any scene loads (the controller caches its mask on first use) and again in Awake for safety.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureLayerMatrix()
        {
            int barrier = LayerMask.NameToLayer(LayerName);
            if (barrier < 0) return;
            int enemies = LayerMask.NameToLayer("Enemies");
            for (int i = 0; i < 32; i++)
                Physics.IgnoreLayerCollision(barrier, i, i != enemies);
        }

        private void Awake()
        {
            ConfigureLayerMatrix();
            int layer = LayerMask.NameToLayer(LayerName);
            if (layer >= 0) gameObject.layer = layer;
            var box = GetComponent<BoxCollider>();
            box.isTrigger = false;
            int enemies = LayerMask.NameToLayer("Enemies");
            // Exclude every layer but Enemies: this collider then only ever touches enemy bodies.
            if (enemies >= 0) box.excludeLayers = ~(1 << enemies);
        }

        private void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.9f, 0.2f, 0.2f, 0.18f);
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(0.9f, 0.2f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
