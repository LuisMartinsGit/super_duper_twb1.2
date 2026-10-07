// RegionTreePlanter.cs
// Plants a map's trees as Unity TERRAIN tree instances, from its regions.
//
// docs/Design/Territory_And_Nature.md (2026-10-06 header): trees are
// decoration on ground that is ALREADY blocked. They go in exactly three
// places, and all three are blocked by something other than the tree:
//
//   Forest region      planted solid      the region kind (NoWalk paint)
//   Mountain region    sparse, lower      the region kind (NoWalk paint)
//                      slopes only
//   Forest / Thicket   planted solid      the stand's disc
//   nature stand                          (NatureRegionBootstrap, at runtime)
//
// So a tree never decides passability, and the trees and the passability
// grid cannot disagree.
//
// Terrain trees rather than GameObjects: they are instanced and distance
// culled for free, they carry no per-tree Transform in the scene, and
// TreeInstance.prototypeIndex is what the per-owner look swap in
// Territory_And_Nature.md §8 rewrites.
//
// This REPLACES the terrain's whole tree layer. It is deterministic: the same
// scene and markers always plant the same trees, so re-running it after
// moving a marker only changes what the move changed.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.World.MapMarkers;
using TheWaningBorder.World.Regions;
using Kind = TheWaningBorder.World.MapMarkers.RegionSeedMarker.RegionKind;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class RegionTreePlanter
    {
        #region Tunables

        /// <summary>Canopy width a planted tree is scaled to, in metres.</summary>
        const float CanopyWidth = 5f;

        /// <summary>Random size spread around CanopyWidth, as a +/- fraction.</summary>
        const float SizeJitter = 0.2f;

        /// <summary>Height as a fraction of width. Synty trees are modelled tall
        /// for close-up use and read as stretched from an RTS camera; the same
        /// ratio MapGenKit's flora pass uses.</summary>
        const float HeightRatio = 0.5f;

        /// <summary>Metres between trees in a solid forest. Under CanopyWidth,
        /// so the canopy closes and the stand reads as a wall.</summary>
        const float ForestSpacing = 3.5f;

        /// <summary>Metres a solid forest's trees keep inside its edge, so the
        /// canopy does not hang far over the walkable ground beside it.</summary>
        const float ForestEdgeInset = 1f;

        /// <summary>Metres between candidate spots on a mountain's fringe.</summary>
        const float MountainSpacing = 6f;

        /// <summary>Depth of the band inside a mountain's edge that gets trees.</summary>
        const float MountainFringe = 16f;

        /// <summary>Chance of a tree at the very foot of a mountain, fading to 0
        /// at MountainFringe in.</summary>
        const float MountainDensity = 0.7f;

        /// <summary>Steepest mountain ground a tree grows on, in degrees.</summary>
        const float MountainMaxSlope = 35f;

        /// <summary>Jitter on every planting grid, as a fraction of its spacing,
        /// or the stand reads as a plantation.</summary>
        const float GridJitter = 0.4f;

        #endregion

        /// <summary>Living temperate trees for solid forests. The dead and burnt
        /// models are kept for the Blighted / Ashen looks (§2), and the Big Tree
        /// bases are set pieces, not woodland.</summary>
        static readonly string[] ForestSpecies =
        {
            "SM_Env_Tree_Large_", "SM_Env_Tree_Round_", "SM_Env_Tree_Thin_", "SM_Env_Tree_Pine_",
        };

        /// <summary>Of those, the ones that belong on a mountain.</summary>
        static readonly string[] MountainSpecies =
        {
            "SM_Env_Tree_Pine_", "SM_Env_Tree_Thin_",
        };

        [MenuItem("Waning Border/Maps/Plant Trees From Regions")]
        public static void PlantFromMenu()
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null)
            {
                Debug.LogError("[TreePlanter] the open scene has no active Terrain.");
                return;
            }

            var markers = Object.FindObjectsByType<RegionSeedMarker>(FindObjectsSortMode.None);
            InstallRegions(markers);

            // Run on its own, the planter also has to make Forest regions
            // block, which the full generator does in its Paint pass.
            int painted = PaintForestRegionsNoWalk(terrain);

            int trees = Plant(terrain, markers);

            EditorUtility.SetDirty(terrain.terrainData);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            Debug.Log($"[TreePlanter] {trees} tree(s) planted; NoWalk painted on {painted} "
                    + "forest-region texel(s). Save the scene.");
        }

        /// <summary>
        /// Replace the terrain's tree layer with trees for every Forest region,
        /// Mountain region fringe and forest nature stand. RegionMap must
        /// already be configured from <paramref name="markers"/>. Returns the
        /// number of trees planted.
        /// </summary>
        public static int Plant(Terrain terrain, RegionSeedMarker[] markers)
        {
            var data = terrain.terrainData;

            var forest = MapGenKit.FindPrefabs(ForestSpecies, 16);
            if (forest.Count == 0)
            {
                Debug.LogWarning("[TreePlanter] no Synty tree prefabs found — the map has no trees.");
                return 0;
            }

            // Mountain species are a subset of the forest list, so they share
            // its prototypes rather than registering the same prefab twice.
            var mountainIdx = new List<int>();
            for (int i = 0; i < forest.Count; i++)
                foreach (var prefix in MountainSpecies)
                    if (forest[i].name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    { mountainIdx.Add(i); break; }
            if (mountainIdx.Count == 0)
                for (int i = 0; i < forest.Count; i++) mountainIdx.Add(i);

            var allIdx = new List<int>();
            for (int i = 0; i < forest.Count; i++) allIdx.Add(i);

            var protos = new TreePrototype[forest.Count];
            var width = new float[forest.Count];
            for (int i = 0; i < forest.Count; i++)
            {
                protos[i] = new TreePrototype { prefab = forest[i], bendFactor = 0f };
                width[i] = MapGenKit.MeasurePrefabWidth(forest[i]);
            }

            var origin = terrain.transform.position;
            var size = data.size;
            var trees = new List<TreeInstance>();

            void Add(System.Random rng, List<int> species, float wx, float wz)
            {
                float u = (wx - origin.x) / size.x, v = (wz - origin.z) / size.z;
                if (u < 0f || u > 1f || v < 0f || v > 1f) return;

                int proto = species[rng.Next(species.Count)];
                float fit = width[proto] > 0.01f ? CanopyWidth / width[proto] : 1f;
                float scale = fit * (1f + ((float)rng.NextDouble() * 2f - 1f) * SizeJitter);
                trees.Add(new TreeInstance
                {
                    position = new Vector3(u, 0f, v),
                    prototypeIndex = proto,
                    widthScale = scale,
                    heightScale = scale * HeightRatio,
                    rotation = (float)(rng.NextDouble() * Mathf.PI * 2f),
                    color = Color.white,
                    lightmapColor = Color.white,
                });
            }

            // Markers come back from FindObjectsByType in no fixed order; sort
            // them so a re-run plants the same trees.
            var ordered = new List<RegionSeedMarker>(markers);
            ordered.Sort((a, b) => ComparePos(a.transform.position, b.transform.position));

            foreach (var m in ordered)
            {
                if (m.Shape == null || m.Shape.Length < 3) continue;
                if (m.Kind != Kind.Forest && m.Kind != Kind.Mountain) continue;

                var rng = new System.Random(Seed(m.transform.position));
                bool solid = m.Kind == Kind.Forest;
                float spacing = solid ? ForestSpacing : MountainSpacing;

                Bounds2(m.Shape, out Vector2 lo, out Vector2 hi);
                for (float z = lo.y; z <= hi.y; z += spacing)
                for (float x = lo.x; x <= hi.x; x += spacing)
                {
                    float jx = x + ((float)rng.NextDouble() * 2f - 1f) * spacing * GridJitter;
                    float jz = z + ((float)rng.NextDouble() * 2f - 1f) * spacing * GridJitter;
                    if (!Inside(m.Shape, jx, jz)) continue;

                    float edge = EdgeDistance(m.Shape, jx, jz);
                    if (solid)
                    {
                        if (edge < ForestEdgeInset) continue;
                        Add(rng, allIdx, jx, jz);
                    }
                    else
                    {
                        // The foot of the massif only: thick at the edge,
                        // thinning out as the ground climbs.
                        if (edge > MountainFringe) continue;
                        if (rng.NextDouble() > MountainDensity * (1f - edge / MountainFringe)) continue;
                        if (Steepness(terrain, jx, jz) > MountainMaxSlope) continue;
                        Add(rng, mountainIdx, jx, jz);
                    }
                }
            }

            // Forest stands inside claimable territories. Rock fields get no
            // trees — their props are not part of this pass.
            var stands = new List<NatureRegionMarker>(
                Object.FindObjectsByType<NatureRegionMarker>(FindObjectsSortMode.None));
            stands.Sort((a, b) => ComparePos(a.transform.position, b.transform.position));

            foreach (var s in stands)
            {
                if (s.Kind == NatureRegionMarker.NatureKind.Rocks) continue;

                var rng = new System.Random(Seed(s.transform.position));
                var c = s.transform.position;
                float r = Mathf.Max(1f, s.Radius) - ForestEdgeInset;
                if (r <= 0f) continue;

                for (float z = c.z - r; z <= c.z + r; z += ForestSpacing)
                for (float x = c.x - r; x <= c.x + r; x += ForestSpacing)
                {
                    float jx = x + ((float)rng.NextDouble() * 2f - 1f) * ForestSpacing * GridJitter;
                    float jz = z + ((float)rng.NextDouble() * 2f - 1f) * ForestSpacing * GridJitter;
                    float dx = jx - c.x, dz = jz - c.z;
                    if (dx * dx + dz * dz > r * r) continue;
                    Add(rng, allIdx, jx, jz);
                }
            }

            int replaced = data.treeInstanceCount;
            data.treePrototypes = protos;
            data.SetTreeInstances(trees.ToArray(), true);
            terrain.drawTreesAndFoliage = true;

            if (replaced > 0)
                Debug.Log($"[TreePlanter] replaced {replaced} existing terrain tree instance(s).");
            return trees.Count;
        }

        /// <summary>
        /// Paint the NoWalk layer over every Forest region, so a map whose
        /// regions were re-kinded without re-running the full generator still
        /// blocks them. Returns the number of alphamap texels painted.
        /// </summary>
        static int PaintForestRegionsNoWalk(Terrain terrain)
        {
            var data = terrain.terrainData;
            int noWalk = MapGenKit.FindNoWalkLayer(data);
            if (noWalk < 0)
            {
                Debug.LogWarning("[TreePlanter] no 'NoWalk' terrain layer — Forest regions "
                               + "will NOT block movement. Run Generate Environment From Regions.");
                return 0;
            }

            var origin = terrain.transform.position;
            var size = data.size;
            int aw = data.alphamapWidth, ah = data.alphamapHeight, layers = data.alphamapLayers;
            var maps = data.GetAlphamaps(0, 0, aw, ah);
            int painted = 0;

            for (int az = 0; az < ah; az++)
            for (int ax = 0; ax < aw; ax++)
            {
                float wx = origin.x + (ax + 0.5f) / aw * size.x;
                float wz = origin.z + (az + 0.5f) / ah * size.z;
                int r = RegionMap.RawRegionAt(wx, wz);
                if (r == RegionMap.None || RegionMap.KindOf(r) != Kind.Forest) continue;

                // Alphamaps are [z, x, layer] and must sum to 1.
                for (int l = 0; l < layers; l++) maps[az, ax, l] = l == noWalk ? 1f : 0f;
                painted++;
            }

            if (painted > 0) data.SetAlphamaps(0, 0, maps);
            return painted;
        }

        static void InstallRegions(RegionSeedMarker[] markers)
        {
            var seeds = new List<Vector2>(markers.Length);
            var names = new List<string>(markers.Length);
            var shapes = new List<Vector2[]>(markers.Length);
            var kinds = new List<Kind>(markers.Length);
            foreach (var m in markers)
            {
                var p = m.transform.position;
                seeds.Add(new Vector2(p.x, p.z));
                names.Add(m.RegionName);
                shapes.Add(m.Shape);
                kinds.Add(m.Kind);
            }
            RegionMap.Configure(seeds, names, shapes, kinds);
        }

        #region Helpers

        static int ComparePos(Vector3 a, Vector3 b)
        {
            int c = a.x.CompareTo(b.x);
            return c != 0 ? c : a.z.CompareTo(b.z);
        }

        /// <summary>A per-marker seed from its position, so moving one marker
        /// re-plants only that marker's trees.</summary>
        static int Seed(Vector3 p) =>
            unchecked(Mathf.RoundToInt(p.x * 10f) * 73856093 ^ Mathf.RoundToInt(p.z * 10f) * 19349663);

        static float Steepness(Terrain t, float wx, float wz)
        {
            var origin = t.transform.position;
            var size = t.terrainData.size;
            return t.terrainData.GetSteepness(Mathf.Clamp01((wx - origin.x) / size.x),
                                              Mathf.Clamp01((wz - origin.z) / size.z));
        }

        static void Bounds2(Vector2[] poly, out Vector2 lo, out Vector2 hi)
        {
            lo = poly[0]; hi = poly[0];
            foreach (var p in poly) { lo = Vector2.Min(lo, p); hi = Vector2.Max(hi, p); }
        }

        static bool Inside(Vector2[] poly, float x, float z)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > z) == (poly[j].y > z)) continue;
                float t = (z - poly[i].y) / (poly[j].y - poly[i].y);
                if (x < poly[i].x + t * (poly[j].x - poly[i].x)) inside = !inside;
            }
            return inside;
        }

        static float EdgeDistance(Vector2[] poly, float x, float z)
        {
            float best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i];
                float abx = b.x - a.x, abz = b.y - a.y;
                float len2 = abx * abx + abz * abz;
                float t = len2 <= 1e-6f ? 0f
                    : Mathf.Clamp01(((x - a.x) * abx + (z - a.y) * abz) / len2);
                best = Mathf.Min(best, Vector2.Distance(new Vector2(x, z),
                                                        new Vector2(a.x + abx * t, a.y + abz * t)));
            }
            return best;
        }

        #endregion
    }
}
