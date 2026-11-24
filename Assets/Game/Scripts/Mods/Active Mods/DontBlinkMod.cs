using UnityEngine;
using System.Collections;
using System.Buffers;

namespace junklite
{
    [CreateAssetMenu(fileName = "DontBlinkMod", menuName = "Junklite/Mods/Dont Blink")]
    public class DontBlinkMod : ActiveModData
    {
        private const int MaxSearchCapacity = 256;
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

        [Header("Gate")]
        [Tooltip("Solid geometry the blink may not pass through or land in (walls, doors, floors).")]
        public LayerMask blockerLayerMask = 6145; // Default | Ground | Wall
        [Tooltip("Height above the feet of the line that must be clear from her to the landing spot.")]
        public float gateCheckHeight = 1f;

        [Header("Strike")]
        public float strikeDamage = 30f;

        [Tooltip("Bonus multiplier applied on top of base strike damage")]
        public float bonusMultiplier = 1.5f;

        [Header("Animation")]
        public string strikeAnimationName = "Attack_Punch_Mantis_2";

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
        [Tooltip("Seconds before a spawned VFX is destroyed. Effects in the pack do not clean themselves up.")]
        public float vfxLifetime = 2f;

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
            // Only an enemy she can reach without crossing a wall, door or camera switch trigger.
            var enemy = FindNearestEnemyInFacingDirection(player);
            if (enemy == null)
            {
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
            context.ShowActiveDuration(Mathf.Max(0f, vanishDuration) + Mathf.Max(0f, strikeDelay) + Mathf.Max(0f, recoveryTime));
            var playerState = player.PlayerState;
            var spineAnim = player.GetComponentInChildren<SpineAnimationController>();

            Vector3 startPos = player.transform.position;
            Rigidbody body = player.GetComponent<Rigidbody>();
            context.LockPlayerControl(overridePhysics: true);

            // Spine draws with mesh renderers, which SetVisible does not touch: hide them here.
            Renderer[] hidden = player.GetComponentsInChildren<Renderer>(false);
            context.AddCleanup(() => SetRenderers(hidden, true));
            context.AddCleanup(() =>
            {
                if (player != null)
                    player.SetVisible(true);
            });

            if (teleportOutVFX != null)
                Destroy(Instantiate(teleportOutVFX, startPos, Quaternion.identity), vfxLifetime);

            player.SetVisible(false);
            SetRenderers(hidden, false);

            yield return new WaitForSeconds(vanishDuration);

            // The enemy may have moved during the vanish: re-check the landing spot and the gate.
            if (enemy == null || !enemy.IsAlive || !TryGetBlinkTarget(player, enemy, out Vector3 behindPos))
            {
                Place(player, body, startPos);
                SetRenderers(hidden, true);
                player.SetVisible(true);
                yield break;
            }

            Place(player, body, behindPos);

            // Face the enemy's back.
            if (player.Controller != null)
                player.Controller.SetFacingDirection(
                    Vector3.Dot(enemy.transform.position - behindPos, player.transform.right) > 0f);

            SetRenderers(hidden, true);
            player.SetVisible(true);

            if (teleportInVFX != null)
                Destroy(Instantiate(teleportInVFX, behindPos, Quaternion.identity), vfxLifetime);

            if (spineAnim != null && !string.IsNullOrEmpty(strikeAnimationName))
                spineAnim.ForcePlayOverride(strikeAnimationName, false, () => { });

            context.ShowActiveDuration(Mathf.Max(0f, strikeDelay) + Mathf.Max(0f, recoveryTime));
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
                    Destroy(Instantiate(strikeVFX, enemy.transform.position, Quaternion.identity), vfxLifetime);
            }

            context.ShowActiveDuration(recoveryTime);
            yield return new WaitForSeconds(recoveryTime);

            if (playerState != null)
                playerState.ApplyInvulnerability(recoveryInvulnerability);
        }

        /// <summary>
        /// The spot behind the enemy's back, on her lane line and standing on the floor, or false
        /// when it can't be reached: a wall or door in the way, a camera switch trigger between
        /// her and the spot (the blink must never skip a corner), or the spot inside geometry.
        /// </summary>
        private bool TryGetBlinkTarget(PlayerCharacter player, EnemyCharacter enemy, out Vector3 target)
        {
            Vector3 laneRight = player.transform.right;
            Vector3 playerPos = player.transform.position;
            Vector3 enemyPos = enemy.transform.position;

            // Behind = opposite the way the enemy faces; with no facing known, the far side from her.
            float toEnemy = Mathf.Sign(Vector3.Dot(enemyPos - playerPos, laneRight));
            float backSide = toEnemy;
            EnemyMovement movement = enemy.Movement;
            if (movement != null)
            {
                float facingAlongLane = Vector3.Dot(movement.MovementAxis * movement.FacingDirection, laneRight);
                if (Mathf.Abs(facingAlongLane) > 0.1f)
                    backSide = -Mathf.Sign(facingAlongLane);
            }

            target = enemyPos + laneRight * (backSide * behindOffset);
            // Stay on her lane line (free roam: the enemy's depth).
            Vector3 depthAxis = player.transform.forward;
            bool freeRoam = player.Controller != null && player.Controller.IsFreeRoaming;
            float depth = Vector3.Dot(freeRoam ? enemyPos : playerPos, depthAxis);
            target += depthAxis * (depth - Vector3.Dot(target, depthAxis));

            // Feet on the floor under the spot (pivot height above her collider bottom kept).
            Collider body = player.GetComponent<Collider>();
            float pivotAboveFeet = body != null ? playerPos.y - body.bounds.min.y : 0f;
            LayerMask floorMask = groundLayerMask | (player.Controller != null ? player.Controller.GroundLayerMask : (LayerMask)0);
            const float upOffset = 1.5f;
            target.y = Physics.Raycast(new Vector3(target.x, enemyPos.y + upOffset, target.z), Vector3.down,
                out RaycastHit floor, groundSnapDistance + upOffset, floorMask, QueryTriggerInteraction.Ignore)
                ? floor.point.y + pivotAboveFeet
                : playerPos.y;

            return IsBlinkPathClear(player, playerPos, target, body);
        }

