using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Robot enemy - dashes at player when spotted.
    /// Has a chance to grab and throw the player on hit.
    /// 
    /// BEHAVIOR (decisions defined here):
    /// - Player spotted → Enter combat, start charging
    /// - Charge complete → Dash to player position
    /// - Dash hit (grab) → Hold player in GrabState → Throw → Recover
    /// - Dash hit (no grab) → Recover
    /// - Dash complete (miss) → Recover
    /// - Recovery complete → If player still visible, charge again; else exit combat and patrol
    /// </summary>
    public class RobotEnemy : EnemyCharacter
    {
        [Header("Robot - VFX")]
        [SerializeField] private GameObject hitParticlePrefab;
        [SerializeField] private GameObject hurtParticlePrefab;
        [SerializeField] private int hurtParticlePoolSize = 4;
        [SerializeField] private float hurtParticleLifetime = 0.5f;
        [SerializeField] private GameObject deathParticlePrefab;
        [SerializeField] private float deathParticleLifetime = 2f;
        [SerializeField] private GameObject robotVisual;

        [Header("Robot - Hitstop")]
        [SerializeField] private float hitstopDuration = 0.05f;

        [Header("Robot - Dash Attack")]
        [SerializeField] private float dashChargeTime = 1f;
        [SerializeField] private float dashSpeed = 15f;
        [SerializeField] private float dashRecoveryTime = 0.3f;
        [SerializeField] private float dashDamage = 10f;
        [SerializeField] private Vector2 dashKnockback = new Vector2(15f, 5f);

        [Header("Robot - Grab Attack")]
        [SerializeField] private bool canGrab = true;
        [SerializeField][Range(0f, 1f)] private float grabChance = 0.3f;
        [SerializeField] private float grabDuration = 0.5f;
        [SerializeField] private Vector2 throwForce = new Vector2(25f, 10f);
        [SerializeField] private float throwDamage = 5f;
        [SerializeField] private Vector3 grabOffset = new Vector3(0f, 1.5f, 0f);

        // Hurt particle pool
        private readonly Queue<GameObject> hurtParticlePool = new();
        private Transform hurtParticlePoolRoot;

        // Override base class properties
        public override float DashChargeTime => dashChargeTime;
        public override float DashSpeed => dashSpeed;
        public override float DashRecoveryTime => dashRecoveryTime;
        public override float DashDamage => dashDamage;
        public override Vector2 DashKnockback => dashKnockback;

        // Expose grab duration for GrabState
        public float GrabDuration => grabDuration;

        protected override void Awake()
        {
            base.Awake();
            InitializeHurtParticlePool();
        }

        protected override void InitializeStateMachine()
        {
            stateMachine.RegisterStates(
                new PatrolState(this),
                new IdleState(this),
                new ChargeState(this),
                new DashState(this),
                new GrabState(this),
                new RecoverState(this),
                new DeadState(this)
            );

            if (HasPatrol)
                stateMachine.SetInitialState<PatrolState>();
            else
                stateMachine.SetInitialState<IdleState>();
        }

        #region Hurt Particle Pool

        private void InitializeHurtParticlePool()
        {
            if (hurtParticlePrefab == null)
                return;

            var poolObj = new GameObject("HurtParticlePool");
            poolObj.transform.SetParent(transform);
            hurtParticlePoolRoot = poolObj.transform;

            for (int i = 0; i < hurtParticlePoolSize; i++)
            {
                GameObject go = Instantiate(hurtParticlePrefab, hurtParticlePoolRoot);
                go.SetActive(false);
                hurtParticlePool.Enqueue(go);
            }
        }

        private GameObject GetHurtParticle()
        {
            if (hurtParticlePool.Count > 0)
                return hurtParticlePool.Dequeue();

            return Instantiate(hurtParticlePrefab, hurtParticlePoolRoot);
        }

        private void ReturnHurtParticle(GameObject go)
        {
            go.SetActive(false);
            go.transform.SetParent(hurtParticlePoolRoot, false);
            hurtParticlePool.Enqueue(go);
        }

        private void SpawnHurtParticle()
        {
            if (hurtParticlePrefab == null)
                return;

            GameObject go = GetHurtParticle();
            go.transform.SetParent(null);
            go.transform.position = transform.position;
            go.transform.rotation = Quaternion.identity;
            go.SetActive(true);

            var ps = go.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                ps.Clear(true);
                ps.Play(true);
            }

            StartCoroutine(ReturnHurtParticleAfterDelay(go, hurtParticleLifetime));
        }

        private System.Collections.IEnumerator ReturnHurtParticleAfterDelay(GameObject go, float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnHurtParticle(go);
        }

        #endregion

        #region Death Particles

        private void SpawnDeathParticles()
        {
            if (deathParticlePrefab == null)
                return;

            GameObject go = Instantiate(deathParticlePrefab, transform.position, Quaternion.identity);

            if (deathParticleLifetime > 0f)
                Destroy(go, deathParticleLifetime);
        }

        private void DisableRobotVisual()
        {
            if (robotVisual != null)
                robotVisual.SetActive(false);
        }

        #endregion

        // === DAMAGE and Death HANDLING ===

        public override void TakeDamage(DamageInfo info)
        {
            if (state != null && !state.CanTakeDamage)
                return;

            base.TakeDamage(info);

            // Spawn hurt particle on damage
            SpawnHurtParticle();
        }

        protected override void HandleDeath()
        {
            // Spawn death particles and hide visual
            SpawnDeathParticles();
            DisableRobotVisual();

            base.HandleDeath();
        }

        // === ROBOT BRAIN - All decisions live here ===

        public override void OnPlayerSpotted()
        {
            if (!IsAlive) return;
            if (isInCombat) return;

            EnterCombat();
            stateMachine.ChangeState<ChargeState>();
        }

        public override void OnPlayerLost()
        {
            if (!IsAlive) return;

            if (!isInCombat)
            {
                if (HasPatrol)
                    stateMachine.ChangeState<PatrolState>();
                else
                    stateMachine.ChangeState<IdleState>();
            }
        }

        public override void OnChargeComplete()
        {
            if (!IsAlive) return;
            stateMachine.ChangeState<DashState>();
        }

        public override void OnDashComplete()
        {
            if (!IsAlive) return;
            stateMachine.ChangeState<RecoverState>();
        }

        public override void OnGrabComplete()
        {
            if (!IsAlive) return;
            stateMachine.ChangeState<RecoverState>();
        }

        public override void OnRecoveryComplete()
        {
            if (!IsAlive) return;

            if (HasTarget)
            {
                stateMachine.ChangeState<ChargeState>();
            }
            else
            {
                ExitCombat();
                if (HasPatrol)
                    stateMachine.ChangeState<PatrolState>();
                else
                    stateMachine.ChangeState<IdleState>();
            }
        }

        protected override void OnTargetAcquired()
        {
            Debug.Log($"{gameObject.name}: Target acquired!");
        }

        protected override void OnTargetLost()
        {
            Debug.Log($"{gameObject.name}: Target lost.");
        }

        // === ROBOT-SPECIFIC HIT BEHAVIOR ===

        protected override void OnDashHitboxHit(Collider other, Hitbox hitbox)
        {
            hitbox?.Deactivate();

            var damageable = other.GetComponent<IDamageable>();
            if (damageable == null)
                damageable = other.GetComponentInParent<IDamageable>();

            if (damageable == null || !damageable.IsAlive)
                return;

            // Hitstop when hitting enemy
            if (FeedbackManager.Instance != null)
                FeedbackManager.Instance.DoHitstop(hitstopDuration);

            int throwDir = Movement != null ? Movement.FacingDirection : 1;

            bool doGrab = canGrab && Random.value <= grabChance;

            if (doGrab)
            {
                var grabbable = other.GetComponent<IGrabbable>();
                if (grabbable == null)
                    grabbable = other.GetComponentInParent<IGrabbable>();

                if (grabbable != null && grabbable.CanBeGrabbed)
                {
                    var damageInfo = new DamageInfo(dashDamage, gameObject, DamageType.Physical);
                    damageable.TakeDamage(damageInfo);

                    var grabInfo = new GrabInfo(
                        gameObject,
                        grabDuration,
                        grabOffset,
                        throwForce,
                        throwDamage,
                        throwDir
                    );
                    grabbable.GetGrabbed(grabInfo);

                    stateMachine.ChangeState<GrabState>();

                    Debug.Log($"{gameObject.name} GRABBED {other.name}!");
                    return;
                }
            }

            var info = new DamageInfo(dashDamage, gameObject, DamageType.Physical, dashKnockback);
            damageable.TakeDamage(info);
            Debug.Log($"{gameObject.name} hit {other.name} for {dashDamage} damage");
        }
    }
}