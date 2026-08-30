using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace junklite
{
    [CreateAssetMenu(fileName = "SocialDistanceMod", menuName = "Junklite/Mods/Social Distance")]
    public class SocialDistanceMod : ActiveModData
    {
        #region Config

        [Header("Push")]
        public float pushRadius = 5f;
        public float pushForce = 15f;
        public float pushUpForce = 5f;

        [Tooltip("How long the pulse takes to expand to full radius")]
        public float pulseDuration = 0.35f;

        [Tooltip("Force multiplier at the edge of the radius (0.1 = 10% force at border)")]
        [Range(0.01f, 1f)]
        public float edgeFalloff = 0.15f;

        public LayerMask enemyLayerMask = 1;

        [Header("Damage")]
        public float pushDamage = 10f;

        [Header("VFX")]
        [Tooltip("Should be a flat radial effect — will be scaled up to match push radius over pulse duration")]
        public GameObject pushVFX;

        [Header("Feedback")]
        public float cameraShakeIntensity = 2f;

        #endregion

        #region Overrides

        public override void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy, float damageDealt)
        {
            // No charges - do nothing
        }

        protected override bool ExecuteAbility(
            ModInstance instance,
            PlayerCharacter player,
            ModExecutionRunner executionRunner)
        {
            return executionRunner.TryStart(
                instance,
                context => CoExecutePulse(context, player));
        }

        #endregion

        #region Pulse

        private IEnumerator CoExecutePulse(ModExecutionContext context, PlayerCharacter player)
        {
            Vector3 origin = player.transform.position;
            var hitEnemies = new HashSet<EnemyCharacter>();
            var pushBuffer = new Collider[32];

            // Camera shake at start of pulse
            if (cameraShakeIntensity > 0f && FeedbackManager.Instance != null)
            {
                var impulse = player.GetComponent<Unity.Cinemachine.CinemachineImpulseSource>();
                if (impulse != null)
                    FeedbackManager.Instance.DoCameraShake(impulse, cameraShakeIntensity);
            }

            // Spawn and scale VFX in sync with pulse expansion
            GameObject vfxInstance = null;
            if (pushVFX != null)
                vfxInstance = Instantiate(pushVFX, origin, Quaternion.identity);

            context.AddCleanup(() =>
            {
                if (vfxInstance != null)
                    Destroy(vfxInstance);
            });

            float elapsed = 0f;

            while (elapsed < pulseDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / pulseDuration);
                float currentRadius = Mathf.Lerp(0f, pushRadius, t);

                // Scale VFX to match current pulse radius
                if (vfxInstance != null)
                {
                    float diameter = currentRadius * 2f;
                    vfxInstance.transform.localScale = Vector3.one * diameter;
                }

                // Check for newly reached enemies
                int count = Physics.OverlapSphereNonAlloc(origin, currentRadius, pushBuffer, enemyLayerMask);

                for (int i = 0; i < count; i++)
                {
                    var col = pushBuffer[i];
                    if (col.gameObject == player.gameObject) continue;

                    var enemy = col.GetComponentInParent<EnemyCharacter>();
                    if (enemy == null || !enemy.IsAlive || hitEnemies.Contains(enemy)) continue;

                    hitEnemies.Add(enemy);

                    float dist = Vector3.Distance(origin, enemy.transform.position);
                    float falloff = Mathf.Lerp(1f, edgeFalloff, Mathf.Clamp01(dist / pushRadius));

                    // KnockbackForce.x is a SCALAR MAGNITUDE, not a direction.
                    //
                    // EnemyCharacter.ApplyKnockback independently derives the outward world-space
                    // direction from info.Source (player.gameObject):
                    //   knockbackDir = (enemy.position - source.position).normalized  (Y=0)
                    //   finalForce   = knockbackDir * KnockbackForce.x + Vector3.up * KnockbackForce.y
                    //
                    // Passing a signed X (as in dir.x * pushForce) would cause ApplyKnockback to
                    // multiply two opposing signs and push enemies TOWARD the player on one side.
                    // Keep X positive so enemies are always pushed away, regardless of which
                    // plane (XY or ZY) the game is currently on — the direction math is entirely
                    // world-space inside ApplyKnockback and naturally handles both planes.
                    Vector2 knockback = new Vector2(pushForce * falloff, pushUpForce * falloff);

                    DamageResult result = DamageReceiverUtility.Receive(
                        enemy,
                        new DamageRequest(
                            pushDamage,
                            player.gameObject,
                            DamageType.Physical,
                            knockback));

                    if (result.WasApplied)
                        SpawnHitEffects(origin, enemy);
                }

                yield return null;
            }
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
