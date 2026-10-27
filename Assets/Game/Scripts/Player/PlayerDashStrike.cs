using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Dash Strike upgrade: while the player dashes, enemies swept by the body take one hit each.
    /// Damage goes through the one damage path (<see cref="DamageRequest"/> via <see cref="WeaponDamageResolver"/>)
    /// and applied hits are reported to <see cref="WeaponManager"/> so mods charge like weapon hits.
    /// <see cref="PlayerCharacter"/> enables it when the ability is owned. Added at runtime when the prefab
    /// lacks it; add it to the player prefab to tune the values in the Inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerDashStrike : MonoBehaviour
    {
        [Tooltip("Damage per enemy = this × the current weapon's base damage. Progression damage scaling applies on top.")]
        [SerializeField, Min(0f)] private float damageMultiplier = 0.75f;
        [SerializeField] private Vector2 knockback = new(6f, 3f);
        [Tooltip("Radius of the capsule swept between the previous and current position each physics step.")]
        [SerializeField, Min(0.05f)] private float sweepRadius = 0.6f;
        [Tooltip("Height of the sweep capsule's center above the player's position.")]
        [SerializeField] private float sweepHeight = 1f;

        private Character2D5Controller controller;
        private WeaponManager weapons;
        private WeaponDamageResolver damageResolver;
        private Collider[] overlapBuffer = new Collider[32];
        private readonly HashSet<int> hitThisDash = new();
        private Vector3 previousPosition;
        private bool dashing;

        /// <summary>True when the Dash Strike upgrade is owned. Nothing happens while false.</summary>
        public bool Unlocked { get; set; }
        /// <summary>Receivers hit during the current dash (for tests and debugging).</summary>
        public int HitsThisDash => hitThisDash.Count;

        private void Awake()
        {
            controller = GetComponent<Character2D5Controller>();
            weapons = GetComponent<WeaponManager>();
            damageResolver = new WeaponDamageResolver(gameObject);
        }

        private void OnEnable()
        {
            if (controller == null) return;
            controller.OnDashStarted += HandleDashStarted;
            controller.OnDashEnded += HandleDashEnded;
        }

        private void OnDisable()
        {
            if (controller != null)
            {
                controller.OnDashStarted -= HandleDashStarted;
                controller.OnDashEnded -= HandleDashEnded;
            }
            dashing = false;
        }

        private void HandleDashStarted()
        {
            hitThisDash.Clear();
            previousPosition = transform.position;
            dashing = Unlocked;
        }

        private void HandleDashEnded() => dashing = false;

        private void FixedUpdate()
        {
            if (!dashing || !Unlocked || weapons == null) return;
            Sweep(previousPosition, transform.position);
            previousPosition = transform.position;
        }

        /// <summary>Damages every new enemy between two positions. Public so tests can drive it without physics stepping.</summary>
        public int Sweep(Vector3 from, Vector3 to)
        {
            Vector3 lift = Vector3.up * sweepHeight;
            int count = Physics.OverlapCapsuleNonAlloc(from + lift, to + lift, sweepRadius, overlapBuffer,
                weapons.EnemyLayer, QueryTriggerInteraction.Ignore);
            if (count == overlapBuffer.Length)
                overlapBuffer = new Collider[overlapBuffer.Length * 2];

            WeaponData weapon = weapons.CurrentWeapon;
            float damage = (weapon != null ? weapon.baseDamage : 10f) * damageMultiplier;
            Vector3 direction = (to - from).sqrMagnitude > 0.0001f ? (to - from).normalized
                : controller != null ? controller.MovementAxis * controller.FacingSign : Vector3.right;
            int applied = 0;
            for (int i = 0; i < count; i++)
            {
                Collider target = overlapBuffer[i];
                if (target == null || target.transform.IsChildOf(transform)) continue;
                WeaponDamageResolution resolution = damageResolver.Resolve(target, damage, knockback,
                    HitReactionKind.Flinch, hitThisDash, direction);
                if (!resolution.Result.WasApplied) continue;
                applied++;
                weapons.ReportExternalHit(resolution.Enemy, resolution.Result.AppliedDamage);
                if (CombatEffectsManager.Instance != null)
                {
                    Vector3 point = target.ClosestPoint(to + lift);
                    CombatEffectsManager.Instance.SpawnEnemyHitVFX(point, direction);
                    CombatEffectsManager.Instance.SpawnEnemyHurtParticle(point, direction);
                }
            }
            return applied;
        }
    }
}
