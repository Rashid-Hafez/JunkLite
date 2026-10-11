using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace junklite.Tests
{
    public sealed class BruteBossTests
    {
        private const string BrutePrefab = "Assets/Game/ENEMIES/Brute/Brute Boss.prefab";

        [Test]
        public void DamageRequestsAreParryableUnlessMarked()
        {
            var normal = new DamageRequest(10f);
            var forced = DamageRequest.Forced(10f);
            var grab = new DamageRequest(10f).AsUnparryable();

            Assert.That(normal.Unparryable, Is.False);
            Assert.That(forced.Unparryable, Is.False);
            Assert.That(grab.Unparryable, Is.True);
            Assert.That(grab.BypassesDefenses, Is.False, "Unparryable hits must still respect i-frames and shields.");
        }

        [Test]
        public void GrabInfoDefaultsToTimedGrab()
        {
            var info = new GrabInfo(null, 1f, Vector3.zero, Vector2.one, 5f, 1);

            Assert.That(info.HoldUntilReleased, Is.False, "Robot grabs keep their timer-driven throw.");
            Assert.That(info.Anchor, Is.Null);
        }

        [Test]
        public void MoveWeightRespectsCooldownAndZeroWeight()
        {
            var move = new BruteMoveWeight { weight = 1f, cooldown = 100f };
            Assert.That(move.IsReady, Is.True);

            move.StartCooldown(1f);
            Assert.That(move.IsReady, Is.False);

            var disabled = new BruteMoveWeight { weight = 0f, cooldown = 0f };
            Assert.That(disabled.IsReady, Is.False);
        }

        [Test]
        public void BrutePrefabIsComposedAndWired()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BrutePrefab);
            Assert.That(prefab, Is.Not.Null, BrutePrefab);

            Assert.That(prefab.GetComponent<BruteEnemy>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<BruteBrain>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<BruteAnimationPresenter>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<EnemyBrain>(), Is.InstanceOf<BruteBrain>(), "Only one brain allowed.");

            var brain = new SerializedObject(prefab.GetComponent<BruteBrain>());
            foreach (string reference in new[] { "melee.hitbox", "dash.dashHitbox", "grab.grabHitbox", "grab.grabAnchor" })
            {
                SerializedProperty property = brain.FindProperty(reference);
                Assert.That(property?.objectReferenceValue, Is.Not.Null, $"BruteBrain.{reference} is not assigned.");
            }

            Assert.That(brain.FindProperty("stun.staggerDuration").floatValue, Is.EqualTo(0f),
                "Ordinary hits must not hitstun the boss.");
            Assert.That(brain.FindProperty("dash.canBeInterrupted").boolValue, Is.False);
        }
    }
}
