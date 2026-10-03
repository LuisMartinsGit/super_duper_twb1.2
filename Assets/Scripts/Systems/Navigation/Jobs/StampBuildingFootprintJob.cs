// StampBuildingFootprintJob.cs
// Stamps every BuildingTag entity's footprint into the layer-0 cost field
// each tick. M1 takes the snapshot approach: clear layer 0 to terrain cost,
// then stamp every building. That keeps M1 free of structural-change ECB
// machinery; M4 will move to dirty-tile incremental rebuild.

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

        /// <summary>One build cell, in metres — obstacles with no
        /// NodeFootprint (rocks, props). Kept as a literal because Burst jobs
        /// cannot read the managed BuildGrid constant.</summary>
        private const float DefaultFootprintMeters = 2f;

        /// <summary>Resource nodes' own footprint (4 m, Build_Grid.md §3).</summary>
        [Unity.Collections.ReadOnly] public ComponentLookup<NodeFootprint> Footprints;

        public void Execute(Entity entity, in ObstacleTag tag, in LocalTransform xf)
        {
            float dx = xf.Position.x - Origin.x;
            float dz = xf.Position.z - Origin.z;

            float meters = Footprints.HasComponent(entity)
                ? Footprints[entity].Meters : DefaultFootprintMeters;
            float half = meters * 0.5f;

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
    /// 254 at the gate's opening) and the Rampart (layer 1 = walkable cost 1)
    /// cost slabs.
    ///
    /// THE FOOTPRINT FOLLOWS THE WALL (2026-10-02). Every piece used to stamp
    /// the same 7 x 7 square around its centre, whatever it was — a 7 m
    /// block for a 1 m palisade, a 7 m deck for a wall with no walk at all.
    /// Each piece now stamps a rectangle turned to its own heading
    /// (docs/Design/Age_1_Alanthor.md § The stone wall):
    ///
    ///   * along the wall: the 3 m module plus a 0.5 m lap each end, so the
    ///     pieces of a curved run overlap and the line has no seam — and a
    ///     dead module leaves a 2 m breach between its neighbours.
    ///   * across the wall: the STONE wall blocks 5 m of ground (its 4 m plus
    ///     a margin) and is walkable on the rampart layer over its ~3.2 m
    ///     walkway; a PALISADE blocks a 3 m band and has no deck.
    ///   * a hub is a 5 m square (its 4.2 m drum), walkable on top when stone.
    ///   * a gate blocks its whole span except the middle third — the
    ///     opening — which is the conditional (owner-only) cell; its deck
    ///     runs over the passage roof.
    ///
    /// A cell counts when its CENTRE is inside the rectangle, which for a
    /// band at least 3 cells wide leaves no diagonal gap to slip through.
    ///
    /// Determinism: writes are idempotent for overlapping wall footprints
    /// (every wall picks 255 / 254 for ground, 1 for rampart); the companion
    /// flag bits are set by OR. Runs serial under lockstep (see the caller).
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

        [ReadOnly] public ComponentLookup<PalisadeTag> Palisade;
        [ReadOnly] public ComponentLookup<WallHubTag> Hub;
        [ReadOnly] public ComponentLookup<WallGateSpan> GateSpan;

        /// <summary>Side of the square that bounds what any wall piece
        /// stamps, in cells. Public so the input layer can tell WHICH wall a
        /// deck cell belongs to (SelectionOrders.IsFriendlyRampartDeck).</summary>
        public const int FootprintCells = 5;

        /// <summary>Half the ground band a STONE wall blocks across itself:
        /// its 4 m depth plus half a metre each side.</summary>
        public const float StoneHalfAcross = 2.5f;
        /// <summary>Half the walkable deck across a stone wall: the walkway
        /// between its two parapets.</summary>
        public const float DeckHalfAcross = 1.6f;
        /// <summary>Half the ground band a PALISADE blocks: 3 cells.</summary>
        public const float PalisadeHalfAcross = 1.5f;
        /// <summary>Half a module along the wall plus the 0.5 m lap.</summary>
        public const float ModuleHalfAlong = 2f;
        /// <summary>Half a hub's square (its 4.2 m drum, rounded up).</summary>
        public const float HubHalf = 2.5f;
        /// <summary>Half the gate's opening along the wall — the middle
        /// third of a three-module gatehouse, plus the lap.</summary>
        public const float GateOpeningHalf = 2f;

        public void Execute(Entity e, in WallTag wall, in LocalTransform xf, in FactionTag faction)
        {
            bool palisade = Palisade.HasComponent(e);
            bool hub = Hub.HasComponent(e);

            // The piece's own frame: its forward runs along the wall (cells
            // and gates are spawned facing the wall's tangent); a hub is
            // round, so its square is laid on the world axes.
            float3 fwd = math.mul(xf.Rotation, new float3(0f, 0f, 1f));
            fwd.y = 0f;
            fwd = math.lengthsq(fwd) > 1e-6f ? math.normalize(fwd) : new float3(0f, 0f, 1f);
            if (hub) fwd = new float3(0f, 0f, 1f);

            float halfAlong, halfAcross;
            if (hub)
            {
                halfAlong = HubHalf;
                halfAcross = HubHalf;
            }
            else
            {
                halfAlong = ModuleHalfAlong;
                if (IsGate != 0 && GateSpan.HasComponent(e))
                    halfAlong = GateSpan[e].Metres * 0.5f + 0.5f;
                halfAcross = palisade ? PalisadeHalfAcross : StoneHalfAcross;
            }

            // Only the stone wall has a wall-walk.
            float deckAlong = hub ? HubHalf : halfAlong;
            float deckAcross = palisade ? -1f : (hub ? HubHalf : DeckHalfAcross);

            byte ownerBits = (byte)((byte)faction.Value & NavCostField.FlagOwnerMask);
            StampFootprint(xf.Position, fwd, halfAlong, halfAcross, deckAlong, deckAcross,
                           ownerBits, climb: IsClimbAccess != 0 && !palisade);
        }

        private void StampFootprint(float3 pos, float3 fwd, float halfAlong, float halfAcross,
            float deckAlong, float deckAcross, byte ownerBits, bool climb)
        {
            float3 right = new float3(fwd.z, 0f, -fwd.x);
            float reach = math.sqrt(halfAlong * halfAlong + halfAcross * halfAcross);

            int x0 = math.max(0, (int)math.floor((pos.x - reach - Origin.x) / CellSize));
            int z0 = math.max(0, (int)math.floor((pos.z - reach - Origin.z) / CellSize));
            int x1 = math.min(Width - 1, (int)math.floor((pos.x + reach - Origin.x) / CellSize));
            int z1 = math.min(Height - 1, (int)math.floor((pos.z + reach - Origin.z) / CellSize));

            for (int z = z0; z <= z1; z++)
            {
                int rowG = z * Width;
                int rowR = LayerArea + z * Width;
                float cz = Origin.z + (z + 0.5f) * CellSize - pos.z;
                for (int x = x0; x <= x1; x++)
                {
                    float cx = Origin.x + (x + 0.5f) * CellSize - pos.x;
                    float along = cx * fwd.x + cz * fwd.z;
                    float across = cx * right.x + cz * right.z;
                    if (math.abs(along) > halfAlong || math.abs(across) > halfAcross) continue;

                    int idxG = rowG + x;
                    int idxR = rowR + x;

                    // Ground: impassable wall (255); a gate's opening is the
                    // conditional cell (254) that only its owner passes.
                    //
                    // Hubs are IMPASSABLE too — see the 2026-08-09 history:
                    // the climb pass used to re-open the hub's footprint to
                    // cost 1 and was the hole every wall had at its bastions.
                    // FlagClimbAccess is still written so
                    // WallPortalDetectionSystem keeps emitting its climb
                    // portal; only the ground hole is gone.
                    bool opening = IsGate != 0 && math.abs(along) <= GateOpeningHalf;
                    byte flagBit = opening ? NavCostField.FlagGate
                                 : climb ? NavCostField.FlagClimbAccess
                                 : NavCostField.FlagStaticWall;
                    Cost[idxG] = opening ? NavCostField.CostConditional : NavCostField.CostImpassable;
                    byte newFlags = (byte)(Flags[idxG] | flagBit | NavCostField.FlagBuildingFootprint);
                    if (opening)
                    {
                        // The gate wins the cell: clear any prior owner bits,
                        // then OR in its owner.
                        newFlags = (byte)((newFlags & ~NavCostField.FlagOwnerMask) | ownerBits);
                    }
                    Flags[idxG] = newFlags;

                    // Rampart: walkable over the wall-walk only.
                    if (math.abs(along) <= deckAlong && math.abs(across) <= deckAcross)
                    {
                        Cost[idxR] = 1;
                        Flags[idxR] = (byte)(Flags[idxR] | NavCostField.FlagStaticWall);
                    }
                }
            }
        }
    }
}
