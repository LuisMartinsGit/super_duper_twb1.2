// FormationMoveCommand.cs
// AoE4-style group move: one order for N units creates a formation group
// with a virtual leader, type-ranked formation spots, slowest-member group
// speed and a cohesion gate. See docs/Design/Navigation_And_Formations.md.

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
        public List<int> SlotIndex;       // layout slot index per unit
        public uint LayoutKey;            // identifies the layout SlotIndex belongs to
        public float Radius;              // leader -> outermost slot (m)
        public float3 StartFacing;        // pose the group STARTS in
        public float3 StartLeader;
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
        /// Distance between two TYPE BLOCKS, in row pitches. 2 means the bow
        /// line sits two unit-widths behind the shield wall instead of one.
        /// Also the LATERAL gap out to a cavalry wing, so the whole formation
        /// reads at one spacing rather than two.
        ///
        /// The separation is tactical, not cosmetic: it is what stops a charge
        /// that reaches the front rank from arriving among the archers in the
        /// same moment, and it leaves the melee room to fall back through.
        /// </summary>
        public const float TypeGapPitches = 2f;

        /// <summary>Widest a block's row gets before it grows another rank.</summary>
        private const int MaxBlockRowWidth = 8;

        /// <summary>
        /// Formation ranks, FRONT TO BACK. The order is the layout: rank 0
        /// stands nearest the enemy and each successive rank falls in behind
        /// the last.
        ///
        /// Heroes lead. The cavalry screen comes next and also covers the
        /// flanks. Then the line, then the bow, then the healers, and the
        /// siege last — far enough back that a charge reaching the front rank
        /// is nowhere near it, with the healers between the two so they can
        /// reach the line without standing in it.
        ///
        /// Siege used to sit AHEAD of support, which put engines in front of
        /// the people meant to be protected by everything else.
        /// </summary>
        public const int RankHero    = 0;
        public const int RankCavalry = 1;
        public const int RankMelee   = 2;
        public const int RankRanged  = 3;
        public const int RankSupport = 4;
        public const int RankSiege   = 5;
        public const int RankEconomy = 6;
        public const int RankCount   = 7;

        /// <summary>
        /// Spacing multiplier for a rank. Siege pieces are physically large and
        /// wreck a line if packed at infantry spacing — a catapult is not a
        /// spearman with more health. Double pitch gives an engine its own
        /// footprint and room to be worked.
        /// </summary>
        public static float RankSpacingScale(int rank)
            => rank == RankSiege ? SiegeSpacingScale : 1f;

        private const float SiegeSpacingScale = 2f;

        /// <summary>
        /// The formation's shape for a given rank census: leader-local slot
        /// offsets, plus the run-length breakdown the assignment fills them in.
        ///
        /// Shared so the layout is derived in exactly ONE place. The octagon
        /// scenario used to hand-mirror this arithmetic to spawn its army
        /// already in formation, which was a second copy of the block stack,
        /// the type gap and the centring — and a third would have been needed
        /// for per-rank spacing. A spawn that disagrees with the layout by more
        /// than the pose-fit tolerance makes the first order snap instead of
        /// continue, so the copies drifting is not cosmetic.
        /// </summary>
        /// <param name="census">Unit count per rank, indexed by Rank*.</param>
        /// <param name="groupRank">Rank each group belongs to, so a caller that
        /// is placing units (rather than assigning them) knows what to put
        /// where.</param>
        public static void BuildLayout(int[] census, FormationShape shape,
            out List<float2> slotLocal, out List<int> groupCounts,
            out List<int> groupRank)
        {
            slotLocal = new List<float2>();
            groupCounts = new List<int>();
            groupRank = new List<int>();

            int total = 0;
            for (int r = 0; r < census.Length; r++) total += census[r];
            if (total <= 0) return;

            float basePitch = shape == FormationShape.Staggered ? Spacing * 2f : Spacing;

            // ── CAVALRY SCREENS THE FRONT AND COVERS THE FLANKS. ──
            //
            // Half ride in front, a quarter cover each flank. The split rounds
            // TOWARD THE FRONT, because a wing of one is not a wing: with two
            // knights both ride ahead, and the wings only appear at four, where
            // there is one to spare for each side. Wings need a body to screen,
            // so all-cavalry keeps its block.
            int cav = census[RankCavalry];
            int wingEach = (cav >= 4 && cav < total) ? cav / 4 : 0;
            int cavFront = cav - 2 * wingEach;

            // ── The front-to-back block stack. ──
            var rowCounts = new List<int>();
            var rowY = new List<float>();
            var rowRank = new List<int>();
            var rowPitch = new List<float>();
            float cursorY = 0f;
            int lastRank = -1;
            for (int rank = 0; rank < census.Length; rank++)
            {
                int n = rank == RankCavalry ? cavFront : census[rank];
                if (n <= 0) continue;

                float rp = basePitch * RankSpacingScale(rank);

                // Gap from the previous block, sized on the ROOMIER of the two
                // so a siege train is not crowded by the healers in front of it.
                if (lastRank >= 0)
                    cursorY -= basePitch * (TypeGapPitches - 1f)
                        * math.max(RankSpacingScale(lastRank), RankSpacingScale(rank));

                ComputeBlockRows(n, shape, out var blockRows);
                for (int r = 0; r < blockRows.Count; r++)
                {
                    rowCounts.Add(blockRows[r]);
                    rowY.Add(cursorY);
                    rowRank.Add(rank);
                    rowPitch.Add(rp);
                    cursorY -= rp;
                }
                lastRank = rank;
            }

            // CENTRE THE WHOLE LAYOUT ON THE LEADER along the travel axis.
            //
            // Rows would otherwise hang entirely BEHIND the leader, while the
            // leader starts at the group's own centroid — so a squad already
            // standing in the ordered shape is told to shift a full block-depth
            // BACKWARDS before travelling anywhere. Centred, a formed group
            // maps onto its slots exactly: no correction, no catch-up, no
            // drift, and it arrives centred on the click rather than with its
            // front rank there.
            float meanY = 0f;
            for (int r = 0; r < rowY.Count; r++) meanY += rowY[r];
            if (rowY.Count > 0) meanY /= rowY.Count;

            // Widest the body gets — what a wing has to stand clear of.
            float halfWidth = 0f;
            for (int r = 0; r < rowCounts.Count; r++)
            {
                float w = (rowCounts[r] - 1) * 0.5f * rowPitch[r];
                if (shape == FormationShape.Staggered && (r & 1) == 1)
                    w += rowPitch[r] * 0.5f;
                if (w > halfWidth) halfWidth = w;
            }

            // Slots are emitted in RANK ORDER, and the assignment fills them in
            // that same order from a rank-sorted unit list — so the wings are
            // emitted WITH the cavalry, not appended at the end, or the wing
            // slots would be handed to whatever rank came last.
            int rIdx = 0;
            for (int rank = 0; rank < census.Length; rank++)
            {
                while (rIdx < rowRank.Count && rowRank[rIdx] == rank)
                {
                    int rc = rowCounts[rIdx];
                    float rp = rowPitch[rIdx];
                    float half = (rc - 1) * 0.5f;
                    // Staggered: offset alternate rows half a step so no unit
                    // stands directly behind another (anti-AoE, AoE4 rule).
                    float stagger = (shape == FormationShape.Staggered && (rIdx & 1) == 1)
                        ? rp * 0.5f : 0f;
                    for (int c = 0; c < rc; c++)
                        slotLocal.Add(new float2((c - half) * rp + stagger,
                            rowY[rIdx] - meanY));
                    groupCounts.Add(rc);
                    groupRank.Add(rank);
                    rIdx++;
                }

                if (rank != RankCavalry || wingEach <= 0) continue;

                // A column down each flank, at the SAME spacing that separates
                // the blocks front to back, so the formation reads at one
                // spacing in both axes. Centred on y = 0, the body's centre by
                // construction after the centring above.
                float wingX = halfWidth + TypeGapPitches * basePitch;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    for (int k = 0; k < wingEach; k++)
                        slotLocal.Add(new float2(sgn * wingX,
                            (k - (wingEach - 1) * 0.5f) * basePitch));
                    groupCounts.Add(wingEach);
                    groupRank.Add(RankCavalry);
                }
            }
        }

        /// <summary>
        /// How far off its remembered lattice (RMS, metres) an army may be and
        /// still be treated as standing IN that formation, so a new order
        /// continues from its current pose instead of snapping to the new
        /// bearing.
        ///
        /// A marching formation sits well inside a metre of its spots, so 2 m
        /// accepts every real continuation while comfortably rejecting a blob
        /// that has never formed up — which must snap, or it would wheel away
        /// from the destination before setting off.
        /// </summary>
        private const float PoseFitTolerance = 2.0f;

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
                // The pose the army is ALREADY in, not the bearing to the new
                // destination - see the Procrustes fit in BuildPlan. Starting
                // here is what turns a corner into a wheel instead of a
                // 45-degree teleport of every spot in the formation.
                LeaderPos = plan.StartLeader,
                Destination = plan.Destination,
                Facing = plan.StartFacing,
                Radius = plan.Radius,
                GroupSpeed = plan.GroupSpeed,
                FactionIdx = plan.FactionIdx,
                Shape = shape,
                State = FormationGroup.StateMoving,
                BestLag = float.MaxValue,
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

                // Remember the slot so the NEXT order can keep this unit in it.
                // Written after IssuePlanOrders on purpose: the per-unit move
                // commands strip formation state, and this has to outlive that.
                var mem = new FormationSlotMemory
                {
                    Slot = plan.SlotIndex[i],
                    LayoutKey = plan.LayoutKey,
                };
                if (em.HasComponent<FormationSlotMemory>(unit))
                    em.SetComponentData(unit, mem);
                else
                    em.AddComponentData(unit, mem);
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

            // ── Layout: one BLOCK PER UNIT TYPE, front to back, with a gap
            //    between blocks. ──
            //
            // The layout used to be a single rectangle sized from the total
            // head count, and the type layering only decided the ORDER units
            // were poured into it. A 9-melee + 6-ranged army therefore got a
            // 5x3 box whose second row was four spearmen and one archer: the
            // line had a hole in it and an archer stood in the shield wall.
            //
            // Each type now gets its own block, sized on its own count, and the
            // blocks are separated by a real gap. The gap is the point — it is
            // what stops a charge that reaches the front rank from also
            // reaching the bow line, and it gives the melee somewhere to fall
            // back through.
            float pitch = shape == FormationShape.Staggered ? Spacing * 2f : Spacing;

            // Rank per unit, needed BEFORE the layout now: the blocks are built
            // from the type census, not from the total.
            var ranks = new int[count];
            var lateral = new float[count];
            var along = new float[count];
            for (int i = 0; i < count; i++)
            {
                ranks[i] = FormationRank(em, units[i]);
                float3 d = positions[i] - centroid;
                lateral[i] = d.x * right.x + d.z * right.z;
                along[i] = d.x * facing.x + d.z * facing.z;
            }

            var census = new int[RankCount];
            for (int i = 0; i < count; i++)
                census[math.clamp(ranks[i], 0, RankCount - 1)]++;
            BuildLayout(census, shape, out var slotLocal, out var groupCounts, out _);

            // Identifies THIS layout, so a remembered slot index can only be
            // reused while the shape it indexes into is unchanged. Shape plus
            // the row breakdown covers everything that moves a slot: lose a
            // unit and a block resizes, add a type and the gap appears.
            uint layoutKey = 2166136261u;
            layoutKey = (layoutKey ^ (uint)(int)shape) * 16777619u;
            for (int r = 0; r < groupCounts.Count; r++)
                layoutKey = (layoutKey ^ (uint)groupCounts[r]) * 16777619u;

            // How far the outermost slot sits from the leader. The formation's
            // own radius, used below to size the cohesion allowance.
            float layoutRadius = 0f;
            for (int i = 0; i < slotLocal.Count; i++)
                layoutRadius = math.max(layoutRadius, math.length(slotLocal[i]));

            // ── Assignment: type layering first, then PRESERVE THE ORDER THE
            // UNITS ARE ALREADY IN so nobody has to cross anybody. ──
            //
            // Rows are filled front-rank-first, so whoever is sorted first gets
            // the slot nearest the destination. That means the sort has to know
            // which units are already nearest the destination — and it did not.
            // It ordered by unit TYPE and then by LATERAL offset, with the
            // entity index as the only tie-break, so a squad of four identical
            // archers laid out
            //
            //     A B
            //     C D          (order: move "down", toward C/D and beyond)
            //
            // split front/back by entity id rather than by position. A and B —
            // the two furthest from the destination — could be handed the FRONT
            // rank, which put their slots beyond C and D, and C and D the back
            // rank, which put their slots behind A and B. Both pairs then walked
            // through each other. They collide, separation shoves them apart,
            // the whole group crawls, and archers ordered to kite die to the
            // spearmen they were running from.
            //
            // Sorting by distance along the travel direction (furthest along
            // first) makes the assignment order-preserving for a rank-and-file
            // layout: the leading pair takes the leading rank, the trailing pair
            // takes the trailing rank, nobody swaps sides, and no path crosses
            // another. Lateral order within the row does the same job across
            // the row, and was already correct.
            //
            // Type rank still outranks position, deliberately: a melee unit
            // ordered to the front SHOULD walk past the archers. That is the
            // formation doing its job, not units tripping over each other.
            var order = new List<int>(count);
            for (int i = 0; i < count; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int byRank = ranks[a].CompareTo(ranks[b]);
                if (byRank != 0) return byRank;
                // Descending: the unit furthest ALONG the travel direction is
                // already closest to the destination, so it takes the front rank.
                int byAlong = along[b].CompareTo(along[a]);
                if (byAlong != 0) return byAlong;
                int byLat = lateral[a].CompareTo(lateral[b]);
                if (byLat != 0) return byLat;
                return units[a].Index.CompareTo(units[b].Index); // deterministic tie-break
            });

            var unitSlot = new int[count];
            for (int i = 0; i < count; i++) unitSlot[i] = -1;
            var slotTaken = new bool[slotLocal.Count];

            // PASS 1 — A UNIT KEEPS THE PLACE IT ALREADY HOLDS.
            //
            // Without this the assignment is re-derived from live positions on
            // every order, and "live positions" are measured along the NEW
            // travel axis. Turn an army 45 degrees and every unit's `along`
            // changes, so the ordering inside each rank changes, so everyone is
            // handed a different slot and the whole army trades places to reach
            // it. Eight turns around an octagon meant eight full reshuffles —
            // ranks dissolving and re-forming rather than a formation turning.
            //
            // Keeping the slot makes a turn a RIGID ROTATION of the shape: each
            // unit walks a short arc to the same place in the same rank, which
            // is what "the army moved as one" looks like.
            //
            // Read from FormationSlotMemory, NOT FormationMemberState. The
            // latter is stripped whenever a group dissolves — which includes
            // arriving, i.e. every corner — so it was empty exactly when this
            // pass needed it and the preservation never once fired. And matched
            // by INDEX, not by offset: ResolveSlot nudges offsets onto standable
            // ground, so two identical orders produce offsets differing by
            // centimetres and a 1e-4 offset compare never matched either.
            //
            // LayoutKey is the guard. If the shape changed, the indices mean
            // something else and the assignment falls through to a clean
            // rebuild below.
            for (int i = 0; i < count; i++)
            {
                if (!em.HasComponent<FormationSlotMemory>(units[i])) continue;
                var mem = em.GetComponentData<FormationSlotMemory>(units[i]);
                if (mem.LayoutKey != layoutKey) continue;
                if (mem.Slot < 0 || mem.Slot >= slotLocal.Count) continue;
                if (slotTaken[mem.Slot]) continue;
                unitSlot[i] = mem.Slot;
                slotTaken[mem.Slot] = true;
            }

            // PASS 2 — everyone still unplaced, filled front-rank-first in the
            // sorted order, laterally within each row so paths do not cross.
            int cursor = 0, slotBase = 0;
            for (int r = 0; r < groupCounts.Count; r++)
            {
                int rc = groupCounts[r];
                var freeSlots = new List<int>(rc);
                for (int k = 0; k < rc; k++)
                    if (!slotTaken[slotBase + k]) freeSlots.Add(slotBase + k);

                if (freeSlots.Count > 0)
                {
                    var rowUnits = new List<int>(freeSlots.Count);
                    while (rowUnits.Count < freeSlots.Count && cursor < count)
                    {
                        int u = order[cursor++];
                        if (unitSlot[u] < 0) rowUnits.Add(u);
                    }
                    rowUnits.Sort((a, b) =>
                    {
                        int byLat = lateral[a].CompareTo(lateral[b]);
                        if (byLat != 0) return byLat;
                        return units[a].Index.CompareTo(units[b].Index);
                    });
                    for (int k = 0; k < rowUnits.Count; k++)
                    {
                        unitSlot[rowUnits[k]] = freeSlots[k];   // row slots are x-ascending
                        slotTaken[freeSlots[k]] = true;
                    }
                }
                slotBase += rc;
            }

            // Anything still unplaced (more units than slots can only happen if
            // the census and the layout disagree) takes whatever is left, so no
            // unit is ever dropped on the floor with slot -1.
            for (int i = 0; i < count; i++)
            {
                if (unitSlot[i] >= 0) continue;
                for (int sIdx = 0; sIdx < slotTaken.Length; sIdx++)
                {
                    if (slotTaken[sIdx]) continue;
                    unitSlot[i] = sIdx; slotTaken[sIdx] = true; break;
                }
                if (unitSlot[i] < 0) unitSlot[i] = 0;
            }

            // ── Per-unit slot worlds + gates. ──
            var slotWorld = new List<float3>(count);
            var slotOfUnit = new List<float2>(count);
            var member = new List<bool>(count);
            var onRampart = new List<bool>(count);
            float slowest = float.MaxValue;
            byte factionIdx = 0xFF;

            // Build cells already promised to a slot. The layout is authored
            // geometry — it knows nothing about the terrain it lands on, so
            // ordering a formation onto a shoreline or a wall put a share of
            // its slots inside impassable ground. Those units could never
            // arrive, and worse, every one of them was individually snapped to
            // the SAME nearest walkable cell by MoveCommandHelper, so they
            // piled onto one point and churned. Resolve each slot to distinct,
            // standable ground here instead.
            var claimedSlots = new List<float2>(count);

            for (int i = 0; i < count; i++)
            {
                float2 s = slotLocal[unitSlot[i]];
                float3 want = destination + right * s.x + facing * s.y;
                float3 resolved = ResolveSlot(want, claimedSlots);

                // Re-derive the LEADER-LOCAL offset from where the slot
                // actually ended up. FormationGroupSystem steers each member to
                // LeaderPos + right*s.x + Facing*s.y, so leaving the old offset
                // here would aim the moving spot at the impassable ground we
                // just moved the destination off — the unit would be steered
                // one way and ordered another.
                float3 delta = resolved - destination;
                s = new float2(
                    delta.x * right.x + delta.z * right.z,
                    delta.x * facing.x + delta.z * facing.z);

                slotOfUnit.Add(s);
                slotWorld.Add(resolved);

                bool rampart = em.HasComponent<NavLayerIndex>(units[i])
                    && em.GetComponentData<NavLayerIndex>(units[i]).Layer == NavLayerIndex.LayerRampart;
                onRampart.Add(rampart);

                // COHESION IS SLACK AROUND THE SHAPE, NOT AROUND A POINT.
                //
                // A flat 12 m from the centroid was sized when a formation was
                // a small square. A 15-unit army with a type gap is 10 m deep
                // and 8 m wide on its own, so the archer block already stands
                // 5 m back before anybody has lagged a step — leaving 7 m of
                // allowance for the rear rank and 12 m for the middle. Any unit
                // that fell outside was quietly dropped from the group
                // (isMember = false) and walked to its slot on its own, which
                // is precisely "it moved as two armies": the formation kept the
                // units near the centre and abandoned the rest.
                //
                // The allowance is now measured from where the unit is SUPPOSED
                // to stand, so every member gets the same slack regardless of
                // how big the formation is or where in it they belong.
                float3 fromCentroid = positions[i] - centroid;
                fromCentroid.y = 0f;
                float cohesionLimit = FormationGroup.CohesionRadius + layoutRadius;
                bool inCohesion = math.lengthsq(fromCentroid) <= cohesionLimit * cohesionLimit;

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

            // ── STARTING POSE: where the army ALREADY STANDS. ──
            //
            // Facing above is the bearing to the destination, and using it as
            // the group's starting pose is what made a corner a mess. The
            // spots are computed live as LeaderPos + right*s.x + Facing*s.y,
            // so snapping Facing 45 degrees on the frame the order lands
            // teleports the whole lattice: every spot jumps metres sideways,
            // every member is instantly out of formation, and the squad
            // scrambles to a shape that has already moved again by the time it
            // gets there.
            //
            // Fit the remembered lattice to the live positions instead and
            // start the group in the pose the army is actually in. Then the
            // only thing left to do is TURN, which is the leader's job and is
            // rate-limited to something the outer flank can follow.
            //
            // Closed-form 2D Procrustes. With world = right*s.x + facing*s.y
            // and right = (facing.z, -facing.x), the residual is linear in
            // facing, so the best unit-length facing is just the normalised
            // accumulator below — no iteration, no trig, fully deterministic.
            float3 startFacing = facing;
            float3 startLeader = centroid;
            {
                float3 pMean = float3.zero;
                float2 sMean = float2.zero;
                int n = 0;
                for (int i = 0; i < count; i++)
                {
                    if (!member[i]) continue;
                    pMean += positions[i];
                    sMean += slotOfUnit[i];
                    n++;
                }
                if (n >= 2)
                {
                    pMean /= n;
                    sMean /= n;

                    float sumA = 0f, sumB = 0f;
                    for (int i = 0; i < count; i++)
                    {
                        if (!member[i]) continue;
                        float qx = positions[i].x - pMean.x;
                        float qz = positions[i].z - pMean.z;
                        float sx = slotOfUnit[i].x - sMean.x;
                        float sy = slotOfUnit[i].y - sMean.y;
                        sumA += qx * sy - qz * sx;
                        sumB += qx * sx + qz * sy;
                    }

                    float mag = math.sqrt(sumA * sumA + sumB * sumB);
                    if (mag > 1e-3f)
                    {
                        float3 fitFace = new float3(sumA / mag, 0f, sumB / mag);
                        float3 fitRight = math.cross(new float3(0f, 1f, 0f), fitFace);
                        float3 fitLeader = pMean
                            - (fitRight * sMean.x + fitFace * sMean.y);
                        fitLeader.y = 0f;

                        // ONLY IF THE ARMY IS ACTUALLY IN THIS SHAPE. A blob of
                        // units that has never formed up fits the lattice at
                        // some arbitrary angle, and starting the group pointing
                        // that way would have it wheel away from the
                        // destination before setting off. Measure the fit and
                        // fall back to the destination bearing when it is poor
                        // — which is the right behaviour for a first order:
                        // face the target and form up.
                        float err2 = 0f;
                        for (int i = 0; i < count; i++)
                        {
                            if (!member[i]) continue;
                            float3 want = fitLeader
                                + fitRight * slotOfUnit[i].x
                                + fitFace * slotOfUnit[i].y;
                            float dx = want.x - positions[i].x;
                            float dz = want.z - positions[i].z;
                            err2 += dx * dx + dz * dz;
                        }
                        if (math.sqrt(err2 / n) <= PoseFitTolerance)
                        {
                            startFacing = fitFace;
                            startLeader = fitLeader;
                        }
                    }
                }
            }

            var slotIndexOf = new List<int>(count);
            for (int i = 0; i < count; i++) slotIndexOf.Add(unitSlot[i]);

            plan = new FormationPlan
            {
                SlotIndex = slotIndexOf,
                LayoutKey = layoutKey,
                Radius = layoutRadius,
                StartFacing = startFacing,
                StartLeader = startLeader,
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

        /// <summary>How far out (in build cells) a displaced slot may look for
        /// standable ground before giving up.</summary>
        private const int SlotSearchRings = 6;

        /// <summary>
        /// Put a formation slot on ground a unit can actually stand on, and on
        /// a build cell no other slot has taken.
        ///
        /// Walks outward ring by ring from the authored position, so a slot
        /// pushed off a cliff lands as close to its intended place in the
        /// formation as the terrain allows. Deterministic (fixed ring and
        /// scan order, claims taken in unit-index order), so every lockstep
        /// client resolves the identical layout.
        ///
        /// Falls back to the authored slot when nothing is free within the
        /// search — better a crowded formation than a silently dropped unit;
        /// the movement stack's own arrival and stuck rules handle it from
        /// there.
        /// </summary>
        /// <summary>How close two slots may sit before they count as the same
        /// place. Just under the layout pitch, so the authored spacing always
        /// passes and only genuine overlaps are resolved.</summary>
        private const float MinSlotSeparation = Spacing * 0.7f;

        /// <summary>
        /// Give a slot standable ground of its own.
        ///
        /// Distinctness is measured in WORLD SPACE, not in build cells. The
        /// slot lattice is laid out along the travel direction, so on any order
        /// that is not axis-aligned it sits at an angle to the build grid — and
        /// two slots a full 2 m apart could then land in the SAME 2 m cell and
        /// be treated as a collision. At a 30-degree heading that hit two of a
        /// 3x3's nine slots. The displaced ones were shoved a whole cell
        /// sideways, so a square ordered straight ahead broke into a group
        /// going left and a group going right that only re-formed on the way.
        ///
        /// The ring search that resolved those collisions also scanned -dz then
        /// -dx first and took the first hit, so displacement was biased back
        /// and to the left rather than to the nearest free ground. It now takes
        /// the CLOSEST valid candidate, which keeps a genuinely blocked slot as
        /// near its authored position as the terrain allows and removes the
        /// directional bias entirely.
        /// </summary>
        private static float3 ResolveSlot(float3 want, List<float2> claimed)
        {
            if (NavGridQuery.IsWorldStandable(want) && !TooClose(want, claimed))
            {
                claimed.Add(new float2(want.x, want.z));
                return want;
            }

            int2 wantCell = BuildGrid.WorldToCell(want);
            bool haveBest = false;
            float bestDistSq = float.MaxValue;
            float3 best = want;

            for (int r = 1; r <= SlotSearchRings && !haveBest; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        // Ring only — the interior was covered by smaller r.
                        if (math.max(math.abs(dx), math.abs(dz)) != r) continue;

                        float2 c = BuildGrid.CellCentre(wantCell + new int2(dx, dz));
                        var candidate = new float3(c.x, want.y, c.y);
                        if (!NavGridQuery.IsWorldStandable(candidate)) continue;
                        if (TooClose(candidate, claimed)) continue;

                        float ddx = candidate.x - want.x, ddz = candidate.z - want.z;
                        float d2 = ddx * ddx + ddz * ddz;
                        if (d2 < bestDistSq) { bestDistSq = d2; best = candidate; haveBest = true; }
                    }
                }
            }

            claimed.Add(new float2(best.x, best.z));
            return best;
        }

        private static bool TooClose(float3 p, List<float2> claimed)
        {
            const float r2 = MinSlotSeparation * MinSlotSeparation;
            for (int i = 0; i < claimed.Count; i++)
            {
                float dx = p.x - claimed[i].x, dz = p.z - claimed[i].y;
                if (dx * dx + dz * dz < r2) return true;
            }
            return false;
        }

        /// <summary>
        /// Row sizes front→back for a shape (AoE4 §3.1):
        ///   Box / Staggered — rectangle ~2:1 width:depth in unit counts.
        ///   Line            — 1 rank up to 12 units, 2 ranks beyond.
        ///   Wedge           — pyramid 1, 3, 5, ...
        /// </summary>
        /// <summary>
        /// Rows for ONE type block, front to back and never growing toward the
        /// back — a line with more men in the second rank than the first reads
        /// as a mistake.
        ///
        /// Box / Staggered form a DOUBLE LINE: 9 melee become 5 + 4, 6 archers
        /// become 3 + 3. That is the shape a mixed army actually wants — wide
        /// enough to present a front, shallow enough that the back rank is not
        /// spectating — and it only grows a third rank once a block would
        /// otherwise be wider than MaxBlockRowWidth. Line and Wedge are
        /// explicit shape requests and keep their own geometry.
        /// </summary>
        internal static void ComputeBlockRows(int count, FormationShape shape,
            out List<int> rowCounts)
        {
            rowCounts = new List<int>();
            if (count <= 0) return;

            if (shape == FormationShape.Line || shape == FormationShape.Wedge)
            {
                ComputeRows(count, shape, out rowCounts);
                return;
            }

            // A handful stands in one rank; splitting three men into 2+1 is a
            // formation only in the arithmetic sense.
            if (count <= 3) { rowCounts.Add(count); return; }

            int rows = math.max(2, (count + MaxBlockRowWidth - 1) / MaxBlockRowWidth);
            int baseCount = count / rows;
            int extra = count % rows;
            for (int r = 0; r < rows; r++)
                rowCounts.Add(baseCount + (r < extra ? 1 : 0));   // front ranks take the remainder
        }

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
            // Heroes lead. UniqueUnitTag is the game's own hero marker —
            // "Marks a hero / one-per-player unique unit" — and it is tested
            // FIRST so a mounted hero leads the army rather than disappearing
            // into the cavalry screen.
            if (em.HasComponent<TheWaningBorder.Abilities.UniqueUnitTag>(e))
                return RankHero;
            if (em.HasComponent<CavalryTag>(e)) return RankCavalry;
            if (em.HasComponent<UnitTag>(e))
            {
                switch (em.GetComponentData<UnitTag>(e).Class)
                {
                    case UnitClass.Scout: return RankCavalry;
                    case UnitClass.Melee: return RankMelee;
                    case UnitClass.Ranged: return RankRanged;
                    case UnitClass.Siege: return RankSiege;
                    case UnitClass.Support:
                    case UnitClass.Magic: return RankSupport;
                    case UnitClass.Economy:
                    case UnitClass.Miner: return RankEconomy;
                }
            }
            return RankMelee;
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
