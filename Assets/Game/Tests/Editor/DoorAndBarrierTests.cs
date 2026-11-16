using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace junklite.Tests
{
    /// <summary>Door / boss-room / enemy-barrier rules from the 2026-11-16 door pass.</summary>
    public sealed class DoorAndBarrierTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private readonly List<Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in owned)
                if (obj != null) Object.DestroyImmediate(obj);
            owned.Clear();
        }

        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        private static object Get(object target, string field) => target.GetType().GetField(field, Any).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Any).SetValue(target, value);
        private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Any).Invoke(target, args);

        private BoxCollider MakeWall(float x)
        {
            var go = Own(new GameObject("Wall"));
            go.transform.position = new Vector3(x, 1f, 0f);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(0.5f, 4f, 4f);
            Physics.SyncTransforms();
            return box;
        }

        [Test]
        public void LeapSweepStopsBeforeAWallAndPassesWhenClear()
        {
            MakeWall(5f);
            int mask = LayerMask.GetMask("Default");

            bool blocked = LeapState.SweepBlocked(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f), 0.9f, mask, out Vector3 stop);
            Assert.That(blocked, Is.True);
            Assert.That(stop.x, Is.LessThan(4.75f).And.GreaterThan(3f), "Stops short of the wall face (x 4.75).");

            Assert.That(LeapState.SweepBlocked(new Vector3(0f, 0f, 0f), new Vector3(3f, 0f, 0f), 0.9f, mask, out _), Is.False);
            Assert.That(LeapState.SweepBlocked(new Vector3(6f, 0f, 0f), new Vector3(10f, 0f, 0f), 0.9f, mask, out _), Is.False,
                "A wall behind the leap does not block it.");
        }

        [Test]
        public void SlidingDoorDropsAPlayerColliderThatWasDisabledInsideIt()
        {
            var door = Own(new GameObject("Door")).AddComponent<SlidingDoor>();
            Call(door, "Awake");
            var player = Own(new GameObject("Player")).AddComponent<BoxCollider>();
            player.gameObject.tag = "Player";

            Call(door, "OnTriggerEnter", player);
            Call(door, "Update");
            Assert.That(((HashSet<Collider>)Get(door, "playerColliders")).Count, Is.EqualTo(1));

            // Death switches the player's colliders off without an OnTriggerExit.
            player.enabled = false;
            Call(door, "Update");
            Assert.That(((HashSet<Collider>)Get(door, "playerColliders")).Count, Is.Zero);
        }

        [TestCase(true, 1)]
        [TestCase(false, 0)]
        public void OnlyDoorsThatAllowItOpenForAnEnemy(bool enemiesCanOpen, int expectedContacts)
        {
            var door = Own(new GameObject("Door")).AddComponent<SlidingDoor>();
            Call(door, "Awake");
            door.EnemiesCanOpen = enemiesCanOpen;
            var enemyGo = Own(new GameObject("Enemy"));
            enemyGo.AddComponent<Rigidbody>();
            var enemy = enemyGo.AddComponent<EnemyCharacter>();
            var body = enemyGo.AddComponent<CapsuleCollider>();

            Call(door, "OnTriggerEnter", body);

            var contacts = (System.Collections.IDictionary)Get(door, "enemyContacts");
            Assert.That(contacts.Count, Is.EqualTo(expectedContacts));
            Assert.That(enemy, Is.Not.Null);
        }

        [Test]
        public void EnemyBarrierOnlyCollidesWithEnemies()
        {
            var barrier = Own(new GameObject("Barrier"));
            var box = barrier.AddComponent<BoxCollider>();
            var comp = barrier.AddComponent<EnemyBarrier>();
            Call(comp, "Awake");

            int enemies = 1 << LayerMask.NameToLayer("Enemies");
            Assert.That(barrier.layer, Is.EqualTo(LayerMask.NameToLayer(EnemyBarrier.LayerName)), "On its own layer, out of the controller's probes.");
            Assert.That(box.isTrigger, Is.False);
            Assert.That((box.excludeLayers.value & enemies), Is.Zero, "Enemies are not excluded.");
            Assert.That((box.excludeLayers.value & (1 << LayerMask.NameToLayer("Player"))), Is.Not.Zero, "The player passes through.");
            Assert.That((box.excludeLayers.value & 1), Is.Not.Zero, "Default layer props pass through.");
        }

        [Test]
        public void LaneGateOnlyStaysOpenOnTheFarSideOfItsDoor()
        {
            var trigger = Own(new GameObject("Trigger")).AddComponent<BoxCollider>();
            var gate = Own(new GameObject("Door"));
            gate.transform.position = new Vector3(3f, 0f, 0f);
            var door = gate.AddComponent<SlidingDoor>();
            var entry = trigger.gameObject.AddComponent<LaneEntryTrigger>();
            Set(entry, "gateDoor", door);

            Assert.That((bool)Call(entry, "IsPastGate", new Vector3(6f, 0f, 0f)), Is.True, "Beyond the door.");
            Assert.That((bool)Call(entry, "IsPastGate", new Vector3(-2f, 0f, 0f)), Is.False, "Still in the room.");
            Assert.That((bool)Call(entry, "IsPastGate", new Vector3(3.2f, 0f, 0f)), Is.False, "In the doorway counts as the room.");
        }
    }
}
