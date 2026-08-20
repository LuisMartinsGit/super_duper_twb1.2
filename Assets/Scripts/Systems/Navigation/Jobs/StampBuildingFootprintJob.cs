// StampBuildingFootprintJob.cs
// Stamps every BuildingTag entity's footprint into the layer-0 cost field
// each tick. M1 takes the snapshot approach: clear layer 0 to terrain cost,
// then stamp every building. That keeps M1 free of structural-change ECB
// machinery; M4 will move to dirty-tile incremental rebuild.
//
// Location: Assets/Scripts/Systems/Navigation/Jobs/StampBuildingFootprintJob.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Reads <see cref="BuildingTag"/> entities with <see cref="LocalTransform"/>
    /// and (optional) <see cref="BuildingSize"/>; marks every cell their
    /// footprint covers as impassable.
    ///
    /// Determinism note: writes use plain stores, not interlocked ops, so
    /// in parallel two buildings whose footprints overlap may race on the
    /// same cell. In M1 that's harmless because every overlap result is the
    /// same value (255 / FlagBuildingFootprint). M4's incremental rebuild
    /// switches to Interlocked.Or per DR-6.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal partial struct StampBuildingFootprintJob : IJobEntity
    {
        [NativeDisableParallelForRestriction] public NativeArray<byte> Cost;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Flags;
        public int Width;
        public int Height;
        public float CellSize;
        public float3 Origin;

        // Default building footprint when no BuildingSize is present.
        private const int DefaultFootprint = 3;

        public void Execute(in BuildingTag tag, in LocalTransform xf)
        {
            // Compute centre cell in grid space.
            float dx = xf.Position.x - Origin.x;
            float dz = xf.Position.z - Origin.z;
            int cx = (int)math.floor(dx / CellSize);
            int cz = (int)math.floor(dz / CellSize);

            // Default footprint; overridden below if the entity also has a
            // BuildingSize component. M1 keeps the test scenario simple so
            // BuildingSize lookups aren't exposed to this job — the default
            // 3x3 stamp covers every M1 building footprint on the flat grid.
            int w = DefaultFootprint;
            int h = DefaultFootprint;

            int halfW = w / 2;
            int halfH = h / 2;

            int x0 = math.max(0, cx - halfW);
            int z0 = math.max(0, cz - halfH);
            int x1 = math.min(Width - 1, cx + halfW);
            int z1 = math.min(Height - 1, cz + halfH);

            for (int z = z0; z <= z1; z++)
            {
                int rowStart = z * Width;
                for (int x = x0; x <= x1; x++)
                {
                    int idx = rowStart + x;
                    Cost[idx] = NavCostField.CostImpassable;
                    Flags[idx] = (byte)(Flags[idx] | NavCostField.FlagBuildingFootprint);
                }
            }
        }
    }

    /// <summary>
    /// Variant that accepts a <see cref="BuildingSize"/> footprint. Run
    /// after <see cref="StampBuildingFootprintJob"/> to refine the stamp
    /// for entities that carry an explicit size.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal partial struct StampBuildingFootprintSizedJob : IJobEntity
    {
        [NativeDisableParallelForRestriction] public NativeArray<byte> Cost;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Flags;
        public int Width;
        public int Height;
        public float CellSize;
        public float3 Origin;

        /// <summary>How much of each side a building stops blocking, in metres.
        /// One nav cell — the smallest amount that can actually free a cell,
        /// since the stamp rounds outward to every touched cell.</summary>
        public const float EdgeClearance = 1f;

        public void Execute(in BuildingTag tag, in BuildingSize size, in LocalTransform xf)
        {
            float dx = xf.Position.x - Origin.x;
            float dz = xf.Position.z - Origin.z;

            // Stamp exactly the cells the CENTRED footprint span
            // [pos - size/2, pos + size/2) intersects. The old integer form
            // (cell(pos) +/- size/2) stamped size+1 cells for EVEN footprints
            // — a 4x4 Hall blocked 5x5, hanging one extra metre off the +x/+z
            // side of its authored position. That bias is what made "the Hall
            // is not exactly on its marker": the blocked rect, not the entity,
            // was off-centre, silently eating authored clearances (and the
            // spawn ring) beside it.
            float halfW = size.Width * 0.5f * CellSize;
            float halfH = size.Height * 0.5f * CellSize;

            // EDGE CLEARANCE: a building stops blocking its outermost ring, so
            // two buildings placed flush leave a walkable lane between them and
            // units can move between the houses instead of treating a block of
            // them as one solid wall. Buildings tile the 2 m build grid exactly
            // — flush footprints touch with zero space — and the stamp below
            // rounds outward to every cell a footprint TOUCHES, so nothing
            // narrower than a whole nav cell frees anything at all.
            //
            // The cost is that the blocked rect is one metre smaller per side
            // than the visual, i.e. units clip slightly into building edges.
            // Deliberate trade (user call 2026-08-15).
            //
            // NOT applied when it would consume the whole footprint: a 1-cell
            // building (Hut is 1 cell — docs/Design/Build_Grid.md) would block
            // nothing at all and units would walk straight through it. Those
            // keep their full stamp and stay solid when placed flush.
            //
            // Walls never reach this job — CostFieldStampSystem's building
            // queries exclude WallTag and stamp walls through their own path,
            // which is what keeps a wall line sealed.
            if (halfW > EdgeClearance) halfW -= EdgeClearance;
            if (halfH > EdgeClearance) halfH -= EdgeClearance;

            int x0 = math.max(0, (int)math.floor((dx - halfW) / CellSize));
            int z0 = math.max(0, (int)math.floor((dz - halfH) / CellSize));
            int x1 = math.min(Width - 1, (int)math.ceil((dx + halfW) / CellSize) - 1);
            int z1 = math.min(Height - 1, (int)math.ceil((dz + halfH) / CellSize) - 1);

            for (int z = z0; z <= z1; z++)
            {
                int rowStart = z * Width;
                for (int x = x0; x <= x1; x++)
                {
                    int idx = rowStart + x;
                    Cost[idx] = NavCostField.CostImpassable;
                    Flags[idx] = (byte)(Flags[idx] | NavCostField.FlagBuildingFootprint);
                }
            }
        }
    }

    /// <summary>
    /// Clears the layer-0 slab to terrain-walkable so the per-tick building
    /// stamp can write a fresh snapshot. Parallel over rows.
    /// </summary>
    /// <summary>
    /// task-112 follow-up -- stamps the cost field for entities tagged
    /// <see cref="ObstacleTag"/> (iron deposits, veilstone nodes, outcroppings,
    /// forest macro cells, etc.). Mirror of <see cref="StampBuildingFootprintJob"/>
    /// but reads ObstacleTag instead of BuildingTag.
    ///
    /// Build-grid rework: every node occupies exactly ONE 2 m build cell and
    /// is impassable while it exists, so the stamp is a centred 2x2 nav-cell
    /// span rather than the old hardcoded 3x3. The 3x3 blocked a metre of
    /// ground the node did not own on every side, which is what made ore
    /// patches feel like walls. Uses the same centred-span math as
    /// <see cref="StampBuildingFootprintSizedJob"/> so an even footprint
    /// stamps exactly its own cells. docs/Design/Build_Grid.md
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal partial struct StampObstacleFootprintJob : IJobEntity
    {
        [NativeDisableParallelForRestriction] public NativeArray<byte> Cost;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Flags;
        public int Width;
        public int Height;
        public float CellSize;
        public float3 Origin;

        /// <summary>One build cell, in metres. Kept as a literal because
        /// Burst jobs cannot read the managed BuildGrid constant.</summary>
        private const float NodeFootprintMeters = 2f;

        public void Execute(in ObstacleTag tag, in LocalTransform xf)
        {
            float dx = xf.Position.x - Origin.x;
            float dz = xf.Position.z - Origin.z;

            float half = NodeFootprintMeters * 0.5f;

            int x0 = math.max(0, (int)math.floor((dx - half) / CellSize));
            int z0 = math.max(0, (int)math.floor((dz - half) / CellSize));
            int x1 = math.min(Width - 1, (int)math.ceil((dx + half) / CellSize) - 1);
            int z1 = math.min(Height - 1, (int)math.ceil((dz + half) / CellSize) - 1);

            for (int z = z0; z <= z1; z++)
            {
                int rowStart = z * Width;
                for (int x = x0; x <= x1; x++)
                {
                    int idx = rowStart + x;
                    Cost[idx] = NavCostField.CostImpassable;
                    Flags[idx] = (byte)(Flags[idx] | NavCostField.FlagBuildingFootprint);
                }
            }
        }
    }

    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal struct ClearLayer0Job : IJobParallelFor
    {
        // Each Execute(row) writes the entire row [row*Width .. row*Width+Width-1].
        // Rows do not overlap, so disabling the IJobParallelFor "you may only
        // write at the job index" check is safe. Without this attribute the
        // Cost[rowStart + x] write trips the safety system at the first index
        // that isn't equal to `row`.
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<byte> Cost;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<byte> Flags;
        // Baked layer-0 terrain mask (water + over-budget slope). Same length
        // and row-major layout as the layer-0 slab. Each cell is seeded to its
        // terrain value so deep water / steep mountain reads as impassable
        // BEFORE buildings, obstacles, and walls stamp on top. Stays all-zero
        // (walkable, == the old behaviour) on terrain-less scenes.
        [ReadOnly] public NativeArray<byte> TerrainCost;
        public int Width;

        public void Execute(int row)
        {
            int rowStart = row * Width;
            for (int x = 0; x < Width; x++)
            {
                Cost[rowStart + x] = TerrainCost[rowStart + x];
                Flags[rowStart + x] = 0;
            }
        }
    }

    /// <summary>
    /// task-112 M5 -- clears a non-zero layer (e.g. Rampart = layer 1)
    /// to <see cref="NavCostField.CostImpassable"/>. Rampart cells start
    /// impassable everywhere; walls then stamp walkable cells onto their
    /// own footprint via <see cref="StampWallLayersJob"/>. Parallel over
    /// rows within the layer's slab.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal struct ClearLayerImpassableJob : IJobParallelFor
    {
        // Same per-row-disjoint write pattern as ClearLayer0Job -- see the
        // comment there. NativeDisableParallelForRestriction is required.
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<byte> Cost;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<byte> Flags;
        public int Width;
        /// <summary>Offset (in cell indices) of the layer's slab start.
        /// Layer-major: <c>layer * (Width * Height)</c>.</summary>
        public int LayerOffset;

        public void Execute(int row)
        {
            int rowStart = LayerOffset + row * Width;
            for (int x = 0; x < Width; x++)
            {
                Cost[rowStart + x] = NavCostField.CostImpassable;
                Flags[rowStart + x] = 0;
            }
        }
    }

    /// <summary>
    /// task-112 M5 -- per-wall stamp pass. Reads <see cref="WallTag"/>
    /// entities with a <see cref="LocalTransform"/> and writes the wall
    /// footprint into BOTH the Ground (layer 0 = impassable, sentinel
    /// 254 at gate cells) and the Rampart (layer 1 = walkable cost 1)
    /// cost slabs.
    ///
    /// Determinism: writes are idempotent for overlapping wall
    /// footprints (every wall picks 255 / 254 for ground, 1 for
    /// rampart). The companion flag bits
    /// (<see cref="NavCostField.FlagStaticWall"/>,
    /// <see cref="NavCostField.FlagGate"/>) are set by OR.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal partial struct StampWallLayersJob : IJobEntity
    {
        [NativeDisableParallelForRestriction] public NativeArray<byte> Cost;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Flags;
        public int Width;
        public int Height;
        public float CellSize;
        public float3 Origin;
        public int LayerArea; // == Width * Height
        public byte HasBuildingSizeFootprint;
        public byte IsGate;
        public byte IsClimbAccess;

        public void Execute(in WallTag wall, in LocalTransform xf, in FactionTag faction)
        {
            // Wall instance footprint: 7x7. See history above for the
            // sealing math (3 cells of overlap between 4 m-spaced cubes,
            // blocks Bresenham diagonal tunneling).
            int w = 7;
            int h = 7;
            // Gate cells encode owner faction in the low 3 bits of Flags
            // so faction-aware LOS / obstacle-avoidance probes can tell
            // who is allowed through. Owner faction value is the Faction
            // enum index (Blue=0..White=7).
            byte ownerBits = (byte)((byte)faction.Value & NavCostField.FlagOwnerMask);
            StampFootprint(xf.Position, w, h, ownerBits);
        }

        private void StampFootprint(float3 pos, int w, int h, byte ownerBits)
        {
            float dx = pos.x - Origin.x;
            float dz = pos.z - Origin.z;
            int cx = (int)math.floor(dx / CellSize);
            int cz = (int)math.floor(dz / CellSize);
            int halfW = w / 2;
            int halfH = h / 2;
            int x0 = math.max(0, cx - halfW);
            int z0 = math.max(0, cz - halfH);
            int x1 = math.min(Width - 1, cx + halfW);
            int z1 = math.min(Height - 1, cz + halfH);

            byte groundCost = IsGate != 0
                ? NavCostField.CostConditional
                : NavCostField.CostImpassable;
            byte flagBit = IsGate != 0
                ? NavCostField.FlagGate
                : (IsClimbAccess != 0
                    ? NavCostField.FlagClimbAccess
                    : NavCostField.FlagStaticWall);

            for (int z = z0; z <= z1; z++)
            {
                int rowG = z * Width;
                int rowR = LayerArea + z * Width;
                for (int x = x0; x <= x1; x++)
                {
                    int idxG = rowG + x;
                    int idxR = rowR + x;

                    // Ground layer: impassable wall (255), conditional at
                    // gate (254).
                    //
                    // Hubs (IsClimbAccess) are IMPASSABLE too. They used to
                    // stamp cost 1 here — "walkable approach to the stair" —
                    // back when walls carried a walkable rampart deck and the
                    // hub was its stair core. The compact-wall rework
                    // (2026-08-09) made walls solid curtain walls with NO
                    // deck, but this carve stayed: running AFTER the plain-
                    // wall pass, it re-opened the hub's whole 7x7 footprint,
                    // punching a ~7 m walkable corridor clean through the
                    // wall line at every bastion — including over the first
                    // curtain module on each side. That is the "units slip
                    // through the wall where it meets the hub" leak: the join
                    // between segments was the one place the wall was open.
                    // The FlagClimbAccess bit is still written below so
                    // WallPortalDetectionSystem keeps emitting its layer-0/1
                    // climb portal; only the ground hole is gone.
                    Cost[idxG] = groundCost;
                    // Preserve any existing flag bits / owner bits, then
                    // overlay this stamp's flag + owner. For gates we
                    // write owner bits; for plain walls / climbs we
                    // leave them at 0 (irrelevant for non-gate cells).
                    byte newFlags = (byte)(Flags[idxG] | flagBit | NavCostField.FlagBuildingFootprint);
                    if (IsGate != 0)
                    {
                        // Clear any prior owner bits in this cell first,
                        // then OR in our owner. This handles the case where
                        // a previous tick's stamp wrote a different owner
                        // (or where a wall stamped 0 then the gate stamps
                        // here -- gate wins).
                        newFlags = (byte)((newFlags & ~NavCostField.FlagOwnerMask) | ownerBits);
                    }
                    Flags[idxG] = newFlags;

                    // Rampart layer: walkable wall-top (cost 1) for every
                    // wall footprint cell -- units can patrol the parapet.
                    Cost[idxR] = 1;
                    Flags[idxR] = (byte)(Flags[idxR] | NavCostField.FlagStaticWall);
                }
            }
        }
    }
}
