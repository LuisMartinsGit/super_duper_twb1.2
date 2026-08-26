// ProceduralTerrain.cs
// Compatibility shim. Procedural map generation has been removed — the game
// ships hand-authored maps only (baked Unity Terrain). Nothing creates a
// ProceduralTerrain instance any more, so Instance is always null and the
// runtime systems that used to call into it (border-ground painting, build
// flatten, minimap water level, passability/bounds) fall through their
// existing `Instance != null` guards exactly as they already did on
// hand-authored maps.
//
// Only the two static members are live:
//   - IsGenerationComplete : the bootstrap-wide "terrain is ready" gate
//     (TerrainUtility.IsReady / SpawnDelayHelper / NavMeshManager / PassabilityGrid).
//   - MarkExternalTerrainReady() : flips that gate; called by GameBootstrap.
//
// The instance fields / methods below exist purely so the (dead, null-guarded)
// call sites keep compiling. They are never executed.

using UnityEngine;

namespace TheWaningBorder.World.Terrain
{
    public class ProceduralTerrain : MonoBehaviour
    {
        /// <summary>Always null — no procedural terrain is created any more.
        /// Callers guard on this and use the active Unity Terrain instead.</summary>
        public static ProceduralTerrain Instance { get; private set; }

        /// <summary>Bootstrap-wide "terrain is ready" gate. Flipped true by
        /// <see cref="MarkExternalTerrainReady"/> once the scene's baked
        /// Unity Terrain is in place.</summary>
        public static bool IsGenerationComplete { get; private set; }

        /// <summary>
        /// Marks the readiness gate complete for a scene that ships its own
        /// baked Unity Terrain (hand-authored map). Called by GameBootstrap in
        /// place of running any generation.
        /// </summary>
        public static void MarkExternalTerrainReady()
        {
            IsGenerationComplete = true;
        }

        // ── Dead, compile-only API (Instance is always null) ───────────────
        // Retained so the null-guarded call sites in PassabilityGrid,
        // BuildCommand(Pannel), GathererHutAreaDisplay, MinimapRenderer,
        // BorderSpreadSystem and BorderGroundRecessionSystem keep compiling.

        public Vector2 worldMin = new Vector2(-256, -256);
        public Vector2 worldMax = new Vector2(256, 256);
        public float waterHeight = 20f;

        public void FlattenAt(Vector3 worldCenter, float halfExtent) { }
        public void PaintBorderGround(float worldX, float worldZ, float radius) { }
        public void UnpaintBorderGround(float worldX, float worldZ, float radius) { }
    }
}
