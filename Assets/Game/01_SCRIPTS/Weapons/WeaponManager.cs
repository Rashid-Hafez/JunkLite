using UnityEngine;
using System.Collections.Generic;
using System.Collections;

namespace junklite
{
    [RequireComponent(typeof(Collider))]
    public class WeaponManager : MonoBehaviour
    {

        [Header("Weapon Holder")] public Transform weaponHolder;

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

        [Header("Hit Particle VFX")]
        [SerializeField] private GameObject hitParticlePrefab;
        [SerializeField] private int hitParticlePoolSize = 8;
        [SerializeField] private float hitParticleLifetime = 0.2f;
        [SerializeField] private float hitParticleSize = 1f;
        [SerializeField] private Transform hitParticlePoolRoot;

        private readonly Queue<GameObject> hitParticlePool = new();


        [Header("Hit Cross VFX")]
        [SerializeField] private GameObject hitCrossPrefab;
        [SerializeField] private int hitCrossPoolSize = 8;
        [SerializeField] private float hitCrossLifetime = 0.12f;
        [SerializeField] private float hitCrossSize = 4f;
        [SerializeField] private Transform hitCrossPoolRoot;

        [Header("Feedback Manager")]
        [SerializeField] private FeedbackManager feedbackManager;
        [SerializeField] private Unity.Cinemachine.CinemachineImpulseSource impulseSource;

        private readonly Queue<GameObject> hitCrossPool = new();

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

            // ensure we have a FeedbackManager instance
            feedbackManager = FeedbackManager.instance ?? FindObjectOfType<FeedbackManager>();
            if (feedbackManager == null)
                Debug.LogWarning("FeedbackManager not found in scene");

            // ensure impulseSource is assigned (try to find on this GameObject or parents)
            if (impulseSource == null)
            {
                impulseSource = GetComponent<Unity.Cinemachine.CinemachineImpulseSource>()
                                ?? GetComponentInParent<Unity.Cinemachine.CinemachineImpulseSource>()
                                ?? GetComponentInChildren<Unity.Cinemachine.CinemachineImpulseSource>();
            }

            if (impulseSource == null)
                Debug.LogWarning("CinemachineImpulseSource not assigned/found on weapon/player");
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
        // ================== ATTACK ==================
        public void Attack(AttackDirection dir)
        {
            if (CurrentWeapon == null)
                return;

            // Trigger the weapon → event → HandleWeaponAttack
            CurrentWeapon.ExecuteAttack(dir);
        }

        private void HandleWeaponAttack(AttackDirection dir, WeaponComboData.ComboStep step, int comboIndex)
        {
            // Forward combo attack to PlayerState for animation binding
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

                    // === DEAL DAMAGE TO ENEMY ===
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
                PlayHitEffect(impactPoint, dir);
                PlayHitCross(impactPoint);
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

        #region mod attack logic
        /// <summary>
        /// Deals damage to the target hit by the weapon.
        /// </summary>
        private void DealDamageToTarget(Collider targetCollider, WeaponComboData.ComboStep step)
        {
            // Find IDamageable on target
            var damageable = targetCollider.GetComponent<IDamageable>();
            if (damageable == null)
                damageable = targetCollider.GetComponentInParent<IDamageable>();

            if (damageable == null || !damageable.IsAlive)
                return;

            // Calculate damage
            float damage = CurrentWeapon != null ? CurrentWeapon.baseDamage : 10f;

            // Apply step damage multiplier if available
            if (step.damageMultiplier > 0f)
                damage *= step.damageMultiplier;

            // Calculate knockback direction
            Vector3 knockbackDir = (targetCollider.transform.position - playerTransform.position).normalized;
            Vector2 knockback = new Vector2(
                knockbackDir.x * defaultKnockback.x,
                defaultKnockback.y
            );

            // Create damage info
            var damageInfo = new DamageInfo(damage, playerTransform.gameObject, DamageType.Physical, knockback);

            // --- TRIGGER MOD EFFECTS (Simplified!) ---
            if (CurrentWeapon != null)
            {
                var enemy = targetCollider.GetComponent<EnemyCharacter>()
                         ?? targetCollider.GetComponentInParent<EnemyCharacter>();

                // One simple call - weapon handles everything
                CurrentWeapon.TriggerModsOnHit(enemy, playerCharacter);
            }

            PlayFeedback();
            damageable.TakeDamage(damageInfo);
        }

        #endregion mod attack logic

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

            // Start from behind so it works even if overlapping
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
                // Fallback
                point = origin + rayDir * radius;
            }

