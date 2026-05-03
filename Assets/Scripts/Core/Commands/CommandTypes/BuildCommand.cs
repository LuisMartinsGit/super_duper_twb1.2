// BuildCommand.cs
// Build command component and execution logic
// Location: Assets/Scripts/Core/Commands/CommandTypes/BuildCommand.cs

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

            // Clear conflicting commands
            CommandHelper.ClearAllCommands(em, builder);

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
        {
            // Compute AABB half-extents for the new building
            float halfW = buildingSize.x / 2f;
            float halfH = buildingSize.y / 2f;
            float2 newMin = new float2(position.x - halfW, position.z - halfH);
            float2 newMax = new float2(position.x + halfW, position.z + halfH);

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