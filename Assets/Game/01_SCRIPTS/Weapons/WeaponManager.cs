using UnityEngine;
using System.Collections.Generic;
using System.Collections;

namespace junklite
{
    [RequireComponent(typeof(Collider))]
    public class WeaponManager : MonoBehaviour
    {
        [Header("Weapon Holder")]
        public Transform weaponHolder;

        [Header("Attack Transforms (Scene Anchors)")]
        [SerializeField] private Transform sideAttack;
        [SerializeField] private Transform upAttack;
        [SerializeField] private Transform downAttack;

        [Header("Fallback Hit Radii")]
        [SerializeField] private float sideRadius = 1f;
        [SerializeField] private float upRadius = 1f;
        [SerializeField] private float downRadius = 1f;

        [Header("Hit Detection")]
        [SerializeField] private LayerMask enemyLayer;
        [SerializeField] private LayerMask environmentLayer;

        [Header("Knockback")]
        [SerializeField] private Vector2 defaultKnockback = new Vector2(8f, 4f);

        [Header("Environment Hit VFX")]
        [SerializeField] private GameObject hitParticlePrefab;
        [SerializeField] private int hitParticlePoolSize = 8;
        [SerializeField] private float hitParticleLifetime = 0.2f;
        [SerializeField] private Transform hitParticlePoolRoot;

        private readonly Queue<GameObject> hitParticlePool = new();

        [Header("Enemy Hit VFX")]
        [SerializeField] private GameObject enemyHitVFXPrefab;
        [SerializeField] private int enemyHitVFXPoolSize = 8;
        [SerializeField] private float enemyHitVFXLifetime = 0.3f;
        [SerializeField] private Transform enemyHitVFXPoolRoot;

        private readonly Queue<GameObject> enemyHitVFXPool = new();

        [Header("Hit Cross VFX")]
        [SerializeField] private GameObject hitCrossPrefab;
        [SerializeField] private int hitCrossPoolSize = 8;
        [SerializeField] private float hitCrossLifetime = 0.12f;
        [SerializeField] private float hitCrossSize = 4f;
        [SerializeField] private Transform hitCrossPoolRoot;

        private readonly Queue<GameObject> hitCrossPool = new();

        [Header("Feedback Settings")]
        [SerializeField] private Unity.Cinemachine.CinemachineImpulseSource impulseSource;
        [SerializeField] private float enemyHitHitstopDuration = 0.06f;
        [SerializeField] private float enemyHitShakeForce = 0.8f;
        [SerializeField] private float environmentHitHitstopDuration = 0.03f;
        [SerializeField] private float environmentHitShakeForce = 0.4f;

        [Header("Recoil")]
        [SerializeField] private float sideRecoil = 6f;

        [Header("Slash Pooling")]
        [SerializeField] private int poolSizePerSlash = 5;
        [SerializeField] private Transform slashPoolRoot;
        [SerializeField] private float slashOffsetDirection = 0.5f;
        [SerializeField] private float slashOffsetDistance = 0.5f;
        [SerializeField] private float slashScale = 1f;

        private readonly Dictionary<GameObject, Queue<GameObject>> slashPools = new();

        // ================== INTERNAL ==================
        private Rigidbody playerRb;
        private Transform playerTransform;
        private PlayerState playerState;
        private PlayerCharacter playerCharacter;

        public WeaponInstance CurrentWeapon { get; private set; }
        private WorldWeaponPickup storedPickup;

        public event System.Action OnWeaponChanged;

        // ================== PROPERTIES ==================
        public float Facing => Mathf.Sign(playerTransform.localScale.x);

        // ================== UNITY ==================
        private void Awake()
        {
            playerRb = GetComponentInParent<Rigidbody>();
            playerTransform = transform.parent ?? transform;
            playerState = GetComponentInParent<PlayerState>();
            playerCharacter = GetComponentInParent<PlayerCharacter>();

            // Find impulse source if not assigned
            if (impulseSource == null)
            {
                impulseSource = GetComponent<Unity.Cinemachine.CinemachineImpulseSource>()
                                ?? GetComponentInParent<Unity.Cinemachine.CinemachineImpulseSource>()
                                ?? GetComponentInChildren<Unity.Cinemachine.CinemachineImpulseSource>();
            }

            if (impulseSource == null)
                Debug.LogWarning("WeaponManager: CinemachineImpulseSource not found. Camera shake will be disabled.");
        }

