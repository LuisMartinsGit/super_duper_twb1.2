// TerrainCostBakeSystem.cs
// Bakes terrain traversability (deep water + over-budget slope) into the
// nav cost field ONCE, so the flow-field planner — not just a reactive
// per-step clamp — avoids water and mountains.
//
// Why this exists:
//   The task-112 nav rewrite made NavCostField the single source of truth
//   for pathing, but the terrain stamp was left a stub (StampTerrainCostJob
//   wrote 0 everywhere and was never even scheduled). The result was that
//   flow fields and the portal graph treated water / mountains as flat
//   walkable ground, so units routed straight into the lake and up the
//   massif on hand-authored maps (e.g. Yiel Lymwérra). PassabilityGrid
//   already computes the correct water + slope mask from the terrain but
//   was disconnected from the pathing layer. This system reconnects them:
//   it projects PassabilityGrid's terrain-blocked cells onto
//   NavCostField.TerrainCost, which ClearLayer0Job then uses as the per-tick
//   layer-0 clear value.
//
// Propagation is automatic after the bake: changing the layer-0 cost cells
// makes BuildingCostStampSystem's per-tick shadow diff mark the affected
// tiles dirty, which drives IncrementalPortalRebuildSystem to rebuild the
// portal graph (and invalidate the matching flow-cache slabs) with terrain
// present. No manual dirty-marking needed here.
//
// Terrain-less scenes (nav-stack test scenarios) have no PassabilityGrid, so
// the bake simply never fires and TerrainCost stays all-walkable — identical
// to the pre-bake behaviour.
//
// NOT [BurstCompile]: reads the managed PassabilityGrid singleton.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// One-shot system (InitializationSystemGroup, after
    /// <see cref="NavGridBootstrapSystem"/>) that fills
    /// <see cref="NavCostField.TerrainCost"/> from the terrain-derived
    /// <see cref="PassabilityGrid"/>. Runs every frame until both the cost
    /// field and PassabilityGrid are ready, then bakes once and goes idle.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [UpdateAfter(typeof(NavGridBootstrapSystem))]
    public partial struct TerrainCostBakeSystem : ISystem
    {
        private byte _baked;

        /// <summary>
        /// Which NavCostField entity the current bake belongs to.
        ///
        /// A one-shot `_baked` latch alone is WRONG now that the nav grid
        /// rebuilds itself per match: NavGridBootstrapSystem allocates a fresh
        /// TerrainCost array with ClearMemory — i.e. every cell walkable — and
        /// with the latch still set this system never refilled it. Mountains
        /// and water stopped blocking from the second match onward, so units
        /// walked (and shot) straight over terrain that should have stopped
        /// them. The file header above predicted exactly this failure.
        ///
        /// Comparing the singleton ENTITY is the reliable test: a rebuild
        /// creates a brand-new entity, and Entity equality is by index+version,
        /// so it stays valid to compare even after the old one is destroyed.
        /// (Comparing the array handle instead would risk a false match if the
        /// allocator handed back the same address and length.)
        /// </summary>
        private Entity _bakedFor;

        /// <summary>
        /// Which <see cref="PassabilityGrid.MaskGeneration"/> the current bake
        /// was taken from. The mask is rebuilt on its own schedule (terrain
        /// ready, map reload); a bake taken from an older generation describes
        /// terrain that no longer exists.
        /// </summary>
        private int _bakedMaskGeneration;

        /// <summary>
        /// How many times we have refused to latch a bake that found no blocked
        /// cells at all while the mask insists it has some. Bounded so a
        /// genuinely mismatched pair of grids reports and moves on instead of
        /// screaming every frame for the whole match.
        /// </summary>
        private int _mismatchRetries;
        private const int MaxMismatchRetries = 3;

        public void OnCreate(ref SystemState state)
        {
            _baked = 0;
            _bakedFor = Entity.Null;
            _bakedMaskGeneration = -1;
            _mismatchRetries = 0;
            state.RequireForUpdate<NavCostField>();
            state.RequireForUpdate<NavGridSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var costEntity = SystemAPI.GetSingletonEntity<NavCostField>();

            // PassabilityGrid is a managed MonoBehaviour built from the terrain
            // on an async coroutine.
            //
            // IsMaskReady, NOT Cells.IsCreated: the array is allocated one
            // statement before it is filled, and it is zero-initialised — zero
            // being Passable. Gating on IsCreated let this system bake a
            // terrain layer from a blank mask, latch it, and never look again;
            // water, cliffs and every bridge crossing were disabled for the
            // rest of the match. See the readiness note in PassabilityGrid.
            var pg = PassabilityGrid.Instance;
            if (pg == null || !pg.IsMaskReady) return;

            if (_baked != 0 && costEntity == _bakedFor
                && pg.MaskGeneration == _bakedMaskGeneration) return;

            var field = SystemAPI.GetSingleton<NavCostField>();
            var grid = SystemAPI.GetSingleton<NavGridSingleton>();
            if (!field.TerrainCost.IsCreated) return;

            // Defensive: drain anything that might still be reading the cost
            // field's arrays before we write the terrain mask in place.
            state.CompleteDependency();

            int w = field.Width;
            int h = field.Height;
            float cs = grid.CellSize;
            float3 origin = grid.Origin;

            // Split the tally. A nav grid is deliberately one cell larger than
            // the map on each side, so its outer ring projects OUTSIDE the
            // passability grid and reads blocked-by-being-off-map. Counting
            // that ring together with real terrain hid the Twin Spans failure
            // completely: the shipped log read "1412/125316 impassable", and
            // 1412 is exactly 354x354 - 352x352 — the ring, and nothing else.
            int blocked = 0;        // in-bounds cells the terrain mask rejects
            int outside = 0;        // nav cells with no passability cell at all
            int deckOnly = 0;
            int mount = 0;

            for (int z = 0; z < h; z++)
            {
                int rowStart = z * w;
                float worldZ = origin.z + (z + 0.5f) * cs;
                for (int x = 0; x < w; x++)
                {
                    float worldX = origin.x + (x + 0.5f) * cs;

                    // Project the nav cell centre onto the PassabilityGrid
                    // (different cell size / origin in general — go via world
                    // space). GetCell returns TerrainBlocked for off-map
                    // cells, so terrain past the map edge is impassable too.
                    int2 pgCell = pg.WorldToCell(new float3(worldX, 0f, worldZ));
                    bool inside = pgCell.x >= 0 && pgCell.x < pg.Width
                               && pgCell.y >= 0 && pgCell.y < pg.Height;
                    byte v = pg.GetCell(pgCell);

                    // ONLY terrain blocks the baked mask — buildings, walls,
                    // and obstacles are stamped per-tick by the ECS stamp
                    // systems and must not be frozen into the terrain layer.
                    if (v == PassabilityGrid.TerrainBlocked)
                    {
                        field.TerrainCost[rowStart + x] = NavCostField.CostImpassable;
                        if (inside) blocked++; else outside++;
                    }
                    else if (pg.IsBridgeDeckOnly(pgCell))
                    {
                        // Walkable only via a bridge deck (cliff / NoWalk
                        // ground beneath): expensive, so planning crosses
                        // it only for genuine bridge routes.
                        field.TerrainCost[rowStart + x] = NavCostField.CostBridgeDeckOnly;
                        deckOnly++;
                    }
                    else if (pg.IsBridgeMount(pgCell))
                    {
                        // Deck touchdown (ramp toe): the legal entrance of a
                        // deck-only strip for the flow-field integration.
                        field.TerrainCost[rowStart + x] = NavCostField.CostBridgeMount;
                        mount++;
                    }
                    else
                    {
                        field.TerrainCost[rowStart + x] = 0;
                    }
                }
            }

            // The mask says it blocked cells; the projection onto the nav grid
            // found none of them. The two grids do not line up, or the mask was
            // read before it was written. Latching that costs the whole match —
            // no water, no cliffs, no bridges — so refuse to latch and try
            // again, up to a bound.
            if (blocked == 0 && pg.BlockedCellCount > 0
                && _mismatchRetries < MaxMismatchRetries)
            {
                _mismatchRetries++;
                UnityEngine.Debug.LogError(
                    $"[TerrainCostBake] the passability mask blocks {pg.BlockedCellCount} cells but the " +
                    $"projection onto the nav grid found NONE of them in bounds ({outside} nav cells fell " +
                    "outside the mask entirely). Terrain blocking and every bridge crossing would be " +
                    $"disabled. nav grid {w}x{h} cell={cs:0.##} origin=({origin.x:0.0},{origin.z:0.0}); " +
                    $"passability grid {pg.Width}x{pg.Height} cell={pg.CellSize:0.##} " +
                    $"origin=({pg.Origin.x:0.0},{pg.Origin.z:0.0}). " +
                    $"Not latching — retry {_mismatchRetries}/{MaxMismatchRetries} next frame.");
                return;
            }
            _mismatchRetries = 0;

            // Latch TerrainBaked so the change-gated CostFieldStampSystem
            // re-stamps once and seeds the freshly-baked terrain into layer-0.
            field.TerrainBaked = 1;
            SystemAPI.SetSingleton(field);

            _baked = 1;
            _bakedFor = costEntity;                     // re-bake when the grid is rebuilt
            _bakedMaskGeneration = pg.MaskGeneration;   // …and when the mask is
            UnityEngine.Debug.Log(
                $"[TerrainCostBake] baked terrain into cost field: {blocked}/{w * h} cells " +
                $"impassable (water + slope > {PassabilityGrid.MaxWalkableSlope:0.##} budget), " +
                $"plus {outside} outside the passability grid; " +
                $"bridges: {deckOnly} deck-only, {mount} mount.");

            // Tripwire: an (almost) fully-blocked bake means the terrain mask
            // is wrong (e.g. a water level above the whole terrain). Pathing
            // degenerates into steering-only wandering in that state — make
            // the failure impossible to miss.
            if (blocked > (w * h * 8) / 10)
            {
                UnityEngine.Debug.LogError(
                    $"[TerrainCostBake] {blocked}/{w * h} cells impassable — the terrain mask has " +
                    "blocked (nearly) the whole map and navigation WILL misbehave. Check the " +
                    "PassabilityGrid water level / slope budget against this map's terrain.");
            }
        }
    }
}
