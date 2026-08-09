// FormationMoveCommand.cs
// AoE4-style group move: one order for N units creates a formation group
// with a virtual leader, type-ranked formation spots, slowest-member group
// speed and a cohesion gate. See docs/Design/Navigation_And_Formations.md.
// Location: Assets/Scripts/Core/Commands/CommandTypes/FormationMoveCommand.cs

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Systems.Navigation;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// Everything the router / helper needs to turn one clicked destination
    /// into per-unit slot destinations: filtered units, world slots, and
    /// which units travel as formation members (cohesion + type gates).
    /// </summary>
    public struct FormationPlan
    {
        public List<Entity> Units;        // filtered movable units
        public List<float3> SlotWorld;    // final world-space slot per unit
        public List<bool> Member;         // true = travels as a formation member
        public List<bool> OnRampart;      // true = needs a layered move down first
        public List<float2> SlotLocal;    // leader-local slot offsets (right, forward)
        public float GroupSpeed;          // slowest member speed
        public float3 Centroid;
        public float3 Facing;             // unit-length travel direction
        public float3 Destination;        // walkable-snapped click point
        public byte FactionIdx;
    }

    /// <summary>
    /// Builds formation plans and executes formation move / attack-move
    /// orders. Layouts, rank layering and spacing follow AoE4 (see
    /// docs/Research/AoE4_Navigation_Study.md §3).
    /// </summary>
    public static class FormationMoveCommandHelper
    {
        /// <summary>Base spacing between unit centers (m).</summary>
        public const float Spacing = 2.0f;

        /// <summary>
        /// Execute a formation move (or attack-move) directly. Creates the
        /// persistent formation group consumed by FormationGroupSystem.
        /// </summary>
        public static void Execute(EntityManager em, IReadOnlyList<Entity> units,
            float3 destination, FormationShape shape, bool attackMove)
        {
            if (!BuildPlan(em, units, destination, shape, out var plan)) return;

            IssuePlanOrders(em, plan, attackMove);

            // Count formation members; a group of one is just a plain move.
            int memberCount = 0;
            for (int i = 0; i < plan.Units.Count; i++)
                if (plan.Member[i]) memberCount++;
            if (memberCount < 2) return;

            // Create the group entity with its virtual leader at the
            // members' centroid.
            var group = em.CreateEntity();
            em.AddComponentData(group, new FormationGroup
            {
                LeaderPos = new float3(plan.Centroid.x, 0f, plan.Centroid.z),
                Destination = plan.Destination,
                Facing = plan.Facing,
                GroupSpeed = plan.GroupSpeed,
                FactionIdx = plan.FactionIdx,
                Shape = shape,
                State = FormationGroup.StateMoving,
            });
            var members = em.AddBuffer<FormationMember>(group);
            for (int i = 0; i < plan.Units.Count; i++)
            {
                if (!plan.Member[i]) continue;
                members.Add(new FormationMember { Unit = plan.Units[i], Slot = plan.SlotLocal[i] });
            }

            // Attach the back-reference + group speed AFTER the per-unit
            // command execution (MoveCommandHelper.Execute strips any prior
            // formation membership).
            for (int i = 0; i < plan.Units.Count; i++)
            {
                if (!plan.Member[i]) continue;
                var unit = plan.Units[i];
                var stateData = new FormationMemberState { Group = group, Slot = plan.SlotLocal[i] };
                if (em.HasComponent<FormationMemberState>(unit))
                    em.SetComponentData(unit, stateData);
                else
                    em.AddComponentData(unit, stateData);

                var speed = new FormationSpeedOverride { Value = plan.GroupSpeed };
                if (em.HasComponent<FormationSpeedOverride>(unit))
                    em.SetComponentData(unit, speed);
                else
                    em.AddComponentData(unit, speed);
            }
        }

        /// <summary>
        /// Issue the per-unit orders of a plan WITHOUT creating a group.
        /// Used directly by the lockstep fallback (slot moves serialize as
        /// ordinary per-unit move commands).
        /// </summary>
        public static void IssuePlanOrders(EntityManager em, in FormationPlan plan, bool attackMove)
        {
            for (int i = 0; i < plan.Units.Count; i++)
            {
                var unit = plan.Units[i];
                if (plan.OnRampart[i])
                {
                    // Wall-top units climb down via an access point first.
                    CommandRouter.IssueLayeredMove(em, unit, plan.SlotWorld[i], 0,
                        CommandSource.System);
                    continue;
                }
                if (attackMove)
                    AttackMoveCommandHelper.Execute(em, unit, plan.SlotWorld[i]);
                else
                    MoveCommandHelper.Execute(em, unit, plan.SlotWorld[i]);
            }
        }

        /// <summary>
        /// Filter the candidate units, lay out the formation slots at the
        /// destination, assign units to slots (type-rank rows, minimal
        /// crossing within a row), and apply the cohesion / worker /
        /// rampart gates. Returns false when nothing is movable.
        /// </summary>
        public static bool BuildPlan(EntityManager em, IReadOnlyList<Entity> candidates,
            float3 destination, FormationShape shape, out FormationPlan plan)
        {
            plan = default;

            var units = new List<Entity>();
            var positions = new List<float3>();
            for (int i = 0; i < candidates.Count; i++)
            {
                var e = candidates[i];
                if (e == Entity.Null || !em.Exists(e)) continue;
                if (em.HasComponent<BuildingTag>(e)) continue;
                if (units.Contains(e)) continue;
                units.Add(e);
                positions.Add(em.HasComponent<LocalTransform>(e)
                    ? em.GetComponentData<LocalTransform>(e).Position
                    : float3.zero);
            }
            int count = units.Count;
            if (count == 0) return false;

            NavGridQuery.SnapToWalkable(destination, out var snapped, out var snapOk);
            if (snapOk) destination = snapped;

            float3 centroid = float3.zero;
            for (int i = 0; i < count; i++) centroid += positions[i];
            centroid /= count;

            // Facing = travel direction (AoE4: the formation faces its
            // direction of travel; the layout is perpendicular to it).
            float3 facing = destination - centroid;
            facing.y = 0f;
            facing = math.lengthsq(facing) > 1e-4f
                ? math.normalize(facing)
                : new float3(0f, 0f, 1f);
            float3 right = math.cross(new float3(0f, 1f, 0f), facing);

            // ── Layout: local slots front-rank-first, row-major. ──
            float pitch = shape == FormationShape.Staggered ? Spacing * 2f : Spacing;
            ComputeRows(count, shape, out var rowCounts);
            var slotLocal = new List<float2>(count);
            var slotRowOf = new List<int>(count);
            for (int r = 0; r < rowCounts.Count; r++)
            {
                int rc = rowCounts[r];
                float half = (rc - 1) * 0.5f;
                // Staggered: offset alternate rows half a step so no unit
                // stands directly behind another (anti-AoE, AoE4 rule).
                float stagger = (shape == FormationShape.Staggered && (r & 1) == 1)
                    ? pitch * 0.5f : 0f;
                for (int c = 0; c < rc; c++)
                {
                    slotLocal.Add(new float2((c - half) * pitch + stagger, -r * pitch));
                    slotRowOf.Add(r);
                }
            }

            // ── Assignment: sort units by formation rank (front→back type
            // layering), fill rows in order; within a row order both units
            // and slots laterally so paths don't cross. ──
            var order = new List<int>(count);
            for (int i = 0; i < count; i++) order.Add(i);
            var ranks = new int[count];
            var lateral = new float[count];
            for (int i = 0; i < count; i++)
            {
                ranks[i] = FormationRank(em, units[i]);
                float3 d = positions[i] - centroid;
                lateral[i] = d.x * right.x + d.z * right.z;
            }
            order.Sort((a, b) =>
            {
                int byRank = ranks[a].CompareTo(ranks[b]);
                if (byRank != 0) return byRank;
                int byLat = lateral[a].CompareTo(lateral[b]);
                if (byLat != 0) return byLat;
                return units[a].Index.CompareTo(units[b].Index); // deterministic tie-break
            });

            var unitSlot = new int[count];
            int cursor = 0, slotBase = 0;
            for (int r = 0; r < rowCounts.Count; r++)
            {
                int rc = rowCounts[r];
                // Units for this row, re-sorted laterally within the row.
                var rowUnits = new List<int>(rc);
                for (int k = 0; k < rc && cursor < count; k++, cursor++)
                    rowUnits.Add(order[cursor]);
                rowUnits.Sort((a, b) =>
                {
                    int byLat = lateral[a].CompareTo(lateral[b]);
                    if (byLat != 0) return byLat;
                    return units[a].Index.CompareTo(units[b].Index);
                });
                for (int k = 0; k < rowUnits.Count; k++)
                    unitSlot[rowUnits[k]] = slotBase + k; // row slots are x-ascending
                slotBase += rc;
            }

            // ── Per-unit slot worlds + gates. ──
            var slotWorld = new List<float3>(count);
            var slotOfUnit = new List<float2>(count);
            var member = new List<bool>(count);
            var onRampart = new List<bool>(count);
            float slowest = float.MaxValue;
            byte factionIdx = 0xFF;

            for (int i = 0; i < count; i++)
            {
                float2 s = slotLocal[unitSlot[i]];
                slotOfUnit.Add(s);
                slotWorld.Add(destination + right * s.x + facing * s.y);

                bool rampart = em.HasComponent<NavLayerIndex>(units[i])
                    && em.GetComponentData<NavLayerIndex>(units[i]).Layer == NavLayerIndex.LayerRampart;
                onRampart.Add(rampart);

                float3 fromCentroid = positions[i] - centroid;
                fromCentroid.y = 0f;
                bool inCohesion = math.lengthsq(fromCentroid)
                    <= FormationGroup.CohesionRadius * FormationGroup.CohesionRadius;

                bool isMember = inCohesion && !rampart && !IsWorker(em, units[i]);
                member.Add(isMember);

                if (isMember)
                {
                    float sp = em.HasComponent<MoveSpeed>(units[i])
                        ? em.GetComponentData<MoveSpeed>(units[i]).Value : 3.5f;
                    if (sp > 0f && sp < slowest) slowest = sp;
                    if (factionIdx == 0xFF && em.HasComponent<FactionTag>(units[i]))
                    {
                        int f = (int)em.GetComponentData<FactionTag>(units[i]).Value;
                        if (f >= 0 && f <= 7) factionIdx = (byte)f;
                    }
                }
            }
            if (slowest <= 0f || slowest == float.MaxValue) slowest = 3.5f;

            plan = new FormationPlan
            {
                Units = units,
                SlotWorld = slotWorld,
                SlotLocal = slotOfUnit,
                Member = member,
                OnRampart = onRampart,
                GroupSpeed = slowest,
                Centroid = centroid,
                Facing = facing,
                Destination = destination,
                FactionIdx = factionIdx,
            };
            return true;
        }

        /// <summary>
        /// Row sizes front→back for a shape (AoE4 §3.1):
        ///   Box / Staggered — rectangle ~2:1 width:depth in unit counts.
        ///   Line            — 1 rank up to 12 units, 2 ranks beyond.
        ///   Wedge           — pyramid 1, 3, 5, ...
        /// </summary>
        internal static void ComputeRows(int count, FormationShape shape, out List<int> rowCounts)
        {
            rowCounts = new List<int>();
            switch (shape)
            {
                case FormationShape.Line:
                {
                    int depth = count <= 12 ? 1 : 2;
                    int width = (count + depth - 1) / depth;
                    int left = count;
                    for (int r = 0; r < depth && left > 0; r++)
                    {
                        int rc = math.min(width, left);
                        rowCounts.Add(rc);
                        left -= rc;
                    }
                    return;
                }
                case FormationShape.Wedge:
                {
                    int left = count;
                    int rc2 = 1;
                    while (left > 0)
                    {
                        int take = math.min(rc2, left);
                        rowCounts.Add(take);
                        left -= take;
                        rc2 += 2;
                    }
                    return;
                }
                default: // Box, Staggered: width ≈ 2 × depth
                {
                    int depth = (int)math.ceil(math.sqrt(count / 2f));
                    if (depth < 1) depth = 1;
                    int width = (count + depth - 1) / depth;
                    int left = count;
                    while (left > 0)
                    {
                        int rc = math.min(width, left);
                        rowCounts.Add(rc);
                        left -= rc;
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// AoE4 front→back type layering (study §3.2), collapsed onto this
        /// game's unit classes: cavalry/scouts, melee, ranged, siege,
        /// support/magic, workers.
        /// </summary>
        internal static int FormationRank(EntityManager em, Entity e)
        {
            if (em.HasComponent<CavalryTag>(e)) return 0;
            if (em.HasComponent<UnitTag>(e))
            {
                switch (em.GetComponentData<UnitTag>(e).Class)
                {
                    case UnitClass.Scout: return 0;
                    case UnitClass.Melee: return 1;
                    case UnitClass.Ranged: return 2;
                    case UnitClass.Siege: return 3;
                    case UnitClass.Support:
                    case UnitClass.Magic: return 4;
                    case UnitClass.Economy:
                    case UnitClass.Miner: return 5;
                }
            }
            return 1;
        }

        /// <summary>Villagers / worker units never form up (AoE4 rule) —
        /// they still receive their slot destination, just no membership.</summary>
        internal static bool IsWorker(EntityManager em, Entity e)
        {
            if (em.HasComponent<MinerTag>(e)) return true;
            if (em.HasComponent<CanBuild>(e)) return true;
            if (em.HasComponent<UnitTag>(e))
            {
                var c = em.GetComponentData<UnitTag>(e).Class;
                if (c == UnitClass.Economy || c == UnitClass.Miner) return true;
            }
            return false;
        }
    }
}
