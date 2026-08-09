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
            if (terrain == null || terrain.terrainData == null) return false;
            // Also require the procedural generation coroutine to have
            // finished — ProceduralTerrain.Awake now creates the Terrain
            // GameObject fast and the heavy heightmap/erosion/texture pass
            // runs in a Start() coroutine. Without this gate, callers like
            // SpawnDelayHelper would proceed against an empty heightmap.
            return ProceduralTerrain.IsGenerationComplete;
        }

        /// <summary>
        /// Check if terrain is ready, with out parameter for the terrain reference.
        /// </summary>
        public static bool IsReady(out UnityEngine.Terrain terrain)
        {
            terrain = UnityEngine.Terrain.activeTerrain;
            if (terrain == null || terrain.terrainData == null) return false;
            return ProceduralTerrain.IsGenerationComplete;
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
        /// World-space XZ bounds of the playable area, read from the ACTUAL
        /// Terrain object(s) in the scene (union over all active tiles).
        /// Unity terrains are corner-anchored: they span transform.position ..
        /// position + terrainData.size, so hand-authored maps are usually NOT
        /// centred on the origin. Returns false when no terrain exists yet.
        /// </summary>
        public static bool TryGetWorldBounds(out Vector2 min, out Vector2 max)
        {
            min = Vector2.zero;
            max = Vector2.zero;
            bool found = false;

            var tiles = UnityEngine.Terrain.activeTerrains;
            if (tiles != null)
            {
                for (int i = 0; i < tiles.Length; i++)
                {
                    var t = tiles[i];
                    if (t == null || t.terrainData == null) continue;
                    var p = t.transform.position;
                    var s = t.terrainData.size;
                    var tileMin = new Vector2(p.x, p.z);
                    var tileMax = new Vector2(p.x + s.x, p.z + s.z);
                    if (!found) { min = tileMin; max = tileMax; found = true; }
                    else { min = Vector2.Min(min, tileMin); max = Vector2.Max(max, tileMax); }
                }
            }

            if (!found)
            {
                // GetActiveTerrain covers the ProcTerrain-by-name fallback.
                var t = GetActiveTerrain();
                if (t != null)
                {
                    var p = t.transform.position;
                    var s = t.terrainData.size;
                    min = new Vector2(p.x, p.z);
                    max = new Vector2(p.x + s.x, p.z + s.z);
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Like <see cref="TryGetWorldBounds"/> but never fails: falls back to
        /// the origin-centred GameSettings.MapHalfSize box when no Terrain
        /// exists yet (early bootstrap, terrainless test scenes).
        /// </summary>
        public static void GetPlayableBounds(out Vector2 min, out Vector2 max)
        {
            if (TryGetWorldBounds(out min, out max)) return;
            float half = Mathf.Max(1, GameSettings.MapHalfSize);
            min = new Vector2(-half, -half);
            max = new Vector2(half, half);
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
        /// Walking-surface height at (x, z) for something currently at
        /// <paramref name="referenceY"/>. Where a bridge deck (BridgeSurface)
        /// spans the point there are TWO surfaces — the ground and the deck —
        /// and the deck wins whenever it is within STEP-UP reach of the
        /// reference (BridgeSurface.MountStepLimit above it, or anywhere
        /// below it): a unit at a ramp toe steps up onto the deck, a unit on
        /// the deck stays on it up- and down-slope, and a unit under a tall
        /// arch (deck far overhead) stays on the ground. A pure
        /// nearest-surface rule fails at the ramp toe — walkable ground there
        /// is closer than the ramp's top face, so units never mounted.
        /// Use this for units/visuals that have a current position; use
        /// GetHeight for stateless queries (spawns, projectile ground checks,
        /// bounds). Deterministic — pure cached math on static scene data.
        /// </summary>
        public static float GetSurfaceHeight(float x, float z, float referenceY)
        {
            float ground = GetHeight(x, z);

            if (BridgeSurface.HasAny
                && BridgeSurface.TryGetDeckHeight(x, z, out float deckY)
                && deckY > ground
                && deckY - referenceY <= BridgeSurface.MountStepLimit)
            {
                return deckY;
            }

            return ground;
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