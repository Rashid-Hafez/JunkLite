using UnityEngine;

namespace junklite
{
    public interface IDamageable
    {
        void TakeDamage(DamageInfo info);
        bool IsAlive { get; }
    }

    public enum DamageType { Physical, Fire, Magic }

    public struct DamageInfo
    {
        public float Amount;
        public GameObject Source;
        public DamageType Type;

        public DamageInfo(float amount, GameObject source = null, DamageType type = DamageType.Physical)
        {
            Amount = amount; Source = source; Type = type;
        }
    }

    /// <summary>
    /// Single entry point for damage. Computes final damage using Stats,
    /// subtracts from Health via AttributeManager, and can trigger stun via CharacterState.
    /// </summary>
    public sealed class Damageable : MonoBehaviour
    {
        CharacterStats stats;
        AttributeManager attributes;
        CharacterState state;
        TeamMember myTeam;

        public event System.Action<float, GameObject> OnDamaged;

        public void Bind(CharacterStats s, AttributeManager a, CharacterState st)
        {
            stats = s; attributes = a; state = st;
            if (myTeam == null) myTeam = GetComponent<TeamMember>();
        }

        public void TakeDamage(DamageInfo info)
        {
            // 1) must be alive
            if (attributes == null || attributes.Health == null || !attributes.IsAlive) return;

            // 2) no self-hits
            if (info.Source == gameObject) return;

            // 3) team check
            if (!IsHostile(info.Source)) return;

            // 4) compute final damage
            float armor = stats != null ? stats.armor : 0f;
            float finalDamage = Mathf.Max(1f, info.Amount - armor);

            // 5) apply
            attributes.Health.Remove(finalDamage);
            OnDamaged?.Invoke(finalDamage, info.Source);

            // 6) optional brief hit-stun
            state?.ApplyStun(0.1f);
        }

        bool IsHostile(GameObject source)
        {
            // If either has no team, allow by default (or return false if you prefer strict).
            var srcTeam = source ? source.GetComponentInParent<TeamMember>() : null;
            if (myTeam == null || srcTeam == null) return true;

            // Disallow same-team damage; allow Player ↔ Enemy
            return myTeam.Team != srcTeam.Team;
        }
    }
}
