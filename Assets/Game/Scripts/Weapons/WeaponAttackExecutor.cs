using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Attack clips can carry Spine 'Hold' and 'Release' events: the clip pauses at Hold, then runs
    /// faster from Release to its end. Combat timing is authored against the plain clip, so this says
    /// when that moment really arrives once the clip is warped. Pure maths, no Spine or Unity state.
    /// </summary>
    public static class AttackHoldTiming
    {
        /// <param name="authoredDelay">Real seconds to that moment on the plain clip (hitDelay, or clip length / baseScale).</param>
        /// <param name="baseScale">The swing's normal track entry time scale.</param>
        /// <param name="holdAt">Clip seconds of the Hold event, negative when the clip has none.</param>
        /// <param name="releaseAt">Clip seconds of the Release event, negative when the clip has none.</param>
        public static float WarpDelay(float authoredDelay, float baseScale, float holdAt, float releaseAt,
            float holdDuration, float releaseSpeed)
        {
            float clipTime = authoredDelay * baseScale;
            float delay = authoredDelay;
            if (holdAt >= 0f && holdAt <= clipTime)
                delay += holdDuration;
            if (releaseAt >= 0f && releaseAt < clipTime && baseScale > 0f && releaseSpeed > 0f)
                delay -= (clipTime - releaseAt) / baseScale * (1f - 1f / releaseSpeed);
            return delay;
        }
    }

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
        /// <summary>Metres the side lunge should travel (combat focus). Negative = use the authored lunge.</summary>
        public float LungeDistance { get; }
        /// <summary>Top speed when the lunge must go further than authored (gap close).</summary>
        public float GapCloseMaxSpeed { get; }
        /// <summary>Focused enemy's collider: preferred by the swing when it overlaps several.</summary>
        public Collider FocusCollider { get; }
        /// <summary>Polled during the lunge; true ends it (reached the focus, or the focus is gone).</summary>
        public Func<bool> StopLunge { get; }
        /// <summary>Real seconds from the clip start until its wind-up pause ends; the lunge waits for it. 0 = none.</summary>
        public float WindupRelease { get; }
        /// <summary>Real seconds the swing clip runs (with its hold and release). 0 = unknown.</summary>
        public float ClipDuration { get; }

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
            WeaponAttackExecutionSettings settings,
            float lungeDistance = -1f,
            float gapCloseMaxSpeed = 0f,
            Collider focusCollider = null,
            Func<bool> stopLunge = null,
            float windupRelease = 0f,
            float clipDuration = 0f)
        {
            WindupRelease = windupRelease;
            ClipDuration = clipDuration;
            LungeDistance = lungeDistance;
            GapCloseMaxSpeed = gapCloseMaxSpeed;
            FocusCollider = focusCollider;
            StopLunge = stopLunge;
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
        private readonly Func<string, float, float> warpHitDelay;
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
            Action playHitFeedback,
            Func<string, float, float> warpHitDelay = null)
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
            this.warpHitDelay = warpHitDelay;
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

        private const float LungeEndMargin = 0.02f;
        private const float MinLungeTime = 0.04f;

        private IEnumerator RunMelee(
            Execution execution,
            WeaponAttackExecutionRequest request,
            MeleeWeaponData.MeleeComboStep step)
        {
            float forwardPeak = step.forwardImpulse;
            float forwardDuration = step.forwardImpulseDuration;

            // A step with a wind-up: planted through the pause, the lunge fires on the release.
            float pushDelay = request.WindupRelease > 0f ? request.Settings.AnimationLeadTime + request.WindupRelease : 0f;

            if (request.LungeDistance >= 0f)
            {
                // Combat focus: travel exactly the chosen distance (0 = stand still). A gap close may
                // run longer than authored, but must arrive while the hit window is still open.
                float arriveBy = request.Settings.AnimationLeadTime + ResolveHitDelay(request, step) +
                                 request.Settings.AttackOpenWindow * 0.5f - pushDelay;
                LungeWarp.Solve(step.forwardImpulse, step.forwardImpulseDuration, step.lungeCurve,
                    request.LungeDistance, request.GapCloseMaxSpeed, arriveBy, out forwardPeak, out forwardDuration);
            }

            // The swing ending cancels the lunge: a lunge that would outlast it runs faster
            // over the time left, so the whole distance still lands (wind-ups start it late).
            if (request.ClipDuration > 0f)
            {
                float timeLeft = request.Settings.AnimationLeadTime + request.ClipDuration - pushDelay - LungeEndMargin;
                if (timeLeft > MinLungeTime && forwardDuration > timeLeft)
                {
                    forwardPeak *= forwardDuration / timeLeft;
                    forwardDuration = timeLeft;
                }
            }

            execution.StartOwned(motion.ApplyPush(
                request.ExecutionId,
                request.Direction,
                request.Facing,
                forwardPeak,
                step.verticalImpulse,
                forwardDuration,
                step.lungeCurve,
                request.StopLunge,
                ownHorizontalAtZero: request.LungeDistance >= 0f,
                startDelay: pushDelay));

            // Air attacks stall the fall for part of the swing, then gravity resumes.
            float floatNormalized = request.Direction == AttackDirection.Down
                ? request.Settings.DownAttackFloatNormalized
                : request.Settings.AirAttackFloatNormalized;
            if (!request.WasGrounded && floatNormalized > 0f)
            {
                float totalDuration = request.Settings.AnimationLeadTime +
                                      ResolveHitDelay(request, step) +
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

            float resolvedHitDelay = ResolveHitDelay(request, step);
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
                    playerTransform.position,
                    request.FocusCollider);

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
                        // A focused slide follows the knockback: no recoil to cut it short.
                        bool followsTarget = request.FocusCollider != null && step.focusedSlide > 0f;
                        if (!followsTarget)
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

        // Authored against the plain clip; the warp moves it with the clip's Hold and Release so the hit stays on the visual hit.
        private float ResolveHitDelay(WeaponAttackExecutionRequest request, MeleeWeaponData.MeleeComboStep step)
        {
            float authored = step.hitDelay > 0f
                ? step.hitDelay
                : Mathf.Max(0f, request.Settings.DelayBeforeAttack - request.Settings.AnimationLeadTime);
            return warpHitDelay != null ? warpHitDelay(request.AnimationName, authored) : authored;
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
                    anchorPosition.z),
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