            // ---- CRITICAL 2.5D VISIBILITY FIX ----
            // Push out of surface
            point += normal * 0.06f;

            // Push toward camera so it renders in front
            Camera cam = Camera.main;
            if (cam != null)
                point += (-cam.transform.forward) * 0.1f;

            return point;
        }


        public Transform GetAttackTransform(AttackDirection dir)
        {
            switch (dir)
            {
                case AttackDirection.Up:
                    return upAttack;
                case AttackDirection.Down:
                    return downAttack;
                default:
                    return sideAttack;
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

        #endregion Attack

        #region Object Pool

        private void InitializeHitParticlePool()
        {
            if (hitParticlePrefab == null || hitParticlePoolRoot == null)
                return;

            for (int i = 0; i < hitParticlePoolSize; i++)
            {
                GameObject go = Instantiate(hitParticlePrefab, hitParticlePoolRoot);
                go.SetActive(false);
                hitParticlePool.Enqueue(go);
            }
        }

        private void InitializeHitCrossPool()
        {
            if (hitCrossPrefab == null || hitCrossPoolRoot == null)
                return;

            for (int i = 0; i < hitCrossPoolSize; i++)
            {
                GameObject go = Instantiate(hitCrossPrefab, hitCrossPoolRoot);
                go.SetActive(false);
                hitCrossPool.Enqueue(go);
            }
        }

        private GameObject GetHitCross()
        {
            if (hitCrossPool.Count > 0)
                return hitCrossPool.Dequeue();

            return Instantiate(hitCrossPrefab, hitCrossPoolRoot);
        }

        private void PlayHitCross(Vector3 position)
        {
            if (hitCrossPrefab == null)
                return;

            GameObject cross = GetHitCross();
            cross.transform.SetParent(null);
            cross.transform.position = position;
            cross.transform.localScale = Vector3.one * hitCrossSize;
            cross.SetActive(true);

            StartCoroutine(ReturnHitCrossAfterTime(cross, hitCrossLifetime));
        }

        private void ReturnHitCross(GameObject cross)
        {
            cross.SetActive(false);
            cross.transform.SetParent(hitCrossPoolRoot, false);
            hitCrossPool.Enqueue(cross);
        }

        private IEnumerator ReturnHitCrossAfterTime(GameObject cross, float time)
        {
            yield return new WaitForSeconds(time);
            ReturnHitCross(cross);
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

        private GameObject GetHitParticle()
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


        /// <summary>
        /// Retrieves a slash GameObject from the pool or instantiates a new one if the pool is empty.
        /// Sets its parent to the given attack anchor and resets its transform.
        /// 
        /// See also: Spine Runtime Example on object pooling and pooling pattern
        /// https://github.com/EsotericSoftware/spine-runtimes/blob/4.1/spine-unity/Assets/Spine Examples/Scripts/Sample Components/SkeletonUtility%20Modules/Editor/spine-unity-examples-editor.asmdef
        /// For best practices on pooling and instantiation in Unity, refer to:
        /// https://docs.unity3d.com/Manual/Pooling.html
        /// </summary>
        /// <param name="prefab">The slash GameObject prefab.</param>
        /// <param name="attackAnchor">The transform at which to parent the slash instance.</param>
        /// <returns>A pooled or newly instantiated slash GameObject, or null if prefab is not in pool.</returns>
        /// <remarks>
        /// Optionally offset the slash, depending on design.
        /// Vector3 offsetToBody = -attackAnchor.right * Facing * 0.5f;
        /// </remarks>
    
        public GameObject GetSlash(GameObject prefab, Transform attackAnchor)
        {
            if (prefab == null || !slashPools.TryGetValue(prefab, out var pool))
                return null;

            GameObject slash = pool.Count > 0
                ? pool.Dequeue()
                : Instantiate(prefab, slashPoolRoot);

            // Optionally offset the slash, depending on design.
            slash.transform.SetParent(attackAnchor, false);
    
        // Use world direction - offset toward body (opposite of facing)    //Y axis //z axis (dont touch 0)
            Vector3 offsetToBody = new Vector3(-Facing * slashOffsetDirection, 0f, 0f); // world direction
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

        private IEnumerator ReturnHitParticleAfterTime(GameObject go, float time)
        {
            yield return new WaitForSeconds(time);
            ReturnHitParticle(go);
        }

        private IEnumerator ReturnSlashAfterTime(GameObject prefab, GameObject slash, float time)
        {
            yield return new WaitForSeconds(time);
            ReturnSlash(prefab, slash);
        }


        #endregion Object Pool

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

            // Auto-equip any stored mods from inventory
            var inventory = GetComponent<InventoryComponent>();
            if (inventory != null)
            {
                inventory.EquipAllPossible();
            }

            InitializeSlashPools(CurrentWeapon.weaponData.comboData);
            InitializeHitParticlePool();
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

            // Try to add directly to weapon if it has a free slot
            if (CurrentWeapon != null && CurrentWeapon.HasFreeSlot)
            {
                if (CurrentWeapon.TryAddMod(pickup.modData))
                {
                    Destroy(pickup.gameObject);
                    return;
                }
            }

            // No free slot on weapon - store in inventory
            var inventory = GetComponent<InventoryComponent>();
            if (inventory != null)
            {
                inventory.PickupMod(pickup.modData);
                Destroy(pickup.gameObject);
            }
        }

        #endregion Weapon and Mod Pickup

        #region Effects
        public void PlaySlash(GameObject prefab, Transform attackAnchor, float lifetime = 0.2f)
        {
            GameObject slash = GetSlash(prefab, attackAnchor);
            if (slash == null) return;

            StartCoroutine(ReturnSlashAfterTime(prefab, slash, lifetime));
        }

        public void PlaySlashAt(GameObject prefab, Transform attackAnchor, Vector3 worldContactPoint, float lifetime = 0.12f)
        {
            GameObject slash = GetSlash(prefab, attackAnchor);
            if (slash == null) return;

            // Convert world contact → local space of anchor
            Vector3 localPoint = attackAnchor.InverseTransformPoint(worldContactPoint);
            slash.transform.localPosition = localPoint;

            StartCoroutine(ReturnSlashAfterTime(prefab, slash, lifetime));
        }

        public void PlayHitEffect(Vector3 impactPoint, AttackDirection dir)
        {
            if (hitParticlePrefab == null)
                return;

            GameObject go = GetHitParticle();

            // Determine attack direction in world
            Vector3 attackDir;

            switch (dir)
            {
                case AttackDirection.Up:
                    attackDir = Vector3.up;
                    break;
                case AttackDirection.Down:
                    attackDir = Vector3.down;
                    break;
                default:
                    attackDir = Vector3.right * Facing;
                    break;
            }

            // IMPORTANT:
            // Push particles slightly FORWARD along attack direction
            // This compensates for particle size expanding inward
            const float directionalOffset = 0.12f;

            Vector3 spawnPos =
                impactPoint +
                attackDir * directionalOffset;

            go.transform.SetParent(null);
            go.transform.position = spawnPos;
            go.transform.rotation = Quaternion.identity;
            go.SetActive(true);

            // Restart particle simulation cleanly
            ParticleSystem ps = go.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                ps.Clear(true);
                ps.Play(true);
            }

            StartCoroutine(ReturnHitParticleAfterTime(go, hitParticleLifetime));
        }

        private void PlayFeedback()
        {
            feedbackManager.HitStop(0.08f);
            feedbackManager.CinemachineShake(impulseSource);
        }
        #endregion Effects


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

    }

    public enum AttackDirection
    {
        Side,
        Up,
        Down
    }
}