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
//
// Location: Assets/Scripts/Systems/Navigation/TerrainCostBakeSystem.cs

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

        public void OnCreate(ref SystemState state)
        {
            _baked = 0;
            state.RequireForUpdate<NavCostField>();
            state.RequireForUpdate<NavGridSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_baked != 0) return;

            // PassabilityGrid is a managed MonoBehaviour built from the terrain
            // on an async coroutine; wait until it has produced its mask. Its
            // Cells handle becomes valid only after GenerateFromTerrain runs.
            var pg = PassabilityGrid.Instance;
            if (pg == null || !pg.Cells.IsCreated) return;

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

            int blocked = 0;
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
                    byte v = pg.GetCell(pgCell);

                    // ONLY terrain blocks the baked mask — buildings, walls,
                    // and obstacles are stamped per-tick by the ECS stamp
                    // systems and must not be frozen into the terrain layer.
                    if (v == PassabilityGrid.TerrainBlocked)
                    {
                        field.TerrainCost[rowStart + x] = NavCostField.CostImpassable;
                        blocked++;
                    }
                    else if (pg.IsBridgeDeckOnly(pgCell))
                    {
                        // Walkable only via a bridge deck (cliff / NoWalk
                        // ground beneath): expensive, so planning crosses
                        // it only for genuine bridge routes.
                        field.TerrainCost[rowStart + x] = NavCostField.CostBridgeDeckOnly;
                    }
                    else if (pg.IsBridgeMount(pgCell))
                    {
                        // Deck touchdown (ramp toe): the legal entrance of a
                        // deck-only strip for the flow-field integration.
                        field.TerrainCost[rowStart + x] = NavCostField.CostBridgeMount;
                    }
                    else
                    {
                        field.TerrainCost[rowStart + x] = 0;
                    }
                }
            }

            // Latch TerrainBaked so the change-gated CostFieldStampSystem
            // re-stamps once and seeds the freshly-baked terrain into layer-0.
            field.TerrainBaked = 1;
            SystemAPI.SetSingleton(field);

            _baked = 1;
            UnityEngine.Debug.Log(
                $"[TerrainCostBake] baked terrain into cost field: {blocked}/{w * h} cells " +
                $"impassable (water + slope > {PassabilityGrid.MaxWalkableSlope:0.##} budget).");

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
