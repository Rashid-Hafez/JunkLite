using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace junklite.Tests
{
    public sealed class LevelEventTests
    {
        private readonly List<UnityEngine.Object> owned = new();

        [TearDown]
        public void TearDown()
        {
            typeof(ObjectiveTracker).GetProperty(nameof(ObjectiveTracker.Instance)).SetValue(null, null);
            foreach (UnityEngine.Object obj in owned)
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            owned.Clear();
        }

        [Test]
        public void ActionsRunInOrder()
        {
            var log = new List<string>();
            LevelEvent evt = MakeEvent(new RecordAction("a", log), new RecordAction("b", log), new RecordAction("c", log));

            RunToEnd(evt);

            Assert.That(log, Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void ObjectiveActionsDriveTracker()
        {
            ObjectiveTracker tracker = MakeTracker();
            ObjectiveDefinition basement = MakeObjective("ESCAPE_BASEMENT");
            LevelEvent evt = MakeEvent(new StartObjectiveAction { objective = basement });

            RunToEnd(evt);
            Assert.That(tracker.IsActive(basement), Is.True);

            LevelEvent done = MakeEvent(new CompleteObjectiveAction { objective = basement });
            RunToEnd(done);
            Assert.That(tracker.IsComplete(basement), Is.True);
        }

        [Test]
        public void AbortReleasesEveryLease()
        {
            LevelEvent evt = MakeEvent();
            var a = new CountingLease();
            var b = new CountingLease();
            evt.AddLease(a);
            evt.AddLease(b);

            evt.Abort();
            evt.Abort();

            Assert.That(a.Disposed, Is.EqualTo(1));
            Assert.That(b.Disposed, Is.EqualTo(1));
        }

        [Test]
        public void FinishingReleasesLeases()
        {
            var lease = new CountingLease();
            LevelEvent evt = MakeEvent(new LeaseAction(lease));

            RunToEnd(evt);

            Assert.That(lease.Disposed, Is.EqualTo(1));
        }

        [Test]
        public void ObjectiveCompletedSourceOnlyMatchesItsObjective()
        {
            ObjectiveTracker tracker = MakeTracker();
            ObjectiveDefinition watched = MakeObjective("DEFEAT_BRUTE");
            ObjectiveDefinition other = MakeObjective("GET_HEALTH");
            LevelEvent evt = MakeEvent();
            SetField(evt, "source", LevelEventSource.ObjectiveCompleted);
            SetField(evt, "sourceObjective", watched);
            Call(evt, "Rebind");

            tracker.Begin(watched);
            tracker.Complete(other);
            Assert.That(evt.HasPlayed, Is.False);

            tracker.Complete(watched);
            Assert.That(evt.HasPlayed, Is.True);
            Call(evt, "Unbind");
        }

        [Test]
        public void EveryBuiltInActionHasADefaultConstructorAndSummary()
        {
            foreach (Type type in typeof(LevelEventAction).Assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(LevelEventAction).IsAssignableFrom(type)) continue;
                var action = (LevelEventAction)Activator.CreateInstance(type);
                Assert.That(action.Summary, Is.Not.Empty, type.Name);
                Assert.That(type.GetCustomAttribute<SerializableAttribute>(), Is.Not.Null, $"{type.Name} must be [Serializable].");
            }
        }

        #region Helpers

        private sealed class RecordAction : LevelEventAction
        {
            private readonly string id;
            private readonly List<string> log;
            public RecordAction(string id, List<string> log) { this.id = id; this.log = log; }
            public RecordAction() { }

            public override IEnumerator Run(LevelEvent context)
            {
                log.Add(id);
                yield return null;
            }
        }

        private sealed class LeaseAction : LevelEventAction
        {
            private readonly IDisposable lease;
            public LeaseAction(IDisposable lease) => this.lease = lease;
            public LeaseAction() { }

            public override IEnumerator Run(LevelEvent context)
            {
                context.AddLease(lease);
                yield break;
            }
        }

        private sealed class CountingLease : IDisposable
        {
            public int Disposed;
            private bool done;
            public void Dispose()
            {
                if (done) return;
                done = true;
                Disposed++;
            }
        }

        /// <summary>EditMode does not tick coroutines, so step the event's routine (and nested ones) by hand.</summary>
        private static void RunToEnd(LevelEvent evt)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push((IEnumerator)Call(evt, "Run"));
            int guard = 0;
            while (stack.Count > 0 && guard++ < 1000)
            {
                IEnumerator top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) stack.Push(nested);
            }
            Assert.That(guard, Is.LessThan(1000), "Event did not finish.");
        }

        private LevelEvent MakeEvent(params LevelEventAction[] actions)
        {
            var evt = Own(new GameObject("Level Event")).AddComponent<LevelEvent>();
            SetField(evt, "source", LevelEventSource.Manual);
            SetField(evt, "actions", new List<LevelEventAction>(actions));
            return evt;
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

        private T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }

        private static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static object Call(object target, string name, params object[] arguments) => target.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Invoke(target, arguments);

        #endregion
    }
}
