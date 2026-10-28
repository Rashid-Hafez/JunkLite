using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Centralized manager for all combat visual effects.
    /// Handles environment hit particles, enemy hit VFX, enemy hurt particles, hit cross VFX, and blood splatters.
    /// </summary>
    public class CombatEffectsManager : MonoBehaviour
    {
        public static CombatEffectsManager Instance { get; private set; }

        [Header("Environment Hit VFX")]
        [SerializeField] private GameObject envHitParticlePrefab;
        [SerializeField] private int envHitPoolSize = 8;
        [SerializeField] private float envHitLifetime = 0.2f;

        [Header("Enemy Hit VFX")]
        [SerializeField] private GameObject enemyHitVFXPrefab;
        [SerializeField] private int enemyHitVFXPoolSize = 8;
        [SerializeField] private float enemyHitVFXLifetime = 0.3f;
        [SerializeField] private Vector3 enemyHitVFXLocalOffset = Vector3.zero;
        [Tooltip("Random roll in degrees, either way, so repeated hits don't look identical.")]
        [SerializeField, Range(0f, 45f)] private float enemyHitVFXRandomRoll = 12f;
        [Tooltip("Random shift in metres on screen, so repeated hits don't stack on one spot.")]
        [SerializeField, Min(0f)] private float enemyHitVFXRandomOffset = 0.25f;

        [Header("Enemy Hurt Particle (Blood)")]
        [SerializeField] private GameObject enemyHurtParticlePrefab;
        [SerializeField] private int enemyHurtPoolSize = 8;
        [SerializeField] private float enemyHurtLifetime = 0.5f;

        [Header("Hit Cross VFX")]
        [SerializeField] private GameObject hitCrossPrefab;
        [SerializeField] private int hitCrossPoolSize = 8;
        [SerializeField] private float hitCrossLifetime = 0.12f;
        [SerializeField] private float hitCrossSize = 4f;

        [Header("Blood Splatter (Ground Decals)")]
        [SerializeField] private GameObject bloodSplatterPrefab;
        [SerializeField] private int bloodSplatterPoolSize = 50;
        [SerializeField] private float bloodSplatterLifetime = 5f;

        // Standard pools (Queue-based, return after delay)
        private readonly Queue<GameObject> envHitPool = new();
        private readonly Queue<GameObject> enemyHitVFXPool = new();
        private readonly Queue<GameObject> enemyHurtPool = new();
        private readonly Queue<GameObject> hitCrossPool = new();
        private readonly Queue<GameObject> bloodSplatterPool = new();

        // Pool roots
        private Transform envHitPoolRoot;
        private Transform enemyHitVFXPoolRoot;
        private Transform enemyHurtPoolRoot;
        private Transform hitCrossPoolRoot;
        private Transform bloodSplatterPoolRoot;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializePools();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void InitializePools()
        {
            if (envHitParticlePrefab != null)
            {
                envHitPoolRoot = CreatePoolRoot("EnvHitPool");
                FillPool(envHitPool, envHitParticlePrefab, envHitPoolRoot, envHitPoolSize);
            }

            if (enemyHitVFXPrefab != null)
            {
                enemyHitVFXPoolRoot = CreatePoolRoot("EnemyHitVFXPool");
                FillPool(enemyHitVFXPool, enemyHitVFXPrefab, enemyHitVFXPoolRoot, enemyHitVFXPoolSize);
            }

            if (enemyHurtParticlePrefab != null)
            {
                enemyHurtPoolRoot = CreatePoolRoot("EnemyHurtPool");
                FillPool(enemyHurtPool, enemyHurtParticlePrefab, enemyHurtPoolRoot, enemyHurtPoolSize);
            }

            if (hitCrossPrefab != null)
            {
                hitCrossPoolRoot = CreatePoolRoot("HitCrossPool");
                FillPool(hitCrossPool, hitCrossPrefab, hitCrossPoolRoot, hitCrossPoolSize);
            }

            // Blood splatters use standard pool with lifetime
            if (bloodSplatterPrefab != null)
            {
                bloodSplatterPoolRoot = CreatePoolRoot("BloodSplatterPool");
                FillPool(bloodSplatterPool, bloodSplatterPrefab, bloodSplatterPoolRoot, bloodSplatterPoolSize);
            }
        }

        private Transform CreatePoolRoot(string name)
        {
            var poolObj = new GameObject(name);
            poolObj.transform.SetParent(transform);
            return poolObj.transform;
        }

        private void FillPool(Queue<GameObject> pool, GameObject prefab, Transform root, int size)
        {
            for (int i = 0; i < size; i++)
            {
                var go = Instantiate(prefab, root);
                go.SetActive(false);
                pool.Enqueue(go);
            }
        }

        #region Public Spawn Methods

        /// <summary>
        /// Where enemy hit VFX go: the middle of the drawn body (else the hit collider's
        /// center), pulled toward the camera so the effect draws over the sprite.
        /// </summary>
        public static Vector3 BodyHitPoint(Collider target)
        {
            EnemyCharacter enemy = target.GetComponentInParent<EnemyCharacter>();
            EnemyAnimationPresenter presenter = enemy != null ? enemy.AnimationPresenter : null;
            Vector3 point = presenter != null && presenter.TryGetBodyCenter(out Vector3 center)
                ? center
                : target.bounds.center;
            Camera camera = Camera.main;
            return camera != null ? point - camera.transform.forward * 0.3f : point;
        }

        /// <summary>
        /// Spawn environment hit particle at position with direction.
        /// </summary>
        public void SpawnEnvHitParticle(Vector3 position, Vector3 attackDirection)
        {
            if (envHitParticlePrefab == null)
                return;

            GameObject go = GetFromPool(envHitPool, envHitParticlePrefab, envHitPoolRoot);
            SetupParticle(go, position, attackDirection);
            StartCoroutine(ReturnAfterDelay(go, envHitPool, envHitPoolRoot, envHitLifetime));
        }

        /// <summary>
        /// Spawn enemy hit VFX at position with direction (impact effect).
        /// </summary>
        public void SpawnEnemyHitVFX(Vector3 position, Vector3 attackDirection)
        {
            if (enemyHitVFXPrefab == null)
                return;

            GameObject go = GetFromPool(enemyHitVFXPool, enemyHitVFXPrefab, enemyHitVFXPoolRoot);
            SetupParticle(go, position, attackDirection,
                enemyHitVFXLocalOffset + (Vector3)(Random.insideUnitCircle * enemyHitVFXRandomOffset));
            go.transform.Rotate(0f, 0f, Random.Range(-enemyHitVFXRandomRoll, enemyHitVFXRandomRoll), Space.Self);
            StartCoroutine(ReturnAfterDelay(go, enemyHitVFXPool, enemyHitVFXPoolRoot, enemyHitVFXLifetime));
        }

        /// <summary>
        /// Spawn enemy hurt particle at position with direction (blood splatter).
        /// </summary>
        public void SpawnEnemyHurtParticle(Vector3 position, Vector3 attackDirection)
        {
            if (enemyHurtParticlePrefab == null)
                return;

            GameObject go = GetFromPool(enemyHurtPool, enemyHurtParticlePrefab, enemyHurtPoolRoot);
            SetupParticle(go, position, attackDirection);
            StartCoroutine(ReturnAfterDelay(go, enemyHurtPool, enemyHurtPoolRoot, enemyHurtLifetime));
        }

        /// <summary>
        /// Spawn hit cross at position.
        /// </summary>
        public void SpawnHitCross(Vector3 position)
        {
            if (hitCrossPrefab == null)
                return;

            GameObject go = GetFromPool(hitCrossPool, hitCrossPrefab, hitCrossPoolRoot);
            go.transform.SetParent(null);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * hitCrossSize;
            go.SetActive(true);

            StartCoroutine(ReturnAfterDelay(go, hitCrossPool, hitCrossPoolRoot, hitCrossLifetime));
        }

        /// <summary>
        /// Spawn blood splatter decal at position with surface normal.
        /// </summary>
        public void SpawnBloodSplatter(Vector3 position, Vector3 normal)
        {
            if (bloodSplatterPrefab == null)
                return;

            GameObject go = GetFromPool(bloodSplatterPool, bloodSplatterPrefab, bloodSplatterPoolRoot);

            // Random rotation around the normal
            Quaternion rot = Quaternion.LookRotation(normal) * Quaternion.Euler(90f, 0f, Random.Range(0f, 360f));

            go.transform.SetParent(null);
            go.transform.SetPositionAndRotation(position, rot);
            go.SetActive(true);

            StartCoroutine(ReturnAfterDelay(go, bloodSplatterPool, bloodSplatterPoolRoot, bloodSplatterLifetime));
        }

        #endregion

        #region Pool Helpers

        private GameObject GetFromPool(Queue<GameObject> pool, GameObject prefab, Transform poolRoot)
        {
            if (pool.Count > 0)
                return pool.Dequeue();

            return Instantiate(prefab, poolRoot);
        }

        private void SetupParticle(GameObject go, Vector3 position, Vector3 attackDirection, Vector3 localOffset = default)
        {
            go.transform.SetParent(null);

            // Lay the effect in the camera's view plane (+Z away from the camera), rolled toward
            // the attack on screen, so it reads the same on X and Z lanes.
            Camera camera = Camera.main;
            Quaternion view = camera != null
                ? Quaternion.LookRotation(camera.transform.forward, Vector3.up)
                : Quaternion.identity;
            // Leftward hits mirror instead of rolling 180°, so the effect isn't upside down.
            float mirror = 1f;
            if (attackDirection != Vector3.zero)
            {
                Vector3 local = Quaternion.Inverse(view) * attackDirection;
                mirror = local.x < 0f ? -1f : 1f;
                view *= Quaternion.Euler(0f, 0f, Mathf.Atan2(local.y, Mathf.Abs(local.x)) * mirror * Mathf.Rad2Deg);
            }
            go.transform.rotation = view;
            go.transform.localScale = new Vector3(mirror, 1f, 1f);

            go.transform.position = position + go.transform.TransformVector(localOffset);
            go.SetActive(true);

            // Wrapper prefabs keep the particle system on a child.
            var ps = go.GetComponentInChildren<ParticleSystem>();
            if (ps != null)
            {
                ps.Clear(true);
                ps.Play(true);
            }
        }

        private IEnumerator ReturnAfterDelay(GameObject go, Queue<GameObject> pool, Transform poolRoot, float delay)
        {
            yield return new WaitForSeconds(delay);
            go.SetActive(false);
            go.transform.SetParent(poolRoot, false);
            pool.Enqueue(go);
        }

        #endregion
    }
}