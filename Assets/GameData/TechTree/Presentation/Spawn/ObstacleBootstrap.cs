// File: Assets/GameData/TechTree/Presentation/Spawn/ObstacleBootstrap.cs
// Forest / rock obstacle constants + registry. Procedural scatter spawning was
// removed with procedural maps — hand-authored maps bake their own vegetation
// into the scene's Unity Terrain. These symbols remain because the presentation
// and minimap layers reference them.

using System.Collections.Generic;
using Unity.Mathematics;

namespace TheWaningBorder.Bootstrap
{
    public static class ObstacleBootstrap
    {
        // Presentation IDs (must match PresentationSpawnSystem)
        public const int ForestPresentationId = 400;
        public const int RockPresentationId = 401;

        /// <summary>
        /// Forest center positions and radii, used by MinimapRenderer to draw
        /// forest areas. Empty on hand-authored maps (vegetation is baked into
        /// the scene's Unity Terrain rather than spawned procedurally).
        /// </summary>
        public static readonly List<(float3 center, float radius)> ForestPositions = new();
    }
}
