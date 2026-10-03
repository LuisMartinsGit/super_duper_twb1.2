// BuildCommand.cs
// Build command component and execution logic

using Unity.Collections;
using TheWaningBorder.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// ECS Component representing a build command for a worker unit.
    /// When attached to an entity, construction systems will process it.
    /// </summary>
    public struct BuildCommand : IComponentData
    {
        /// <summary>ID of the building to construct (e.g., "Barracks", "GatherersHut")</summary>
        public FixedString64Bytes BuildingId;
        
        /// <summary>World position where building should be placed</summary>
        public float3 Position;
        
        /// <summary>The building entity being constructed (Entity.Null if not yet created)</summary>
        public Entity TargetBuilding;
    }

    /// <summary>
    /// Helper class for executing build commands
    /// </summary>
    public static class BuildCommandHelper
    {

        #region Cached queries

        // IsValidBuildPosition is called EVERY FRAME while the player drags a
        // building ghost (BuildCommandPannel.Update). Each of these used to be
        // a fresh CreateEntityQuery that was never disposed, so placing a few
        // buildings left thousands of dead queries registered with the world —
        // and a bloated registry slows every later query AND every structural
        // change, which is most of what issuing an order does.
        // See Core/CachedEntityQuery.cs.
        static readonly ComponentType[] BuildingTypes =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery _buildingQuery;

        static readonly ComponentType[] VeilTypes = { ComponentType.ReadOnly<VeilField>() };
        static CachedEntityQuery _veilQuery;

        static readonly ComponentType[] ObstacleTypes =
        {
            ComponentType.ReadOnly<ObstacleTag>(),
            ComponentType.ReadOnly<Radius>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery _obstacleQuery;

        #endregion

        /// <summary>
        /// Execute a build command on a worker unit.
        /// Clears conflicting commands and sets up construction state.
        /// </summary>
        public static void Execute(EntityManager em, Entity worker, Entity targetBuilding,
            string buildingId, float3 position)
        {
            if (!em.Exists(worker)) return;

            // Verify worker can build
            if (!em.HasComponent<CanBuild>(worker)) return;

            // A BUSY worker gets the new site QUEUED, not substituted.
            //
            // BuildCommand and BuildOrder are single components, so this method
            // used to overwrite whatever the worker was already doing. Placing
            // several buildings in a row therefore kept only the LAST one and
            // silently dropped the rest — resources spent, foundations never
            // touched. It only looked intermittent because the LOS auto-chain
            // rescued sites that happened to be near each other; far-apart ones
            // were lost.
            //
            // Queuing (rather than replacing) is right for this game because
            // placement is not "order this worker somewhere" — the player
            // places a BUILDING and the system picks a worker. Cancelling the
            // worker's current job was never the intent of that click.
            if (IsBusy(em, worker))
            {
                var queue = em.HasBuffer<QueuedBuildSite>(worker)
                    ? em.GetBuffer<QueuedBuildSite>(worker)
                    : em.AddBuffer<QueuedBuildSite>(worker);

                queue.Add(new QueuedBuildSite
                {
                    BuildingId = new FixedString64Bytes(buildingId),
                    Position = position,
                    TargetBuilding = targetBuilding,
                });
                return;
            }

            // Clear conflicting commands
            CommandHelper.ClearAllCommands(em, worker);

            // A drafted mining worker must LEAVE the mining state machine —
            // ClearAllCommands strips the GatherCommand but WorkerState.State
            // kept running, so MiningSystem steered the worker toward its
            // deposit every tick while the construction mover steered it
            // toward the site (workers visibly walking away from their own
            // destination line).
            if (em.HasComponent<WorkerState>(worker))
            {
                var ms = em.GetComponentData<WorkerState>(worker);
                if (ms.State != WorkerActivity.Idle)
                {
                    ms.State = WorkerActivity.Idle;
                    ms.AssignedDeposit = Entity.Null;
                    em.SetComponentData(worker, ms);
                }
            }

            // Set up build command
            SetupBuild(em, worker, targetBuilding, buildingId, position);
        }

        /// <summary>
        /// Check if a build command can be executed
        /// </summary>
        public static bool CanExecute(EntityManager em, Entity worker, string buildingId)
        {
            if (!em.Exists(worker)) return false;
            if (!em.HasComponent<CanBuild>(worker)) return false;
            if (string.IsNullOrEmpty(buildingId)) return false;

            // Could add resource checking here
            return true;
        }

        // The circle-radius-based IsValidBuildPosition(EntityManager, float3, float)
        // overload was removed in task-062 Q-41 — every caller goes through the
        // int2-size AABB overload below. The grid-aligned check is the supported
        // placement model now (BuildingSizeConfig drives footprint).

        /// <summary>
        /// Get the grid-aligned size for a building by its ID.
        /// Delegates to BuildingSizeConfig.
        /// </summary>
        public static int2 GetBuildingSize(string buildingId)
        {
            return BuildingSizeConfig.GetSize(buildingId);
        }

        /// <summary>
        /// Check if a position is valid for building placement using AABB collision.
        /// Checks building overlap, obstacle overlap, terrain passability, and grid footprint.
        /// </summary>
        public static bool IsValidBuildPosition(EntityManager em, float3 position, int2 buildingSize)
            => IsValidBuildPosition(em, position, buildingSize, null);

        /// <summary>
        /// Footprint-vs-footprint overlap against the buildings that already
        /// exist — and NOTHING else. No terrain, slope, water, veil-crust or
        /// passability rules.
        ///
        /// This is the executor's last-line invariant: two buildings may never
        /// occupy the same ground. It is deliberately narrower than
        /// <see cref="IsValidBuildPosition"/>, which an issue site runs against
        /// a CANDIDATE position that may be fractional, may be stale by the
        /// time a queued command executes, and in the lockstep case was
        /// evaluated on another machine entirely. Only geometry is re-tested
        /// here, so this can never refuse a placement for a reason the player's
        /// own placement preview did not already show them.
        ///
        /// Reads only replicated simulation state, so every peer reaches the
        /// same verdict from the same command stream.
        /// </summary>
        public static bool OverlapsExistingBuilding(EntityManager em, float3 position, int2 buildingSize)
        {
            FootprintAabb(position, buildingSize, out float2 newMin, out float2 newMax);

            var buildingQuery = _buildingQuery.Get(em, BuildingTypes);
            using var xfs = buildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var ents = buildingQuery.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < xfs.Length; i++)
            {
                BuildingAabb(em, ents[i], xfs[i].Position, out float2 otherMin, out float2 otherMax);
                if (newMin.x < otherMax.x && newMax.x > otherMin.x &&
                    newMin.y < otherMax.y && newMax.y > otherMin.y)
                    return true;
            }
            return OverlapsWall(newMin, newMax);
        }

        /// <summary>
        /// True when the footprint covers a WALL cell. A wall piece's own box
        /// cannot answer this: it is axis-aligned and the wall is not, so a
        /// diagonal or curved wall left staircase notches that building
        /// corners slotted into. The nav grid holds every wall piece stamped
        /// to its own heading — the stair-stepped cells it really crosses —
        /// so that is what a building is tested against
        /// (docs/Design/Build_Grid.md § Walls on the grid).
        /// </summary>
        public static bool OverlapsWall(float2 min, float2 max)
            => TheWaningBorder.Systems.Navigation.NavGridQuery.AnyWallCellIn(min, max);

        /// <summary>
        /// Placement test, with the building id so the crust rule can make its
        /// one exception. Callers that do not know the id pass null and get the
        /// strict rule.
        /// </summary>
        public static bool IsValidBuildPosition(EntityManager em, float3 position, int2 buildingSize,
            string buildingId)
            => CheckBuildPosition(em, position, buildingSize, buildingId)
               == TheWaningBorder.World.Regions.PlacementRefusal.None;

        /// <summary>
        /// <see cref="IsValidBuildPosition(EntityManager, float3, int2, string)"/>
        /// with its REASON: CursedGround, Terrain (map edge, obstacle, slope,
        /// water, impassable cells) or Overlap. Same stages, same order — the
        /// bool form is defined as "this answered None".
        /// </summary>
        public static TheWaningBorder.World.Regions.PlacementRefusal CheckBuildPosition(
            EntityManager em, float3 position, int2 buildingSize, string buildingId)
        {
            const TheWaningBorder.World.Regions.PlacementRefusal Ok =
                TheWaningBorder.World.Regions.PlacementRefusal.None;
            const TheWaningBorder.World.Regions.PlacementRefusal Terrain =
                TheWaningBorder.World.Regions.PlacementRefusal.Terrain;

            // THE VEIL (Curse & Shardroot canon §2.3): veilstone crust is
            // unbuildable ground — humanity is being pushed back. Reclaim it
            // (mine the frontier crystals, starve the wells, sanctify with a
            // Font) before building on it.
            if (!PassesCrustRule(em, position, buildingId))
                return TheWaningBorder.World.Regions.PlacementRefusal.CursedGround;

            // Compute AABB half-extents for the new building
            FootprintAabb(position, buildingSize, out float2 newMin, out float2 newMax);

            // 0. Map-bounds check — the building's footprint must fit entirely
            //    inside the world rectangle.
            if (!InsideMapBounds(newMin, newMax)) return Terrain;

            // 1. Building overlap check (AABB-vs-AABB on XZ plane)
            var buildingQuery = _buildingQuery.Get(em, BuildingTypes);
            using var buildingTransforms = buildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var buildingEntities = buildingQuery.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < buildingTransforms.Length; i++)
            {
                BuildingAabb(em, buildingEntities[i], buildingTransforms[i].Position,
                    out float2 otherMin, out float2 otherMax);

                // AABB overlap test
                if (newMin.x < otherMax.x && newMax.x > otherMin.x &&
                    newMin.y < otherMax.y && newMax.y > otherMin.y)
                    return TheWaningBorder.World.Regions.PlacementRefusal.Overlap;
            }
            // 1a. Walls, at their real (rotated) footprint.
            if (OverlapsWall(newMin, newMax))
                return TheWaningBorder.World.Regions.PlacementRefusal.Overlap;

            // AN EXTRACTOR STANDS ON ITS NODE (docs/Design/Regions.md §4) —
            // and the iron / veilstone / veilsteel nodes are ObstacleTag
            // entities that also block their footprint cells, so the obstacle
            // and passability checks below refused the ONE placement the
            // design requires. Diagnosed from a headless batch: six 30-minute
            // matches, 80 Gatherer's Huts (the supply node is deliberately
            // obstacle-free) and not a single Mine or Veilstone Mine
            // — every on-node candidate died here, for the AI and the player
            // alike. The node kind the building is FOR is exempt from the
            // obstacle test; other node kinds and every other obstacle still
            // refuse. The position cannot wander off the node on this
            // exemption: the router's OnFreeNodeFor gate leashes an extractor
            // to within 4 m of a free node of exactly this kind.
            var ownNode = buildingId != null
                ? TheWaningBorder.World.Regions.TerritoryOwnership.RequiredNodeFor(buildingId)
                : null;

            // 1b. NOTHING IS BUILT ON A RESOURCE NODE except the extractor made
            //     for it (Build_Grid.md §3). The node's whole 4 x 4 m square is
            //     tested — the obstacle circle below misses its corners, and a
            //     supply node is no obstacle at all.
            if (TheWaningBorder.Entities.ResourceNodeSite.OverlapsNode(em, newMin, newMax, ownNode))
                return TheWaningBorder.World.Regions.PlacementRefusal.OnResourceNode;

            // 2. Obstacle overlap check (AABB-vs-circle for natural obstacles)
            var obstacleQuery = _obstacleQuery.Get(em, ObstacleTypes);
            using var obstacleRadii = obstacleQuery.ToComponentDataArray<Radius>(Allocator.Temp);
            using var obstacleTransforms = obstacleQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var obstacleEntities = obstacleQuery.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < obstacleRadii.Length; i++)
            {
                if (ownNode != null && em.HasComponent(obstacleEntities[i], ownNode.Value))
                    continue;   // the node this extractor exists to stand on

                if (CircleHitsAabb(obstacleTransforms[i].Position, obstacleRadii[i].Value,
                        newMin, newMax))
                    return Terrain;
            }

            // 3 + 4. Terrain (water, slope) and the passability grid.
            return PassesTerrainAndGrid(position, buildingSize, newMin, newMax, ownNode != null)
                ? Ok : Terrain;
        }

        // ─────────────────────────────────────────────────────────────────
        // SHARED STAGES
        //
        // IsValidBuildPosition and BuildSiteSnapshot.IsValidBuildPosition run
        // the SAME stages in the SAME order; only where the building and
        // obstacle lists come from differs (a live query per call vs one
        // snapshot per sim tick). Keeping the stages here is what stops the
        // AI's site picker and the router's validator from drifting apart.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Crust rule: false on veil crust, except for the one
        /// building raised on cursed ground (Sect_Veilworks).</summary>
        internal static bool PassesCrustRule(EntityManager em, float3 position, string buildingId)
        {
            var veilQuery = _veilQuery.Get(em, VeilTypes);
            if (veilQuery.IsEmptyIgnoreFilter) return true;

            // Veilworks (Sect of Reclamation) is the ONE exception: a
            // smelter for cursed matter, explicitly raised on cursed ground
            // (docs/Design/Sects.md section 4). Everything else obeys the
            // crust rule.
            if (buildingId == "Sect_Veilworks") return true;
            var veil = veilQuery.GetSingleton<VeilField>();
            return !(veil.Initialised != 0
                     && veil.SaturationAt(position) >= VeilField.CrustThreshold);
        }

        internal static void FootprintAabb(float3 position, int2 buildingSize,
            out float2 min, out float2 max)
        {
            float halfW = buildingSize.x / 2f;
            float halfH = buildingSize.y / 2f;
            min = new float2(position.x - halfW, position.z - halfH);
            max = new float2(position.x + halfW, position.z + halfH);
        }

        /// <summary>An existing building's XZ footprint: its BuildingSize,
        /// or the legacy Radius square (1.5 m when it has neither).</summary>
        internal static void BuildingAabb(EntityManager em, Entity e, float3 bPos,
            out float2 min, out float2 max)
        {
            if (em.HasComponent<BuildingSize>(e))
            {
                var bSize = em.GetComponentData<BuildingSize>(e);
                float bHalfW = bSize.Width / 2f;
                float bHalfH = bSize.Height / 2f;
                min = new float2(bPos.x - bHalfW, bPos.z - bHalfH);
                max = new float2(bPos.x + bHalfW, bPos.z + bHalfH);
            }
            else
            {
                // Fallback for buildings without BuildingSize (legacy)
                float r = em.HasComponent<Radius>(e)
                    ? em.GetComponentData<Radius>(e).Value
                    : 1.5f;
                min = new float2(bPos.x - r, bPos.z - r);
                max = new float2(bPos.x + r, bPos.z + r);
            }
        }

        /// <summary>Circle (natural obstacle) vs footprint: clamp the centre
        /// to the AABB and compare the distance.</summary>
        internal static bool CircleHitsAabb(float3 c, float r, float2 min, float2 max)
        {
            float closestX = math.clamp(c.x, min.x, max.x);
            float closestZ = math.clamp(c.z, min.y, max.y);
            float dx = c.x - closestX;
            float dz = c.z - closestZ;
            return dx * dx + dz * dz < r * r;
        }

        /// <summary>
        /// The footprint must fit entirely inside the world rectangle. Bounds
        /// source priority:
        ///   1. ProceduralTerrain.Instance (procedural maps)
        ///   2. Unity Terrain.activeTerrain (hand-authored maps — MapMagic
        ///      terrain may sit at non-origin coords)
        ///   3. ±GameSettings.MapHalfSize box (early bootstrap / flat test map
        ///      fallback)
        /// </summary>
        internal static bool InsideMapBounds(float2 newMin, float2 newMax)
        {
            var terrain = ProceduralTerrain.Instance;
            if (terrain != null)
            {
                return !(newMin.x < terrain.worldMin.x || newMin.y < terrain.worldMin.y ||
                         newMax.x > terrain.worldMax.x || newMax.y > terrain.worldMax.y);
            }
            var ut = UnityEngine.Terrain.activeTerrain;
            if (ut != null && ut.terrainData != null)
            {
                var origin = ut.transform.position;
                var size = ut.terrainData.size;
                return !(newMin.x < origin.x || newMin.y < origin.z ||
                         newMax.x > origin.x + size.x || newMax.y > origin.z + size.z);
            }
            float half = GameSettings.MapHalfSize;
            return !(newMin.x < -half || newMin.y < -half ||
                     newMax.x >  half || newMax.y >  half);
        }

        /// <summary>
        /// 3. Terrain checks for the centre and the four corners (water and a
        /// 15° slope limit), then 4. the passability grid — every cell under
        /// the footprint must be passable. The grid test is SKIPPED FOR
        /// EXTRACTORS: the blocked cells under an on-node candidate ARE its
        /// node (nodes block their footprint at spawn), the 4 m node gate
        /// keeps the footprint on the node, and the finished building blocks
        /// the same ground again.
        /// </summary>
        internal static bool PassesTerrainAndGrid(float3 position, int2 buildingSize,
            float2 newMin, float2 newMax, bool isExtractor)
        {
            // Centre first, then the corners — the order the old point array
            // had, without allocating one per call (this runs per candidate
            // in the AI's site search).
            if (!TerrainPointOk(position.x, position.z)) return false;
            if (!TerrainPointOk(newMin.x, newMin.y)) return false;
            if (!TerrainPointOk(newMax.x, newMin.y)) return false;
            if (!TerrainPointOk(newMin.x, newMax.y)) return false;
            if (!TerrainPointOk(newMax.x, newMax.y)) return false;

            var grid = PassabilityGrid.Instance;
            if (grid != null && !isExtractor)
            {
                if (!grid.IsFootprintPassable(position, buildingSize))
                    return false;
            }
            return true;
        }

        private static bool TerrainPointOk(float x, float z)
        {
            // tan(15°) ≈ 0.2679 — buildings reject placement on terrain steeper than 15°.
            const float maxSlope = 0.2679f;
            const float slopeStep = 1.5f;

            float h = TerrainUtility.GetHeight(x, z);
            if (WaterPlane.Instance != null &&
                WaterPlane.Instance.IsUnderwater(new UnityEngine.Vector3(x, h, z)))
                return false;

            float hL = TerrainUtility.GetHeight(x - slopeStep, z);
            float hR = TerrainUtility.GetHeight(x + slopeStep, z);
            float hD = TerrainUtility.GetHeight(x, z - slopeStep);
            float hU = TerrainUtility.GetHeight(x, z + slopeStep);
            float dX = (hR - hL) / (slopeStep * 2f);
            float dZ = (hU - hD) / (slopeStep * 2f);
            float slope = math.sqrt(dX * dX + dZ * dZ);
            return !(slope > maxSlope);
        }

        /// <summary>The building and obstacle query shapes, for
        /// <see cref="BuildSiteSnapshot"/> — one definition, so the snapshot
        /// sees exactly the entities the live validator sees.</summary>
        internal static EntityQuery BuildingQuery(EntityManager em) => _buildingQuery.Get(em, BuildingTypes);
        internal static EntityQuery ObstacleQuery(EntityManager em) => _obstacleQuery.Get(em, ObstacleTypes);

        // GetBuildingRadius removed in task-062 Q-41 — zero callers. Building
        // collision is footprint-based (BuildingSizeConfig), not circle-radius.

        /// <summary>
        /// True while the worker is already walking to a site (BuildCommand)
        /// or actively constructing one (BuildOrder).
        /// </summary>
        private static bool IsBusy(EntityManager em, Entity worker)
            => em.HasComponent<BuildCommand>(worker)
            || em.HasComponent<BuildOrder>(worker);

        /// <summary>
        /// Start the next queued site, if any. Returns true when one was
        /// issued. Called when a worker finishes or loses its current job so
        /// the queued plan continues on its own.
        /// </summary>
        public static bool TryStartNextQueued(EntityManager em, Entity worker)
        {
            if (!em.Exists(worker) || !em.HasBuffer<QueuedBuildSite>(worker)) return false;

            var queue = em.GetBuffer<QueuedBuildSite>(worker);
            while (queue.Length > 0)
            {
                var next = queue[0];
                queue.RemoveAt(0);

                // Skip entries whose site died before we got to it (razed,
                // cancelled, or never placed).
                if (next.TargetBuilding != Entity.Null && !em.Exists(next.TargetBuilding))
                    continue;

                SetupBuild(em, worker, next.TargetBuilding,
                           next.BuildingId.ToString(), next.Position);
                return true;
            }
            return false;
        }

        private static void SetupBuild(EntityManager em, Entity worker, Entity targetBuilding,
            string buildingId, float3 position)
        {
            var cmd = new BuildCommand
            {
                BuildingId = new FixedString64Bytes(buildingId),
                Position = position,
                TargetBuilding = targetBuilding
            };

            if (!em.HasComponent<BuildCommand>(worker))
                em.AddComponentData(worker, cmd);
                else
                    em.SetComponentData(worker, cmd);

            // Set destination to build position
            if (em.HasComponent<DesiredDestination>(worker))
            {
                em.SetComponentData(worker, new DesiredDestination
                {
                    Position = position,
                    Has = 1
                });
            }
            else
            {
                em.AddComponentData(worker, new DesiredDestination
                {
                    Position = position,
                    Has = 1
                });
            }
        }
    }
}