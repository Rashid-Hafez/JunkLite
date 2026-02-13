using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Simple VFX pooling system. Reuses VFX instances instead of creating/destroying.
    /// 
    /// Usage:
    /// - VFXPool.Get(prefab, parent) - gets a pooled instance or creates new
    /// - VFXPool.Release(instance) - returns to pool (deactivates, unparents)
    /// 
    /// Automatically creates pools per prefab. No setup required.
    /// </summary>
    public class VFXPool : MonoBehaviour
    {
        private static VFXPool instance;
        public static VFXPool Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("[VFXPool]");
                    instance = go.AddComponent<VFXPool>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        // Pool storage: prefab ID -> list of inactive instances
        private Dictionary<int, Queue<GameObject>> pools = new Dictionary<int, Queue<GameObject>>();

        // Track which prefab each instance came from (for returning to correct pool)
        private Dictionary<GameObject, int> instanceToPrefabId = new Dictionary<GameObject, int>();

        /// <summary>
        /// Get a VFX instance from pool (or create new if pool empty).
        /// </summary>
        public static GameObject Get(GameObject prefab, Transform parent, float scale = 1f)
        {
            if (prefab == null) return null;
            return Instance.GetInternal(prefab, parent, scale);
        }

        /// <summary>
        /// Return a VFX instance to the pool.
        /// </summary>
        public static void Release(ref GameObject instance)
        {
            if (instance == null) return;
            Instance.ReleaseInternal(instance);
            instance = null;
        }

        private GameObject GetInternal(GameObject prefab, Transform parent, float scale)
        {
            int prefabId = prefab.GetInstanceID();

            // Try get from pool
            if (pools.TryGetValue(prefabId, out var pool) && pool.Count > 0)
            {
                var obj = pool.Dequeue();
                obj.transform.SetParent(parent);
                obj.transform.localPosition = Vector3.zero;
                obj.transform.localRotation = Quaternion.identity;
                obj.transform.localScale = Vector3.one * scale;
                obj.SetActive(true);
                return obj;
            }

            // Create new
            var newObj = Instantiate(prefab, parent);
            newObj.transform.localPosition = Vector3.zero;
            newObj.transform.localScale = Vector3.one * scale;

            // Track which prefab it came from
            instanceToPrefabId[newObj] = prefabId;

            return newObj;
        }

        private void ReleaseInternal(GameObject obj)
        {
            if (obj == null) return;

            // Find which pool it belongs to
            if (!instanceToPrefabId.TryGetValue(obj, out int prefabId))
            {
                // Not from pool - just destroy
                Destroy(obj);
                return;
            }

            // Deactivate and unparent
            obj.SetActive(false);
            obj.transform.SetParent(transform);

            // Return to pool
            if (!pools.TryGetValue(prefabId, out var pool))
            {
                pool = new Queue<GameObject>();
                pools[prefabId] = pool;
            }
            pool.Enqueue(obj);
        }

        /// <summary>
        /// Clear all pools (call on scene unload if needed).
        /// </summary>
        public static void ClearAll()
        {
            if (instance == null) return;
            instance.ClearAllInternal();
        }

        private void ClearAllInternal()
        {
            foreach (var pool in pools.Values)
            {
                while (pool.Count > 0)
                {
                    var obj = pool.Dequeue();
                    if (obj != null) Destroy(obj);
                }
            }
            pools.Clear();
            instanceToPrefabId.Clear();
        }

        private void OnDestroy()
        {
            ClearAllInternal();
            if (instance == this) instance = null;
        }
    }
}