        private void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current?.gKey.wasPressedThisFrame == true)
                DropWeapon();
        }

        private void OnTriggerEnter(Collider other)
        {
            var weaponPickup = other.GetComponent<WorldWeaponPickup>();
            if (weaponPickup != null && CurrentWeapon == null)
            {
                PickupWeapon(weaponPickup);
                return;
            }

            var modPickup = other.GetComponent<WorldModPickup>();
            if (modPickup != null)
                PickupMod(modPickup);
        }

        #region Attack

        public void Attack(AttackDirection dir)
        {
            if (CurrentWeapon == null)
                return;

            CurrentWeapon.ExecuteAttack(dir);
        }

        private void HandleWeaponAttack(AttackDirection dir, WeaponComboData.ComboStep step, int comboIndex)
        {
            if (playerState != null && comboIndex >= 0)
                playerState.TriggerComboAttack(comboIndex);

            Transform anchor = GetAttackTransform(dir);
            if (anchor == null)
                return;

            float fallbackRadius = GetFallbackRadius(dir);
            float finalRadius = step.hitRadius > 0f ? step.hitRadius : fallbackRadius;

            // --- HIT DETECTION ---
            Collider[] hits = Physics.OverlapSphere(
                anchor.position,
                finalRadius,
                enemyLayer | environmentLayer,
                QueryTriggerInteraction.Ignore
            );

            AttackHitResult result = AttackHitResult.None;
            Collider hitCollider = null;

            for (int i = 0; i < hits.Length; i++)
            {
                int mask = 1 << hits[i].gameObject.layer;

                if ((mask & enemyLayer) != 0)
                {
                    result = AttackHitResult.Enemy;
                    hitCollider = hits[i];

                    DealDamageToTarget(hits[i], step);
                    break;
                }

                if ((mask & environmentLayer) != 0)
                {
                    result = AttackHitResult.Environment;
                    hitCollider = hits[i];
                }
            }

            // --- IMPACT POINT ---
            Vector3 impactPoint = anchor.position;

            if (result != AttackHitResult.None)
            {
                impactPoint = ResolveImpactPoint(dir, anchor.position, finalRadius);

                // Spawn appropriate VFX based on hit type
                if (result == AttackHitResult.Environment)
                {
                    PlayHitParticle(impactPoint, dir);
                }
                else if (result == AttackHitResult.Enemy)
                {
                    PlayEnemyHitVFX(impactPoint, dir);
                }

                PlayHitCross(impactPoint);
                PlayHitFeedback(result);
            }

            // --- SLASH ---
            if (step.slashPrefab != null)
            {
                if (result != AttackHitResult.None)
                    PlaySlashAt(step.slashPrefab, anchor, impactPoint);
                else
                    PlaySlash(step.slashPrefab, anchor);
            }

            ApplyRecoil(dir, result);
        }

        #endregion Attack

        #region Damage

        private void DealDamageToTarget(Collider targetCollider, WeaponComboData.ComboStep step)
        {
            var damageable = targetCollider.GetComponent<IDamageable>()
                          ?? targetCollider.GetComponentInParent<IDamageable>();

            if (damageable == null || !damageable.IsAlive)
                return;

            float damage = CurrentWeapon != null ? CurrentWeapon.baseDamage : 10f;

            if (step.damageMultiplier > 0f)
                damage *= step.damageMultiplier;

            Vector3 knockbackDir = (targetCollider.transform.position - playerTransform.position).normalized;
            Vector2 knockback = new Vector2(
                knockbackDir.x * defaultKnockback.x,
                defaultKnockback.y
            );

            var damageInfo = new DamageInfo(damage, playerTransform.gameObject, DamageType.Physical, knockback);

            if (CurrentWeapon != null)
            {
                var enemy = targetCollider.GetComponent<EnemyCharacter>()
                         ?? targetCollider.GetComponentInParent<EnemyCharacter>();

                CurrentWeapon.TriggerModsOnHit(enemy, playerCharacter);
            }

            damageable.TakeDamage(damageInfo);
        }

        #endregion Damage

        #region Impact Resolution

        private Vector3 ResolveImpactPoint(AttackDirection dir, Vector3 origin, float radius)
        {
            Vector3 rayDir;

            switch (dir)
            {
                case AttackDirection.Up:
                    rayDir = Vector3.up;
                    break;
                case AttackDirection.Down:
                    rayDir = Vector3.down;
                    break;
                default:
                    rayDir = Vector3.right * Facing;
                    break;
            }

            Vector3 rayStart = origin - rayDir * (radius + 0.25f);
            float rayLength = (radius + 0.25f) + (radius + 0.5f);

            Vector3 point = origin;
            Vector3 normal = -rayDir;

            if (Physics.Raycast(
                rayStart,
                rayDir,
                out RaycastHit hit,
                rayLength,
                enemyLayer | environmentLayer,
                QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                normal = hit.normal;
            }
            else
            {
                point = origin + rayDir * radius;
            }

            // Push out of surface
            point += normal * 0.06f;

            // Push toward camera for 2.5D visibility
            Camera cam = Camera.main;
            if (cam != null)
                point += (-cam.transform.forward) * 0.1f;

            return point;
        }

        public Transform GetAttackTransform(AttackDirection dir)
        {
            switch (dir)
            {
                case AttackDirection.Up: return upAttack;
                case AttackDirection.Down: return downAttack;
                default: return sideAttack;
            }
        }

        private float GetFallbackRadius(AttackDirection dir)
        {
            switch (dir)
            {
                case AttackDirection.Up: return upRadius;
                case AttackDirection.Down: return downRadius;
                default: return sideRadius;
            }
        }

        private void ApplyRecoil(AttackDirection dir, AttackHitResult result)
        {
            if (playerRb == null || result == AttackHitResult.None)
                return;

            if (dir == AttackDirection.Side)
            {
                float recoilDir = -Facing;
                playerRb.AddForce(Vector3.right * recoilDir * sideRecoil, ForceMode.Impulse);
            }
        }

        #endregion Impact Resolution

        #region Feedback

        private void PlayHitFeedback(AttackHitResult result)
        {
            if (FeedbackManager.Instance == null)
                return;

            switch (result)
            {
                case AttackHitResult.Enemy:
                    FeedbackManager.Instance.DoHitFeedback(impulseSource, enemyHitHitstopDuration, enemyHitShakeForce);
                    break;

                case AttackHitResult.Environment:
                    FeedbackManager.Instance.DoHitFeedback(impulseSource, environmentHitHitstopDuration, environmentHitShakeForce);
                    break;
            }
        }

        #endregion Feedback

        #region Object Pools

        private void InitializeHitParticlePool()
        {
            if (hitParticlePrefab == null)
                return;

            if (hitParticlePoolRoot == null)
            {
                var poolObj = new GameObject("HitParticlePool");
                poolObj.transform.SetParent(transform);
                hitParticlePoolRoot = poolObj.transform;
            }

            for (int i = 0; i < hitParticlePoolSize; i++)
            {
                GameObject go = Instantiate(hitParticlePrefab, hitParticlePoolRoot);
                go.SetActive(false);
                hitParticlePool.Enqueue(go);
            }
        }

        private GameObject GetPooledHitParticle()
        {
            if (hitParticlePool.Count > 0)
                return hitParticlePool.Dequeue();

            return Instantiate(hitParticlePrefab, hitParticlePoolRoot);
        }

        private void ReturnHitParticle(GameObject go)
        {
            go.SetActive(false);
            go.transform.SetParent(hitParticlePoolRoot, false);
            hitParticlePool.Enqueue(go);
        }

        private void InitializeEnemyHitVFXPool()
        {
            if (enemyHitVFXPrefab == null)
                return;

            if (enemyHitVFXPoolRoot == null)
            {
                var poolObj = new GameObject("EnemyHitVFXPool");
                poolObj.transform.SetParent(transform);
                enemyHitVFXPoolRoot = poolObj.transform;
            }

            for (int i = 0; i < enemyHitVFXPoolSize; i++)
            {
                GameObject go = Instantiate(enemyHitVFXPrefab, enemyHitVFXPoolRoot);
                go.SetActive(false);
                enemyHitVFXPool.Enqueue(go);
            }
        }

        private GameObject GetPooledEnemyHitVFX()
        {
            if (enemyHitVFXPool.Count > 0)
                return enemyHitVFXPool.Dequeue();

            return Instantiate(enemyHitVFXPrefab, enemyHitVFXPoolRoot);
        }

        private void ReturnEnemyHitVFX(GameObject go)
        {
            go.SetActive(false);
            go.transform.SetParent(enemyHitVFXPoolRoot, false);
            enemyHitVFXPool.Enqueue(go);
        }

        private void InitializeHitCrossPool()
        {
            if (hitCrossPrefab == null)
                return;

            if (hitCrossPoolRoot == null)
            {
                var poolObj = new GameObject("HitCrossPool");
                poolObj.transform.SetParent(transform);
                hitCrossPoolRoot = poolObj.transform;
            }

            for (int i = 0; i < hitCrossPoolSize; i++)
            {
                GameObject go = Instantiate(hitCrossPrefab, hitCrossPoolRoot);
                go.SetActive(false);
                hitCrossPool.Enqueue(go);
            }
        }

        private GameObject GetPooledHitCross()
        {
            if (hitCrossPool.Count > 0)
                return hitCrossPool.Dequeue();

            return Instantiate(hitCrossPrefab, hitCrossPoolRoot);
        }

        private void ReturnHitCross(GameObject cross)
        {
            cross.SetActive(false);
            cross.transform.SetParent(hitCrossPoolRoot, false);
            hitCrossPool.Enqueue(cross);
        }

        private void InitializeSlashPools(WeaponComboData comboData)
        {
            slashPools.Clear();

            void Register(GameObject prefab)
            {
                if (prefab == null || slashPools.ContainsKey(prefab))
                    return;

                Queue<GameObject> pool = new();

                for (int i = 0; i < poolSizePerSlash; i++)
                {
                    GameObject slash = Instantiate(prefab, slashPoolRoot);
                    slash.SetActive(false);
                    pool.Enqueue(slash);
                }

                slashPools.Add(prefab, pool);
            }

            foreach (var step in comboData.sideComboSteps)
                Register(step.slashPrefab);

            Register(comboData.upAttack.slashPrefab);
            Register(comboData.downAttack.slashPrefab);
        }

        public GameObject GetSlash(GameObject prefab, Transform attackAnchor)
        {
            if (prefab == null || !slashPools.TryGetValue(prefab, out var pool))
                return null;

            GameObject slash = pool.Count > 0
                ? pool.Dequeue()
                : Instantiate(prefab, slashPoolRoot);

            slash.transform.SetParent(attackAnchor, false);

            Vector3 offsetToBody = new Vector3(-Facing * slashOffsetDirection, 0f, 0f);
            slash.transform.localPosition = offsetToBody * slashOffsetDistance;

            slash.transform.localRotation = Quaternion.identity;
            slash.transform.localScale = Vector3.one * slashScale;
            slash.SetActive(true);
            return slash;
        }

        public void ReturnSlash(GameObject prefab, GameObject slash)
        {
            slash.SetActive(false);
            slash.transform.SetParent(slashPoolRoot, false);

            if (slashPools.TryGetValue(prefab, out var pool))
                pool.Enqueue(slash);
        }

        #endregion Object Pools

        #region Effects

        public void PlaySlash(GameObject prefab, Transform attackAnchor, float lifetime = 0.2f)
        {
            GameObject slash = GetSlash(prefab, attackAnchor);
            if (slash == null) return;

            StartCoroutine(ReturnAfterDelay(slash, prefab, lifetime, ReturnSlash));
        }

        public void PlaySlashAt(GameObject prefab, Transform attackAnchor, Vector3 worldContactPoint, float lifetime = 0.12f)
        {
            GameObject slash = GetSlash(prefab, attackAnchor);
            if (slash == null) return;

            Vector3 localPoint = attackAnchor.InverseTransformPoint(worldContactPoint);
            slash.transform.localPosition = localPoint;

            StartCoroutine(ReturnAfterDelay(slash, prefab, lifetime, ReturnSlash));
        }

        private void PlayHitParticle(Vector3 impactPoint, AttackDirection dir)
        {
            if (hitParticlePrefab == null)
                return;

            GameObject go = GetPooledHitParticle();

            Vector3 attackDir = dir switch
            {
                AttackDirection.Up => Vector3.up,
                AttackDirection.Down => Vector3.down,
                _ => Vector3.right * Facing
            };

            const float directionalOffset = 0.12f;
            Vector3 spawnPos = impactPoint + attackDir * directionalOffset;

            go.transform.SetParent(null);
            go.transform.position = spawnPos;
            go.transform.rotation = Quaternion.identity;
            go.SetActive(true);

            ParticleSystem ps = go.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                ps.Clear(true);
                ps.Play(true);
            }

            StartCoroutine(ReturnHitParticleAfterDelay(go, hitParticleLifetime));
        }

        private void PlayEnemyHitVFX(Vector3 impactPoint, AttackDirection dir)
        {
            if (enemyHitVFXPrefab == null)
                return;

            GameObject go = GetPooledEnemyHitVFX();

            Vector3 attackDir = dir switch
            {
                AttackDirection.Up => Vector3.up,
                AttackDirection.Down => Vector3.down,
                _ => Vector3.right * Facing
            };

            const float directionalOffset = 0.12f;
            Vector3 spawnPos = impactPoint + attackDir * directionalOffset;

            go.transform.SetParent(null);
            go.transform.position = spawnPos;
            go.transform.rotation = Quaternion.identity;
            go.SetActive(true);

            ParticleSystem ps = go.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                ps.Clear(true);
                ps.Play(true);
            }

            StartCoroutine(ReturnEnemyHitVFXAfterDelay(go, enemyHitVFXLifetime));
        }

        private void PlayHitCross(Vector3 position)
        {
            if (hitCrossPrefab == null)
                return;

            GameObject cross = GetPooledHitCross();
            cross.transform.SetParent(null);
            cross.transform.position = position;
            cross.transform.localScale = Vector3.one * hitCrossSize;
            cross.SetActive(true);

            StartCoroutine(ReturnHitCrossAfterDelay(cross, hitCrossLifetime));
        }

        private IEnumerator ReturnHitParticleAfterDelay(GameObject go, float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnHitParticle(go);
        }

        private IEnumerator ReturnEnemyHitVFXAfterDelay(GameObject go, float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnEnemyHitVFX(go);
        }

        private IEnumerator ReturnHitCrossAfterDelay(GameObject cross, float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnHitCross(cross);
        }

        private IEnumerator ReturnAfterDelay(GameObject obj, GameObject prefab, float delay, System.Action<GameObject, GameObject> returnAction)
        {
            yield return new WaitForSeconds(delay);
            returnAction(prefab, obj);
        }

        #endregion Effects

        #region Weapon and Mod Pickup

        private void PickupWeapon(WorldWeaponPickup pickup)
        {
            storedPickup = pickup;
            pickup.gameObject.SetActive(false);

            CurrentWeapon = pickup.weaponInstance;
            CurrentWeapon.gameObject.SetActive(true);
            CurrentWeapon.transform.parent = weaponHolder;
            CurrentWeapon.transform.localPosition = Vector3.zero;
            CurrentWeapon.transform.localRotation = Quaternion.Euler(0, 0, -30f);
            CurrentWeapon.transform.localScale = Vector3.one;
            CurrentWeapon.GetComponent<SpriteRenderer>().sortingOrder = 11;
            CurrentWeapon.SetOwnerRigidbody(playerRb);

            CurrentWeapon.OnAttack += HandleWeaponAttack;

            var inventory = GetComponent<InventoryComponent>();
            if (inventory != null)
                inventory.EquipAllPossible();

            InitializeSlashPools(CurrentWeapon.weaponData.comboData);
            InitializeHitParticlePool();
            InitializeEnemyHitVFXPool();
            InitializeHitCrossPool();
            OnWeaponChanged?.Invoke();
        }

        public void DropWeapon()
        {
            if (CurrentWeapon == null || storedPickup == null)
                return;

            CurrentWeapon.OnAttack -= HandleWeaponAttack;

            CurrentWeapon.transform.SetParent(storedPickup.transform, false);
            CurrentWeapon.gameObject.SetActive(false);
            CurrentWeapon = null;

            storedPickup.transform.position =
                transform.position + Vector3.right * Facing * 1.2f;

            storedPickup.gameObject.SetActive(true);
            storedPickup = null;

            OnWeaponChanged?.Invoke();
        }

        private void PickupMod(WorldModPickup pickup)
        {
            if (pickup.modData == null)
                return;

            if (CurrentWeapon != null && CurrentWeapon.HasFreeSlot)
            {
                if (CurrentWeapon.TryAddMod(pickup.modData))
                {
                    Destroy(pickup.gameObject);
                    return;
                }
            }

            var inventory = GetComponent<InventoryComponent>();
            if (inventory != null)
            {
                inventory.PickupMod(pickup.modData);
                Destroy(pickup.gameObject);
            }
        }

        #endregion Weapon and Mod Pickup

        #region Debug

        private void OnDrawGizmosSelected()
        {
            if (sideAttack == null || upAttack == null || downAttack == null)
                return;

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(sideAttack.position, sideRadius);

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(upAttack.position, upRadius);

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(downAttack.position, downRadius);
        }

        #endregion Debug
    }

    public enum AttackDirection
    {
        Side,
        Up,
        Down
    }
}