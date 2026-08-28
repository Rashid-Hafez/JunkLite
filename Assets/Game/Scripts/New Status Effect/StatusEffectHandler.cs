using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Actor-neutral runtime controller for buffs and debuffs. It owns timing,
    /// stacking and ticks, then publishes one aggregate snapshot to the actor.
    /// </summary>
    public class StatusEffectHandler : MonoBehaviour
    {
        [Header("Legacy VFX References (Disabled by Default)")]
        [Tooltip("Kept so existing enemy prefabs retain their current presentation. New presentation should subscribe to the status events.")]
        [SerializeField] private GameObject burnVFX;
        [SerializeField] private GameObject poisonVFX;
        [SerializeField] private GameObject bleedVFX;
        [SerializeField] private GameObject electricVFX;

        [Header("Debug")]
        [SerializeField] private bool showDebugLogs;

        private readonly List<StatusEffectInstance> activeEffects = new();
        private readonly List<StatusEffectInstance> tickBuffer = new();
        private readonly List<StatusEffectInstance> removalBuffer = new();

        private IStatusEffectTarget target;
        private IDamageReceiver damageReceiver;
        private StatusEffectSnapshot snapshot = StatusEffectSnapshot.Clear;

        public event Action<StatusEffectType> OnEffectApplied;
        public event Action<StatusEffectType> OnEffectRemoved;
        public event Action<StatusEffectType> OnEffectRefreshed;
        public event Action<StatusEffectType, float> OnEffectTick;
        public event Action<StatusEffectSnapshot> OnSnapshotChanged;

        public int ActiveEffectCount => activeEffects.Count;
        public StatusEffectSnapshot CurrentSnapshot => snapshot;
        public bool IsCrowdControlled => snapshot.IsCrowdControlled;
        public float MoveSpeedMultiplier => snapshot.MoveSpeedMultiplier;

        private void Awake()
        {
            CacheTarget();
        }

        public void BindTarget(IStatusEffectTarget statusTarget)
        {
            target = statusTarget;
            damageReceiver = statusTarget;
            PublishSnapshot();
        }

        public bool HasEffect(StatusEffectType type)
        {
            for (int i = 0; i < activeEffects.Count; i++)
            {
                if (activeEffects[i].Type == type)
                    return true;
            }

            return false;
        }

        public bool HasTag(StatusEffectTags tag)
        {
            return (snapshot.Tags & tag) != 0;
        }

        public StatusEffectInstance Apply(StatusEffectApplication application)
        {
            if (!application.IsValid)
                return null;

            CacheTarget();
            if (damageReceiver != null && !damageReceiver.IsAlive)
                return null;

            float now = Time.time;
            StatusEffectInstance existing = FindMatchingEffect(application);
            if (existing != null)
            {
                bool aggregatesChanged = existing.Merge(application, now);
                if (aggregatesChanged)
                    RecalculateSnapshot();

                OnEffectRefreshed?.Invoke(existing.Type);

                if (showDebugLogs)
                    Debug.Log($"[StatusEffect] Refreshed {existing.Type} on {name}", this);

                return existing;
            }

            var effect = new StatusEffectInstance(application, now);
            activeEffects.Add(effect);
            SetVFXActive(effect.Type, true);
            RecalculateSnapshot();
            OnEffectApplied?.Invoke(effect.Type);

            if (showDebugLogs)
                Debug.Log($"[StatusEffect] Applied {effect.Type} to {name} ({application.Duration:0.##}s)", this);

            return effect;
        }

        // Backward-compatible entry point for older code-created effects.
        public StatusEffectInstance Apply(StatusEffectInstance effect)
        {
            return effect != null ? Apply(effect.ToApplication()) : null;
        }

        public StatusEffectInstance ApplyHitstun(float duration, GameObject source = null)
        {
            return Apply(StatusEffectApplication.Hitstun(duration, source));
        }

        public StatusEffectInstance ApplyStun(float duration, GameObject source = null)
        {
            return Apply(StatusEffectApplication.Stun(duration, source));
        }

        public StatusEffectInstance ApplySlow(float duration, float moveSpeedMultiplier, GameObject source = null)
        {
            return Apply(StatusEffectApplication.Slow(duration, moveSpeedMultiplier, source));
        }

        public StatusEffectInstance ApplyFreeze(float duration, GameObject source = null)
        {
            return Apply(StatusEffectApplication.Freeze(duration, source));
        }

        public void Remove(StatusEffectType type)
        {
            removalBuffer.Clear();
            for (int i = 0; i < activeEffects.Count; i++)
            {
                if (activeEffects[i].Type == type)
                    removalBuffer.Add(activeEffects[i]);
            }

            RemoveBufferedEffects();
        }

        public void Remove(StatusEffectDefinition definition)
        {
            if (definition == null)
                return;

            removalBuffer.Clear();
            for (int i = 0; i < activeEffects.Count; i++)
            {
                if (activeEffects[i].Definition == definition)
                    removalBuffer.Add(activeEffects[i]);
            }

            RemoveBufferedEffects();
        }

        public void ClearAllEffects()
        {
            if (activeEffects.Count == 0)
            {
                if (snapshot.Tags != StatusEffectTags.None ||
                    snapshot.BlockedActions != StatusActionBlock.None ||
                    !Mathf.Approximately(snapshot.MoveSpeedMultiplier, 1f))
                {
                    snapshot = StatusEffectSnapshot.Clear;
                    PublishSnapshot();
                }
                return;
            }

            removalBuffer.Clear();
            removalBuffer.AddRange(activeEffects);
            activeEffects.Clear();

            for (int i = 0; i < removalBuffer.Count; i++)
            {
                StatusEffectType type = removalBuffer[i].Type;
                SetVFXActive(type, false);
                OnEffectRemoved?.Invoke(type);
            }

            removalBuffer.Clear();
            RecalculateSnapshot();
        }

        public float GetRemainingDuration(StatusEffectType type)
        {
            float longest = 0f;
            for (int i = 0; i < activeEffects.Count; i++)
            {
                if (activeEffects[i].Type == type)
                    longest = Mathf.Max(longest, activeEffects[i].RemainingDuration);
            }

            return longest;
        }

        private void Update()
        {
            CacheTarget();
            if (damageReceiver != null && !damageReceiver.IsAlive)
            {
                ClearAllEffects();
                return;
            }

            TickEffects(Time.time);
        }

        private void TickEffects(float now)
        {
            if (activeEffects.Count == 0)
                return;

            tickBuffer.Clear();
            tickBuffer.AddRange(activeEffects);

            for (int i = 0; i < tickBuffer.Count; i++)
            {
                StatusEffectInstance effect = tickBuffer[i];
                if (!activeEffects.Contains(effect))
                    continue;

                int catchUpTicks = 0;
                while (effect.CanTick && now >= effect.NextTickAt && effect.NextTickAt <= effect.ExpiresAt && catchUpTicks < 8)
                {
                    ApplyTickDamage(effect);
                    effect.AdvanceTick();
                    catchUpTicks++;

                    if (damageReceiver != null && !damageReceiver.IsAlive)
                        break;
                }

                if (damageReceiver != null && !damageReceiver.IsAlive)
                    break;

                if (effect.IsExpired)
                    RemoveInstance(effect, recalculate: false);
            }

            tickBuffer.Clear();

            if (damageReceiver != null && !damageReceiver.IsAlive)
                ClearAllEffects();
            else
                RecalculateSnapshot();
        }

        private void ApplyTickDamage(StatusEffectInstance effect)
        {
            if (damageReceiver == null || !damageReceiver.IsAlive || effect.DamagePerTick <= 0f)
                return;

            DamageResult result = damageReceiver.ReceiveDamage(new DamageRequest(
                effect.DamagePerTick,
                effect.Source,
                effect.DamageType,
                isTickDamage: true));

            if (!result.WasApplied)
                return;

            OnEffectTick?.Invoke(effect.Type, result.AppliedDamage);

            if (showDebugLogs)
                Debug.Log($"[StatusEffect] {effect.Type} tick: {result.AppliedDamage} damage to {name}", this);
        }

        private StatusEffectInstance FindMatchingEffect(StatusEffectApplication application)
        {
            for (int i = 0; i < activeEffects.Count; i++)
            {
                if (activeEffects[i].Matches(application))
                    return activeEffects[i];
            }

            return null;
        }

        private void RemoveBufferedEffects()
        {
            if (removalBuffer.Count == 0)
                return;

            for (int i = 0; i < removalBuffer.Count; i++)
                RemoveInstance(removalBuffer[i], recalculate: false);

            removalBuffer.Clear();
            RecalculateSnapshot();
        }

        private void RemoveInstance(StatusEffectInstance effect, bool recalculate)
        {
            if (effect == null || !activeEffects.Remove(effect))
                return;

            if (!HasEffect(effect.Type))
                SetVFXActive(effect.Type, false);

            OnEffectRemoved?.Invoke(effect.Type);

            if (showDebugLogs)
                Debug.Log($"[StatusEffect] Removed {effect.Type} from {name}", this);

            if (recalculate)
                RecalculateSnapshot();
        }

        private void RecalculateSnapshot()
        {
            StatusEffectTags tags = StatusEffectTags.None;
            StatusActionBlock blockedActions = StatusActionBlock.None;
            float strongestSlow = 1f;
            float strongestHaste = 1f;

            for (int i = 0; i < activeEffects.Count; i++)
            {
                StatusEffectInstance effect = activeEffects[i];
                tags |= effect.Tags;
                blockedActions |= effect.BlockedActions;

                float multiplier = effect.MoveSpeedMultiplier;
                if (multiplier < 1f)
                    strongestSlow = Mathf.Min(strongestSlow, multiplier);
                else if (multiplier > 1f)
                    strongestHaste = Mathf.Max(strongestHaste, multiplier);
            }

            float moveSpeedMultiplier = strongestSlow < 1f ? strongestSlow : strongestHaste;
            var next = new StatusEffectSnapshot(tags, blockedActions, moveSpeedMultiplier);

            bool changed = next.Tags != snapshot.Tags ||
                           next.BlockedActions != snapshot.BlockedActions ||
                           !Mathf.Approximately(next.MoveSpeedMultiplier, snapshot.MoveSpeedMultiplier);

            snapshot = next;
            if (changed)
                PublishSnapshot();
        }

        private void PublishSnapshot()
        {
            target?.ApplyStatusEffectSnapshot(snapshot);
            OnSnapshotChanged?.Invoke(snapshot);
        }

        private void CacheTarget()
        {
            if (target == null)
                target = GetComponent<IStatusEffectTarget>();
            if (damageReceiver == null)
                damageReceiver = target ?? GetComponent<IDamageReceiver>();
        }

        private void SetVFXActive(StatusEffectType type, bool active)
        {
            GameObject vfx = GetVFXForType(type);
            if (vfx != null)
                vfx.SetActive(active);
        }

        private GameObject GetVFXForType(StatusEffectType type)
        {
            return type switch
            {
                StatusEffectType.Burn => burnVFX,
                StatusEffectType.Poison => poisonVFX,
                StatusEffectType.Bleed => bleedVFX,
                StatusEffectType.Electric => electricVFX,
                _ => null
            };
        }

        private void OnDisable()
        {
            ClearAllEffects();
        }

        private void OnDestroy() => ClearAllEffects();

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Auto-disable VFX references in editor
            if (burnVFX != null && burnVFX.activeSelf) burnVFX.SetActive(false);
            if (poisonVFX != null && poisonVFX.activeSelf) poisonVFX.SetActive(false);
            if (bleedVFX != null && bleedVFX.activeSelf) bleedVFX.SetActive(false);
            if (electricVFX != null && electricVFX.activeSelf) electricVFX.SetActive(false);
        }
#endif
    }
}
