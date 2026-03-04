using UnityEngine;
using System;
using System.Collections.Generic;

namespace junklite
{
    public class ProjectileManager : MonoBehaviour
    {
        public static ProjectileManager Instance { get; private set; }

        [Header("Pool Settings")]
        [Tooltip("How many bullets to pre-warm per prefab on startup. Set to 0 to disable.")]
        [SerializeField] private int prewarmCount = 10;

        [Tooltip("Parent transform for pooled bullets. Keeps the hierarchy clean.")]
        [SerializeField] private Transform poolParent;

        // One queue per prefab. Key is the prefab GameObject (not an instance).
        private readonly Dictionary<GameObject, Queue<Bullet>> pools = new();


        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (poolParent == null)
            {
                var poolGo = new GameObject("BulletPool");
                poolGo.transform.SetParent(transform);
                poolParent = poolGo.transform;
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public Bullet FireBullet(
            GameObject prefab,
            BulletConfig config,
            LayerMask enemyLayer,
            LayerMask environmentLayer,
            Action<Collider> onEnemyHit)
        {
            if (prefab == null)
            {
                Debug.LogWarning("[ProjectileManager] FireBullet called with null prefab.");
                return null;
            }

            var bullet = GetFromPool(prefab);

            bullet.gameObject.SetActive(true);
            bullet.Initialize(
                config,
                enemyLayer,
                environmentLayer,
                onEnemyHit,
                () => ReturnToPool(prefab, bullet)   // bullet calls this when done
            );

            return bullet;
        }

        public void PrewarmPool(GameObject prefab, int count)
        {
            if (prefab == null) return;

            if (!pools.ContainsKey(prefab))
                pools[prefab] = new Queue<Bullet>();

            for (int i = 0; i < count; i++)
            {
                var bullet = InstantiateBullet(prefab);
                bullet.gameObject.SetActive(false);
                pools[prefab].Enqueue(bullet);
            }
        }


        private Bullet GetFromPool(GameObject prefab)
        {
            if (!pools.ContainsKey(prefab))
                pools[prefab] = new Queue<Bullet>();

            var pool = pools[prefab];

            // Find an inactive bullet in the queue
            while (pool.Count > 0)
            {
                var candidate = pool.Dequeue();
                if (candidate != null && !candidate.gameObject.activeSelf)
                    return candidate;
                // candidate was destroyed or already active (shouldn't happen) — skip it
            }

            // Pool exhausted — instantiate a new one
            return InstantiateBullet(prefab);
        }

        private void ReturnToPool(GameObject prefab, Bullet bullet)
        {
            if (bullet == null) return;

            bullet.gameObject.SetActive(false);

            if (!pools.ContainsKey(prefab))
                pools[prefab] = new Queue<Bullet>();

            pools[prefab].Enqueue(bullet);
        }

        private Bullet InstantiateBullet(GameObject prefab)
        {
            var go = Instantiate(prefab, poolParent);
            var bullet = go.GetComponent<Bullet>();

            if (bullet == null)
            {
                Debug.LogError($"[ProjectileManager] Prefab '{prefab.name}' is missing a Bullet component!");
                bullet = go.AddComponent<Bullet>();
            }

            go.SetActive(false);
            return bullet;
        }
    }
}