        private bool IsBlinkPathClear(PlayerCharacter player, Vector3 from, Vector3 to, Collider body)
        {
            Vector3 a = from + Vector3.up * gateCheckHeight;
            Vector3 b = to + Vector3.up * gateCheckHeight;

            // Walls and doors between her and the spot.
            if (Physics.Linecast(a, b, blockerLayerMask, QueryTriggerInteraction.Ignore))
                return false;

            // Never across a camera switch trigger: the corner would be skipped.
            Vector3 delta = b - a;
            float length = delta.magnitude;
            if (length > 0.001f)
            {
                RaycastHit[] hits = Physics.RaycastAll(a, delta / length, length, ~0, QueryTriggerInteraction.Collide);
                foreach (RaycastHit hit in hits)
                {
                    if (hit.collider.GetComponentInParent<CameraSwitchTrigger>() != null)
                        return false;
                }
            }

            // The spot itself must be free (not inside a wall or a ledge).
            float radius = body is CapsuleCollider capsule ? capsule.radius * 0.9f : 0.3f;
            Vector3 low = to + Vector3.up * (radius + 0.05f);
            Vector3 high = to + Vector3.up * Mathf.Max(radius + 0.05f, gateCheckHeight * 1.5f);
            return !Physics.CheckCapsule(low, high, radius, blockerLayerMask, QueryTriggerInteraction.Ignore);
        }

        private static void Place(PlayerCharacter player, Rigidbody body, Vector3 position)
        {
            if (body != null)
            {
                body.position = position;
                body.linearVelocity = Vector3.zero;
            }
            player.transform.position = position;
        }

        private static void SetRenderers(Renderer[] renderers, bool visible)
        {
            if (renderers == null) return;
            foreach (Renderer r in renderers)
                if (r != null) r.enabled = visible;
        }

        private void SpawnHitEffects(Vector3 attackOrigin, EnemyCharacter enemy)
        {
            if (CombatEffectsManager.Instance == null) return;

            var enemyCollider = enemy.GetComponent<Collider>();
            if (enemyCollider == null) return;

            Vector3 hitPoint = CombatEffectsManager.BodyHitPoint(enemyCollider);
            Vector3 hitDir = (hitPoint - attackOrigin).normalized;

            CombatEffectsManager.Instance.SpawnEnemyHitVFX(hitPoint, hitDir);
            CombatEffectsManager.Instance.SpawnEnemyHurtParticle(hitPoint, hitDir);
        }

        #endregion

        #region Helpers

        private Vector3 GetFacingWorldDirection(PlayerCharacter player)
        {
            float flip = player.Controller != null
                ? player.Controller.FacingSign
                : Mathf.Sign(player.transform.localScale.x);
            return player.transform.right * flip;
        }

        private EnemyCharacter FindNearestEnemyInFacingDirection(PlayerCharacter player)
        {
            Vector3 origin = player.transform.position;
            Vector3 facingDir = GetFacingWorldDirection(player);

            Collider[] searchResults = ArrayPool<Collider>.Shared.Rent(64);
            int searchCount;
            while (true)
            {
                searchCount = Physics.OverlapSphereNonAlloc(
                    origin,
                    searchRange,
                    searchResults,
                    enemyLayerMask,
                    QueryTriggerInteraction.Ignore);
                if (searchCount < searchResults.Length || searchResults.Length >= MaxSearchCapacity)
                    break;

                Collider[] largerBuffer = ArrayPool<Collider>.Shared.Rent(
                    Mathf.Min(searchResults.Length * 2, MaxSearchCapacity));
                ArrayPool<Collider>.Shared.Return(searchResults, clearArray: true);
                searchResults = largerBuffer;
            }

            EnemyCharacter closest = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < searchCount; i++)
            {
                var col = searchResults[i];
                if (col == null) continue;
                if (col.gameObject == player.gameObject) continue;

                var enemy = col.GetComponentInParent<EnemyCharacter>();
                if (enemy == null || !enemy.IsAlive) continue;

                Vector3 toEnemy = enemy.transform.position - origin;
                if (Vector3.Dot(toEnemy, facingDir) <= 0f) continue;

                float dist = toEnemy.sqrMagnitude;
                if (dist < closestDist && TryGetBlinkTarget(player, enemy, out _))
                {
                    closestDist = dist;
                    closest = enemy;
                }
            }

            ArrayPool<Collider>.Shared.Return(searchResults, clearArray: true);
            return closest;
        }

        #endregion
    }
}
