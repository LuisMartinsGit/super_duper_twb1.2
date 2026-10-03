// AIWallPlanner.cs
// Terrain-shelter assessment + wall-plan generation for the AI wall
// doctrine (AIAlanthorEndgameSystem phase 6b executes the plan).
//
// The doctrine mirrors the player thought process:
//   1. Am I sheltered by terrain — does ingress to my base mean going
//      through chokepoints?
//   2. If yes: wall off and fortify the chokepoints (the Fiendstone Keep
//      also stands there — see SimpleAISystem's choice-building hook).
//   3. If not: wall a LARGE square-ish area around what's important
//      (military production, Temple, the near Gatherer's Huts),
//      with a gate facing each cardinal direction and towers on the wall.
//
// The assessment is TERRAIN-ONLY (PassabilityGrid cell value ==
// TerrainBlocked: slope / water / NoWalk paint / mountain mask; map edge
// counts as blocked). Buildings and tree/rock obstacles never count as
// shelter — they can be razed.
//
// All scans are deterministic (fixed bearings, fixed step sizes, no RNG),
// so lockstep peers running the same plan agree.

using Unity.Collections;
using TheWaningBorder.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>Frozen wall plan for one AI faction. Stamped on the brain
    /// entity by the endgame system the first time the wall doctrine runs;
    /// never recomputed (the wall keeps building toward a stable shape).</summary>
    public struct AIWallPlan : IComponentData
    {
        /// <summary>One of the AIWallPlanner.Mode* values.</summary>
        public byte Mode;
        /// <summary>ModeBorder: the owned-territory set the plan was drawn
        /// for (AIWallPlanner.TerritorySignature). A different set means the
        /// border moved and the plan is redrawn.</summary>
        public uint Territories;
    }

    /// <summary>One planned hub position. Buffer order IS chain order —
    /// consecutive slots with the same Chain id are wall neighbours.</summary>
    public struct AIWallPlanSlot : IBufferElementData
    {
        public float3 Position;
        /// <summary>Chain id: corridor index in chokepoint mode, 0 for the
        /// perimeter loop.</summary>
        public byte Chain;
        /// <summary>AIWallPlanner.Flag* bits.</summary>
        public byte Flags;
        /// <summary>Orders issued for this slot's hub that have not (yet)
        /// produced one — a refusal the AI only learns of by its absence
        /// (under lockstep the executor's answer never comes back).</summary>
        public byte HubTries;
        /// <summary>Orders issued to link this slot to the NEXT live slot
        /// that have not (yet) connected the two.</summary>
        public byte LinkTries;
    }

    /// <summary>
    /// Static planner: shelter scan, ingress-corridor extraction, chokepoint
    /// cross-sections, perimeter rectangle, and slot-list generation.
    /// </summary>
    public static class AIWallPlanner
    {
        #region Cached queries

        // CreateEntityQuery registers a new query with the world on EVERY
        // call; these run per think tick. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_BuildingTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_BuildingTagFactionTagLocalTransform;

        #endregion

        #region Tuning

        /// <summary>Every number below used to be a const in this file;
        /// they live in AIWallPlanner.asset now.</summary>
        static AIWallPlannerConfig Cfg => AIWallPlannerConfig.I;

        /// <summary>maxSealableWidth, for callers outside this class.</summary>
        public static float MaxSealableWidth => Cfg.maxSealableWidth;

        /// <summary>hubSpacing, for callers outside this class.</summary>
        public static float HubSpacing => Cfg.hubSpacing;

        #endregion

        // ── Plan modes ────────────────────────────────────────────────────
        /// <summary>Fully sheltered — terrain closes every approach, no
        /// walls needed at all.</summary>
        public const byte ModeNone = 0;
        /// <summary>Terrain shelters the base except for a few narrow
        /// corridors — seal each corridor wall-to-wall.</summary>
        public const byte ModeChokepoints = 1;
        /// <summary>Open ground — enclose the important buildings in a
        /// large square-ish perimeter.</summary>
        public const byte ModePerimeter = 2;
        /// <summary>The wall follows the owner's TERRITORY BORDER, a few
        /// cells inside it, closed by terrain where terrain closes the way
        /// (2026-09-30). The mode every AI plans on a partitioned map.</summary>
        public const byte ModeBorder = 3;

        // ── Slot flags ────────────────────────────────────────────────────
        /// <summary>The segment from this slot to the NEXT slot of the same
        /// chain becomes a gate once both hubs stand.</summary>
        public const byte FlagGateAfter = 1;
        /// <summary>The wall instance nearest this slot converts to a
        /// Wall Tower once built.</summary>
        public const byte FlagTower = 2;
        /// <summary>The gap between this slot and the NEXT live slot of the
        /// same chain is closed by impassable TERRAIN — a mountain shoulder
        /// the wall does not need to span. Set by the planner when it drops a
        /// slot it could not rescue onto open ground, and read by the
        /// executor's gap-closing pass so it bridges real holes but leaves
        /// terrain-sealed stretches alone.</summary>
        public const byte FlagTerrainSealed = 4;
        /// <summary>The link from this slot to the NEXT live slot was refused
        /// (blocked, off its owner's ground, too long) and no detour exists —
        /// left open rather than re-issued forever (2026-10-02).</summary>
        public const byte FlagLinkRefused = 8;
        /// <summary>Orders a slot's hub or link may have outstanding before
        /// the AI decides the executor refused them.</summary>
        public const byte MaxWallTries = 3;
        /// <summary>Slot proved unplaceable at execution time — skip it
        /// forever.</summary>
        public const byte FlagDead = 128;

        /// <summary>How far a perimeter slot may slide (along the side, then
        /// inward) looking for open ground before the planner concedes the
        /// stretch to terrain. Half the hub spacing, so a rescued slot never
        /// crosses past its neighbour.</summary>
        private static float SlotRescueReach => Cfg.hubSpacing * 0.5f;

        // ── Shelter scan tuning ───────────────────────────────────────────
        private const int Bearings = 48;          // 7.5 degree resolution
        /// <summary>Consecutive terrain-blocked samples that count as a real
        /// barrier (6 m deep) — filters single-cell slope noise.</summary>
        private const int ShelterRunSamples = 3;
        private const int MaxCorridors = 3;


        /// <summary>One ingress corridor through the terrain shelter.</summary>
        public struct Corridor
        {
            /// <summary>Centre of the narrowest cross-section.</summary>
            public float3 ChokePos;
            /// <summary>Unit vector ALONG the wall line (perpendicular to
            /// the approach direction).</summary>
            public float3 ChokeAxis;
            /// <summary>Approach direction (Hall toward the corridor).</summary>
            public float3 Approach;
            public float ChokeWidth;
            /// <summary>Bearing arc this corridor spans, as a fraction of
            /// the full circle (primary corridor = widest arc).</summary>
            public float ArcFraction;
            public bool Sealable;
        }

        // ──────────────────────────────────────────────────────────────────
        // SHELTER ASSESSMENT
        // ──────────────────────────────────────────────────────────────────

        /// <summary>Terrain-only blockage probe. Off-grid reads as blocked
        /// (the map edge shelters). No grid at all reads as open.</summary>
        private static bool TerrainBlockedAt(float x, float z)
        {
            var grid = PassabilityGrid.Instance;
            if (grid == null) return false;
            return grid.GetCell(grid.WorldToCell(new float3(x, 0f, z)))
                == PassabilityGrid.TerrainBlocked;
        }

        /// <summary>
        /// Scan <see cref="Bearings"/> rays out of the Hall and decide
        /// whether terrain shelters the base. Returns true when the base
        /// qualifies as sheltered: open bearings under budget, at most
        /// <see cref="MaxCorridors"/> ingress corridors, every corridor
        /// sealable. The analysis always runs to completion —
        /// <paramref name="verdict"/> carries the human-readable numbers
        /// for the AI log so a wrong mode pick can be diagnosed from the
        /// match log alone. <paramref name="corridors"/> must hold at least
        /// <see cref="MaxCorridors"/> entries; <paramref name="corridorCount"/>
        /// is 0 when terrain closes every approach.
        /// </summary>
        public static bool TryAssess(float3 hallPos, Corridor[] corridors,
            out int corridorCount, out float openFraction, out string verdict)
        {
            corridorCount = 0;
            openFraction = 1f;
            verdict = "no passability grid";
            if (PassabilityGrid.Instance == null) return false;

            // 1. Per-bearing shelter: does a >= ShelterRunSamples-deep
            //    terrain barrier interrupt the ray before the horizon?
            var open = new bool[Bearings];
            for (int b = 0; b < Bearings; b++)
            {
                float ang = (b / (float)Bearings) * 2f * math.PI;
                float dx = math.cos(ang), dz = math.sin(ang);
                int run = 0;
                bool sheltered = false;
                for (float r = Cfg.scanStart; r <= Cfg.scanEnd; r += Cfg.scanStep)
                {
                    if (TerrainBlockedAt(hallPos.x + dx * r, hallPos.z + dz * r))
                    {
                        if (++run >= ShelterRunSamples) { sheltered = true; break; }
                    }
                    else run = 0;
                }
                open[b] = !sheltered;
            }

            // Erode single-bearing shelter blips: a lone boulder on one ray
            // splits a real corridor in two and pushes the corridor count
            // past the cap. Open blips are NOT eroded — a one-bearing lane
            // is a real walkable ingress.
            var smoothed = new bool[Bearings];
            int openCount = 0;
            for (int b = 0; b < Bearings; b++)
            {
                smoothed[b] = open[b]
                    || (open[(b + Bearings - 1) % Bearings] && open[(b + 1) % Bearings]);
                if (smoothed[b]) openCount++;
            }
            open = smoothed;
            openFraction = openCount / (float)Bearings;

            // Fully closed — sheltered with zero corridors to wall.
            if (openCount == 0)
            {
                verdict = "terrain closes every approach";
                return true;
            }

            // 2. Circular grouping of open bearings into corridors. Start
            //    the walk on a sheltered bearing so no arc is split across
            //    the wrap seam. Every corridor is analysed (first
            //    MaxCorridors stored) so the verdict always has the numbers.
            int start = -1;
            for (int b = 0; b < Bearings; b++)
                if (!open[b]) { start = b; break; }

            int totalCorridors = 0;
            if (start < 0)
            {
                totalCorridors = 1; // fully open circle — one giant corridor
            }
            else
            {
                int runStart = -1, runLen = 0;
                for (int i = 1; i <= Bearings; i++)
                {
                    int b = (start + i) % Bearings;
                    if (open[b])
                    {
                        if (runLen == 0) runStart = b;
                        runLen++;
                        continue;
                    }
                    if (runLen == 0) continue;

                    totalCorridors++;
                    if (corridorCount < MaxCorridors)
                    {
                        float midIdx = runStart + (runLen - 1) * 0.5f;
                        float midAng = (midIdx / Bearings) * 2f * math.PI;
                        var c = new Corridor
                        {
                            Approach = new float3(math.cos(midAng), 0f, math.sin(midAng)),
                            ArcFraction = runLen / (float)Bearings,
                        };
                        c.Sealable = TryFindChokeCrossSection(hallPos, c.Approach,
                            out c.ChokePos, out c.ChokeAxis, out c.ChokeWidth);
                        corridors[corridorCount++] = c;
                    }
                    runLen = 0;
                }
            }

            var sb = new System.Text.StringBuilder(96);
            sb.Append($"open {(int)(openFraction * 100f)}% of arc, {totalCorridors} corridor(s)");
            for (int i = 0; i < corridorCount; i++)
                sb.Append(corridors[i].Sealable
                    ? $", w={corridors[i].ChokeWidth:F0}"
                    : ", unsealable");

            if (openFraction > Cfg.maxOpenFraction)
            {
                sb.Append(" - too open");
                verdict = sb.ToString();
                return false;
            }
            if (totalCorridors > MaxCorridors)
            {
                sb.Append(" - too many corridors");
                verdict = sb.ToString();
                return false;
            }
            for (int i = 0; i < corridorCount; i++)
                if (!corridors[i].Sealable)
                {
                    verdict = sb.ToString();
                    return false;
                }
            verdict = sb.ToString();
            return true;
        }

        /// <summary>
        /// Walk the corridor's approach line and find the narrowest bounded
        /// cross-section. At every step the width is measured along SEVERAL
        /// candidate axes (the approach perpendicular swept +/-45 degrees in
        /// 15 degree steps) and the narrowest bounded one wins — the
        /// hall-to-corridor bearing is rarely parallel to the pass itself,
        /// and measuring only its exact perpendicular both inflated widths
        /// diagonally and oriented the wall ALONGSIDE the obstacle instead
        /// of across the gap (2026-08-11 game, Yellow).
        /// </summary>
        private static bool TryFindChokeCrossSection(float3 hallPos, float3 dir,
            out float3 chokePos, out float3 chokeAxis, out float chokeWidth)
        {
            chokePos = default;
            chokeAxis = default;
            chokeWidth = float.MaxValue;
            bool found = false;

            float baseAng = math.atan2(dir.z, dir.x) + math.PI * 0.5f;
            const float sweepStep = 15f * math.PI / 180f;

            for (float d = Cfg.scanStart + 2f; d <= 70f; d += 2f)
            {
                float3 p = hallPos + dir * d;
                for (int a = -3; a <= 3; a++)
                {
                    float ang = baseAng + a * sweepStep;
                    float3 axis = new float3(math.cos(ang), 0f, math.sin(ang));
                    if (!TryTerrainClearance(p, axis, out float left)) continue;
                    if (!TryTerrainClearance(p, -axis, out float right)) continue;
                    float width = left + right;
                    if (width > Cfg.maxSealableWidth) continue;
                    if (width >= chokeWidth) continue;
                    chokeWidth = width;
                    chokePos = p + axis * (left - right) * 0.5f;
                    chokePos.y = TerrainUtility.GetHeight(chokePos.x, chokePos.z);
                    chokeAxis = axis;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>Metres from <paramref name="from"/> along
        /// <paramref name="stepDir"/> to the first 2-sample terrain run.
        /// False when the probe cap is reached first (flank unbounded).</summary>
        private static bool TryTerrainClearance(float3 from, float3 stepDir, out float clearance)
        {
            int run = 0;
            for (float s = 1f; s <= Cfg.chokeProbeCap; s += 1f)
            {
                if (TerrainBlockedAt(from.x + stepDir.x * s, from.z + stepDir.z * s))
                {
                    if (++run >= 2) { clearance = s - run; return true; }
                }
                else run = 0;
            }
            clearance = Cfg.chokeProbeCap;
            return false;
        }

        // ──────────────────────────────────────────────────────────────────
        // PLAN GENERATION
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Build the full slot plan for a faction. Fills
        /// <paramref name="slots"/> in chain order and returns the plan
        /// mode; <paramref name="why"/> carries the assessment numbers for
        /// the AI log.
        /// </summary>
        public static byte BuildPlan(EntityManager em, Faction faction, float3 hallPos,
            NativeList<AIWallPlanSlot> slots, out string why)
        {
            // Territories exist: the wall follows the border (a wall may only
            // stand on its owner's ground — CommandRouter.WallLineOnOwnGround).
            // The terrain-only square below is the fallback for maps with no
            // partition, where the ownership gate is off.
            if (TheWaningBorder.World.Regions.RegionMap.Ready
                && TheWaningBorder.World.Regions.TerritoryOwnership.Ready
                && TheWaningBorder.World.Regions.TerritoryOwnership.CountOf(faction) > 0)
            {
                EmitBorderLoop(em, faction, hallPos, slots, out why);
                return ModeBorder;
            }

            var corridors = new Corridor[MaxCorridors];
            if (TryAssess(hallPos, corridors, out int corridorCount, out _, out why))
            {
                if (corridorCount == 0) return ModeNone;
                for (int c = 0; c < corridorCount; c++)
                    EmitChokeLine(em, corridors[c], (byte)c, slots);
                return ModeChokepoints;
            }

            EmitPerimeter(em, faction, hallPos, slots);
            return ModePerimeter;
        }

        /// <summary>Hub line spanning a corridor wall-to-wall. The END hubs
        /// are resolved AGAINST the flanking obstacles: from each measured
        /// terrain edge, walk inward until the hub footprint actually
        /// places, so the bastion hugs the rock face and no walkable gap
        /// survives on either side (a naive edge+overshoot slot lands ON
        /// blocked ground, dies at execution, and turns one large chokepoint
        /// into two small ones — 2026-08-11 game). Interior hubs divide the
        /// resolved span at up to <see cref="HubSpacing"/>. The middle
        /// segment is the corridor's gate; the ends and the gate's shoulders
        /// carry towers.</summary>
        private static void EmitChokeLine(EntityManager em, Corridor c, byte chain,
            NativeList<AIWallPlanSlot> slots)
        {
            int2 hubSize = BuildingSizeConfig.GetSize("Alanthor_Wall");
            float half = c.ChokeWidth * 0.5f;
            float3 endA = ResolveLineEnd(em,
                c.ChokePos - c.ChokeAxis * half, c.ChokeAxis, hubSize);
            float3 endB = ResolveLineEnd(em,
                c.ChokePos + c.ChokeAxis * half, -c.ChokeAxis, hubSize);

            float span = math.distance(
                new float2(endA.x, endA.z), new float2(endB.x, endB.z));
            int segs = math.max(1, (int)math.ceil(span / Cfg.hubSpacing));
            int hubCount = segs + 1;
            int gateSlot = (segs - 1) / 2;       // middle segment gateSlot -> gateSlot+1

            for (int i = 0; i < hubCount; i++)
            {
                float t = i / (float)(hubCount - 1);
                float3 pos = math.lerp(endA, endB, t);
                pos.y = TerrainUtility.GetHeight(pos.x, pos.z);

                byte flags = 0;
                if (i == gateSlot) flags |= FlagGateAfter;
                if (i == 0 || i == hubCount - 1
                    || i == gateSlot || i == gateSlot + 1) flags |= FlagTower;
                slots.Add(new AIWallPlanSlot { Position = pos, Chain = chain, Flags = flags });
            }
        }

        /// <summary>Outermost buildable hub position hugging a corridor
        /// flank: walk inward from the measured terrain edge in half-metre
        /// steps until the hub footprint validates. Falls back to just
        /// inside the edge when nothing validates within reach — execution
        /// then nudges or dead-marks the slot.</summary>
        private static float3 ResolveLineEnd(EntityManager em, float3 edge, float3 inward,
            int2 hubSize)
        {
            for (float t = 0.5f; t <= 8f; t += 0.5f)
            {
                float3 p = edge + inward * t;
                p.y = TerrainUtility.GetHeight(p.x, p.z);
                if (BuildSiteSnapshot.Current(em).IsValidBuildPosition(em, p, hubSize, null)) return p;
            }
            float3 fallback = edge + inward * 1.5f;
            fallback.y = TerrainUtility.GetHeight(fallback.x, fallback.z);
            return fallback;
        }

        /// <summary>Square-ish perimeter around the base cluster: bounding
        /// box of every non-wall faction building near the Hall, padded and
        /// clamped. Gates sit at the four side midpoints (the rectangle is
        /// axis-aligned, so they face the cardinal directions exactly);
        /// towers stand on the corners and the gate shoulders. Slots on
        /// terrain-blocked ground are dropped — the mountain IS the wall
        /// there, and the resulting 2x-spacing hub gap sits beyond the
        /// doctrine's link radius, so no segment ever spans it.</summary>
        private static void EmitPerimeter(EntityManager em, Faction faction, float3 hallPos,
            NativeList<AIWallPlanSlot> slots)
        {
            // Bounding box over the base cluster (walls / towers excluded —
            // fortifications must not drag the rectangle outward).
            float2 mn = new float2(hallPos.x, hallPos.z);
            float2 mx = mn;
            var q = QC_BuildingTagFactionTagLocalTransform.Get(em, QT_BuildingTagFactionTagLocalTransform);
            using (var ents = q.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                    if (em.HasComponent<WallTag>(ents[i])) continue;
                    if (em.HasComponent<WallHubTag>(ents[i])) continue;
                    if (em.HasComponent<WallInstanceTag>(ents[i])) continue;
                    if (em.HasComponent<WallSegmentTag>(ents[i])) continue;
                    if (em.HasComponent<WatchTowerTag>(ents[i])) continue;
                    float3 p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                    float dx = p.x - hallPos.x, dz = p.z - hallPos.z;
                    if (dx * dx + dz * dz > Cfg.perimeterGatherRadius * Cfg.perimeterGatherRadius)
                        continue;
                    mn = math.min(mn, new float2(p.x, p.z));
                    mx = math.max(mx, new float2(p.x, p.z));
                }
            }

            float2 center = (mn + mx) * 0.5f;
            // The wall is planned ONCE, usually while the base is still small,
            // so its extent must cover the ground the AI will build on later —
            // not just what stands today. The floor is the base placer's own
            // reach (last-resort ring + the widest footprint's half + the
            // interior clearance it keeps), so wider AI spacing can never push
            // later buildings out through the wall (2026-09-25). SimpleAISystem
            // then refuses sites outside the planned rectangle.
            var ai = SimpleAISystemConfig.I;
            float reach = math.max(ai.buildRingDistanceMax, ai.relaxedBuildRingDistanceMax)
                        + Cfg.perimeterFootprintAllowance + ai.wallInteriorClearance;
            float heMin = math.min(math.max(Cfg.perimeterHalfExtentMin, reach),
                                   Cfg.perimeterHalfExtentMax);
            // Centre the enclosure on the Hall the ring is measured from, far
            // enough that the Hall-centred build disc fits on every side.
            float2 hall2 = new float2(hallPos.x, hallPos.z);
            float2 lo = math.min(mn - Cfg.perimeterPad, hall2 - heMin);
            float2 hi = math.max(mx + Cfg.perimeterPad, hall2 + heMin);
            center = (lo + hi) * 0.5f;
            float2 he = math.clamp((hi - lo) * 0.5f, heMin, Cfg.perimeterHalfExtentMax);

            // Corners in loop order (counter-clockwise, starting +x/+z).
            var corners = new float2[4]
            {
                center + new float2( he.x,  he.y),
                center + new float2(-he.x,  he.y),
                center + new float2(-he.x, -he.y),
                center + new float2( he.x, -he.y),
            };

            for (int side = 0; side < 4; side++)
            {
                float2 a = corners[side];
                float2 b = corners[(side + 1) % 4];
                float len = math.distance(a, b);
                int segs = math.max(1, (int)math.ceil(len / Cfg.hubSpacing));
                int gateFrom = segs / 2;   // segment nearest the side midpoint

                // Along-side unit vector and the inward normal (toward the
                // rectangle centre) — the two axes a blocked slot slides on.
                float2 along = math.normalizesafe(b - a, new float2(1f, 0f));
                float2 inward = math.normalizesafe(center - (a + b) * 0.5f,
                    new float2(-along.y, along.x));

                // Slot at each fraction j/segs; the next side contributes
                // the shared corner, so stop short of t = 1.
                for (int j = 0; j < segs; j++)
                {
                    float2 xz = math.lerp(a, b, j / (float)segs);

                    // A slot on terrain-blocked ground used to be dropped
                    // outright, on the theory that the mountain IS the wall
                    // there and the 2x-spacing hole would sit beyond the
                    // link radius. But TerrainBlockedAt is a SINGLE-POINT
                    // sample: one boulder, one steep metre, or the corner of
                    // a cliff under the slot deleted a hub and left a 60 m
                    // walkable gap in a wall the AI still reported as
                    // planned. That is the "AI never finishes its wall"
                    // report. Now we RESCUE the slot first — slide it along
                    // the side and inward until it finds open ground — and
                    // only concede the stretch to terrain when nothing
                    // within reach is open, in which case the PREVIOUS slot
                    // is marked terrain-sealed so the executor knows the gap
                    // is deliberate rather than a hole to bridge.
                    if (TerrainBlockedAt(xz.x, xz.y)
                        && !TryRescueSlot(xz, along, inward, out xz))
                    {
                        if (slots.Length > 0)
                        {
                            var prev = slots[slots.Length - 1];
                            prev.Flags |= FlagTerrainSealed;
                            slots[slots.Length - 1] = prev;
                        }
                        continue;
                    }

                    byte flags = 0;
                    if (j == 0) flags |= FlagTower;                      // corner
                    if (j == gateFrom) flags |= (byte)(FlagGateAfter | FlagTower);
                    if (j == gateFrom + 1) flags |= FlagTower;           // gate shoulder
                    slots.Add(new AIWallPlanSlot
                    {
                        Position = new float3(xz.x,
                            TerrainUtility.GetHeight(xz.x, xz.y), xz.y),
                        Chain = 0,
                        Flags = flags,
                    });
                }
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // BORDER LOOP
        // ──────────────────────────────────────────────────────────────────

        /// <summary>Order-independent signature of the territories this
        /// faction owns — the plan is redrawn when it changes.</summary>
        public static uint TerritorySignature(Faction faction)
        {
            if (!TheWaningBorder.World.Regions.TerritoryOwnership.Ready) return 0;
            uint h = 2166136261u;
            var owned = TheWaningBorder.World.Regions.TerritoryOwnership.TerritoriesOf(faction);
            for (int i = 0; i < owned.Count; i++)
                h = (h ^ (uint)(owned[i] + 1)) * 16777619u;
            return h;
        }

        /// <summary>Owned by this faction, on the map, and not impassable
        /// region kind — the ground a border wall stands inside.</summary>
        private static bool OwnGround(Faction faction, float x, float z)
        {
            int t = TheWaningBorder.World.Regions.RegionMap.RegionAt(x, z);
            if (t == TheWaningBorder.World.Regions.RegionMap.None) return false;
            return TheWaningBorder.World.Regions.TerritoryOwnership.OwnerOf(t) == (int)faction;
        }

        /// <summary>
        /// THE WALL FOLLOWS THE BORDER — traced, not ray-cast (2026-10-02;
        /// docs/Design/Age_1_Alanthor.md § The AI's wall).
        ///
        /// The 2026-09-30 version marched 48 rays out of the Hall and walled
        /// the FIRST point each left owned ground. An inner lake or mountain
        /// ended a ray, a notch in the border ended it early, territory not in
        /// a straight line of sight from the Hall was never walled, and the
        /// 8-12 m inset plus inward nudges finished the job: the AI's wall
        /// stood far inside its border.
        ///
        /// Now, on a 1 m grid around the Fortress:
        ///   1. the faction's owned ground connected to the Hall;
        ///   2. every cell's distance to FOREIGN ground (someone else's or
        ///      neutral territory — a lake or mountain is not foreign, so it
        ///      does not push the wall in);
        ///   3. the ground at least <c>borderInset</c> inside, holes filled
        ///      (the wall runs round the outside, never round a lake);
        ///   4. its outline traced as one loop (Moore neighbour tracing);
        ///   5. outline points far from any foreign ground run along
        ///      impassable ground the map closes — no wall there;
        ///   6. hubs wherever the outline bends away from a straight run by
        ///      more than <c>borderFollowTolerance</c>, and at least every
        ///      <see cref="HubSpacing"/>.
        /// Pure function of replicated state (region map, ownership, terrain):
        /// every lockstep peer draws the same wall.
        /// </summary>
        private static void EmitBorderLoop(EntityManager em, Faction faction, float3 hallPos,
            NativeList<AIWallPlanSlot> slots, out string why)
        {
            const float cs = 1f;
            float R = Cfg.borderScanMax;
            int n = math.max(8, (int)math.ceil(2f * R / cs));
            float ox = hallPos.x - n * cs * 0.5f, oz = hallPos.z - n * cs * 0.5f;
            int N = n * n;

            // 1. Owned and foreign ground.
            var owned = new bool[N];
            var foreign = new bool[N];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float wx = ox + (x + 0.5f) * cs, wz = oz + (z + 0.5f) * cs;
                    int t = TheWaningBorder.World.Regions.RegionMap.RegionAt(wx, wz);
                    if (t == TheWaningBorder.World.Regions.RegionMap.None) continue;
                    if (TheWaningBorder.World.Regions.TerritoryOwnership.OwnerOf(t) == (int)faction)
                        owned[z * n + x] = true;
                    else foreign[z * n + x] = true;
                }

            int hx = n / 2, hz = n / 2;
            if (!owned[hz * n + hx])
            {
                why = "border trace: the Hall does not stand on its own ground";
                return;
            }

            // 2. Distance to foreign ground: two-pass chamfer transform.
            var dist = new float[N];
            const float Far = 1e9f, D1 = cs, D2 = cs * 1.41421356f;
            for (int i = 0; i < N; i++) dist[i] = foreign[i] ? 0f : Far;
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int i = z * n + x;
                    float d = dist[i];
                    if (x > 0) d = math.min(d, dist[i - 1] + D1);
                    if (z > 0)
                    {
                        d = math.min(d, dist[i - n] + D1);
                        if (x > 0) d = math.min(d, dist[i - n - 1] + D2);
                        if (x < n - 1) d = math.min(d, dist[i - n + 1] + D2);
                    }
                    dist[i] = d;
                }
            for (int z = n - 1; z >= 0; z--)
                for (int x = n - 1; x >= 0; x--)
                {
                    int i = z * n + x;
                    float d = dist[i];
                    if (x < n - 1) d = math.min(d, dist[i + 1] + D1);
                    if (z < n - 1)
                    {
                        d = math.min(d, dist[i + n] + D1);
                        if (x < n - 1) d = math.min(d, dist[i + n + 1] + D2);
                        if (x > 0) d = math.min(d, dist[i + n - 1] + D2);
                    }
                    dist[i] = d;
                }

            // 3. Inside the inset, connected to the Hall, holes filled.
            float inset = Cfg.borderInset;
            var inside = new bool[N];
            for (int i = 0; i < N; i++) inside[i] = owned[i] && dist[i] >= inset;
            if (!inside[hz * n + hx])
            {
                why = "border trace: the Hall stands within the wall inset of its border";
                return;
            }
            var mask = Flood(inside, n, hx, hz, eight: true);
            FillHoles(mask, n);

            // 4. Trace the outline, starting east of the Hall.
            int sx = hx;
            while (sx + 1 < n && mask[hz * n + sx + 1]) sx++;
            var loop = TraceOutline(mask, n, sx, hz);
            if (loop.Count < 8)
            {
                why = $"border trace: outline too short ({loop.Count} cells)";
                return;
            }

            // Cell centres, lightly smoothed (the outline is a staircase).
            int m = loop.Count;
            var pts = new float2[m];
            var open = new bool[m];
            float openReach = inset + 3f * cs;
            int openCount = 0;
            for (int k = 0; k < m; k++)
            {
                float2 sum = float2.zero;
                for (int w = -2; w <= 2; w++)
                {
                    int c = loop[(k + w + m) % m];
                    sum += new float2(ox + (c % n + 0.5f) * cs, oz + (c / n + 0.5f) * cs);
                }
                pts[k] = sum / 5f;
                int ck = loop[k];
                // 5. Near foreign ground: this stretch is a border to wall.
                // Far from it, the outline is running along a lake shore or a
                // mountain the map closes. Too near the Hall: never walled.
                open[k] = dist[ck] <= openReach
                          && math.distance(pts[k], hallPos.xz) >= Cfg.borderMinRadius;
                if (open[k]) openCount++;
            }

            why = $"border trace: outline {m} m, {openCount} m walled, inset {inset:F1} m";
            if (openCount < 2) return;

            // 6. Runs of open outline -> hubs at bends and every HubSpacing.
            int start = -1;
            for (int k = 0; k < m; k++) if (!open[k]) { start = k; break; }
            bool fullLoop = start < 0;
            if (fullLoop) start = 0;

            int firstSlot = slots.Length;
            var run = new System.Collections.Generic.List<float2>(m + 1);
            for (int i = 0; i <= m; i++)
            {
                int k = (start + i) % m;
                bool isOpen = i < m && open[k];
                if (isOpen) { run.Add(pts[k]); continue; }
                if (fullLoop && i == m) run.Add(pts[start]);   // close the ring
                if (run.Count >= 2)
                {
                    EmitFollowing(run, slots, closesRing: fullLoop);
                    if (!fullLoop && slots.Length > firstSlot)
                    {
                        var last = slots[slots.Length - 1];
                        last.Flags |= FlagTerrainSealed;
                        slots[slots.Length - 1] = last;
                    }
                }
                run.Clear();
            }

            // Gates spread evenly (one per ~quarter), towers on their
            // shoulders and every fourth hub.
            int count = slots.Length - firstSlot;
            if (count < 2) return;
            int gates = math.clamp(count / 4, 1, 4);
            for (int g = 0; g < gates; g++)
            {
                int i = firstSlot + (int)((g + 0.5f) * count / gates);
                if (i >= slots.Length - 1 && !fullLoop) i = slots.Length - 2;
                var s = slots[i];
                if ((s.Flags & FlagTerrainSealed) != 0) continue;
                s.Flags |= (byte)(FlagGateAfter | FlagTower);
                slots[i] = s;
                int j = i + 1 < slots.Length ? i + 1 : firstSlot;
                var t = slots[j]; t.Flags |= FlagTower; slots[j] = t;
            }
            for (int i = firstSlot; i < slots.Length; i += 4)
            {
                var s = slots[i]; s.Flags |= FlagTower; slots[i] = s;
            }
        }

        /// <summary>
        /// THE BORDER BAND (2026-10-02, docs/Design/Age_1_Alanthor.md § The
        /// AI's wall): an AI building keeps <c>buildingBorderClearance</c>
        /// metres between its EDGE and its territory border, so the band the
        /// border wall will run along is free from the first minute — long
        /// before the wall plan exists (it is drawn after age-up, when most of
        /// the base already stands). True when the footprint grown by the
        /// clearance touches no ground owned by anyone else, nor neutral
        /// territory. The owner is whoever holds the footprint's centre, so
        /// any placer can ask without knowing the faction. Lakes, mountains
        /// and the map edge (region None) are not borders. Fails open before
        /// the region map exists.
        /// </summary>
        public static bool FootprintClearOfBorder(float3 centre, int2 size)
        {
            if (!TheWaningBorder.World.Regions.RegionMap.Ready
                || !TheWaningBorder.World.Regions.TerritoryOwnership.Ready) return true;
            int home = TheWaningBorder.World.Regions.RegionMap.RegionAt(centre.x, centre.z);
            if (home == TheWaningBorder.World.Regions.RegionMap.None) return true;
            int owner = TheWaningBorder.World.Regions.TerritoryOwnership.OwnerOf(home);

            float c = Cfg.buildingBorderClearance;
            float hx = size.x * 0.5f + c, hz = size.y * 0.5f + c;
            const float Step = 2f;
            for (float x = -hx; x <= hx + 1e-3f; x += Step)
                for (float z = -hz; z <= hz + 1e-3f; z += Step)
                {
                    int t = TheWaningBorder.World.Regions.RegionMap.RegionAt(centre.x + x, centre.z + z);
                    if (t == TheWaningBorder.World.Regions.RegionMap.None || t == home) continue;
                    if (TheWaningBorder.World.Regions.TerritoryOwnership.OwnerOf(t) != owner) return false;
                }
            return true;
        }

        /// <summary>
        /// Hubs along a traced outline: one at the start, then — walking on —
        /// a new hub at the last point the straight run from the previous hub
        /// still stays within <c>borderFollowTolerance</c> of the outline, or
        /// at <see cref="HubSpacing"/> of arc, whichever comes first. So the
        /// straight wall between two hubs follows the border's bends instead
        /// of cutting across them.
        /// </summary>
        private static void EmitFollowing(System.Collections.Generic.List<float2> line,
            NativeList<AIWallPlanSlot> slots, bool closesRing)
        {
            void Add(float2 p) => slots.Add(new AIWallPlanSlot
            {
                Position = new float3(p.x, TerrainUtility.GetHeight(p.x, p.y), p.y),
                Chain = 0,
            });

            float tol = math.max(0.25f, Cfg.borderFollowTolerance);
            int a = 0;
            Add(line[0]);
            float arc = 0f;
            for (int j = 1; j < line.Count; j++)
            {
                arc += math.distance(line[j - 1], line[j]);
                bool bends = false;
                for (int k = a + 1; k < j && !bends; k++)
                    bends = DistToSegment(line[k], line[a], line[j]) > tol;
                if (!bends && arc < Cfg.hubSpacing) continue;
                int at = bends ? j - 1 : j;
                if (at <= a) at = j;
                Add(line[at]);
                a = at;
                arc = 0f;
                for (int k = a + 1; k <= j; k++) arc += math.distance(line[k - 1], line[k]);
            }
            int last = line.Count - 1;
            if (a == last) { if (closesRing && slots.Length > 1) slots.RemoveAt(slots.Length - 1); return; }
            if (!closesRing) Add(line[last]);
        }

        static float DistToSegment(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float l2 = math.lengthsq(ab);
            float t = l2 > 1e-8f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
            return math.distance(p, a + ab * t);
        }

        static readonly int[] Ox = { 0, 1, 1, 1, 0, -1, -1, -1 };   // clockwise from north
        static readonly int[] Oz = { 1, 1, 0, -1, -1, -1, 0, 1 };

        /// <summary>The cells of <paramref name="src"/> connected to (x, z).</summary>
        static bool[] Flood(bool[] src, int n, int x, int z, bool eight)
        {
            var o = new bool[src.Length];
            var q = new System.Collections.Generic.Queue<int>();
            o[z * n + x] = true;
            q.Enqueue(z * n + x);
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                int cx = c % n, cz = c / n;
                for (int d = 0; d < 8; d++)
                {
                    if (!eight && (d & 1) == 1) continue;
                    int nx = cx + Ox[d], nz = cz + Oz[d];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    int ni = nz * n + nx;
                    if (o[ni] || !src[ni]) continue;
                    o[ni] = true;
                    q.Enqueue(ni);
                }
            }
            return o;
        }

        /// <summary>Set every cell of <paramref name="mask"/> that the grid's
        /// edge cannot reach through unset cells: the holes.</summary>
        static void FillHoles(bool[] mask, int n)
        {
            var outside = new bool[mask.Length];
            var q = new System.Collections.Generic.Queue<int>();
            for (int i = 0; i < n; i++)
            {
                foreach (int c in new[] { i, (n - 1) * n + i, i * n, i * n + n - 1 })
                    if (!mask[c] && !outside[c]) { outside[c] = true; q.Enqueue(c); }
            }
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                int cx = c % n, cz = c / n;
                for (int d = 0; d < 8; d += 2)
                {
                    int nx = cx + Ox[d], nz = cz + Oz[d];
                    if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                    int ni = nz * n + nx;
                    if (outside[ni] || mask[ni]) continue;
                    outside[ni] = true;
                    q.Enqueue(ni);
                }
            }
            for (int i = 0; i < mask.Length; i++) if (!outside[i]) mask[i] = true;
        }

        /// <summary>
        /// Moore-neighbour trace of the outline through (sx, sz), a set cell
        /// whose east neighbour is unset, with Jacob's stopping rule. Returns
        /// cell indices in walking order.
        /// </summary>
        static System.Collections.Generic.List<int> TraceOutline(bool[] mask, int n, int sx, int sz)
        {
            bool In(int x, int z) => x >= 0 && z >= 0 && x < n && z < n && mask[z * n + x];
            var outl = new System.Collections.Generic.List<int>();
            int cx = sx, cz = sz;
            int back = 2;                       // the unset cell we came from: east
            int startBack = back;
            int limit = 8 * n;                  // an outline in an n x n box is far shorter
            int startVisits = 0;
            for (int step = 0; step < limit * 4; step++)
            {
                if (cx == sx && cz == sz && ++startVisits > 2) break;
                outl.Add(cz * n + cx);
                bool found = false;
                for (int k = 1; k <= 8; k++)
                {
                    int d = (back + k) % 8;
                    int nx = cx + Ox[d], nz = cz + Oz[d];
                    if (!In(nx, nz)) continue;
                    int prev = (d + 7) % 8;
                    int bx = cx + Ox[prev] - nx, bz = cz + Oz[prev] - nz;
                    int nb = 0;
                    for (int e = 0; e < 8; e++) if (Ox[e] == bx && Oz[e] == bz) { nb = e; break; }
                    cx = nx; cz = nz; back = nb;
                    found = true;
                    break;
                }
                if (!found) break;                               // a lone cell
                if (cx == sx && cz == sz && back == startBack) break;
            }
            return outl;
        }

        /// <summary>Hubs along a polyline at up to <see cref="HubSpacing"/>:
        /// the first point, then every spacing of arc length, then the last
        /// point (unless it closes a ring back onto the first).</summary>
        private static void EmitResampled(System.Collections.Generic.List<float2> line,
            NativeList<AIWallPlanSlot> slots, bool closesRing)
        {
            void Add(float2 p) => slots.Add(new AIWallPlanSlot
            {
                Position = new float3(p.x, TerrainUtility.GetHeight(p.x, p.y), p.y),
                Chain = 0,
            });

            Add(line[0]);
            float since = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                float2 a = line[i - 1], b = line[i];
                float seg = math.distance(a, b);
                float along = 0f;
                while (since + (seg - along) >= Cfg.hubSpacing)
                {
                    along += Cfg.hubSpacing - since;
                    since = 0f;
                    Add(math.lerp(a, b, along / seg));
                }
                since += seg - along;
            }
            if (!closesRing && since > Cfg.hubSpacing * 0.25f) Add(line[line.Count - 1]);
            else if (closesRing && slots.Length > 0 && since < Cfg.hubSpacing * 0.25f
                     && math.distance(new float2(slots[slots.Length - 1].Position.x,
                                                 slots[slots.Length - 1].Position.z), line[0]) < 1f)
                slots.RemoveAt(slots.Length - 1);   // the ring's closing point duplicates the first hub
        }

        /// <summary>
        /// Slide a terrain-blocked perimeter slot onto open ground. Probes an
        /// expanding set of offsets: along the wall line first (keeps the ring
        /// shape), then inward (tucks the wall behind the obstacle), then the
        /// two diagonals. Returns false when everything within
        /// <see cref="SlotRescueReach"/> is blocked — that is a real mountain,
        /// not a boulder, and the stretch is genuinely sealed.
        ///
        /// Outward is deliberately NOT probed: pushing the wall out past the
        /// obstacle would enlarge the enclosure around ground the AI is not
        /// defending, and the outward side is where the attacker stands.
        /// </summary>
        private static bool TryRescueSlot(float2 blocked, float2 along, float2 inward,
            out float2 rescued)
        {
            for (float d = 2f; d <= SlotRescueReach; d += 2f)
            {
                // Fixed probe order — every lockstep peer walks the same
                // sequence and picks the same first hit.
                for (int p = 0; p < 5; p++)
                {
                    float2 probe = p switch
                    {
                        0 => blocked + along * d,
                        1 => blocked - along * d,
                        2 => blocked + inward * d,
                        3 => blocked + inward * d + along * d,
                        _ => blocked + inward * d - along * d,
                    };
                    if (TerrainBlockedAt(probe.x, probe.y)) continue;
                    rescued = probe;
                    return true;
                }
            }
            rescued = blocked;
            return false;
        }

        // ──────────────────────────────────────────────────────────────────
        // FIENDSTONE KEEP SITING
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// When terrain shelters this base and ingress runs through a
        /// chokepoint, the Fiendstone Keep belongs AT the primary corridor
        /// (widest arc — the likeliest approach), pulled back toward the
        /// Hall so it stands behind the future wall line. Returns false on
        /// open ground — the caller falls back to base-ring placement.
        /// </summary>
        public static bool TryFindKeepChokeSpot(EntityManager em, float3 hallPos,
            int2 keepSize, out float3 pos)
        {
            pos = default;
            var corridors = new Corridor[MaxCorridors];
            if (!TryAssess(hallPos, corridors, out int corridorCount, out _, out _)) return false;
            if (corridorCount == 0) return false;

            int primary = 0;
            for (int i = 1; i < corridorCount; i++)
                if (corridors[i].ArcFraction > corridors[primary].ArcFraction) primary = i;
            var c = corridors[primary];

            // Behind the choke, pulled toward the Hall (the swept wall axis
            // is not necessarily perpendicular to the approach bearing, so
            // "behind" is hall-ward, not -Approach); lateral nudges keep the
            // 5x5 footprint out of the flanking terrain.
            float3 back3 = hallPos - c.ChokePos;
            back3.y = 0f;
            float backLen = math.length(back3);
            float3 backDir = backLen > 0.01f ? back3 / backLen : -c.Approach;
            float[] laterals = { 0f, 5f, -5f, 10f, -10f };
            for (float back = 10f; back <= 30f; back += 4f)
            {
                for (int l = 0; l < laterals.Length; l++)
                {
                    float3 candidate = c.ChokePos + backDir * back
                        + c.ChokeAxis * laterals[l];
                    candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);
                    if (BuildSiteSnapshot.Current(em)
                            .IsValidBuildPosition(em, candidate, keepSize, null))
                    {
                        pos = candidate;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
