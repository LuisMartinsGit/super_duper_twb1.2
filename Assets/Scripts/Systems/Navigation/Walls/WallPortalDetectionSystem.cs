// WallPortalDetectionSystem.cs
// task-112 M5 -- emits climb / gate-ground / gate-rampart portal specs
// derived from wall structural entities (WallHubTag for climb access,
// WallGateTag for gates). The output sits in the
// WallPortalSpecList singleton; the next portal-graph build
// (PortalGraphBuildSystem or IncrementalPortalRebuildSystem) folds the
// specs into the new blob.
//
// Determinism notes (DR-10):
//   * Source entities snapshotted to a sorted-by-entity.Index array
//     before emission so portal-node indices stay stable across
//     machines.
//   * Cell math uses the NavGridSingleton Origin / CellSize — integer
//     floor — same routine as every other M3..M5 caller.
//
// Runs after IncrementalPortalRebuildSystem so structural changes
// processed THIS tick by the rebuilder are reflected in the spec list
// for the NEXT tick. M5 only re-emits when the wall entity set changed
// (counter on the singleton); a future polish pass can wire this to the
// NavDirtyTiles event.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// task-112 M5 -- one-pass detection of wall climb / gate portal
    /// candidates. Owns the <see cref="WallPortalSpecList"/> singleton.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(IncrementalPortalRebuildSystem))]
    public partial struct WallPortalDetectionSystem : ISystem
    {
        private Entity _specEntity;
        private byte _initialised;
        /// <summary>Mirror of the singleton's list, disposable after a wipe.</summary>
        private NativeList<WallPortalSpec> _specs;
        private EntityQuery _hubQuery;
        private EntityQuery _gateQuery;
        private int _prevHubCount;
        private int _prevGateCount;

        // NOT [BurstCompile] -- CreateEntity is managed (BC1028).
        public void OnCreate(ref SystemState state)
        {
            _initialised = 0;
            _prevHubCount = -1;
            _prevGateCount = -1;
            _hubQuery = SystemAPI.QueryBuilder()
                .WithAll<WallTag, WallHubTag, LocalTransform>()
                .Build();
            // FactionTag is required here because OnUpdate calls
            // ToComponentDataArray<FactionTag>() on this query to read the
            // gate's owner. Without declaring it the iterator throws
            // InvalidOperationException ("the required component type was
            // not declared in the EntityQuery").
            _gateQuery = SystemAPI.QueryBuilder()
                .WithAll<WallTag, WallGateTag, LocalTransform, FactionTag>()
                .Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // Existence-gated: the end-of-match wipe destroys this singleton
            // while the system survives. This system has no RequireForUpdate,
            // so it runs every frame regardless — the unguarded GetSingleton
            // below would throw forever once the nav grid came back.
            if (_initialised == 0
                || !em.Exists(_specEntity)
                || !em.HasComponent<WallPortalSpecList>(_specEntity))
            {
                if (_specs.IsCreated) _specs.Dispose();
                _specs = new NativeList<WallPortalSpec>(64, Allocator.Persistent);

                _initialised = 1;
                _specEntity = em.CreateEntity(typeof(WallPortalSpecList));
                em.SetComponentData(_specEntity, new WallPortalSpecList
                {
                    Specs = _specs,
                    Generation = 0,
                });
                // Counts belong to the previous match's wall set.
                _prevHubCount = -1;
                _prevGateCount = -1;
            }

            // Cheap "did the wall set change" gate: hubs + gates counts.
            // Misses in-place mutations (a hub re-parented to a different
            // cell), but the M5 scenario builds walls once at scenario
            // setup, so this is sufficient for shipping the portal kinds
            // into the graph. M6 hardens this with NavDirtyTiles wiring.
            int hubCount = _hubQuery.CalculateEntityCount();
            int gateCount = _gateQuery.CalculateEntityCount();
            if (hubCount == _prevHubCount && gateCount == _prevGateCount) return;
            _prevHubCount = hubCount;
            _prevGateCount = gateCount;

            if (!SystemAPI.HasSingleton<NavGridSingleton>()) return;
            var grid = SystemAPI.GetSingleton<NavGridSingleton>();

            if (!SystemAPI.HasSingleton<WallPortalSpecList>()) return;
            var specList = SystemAPI.GetSingleton<WallPortalSpecList>();
            if (!specList.Specs.IsCreated) return;

            specList.Specs.Clear();

            // ── Climb portals (WallHubTag entities -- stair / access cores) ─
            using (var hubEntities = _hubQuery.ToEntityArray(Allocator.Temp))
            using (var hubXfs = _hubQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                var order = new NativeArray<int>(hubEntities.Length, Allocator.Temp);
                for (int i = 0; i < order.Length; i++) order[i] = i;
                // Insertion sort by entity.Index asc (DR-10).
                for (int i = 1; i < order.Length; i++)
                {
                    int k = order[i];
                    int j = i - 1;
                    while (j >= 0 && hubEntities[order[j]].Index > hubEntities[k].Index)
                    {
                        order[j + 1] = order[j];
                        j--;
                    }
                    order[j + 1] = k;
                }

                for (int oi = 0; oi < order.Length; oi++)
                {
                    int idx = order[oi];
                    var e = hubEntities[idx];
                    var pos = hubXfs[idx].Position;
                    int2 cell = WorldToCell(grid, pos);
                    // Climb portals connect Ground(0) and Rampart(1) at the
                    // hub's footprint centre cell. OwnerId = -1 ("any").
                    specList.Specs.Add(new WallPortalSpec
                    {
                        Kind = PortalNode.KindClimb,
                        SourceCell = cell,
                        SourceLayer = 0,
                        TargetCell = cell,
                        TargetLayer = 1,
                        OwnerId = -1,
                        SourceEntity = e,
                    });
                }
                order.Dispose();
            }

            // ── Gate portals (WallGateTag instances) ─────────────────────
            using (var gateEntities = _gateQuery.ToEntityArray(Allocator.Temp))
            using (var gateXfs = _gateQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            using (var gateFactions = _gateQuery.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                var order = new NativeArray<int>(gateEntities.Length, Allocator.Temp);
                for (int i = 0; i < order.Length; i++) order[i] = i;
                for (int i = 1; i < order.Length; i++)
                {
                    int k = order[i];
                    int j = i - 1;
                    while (j >= 0 && gateEntities[order[j]].Index > gateEntities[k].Index)
                    {
                        order[j + 1] = order[j];
                        j--;
                    }
                    order[j + 1] = k;
                }

                for (int oi = 0; oi < order.Length; oi++)
                {
                    int idx = order[oi];
                    var e = gateEntities[idx];
                    var pos = gateXfs[idx].Position;
                    var fac = gateFactions[idx].Value;
                    int2 centreCell = WorldToCell(grid, pos);
                    // Approximate inside/outside cells as one cell on each
                    // side of the gate centre along +X axis (matches the
                    // wall layout used by the M5 test scenario). A future
                    // polish pass should derive the axis from the wall
                    // hub graph.
                    int2 insideCell = new int2(centreCell.x - 1, centreCell.y);
                    int2 outsideCell = new int2(centreCell.x + 1, centreCell.y);

                    // Ground gate portal: inside <-> outside at layer 0.
                    specList.Specs.Add(new WallPortalSpec
                    {
                        Kind = PortalNode.KindGateGround,
                        SourceCell = insideCell,
                        SourceLayer = 0,
                        TargetCell = outsideCell,
                        TargetLayer = 0,
                        OwnerId = (int)fac,
                        SourceEntity = e,
                    });
                    // Rampart gate portal: bridges the rampart spans across
                    // the gatehouse roof at layer 1.
                    specList.Specs.Add(new WallPortalSpec
                    {
                        Kind = PortalNode.KindGateRampart,
                        SourceCell = insideCell,
                        SourceLayer = 1,
                        TargetCell = outsideCell,
                        TargetLayer = 1,
                        OwnerId = (int)fac,
                        SourceEntity = e,
                    });
                }
                order.Dispose();
            }

            specList.Generation++;
            SystemAPI.SetSingleton(specList);

            // task-112 M5 -- topology changed; flag the spec-covered tiles
            // dirty so IncrementalPortalRebuildSystem on the NEXT tick
            // picks the new wall-portal specs into the graph blob. Without
            // this nudge, the wall-stamp pass might have already drained
            // the dirty set in the same tick the walls were created.
            if (SystemAPI.HasSingleton<NavDirtyTiles>())
            {
                var dirty = SystemAPI.GetSingleton<NavDirtyTiles>();
                if (dirty.DirtyTileIndices.IsCreated)
                {
                    int tileSize = PortalGraphSingleton.TileSize;
                    int tilesX = (grid.Width + tileSize - 1) / tileSize;
                    for (int i = 0; i < specList.Specs.Length; i++)
                    {
                        var s = specList.Specs[i];
                        int tx = s.SourceCell.x / tileSize;
                        int tz = s.SourceCell.y / tileSize;
                        dirty.DirtyTileIndices.Add(tz * tilesX + tx);
                        tx = s.TargetCell.x / tileSize;
                        tz = s.TargetCell.y / tileSize;
                        dirty.DirtyTileIndices.Add(tz * tilesX + tx);
                    }
                    SystemAPI.SetSingleton(dirty);
                }
            }
        }

        public void OnDestroy(ref SystemState state)
        {
            // Mirror, not component — the entity may already be wiped.
            if (_specs.IsCreated) _specs.Dispose();
        }

        private static int2 WorldToCell(in NavGridSingleton grid, float3 world)
        {
            int cx = (int)math.floor((world.x - grid.Origin.x) / grid.CellSize);
            int cz = (int)math.floor((world.z - grid.Origin.z) / grid.CellSize);
            return new int2(cx, cz);
        }
    }
}
