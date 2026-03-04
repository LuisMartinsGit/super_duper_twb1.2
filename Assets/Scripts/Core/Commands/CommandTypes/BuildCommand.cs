// BuildCommand.cs
// Build command component and execution logic
// Location: Assets/Scripts/Core/Commands/CommandTypes/BuildCommand.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

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
        /// Check if a position is valid for building placement
        /// </summary>
        public static bool IsValidBuildPosition(EntityManager em, float3 position, float buildingRadius)
        {
            // TODO: Implement collision checking with other buildings/units
            // For now, assume all positions are valid
            return true;
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