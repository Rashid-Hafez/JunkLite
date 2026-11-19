using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace junklite.Tests
{
    /// <summary>
    /// Only grunts are launched; everything else takes a launching hit as a sideways push.
    /// Dev-tool spawns stand on the floor instead of dropping from the spawn height.
    /// </summary>
    public sealed class LaunchOnlyGruntsAndDevSpawnTests
    {
        private readonly List<Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in owned) if (o != null) Object.DestroyImmediate(o);
            owned.Clear();
        }

        [Test]
        public void OnlyGruntsAndHyenasCanBeLaunched()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/ENEMIES", "Assets/Game/Prefabs/Enemies" });
            int checkedEnemies = 0;
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                EnemyCharacter enemy = prefab != null ? prefab.GetComponent<EnemyCharacter>() : null;
                if (enemy == null) continue;
                checkedEnemies++;
                // Launch is a per-prefab toggle; bosses (Brute), robots and dummies stay grounded.
                bool launchable = enemy is GruntEnemy || enemy is HyenaEnemy;
                Assert.That(enemy.CanBeLaunched, Is.EqualTo(launchable), $"{path}: only grunts and hyenas are launched");
            }
            Assert.That(checkedEnemies, Is.GreaterThan(3), "the enemy prefabs were found");
        }

        [Test]
        public void ADevSpawnStandsOnTheFloorBodyAndTransformAlike()
        {
            var go = new GameObject("Dev Spawn Body");
            owned.Add(go);
            go.transform.position = new Vector3(3000f, 3004f, 3000f); // the panel's spawn height above the floor
            var body = go.AddComponent<Rigidbody>();
            body.interpolation = RigidbodyInterpolation.Interpolate;
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.height = 2f;
            capsule.radius = 0.4f;
            Physics.SyncTransforms();

            MethodInfo place = typeof(WeaponManager).Assembly.GetType("junklite.PauseMenuDeveloperTools").GetMethod("PlaceOnGround", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(place, Is.Not.Null);
            const float floor = 3000f;
            place.Invoke(null, new object[] { go, floor });

            float expectedY = floor + 0.08f + 1f; // clearance + half the capsule
            Assert.That(go.transform.position.y, Is.EqualTo(expectedY).Within(0.01f));
            Assert.That(body.position.y, Is.EqualTo(expectedY).Within(0.01f), "the interpolated body is moved too, so it does not fall from the spawn height");
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));
        }
    }
}
