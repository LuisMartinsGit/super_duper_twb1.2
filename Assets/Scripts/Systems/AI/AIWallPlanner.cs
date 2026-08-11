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
//      (military production, Temple, Smelters, the near Gatherer's Huts),
//      with a gate facing each cardinal direction and towers on the wall.
//
// The assessment is TERRAIN-ONLY (PassabilityGrid cell value ==
// TerrainBlocked: slope / water / NoWalk paint / mountain mask; map edge
// counts as blocked). Buildings and tree/rock obstacles never count as
// shelter — they can be razed.
//
// All scans are deterministic (fixed bearings, fixed step sizes, no RNG),
// so lockstep peers running the same plan agree.
//
// Location: Assets/Scripts/Systems/AI/AIWallPlanner.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    /// <summary>Frozen wall plan for one AI faction. Stamped on the brain
    /// entity by the endgame system the first time the wall doctrine runs;
    /// never recomputed (the wall keeps building toward a stable shape).</summary>
    public struct AIWallPlan : IComponentData
    {
        /// <summary>One of the AIWallPlanner.Mode* values.</summary>
        public byte Mode;
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
    }

    /// <summary>
    /// Static planner: shelter scan, ingress-corridor extraction, chokepoint
    /// cross-sections, perimeter rectangle, and slot-list generation.
    /// </summary>
    public static class AIWallPlanner
    {
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

        // ── Slot flags ────────────────────────────────────────────────────
        /// <summary>The segment from this slot to the NEXT slot of the same
        /// chain becomes a gate once both hubs stand.</summary>
        public const byte FlagGateAfter = 1;
        /// <summary>The wall instance nearest this slot converts to a
        /// Wall Tower once built.</summary>
        public const byte FlagTower = 2;
        /// <summary>Slot proved unplaceable at execution time — skip it
        /// forever (the terrain-blocked slots never enter the buffer).</summary>
        public const byte FlagDead = 128;

        // ── Shelter scan tuning ───────────────────────────────────────────
        private const int Bearings = 48;          // 7.5 degree resolution
        private const float ScanStart = 12f;      // clear of the Hall footprint
        private const float ScanEnd = 88f;        // "near the base" horizon
        private const float ScanStep = 2f;
        /// <summary>Consecutive terrain-blocked samples that count as a real
        /// barrier (6 m deep) — filters single-cell slope noise.</summary>
        private const int ShelterRunSamples = 3;
        /// <summary>Open-arc budget: above this fraction of open bearings
        /// the base does not count as terrain-sheltered.</summary>
        private const float MaxOpenFraction = 0.45f;
        private const int MaxCorridors = 3;
        /// <summary>Widest corridor cross-section a wall line will seal —
        /// at 30 m hub spacing a 60 m line is only 2-3 curtains, so wide
        /// mountain passes are still worth sealing.</summary>
        public const float MaxSealableWidth = 60f;
        /// <summary>Per-flank probe cap for cross-section width.</summary>
        private const float ChokeProbeCap = 34f;

        // ── Plan geometry ─────────────────────────────────────────────────
        /// <summary>Maximum hub spacing along a planned line — long 30 m
        /// curtains between bastions. AlanthorWall.CreateSegment tiles 3 m
        /// modules across any span, so segment length is unconstrained; the
        /// 16 m WallAutoSegmentSystem rule is a disabled AUTO-link rule, not
        /// a segment limit, and the doctrine links plan neighbours
        /// explicitly (see WallLinkRadius in the endgame system).</summary>
        public const float HubSpacing = 30f;
        /// <summary>Perimeter padding beyond the outermost enclosed
        /// building.</summary>
        private const float PerimeterPad = 12f;
        private const float PerimeterHalfExtentMin = 36f;
        private const float PerimeterHalfExtentMax = 62f;
        /// <summary>Buildings farther than this from the Hall are outlying
        /// expansion, not base — the perimeter does not chase them.</summary>
        private const float PerimeterGatherRadius = 60f;

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
                for (float r = ScanStart; r <= ScanEnd; r += ScanStep)
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

            if (openFraction > MaxOpenFraction)
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

            for (float d = ScanStart + 2f; d <= 70f; d += 2f)
            {
                float3 p = hallPos + dir * d;
                for (int a = -3; a <= 3; a++)
                {
                    float ang = baseAng + a * sweepStep;
                    float3 axis = new float3(math.cos(ang), 0f, math.sin(ang));
                    if (!TryTerrainClearance(p, axis, out float left)) continue;
                    if (!TryTerrainClearance(p, -axis, out float right)) continue;
                    float width = left + right;
                    if (width > MaxSealableWidth) continue;
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
            for (float s = 1f; s <= ChokeProbeCap; s += 1f)
            {
                if (TerrainBlockedAt(from.x + stepDir.x * s, from.z + stepDir.z * s))
                {
                    if (++run >= 2) { clearance = s - run; return true; }
                }
                else run = 0;
            }
            clearance = ChokeProbeCap;
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
            int segs = math.max(1, (int)math.ceil(span / HubSpacing));
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
                if (BuildCommandHelper.IsValidBuildPosition(em, p, hubSize)) return p;
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
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
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
                    if (dx * dx + dz * dz > PerimeterGatherRadius * PerimeterGatherRadius)
                        continue;
                    mn = math.min(mn, new float2(p.x, p.z));
                    mx = math.max(mx, new float2(p.x, p.z));
                }
            }

            float2 center = (mn + mx) * 0.5f;
            float2 he = math.clamp((mx - mn) * 0.5f + PerimeterPad,
                PerimeterHalfExtentMin, PerimeterHalfExtentMax);

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
                int segs = math.max(1, (int)math.ceil(len / HubSpacing));
                int gateFrom = segs / 2;   // segment nearest the side midpoint

                // Slot at each fraction j/segs; the next side contributes
                // the shared corner, so stop short of t = 1.
                for (int j = 0; j < segs; j++)
                {
                    float2 xz = math.lerp(a, b, j / (float)segs);
                    if (TerrainBlockedAt(xz.x, xz.y)) continue;

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
                    if (BuildCommandHelper.IsValidBuildPosition(em, candidate, keepSize))
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
