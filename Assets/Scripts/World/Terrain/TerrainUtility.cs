// TerrainUtility.cs
// Centralized terrain utility functions
// Location: Assets/Scripts/World/Terrain/TerrainUtility.cs

using UnityEngine;

namespace TheWaningBorder.World.Terrain
{
    /// <summary>
    /// Shared terrain utility functions used across the codebase.
    /// Eliminates duplicate terrain height lookup code.
    /// </summary>
    public static class TerrainUtility
    {
        private const float RaycastOriginHeight = 1000f;
        private const float RaycastDistance = 2000f;

        /// <summary>
        /// Check if terrain is ready and has valid data.
        /// </summary>
        public static bool IsReady()
        {
            var terrain = UnityEngine.Terrain.activeTerrain;
            return terrain != null && terrain.terrainData != null;
        }

        /// <summary>
        /// Check if terrain is ready, with out parameter for the terrain reference.
        /// </summary>
        public static bool IsReady(out UnityEngine.Terrain terrain)
        {
            terrain = UnityEngine.Terrain.activeTerrain;
            return terrain != null && terrain.terrainData != null;
        }

        /// <summary>
        /// Get the active terrain reference, or null if not available.
        /// </summary>
        public static UnityEngine.Terrain GetActiveTerrain()
        {
            // Primary: use Unity's active terrain
            var terrain = UnityEngine.Terrain.activeTerrain;
            if (terrain != null && terrain.terrainData != null)
                return terrain;

            // Fallback: search active terrains array
            foreach (var t in UnityEngine.Terrain.activeTerrains)
            {
                if (t != null && t.terrainData != null)
                    return t;
            }

            // Last resort: find by name
            var go = GameObject.Find("ProcTerrain");
            if (go != null)
            {
                terrain = go.GetComponent<UnityEngine.Terrain>();
                if (terrain != null && terrain.terrainData != null)
                    return terrain;
            }

            return null;
        }

        /// <summary>
        /// Get terrain height at world position (x, z).
        /// Falls back to raycast, then to 0f.
        /// </summary>
        public static float GetHeight(float x, float z)
        {
            var terrain = GetActiveTerrain();

            if (terrain != null)
            {
                return terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y;
            }

            // Fallback: raycast from above
            if (Physics.Raycast(
                new Vector3(x, RaycastOriginHeight, z),
                Vector3.down,
                out RaycastHit hit,
                RaycastDistance))
            {
                return hit.point.y;
            }

            return 0f;
        }

        /// <summary>
        /// Get terrain height at Vector3 position (uses x and z).
        /// </summary>
        public static float GetHeight(Vector3 position)
        {
            return GetHeight(position.x, position.z);
        }

        /// <summary>
        /// Get terrain height at Unity.Mathematics float3 position (uses x and z).
        /// </summary>
        public static float GetHeight(Unity.Mathematics.float3 position)
        {
            return GetHeight(position.x, position.z);
        }

        /// <summary>
        /// Snap a position's Y to terrain height.
        /// </summary>
        public static Vector3 SnapToTerrain(Vector3 position)
        {
            position.y = GetHeight(position.x, position.z);
            return position;
        }

        /// <summary>
        /// Snap a float3 position's Y to terrain height.
        /// </summary>
        public static Unity.Mathematics.float3 SnapToTerrain(Unity.Mathematics.float3 position)
        {
            position.y = GetHeight(position.x, position.z);
            return position;
        }

        /// <summary>
        /// Get interpolated height using UV coordinates (0-1 range).
        /// Useful for splatmap generation.
        /// </summary>
        public static float GetInterpolatedHeight(UnityEngine.Terrain terrain, float u, float v)
        {
            if (terrain == null || terrain.terrainData == null)
                return 0f;

            return terrain.terrainData.GetInterpolatedHeight(u, v);
        }

        /// <summary>
        /// Get interpolated normal using UV coordinates (0-1 range).
        /// Useful for slope calculations.
        /// </summary>
        public static Vector3 GetInterpolatedNormal(UnityEngine.Terrain terrain, float u, float v)
        {
            if (terrain == null || terrain.terrainData == null)
                return Vector3.up;

            return terrain.terrainData.GetInterpolatedNormal(u, v);
        }
    }
}