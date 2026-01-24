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

        [Header("Attack State")]
        [SerializeField] private float attackDuration = 0.3f;

        private Coroutine _attackStateCo;

        [Header("Fallback Hit Radii")]
        [SerializeField] private float sideRadius = 1f;
        [SerializeField] private float upRadius = 1f;
        [SerializeField] private float downRadius = 1f;

        [Header("Hit Detection")]
        [SerializeField] private LayerMask enemyLayer;
        [SerializeField] private LayerMask environmentLayer;

        [Header("Knockback")]
        [SerializeField] private Vector2 defaultKnockback = new Vector2(8f, 4f);

        [Header("Feedback Settings")]
        [SerializeField] private Unity.Cinemachine.CinemachineImpulseSource impulseSource;
        [SerializeField] private float enemyHitHitstopDuration = 0.06f;
        [SerializeField] private float enemyHitShakeForce = 0.8f;
        [SerializeField] private float enemyHitDelay = 0.3f;

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
        public void SetWeaponVisible(bool visible)
        {
            if (CurrentWeapon == null)
                return;

            var renderers = CurrentWeapon.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (var sr in renderers)
            {
                if (sr != null)
                    sr.enabled = visible;
            }
        }


        #region Attack

        public void Attack(AttackDirection dir)
        {
            if (CurrentWeapon == null)
                return;
            if (playerState != null)
                playerState.SetAttacking(true);

            // Reset after duration
            if (_attackStateCo != null)
                StopCoroutine(_attackStateCo);
            _attackStateCo = StartCoroutine(ResetAttackingAfterDuration());

            CurrentWeapon.ExecuteAttack(dir);
        }

        private IEnumerator ResetAttackingAfterDuration()
        {
            yield return new WaitForSeconds(attackDuration);

            if (playerState != null)
                playerState.SetAttacking(false);

            _attackStateCo = null;
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
            Collider closestEnemy = null;
            float closestEnemyDist = float.MaxValue;
            bool hitEnvironment = false;

            // Find closest enemy and check for environment hits
            for (int i = 0; i < hits.Length; i++)
            {
                int mask = 1 << hits[i].gameObject.layer;

                if ((mask & enemyLayer) != 0)
                {
                    float dist = Vector3.Distance(playerTransform.position, hits[i].transform.position);
                    if (dist < closestEnemyDist)
                    {
                        closestEnemyDist = dist;
                        closestEnemy = hits[i];
                    }
                }
                else if ((mask & environmentLayer) != 0)
                {
                    hitEnvironment = true;
                }
            }

            // Hit the closest enemy if found
            if (closestEnemy != null)
            {
                result = AttackHitResult.Enemy;
                StartCoroutine(DelayDealDamage(closestEnemy, step));
            }
            else if (hitEnvironment)
            {
                result = AttackHitResult.Environment;
            }

            // --- IMPACT POINT & VFX ---
            if (result != AttackHitResult.None)
            {
                Vector3 impactPoint = ResolveImpactPoint(dir, anchor.position, finalRadius);
                Vector3 attackDir = GetAttackDirection(dir);

                // Spawn VFX via CombatEffectsManager
                if (CombatEffectsManager.Instance != null)
                {
                    if (result == AttackHitResult.Environment)
                    {
                        CombatEffectsManager.Instance.SpawnEnvHitParticle(impactPoint, attackDir);
                        CombatEffectsManager.Instance.SpawnHitCross(impactPoint);
                    }
                    // Enemy hit VFX is now handled in DealDamageToTarget (only if damage was dealt)
                }

                // Slash at impact point
                if (step.slashPrefab != null)
                    PlaySlashAt(step.slashPrefab, anchor, impactPoint);
            }
            else
            {
                // Slash at default position (no hit)
                if (step.slashPrefab != null)
                    PlaySlash(step.slashPrefab, anchor);
            }

            ApplyRecoil(dir, result);
        }

        private Vector3 GetAttackDirection(AttackDirection dir)
        {
            return dir switch
            {
                AttackDirection.Up => Vector3.up,
                AttackDirection.Down => Vector3.down,
                _ => Vector3.right * Facing
            };
        }

        #endregion Attack

        #region Damage

        private IEnumerator DelayDealDamage(Collider targetCollider, WeaponComboData.ComboStep step)
        {
            if (enemyHitDelay > 0f)
                yield return new WaitForSeconds(enemyHitDelay);

            // Target might have been destroyed / disabled during the delay.
            if (targetCollider == null)
                yield break;

            PlayHitFeedback();
            DealDamageToTarget(targetCollider, step);
        }

        private void DealDamageToTarget(Collider targetCollider, WeaponComboData.ComboStep step)
        {
            var damageable = targetCollider.GetComponent<IDamageable>()
                          ?? targetCollider.GetComponentInParent<IDamageable>();
            if (damageable == null || !damageable.IsAlive)
                return;

            float damage = CurrentWeapon != null ? CurrentWeapon.baseDamage : 10f;
            if (step.damageMultiplier > 0f)
                damage *= step.damageMultiplier;

            // Pass raw knockback - let the target calculate direction from Source
            var damageInfo = new DamageInfo(damage, playerTransform.gameObject, DamageType.Physical, defaultKnockback);

            bool damageDealt = damageable.TakeDamage(damageInfo);

            if (damageDealt)
            {
                if (CurrentWeapon != null)
                {
                    var enemy = targetCollider.GetComponent<EnemyCharacter>()
                             ?? targetCollider.GetComponentInParent<EnemyCharacter>();
                    CurrentWeapon.TriggerModsOnHit(enemy, playerCharacter);
                }

                if (CombatEffectsManager.Instance != null)
                {
                    Vector3 hitPoint = targetCollider.ClosestPoint(playerTransform.position);
                    Vector3 attackDir = (targetCollider.transform.position - playerTransform.position).normalized;
                    CombatEffectsManager.Instance.SpawnEnemyHitVFX(hitPoint, attackDir);
                    CombatEffectsManager.Instance.SpawnEnemyHurtParticle(hitPoint, attackDir);
                }
            }
        }

        #endregion Damage

        #region Impact Resolution

        private Vector3 ResolveImpactPoint(AttackDirection dir, Vector3 origin, float radius)
        {
            Vector3 rayDir = GetAttackDirection(dir);

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

            point += normal * 0.06f;

            Camera cam = Camera.main;
            if (cam != null)
                point += (-cam.transform.forward) * 0.1f;

            return point;
        }

        public Transform GetAttackTransform(AttackDirection dir)
        {
            return dir switch
            {
                AttackDirection.Up => upAttack,
                AttackDirection.Down => downAttack,
                _ => sideAttack
            };
        }

        private float GetFallbackRadius(AttackDirection dir)
        {
            return dir switch
            {
                AttackDirection.Up => upRadius,
                AttackDirection.Down => downRadius,
                _ => sideRadius
            };
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

        private void PlayHitFeedback()
        {
            if (FeedbackManager.Instance != null)
                FeedbackManager.Instance.DoHitFeedback(impulseSource, enemyHitHitstopDuration, enemyHitShakeForce);
        }

        #endregion Feedback

        #region Slash Pool

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

        public void PlaySlash(GameObject prefab, Transform attackAnchor, float lifetime = 0.2f)
        {
            GameObject slash = GetSlash(prefab, attackAnchor);
            if (slash == null) return;

            StartCoroutine(ReturnSlashAfterDelay(slash, prefab, lifetime));
        }

        public void PlaySlashAt(GameObject prefab, Transform attackAnchor, Vector3 worldContactPoint, float lifetime = 0.12f)
        {
            GameObject slash = GetSlash(prefab, attackAnchor);
            if (slash == null) return;

            Vector3 localPoint = attackAnchor.InverseTransformPoint(worldContactPoint);
            slash.transform.localPosition = localPoint;

            StartCoroutine(ReturnSlashAfterDelay(slash, prefab, lifetime));
        }

        private IEnumerator ReturnSlashAfterDelay(GameObject slash, GameObject prefab, float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnSlash(prefab, slash);
        }

        #endregion Slash Pool

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