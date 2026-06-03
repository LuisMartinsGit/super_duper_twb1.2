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
    [BurstCompile]
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
    [BurstCompile]
    internal partial struct StampBuildingFootprintSizedJob : IJobEntity
    {
        [NativeDisableParallelForRestriction] public NativeArray<byte> Cost;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Flags;
        public int Width;
        public int Height;
        public float CellSize;
        public float3 Origin;

        public void Execute(in BuildingTag tag, in BuildingSize size, in LocalTransform xf)
        {
            float dx = xf.Position.x - Origin.x;
            float dz = xf.Position.z - Origin.z;
            int cx = (int)math.floor(dx / CellSize);
            int cz = (int)math.floor(dz / CellSize);

            int halfW = size.Width / 2;
            int halfH = size.Height / 2;

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
    /// Clears the layer-0 slab to terrain-walkable so the per-tick building
    /// stamp can write a fresh snapshot. Parallel over rows.
    /// </summary>
    /// <summary>
    /// task-112 follow-up -- stamps the cost field for entities tagged
    /// <see cref="ObstacleTag"/> (iron deposits, crystal nodes, cadavers,
    /// forest macro cells, etc.). Mirror of <see cref="StampBuildingFootprintJob"/>
    /// but reads ObstacleTag instead of BuildingTag. Uses a 3x3 default
    /// footprint; if the entity also carries BuildingSize, that variant
    /// can be added later (most obstacles don't).
    /// </summary>
    [BurstCompile]
    internal partial struct StampObstacleFootprintJob : IJobEntity
    {
        [NativeDisableParallelForRestriction] public NativeArray<byte> Cost;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Flags;
        public int Width;
        public int Height;
        public float CellSize;
        public float3 Origin;

        private const int DefaultFootprint = 3;

        public void Execute(in ObstacleTag tag, in LocalTransform xf)
        {
            float dx = xf.Position.x - Origin.x;
            float dz = xf.Position.z - Origin.z;
            int cx = (int)math.floor(dx / CellSize);
            int cz = (int)math.floor(dz / CellSize);

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

    [BurstCompile]
    internal struct ClearLayer0Job : IJobParallelFor
    {
        // Each Execute(row) writes the entire row [row*Width .. row*Width+Width-1].
        // Rows do not overlap, so disabling the IJobParallelFor "you may only
        // write at the job index" check is safe. Without this attribute the
        // Cost[rowStart + x] write trips the safety system at the first index
        // that isn't equal to `row`.
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<byte> Cost;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<byte> Flags;
        public int Width;

        public void Execute(int row)
        {
            int rowStart = row * Width;
            for (int x = 0; x < Width; x++)
            {
                Cost[rowStart + x] = 0;
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
    [BurstCompile]
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
    [BurstCompile]
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
                    // gate (254). Climb access stays walkable (cost 1)
                    // because units must approach via the ground first.
                    if (IsClimbAccess != 0)
                    {
                        Cost[idxG] = 1; // walkable approach to the stair
                    }
                    else
                    {
                        Cost[idxG] = groundCost;
                    }
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
