using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace junklite.Tests
{
    /// <summary>
    /// A basic swing hits one enemy: the one nearest the player. Piercing (every enemy in the sphere)
    /// is an opt-in per combo step for future collateral attacks.
    /// </summary>
    public sealed class SingleTargetSwordTests
    {
        private readonly List<Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in owned) if (o != null) Object.DestroyImmediate(o);
            owned.Clear();
        }

        private Collider MakeEnemyBody(string name, Vector3 position, int layer)
        {
            var go = new GameObject(name);
            owned.Add(go);
            go.layer = layer;
            go.transform.position = position;
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.radius = 0.4f;
            capsule.height = 2f;
            return capsule;
        }

        [Test]
        public void ANonPiercingSwingHitsTheEnemyInFrontNotTheOneBehindIt()
        {
            int enemyLayer = LayerMask.NameToLayer("Enemies");
            Assert.That(enemyLayer, Is.GreaterThanOrEqualTo(0));
            Vector3 player = new(7000f, 7000f, 7000f);
            // Two grunts in a line ahead of the player; the swing sphere sits between them and reaches both,
            // and its centre is nearer the back grunt's centre.
            Collider front = MakeEnemyBody("Front Grunt", player + new Vector3(1.0f, 0f, 0f), enemyLayer);
            Collider back = MakeEnemyBody("Back Grunt", player + new Vector3(2.1f, 0f, 0f), enemyLayer);
            Physics.SyncTransforms();

            Type resolverType = typeof(WeaponManager).Assembly.GetType("junklite.WeaponHitResolver");
            object resolver = Activator.CreateInstance(resolverType, (LayerMask)(1 << enemyLayer), (LayerMask)0);
            MethodInfo detect = resolverType.GetMethod("Detect");
            Vector3 origin = player + new Vector3(1.7f, 0f, 0f);

            object single = detect.Invoke(resolver, new object[] { origin, 1f, false, (Vector3?)player });
            Collider target = (Collider)single.GetType().GetProperty("Target").GetValue(single);
            Assert.That(target, Is.SameAs(front), "the front grunt takes the hit, the one it blocks does not");

            object pierce = detect.Invoke(resolver, new object[] { origin, 1f, true, (Vector3?)player });
            int count = (int)pierce.GetType().GetProperty("AllTargetCount").GetValue(pierce);
            Assert.That(count, Is.EqualTo(2), "piercing still reaches both, for attacks that opt in");
            Assert.That(back, Is.Not.Null);
        }

        [Test]
        public void TheBasicSwordAndFistsNeverPierce()
        {
            foreach (string path in new[] { "Assets/Game/Scripts/Weapons/Sword.asset", "Assets/Game/Scripts/Weapons/Fist.asset" })
            {
                var weapon = AssetDatabase.LoadAssetAtPath<MeleeWeaponData>(path);
                Assert.That(weapon, Is.Not.Null, path);
                foreach (MeleeWeaponData.MeleeComboStep step in weapon.sideCombo)
                    Assert.That(step.piercing, Is.False, $"{path} {step.animationName}");
                foreach (MeleeWeaponData.MeleeComboStep step in weapon.airSideCombo ?? Array.Empty<MeleeWeaponData.MeleeComboStep>())
                    Assert.That(step.piercing, Is.False, $"{path} {step.animationName}");
                Assert.That(weapon.upAttack.piercing, Is.False, $"{path} up attack");
                Assert.That(weapon.downAttack.piercing, Is.False, $"{path} down attack");
            }
        }
    }
}
