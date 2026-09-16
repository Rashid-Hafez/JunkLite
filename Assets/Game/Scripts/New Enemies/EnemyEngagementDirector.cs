using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    public readonly struct EnemyEngagementAssignment
    {
        public EnemyEngagementAssignment(Vector3 destination, int queueIndex)
        {
            Destination = destination;
            QueueIndex = queueIndex;
        }

        public Vector3 Destination { get; }
        public int QueueIndex { get; }
        public bool IsFront => QueueIndex == 0;
    }

    /// <summary>
    /// Runtime-only coordinator for enemies targeting the same player. It assigns
    /// stable approach positions on each side and grants one attack lease at a time.
    /// No scene object is required.
    /// </summary>
    public static class EnemyEngagementDirector
    {
        private enum AxisKind
        {
            X,
            Z
        }

        private sealed class Entry
        {
            public EnemyCharacter Enemy;
            public int LastSide = 1;
            public float HalfWidthX;
            public float HalfWidthZ;
        }

        private sealed class Group
        {
            public PlayerCharacter Target;
            public readonly Dictionary<EnemyCharacter, Entry> Entries = new();
            public EnemyCharacter ActiveAttacker;
            public EnemyCharacter LastAttacker;
            public float NextAttackAllowedTime;
        }

        private sealed class LaneEntryComparer : IComparer<Entry>
        {
            public PlayerCharacter Target;
            public Vector3 Axis;

            public int Compare(Entry a, Entry b)
            {
                return CompareLaneEntries(a, b, Target, Axis);
            }
        }

        private const float QueueGap = 0.2f;
        private const float MinimumHalfWidth = 0.3f;
        private const float SideDeadZone = 0.1f;
        private const float LaneVerticalTolerance = 1.5f;
        private const float AttackHandoffDelay = 0.25f;
        private const float AttackReadyTolerance = 0.15f;

        private static readonly Dictionary<PlayerCharacter, Group> Groups = new();
        private static readonly Dictionary<EnemyCharacter, Group> Memberships = new();
        private static readonly List<Entry> LaneBuffer = new();
        private static readonly List<EnemyCharacter> StaleEnemyBuffer = new();
        private static readonly LaneEntryComparer LaneComparer = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntimeState()
        {
            Groups.Clear();
            Memberships.Clear();
            LaneBuffer.Clear();
            StaleEnemyBuffer.Clear();
        }

        public static bool Register(EnemyCharacter enemy)
        {
            if (!IsValidParticipant(enemy) || enemy.TargetCharacter == null)
            {
                Unregister(enemy);
                return false;
            }

            PlayerCharacter target = enemy.TargetCharacter;
            if (Memberships.TryGetValue(enemy, out Group currentGroup))
            {
                if (currentGroup.Target == target)
                {
                    Prune(currentGroup);
                    return currentGroup.Entries.ContainsKey(enemy);
                }

                RemoveEntry(currentGroup, enemy);
            }

            if (!Groups.TryGetValue(target, out Group group))
            {
                group = new Group { Target = target };
                Groups.Add(target, group);
            }

            var entry = new Entry
            {
                Enemy = enemy,
                HalfWidthX = MeasureHalfWidth(enemy, Vector3.right),
                HalfWidthZ = MeasureHalfWidth(enemy, Vector3.forward)
            };

            group.Entries[enemy] = entry;
            Memberships[enemy] = group;
            return true;
        }

        public static void Unregister(EnemyCharacter enemy)
        {
            if (ReferenceEquals(enemy, null))
                return;

            if (Memberships.TryGetValue(enemy, out Group group))
                RemoveEntry(group, enemy);
        }

        public static bool TryGetAssignment(
            EnemyCharacter enemy,
            out EnemyEngagementAssignment assignment)
        {
            assignment = default;
            if (!Register(enemy) || !Memberships.TryGetValue(enemy, out Group group))
                return false;

            return TryGetAssignmentInternal(group, enemy, out assignment);
        }

        public static bool TryAcquireAttack(EnemyCharacter enemy, bool requireFrontPosition)
        {
            if (!Register(enemy) || !Memberships.TryGetValue(enemy, out Group group))
                return false;

            Prune(group);

            if (group.ActiveAttacker == enemy)
                return true;
            if (group.ActiveAttacker != null || Time.time < group.NextAttackAllowedTime)
                return false;

            if (requireFrontPosition)
            {
                if (!TryGetAssignmentInternal(group, enemy, out EnemyEngagementAssignment assignment)
                    || !assignment.IsFront)
                {
                    return false;
                }
            }

            if (group.LastAttacker == enemy && HasReadyAlternative(group, enemy))
                return false;

            group.ActiveAttacker = enemy;
            return true;
        }

        public static bool HoldsAttack(EnemyCharacter enemy)
        {
            return !ReferenceEquals(enemy, null)
                && Memberships.TryGetValue(enemy, out Group group)
                && group.ActiveAttacker == enemy;
        }

        public static void ReleaseAttack(EnemyCharacter enemy)
        {
            if (ReferenceEquals(enemy, null)
                || !Memberships.TryGetValue(enemy, out Group group)
                || group.ActiveAttacker != enemy)
            {
                return;
            }

            group.ActiveAttacker = null;
            group.LastAttacker = enemy;
            group.NextAttackAllowedTime = Time.time + AttackHandoffDelay;
        }

        private static bool TryGetAssignmentInternal(
            Group group,
            EnemyCharacter enemy,
            out EnemyEngagementAssignment assignment)
        {
            assignment = default;
            if (!group.Entries.TryGetValue(enemy, out Entry requestingEntry))
                return false;

            AxisKind axisKind = GetAxisKind(enemy);
            Vector3 axis = GetAxis(axisKind);
            int side = GetSide(requestingEntry, group.Target, axis);
            float requestY = enemy.transform.position.y;

            LaneBuffer.Clear();
            foreach (Entry entry in group.Entries.Values)
            {
                if (!IsValidForGroup(entry.Enemy, group.Target)
                    || !(entry.Enemy.Brain is IChaseDestinationProvider)
                    || GetAxisKind(entry.Enemy) != axisKind
                    || GetSide(entry, group.Target, axis) != side
                    || Mathf.Abs(entry.Enemy.transform.position.y - requestY) > LaneVerticalTolerance)
                {
                    continue;
                }

                LaneBuffer.Add(entry);
            }

            if (LaneBuffer.Count == 0)
                return false;

            LaneComparer.Target = group.Target;
            LaneComparer.Axis = axis;
            LaneBuffer.Sort(LaneComparer);

            float slotDistance = GetAttackDistance(LaneBuffer[0].Enemy);
            for (int i = 0; i < LaneBuffer.Count; i++)
            {
                if (i > 0)
                {
                    slotDistance += GetHalfWidth(LaneBuffer[i - 1], axisKind)
                        + GetHalfWidth(LaneBuffer[i], axisKind)
                        + QueueGap;
                }

                if (LaneBuffer[i] != requestingEntry)
                    continue;

                Vector3 destination = BuildDestination(
                    enemy,
                    group.Target.transform.position,
                    axis,
                    side,
                    slotDistance);
                assignment = new EnemyEngagementAssignment(destination, i);
                return true;
            }

            return false;
        }

        private static int CompareLaneEntries(
            Entry a,
            Entry b,
            PlayerCharacter target,
            Vector3 axis)
        {
            float aDistance = Mathf.Abs(Vector3.Dot(
                a.Enemy.transform.position - target.transform.position,
                axis));
            float bDistance = Mathf.Abs(Vector3.Dot(
                b.Enemy.transform.position - target.transform.position,
                axis));

            int distanceComparison = aDistance.CompareTo(bDistance);
            return distanceComparison != 0
                ? distanceComparison
                : a.Enemy.GetInstanceID().CompareTo(b.Enemy.GetInstanceID());
        }

        private static bool HasReadyAlternative(Group group, EnemyCharacter excluded)
        {
            foreach (Entry entry in group.Entries.Values)
            {
                EnemyCharacter candidate = entry.Enemy;
                if (candidate == excluded || !IsValidForGroup(candidate, group.Target))
                    continue;
                if (!CanRequestAttack(candidate))
                    continue;

                if (candidate.Brain is IChaseDestinationProvider)
                {
                    if (!TryGetAssignmentInternal(
                            group,
                            candidate,
                            out EnemyEngagementAssignment assignment)
                        || !assignment.IsFront)
                    {
                        continue;
                    }

                    float distance = candidate.Movement.GetAbsAxisDistance(
                        candidate.transform.position,
                        candidate.Target.position);
                    if (distance > GetAttackDistance(candidate) + AttackReadyTolerance)
                        continue;
                }

                return true;
            }

            return false;
        }

        private static Vector3 BuildDestination(
            EnemyCharacter enemy,
            Vector3 targetPosition,
            Vector3 axis,
            int side,
            float distance)
        {
            Vector3 destination = targetPosition + axis * (side * distance);
            destination.y = enemy.transform.position.y;

            Vector3 depthAxis = Vector3.Cross(Vector3.up, axis).normalized;
            float currentDepth = Vector3.Dot(enemy.transform.position, depthAxis);
            float destinationDepth = Vector3.Dot(destination, depthAxis);
            destination += depthAxis * (currentDepth - destinationDepth);
            return destination;
        }

        private static int GetSide(Entry entry, PlayerCharacter target, Vector3 axis)
        {
            float signedDistance = Vector3.Dot(
                entry.Enemy.transform.position - target.transform.position,
                axis);
            if (Mathf.Abs(signedDistance) > SideDeadZone)
                entry.LastSide = signedDistance >= 0f ? 1 : -1;

            return entry.LastSide;
        }

        private static float GetAttackDistance(EnemyCharacter enemy)
        {
            IChaser chaser = enemy.GetCapability<IChaser>();
            float distance = chaser != null && chaser.ChaseStopDistance > 0f
                ? chaser.ChaseStopDistance
                : enemy.AttackRange;
            return Mathf.Max(0.1f, distance);
        }

        private static AxisKind GetAxisKind(EnemyCharacter enemy)
        {
            Vector3 movementAxis = enemy.Movement != null
                ? enemy.Movement.MovementAxis
                : Vector3.right;
            return Mathf.Abs(movementAxis.x) >= Mathf.Abs(movementAxis.z)
                ? AxisKind.X
                : AxisKind.Z;
        }

        private static Vector3 GetAxis(AxisKind axisKind)
        {
            return axisKind == AxisKind.X ? Vector3.right : Vector3.forward;
        }

        private static float GetHalfWidth(Entry entry, AxisKind axisKind)
        {
            return axisKind == AxisKind.X ? entry.HalfWidthX : entry.HalfWidthZ;
        }

        private static float MeasureHalfWidth(EnemyCharacter enemy, Vector3 axis)
        {
            float halfWidth = MinimumHalfWidth;
            Collider[] colliders = enemy.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;

                Vector3 extents = collider.bounds.extents;
                float projectedExtent = Mathf.Abs(axis.x) * extents.x
                    + Mathf.Abs(axis.y) * extents.y
                    + Mathf.Abs(axis.z) * extents.z;
                halfWidth = Mathf.Max(halfWidth, projectedExtent);
            }

            return halfWidth;
        }

        private static bool IsValidParticipant(EnemyCharacter enemy)
        {
            return enemy != null
                && enemy.isActiveAndEnabled
                && enemy.IsAlive
                && !enemy.IsTutorialFrozen
                && enemy.HasTarget
                && enemy.TargetCharacter != null;
        }

        private static bool CanRequestAttack(EnemyCharacter enemy)
        {
            IState state = enemy.StateMachine != null
                ? enemy.StateMachine.CurrentState
                : null;
            return state is IdleState
                || state is PatrolState
                || state is ChaseState
                || state is WaitForOpeningState;
        }

        private static bool IsValidForGroup(EnemyCharacter enemy, PlayerCharacter target)
        {
            return IsValidParticipant(enemy) && enemy.TargetCharacter == target;
        }

        private static void Prune(Group group)
        {
            StaleEnemyBuffer.Clear();
            foreach (EnemyCharacter enemy in group.Entries.Keys)
            {
                if (!IsValidForGroup(enemy, group.Target))
                    StaleEnemyBuffer.Add(enemy);
            }

            for (int i = 0; i < StaleEnemyBuffer.Count; i++)
                RemoveEntry(group, StaleEnemyBuffer[i]);
        }

        private static void RemoveEntry(Group group, EnemyCharacter enemy)
        {
            if (group.ActiveAttacker == enemy)
            {
                group.ActiveAttacker = null;
                group.NextAttackAllowedTime = Time.time + AttackHandoffDelay;
            }

            if (group.LastAttacker == enemy)
                group.LastAttacker = null;

            group.Entries.Remove(enemy);
            Memberships.Remove(enemy);

            if (group.Entries.Count == 0 && !ReferenceEquals(group.Target, null))
                Groups.Remove(group.Target);
        }
    }
}
