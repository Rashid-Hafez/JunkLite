using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Enemy trigger volumes (perception spheres, attack hitboxes) live on the
    /// <c>EnemySensor</c> layer, which collides with Player and Damageable only.
    /// Enemy bodies stay on <c>Enemies</c>, which no longer collides with Player,
    /// so the player passes through enemies while enemies stay solid against each
    /// other and the level. Sensors call this from Awake so a prefab or scene
    /// override that leaves a trigger on the body layer cannot silently blind
    /// the enemy: the layer is enforced by code, the prefab only mirrors it.
    /// </summary>
    public static class EnemySensorLayer
    {
        public const string LayerName = "EnemySensor";

        private static int cachedLayer = -2;

        // Fast Enter Play Mode keeps statics between sessions; forget the cached index.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => cachedLayer = -2;

        public static int Layer
        {
            get
            {
                if (cachedLayer == -2)
                    cachedLayer = LayerMask.NameToLayer(LayerName);
                return cachedLayer;
            }
        }

        public static void Apply(GameObject sensor)
        {
            int layer = Layer;
            if (layer < 0)
            {
                Debug.LogWarning(
                    $"[{sensor.name}] Layer '{LayerName}' is missing from the Tag Manager; enemy sensors will collide like bodies.",
                    sensor);
                return;
            }

            if (sensor.layer != layer)
                sensor.layer = layer;
        }
    }
}
