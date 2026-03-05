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

        /// <summary>
        /// Check if a position is valid for building placement.
        /// Checks building overlap, obstacle overlap, and terrain passability.
        /// </summary>
        public static bool IsValidBuildPosition(EntityManager em, float3 position, float buildingRadius)
        {
            // 1. Building overlap check (circle-vs-circle on XZ plane)
            var buildingQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<Radius>(),
                ComponentType.ReadOnly<LocalTransform>()
            );
            using var buildingRadii = buildingQuery.ToComponentDataArray<Radius>(Allocator.Temp);
            using var buildingTransforms = buildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < buildingRadii.Length; i++)
            {
                float dist = math.distance(
                    new float2(position.x, position.z),
                    new float2(buildingTransforms[i].Position.x, buildingTransforms[i].Position.z));
                float minDist = buildingRadius + buildingRadii[i].Value;
                if (dist < minDist)
                    return false;
            }

            // 2. Obstacle overlap check (same pattern)
            var obstacleQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<ObstacleTag>(),
                ComponentType.ReadOnly<Radius>(),
                ComponentType.ReadOnly<LocalTransform>()
            );
            using var obstacleRadii = obstacleQuery.ToComponentDataArray<Radius>(Allocator.Temp);
            using var obstacleTransforms = obstacleQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < obstacleRadii.Length; i++)
            {
                float dist = math.distance(
                    new float2(position.x, position.z),
                    new float2(obstacleTransforms[i].Position.x, obstacleTransforms[i].Position.z));
                float minDist = buildingRadius + obstacleRadii[i].Value;
                if (dist < minDist)
                    return false;
            }

            // 3. Terrain passability check (matches MovementSystem / TerrainPassabilityGizmo)
            const float maxSlope = 0.55f;
            const float slopeStep = 1.5f;

            float h = TerrainUtility.GetHeight(position.x, position.z);

            // Water check — use WaterPlane singleton if available
            if (WaterPlane.Instance != null && WaterPlane.Instance.IsUnderwater(new UnityEngine.Vector3(position.x, h, position.z)))
                return false;

            // Slope check
            float hL = TerrainUtility.GetHeight(position.x - slopeStep, position.z);
            float hR = TerrainUtility.GetHeight(position.x + slopeStep, position.z);
            float hD = TerrainUtility.GetHeight(position.x, position.z - slopeStep);
            float hU = TerrainUtility.GetHeight(position.x, position.z + slopeStep);

            float dX = (hR - hL) / (slopeStep * 2f);
            float dZ = (hU - hD) / (slopeStep * 2f);
            float slope = math.sqrt(dX * dX + dZ * dZ);
            if (slope > maxSlope)
                return false;

            return true;
        }

        /// <summary>
        /// Get the collision radius for a building by its ID.
        /// Tries TechTreeDB first, falls back to hardcoded defaults.
        /// </summary>
        public static float GetBuildingRadius(string buildingId)
        {
            if (TechTreeDB.Instance != null &&
                TechTreeDB.Instance.TryGetBuilding(buildingId, out var def))
            {
                if (def.radius > 0) return def.radius;
            }
            return buildingId switch
            {
                "Hut" => 1.6f,
                "GatherersHut" => 0.5f,
                "Barracks" => 0.8f,
                "TempleOfRidan" => 1.8f,
                "VaultOfAlmierra" => 2.0f,
                "FiendstoneKeep" => 2.4f,
                "Alanthor_Wall" => 0.8f,
                "Alanthor_Smelter" => 1.5f,
                _ => 1.5f
            };
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
        }
    }
}