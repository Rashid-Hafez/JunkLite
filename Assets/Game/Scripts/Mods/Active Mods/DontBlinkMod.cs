using UnityEngine;
using System.Collections;

namespace junklite
{
    [CreateAssetMenu(fileName = "DontBlinkMod", menuName = "Junklite/Mods/Dont Blink")]
    public class DontBlinkMod : ActiveModData
    {
        #region Config

        [Header("Teleport")]
        [Tooltip("Max distance to search for enemies in facing direction")]
        public float searchRange = 8f;

        [Tooltip("How far behind the enemy to teleport")]
        public float behindOffset = 1.5f;

        public LayerMask enemyLayerMask = 1;

        [Tooltip("Layer mask used to raycast for ground beneath the enemy (set to your Ground layer)")]
        public LayerMask groundLayerMask = 1;

        [Tooltip("How far down to raycast when snapping to ground beneath the enemy")]
        public float groundSnapDistance = 10f;

        [Header("Strike")]
        public float strikeDamage = 30f;

        [Tooltip("Bonus multiplier applied on top of base strike damage")]
        public float bonusMultiplier = 1.5f;

        [Header("Animation")]
        public string strikeAnimationName = "DontBlinkStrike";

        [Header("Timing")]
        [Tooltip("Pause after vanishing before reappearing behind enemy")]
        public float vanishDuration = 0.15f;

        [Tooltip("Pause after reappearing before the strike hits")]
        public float strikeDelay = 0.1f;

        [Tooltip("Time after strike before controls are returned")]
        public float recoveryTime = 0.15f;

        [Tooltip("Brief invulnerability after the sequence completes")]
        public float recoveryInvulnerability = 0.2f;

        [Header("VFX")]
        public GameObject teleportOutVFX;
        public GameObject teleportInVFX;
        public GameObject strikeVFX;

        [Header("Hit Feedback")]
        [Tooltip("Camera shake intensity on strike hit")]
        public float hitShakeIntensity = 1.5f;

        #endregion

        #region Overrides

        public override void OnHitRegistered(ModInstance instance, PlayerCharacter player, EnemyCharacter enemy, float damageDealt)
        {
        }

        protected override bool ExecuteAbility(
            ModInstance instance,
            PlayerCharacter player,
            ModExecutionRunner executionRunner)
        {
            var enemy = FindNearestEnemyInFacingDirection(player);
            if (enemy == null)
            {
                Debug.Log("[DontBlink] No enemy found in facing direction within range.");
                return false;
            }

            return executionRunner.TryStart(
                instance,
                context => CoExecuteBlink(context, player, enemy));
        }

        #endregion

        #region Blink Sequence

        private IEnumerator CoExecuteBlink(
            ModExecutionContext context,
            PlayerCharacter player,
            EnemyCharacter enemy)
        {
            var playerState = player.PlayerState;
            var spineAnim = player.GetComponent<SpineAnimationController>();

            Vector3 startPos = player.transform.position;
            context.LockPlayerControl(overridePhysics: true);
            context.AddCleanup(() =>
            {
                if (player != null)
                    player.SetVisible(true);
            });

            if (teleportOutVFX != null)
                Instantiate(teleportOutVFX, startPos, Quaternion.identity);

            player.SetVisible(false);

            yield return new WaitForSeconds(vanishDuration);

            if (enemy == null || !enemy.IsAlive)
            {
                Debug.Log("[DontBlink] Enemy died during vanish, aborting.");
                player.transform.position = startPos;
                player.SetVisible(true);
                yield break;
            }

            Vector3 enemyPos = enemy.transform.position;
            float targetY = GetGroundYBeneathEnemy(enemyPos);

            Vector3 facingDir = GetFacingWorldDirection(player);
            Vector3 behindPos = new Vector3(
                enemyPos.x + facingDir.x * behindOffset,
                targetY,
                enemyPos.z + facingDir.z * behindOffset
            );

            player.transform.position = behindPos;

            // Flip to face the enemy's back
            Vector3 scale = player.transform.localScale;
            float facing = Mathf.Sign(player.transform.localScale.x);
            scale.x = Mathf.Abs(scale.x) * -facing;
            player.transform.localScale = scale;

            player.SetVisible(true);

            if (teleportInVFX != null)
                Instantiate(teleportInVFX, behindPos, Quaternion.identity);

            if (spineAnim != null && !string.IsNullOrEmpty(strikeAnimationName))
                spineAnim.ForcePlayOverride(strikeAnimationName, false, () => { });

            yield return new WaitForSeconds(strikeDelay);

            if (enemy != null && enemy.IsAlive)
            {
                float totalDamage = strikeDamage * bonusMultiplier;
                DamageResult result = DamageReceiverUtility.Receive(
                    enemy,
                    new DamageRequest(
                        totalDamage,
                        player.gameObject,
                        DamageType.Physical,
                        Vector2.zero));

                if (result.WasApplied)
                {
                    SpawnHitEffects(player.transform.position, enemy);

                    if (hitShakeIntensity > 0f && FeedbackManager.Instance != null)
                    {
                        var impulse = player.GetComponent<Unity.Cinemachine.CinemachineImpulseSource>();
                        if (impulse != null)
                            FeedbackManager.Instance.DoCameraShake(impulse, hitShakeIntensity);
                    }
                }

                if (strikeVFX != null)
                    Instantiate(strikeVFX, enemy.transform.position, Quaternion.identity);
            }

            yield return new WaitForSeconds(recoveryTime);

            if (playerState != null)
                playerState.ApplyInvulnerability(recoveryInvulnerability);
        }

        private float GetGroundYBeneathEnemy(Vector3 enemyPos)
        {
            const float upOffset = 1.5f;
            Vector3 rayOrigin = enemyPos + Vector3.up * upOffset;

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit,
                                groundSnapDistance + upOffset, groundLayerMask,
                                QueryTriggerInteraction.Ignore))
                return hit.point.y;

            return enemyPos.y;
        }

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

        #region Helpers

        private Vector3 GetFacingWorldDirection(PlayerCharacter player)
        {
            float flip = Mathf.Sign(player.transform.localScale.x);
            return player.transform.right * flip;
        }

        private EnemyCharacter FindNearestEnemyInFacingDirection(PlayerCharacter player)
        {
            Vector3 origin = player.transform.position;
            Vector3 facingDir = GetFacingWorldDirection(player);

            Collider[] searchResults = Physics.OverlapSphere(
                origin,
                searchRange,
                enemyLayerMask,
                QueryTriggerInteraction.Ignore);

            EnemyCharacter closest = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < searchResults.Length; i++)
            {
                var col = searchResults[i];
                if (col.gameObject == player.gameObject) continue;

                var enemy = col.GetComponentInParent<EnemyCharacter>();
                if (enemy == null || !enemy.IsAlive) continue;

                Vector3 toEnemy = enemy.transform.position - origin;
                if (Vector3.Dot(toEnemy, facingDir) <= 0f) continue;

                float dist = toEnemy.sqrMagnitude;
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = enemy;
                }
            }

            if (closest == null)
                Debug.Log($"[DontBlink] Search found {searchResults.Length} colliders but none matched (facingDir={facingDir}).");

            return closest;
        }

        #endregion
    }
}
