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
        /// <summary>Same as DownAttackFloatNormalized, for side and up attacks started in the air.</summary>
        public float AirAttackFloatNormalized { get; }
        public float EnemyHitHitstopDuration { get; }
        public float LaunchHitstopDuration { get; }
        /// <summary>Knockback multiplier applied to Launch finishers (upgrade). 1 = authored.</summary>
        public float FinisherForceMultiplier { get; }

        public WeaponAttackExecutionSettings(
            float delayBeforeAttack,
            float animationLeadTime,
            float attackOpenWindow,
            float downAttackFloatNormalized,
            float airAttackFloatNormalized,
            float enemyHitHitstopDuration,
            float launchHitstopDuration = 0f,
            float finisherForceMultiplier = 1f)
        {
            FinisherForceMultiplier = Mathf.Max(0f, finisherForceMultiplier);
            DelayBeforeAttack = Mathf.Max(0f, delayBeforeAttack);
            AnimationLeadTime = Mathf.Max(0f, animationLeadTime);
            AttackOpenWindow = Mathf.Max(0f, attackOpenWindow);
            DownAttackFloatNormalized = Mathf.Clamp01(downAttackFloatNormalized);
            AirAttackFloatNormalized = Mathf.Clamp01(airAttackFloatNormalized);
            EnemyHitHitstopDuration = Mathf.Max(0f, enemyHitHitstopDuration);
            LaunchHitstopDuration = Mathf.Max(EnemyHitHitstopDuration, launchHitstopDuration);
        }
    }

    internal readonly struct WeaponAttackExecutionRequest
    {
        public int ExecutionId { get; }
        public int ComboIndex { get; }
        public AttackDirection Direction { get; }
        public bool WasGrounded { get; }
        public string AnimationName { get; }
        public WeaponData WeaponData { get; }
        public Transform AttackAnchor { get; }
        public float FallbackRadius { get; }
        public float Facing { get; }
        public Vector3 FacingAxis { get; }
        public WeaponAttackExecutionSettings Settings { get; }

        public WeaponAttackExecutionRequest(
            int executionId,
            int comboIndex,
            AttackDirection direction,
            bool wasGrounded,
            string animationName,
            WeaponData weaponData,
            Transform attackAnchor,
            float fallbackRadius,
            float facing,
            Vector3 facingAxis,
            WeaponAttackExecutionSettings settings)
        {
            ExecutionId = executionId;
            ComboIndex = comboIndex;
            Direction = direction;
            WasGrounded = wasGrounded;
            AnimationName = animationName;
            WeaponData = weaponData;
            AttackAnchor = attackAnchor;
            FallbackRadius = fallbackRadius;
            Facing = facing;
            FacingAxis = facingAxis.normalized;
            Settings = settings;
        }
    }

    /// <summary>
    /// Runs the temporal portion of an already-approved melee attack. WeaponManager
    /// owns selection, combo state and cancellation; this class only performs the
    /// supplied attack until its execution handle is cancelled.
    /// </summary>
    internal sealed class WeaponAttackExecutor
    {
        private readonly MonoBehaviour coroutineHost;
        private readonly WeaponAttackMotion motion;
        private readonly WeaponHitResolver hitResolver;
        private readonly WeaponDamageResolver damageResolver;
        private readonly Transform playerTransform;
        private readonly LayerMask environmentLayer;
        private readonly Action<int, string> requestAnimation;
        private readonly Action<int> completeWithoutAnimation;
        private readonly Action<EnemyCharacter, float> enemyHitApplied;
        private readonly Action environmentHit;
        private readonly Action playHitFeedback;
        private readonly HashSet<int> processedReceivers = new();

        public WeaponAttackExecutor(
            MonoBehaviour coroutineHost,
            WeaponAttackMotion motion,
            WeaponHitResolver hitResolver,
            WeaponDamageResolver damageResolver,
            Transform playerTransform,
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
            this.playerTransform = playerTransform;
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

            if (request.WeaponData is not MeleeWeaponData meleeData ||
                !meleeData.TryGetMeleeStep(
                    request.Direction,
                    request.ComboIndex,
                    request.WasGrounded,
                    out MeleeWeaponData.MeleeComboStep meleeStep))
            {
                return null;
            }

            return new Execution(
                coroutineHost,
                motion,
                request.ExecutionId,
                execution => RunMelee(execution, request, meleeStep));
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

            // Air attacks stall the fall for part of the swing, then gravity resumes.
            float floatNormalized = request.Direction == AttackDirection.Down
                ? request.Settings.DownAttackFloatNormalized
                : request.Settings.AirAttackFloatNormalized;
            if (!request.WasGrounded && floatNormalized > 0f)
            {
                float hitDelay = step.hitDelay > 0f
                    ? step.hitDelay
                    : Mathf.Max(
                        0f,
                        request.Settings.DelayBeforeAttack - request.Settings.AnimationLeadTime);
                float totalDuration = request.Settings.AnimationLeadTime +
                                      hitDelay +
                                      request.Settings.AttackOpenWindow;
                execution.StartOwned(motion.HoldAirAttackFloat(
                    request.ExecutionId,
                    floatNormalized * totalDuration));
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

            bool isPiercing = step.piercing;
            float radius = step.hitRadius + request.FallbackRadius;
            Vector2 knockback = step.overrideKnockback
                ? step.knockback
                : request.WeaponData.knockbackForce;
            if (step.reactionKind == HitReactionKind.Launch)
                knockback *= request.Settings.FinisherForceMultiplier;
            bool hasHitEnvironment = false;
            float windowEnd = Time.time + request.Settings.AttackOpenWindow;

            while (execution.IsActive && Time.time < windowEnd)
            {
                // Follow the player: lunges and air attacks move during the window.
                Vector3 hitOrigin = ResolveHitOrigin(request);
                WeaponHitDetectionResult hitResult = hitResolver.Detect(
                    hitOrigin,
                    radius,
                    isPiercing,
                    playerTransform.position);

                if (hitResult.Type == AttackHitResult.Enemy)
                {
                    bool damageApplied = isPiercing && hitResult.AllTargets != null
                        ? DealDamageToAll(request, hitResult.AllTargets, hitResult.AllTargetCount, step.damageMultiplier, knockback, step.reactionKind)
                        : hitResult.Target != null &&
                          DealDamage(request, hitResult.Target, step.damageMultiplier, knockback, step.reactionKind).WasApplied;

                    if (damageApplied)
                    {
                        playHitFeedback?.Invoke();
                        execution.StartOwned(motion.ApplyHitstop(
                            request.ExecutionId,
                            step.reactionKind != HitReactionKind.Flinch
                                ? request.Settings.LaunchHitstopDuration
                                : request.Settings.EnemyHitHitstopDuration));
                        motion.ApplyImmediateRecoil(
                            request.ExecutionId,
                            request.Direction,
                            request.Facing,
                            step.hitRecoil);
                    }

                    yield break;
                }

                // The overlap also touches the floor under the player whenever the sphere
                // reaches below the feet; only geometry on the swing line counts as struck.
                if (hitResult.Type == AttackHitResult.Environment && !hasHitEnvironment &&
                    TryResolveEnvironmentImpact(request, hitOrigin, radius, out Vector3 impactPoint))
                {
                    hasHitEnvironment = true;
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
            Vector2 knockback,
            HitReactionKind reactionKind)
        {
            float damage = CalculateDamage(request.WeaponData, damageMultiplier);
            DamageResult result = ResolveDamage(target, damage, knockback, reactionKind, null, SwingDirection(request));
            if (!result.WasApplied)
                return result;

            SpawnEnemyHitVfx(request, target);
            return result;
        }

        private bool DealDamageToAll(
            WeaponAttackExecutionRequest request,
            Collider[] targets,
            int targetCount,
            float damageMultiplier,
            Vector2 knockback,
            HitReactionKind reactionKind)
        {
            bool anyHit = false;
            float damage = CalculateDamage(request.WeaponData, damageMultiplier);
            processedReceivers.Clear();

            for (int i = 0; i < targetCount; i++)
            {
                Collider target = targets[i];
                if (target == null)
                    continue;
                DamageResult result = ResolveDamage(
                    target,
                    damage,
                    knockback,
                    reactionKind,
                    processedReceivers,
                    SwingDirection(request));
                if (!result.WasApplied)
                    continue;

                anyHit = true;
                SpawnEnemyHitVfx(request, target);
            }

            return anyHit;
        }

        /// <summary>Melee pushes along the swing (facing), not away from the player's position.</summary>
        private static Vector3 SwingDirection(WeaponAttackExecutionRequest request)
        {
            return request.FacingAxis * request.Facing;
        }

        private DamageResult ResolveDamage(
            Collider target,
            float damage,
            Vector2 knockback,
            HitReactionKind reactionKind,
            HashSet<int> processedReceivers,
            Vector3 pushDirection = default)
        {
            WeaponDamageResolution resolution = damageResolver.Resolve(
                target,
                damage,
                knockback,
                reactionKind,
                processedReceivers,
                pushDirection);
            if (resolution.Result.WasApplied)
                enemyHitApplied?.Invoke(resolution.Enemy, resolution.Result.AppliedDamage);
            return resolution.Result;
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

        /// <summary>
        /// Casts along the swing (side: facing, up/down: vertical) through the hit sphere. Succeeds
        /// only when environment lies on that line, e.g. a wall ahead or the floor under a down
        /// attack, and returns the presentation point on its surface.
        /// </summary>
        private bool TryResolveEnvironmentImpact(
            WeaponAttackExecutionRequest request,
            Vector3 origin,
            float radius,
            out Vector3 point)
        {
            Vector3 attackDirection = GetAttackDirection(request);
            Vector3 rayStart = origin - attackDirection * (radius + 0.25f);
            float rayLength = (radius + 0.25f) + (radius + 0.5f);

            if (!Physics.Raycast(
                    rayStart,
                    attackDirection,
                    out RaycastHit hit,
                    rayLength,
                    environmentLayer,
                    QueryTriggerInteraction.Ignore))
            {
                point = default;
                return false;
            }

            point = hit.point + hit.normal * 0.06f;
            Camera camera = Camera.main;
            if (camera != null)
                point += -camera.transform.forward * 0.1f;
            return true;
        }

        private static float CalculateDamage(WeaponData data, float damageMultiplier)
        {
            float damage = data != null ? data.baseDamage : 10f;
            return damageMultiplier > 0f ? damage * damageMultiplier : damage;
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

            SpawnEnemyHitVfx(CombatEffectsManager.BodyHitPoint(target), GetAttackDirection(request));
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

                motion.EndExecution(executionId);
            }
        }
    }
}
