using UnityEngine;

namespace junklite
{
    [CreateAssetMenu(fileName = "SocialDistanceMod", menuName = "Junklite/Mods/Social Distance")]
    public class SocialDistanceMod : ActiveModData
    {
        #region Config

        [Header("Push")]
        [Tooltip("Radius of the push effect around Cas")]
        public float pushRadius = 5f;

        [Tooltip("Horizontal knockback force (enemy's knockback system handles direction from Cas → enemy)")]
        public float pushForce = 15f;

        [Tooltip("Upward lift applied alongside the push")]
        public float pushUpForce = 5f;

        [Tooltip("Force multiplier at the edge of the radius (0.1 = 10% force at border)")]
        [Range(0.01f, 1f)]
        public float edgeFalloff = 0.15f;

        public LayerMask enemyLayerMask = 1;

        [Header("Damage")]
        [Tooltip("Damage dealt to pushed enemies (0 = no damage, just push)")]
        public float pushDamage = 10f;

        [Header("VFX")]
        public GameObject pushVFX;

        [Header("Feedback")]
        public float cameraShakeIntensity = 2f;

        #endregion

        private static readonly Collider[] pushBuffer = new Collider[32];

        #region Overrides

        public override void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy)
        {
            // No charges - do nothing
        }

        protected override bool ExecuteAbility(ModInstance instance, PlayerCharacter player)
        {
            Vector3 origin = player.transform.position;

            int count = Physics.OverlapSphereNonAlloc(origin, pushRadius, pushBuffer, enemyLayerMask);

            if (count == 0)
            {
                Debug.Log("[SocialDistance] No enemies in range.");
                return false;
            }

            bool hitAny = false;

            for (int i = 0; i < count; i++)
            {
                var col = pushBuffer[i];
                if (col.gameObject == player.gameObject) continue;

                var enemy = col.GetComponentInParent<EnemyCharacter>();
                if (enemy == null || !enemy.IsAlive) continue;

                var damageable = enemy.GetComponentInParent<IDamageable>();
                if (damageable == null) continue;

                hitAny = true;

                // Distance falloff: full force at center, edgeFalloff at border
                float dist = Vector3.Distance(origin, enemy.transform.position);
                float t = Mathf.Clamp01(dist / pushRadius);
                float strength = Mathf.Lerp(1f, edgeFalloff, t);

                Vector2 knockback = new Vector2(pushForce * strength, pushUpForce * strength);

                damageable.TakeDamage(new DamageInfo(
                    pushDamage,
                    player.gameObject,
                    DamageType.Physical,
                    knockback
                ));

                SpawnHitEffects(origin, enemy);
            }

            if (!hitAny) return false;

            // Push VFX at player position
            if (pushVFX != null)
                Instantiate(pushVFX, origin, Quaternion.identity);

            // Camera shake
            if (cameraShakeIntensity > 0f && FeedbackManager.Instance != null)
            {
                var impulse = player.GetComponent<Unity.Cinemachine.CinemachineImpulseSource>();
                if (impulse != null)
                    FeedbackManager.Instance.DoCameraShake(impulse, cameraShakeIntensity);
            }

            return true;
        }

        #endregion

        #region Helpers

        private void SpawnHitEffects(Vector3 attackOrigin, EnemyCharacter enemy)
        {
            if (CombatEffectsManager.Instance == null) return;

            var enemyCollider = enemy.GetComponent<Collider>();
            if (enemyCollider == null) return;

            Vector3 hitPoint = enemyCollider.bounds.center;
            Vector3 hitDir = (hitPoint - attackOrigin).normalized;

            CombatEffectsManager.Instance.SpawnEnemyHitVFX(hitPoint, hitDir);
            CombatEffectsManager.Instance.SpawnEnemyHurtParticle(hitPoint, hitDir);
        }

        #endregion
    }
}