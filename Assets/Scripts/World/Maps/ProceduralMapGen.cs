// ProceduralMapGen.cs
//
// Top-level orchestrator for the procedural map system. Single entry point
// `Generate(archetype, seed, td)` which:
//   1. Places tagged regions per archetype.
//   2. Builds the procedural heightmap with slope budgets.
//   3. Verifies TravelLane connectivity (slope ≤ budget at every sample).
//      If verification fails → re-roll seed (cap 10 retries).
//   4. Builds procedural per-layer textures + assigns to TerrainData.
//   5. Builds the splatmap from final heights + slope.
//
// Output: `Current` global with the region list, so PlayerSpawn /
// CrystalNodeBootstrap / Resource spawn can read where things go.

using UnityEngine;

namespace TheWaningBorder.World.Maps
{
    public static class ProceduralMapGen
    {
        // Most-recently-generated map. Other systems (spawn, AI) read this.
        public static MapRegionSet Current { get; private set; }
        public static bool IsActive => Current != null;

        public const int MaxRetries = 10;

        public sealed class Result
        {
            public bool   success;
            public int    retries;
            public string failureReason;
            public MapRegionSet regions;
        }

        public static Result Generate(MapArchetype arch, int initialSeed, int playerCount,
                                      UnityEngine.TerrainData td, float waterPlaneY,
                                      Vector2 worldMin, Vector2 worldMax)
        {
            int seed = initialSeed;
            string lastFailure = null;
            for (int retry = 0; retry <= MaxRetries; retry++)
            {
                // 1a. Draft pass — place every region EXCEPT travel lanes.
                //     We need to know which pairs already have a walkable
                //     natural path before deciding which lanes to carve,
                //     otherwise every map gets a full mesh of flat highways
                //     on terrain that's already gentle.
                var draftSet = RegionPlacer.Place(arch, seed, playerCount, worldMin, worldMax, includeLanes: false);
                var draftHeights = ProceduralHeightmap.Build(arch, seed, draftSet, td, waterPlaneY, worldMin, worldMax);

                // 1b. Final region set — start from the lane-free draft, then
                //     re-add only lanes whose pair is NOT already walkable on
                //     the draft heightmap. Plain is an open battlefield: no
                //     lanes at all, players walk through hills.
                var set = RegionPlacer.Place(arch, seed, playerCount, worldMin, worldMax, includeLanes: false);
                int startCount = RegionPlacer.PlayerStartCount(arch, seed, playerCount, worldMin, worldMax);
                int lanesKept = 0, lanesSkipped = 0;
                bool carveLanes = arch != MapArchetype.Plain;
                if (carveLanes)
                {
                    for (int i = 0; i < startCount; i++)
                    for (int j = i + 1; j < startCount; j++)
                    {
                        var poly = RegionPlacer.BuildLanePolyline(arch, seed, playerCount, worldMin, worldMax, i, j);
                        if (poly == null) continue;
                        if (ProceduralHeightmap.PolylineIsWalkable(draftHeights, td, poly, worldMin, worldMax,
                                                                    RegionPlacer.Budget_TravelLane))
                        {
                            lanesSkipped++;
                            continue;
                        }
                        set.regions.Add(MapRegion.Lane(RegionTag.TravelLane, poly,
                                                       RegionPlacer.Width_TravelLane,
                                                       RegionPlacer.Budget_TravelLane));
                        lanesKept++;
                    }
                }

                // 2. Heightmap (reuse draft when no lanes were carved).
                var heights = (lanesKept == 0)
                    ? draftHeights
                    : ProceduralHeightmap.Build(arch, seed, set, td, waterPlaneY, worldMin, worldMax);

                // 3. Connectivity verification.
                bool ok = ProceduralHeightmap.VerifyConnectivity(heights, td, set, worldMin, worldMax, out string why);
                if (!ok)
                {
                    lastFailure = why;
                    Debug.LogWarning($"[ProceduralMapGen] retry {retry + 1}/{MaxRetries}: {why}");
                    seed = unchecked(seed * 16807 + 1); // re-roll
                    continue;
                }

                // 4. Build procedural texture layers and assign.
                td.terrainLayers = BuildLayers(seed);

                // 5. Apply heights to terrain.
                td.SetHeights(0, 0, heights);

                // Publish the region set BEFORE building the splatmap so
                // ProceduralSplat can read the region set via Current to
                // gate its mountain-mask paint by region distance (otherwise
                // rock would paint over player areas where the bare FBM
                // happens to be high).
                set.rejectRetries = retry;
                Current = set;

                // 6. Splatmap.
                var splat = ProceduralSplat.Build(arch, seed, td, worldMin, worldMax, waterPlaneY);
                td.SetAlphamaps(0, 0, splat);
                Debug.Log($"[ProceduralMapGen] '{arch}' seed={initialSeed}->{seed} retries={retry} " +
                          $"regions={set.regions.Count} lanes_kept={lanesKept} lanes_skipped={lanesSkipped}");
                return new Result { success = true, retries = retry, regions = set };
            }
            Debug.LogError($"[ProceduralMapGen] giving up after {MaxRetries} retries. Last failure: {lastFailure}");
            return new Result { success = false, retries = MaxRetries, failureReason = lastFailure };
        }

        // Layer indices must match ProceduralSplat's L_* contract.
        public static TerrainLayer[] BuildLayers(int seed)
        {
            return new TerrainLayer[]
            {
                ProceduralTextures.BuildTerrainLayer(LayerKind.SeaFloor, seed),
                ProceduralTextures.BuildTerrainLayer(LayerKind.Sand,     seed ^ 0x101),
                ProceduralTextures.BuildTerrainLayer(LayerKind.Grass,    seed ^ 0x202),
                ProceduralTextures.BuildTerrainLayer(LayerKind.Forest,   seed ^ 0x303),
                ProceduralTextures.BuildTerrainLayer(LayerKind.Dirt,     seed ^ 0x404),
                ProceduralTextures.BuildTerrainLayer(LayerKind.Rock,     seed ^ 0x505),
                ProceduralTextures.BuildTerrainLayer(LayerKind.Snow,     seed ^ 0x606),
            };
        }

        public static void ClearCurrent() => Current = null;
    }
}
