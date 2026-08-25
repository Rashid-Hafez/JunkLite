using UnityEngine;

namespace junklite
{
    public interface IDamageable
    {
        /// <summary>
        /// Attempt to deal damage. Returns true if damage was actually dealt.
        /// </summary>
        bool TakeDamage(DamageInfo info);
        bool IsAlive { get; }
    }

    public interface IGrabbable
    {
        void GetGrabbed(GrabInfo info);
        bool CanBeGrabbed { get; }
    }

    public enum DamageType { Physical, Fire, Magic, Electric }

    /// <summary>
    /// Damage data - kept lean and focused only on damage.
    /// </summary>
    public struct DamageInfo
    {
        public float Amount;
        public UnityEngine.GameObject Source;
        public DamageType Type;
        public UnityEngine.Vector2 KnockbackForce;
        public bool IsTickDamage; // NEW: true for DOT ticks (fire, electric, etc.) — skips hitstun

        public DamageInfo(float amount, UnityEngine.GameObject source = null, DamageType type = DamageType.Physical,
            UnityEngine.Vector2 knockback = default, bool isTickDamage = false)
        {
            Amount = amount;
            Source = source;
            Type = type;
            KnockbackForce = knockback;
            IsTickDamage = isTickDamage;
        }
    }

    /// <summary>
    /// Grab data - everything needed for a grab and throw interaction.
    /// </summary>
    public struct GrabInfo
    {
        public GameObject Source;
        public float Duration;
        public Vector3 GrabOffset;
        public Vector2 ThrowForce;
        public float ThrowDamage;
        public int ThrowDirection; // 1 = right, -1 = left

        public GrabInfo(GameObject source, float duration, Vector3 grabOffset, Vector2 throwForce, float throwDamage, int throwDirection)
        {
            Source = source;
            Duration = duration;
            GrabOffset = grabOffset;
            ThrowForce = throwForce;
            ThrowDamage = throwDamage;
            ThrowDirection = throwDirection;
        }
    }

    /// <summary>
    /// Neutral damage resolver. It validates requests, applies mitigation, asks
    /// AttributeManager to mutate health, and returns the actual outcome.
    /// </summary>
    public sealed class Damageable : MonoBehaviour
    {
        CharacterStats stats;
        AttributeManager attributes;
        CharacterState state;
        TeamMember myTeam;

        public event System.Action<DamageResult, DamageRequest> OnDamageResolved;

        // Compatibility event for existing enemy presentation listeners.
        public event System.Action<float, GameObject> OnDamaged;

        public void Bind(CharacterStats s, AttributeManager a, CharacterState st)
        {
            stats = s; attributes = a; state = st;
            if (myTeam == null) myTeam = GetComponent<TeamMember>();
        }

        public bool TryValidateRequest(
            DamageRequest request,
            out DamageResult rejection,
            bool checkDefensiveState = true)
        {
            if (float.IsNaN(request.Amount) || float.IsInfinity(request.Amount) || request.Amount <= 0f)
            {
                rejection = DamageResult.Rejected(DamageOutcome.Invalid, request.Amount);
                return false;
            }

            if (attributes == null || attributes.Health == null)
            {
                rejection = DamageResult.Rejected(DamageOutcome.Invalid, request.Amount);
                return false;
            }

            if (!attributes.IsAlive)
            {
                rejection = DamageResult.Rejected(DamageOutcome.Dead, request.Amount);
                return false;
            }

            if (request.Source == gameObject)
            {
                rejection = DamageResult.Rejected(DamageOutcome.Invalid, request.Amount);
                return false;
            }

            if (!IsHostile(request.Source))
            {
                rejection = DamageResult.Rejected(DamageOutcome.FriendlyFire, request.Amount);
                return false;
            }

            if (!request.BypassesDefenses && checkDefensiveState && state != null && !state.CanTakeDamage)
            {
                rejection = DamageResult.Rejected(DamageOutcome.Invulnerable, request.Amount);
                return false;
            }

            rejection = default;
            return true;
        }

        public DamageResult ReceiveDamage(DamageRequest request)
        {
            if (!TryValidateRequest(request, out var rejection))
                return rejection;

            float armor = stats != null ? stats.armor : 0f;
            float finalDamage = request.BypassesMitigation
                ? request.Amount
                : Mathf.Max(1f, request.Amount - armor);

            float appliedDamage = attributes.ApplyDamage(finalDamage);
            if (appliedDamage <= 0f)
                return DamageResult.Rejected(DamageOutcome.Invalid, request.Amount);

            var result = DamageResult.Applied(request.Amount, appliedDamage);
            OnDamageResolved?.Invoke(result, request);
            OnDamaged?.Invoke(appliedDamage, request.Source);

            if (!request.IsTickDamage)
                state?.ApplyStun(0.1f);

            return result;
        }

        /// <summary>
        /// Legacy adapter. All old callers now route into the request/result pipeline.
        /// </summary>
        public bool TakeDamage(DamageInfo info)
        {
            return ReceiveDamage(DamageRequest.FromLegacy(info)).WasApplied;
        }

        bool IsHostile(GameObject source)
        {
            var srcTeam = source ? source.GetComponentInParent<TeamMember>() : null;
            if (myTeam == null || srcTeam == null) return true;
            return myTeam.Team != srcTeam.Team;
        }
    }
}
