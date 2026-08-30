using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    internal readonly struct WeaponAttackExecutionSettings
    {
        public float DelayBeforeAttack { get; }
        public float AnimationLeadTime { get; }
        public float AttackOpenWindow { get; }
        public float DownAttackFloatNormalized { get; }
        public float EnemyHitHitstopDuration { get; }

        public WeaponAttackExecutionSettings(
            float delayBeforeAttack,
            float animationLeadTime,
            float attackOpenWindow,
            float downAttackFloatNormalized,
            float enemyHitHitstopDuration)
        {
            DelayBeforeAttack = Mathf.Max(0f, delayBeforeAttack);
            AnimationLeadTime = Mathf.Max(0f, animationLeadTime);
            AttackOpenWindow = Mathf.Max(0f, attackOpenWindow);
            DownAttackFloatNormalized = Mathf.Clamp01(downAttackFloatNormalized);
            EnemyHitHitstopDuration = Mathf.Max(0f, enemyHitHitstopDuration);
        }
    }

    internal readonly struct WeaponAttackExecutionRequest
    {
        public int ExecutionId { get; }
        public int WeaponSlot { get; }
        public int ComboIndex { get; }
        public AttackDirection Direction { get; }
        public bool WasGrounded { get; }
        public string AnimationName { get; }
        public WeaponData WeaponData { get; }
        public WeaponInstance Weapon { get; }
        public Transform AttackAnchor { get; }
        public float FallbackRadius { get; }
        public float Facing { get; }
        public Vector3 FacingAxis { get; }
        public WeaponAttackExecutionSettings Settings { get; }

        public WeaponAttackExecutionRequest(
            int executionId,
            int weaponSlot,
            int comboIndex,
            AttackDirection direction,
            bool wasGrounded,
            string animationName,
            WeaponData weaponData,
            WeaponInstance weapon,
            Transform attackAnchor,
            float fallbackRadius,
            float facing,
            Vector3 facingAxis,
            WeaponAttackExecutionSettings settings)
        {
            ExecutionId = executionId;
            WeaponSlot = weaponSlot;
            ComboIndex = comboIndex;
            Direction = direction;
            WasGrounded = wasGrounded;
            AnimationName = animationName;
            WeaponData = weaponData;
            Weapon = weapon;
            AttackAnchor = attackAnchor;
            FallbackRadius = fallbackRadius;
            Facing = facing;
            FacingAxis = facingAxis.normalized;
            Settings = settings;
        }
    }

    /// <summary>
    /// Runs the temporal portion of an already-approved weapon attack. WeaponManager
    /// owns selection, combo state and cancellation; this class only performs the
    /// supplied attack until its execution handle is cancelled.
    /// </summary>
    internal sealed class WeaponAttackExecutor
    {
        private readonly MonoBehaviour coroutineHost;
        private readonly WeaponAttackMotion motion;
        private readonly WeaponHitResolver hitResolver;
        private readonly WeaponDamageResolver damageResolver;
        private readonly SpineAnimationController spineController;
        private readonly Transform playerTransform;
        private readonly Transform muzzlePoint;
        private readonly LayerMask enemyLayer;
        private readonly LayerMask environmentLayer;
        private readonly Action<int, string> requestAnimation;
        private readonly Action<int> completeWithoutAnimation;
        private readonly Action<EnemyCharacter, float> enemyHitApplied;
        private readonly Action environmentHit;
        private readonly Action playHitFeedback;

        public WeaponAttackExecutor(
            MonoBehaviour coroutineHost,
            WeaponAttackMotion motion,
            WeaponHitResolver hitResolver,
            WeaponDamageResolver damageResolver,
            SpineAnimationController spineController,
            Transform playerTransform,
            Transform muzzlePoint,
            LayerMask enemyLayer,
            LayerMask environmentLayer,
            Action<int, string> requestAnimation,
            Action<int> completeWithoutAnimation,
            Action<EnemyCharacter, float> enemyHitApplied,
            Action environmentHit,
            Action playHitFeedback)
        {
            this.coroutineHost = coroutineHost;
            this.motion = motion;
            this.hitResolver = hitResolver;
            this.damageResolver = damageResolver;
            this.spineController = spineController;
            this.playerTransform = playerTransform;
            this.muzzlePoint = muzzlePoint;
            this.enemyLayer = enemyLayer;
            this.environmentLayer = environmentLayer;
            this.requestAnimation = requestAnimation;
            this.completeWithoutAnimation = completeWithoutAnimation;
            this.enemyHitApplied = enemyHitApplied;
            this.environmentHit = environmentHit;
            this.playHitFeedback = playHitFeedback;
        }

        public Execution Prepare(WeaponAttackExecutionRequest request)
        {
            if (coroutineHost == null || motion == null || request.WeaponData == null)
                return null;

            Func<Execution, IEnumerator> routineFactory;

            if (request.WeaponData is MeleeWeaponData meleeData &&
                meleeData.TryGetMeleeStep(
                    request.Direction,
                    request.ComboIndex,
                    request.WasGrounded,
                    out MeleeWeaponData.MeleeComboStep meleeStep))
            {
                routineFactory = execution => RunMelee(execution, request, meleeStep);
            }
            else if (request.WeaponData is RangedWeaponData rangedData &&
                     rangedData.TryGetRangedStep(
                         request.Direction,
                         request.ComboIndex,
                         request.WasGrounded,
                         out RangedWeaponData.RangedComboStep rangedStep))
            {
                routineFactory = execution => RunRanged(
                    execution,
                    request,
                    rangedStep,
                    rangedData);
            }
            else
            {
                return null;
            }

            return new Execution(
                coroutineHost,
                motion,
                request.ExecutionId,
                routineFactory);
        }

        private IEnumerator RunMelee(
            Execution execution,
            WeaponAttackExecutionRequest request,
            MeleeWeaponData.MeleeComboStep step)
        {
            execution.StartOwned(motion.ApplyPush(
                request.ExecutionId,
                request.Direction,
                request.Facing,
                step.forwardImpulse,
                step.verticalImpulse,
                step.forwardImpulseDuration,
                step.lungeCurve));

            if (request.Direction == AttackDirection.Down &&
                !request.WasGrounded &&
                request.Settings.DownAttackFloatNormalized > 0f)
            {
                float hitDelay = step.hitDelay > 0f
                    ? step.hitDelay
                    : Mathf.Max(
                        0f,
                        request.Settings.DelayBeforeAttack - request.Settings.AnimationLeadTime);
                float totalDuration = request.Settings.AnimationLeadTime +
                                      hitDelay +
                                      request.Settings.AttackOpenWindow;
                execution.StartOwned(motion.HoldDownAttackFloat(
                    request.ExecutionId,
                    request.Settings.DownAttackFloatNormalized * totalDuration));
            }

            if (request.Settings.AnimationLeadTime > 0f)
                yield return new WaitForSeconds(request.Settings.AnimationLeadTime);

            if (!execution.IsActive)
                yield break;

            RequestAnimation(request);
            if (!execution.IsActive)
                yield break;

            float resolvedHitDelay = step.hitDelay > 0f
                ? step.hitDelay
                : Mathf.Max(
                    0f,
                    request.Settings.DelayBeforeAttack - request.Settings.AnimationLeadTime);
            if (resolvedHitDelay > 0f)
                yield return new WaitForSeconds(resolvedHitDelay);

            if (!execution.IsActive)
                yield break;

            bool isPiercing = step.piercing || (request.Weapon?.PiercingOverride ?? false);
            float radius = step.hitRadius + request.FallbackRadius;
            Vector2 knockback = step.overrideKnockback
                ? step.knockback
                : request.WeaponData.knockbackForce;
            bool hasHitEnemy = false;
            bool hasHitEnvironment = false;
            float windowEnd = Time.time + request.Settings.AttackOpenWindow;
            Vector3 hitOrigin = ResolveHitOrigin(request);

            while (execution.IsActive && Time.time < windowEnd)
            {
                WeaponHitDetectionResult hitResult = hitResolver.Detect(
                    hitOrigin,
                    radius,
                    isPiercing);

                if (hitResult.Type == AttackHitResult.Enemy && !hasHitEnemy)
                {
                    hasHitEnemy = true;
                    bool damageApplied = isPiercing && hitResult.AllTargets != null
                        ? DealDamageToAll(request, hitResult.AllTargets, step.damageMultiplier, knockback)
                        : hitResult.Target != null &&
                          DealDamage(request, hitResult.Target, step.damageMultiplier, knockback).WasApplied;

                    if (damageApplied)
                    {
                        playHitFeedback?.Invoke();
                        execution.StartOwned(motion.ApplyHitstop(
                            request.ExecutionId,
                            request.Settings.EnemyHitHitstopDuration));
                        motion.ApplyImmediateRecoil(
                            request.ExecutionId,
                            request.Direction,
                            request.Facing,
                            step.hitRecoil);
                    }

                    yield break;
                }

                if (hitResult.Type == AttackHitResult.Environment && !hasHitEnvironment)
                {
                    hasHitEnvironment = true;
                    float radiusForVfx = step.hitRadius > 0f
                        ? step.hitRadius
                        : request.FallbackRadius;
                    Vector3 impactPoint = ResolveImpactPoint(
                        request,
                        hitOrigin,
                        radiusForVfx);
                    Vector3 attackDirection = GetAttackDirection(request);
                    if (CombatEffectsManager.Instance != null)
                    {
                        CombatEffectsManager.Instance.SpawnEnvHitParticle(
                            impactPoint,
                            attackDirection);
                        CombatEffectsManager.Instance.SpawnHitCross(impactPoint);
                        environmentHit?.Invoke();
                    }

                    motion.ApplyImmediateRecoil(
                        request.ExecutionId,
                        request.Direction,
                        request.Facing,
                        step.hitRecoil);
                }

                yield return null;
            }
        }

        private IEnumerator RunRanged(
            Execution execution,
            WeaponAttackExecutionRequest request,
            RangedWeaponData.RangedComboStep step,
            RangedWeaponData rangedData)
        {
            RequestAnimation(request);
            if (!execution.IsActive)
                yield break;

            bool useHover = step.hoverGravityMultiplier >= 0f;
            if (useHover)
                motion.BeginHover(request.ExecutionId, step.hoverGravityMultiplier);

            yield return WaitForFirePose(execution, request, step.fireAtNormalizedTime);
            if (!execution.IsActive)
                yield break;

            if (!Mathf.Approximately(step.forwardImpulse, 0f))
            {
                execution.StartOwned(motion.ApplyPush(
                    request.ExecutionId,
                    request.Direction,
                    request.Facing,
                    step.forwardImpulse,
                    0f,
                    step.forwardImpulseDuration,
                    null));
            }

            bool useBulletTime = step.bulletTimeScale > 0f && step.bulletTimeScale < 1f;
            if (useBulletTime)
                execution.BeginBulletTime(step.bulletTimeScale);

            bool isDirectional = request.Direction is AttackDirection.Down or AttackDirection.Up;
            bool hitAnyEnemy;
            if (isDirectional)
                hitAnyEnemy = FireDirectionalBlast(request, step, rangedData);
            else
                hitAnyEnemy = FireSideHitscan(request, step, rangedData);

            if (hitAnyEnemy)
            {
                execution.StartOwned(motion.ApplyHitstop(
                    request.ExecutionId,
                    request.Settings.EnemyHitHitstopDuration));
            }

            if (useBulletTime && step.bulletTimeDuration > 0f)
            {
                float elapsed = 0f;
                while (execution.IsActive && elapsed < step.bulletTimeDuration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            if (!execution.IsActive)
                yield break;

            if (!Mathf.Approximately(step.hitRecoil, 0f))
            {
                execution.StartOwned(motion.ApplySmoothRecoil(
                    request.ExecutionId,
                    request.Direction,
                    request.Facing,
                    step.hitRecoil,
                    step.recoilDuration));
            }

            if (useBulletTime)
            {
                float restoreDuration = step.bulletTimeRestoreDuration > 0f
                    ? step.bulletTimeRestoreDuration
                    : 0.1f;
                execution.StartOwned(execution.RestoreBulletTime(restoreDuration));
            }

            if (useHover)
                motion.EndHover(request.ExecutionId);
        }

        private bool FireDirectionalBlast(
            WeaponAttackExecutionRequest request,
            RangedWeaponData.RangedComboStep step,
            RangedWeaponData rangedData)
        {
            int durabilityCost = Mathf.Max(
                1,
                Mathf.RoundToInt(step.durabilityMultiplier > 0f
                    ? step.durabilityMultiplier
                    : 1f));
            if (!TryConsumeUseDurability(request.Weapon, durabilityCost))
                return false;

            float damage = rangedData.baseDamage *
                           (step.damageMultiplier > 0f ? step.damageMultiplier : 1f);
            Vector2 knockback = rangedData.knockbackForce;
            float blastRadius = step.blastDamageRadius > 0f
                ? step.blastDamageRadius
                : 1.5f;
            Vector3 blastOrigin = ResolveBlastOrigin(
                request,
                blastRadius,
                step.blastForwardOffset);
            Collider[] enemyHits = Physics.OverlapSphere(
                blastOrigin,
                blastRadius,
                enemyLayer,
                QueryTriggerInteraction.Ignore);
            bool hitAnyEnemy = false;
            var processedReceivers = new HashSet<int>();

            foreach (Collider hit in enemyHits)
            {
                DamageResult result = ResolveDamage(
                    hit,
                    damage,
                    knockback,
                    processedReceivers);
                if (!result.WasApplied)
                    continue;

                hitAnyEnemy = true;
                if (CombatEffectsManager.Instance != null)
                {
                    Vector3 hitPoint = hit.ClosestPoint(blastOrigin);
                    Vector3 hitDirection = (hitPoint - blastOrigin).normalized;
                    CombatEffectsManager.Instance.SpawnEnemyHitVFX(hitPoint, hitDirection);
                    CombatEffectsManager.Instance.SpawnEnemyHurtParticle(hitPoint, hitDirection);
                }
            }

            if (hitAnyEnemy)
                playHitFeedback?.Invoke();

            bool hitAnyEnvironment = SpawnDirectionalEnvironmentHits(
                request,
                blastOrigin,
                blastRadius);
            if (hitAnyEnvironment && !hitAnyEnemy)
                environmentHit?.Invoke();

            return hitAnyEnemy;
        }

        private bool FireSideHitscan(
            WeaponAttackExecutionRequest request,
            RangedWeaponData.RangedComboStep step,
            RangedWeaponData rangedData)
        {
            int durabilityCost = Mathf.Max(
                1,
                Mathf.RoundToInt(step.durabilityMultiplier > 0f
                    ? step.durabilityMultiplier
                    : 1f));
            if (!TryConsumeUseDurability(request.Weapon, durabilityCost))
                return false;

            Transform muzzle = muzzlePoint != null ? muzzlePoint : request.AttackAnchor;
            Vector3 origin = muzzle != null ? muzzle.position : playerTransform.position;
            Vector3 direction = request.FacingAxis * request.Facing;
            float maxRange = step.maxRange > 0f ? step.maxRange : 50f;
            float castRadius = step.bulletRadius;
            float tracerDuration = step.tracerDuration > 0f ? step.tracerDuration : 0.06f;
            float damage = rangedData.baseDamage *
                           (step.damageMultiplier > 0f ? step.damageMultiplier : 1f);
            Vector2 knockback = rangedData.knockbackForce;
            bool piercing = rangedData.piercing;
            Vector3 tracerEnd = origin + direction * maxRange;
            bool hitAnyEnemy = false;

            if (piercing)
            {
                RaycastHit[] allHits = castRadius > 0f
                    ? Physics.SphereCastAll(
                        origin,
                        castRadius,
                        direction,
                        maxRange,
                        enemyLayer | environmentLayer,
                        QueryTriggerInteraction.Ignore)
                    : Physics.RaycastAll(
                        origin,
                        direction,
                        maxRange,
                        enemyLayer | environmentLayer,
                        QueryTriggerInteraction.Ignore);
                Array.Sort(allHits, (a, b) => a.distance.CompareTo(b.distance));

                bool hitEnvironment = false;
                var processedReceivers = new HashSet<int>();
                foreach (RaycastHit hit in allHits)
                {
                    int hitMask = 1 << hit.collider.gameObject.layer;
                    if ((hitMask & enemyLayer) != 0)
                    {
                        DamageResult result = ResolveDamage(
                            hit.collider,
                            damage,
                            knockback,
                            processedReceivers);
                        if (!result.WasApplied)
                            continue;

                        hitAnyEnemy = true;
                        SpawnEnemyHitVfx(hit.point, -direction);
                    }
                    else if ((hitMask & environmentLayer) != 0 && !hitEnvironment)
                    {
                        hitEnvironment = true;
                        tracerEnd = hit.point;
                        SpawnEnvironmentHitVfx(hit.point, hit.normal);
                        if (!hitAnyEnemy)
                            environmentHit?.Invoke();
                    }
                }

                if (hitAnyEnemy)
                    playHitFeedback?.Invoke();
            }
            else
            {
                bool hitSomething;
                RaycastHit hit;
                if (castRadius > 0f)
                {
                    hitSomething = Physics.SphereCast(
                        origin,
                        castRadius,
                        direction,
                        out hit,
                        maxRange,
                        enemyLayer | environmentLayer,
                        QueryTriggerInteraction.Ignore);
                }
                else
                {
                    hitSomething = Physics.Raycast(
                        origin,
                        direction,
                        out hit,
                        maxRange,
                        enemyLayer | environmentLayer,
                        QueryTriggerInteraction.Ignore);
                }

                if (hitSomething)
                {
                    tracerEnd = hit.point;
                    int hitMask = 1 << hit.collider.gameObject.layer;
                    if ((hitMask & enemyLayer) != 0)
                    {
                        DamageResult result = ResolveDamage(
                            hit.collider,
                            damage,
                            knockback,
                            null);
                        if (result.WasApplied)
                        {
                            hitAnyEnemy = true;
                            playHitFeedback?.Invoke();
                            SpawnEnemyHitVfx(hit.point, -direction);
                        }
                    }
                    else if ((hitMask & environmentLayer) != 0)
                    {
                        SpawnEnvironmentHitVfx(hit.point, hit.normal);
                        environmentHit?.Invoke();
                    }
                }
            }

            if (rangedData.tracerPrefab != null && ProjectileManager.Instance != null)
            {
                ProjectileManager.Instance.FireTracer(
                    rangedData.tracerPrefab,
                    origin,
                    tracerEnd,
                    tracerDuration);
            }

            return hitAnyEnemy;
        }

        private IEnumerator WaitForFirePose(
            Execution execution,
            WeaponAttackExecutionRequest request,
            float normalizedTime)
        {
            if (spineController == null)
            {
                if (request.Settings.DelayBeforeAttack > 0f)
                    yield return new WaitForSeconds(request.Settings.DelayBeforeAttack);
                yield break;
            }

            yield return null;
            float elapsed = 0f;
            float timeout = Mathf.Max(request.Settings.DelayBeforeAttack * 4f, 0.5f);

            while (execution.IsActive && elapsed < timeout)
            {
                var entry = spineController.CurrentAttackEntry;
                if (entry != null && entry.AnimationEnd > 0f)
                {
                    float normalizedProgress = entry.TrackTime / entry.AnimationEnd;
                    if (normalizedProgress >= normalizedTime)
                        yield break;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

        }

        private void RequestAnimation(WeaponAttackExecutionRequest request)
        {
            if (!string.IsNullOrEmpty(request.AnimationName))
                requestAnimation?.Invoke(request.ExecutionId, request.AnimationName);
            else
                completeWithoutAnimation?.Invoke(request.ExecutionId);
        }

        private DamageResult DealDamage(
            WeaponAttackExecutionRequest request,
            Collider target,
            float damageMultiplier,
            Vector2 knockback)
        {
            float damage = CalculateDamage(request.WeaponData, damageMultiplier);
            DamageResult result = ResolveDamage(target, damage, knockback, null);
            if (!result.WasApplied)
                return result;

            if (request.WeaponSlot != 0 && request.Weapon != null)
                request.Weapon.ConsumeDurability();
            SpawnEnemyHitVfx(request, target);
            return result;
        }

        private bool DealDamageToAll(
            WeaponAttackExecutionRequest request,
            Collider[] targets,
            float damageMultiplier,
            Vector2 knockback)
        {
            bool anyHit = false;
            float damage = CalculateDamage(request.WeaponData, damageMultiplier);
            var processedReceivers = new HashSet<int>();

            foreach (Collider target in targets)
            {
                DamageResult result = ResolveDamage(
                    target,
                    damage,
                    knockback,
                    processedReceivers);
                if (!result.WasApplied)
                    continue;

                anyHit = true;
                SpawnEnemyHitVfx(request, target);
            }

            if (anyHit && request.WeaponSlot != 0 && request.Weapon != null)
                request.Weapon.ConsumeDurability();
            return anyHit;
        }

        private DamageResult ResolveDamage(
            Collider target,
            float damage,
            Vector2 knockback,
            HashSet<int> processedReceivers)
        {
            WeaponDamageResolution resolution = damageResolver.Resolve(
                target,
                damage,
                knockback,
                processedReceivers);
            if (resolution.Result.WasApplied)
                enemyHitApplied?.Invoke(resolution.Enemy, resolution.Result.AppliedDamage);
            return resolution.Result;
        }

        private bool SpawnDirectionalEnvironmentHits(
            WeaponAttackExecutionRequest request,
            Vector3 blastOrigin,
            float blastRadius)
        {
            if (CombatEffectsManager.Instance == null)
                return false;

            float rayLength = blastRadius * 2f;
            bool hitAnyEnvironment = false;

            if (Physics.Raycast(
                    blastOrigin,
                    Vector3.down,
                    out RaycastHit groundHit,
                    rayLength,
                    environmentLayer,
                    QueryTriggerInteraction.Ignore))
            {
                SpawnEnvironmentHitVfx(groundHit.point, groundHit.normal);
                hitAnyEnvironment = true;
            }

            Vector3 forwardDirection = request.FacingAxis * request.Facing;
            if (Physics.Raycast(
                    blastOrigin,
                    forwardDirection,
                    out RaycastHit wallHit,
                    rayLength,
                    environmentLayer,
                    QueryTriggerInteraction.Ignore))
            {
                SpawnEnvironmentHitVfx(wallHit.point, wallHit.normal);
                hitAnyEnvironment = true;
            }

            Vector3 attackVector = request.Direction == AttackDirection.Down
                ? Vector3.down
                : Vector3.up;
            if (attackVector != Vector3.down || !hitAnyEnvironment)
            {
                if (Physics.Raycast(
                        playerTransform.position,
                        attackVector,
                        out RaycastHit directionHit,
                        rayLength,
                        environmentLayer,
                        QueryTriggerInteraction.Ignore))
                {
                    SpawnEnvironmentHitVfx(directionHit.point, directionHit.normal);
                    hitAnyEnvironment = true;
                }
            }

            return hitAnyEnvironment;
        }

        private Vector3 ResolveHitOrigin(WeaponAttackExecutionRequest request)
        {
            Transform anchor = request.AttackAnchor;
            Vector3 anchorPosition = anchor != null ? anchor.position : playerTransform.position;
            float range = (request.WeaponData as MeleeWeaponData)?.attackRange ?? 1f;

            return request.Direction switch
            {
                AttackDirection.Side =>
                    anchorPosition + request.FacingAxis * (request.Facing * range),
                AttackDirection.Up => new Vector3(
                    anchorPosition.x,
                    playerTransform.position.y + range,
                    playerTransform.position.z),
                AttackDirection.Down => new Vector3(
                    anchorPosition.x,
                    playerTransform.position.y - range,
                    playerTransform.position.z),
                _ => playerTransform.position
            };
        }

        private Vector3 ResolveBlastOrigin(
            WeaponAttackExecutionRequest request,
            float radius,
            float forwardOffset)
        {
            Vector3 origin = playerTransform.position;
            if (forwardOffset > 0f)
                origin += request.FacingAxis * request.Facing * forwardOffset;

            if (request.Direction == AttackDirection.Down)
                origin.y -= radius * 0.5f;
            else if (request.Direction == AttackDirection.Up)
                origin.y += radius * 0.5f;
            return origin;
        }

        private Vector3 ResolveImpactPoint(
            WeaponAttackExecutionRequest request,
            Vector3 origin,
            float radius)
        {
            Vector3 attackDirection = GetAttackDirection(request);
            Vector3 rayStart = origin - attackDirection * (radius + 0.25f);
            float rayLength = (radius + 0.25f) + (radius + 0.5f);
            Vector3 point;
            Vector3 normal;

            if (Physics.Raycast(
                    rayStart,
                    attackDirection,
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
                point = origin + attackDirection * radius;
                normal = -attackDirection;
            }

            point += normal * 0.06f;
            Camera camera = Camera.main;
            if (camera != null)
                point += -camera.transform.forward * 0.1f;
            return point;
        }

        private static float CalculateDamage(WeaponData data, float damageMultiplier)
        {
            float damage = data != null ? data.baseDamage : 10f;
            return damageMultiplier > 0f ? damage * damageMultiplier : damage;
        }

        private static bool TryConsumeUseDurability(WeaponInstance weapon, int cost)
        {
            if (weapon == null)
                return true;

            for (int index = 0; index < cost; index++)
            {
                if (weapon.ConsumeDurability())
                    return false;
            }

            return !weapon.IsBroken;
        }

        private static Vector3 GetAttackDirection(WeaponAttackExecutionRequest request)
        {
            return request.Direction switch
            {
                AttackDirection.Up => Vector3.up,
                AttackDirection.Down => Vector3.down,
                _ => request.FacingAxis * request.Facing
            };
        }

        private static void SpawnEnemyHitVfx(Vector3 point, Vector3 direction)
        {
            if (CombatEffectsManager.Instance == null)
                return;
            CombatEffectsManager.Instance.SpawnEnemyHitVFX(point, direction);
            CombatEffectsManager.Instance.SpawnEnemyHurtParticle(point, direction);
        }

        private void SpawnEnemyHitVfx(
            WeaponAttackExecutionRequest request,
            Collider target)
        {
            if (CombatEffectsManager.Instance == null || target == null)
                return;

            Vector3 origin = request.AttackAnchor != null
                ? request.AttackAnchor.position
                : playerTransform.position + Vector3.up;
            Vector3 hitPoint = target.ClosestPoint(origin);
            Vector3 hitDirection = (hitPoint - origin).normalized;
            SpawnEnemyHitVfx(hitPoint, hitDirection);
        }

        private static void SpawnEnvironmentHitVfx(Vector3 point, Vector3 normal)
        {
            if (CombatEffectsManager.Instance == null)
                return;
            CombatEffectsManager.Instance.SpawnEnvHitParticle(point, normal);
            CombatEffectsManager.Instance.SpawnHitCross(point);
        }

        internal sealed class Execution : IDisposable
        {
            private readonly MonoBehaviour coroutineHost;
            private readonly WeaponAttackMotion motion;
            private readonly int executionId;
            private readonly Func<Execution, IEnumerator> routineFactory;
            private readonly List<Coroutine> ownedCoroutines = new();

            private bool started;
            private bool cancelled;
            private bool ownsTimeScale;
            private float savedTimeScale;
            private float savedFixedDeltaTime;

            public bool IsActive => started && !cancelled;

            public Execution(
                MonoBehaviour coroutineHost,
                WeaponAttackMotion motion,
                int executionId,
                Func<Execution, IEnumerator> routineFactory)
            {
                this.coroutineHost = coroutineHost;
                this.motion = motion;
                this.executionId = executionId;
                this.routineFactory = routineFactory;
            }

            public void Start()
            {
                if (started || cancelled)
                    return;

                started = true;
                motion.BeginExecution(executionId);
                StartOwned(routineFactory(this));
            }

            public void StartOwned(IEnumerator routine)
            {
                if (!IsActive || routine == null || coroutineHost == null)
                    return;

                Coroutine coroutine = coroutineHost.StartCoroutine(routine);
                if (IsActive && coroutine != null)
                    ownedCoroutines.Add(coroutine);
            }

            public void BeginBulletTime(float timeScale)
            {
                if (!IsActive || ownsTimeScale)
                    return;

                savedTimeScale = Time.timeScale;
                savedFixedDeltaTime = Time.fixedDeltaTime;
                ownsTimeScale = true;

                float baselineScale = Mathf.Max(0.0001f, savedTimeScale);
                Time.timeScale = timeScale;
                Time.fixedDeltaTime = savedFixedDeltaTime * (timeScale / baselineScale);
            }

            public IEnumerator RestoreBulletTime(float realDuration)
            {
                if (!IsActive || !ownsTimeScale)
                    yield break;

                float startScale = Time.timeScale;
                float startFixedDelta = Time.fixedDeltaTime;
                float elapsed = 0f;
                float duration = Mathf.Max(0.01f, realDuration);

                while (IsActive && elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    Time.timeScale = Mathf.Lerp(startScale, savedTimeScale, t);
                    Time.fixedDeltaTime = Mathf.Lerp(
                        startFixedDelta,
                        savedFixedDeltaTime,
                        t);
                    yield return null;
                }

                if (!IsActive)
                    yield break;

                RestoreTimeScaleImmediate();
            }

            public void Dispose()
            {
                Cancel();
            }

            public void Cancel()
            {
                if (cancelled)
                    return;

                cancelled = true;
                for (int index = ownedCoroutines.Count - 1; index >= 0; index--)
                {
                    Coroutine coroutine = ownedCoroutines[index];
                    if (coroutine != null && coroutineHost != null)
                        coroutineHost.StopCoroutine(coroutine);
                }
                ownedCoroutines.Clear();

                RestoreTimeScaleImmediate();
                motion.EndExecution(executionId);
            }

            private void RestoreTimeScaleImmediate()
            {
                if (!ownsTimeScale)
                    return;

                Time.timeScale = savedTimeScale;
                Time.fixedDeltaTime = savedFixedDeltaTime;
                ownsTimeScale = false;
            }
        }
    }
}
