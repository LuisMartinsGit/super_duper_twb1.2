// BuildCommand.cs
// Build command component and execution logic

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// ECS Component representing a build command for a builder unit.
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
        /// <summary>
        /// Execute a build command on a builder unit.
        /// Clears conflicting commands and sets up construction state.
        /// </summary>
        public static void Execute(EntityManager em, Entity builder, Entity targetBuilding,
            string buildingId, float3 position)
        {
            if (!em.Exists(builder)) return;

            // Verify builder can build
            if (!em.HasComponent<CanBuild>(builder)) return;

            // A BUSY builder gets the new site QUEUED, not substituted.
            //
            // BuildCommand and BuildOrder are single components, so this method
            // used to overwrite whatever the builder was already doing. Placing
            // several buildings in a row therefore kept only the LAST one and
            // silently dropped the rest — resources spent, foundations never
            // touched. It only looked intermittent because the LOS auto-chain
            // rescued sites that happened to be near each other; far-apart ones
            // were lost.
            //
            // Queuing (rather than replacing) is right for this game because
            // placement is not "order this builder somewhere" — the player
            // places a BUILDING and the system picks a builder. Cancelling the
            // builder's current job was never the intent of that click.
            if (IsBusy(em, builder))
            {
                var queue = em.HasBuffer<QueuedBuildSite>(builder)
                    ? em.GetBuffer<QueuedBuildSite>(builder)
                    : em.AddBuffer<QueuedBuildSite>(builder);

                queue.Add(new QueuedBuildSite
                {
                    BuildingId = new FixedString64Bytes(buildingId),
                    Position = position,
                    TargetBuilding = targetBuilding,
                });
                return;
            }

            // Clear conflicting commands
            CommandHelper.ClearAllCommands(em, builder);

            // A drafted mining worker must LEAVE the mining state machine —
            // ClearAllCommands strips the GatherCommand but MinerState.State
            // kept running, so MiningSystem steered the worker toward its
            // deposit every tick while the construction mover steered it
            // toward the site (workers visibly walking away from their own
            // destination line).
            if (em.HasComponent<MinerState>(builder))
            {
                var ms = em.GetComponentData<MinerState>(builder);
                if (ms.State != MinerWorkState.Idle)
                {
                    ms.State = MinerWorkState.Idle;
                    ms.AssignedDeposit = Entity.Null;
                    em.SetComponentData(builder, ms);
                }
            }

            // Set up build command
            SetupBuild(em, builder, targetBuilding, buildingId, position);
        }

        /// <summary>
        /// Check if a build command can be executed
        /// </summary>
        public static bool CanExecute(EntityManager em, Entity builder, string buildingId)
        {
            if (!em.Exists(builder)) return false;
            if (!em.HasComponent<CanBuild>(builder)) return false;
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
            float halfW = buildingSize.x / 2f;
            float halfH = buildingSize.y / 2f;
            float2 newMin = new float2(position.x - halfW, position.z - halfH);
            float2 newMax = new float2(position.x + halfW, position.z + halfH);

            var buildingQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var xfs = buildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var ents = buildingQuery.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < xfs.Length; i++)
            {
                var bPos = xfs[i].Position;
                float2 otherMin, otherMax;

                if (em.HasComponent<BuildingSize>(ents[i]))
                {
                    var bSize = em.GetComponentData<BuildingSize>(ents[i]);
                    otherMin = new float2(bPos.x - bSize.Width / 2f, bPos.z - bSize.Height / 2f);
                    otherMax = new float2(bPos.x + bSize.Width / 2f, bPos.z + bSize.Height / 2f);
                }
                else
                {
                    float r = em.HasComponent<Radius>(ents[i])
                        ? em.GetComponentData<Radius>(ents[i]).Value : 1.5f;
                    otherMin = new float2(bPos.x - r, bPos.z - r);
                    otherMax = new float2(bPos.x + r, bPos.z + r);
                }

                if (newMin.x < otherMax.x && newMax.x > otherMin.x &&
                    newMin.y < otherMax.y && newMax.y > otherMin.y)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Placement test, with the building id so the crust rule can make its
        /// one exception. Callers that do not know the id pass null and get the
        /// strict rule.
        /// </summary>
        public static bool IsValidBuildPosition(EntityManager em, float3 position, int2 buildingSize,
            string buildingId)
        {
            // THE VEIL (Curse & Shardroot canon §2.3): veilstone crust is
            // unbuildable ground — humanity is being pushed back. Reclaim it
            // (mine the frontier crystals, starve the wells, sanctify with a
            // Font) before building on it.
            var veilQuery = em.CreateEntityQuery(ComponentType.ReadOnly<VeilField>());
            if (!veilQuery.IsEmptyIgnoreFilter)
            {
                // Veilworks (Sect of Reclamation) is the ONE exception: a
                // smelter for cursed matter, explicitly raised on cursed ground
                // (docs/Design/Sects.md section 4). Everything else obeys the
                // crust rule.
                bool ignoresCrust = buildingId == "Sect_Veilworks";
                var veil = veilQuery.GetSingleton<VeilField>();
                if (!ignoresCrust
                    && veil.Initialised != 0
                    && veil.SaturationAt(position) >= VeilField.CrustThreshold)
                    return false;
            }

            // Compute AABB half-extents for the new building
            float halfW = buildingSize.x / 2f;
            float halfH = buildingSize.y / 2f;
            float2 newMin = new float2(position.x - halfW, position.z - halfH);
            float2 newMax = new float2(position.x + halfW, position.z + halfH);

            // 0. Map-bounds check — the building's footprint must fit entirely
            //    inside the world rectangle. Bounds source priority:
            //      1. ProceduralTerrain.Instance (procedural maps)
            //      2. Unity Terrain.activeTerrain (hand-authored maps —
            //         MapMagic terrain may sit at non-origin coords)
            //      3. ±GameSettings.MapHalfSize box (early bootstrap / flat
            //         test map fallback)
            var terrain = ProceduralTerrain.Instance;
            if (terrain != null)
            {
                if (newMin.x < terrain.worldMin.x || newMin.y < terrain.worldMin.y ||
                    newMax.x > terrain.worldMax.x || newMax.y > terrain.worldMax.y)
                    return false;
            }
            else
            {
                var ut = UnityEngine.Terrain.activeTerrain;
                if (ut != null && ut.terrainData != null)
                {
                    var origin = ut.transform.position;
                    var size = ut.terrainData.size;
                    if (newMin.x < origin.x || newMin.y < origin.z ||
                        newMax.x > origin.x + size.x || newMax.y > origin.z + size.z)
                        return false;
                }
                else
                {
                    float half = GameSettings.MapHalfSize;
                    if (newMin.x < -half || newMin.y < -half ||
                        newMax.x >  half || newMax.y >  half)
                        return false;
                }
            }

            // 1. Building overlap check (AABB-vs-AABB on XZ plane)
            var buildingQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );
            using var buildingTransforms = buildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var buildingEntities = buildingQuery.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < buildingTransforms.Length; i++)
            {
                var bPos = buildingTransforms[i].Position;
                float2 otherMin, otherMax;

                if (em.HasComponent<BuildingSize>(buildingEntities[i]))
                {
                    var bSize = em.GetComponentData<BuildingSize>(buildingEntities[i]);
                    float bHalfW = bSize.Width / 2f;
                    float bHalfH = bSize.Height / 2f;
                    otherMin = new float2(bPos.x - bHalfW, bPos.z - bHalfH);
                    otherMax = new float2(bPos.x + bHalfW, bPos.z + bHalfH);
                }
                else
                {
                    // Fallback for buildings without BuildingSize (legacy)
                    float r = em.HasComponent<Radius>(buildingEntities[i])
                        ? em.GetComponentData<Radius>(buildingEntities[i]).Value
                        : 1.5f;
                    otherMin = new float2(bPos.x - r, bPos.z - r);
                    otherMax = new float2(bPos.x + r, bPos.z + r);
                }

                // AABB overlap test
                if (newMin.x < otherMax.x && newMax.x > otherMin.x &&
                    newMin.y < otherMax.y && newMax.y > otherMin.y)
                    return false;
            }

            // 2. Obstacle overlap check (AABB-vs-circle for natural obstacles)
            var obstacleQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<ObstacleTag>(),
                ComponentType.ReadOnly<Radius>(),
                ComponentType.ReadOnly<LocalTransform>()
            );
            using var obstacleRadii = obstacleQuery.ToComponentDataArray<Radius>(Allocator.Temp);
            using var obstacleTransforms = obstacleQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < obstacleRadii.Length; i++)
            {
                var oPos = obstacleTransforms[i].Position;
                float oR = obstacleRadii[i].Value;
                // Clamp circle center to AABB, check distance
                float closestX = math.clamp(oPos.x, newMin.x, newMax.x);
                float closestZ = math.clamp(oPos.z, newMin.y, newMax.y);
                float dx = oPos.x - closestX;
                float dz = oPos.z - closestZ;
                if (dx * dx + dz * dz < oR * oR)
                    return false;
            }

            // 3. Terrain checks for all four corners + center
            float3[] checkPoints = new float3[]
            {
                position,
                new float3(newMin.x, 0, newMin.y),
                new float3(newMax.x, 0, newMin.y),
                new float3(newMin.x, 0, newMax.y),
                new float3(newMax.x, 0, newMax.y)
            };

            // tan(15°) ≈ 0.2679 — buildings reject placement on terrain steeper than 15°.
            const float maxSlope = 0.2679f;
            const float slopeStep = 1.5f;

            foreach (var pt in checkPoints)
            {
                float h = TerrainUtility.GetHeight(pt.x, pt.z);
                if (WaterPlane.Instance != null &&
                    WaterPlane.Instance.IsUnderwater(new UnityEngine.Vector3(pt.x, h, pt.z)))
                    return false;

                float hL = TerrainUtility.GetHeight(pt.x - slopeStep, pt.z);
                float hR = TerrainUtility.GetHeight(pt.x + slopeStep, pt.z);
                float hD = TerrainUtility.GetHeight(pt.x, pt.z - slopeStep);
                float hU = TerrainUtility.GetHeight(pt.x, pt.z + slopeStep);
                float dX = (hR - hL) / (slopeStep * 2f);
                float dZ = (hU - hD) / (slopeStep * 2f);
                float slope = math.sqrt(dX * dX + dZ * dZ);
                if (slope > maxSlope)
                    return false;
            }

            // 4. Passability grid check -- all cells under footprint must be passable
            var grid = PassabilityGrid.Instance;
            if (grid != null)
            {
                if (!grid.IsFootprintPassable(position, buildingSize))
                    return false;
            }

            return true;
        }

        // GetBuildingRadius removed in task-062 Q-41 — zero callers. Building
        // collision is footprint-based (BuildingSizeConfig), not circle-radius.

        /// <summary>
        /// True while the builder is already walking to a site (BuildCommand)
        /// or actively constructing one (BuildOrder).
        /// </summary>
        private static bool IsBusy(EntityManager em, Entity builder)
            => em.HasComponent<BuildCommand>(builder)
            || em.HasComponent<BuildOrder>(builder);

        /// <summary>
        /// Start the next queued site, if any. Returns true when one was
        /// issued. Called when a builder finishes or loses its current job so
        /// the queued plan continues on its own.
        /// </summary>
        public static bool TryStartNextQueued(EntityManager em, Entity builder)
        {
            if (!em.Exists(builder) || !em.HasBuffer<QueuedBuildSite>(builder)) return false;

            var queue = em.GetBuffer<QueuedBuildSite>(builder);
            while (queue.Length > 0)
            {
                var next = queue[0];
                queue.RemoveAt(0);

                // Skip entries whose site died before we got to it (razed,
                // cancelled, or never placed).
                if (next.TargetBuilding != Entity.Null && !em.Exists(next.TargetBuilding))
                    continue;

                SetupBuild(em, builder, next.TargetBuilding,
                           next.BuildingId.ToString(), next.Position);
                return true;
            }
            return false;
        }

        private static void SetupBuild(EntityManager em, Entity builder, Entity targetBuilding,
            string buildingId, float3 position)
        {
            var cmd = new BuildCommand
            {
                BuildingId = new FixedString64Bytes(buildingId),
                Position = position,
                TargetBuilding = targetBuilding
            };

            if (!em.HasComponent<BuildCommand>(builder))
                em.AddComponentData(builder, cmd);
                else
                    em.SetComponentData(builder, cmd);

            // Set destination to build position
            if (em.HasComponent<DesiredDestination>(builder))
            {
                em.SetComponentData(builder, new DesiredDestination
                {
                    Position = position,
                    Has = 1
                });
            }
            else
            {
                em.AddComponentData(builder, new DesiredDestination
                {
                    Position = position,
                    Has = 1
                });
            }
        }
    }
}