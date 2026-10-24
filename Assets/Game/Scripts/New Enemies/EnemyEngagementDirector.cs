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
    /// Read-only view of where one enemy stands in its lane group. Produced by
    /// <see cref="EnemyEngagementDirector.TryGetLaneSnapshot"/> for brains that
    /// make group-aware decisions (vault to the open side, fall back behind the line).
    /// </summary>
    public readonly struct EnemyLaneSnapshot
    {
        public EnemyLaneSnapshot(
            int side,
            int queueIndex,
            int countThisSide,
            int countFarSide,
            float groupPressure,
            float groupQuietSeconds,
            bool hasActiveAttacker)
        {
            Side = side;
            QueueIndex = queueIndex;
            CountThisSide = countThisSide;
            CountFarSide = countFarSide;
            GroupPressure = groupPressure;
            GroupQuietSeconds = groupQuietSeconds;
            HasActiveAttacker = hasActiveAttacker;
        }

        /// <summary>+1 or -1 along the lane axis, relative to the player.</summary>
        public int Side { get; }
        /// <summary>0 = front of this side's queue.</summary>
        public int QueueIndex { get; }
        public int CountThisSide { get; }
        public int CountFarSide { get; }
        /// <summary>0..1, how hard the whole group has been hit recently.</summary>
        public float GroupPressure { get; }
        /// <summary>Seconds since any member of the group took a hit.</summary>
        public float GroupQuietSeconds { get; }
        public bool HasActiveAttacker { get; }

        public bool IsFront => QueueIndex == 0;
        public bool FarFrontOccupied => CountFarSide > 0;
    }

    /// <summary>
    /// The bodies directly ahead of and behind one enemy on its side of the lane,
    /// with distances measured from the player on that side (larger = further).
    /// Leaping enemies are excluded: they are airborne and crossing the line.
    /// </summary>
    public readonly struct EnemyLaneNeighbors
    {
        public EnemyLaneNeighbors(
            int side,
            Vector3 axis,
            Vector3 targetPosition,
            float myDistance,
            bool hasLeader,
            float leaderDistance,
            float leaderComfortSpacing,
            float leaderHardSpacing,
            Vector3 leaderVelocity,
            bool hasFollower,
            float followerDistance,
            float followerHardSpacing,
            Vector3 followerVelocity)
        {
            Side = side;
            Axis = axis;
            TargetPosition = targetPosition;
            MyDistance = myDistance;
            HasLeader = hasLeader;
            LeaderDistance = leaderDistance;
            LeaderComfortSpacing = leaderComfortSpacing;
            LeaderHardSpacing = leaderHardSpacing;
            LeaderVelocity = leaderVelocity;
            HasFollower = hasFollower;
            FollowerDistance = followerDistance;
            FollowerHardSpacing = followerHardSpacing;
            FollowerVelocity = followerVelocity;
        }

        public int Side { get; }
        public Vector3 Axis { get; }
        public Vector3 TargetPosition { get; }
        public float MyDistance { get; }

        public bool HasLeader { get; }
        public float LeaderDistance { get; }
        /// <summary>Centre-to-centre distance to keep while approaching (half-widths + queue gap).</summary>
        public float LeaderComfortSpacing { get; }
        /// <summary>Centre-to-centre distance never to cross (half-widths + contact gap).</summary>
        public float LeaderHardSpacing { get; }
        public Vector3 LeaderVelocity { get; }

        public bool HasFollower { get; }
        public float FollowerDistance { get; }
        public float FollowerHardSpacing { get; }
        public Vector3 FollowerVelocity { get; }
    }

    /// <summary>
    /// Runtime-only coordinator for enemies targeting the same player. It assigns
    /// stable approach positions on each side and grants one attack lease at a time.
    /// Group tactics (lane snapshots, yielding, leap reservation, group hit pressure,
    /// attack tempo) are additive queries on top of that; brains own the policy.
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
            /// <summary>A yielding enemy sorts to the back of its side while it leaves the line.</summary>
            public bool Yielding;
            /// <summary>Extra spacing this enemy wants behind the one in front of it.</summary>
            public float QueueGap = DefaultQueueGap;
        }

        private sealed class Group
        {
            public PlayerCharacter Target;
            public readonly Dictionary<EnemyCharacter, Entry> Entries = new();
            public EnemyCharacter ActiveAttacker;
            public EnemyCharacter LastAttacker;
            public int LastAttackSide;
            public float NextAttackAllowedTime;
            public float AttackHandoffDelay = DefaultAttackHandoffDelay;
            public float NextLeapAllowedTime;
            public readonly HitPressureWindow Pressure = new(12);
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

        private const float DefaultQueueGap = 0.2f;
        private const float MinimumHalfWidth = 0.3f;
        private const float SideDeadZone = 0.1f;
        private const float LaneVerticalTolerance = 1.5f;
        private const float DefaultAttackHandoffDelay = 0.25f;
        private const float AttackReadyTolerance = 0.15f;
        private const float GroupPressureWindow = 2.5f;
        private const int GroupPressureHitsToMax = 4;
        private const float BackOffClearance = 0.15f;

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

            int side = 0;
            if (requireFrontPosition)
            {
                if (!TryGetAssignmentInternal(group, enemy, out EnemyEngagementAssignment assignment)
                    || !assignment.IsFront)
                {
                    return false;
                }

                if (group.Entries.TryGetValue(enemy, out Entry entry))
                    side = GetSide(entry, group.Target, GetAxis(GetAxisKind(enemy)));
            }

            if (group.LastAttacker == enemy && HasReadyAlternative(group, enemy, 0))
                return false;

            // Pincer rhythm: when the last swing came from this side and a ready
            // attacker waits on the other side, let that side go first.
            if (side != 0
                && group.LastAttackSide == side
                && HasReadyAlternative(group, enemy, -side))
            {
                return false;
            }

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
            group.NextAttackAllowedTime = Time.time + group.AttackHandoffDelay;

            if (group.Entries.TryGetValue(enemy, out Entry entry))
                group.LastAttackSide = GetSide(entry, group.Target, GetAxis(GetAxisKind(enemy)));
        }

        // ============================================================
        // GROUP TACTICS — additive queries used by group-aware brains
        // ============================================================

        /// <summary>
        /// Where this enemy stands in its lane: side, queue index, counts on both
        /// sides, and group pressure. False when the enemy is not registered or
        /// not part of a lane (no IChaseDestinationProvider brain).
        /// </summary>
        public static bool TryGetLaneSnapshot(EnemyCharacter enemy, out EnemyLaneSnapshot snapshot)
        {
            snapshot = default;
            if (!Register(enemy) || !Memberships.TryGetValue(enemy, out Group group))
                return false;
            if (!group.Entries.TryGetValue(enemy, out Entry requestingEntry))
                return false;

            AxisKind axisKind = GetAxisKind(enemy);
            Vector3 axis = GetAxis(axisKind);
            int side = GetSide(requestingEntry, group.Target, axis);
            float requestY = enemy.transform.position.y;

            BuildLane(group, requestingEntry, axisKind, axis, side, requestY);
            int queueIndex = LaneBuffer.IndexOf(requestingEntry);
            if (queueIndex < 0)
                return false;

            int countThisSide = LaneBuffer.Count;
            int countFarSide = CountSide(group, null, axisKind, axis, -side, requestY);
            float now = Time.time;

            snapshot = new EnemyLaneSnapshot(
                side,
                queueIndex,
                countThisSide,
                countFarSide,
                group.Pressure.Pressure01(now, GroupPressureWindow, GroupPressureHitsToMax),
                group.Pressure.QuietSeconds(now),
                group.ActiveAttacker != null);
            return true;
        }

        /// <summary>
        /// The slot one place behind every other enemy on the given side (where this
        /// enemy would stand after leaving the line). False when nobody else is there.
        /// </summary>
        public static bool TryGetBackSlot(EnemyCharacter enemy, int side, out Vector3 slot)
        {
            slot = default;
            if (!Memberships.TryGetValue(enemy, out Group group)
                || !group.Entries.TryGetValue(enemy, out Entry requestingEntry)
                || !IsValidForGroup(enemy, group.Target))
            {
                return false;
            }

            AxisKind axisKind = GetAxisKind(enemy);
            Vector3 axis = GetAxis(axisKind);
            side = side >= 0 ? 1 : -1;
            BuildLane(group, null, axisKind, axis, side, enemy.transform.position.y);
            LaneBuffer.Remove(requestingEntry);
            if (LaneBuffer.Count == 0)
                return false;

            float distance = GetSlotDistance(LaneBuffer, LaneBuffer.Count - 1, axisKind);
            Entry last = LaneBuffer[LaneBuffer.Count - 1];
            distance += GetHalfWidth(last, axisKind)
                + GetHalfWidth(requestingEntry, axisKind)
                + Mathf.Max(last.QueueGap, requestingEntry.QueueGap);

            slot = BuildDestination(enemy, group.Target.transform.position, axis, side, distance);
            return true;
        }

        /// <summary>
        /// Farthest axis distance from the player this enemy may retreat to without
        /// overlapping the next enemy behind it on the same side. Infinite when free.
        /// </summary>
        public static float GetBackOffLimit(EnemyCharacter enemy)
        {
            if (!Memberships.TryGetValue(enemy, out Group group)
                || !group.Entries.TryGetValue(enemy, out Entry requestingEntry)
                || !IsValidForGroup(enemy, group.Target))
            {
                return float.PositiveInfinity;
            }

            AxisKind axisKind = GetAxisKind(enemy);
            Vector3 axis = GetAxis(axisKind);
            int side = GetSide(requestingEntry, group.Target, axis);
            Vector3 targetPosition = group.Target.transform.position;
            float myDistance = Mathf.Abs(Vector3.Dot(enemy.transform.position - targetPosition, axis));

            BuildLane(group, requestingEntry, axisKind, axis, side, enemy.transform.position.y);

            float limit = float.PositiveInfinity;
            for (int i = 0; i < LaneBuffer.Count; i++)
            {
                Entry other = LaneBuffer[i];
                if (other == requestingEntry)
                    continue;

                float otherDistance = Mathf.Abs(Vector3.Dot(other.Enemy.transform.position - targetPosition, axis));
                if (otherDistance <= myDistance)
                    continue;

                float clearance = GetHalfWidth(other, axisKind) + GetHalfWidth(requestingEntry, axisKind) + BackOffClearance;
                limit = Mathf.Min(limit, otherDistance - clearance);
            }

            return limit;
        }

        /// <summary>
        /// The real bodies directly ahead of and behind this enemy on its side, so
        /// movement can keep spacing from where neighbours actually are instead of
        /// where their slots say they should be. O(n) over the group, no sort.
        /// </summary>
        public static bool TryGetLaneNeighbors(
            EnemyCharacter enemy,
            float contactGap,
            out EnemyLaneNeighbors neighbors)
        {
            neighbors = default;
            if (ReferenceEquals(enemy, null)
                || !Memberships.TryGetValue(enemy, out Group group)
                || !group.Entries.TryGetValue(enemy, out Entry me)
                || !IsValidForGroup(enemy, group.Target))
            {
                return false;
            }

            AxisKind axisKind = GetAxisKind(enemy);
            Vector3 axis = GetAxis(axisKind);
            int side = GetSide(me, group.Target, axis);
            Vector3 targetPosition = group.Target.transform.position;
            float requestY = enemy.transform.position.y;
            float myDistance = side * Vector3.Dot(enemy.transform.position - targetPosition, axis);
            int myId = enemy.GetInstanceID();

            Entry leader = null;
            Entry follower = null;
            float leaderDistance = float.NegativeInfinity;
            float followerDistance = float.PositiveInfinity;

            foreach (Entry entry in group.Entries.Values)
            {
                if (entry == me
                    || IsAirborne(entry.Enemy)
                    || !IsLaneMember(entry, group, axisKind, axis, side, requestY))
                {
                    continue;
                }

                float distance = side * Vector3.Dot(entry.Enemy.transform.position - targetPosition, axis);
                bool ahead = distance < myDistance
                    || (Mathf.Approximately(distance, myDistance) && entry.Enemy.GetInstanceID() < myId);

                if (ahead)
                {
                    if (distance > leaderDistance)
                    {
                        leaderDistance = distance;
                        leader = entry;
                    }
                }
                else if (distance < followerDistance)
                {
                    followerDistance = distance;
                    follower = entry;
                }
            }

            float myHalfWidth = GetHalfWidth(me, axisKind);
            float leaderHalf = leader != null ? GetHalfWidth(leader, axisKind) : 0f;
            float followerHalf = follower != null ? GetHalfWidth(follower, axisKind) : 0f;
            float contact = Mathf.Max(0f, contactGap);

            neighbors = new EnemyLaneNeighbors(
                side,
                axis,
                targetPosition,
                myDistance,
                leader != null,
                leaderDistance,
                leader != null ? myHalfWidth + leaderHalf + Mathf.Max(me.QueueGap, leader.QueueGap) : 0f,
                leader != null ? myHalfWidth + leaderHalf + contact : 0f,
                leader != null && leader.Enemy.Movement != null ? leader.Enemy.Movement.Velocity : Vector3.zero,
                follower != null,
                followerDistance,
                follower != null ? myHalfWidth + followerHalf + contact : 0f,
                follower != null && follower.Enemy.Movement != null ? follower.Enemy.Movement.Velocity : Vector3.zero);
            return true;
        }

        /// <summary>
        /// Tells every lane-mate behind <paramref name="hitEnemy"/> on its side that it
        /// just took a real hit, with how many places back each one stands, so they
        /// can react as a ripple. Listeners must only record the event.
        /// </summary>
        public static void NotifyAllyHit(EnemyCharacter hitEnemy)
        {
            if (ReferenceEquals(hitEnemy, null)
                || !Memberships.TryGetValue(hitEnemy, out Group group)
                || !group.Entries.TryGetValue(hitEnemy, out Entry hitEntry)
                || !IsValidForGroup(hitEnemy, group.Target))
            {
                return;
            }

            AxisKind axisKind = GetAxisKind(hitEnemy);
            Vector3 axis = GetAxis(axisKind);
            int side = GetSide(hitEntry, group.Target, axis);
            BuildLane(group, hitEntry, axisKind, axis, side, hitEnemy.transform.position.y);

            int hitIndex = LaneBuffer.IndexOf(hitEntry);
            if (hitIndex < 0)
                return;

            for (int i = hitIndex + 1; i < LaneBuffer.Count; i++)
            {
                if (LaneBuffer[i].Enemy.Brain is IAllyHitListener listener)
                    listener.OnAllyHit(hitEnemy, i - hitIndex);
            }
        }

        /// <summary>Marks the enemy as leaving the line so the next one is promoted immediately.</summary>
        public static void SetYielding(EnemyCharacter enemy, bool yielding)
        {
            if (ReferenceEquals(enemy, null)
                || !Memberships.TryGetValue(enemy, out Group group)
                || !group.Entries.TryGetValue(enemy, out Entry entry))
            {
                return;
            }

            entry.Yielding = yielding;
        }

        /// <summary>Spacing this enemy wants between itself and the enemy in front of it (default 0.2).</summary>
        public static void SetQueueGap(EnemyCharacter enemy, float gap)
        {
            if (ReferenceEquals(enemy, null)
                || !Memberships.TryGetValue(enemy, out Group group)
                || !group.Entries.TryGetValue(enemy, out Entry entry))
            {
                return;
            }

            entry.QueueGap = Mathf.Max(0f, gap);
        }

        /// <summary>
        /// Reserves the group's single "someone is airborne" window. False while another
        /// member reserved it less than <paramref name="cooldown"/> seconds ago.
        /// </summary>
        public static bool TryReserveLeap(EnemyCharacter enemy, float cooldown)
        {
            if (!Memberships.TryGetValue(enemy, out Group group))
                return false;
            if (Time.time < group.NextLeapAllowedTime)
                return false;

            group.NextLeapAllowedTime = Time.time + Mathf.Max(0f, cooldown);
            return true;
        }

        /// <summary>Brains call this with successfully applied hits only.</summary>
        public static void ReportHitTaken(EnemyCharacter enemy)
        {
            if (!ReferenceEquals(enemy, null) && Memberships.TryGetValue(enemy, out Group group))
                group.Pressure.Record(Time.time);
        }

        /// <summary>Delay between one attacker releasing the lease and the next acquiring it.</summary>
        public static void SetAttackHandoffDelay(EnemyCharacter enemy, float delay)
        {
            if (!ReferenceEquals(enemy, null) && Memberships.TryGetValue(enemy, out Group group))
                group.AttackHandoffDelay = Mathf.Max(0f, delay);
        }

        // ============================================================
        // INTERNALS
        // ============================================================

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

            BuildLane(group, requestingEntry, axisKind, axis, side, requestY);
            if (LaneBuffer.Count == 0)
                return false;

            int index = LaneBuffer.IndexOf(requestingEntry);
            if (index < 0)
                return false;

            Vector3 destination = BuildDestination(
                enemy,
                group.Target.transform.position,
                axis,
                side,
                GetSlotDistance(LaneBuffer, index, axisKind));
            assignment = new EnemyEngagementAssignment(destination, index);
            return true;
        }

        /// <summary>
        /// Fills and sorts <see cref="LaneBuffer"/> with every valid lane member on
        /// the given side. The requesting entry (if any) is always included so its
        /// own validity cannot drop it from its own query.
        /// </summary>
        private static void BuildLane(
            Group group,
            Entry requestingEntry,
            AxisKind axisKind,
            Vector3 axis,
            int side,
            float requestY)
        {
            LaneBuffer.Clear();
            foreach (Entry entry in group.Entries.Values)
            {
                if (entry != requestingEntry && !IsLaneMember(entry, group, axisKind, axis, side, requestY))
                    continue;

                LaneBuffer.Add(entry);
            }

            LaneComparer.Target = group.Target;
            LaneComparer.Axis = axis;
            LaneBuffer.Sort(LaneComparer);
        }

        private static int CountSide(
            Group group,
            Entry excluded,
            AxisKind axisKind,
            Vector3 axis,
            int side,
            float requestY)
        {
            int count = 0;
            foreach (Entry entry in group.Entries.Values)
            {
                if (entry == excluded)
                    continue;
                if (IsLaneMember(entry, group, axisKind, axis, side, requestY))
                    count++;
            }

            return count;
        }

        private static bool IsLaneMember(
            Entry entry,
            Group group,
            AxisKind axisKind,
            Vector3 axis,
            int side,
            float requestY)
        {
            return IsValidForGroup(entry.Enemy, group.Target)
                && IsHoldingPlace(entry.Enemy)
                && entry.Enemy.Brain is IChaseDestinationProvider
                && GetAxisKind(entry.Enemy) == axisKind
                && GetSide(entry, group.Target, axis) == side
                && Mathf.Abs(entry.Enemy.transform.position.y - requestY) <= LaneVerticalTolerance;
        }

        /// <summary>Axis distance from the player for the slot at <paramref name="index"/> of a sorted lane.</summary>
        private static float GetSlotDistance(List<Entry> lane, int index, AxisKind axisKind)
        {
            float slotDistance = GetAttackDistance(lane[0].Enemy);
            for (int i = 1; i <= index; i++)
            {
                slotDistance += GetHalfWidth(lane[i - 1], axisKind)
                    + GetHalfWidth(lane[i], axisKind)
                    + Mathf.Max(lane[i - 1].QueueGap, lane[i].QueueGap);
            }

            return slotDistance;
        }

        private static int CompareLaneEntries(
            Entry a,
            Entry b,
            PlayerCharacter target,
            Vector3 axis)
        {
            // A yielding enemy is leaving the line: it always sorts behind the others.
            if (a.Yielding != b.Yielding)
                return a.Yielding ? 1 : -1;

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

        /// <summary>
        /// True when another enemy could attack right now. <paramref name="requiredSide"/>
        /// 0 accepts any side; otherwise only candidates on that side count.
        /// </summary>
        private static bool HasReadyAlternative(Group group, EnemyCharacter excluded, int requiredSide)
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

                    if (requiredSide != 0
                        && GetSide(entry, group.Target, GetAxis(GetAxisKind(candidate))) != requiredSide)
                    {
                        continue;
                    }

                    float distance = candidate.Movement.GetAbsAxisDistance(
                        candidate.transform.position,
                        candidate.Target.position);
                    if (distance > GetAttackDistance(candidate) + AttackReadyTolerance)
                        continue;
                }
                else if (requiredSide != 0)
                {
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
                || state is WaitForOpeningState
                || state is HoldPositionState;
        }

        private static bool IsValidForGroup(EnemyCharacter enemy, PlayerCharacter target)
        {
            return IsValidParticipant(enemy) && enemy.TargetCharacter == target;
        }

        /// <summary>
        /// False for an enemy that is not holding its place in the line (stunned, parried,
        /// knocked down, standing idle after a get-up, alerting, searching, patrolling,
        /// dead). It is no slot, leader or follower for anyone else, so the others walk
        /// through it (bodies do not collide) instead of queueing behind it forever. The
        /// enemy asking is always in its own lane, so its own queries and the ally-hit
        /// ripple still work while it is stunned.
        /// </summary>
        private static bool IsHoldingPlace(EnemyCharacter enemy)
        {
            IState state = enemy.StateMachine != null ? enemy.StateMachine.CurrentState : null;
            return !(state is StunnedState
                || state is ParriedState
                || state is LaunchedState
                || state is IdleState
                || state is AlertState
                || state is SearchState
                || state is PatrolState
                || state is DeadState);
        }

        /// <summary>Leaping or launched bodies are not in the line: no slot, no neighbour.</summary>
        private static bool IsAirborne(EnemyCharacter enemy)
        {
            IState state = enemy.StateMachine != null ? enemy.StateMachine.CurrentState : null;
            return state is LeapState || state is LaunchedState;
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
                group.NextAttackAllowedTime = Time.time + group.AttackHandoffDelay;
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
