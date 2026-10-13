using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace junklite.Tests
{
    public sealed class ObjectiveSystemTests
    {
        private readonly List<Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            typeof(ObjectiveTracker).GetProperty(nameof(ObjectiveTracker.Instance)).SetValue(null, null);
            foreach (Object obj in owned)
                if (obj != null) Object.DestroyImmediate(obj);
            owned.Clear();
        }

        [Test]
        public void TrackerCompletesOnceAndRaisesChangedOnce()
        {
            ObjectiveTracker tracker = MakeTracker();
            ObjectiveDefinition objective = MakeObjective("GET_HEALTH");
            int changes = 0;
            tracker.Changed += () => changes++;

            Assert.That(tracker.Complete(objective), Is.True);
            Assert.That(tracker.Complete(objective), Is.False);
            Assert.That(tracker.Complete(null), Is.False);
            Assert.That(tracker.IsComplete(objective), Is.True);
            Assert.That(changes, Is.EqualTo(1));
        }

        [Test]
        public void KeyringAddsAndCountsKeysByTag()
        {
            var keyring = Own(new GameObject("Keyring")).AddComponent<PlayerKeyring>();
            string changedTag = null;
            keyring.Changed += tag => changedTag = tag;

            Assert.That(keyring.Has("Z Key"), Is.False);
            keyring.Add("Z Key");
            keyring.Add("Z Key");

            Assert.That(keyring.Has("Z Key"), Is.True);
            Assert.That(keyring.Count("Z Key"), Is.EqualTo(2));
            Assert.That(keyring.Has("Other Key"), Is.False);
            Assert.That(changedTag, Is.EqualTo("Z Key"));
        }

        [Test]
        public void SlidingDoorStaysLockedUntilEveryOwnerReleases()
        {
            var door = Own(new GameObject("Door")).AddComponent<SlidingDoor>();
            object gate = new object();
            object rule = new object();

            door.AddLock(gate);
            door.AddLock(gate);
            door.AddLock(rule);
            door.RemoveLock(gate);
            Assert.That(door.Locked, Is.True);

            door.RemoveLock(rule);
            door.RemoveLock(rule);
            Assert.That(door.Locked, Is.False);
        }

        [Test]
        public void AccessRuleUnlocksDoorWhenObjectiveCompletes()
        {
            ObjectiveTracker tracker = MakeTracker();
            ObjectiveDefinition objective = MakeObjective("GET_HEALTH");
            DoorAccessRule rule = MakeRule(out SlidingDoor door);
            SetField(rule, "requiredObjective", objective);
            Call(rule, "Awake");
            Call(rule, "OnEnable");

            Assert.That(rule.IsSatisfied(), Is.False);
            Assert.That(door.Locked, Is.True);

            tracker.Complete(objective);

            Assert.That(rule.IsSatisfied(), Is.True);
            Assert.That(door.Locked, Is.False, "Changed should re-evaluate and release the rule's lock.");
            Call(rule, "OnDisable");
        }

        [Test]
        public void AccessRuleUnlocksDoorWhenKeyIsAdded()
        {
            DoorAccessRule rule = MakeRule(out SlidingDoor door);
            SetField(rule, "requiredKeyTag", "Z Key");
            Call(rule, "Awake");
            var keyring = Own(new GameObject("Keyring")).AddComponent<PlayerKeyring>();
            Call(rule, "SetKeyring", keyring);

            Assert.That(door.Locked, Is.True);
            keyring.Add("Z Key");
            Assert.That(door.Locked, Is.False);
            Call(rule, "OnDisable");
        }

        [Test]
        public void LockedForeverRuleNeverUnlocks()
        {
            MakeTracker();
            DoorAccessRule rule = MakeRule(out SlidingDoor door);
            SetField(rule, "lockedForever", true);
            Call(rule, "Awake");

            Assert.That(rule.IsSatisfied(), Is.False);
            Assert.That(door.Locked, Is.True);
        }

        [Test]
        public void AccessRuleLockComposesWithOtherOwners()
        {
            DoorAccessRule rule = MakeRule(out SlidingDoor door);
            object gate = new object();
            door.AddLock(gate);
            Call(rule, "Awake");

            Assert.That(rule.IsSatisfied(), Is.True);
            Assert.That(door.Locked, Is.True, "A satisfied rule must not clear another owner's lock.");
        }

        private ObjectiveTracker MakeTracker()
        {
            var tracker = Own(new GameObject("Objective Tracker")).AddComponent<ObjectiveTracker>();
            Call(tracker, "Awake");
            return tracker;
        }

        private ObjectiveDefinition MakeObjective(string id)
        {
            var objective = Own(ScriptableObject.CreateInstance<ObjectiveDefinition>());
            objective.name = id;
            return objective;
        }

        private DoorAccessRule MakeRule(out SlidingDoor door)
        {
            var root = Own(new GameObject("Door"));
            door = root.AddComponent<SlidingDoor>();
            return root.AddComponent<DoorAccessRule>();
        }

        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        private static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static object Call(object target, string name, params object[] arguments) => target.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Invoke(target, arguments);
    }
}
