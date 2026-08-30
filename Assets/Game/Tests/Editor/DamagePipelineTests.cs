using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace junklite.Tests
{
    public sealed class DamagePipelineTests
    {
        private GameObject target;
        private GameObject source;
        private CharacterStats stats;
        private AttributeManager attributes;
        private Damageable damageable;

        [SetUp]
        public void SetUp()
        {
            target = new GameObject("Damage Test Target");
            attributes = target.AddComponent<AttributeManager>();
            damageable = target.AddComponent<Damageable>();
            SetTeam(target.AddComponent<TeamMember>(), Team.Enemy);

            source = new GameObject("Damage Test Source");
            SetTeam(source.AddComponent<TeamMember>(), Team.Player);

            stats = ScriptableObject.CreateInstance<CharacterStats>();
            stats.armor = 5f;
            stats.attributes.Add(new Attribute
            {
                name = "Health",
                type = AttributeType.Health,
                maxValue = 100f,
                startingValue = 100f
            });

            attributes.Initialize(stats);
            damageable.Bind(stats, attributes, null);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(stats);
        }

        [Test]
        public void AppliedDamageReportsRequestedAndActualValues()
        {
            DamageResult emittedResult = default;
            bool eventRaised = false;
            damageable.OnDamageResolved += (result, request) =>
            {
                emittedResult = result;
                eventRaised = true;
            };

            DamageResult result = damageable.ReceiveDamage(new DamageRequest(20f, source));

            Assert.That(result.Outcome, Is.EqualTo(DamageOutcome.Applied));
            Assert.That(result.RequestedDamage, Is.EqualTo(20f));
            Assert.That(result.AppliedDamage, Is.EqualTo(15f));
            Assert.That(attributes.Health.Current, Is.EqualTo(85f));
            Assert.That(eventRaised, Is.True);
            Assert.That(emittedResult.AppliedDamage, Is.EqualTo(15f));
        }

        [Test]
        public void InvalidSelfFriendlyAndDeadRequestsReturnDistinctOutcomes()
        {
            Assert.That(
                damageable.ReceiveDamage(new DamageRequest(0f, source)).Outcome,
                Is.EqualTo(DamageOutcome.Invalid));
            Assert.That(
                damageable.ReceiveDamage(new DamageRequest(10f, target)).Outcome,
                Is.EqualTo(DamageOutcome.Invalid));

            SetTeam(source.GetComponent<TeamMember>(), Team.Enemy);
            Assert.That(
                damageable.ReceiveDamage(new DamageRequest(10f, source)).Outcome,
                Is.EqualTo(DamageOutcome.FriendlyFire));

            SetTeam(source.GetComponent<TeamMember>(), Team.Player);
            damageable.ReceiveDamage(DamageRequest.Forced(100f, source));
            Assert.That(
                damageable.ReceiveDamage(new DamageRequest(10f, source)).Outcome,
                Is.EqualTo(DamageOutcome.Dead));
        }

        [Test]
        public void DefensiveStateReturnsInvulnerableWithoutChangingHealth()
        {
            var state = target.AddComponent<PlayerState>();
            state.SetVulnerable(false);
            damageable.Bind(stats, attributes, state);

            DamageResult result = damageable.ReceiveDamage(new DamageRequest(20f, source));

            Assert.That(result.Outcome, Is.EqualTo(DamageOutcome.Invulnerable));
            Assert.That(result.AppliedDamage, Is.Zero);
            Assert.That(attributes.Health.Current, Is.EqualTo(100f));
        }

        [Test]
        public void PlayerInputLocksReleaseOnlyAfterEveryOwnerReleases()
        {
            var state = target.AddComponent<PlayerState>();
            var firstLock = state.AcquireInputLock();
            var secondLock = state.AcquireInputLock();

            Assert.That(state.IsInputLocked, Is.True);

            firstLock.Dispose();
            Assert.That(state.IsInputLocked, Is.True);

            secondLock.Dispose();
            Assert.That(state.IsInputLocked, Is.False);
        }

        [Test]
        public void PlayerDamageImmunityLocksComposeAcrossAbilityOwners()
        {
            var state = target.AddComponent<PlayerState>();
            damageable.Bind(stats, attributes, state);

            var firstLock = state.AcquireDamageImmunity();
            var secondLock = state.AcquireDamageImmunity();

            Assert.That(
                damageable.ReceiveDamage(new DamageRequest(20f, source)).Outcome,
                Is.EqualTo(DamageOutcome.Invulnerable));

            firstLock.Dispose();
            Assert.That(
                damageable.ReceiveDamage(new DamageRequest(20f, source)).Outcome,
                Is.EqualTo(DamageOutcome.Invulnerable));

            secondLock.Dispose();
            Assert.That(
                damageable.ReceiveDamage(new DamageRequest(20f, source)).Outcome,
                Is.EqualTo(DamageOutcome.Applied));
        }

        [Test]
        public void DeathFiresOncePerLifeAndReviveResetsTheGuard()
        {
            int deathCount = 0;
            attributes.OnDeath += () => deathCount++;

            DamageResult firstDeath = damageable.ReceiveDamage(DamageRequest.Forced(200f, source));
            DamageResult deadTarget = damageable.ReceiveDamage(new DamageRequest(10f, source));

            Assert.That(firstDeath.AppliedDamage, Is.EqualTo(100f));
            Assert.That(deadTarget.Outcome, Is.EqualTo(DamageOutcome.Dead));
            Assert.That(deathCount, Is.EqualTo(1));

            attributes.RestoreHealthToMax();
            DamageResult secondDeath = damageable.ReceiveDamage(DamageRequest.Forced(200f, source));

            Assert.That(secondDeath.WasApplied, Is.True);
            Assert.That(deathCount, Is.EqualTo(2));
        }

        [Test]
        public void ReinitializingWithTheSameStatsDoesNotResetRuntimeHealth()
        {
            damageable.ReceiveDamage(new DamageRequest(20f, source));

            attributes.Initialize(stats);

            Assert.That(attributes.Health.Current, Is.EqualTo(85f));
        }

        private static void SetTeam(TeamMember member, Team team)
        {
            var serializedMember = new SerializedObject(member);
            serializedMember.FindProperty("team").enumValueIndex = (int)team;
            serializedMember.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
