using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace junklite.Tests
{
    /// <summary>
    /// Regressions from the Rooftop playtest: launched grunts must land on any solid floor
    /// whatever its layer, enemies share the player's floor layers, and the Game Manager
    /// prefab alone picks the spawned player.
    /// </summary>
    public sealed class LaunchLandingAndSpawnSetupTests
    {
        private const int PlayerFloorLayers = (1 << 0) | (1 << 11) | (1 << 12); // Default, Ground, Wall
        private readonly List<Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        private static void Invoke(object target, string name) => target.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

        private static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        /// <summary>An enemy body far from any collider, so its ground ray never sees a floor.</summary>
        private EnemyMovement MakeAirborneEnemy()
        {
            var go = new GameObject("Launch Landing Enemy");
            owned.Add(go);
            go.transform.position = new Vector3(9000f, 9000f, 9000f);
            go.AddComponent<Rigidbody>();
            go.AddComponent<CapsuleCollider>();
            var movement = go.AddComponent<EnemyMovement>();
            Invoke(movement, "Awake");
            // Ground only, as the grunt had: the floor it lands on is not in the mask.
            SetField(movement, "groundLayers", (LayerMask)(1 << 11));
            return movement;
        }

        private static void Step(EnemyMovement movement, float verticalSpeed)
        {
            movement.GetComponent<Rigidbody>().linearVelocity = new Vector3(0f, verticalSpeed, 0f);
            Invoke(movement, "FixedUpdate");
        }

        [Test]
        public void AFallStoppedByAnUnmaskedColliderStillLands()
        {
            EnemyMovement movement = MakeAirborneEnemy();
            movement.Launch(new Vector3(0f, -12f, 0f), 6f);
            Step(movement, -12f);
            Step(movement, -13f);
            Assert.That(movement.TryConsumeLanding(out _), Is.False, "a fall that keeps speeding up is still in the air");

            Step(movement, -0.2f); // the solver stopped the body on a floor the ray cannot see
            Assert.That(movement.TryConsumeLanding(out float impact), Is.True);
            Assert.That(impact, Is.EqualTo(13f).Within(0.01f), "the impact is the speed just before contact");
            Assert.That(movement.TryConsumeLanding(out _), Is.False, "one landing per flight");
        }

        [Test]
        public void TheApexAndAJuggleAreNotLandings()
        {
            EnemyMovement movement = MakeAirborneEnemy();
            movement.Launch(new Vector3(0f, 20f, 0f), 6f);
            Step(movement, 20f);
            Step(movement, 1f);
            Step(movement, -1f); // turning over at the apex: slow, not a stop
            Step(movement, -0.5f);
            Assert.That(movement.TryConsumeLanding(out _), Is.False);

            Step(movement, -8f);
            movement.SetLaunchVelocity(new Vector3(0f, 8f, 0f)); // a juggle hit throws it up again
            Invoke(movement, "FixedUpdate");
            Assert.That(movement.TryConsumeLanding(out _), Is.False, "a hit reversing the fall is not a landing");
        }

        [Test]
        public void EnemiesTreatThePlayersFloorLayersAsGround()
        {
            foreach (string path in new[]
                     {
                         "Assets/Game/ENEMIES/Grunt/Grunt Enemy.prefab",
                         "Assets/Game/ENEMIES/Brute/Brute Boss.prefab",
                         "Assets/Game/Prefabs/Enemies/Hyena.prefab",
                     })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);
                var movement = prefab.GetComponent<EnemyMovement>() ?? prefab.GetComponentInChildren<EnemyMovement>(true);
                Assert.That(movement, Is.Not.Null, path);
                int mask = new SerializedObject(movement).FindProperty("groundLayers").intValue;
                Assert.That(mask & PlayerFloorLayers, Is.EqualTo(PlayerFloorLayers), $"{path} must see Default, Ground and Wall floors");
            }
        }

        private static float LaunchGravity(EnemyMovement movement) => (float)typeof(EnemyMovement)
            .GetMethod("CurrentLaunchGravityScale", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(movement, null);

        [Test]
        public void AnAirHitSlowsTheEnemysFallOnlyWhileFallingAndOnlyForItsWindow()
        {
            EnemyMovement movement = MakeAirborneEnemy();
            var body = movement.GetComponent<Rigidbody>();
            movement.Launch(new Vector3(0f, 12f, 0f), 9f);
            Assert.That(LaunchGravity(movement), Is.EqualTo(9f), "a plain launch uses the full launch gravity");

            movement.SetLaunchFallHang(1.5f, 0.5f);
            Assert.That(movement.IsLaunchFallHangActive, Is.True);
            Assert.That(LaunchGravity(movement), Is.EqualTo(9f), "rising after the hit is still fast");
            body.linearVelocity = new Vector3(0f, -3f, 0f);
            Assert.That(LaunchGravity(movement), Is.EqualTo(1.5f), "falling after the hit is slow");

            movement.SetLaunchFallHang(1f, 0f); // a bounce or spike clears it
            Assert.That(movement.IsLaunchFallHangActive, Is.False);
            Assert.That(LaunchGravity(movement), Is.EqualTo(9f));

            movement.SetLaunchFallHang(1.5f, 0.5f);
            movement.EndLaunch();
            Assert.That(movement.IsLaunchFallHangActive, Is.False, "the hang ends with the launch");
        }

        [Test]
        public void AnAirHitSlowsThePlayersFallUntilLanding()
        {
            var go = new GameObject("Air Hang Player");
            owned.Add(go);
            go.transform.position = new Vector3(-9000f, 9000f, -9000f);
            go.AddComponent<Rigidbody>().useGravity = false;
            go.AddComponent<CapsuleCollider>();
            var controller = go.AddComponent<Character2D5Controller>();
            Invoke(controller, "Awake");
            Assert.That(controller.IsAirHangActive, Is.False);
            controller.ApplyAirHang(0.22f, 0.5f);
            Assert.That(controller.IsAirHangActive, Is.True, "airborne: the hit earns a slow fall");
            controller.ApplyAirHang(0.22f, 0f);
            Assert.That(controller.IsAirHangActive, Is.True, "a zero-length request never cuts a running hang short");
        }

        [Test]
        public void TheGruntGoesUpAndDownFastAndOnlyFloatsAfterAnAirHit()
        {
            var grunt = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/ENEMIES/Grunt/Grunt Enemy.prefab");
            var launch = new SerializedObject(grunt.GetComponent<EnemyCharacter>()).FindProperty("launch");
            Assert.That(launch.FindPropertyRelative("apexHangSpeed").floatValue, Is.Zero, "no float at the top of a plain launch");
            Assert.That(launch.FindPropertyRelative("airHitHangDuration").floatValue, Is.GreaterThan(0f));
            Assert.That(launch.FindPropertyRelative("airHitFallGravityScale").floatValue,
                Is.LessThan(launch.FindPropertyRelative("gravityScale").floatValue), "the earned fall is slower than the launch fall");
        }

        [Test]
        public void TheGameManagerPrefabPicksThePlayerForEveryRoot()
        {
            var manager = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Manager/Game Manager.prefab").GetComponent<PlayerLifecycle>();
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Manager/Game Root.prefab").GetComponent<PlayerLifecycle>();
            Assert.That(manager, Is.Not.Null);
            Assert.That(root, Is.Not.Null);
            Assert.That(new SerializedObject(root).FindProperty("playerSettingsSource").objectReferenceValue, Is.SameAs(manager),
                "Game Root reads the player choice from the Game Manager prefab");
            Assert.That(root.PlayerPrefab, Is.SameAs(manager.PlayerPrefab));

            var oldPlayer = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/PLAYER/Player_2.2.prefab");
            Assert.That(manager.PlayerPrefab, Is.SameAs(oldPlayer), "the old player is the current playtest player");
        }

        [Test]
        public void ASettingsSourceOverridesTheOwnFieldsAndIgnoresItself()
        {
            var oldPlayer = new GameObject("Old");
            var newPlayer = new GameObject("New");
            owned.Add(oldPlayer);
            owned.Add(newPlayer);
            var holder = new GameObject("Lifecycles");
            owned.Add(holder);
            holder.SetActive(false);
            var source = holder.AddComponent<PlayerLifecycle>();
            SetField(source, "playerPrefab", oldPlayer);
            SetField(source, "newPlayerPrefab", newPlayer);
            SetField(source, "useNewPlayer", false);
            var follower = holder.AddComponent<PlayerLifecycle>();
            SetField(follower, "playerPrefab", oldPlayer);
            SetField(follower, "newPlayerPrefab", newPlayer);
            SetField(follower, "useNewPlayer", true);

            Assert.That(follower.PlayerPrefab, Is.SameAs(newPlayer), "without a source its own checkbox decides");
            SetField(follower, "playerSettingsSource", source);
            Assert.That(follower.PlayerPrefab, Is.SameAs(oldPlayer), "the source's checkbox wins");
            SetField(source, "useNewPlayer", true);
            Assert.That(follower.PlayerPrefab, Is.SameAs(newPlayer));
            SetField(source, "playerSettingsSource", source);
            Assert.That(source.PlayerPrefab, Is.SameAs(newPlayer), "a lifecycle pointing at itself uses its own fields");
        }
    }
}
