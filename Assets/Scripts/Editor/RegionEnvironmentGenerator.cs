// RegionEnvironmentGenerator.cs
// Generates a map's environment FROM its regions.
//
// Regions.md § Region kinds: every feature of the map is a region, and the
// terrain is generated from the region's kind and shape rather than the kind
// being inferred from terrain someone sculpted by hand.
//
// This is a GENERATOR, not a preserving pass: it overwrites the heightmap and
// clears what it previously produced. It is for making maps, not for editing
// one you have hand-tuned.
//
//   Water     excavated to the region's shape
//   Mountain  raised to the shape, then a noise + erosion pass
//   Obstacle  nothing generated; RegionMap already reports it impassable
//   Forest    tree PREFABS planted inside the shape, with a clear edge band
//   Normal    randomized resource nodes
//   Start     a richer node set
//
// Then two passes over the whole map: the ground is PAINTED from the same
// region kinds that sculpted it, and every water region asks for a water
// plane over its own bounds.
//
// Two guarantees the generator owes the map:
//   1. every region polygon is CLAMPED to the terrain bounds
//   2. no resource node lands on impassable ground or outside the map

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.World.MapMarkers;
using TheWaningBorder.World.Regions;
using Kind = TheWaningBorder.World.MapMarkers.RegionSeedMarker.RegionKind;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class RegionEnvironmentGenerator
    {
        #region Tunables

        /// <summary>Metres a water region is cut below the surrounding ground.</summary>
        const float WaterDepth = 6f;

        /// <summary>Metres a mountain region is lifted above it.</summary>
        const float MountainHeight = 45f;

        /// <summary>Fraction of the way in from a shape's edge that reaches full depth/height.</summary>
        const float EdgeFalloff = 0.35f;

        /// <summary>Amplitude of the mountain noise, as a fraction of MountainHeight.</summary>
        const float RidgeAmount = 0.35f;

        /// <summary>Smoothing passes over the generated relief — the erosion pass.</summary>
        const int ErosionPasses = 3;

        /// <summary>Metres of forest left clear at the edge, so a Forester can sit there.</summary>
        const float ForestEdgeClear = 8f;

        /// <summary>Metres between planted trees.</summary>
        const float TreeSpacing = 6f;

        /// <summary>Nodes per region.</summary>
        const int NormalNodesMin = 1, NormalNodesMax = 3, StartNodes = 5;

        /// <summary>Steepest ground a node may sit on, in degrees.</summary>
        const float MaxNodeSlope = 25f;

        const string GeneratedRoot = "~GeneratedEnvironment";

        #endregion

        [MenuItem("Waning Border/Maps/Generate Environment From Regions")]
        public static void Generate()
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null)
            {
                Debug.LogError("[RegionGen] the open scene has no active Terrain.");
                return;
            }

            var markers = Object.FindObjectsByType<RegionSeedMarker>(FindObjectsSortMode.None);
            if (markers.Length == 0)
            {
                Debug.LogError("[RegionGen] no RegionSeedMarkers. Run "
                             + "Waning Border > Maps > Seed Regions For Open Scene first.");
                return;
            }

            Bounds(terrain, out Vector2 min, out Vector2 max);

            int clamped = ClampShapes(markers, min, max);
            Install(markers);

            int water = 0, mountain = 0;
            Sculpt(terrain, min, max, ref water, ref mountain);

            Paint(terrain);

            var root = FreshRoot();
            int trees = PlantForests(markers, root, min, max);
            int nodes = ScatterNodes(markers, root, min, max);
            int water_planes = FloodWater(markers, root, min, max);

            EditorUtility.SetDirty(terrain.terrainData);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);

            Debug.Log($"[RegionGen] {markers.Length} regions — {clamped} shape(s) clamped to bounds, "
                    + $"{water} water + {mountain} mountain cell(s) sculpted, "
                    + $"{trees} tree(s), {nodes} node(s), {water_planes} water plane(s). "
                    + "Save the scene.");
        }

        #region Bounds and clamping

        static void Bounds(Terrain t, out Vector2 min, out Vector2 max)
        {
            var origin = t.transform.position;
            var size = t.terrainData.size;
            min = new Vector2(origin.x, origin.z);
            max = new Vector2(origin.x + size.x, origin.z + size.z);
        }

        /// <summary>
        /// Pull every authored point inside the terrain. A region hanging off the
        /// edge would generate water or mountain against a hard rectangular cut,
        /// and its resource nodes would land where nothing can reach them.
        /// </summary>
        static int ClampShapes(RegionSeedMarker[] markers, Vector2 min, Vector2 max)
        {
            int changed = 0;

            foreach (var m in markers)
            {
                if (m.Shape == null || m.Shape.Length < 3) continue;

                bool touched = false;
                for (int i = 0; i < m.Shape.Length; i++)
                {
                    var p = m.Shape[i];
                    var c = new Vector2(Mathf.Clamp(p.x, min.x, max.x),
                                        Mathf.Clamp(p.y, min.y, max.y));
                    if (c != p) { m.Shape[i] = c; touched = true; }
                }

                if (touched) { changed++; EditorUtility.SetDirty(m); }
            }
            return changed;
        }

        static void Install(RegionSeedMarker[] markers)
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

        #endregion

        #region Terrain

        /// <summary>
        /// Cut water down and push mountains up, both shaped by how deep inside
        /// the region a cell is, then smooth the result so the relief reads as
        /// eroded rather than stamped.
        /// </summary>
        static void Sculpt(Terrain terrain, Vector2 min, Vector2 max, ref int water, ref int mountain)
        {
            var data = terrain.terrainData;
            int res = data.heightmapResolution;
            float maxY = data.size.y;
            var heights = data.GetHeights(0, 0, res, res);

            Vector2 span = max - min;

            for (int y = 0; y < res; y++)
            {
                float wz = min.y + (float)y / (res - 1) * span.y;

                for (int x = 0; x < res; x++)
                {
                    float wx = min.x + (float)x / (res - 1) * span.x;

                    int r = RegionMap.RawRegionAt(wx, wz);
                    if (r == RegionMap.None) continue;

                    var kind = RegionMap.KindOf(r);
                    if (kind != Kind.Water && kind != Kind.Mountain) continue;

                    // Falloff by distance to the region's edge, so a lake has a
                    // shore and a massif has a foot instead of a vertical wall.
                    float t = InsetFactor(RegionMap.ShapeOf(r), wx, wz, span);

                    if (kind == Kind.Water)
                    {
                        heights[y, x] -= WaterDepth * t / maxY;
                        water++;
                    }
                    else
                    {
                        float ridge = (Mathf.PerlinNoise(wx * 0.02f, wz * 0.02f) - 0.5f) * 2f;
                        heights[y, x] += MountainHeight * t * (1f + ridge * RidgeAmount) / maxY;
                        mountain++;
                    }

                    heights[y, x] = Mathf.Clamp01(heights[y, x]);
                }
            }

            Erode(heights, res);
            data.SetHeights(0, 0, heights);
        }

        /// <summary>
        /// 0 at the region's edge, 1 well inside it. Approximated from the
        /// distance to the nearest polygon segment; a region with no authored
        /// shape gets a flat 1, which is the old stamped behaviour.
        /// </summary>
        static float InsetFactor(Vector2[] poly, float wx, float wz, Vector2 span)
        {
            if (poly == null || poly.Length < 3) return 1f;

            float best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                best = Mathf.Min(best, DistanceToSegment(wx, wz, poly[j], poly[i]));

            float reach = Mathf.Max(1f, Mathf.Min(span.x, span.y) * EdgeFalloff * 0.1f);
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(best / reach));
        }

        static float DistanceToSegment(float px, float pz, Vector2 a, Vector2 b)
        {
            float abx = b.x - a.x, abz = b.y - a.y;
            float len2 = abx * abx + abz * abz;
            if (len2 <= 1e-6f) return Vector2.Distance(new Vector2(px, pz), a);

            float t = Mathf.Clamp01(((px - a.x) * abx + (pz - a.y) * abz) / len2);
            return Vector2.Distance(new Vector2(px, pz), new Vector2(a.x + abx * t, a.y + abz * t));
        }

        /// <summary>A few box blurs — cheap erosion that softens the seams.</summary>
        static void Erode(float[,] h, int res)
        {
            for (int pass = 0; pass < ErosionPasses; pass++)
            {
                for (int y = 1; y < res - 1; y++)
                for (int x = 1; x < res - 1; x++)
                {
                    h[y, x] = (h[y, x] * 4f
                             + h[y - 1, x] + h[y + 1, x]
                             + h[y, x - 1] + h[y, x + 1]) / 8f;
                }
            }
        }

        #endregion

        #region Forests and nodes

        static GameObject FreshRoot()
        {
            var existing = GameObject.Find(GeneratedRoot);
            if (existing != null) Object.DestroyImmediate(existing);
            return new GameObject(GeneratedRoot);
        }

        static int PlantForests(RegionSeedMarker[] markers, GameObject root,
                                Vector2 min, Vector2 max)
        {
            var prefabs = ForestPrefabs();
            if (prefabs.Count == 0)
            {
                Debug.LogWarning("[RegionGen] no tree prefabs found — forests will be empty.");
                return 0;
            }

            var parent = new GameObject("Forests");
            parent.transform.SetParent(root.transform, false);

            int planted = 0;

            foreach (var m in markers)
            {
                if (m.Kind != Kind.Forest || m.Shape == null || m.Shape.Length < 3) continue;

                PolyBounds(m.Shape, out Vector2 lo, out Vector2 hi);

                for (float z = lo.y; z <= hi.y; z += TreeSpacing)
                for (float x = lo.x; x <= hi.x; x += TreeSpacing)
                {
                    // Jitter, or the stand reads as a plantation grid.
                    float jx = x + Random.Range(-TreeSpacing, TreeSpacing) * 0.35f;
                    float jz = z + Random.Range(-TreeSpacing, TreeSpacing) * 0.35f;

                    if (!InBounds(jx, jz, min, max)) continue;
                    if (!Inside(m.Shape, jx, jz)) continue;

                    // Leave the rim clear: a Forester is placed at the EDGE of a
                    // forest, and a tree standing on that edge blocks the plot.
                    if (EdgeDistance(m.Shape, jx, jz) < ForestEdgeClear) continue;

                    var prefab = prefabs[Random.Range(0, prefabs.Count)];
                    var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
                    tree.transform.position = new Vector3(jx, MapGenKit.SampleHeight(jx, jz), jz);
                    tree.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                    tree.transform.localScale = Vector3.one * Random.Range(0.85f, 1.25f);
                    planted++;
                }
            }
            return planted;
        }

        static int ScatterNodes(RegionSeedMarker[] markers, GameObject root,
                                Vector2 min, Vector2 max)
        {
            var parent = new GameObject("ResourceNodes");
            parent.transform.SetParent(root.transform, false);

            int placed = 0;

            foreach (var m in markers)
            {
                bool start = m.Kind == Kind.PlayerStart;
                if (!start && m.Kind != Kind.Normal && m.Kind != Kind.Forest) continue;

                int want = start ? StartNodes : Random.Range(NormalNodesMin, NormalNodesMax + 1);

                for (int i = 0; i < want; i++)
                {
                    if (!TryFindSpot(m, min, max, out Vector3 pos)) continue;

                    // A start region gets the full opening set; elsewhere it is
                    // whichever node the roll gives.
                    int roll = start ? i % 4 : Random.Range(0, 4);
                    switch (roll)
                    {
                        case 0: MapGenKit.Marker<SupplyNodeMarker>(parent, "Supply", pos); break;
                        case 1: MapGenKit.Marker<IronPatchMarker>(parent, "Iron", pos); break;
                        case 2: MapGenKit.Marker<VeilstoneOutcroppingMarker>(parent, "Veilstone", pos); break;
                        default: MapGenKit.Marker<VeilsteelDepositMarker>(parent, "Veilsteel", pos); break;
                    }
                    placed++;
                }
            }
            return placed;
        }

        /// <summary>
        /// A spot inside the region that is in bounds, on claimable ground and
        /// not too steep. Gives up rather than placing a node somewhere nothing
        /// can reach.
        /// </summary>
        static bool TryFindSpot(RegionSeedMarker m, Vector2 min, Vector2 max, out Vector3 pos)
        {
            pos = default;
            var terrain = Terrain.activeTerrain;
            if (terrain == null) return false;

            bool hasShape = m.Shape != null && m.Shape.Length >= 3;
            if (hasShape) PolyBounds(m.Shape, out min, out max);

            for (int attempt = 0; attempt < 48; attempt++)
            {
                float x = Random.Range(min.x, max.x);
                float z = Random.Range(min.y, max.y);

                if (!InBounds(x, z, min, max)) continue;
                if (hasShape && !Inside(m.Shape, x, z)) continue;

                // Never on ground the map says nothing can hold — water,
                // mountain and obstacle regions all fail this.
                if (!RegionMap.IsClaimable(x, z)) continue;
                if (Slope(terrain, x, z) > MaxNodeSlope) continue;

                pos = new Vector3(x, MapGenKit.SampleHeight(x, z), z);
                return true;
            }
            return false;
        }

        static float Slope(Terrain t, float wx, float wz)
        {
            var origin = t.transform.position;
            var size = t.terrainData.size;
            float u = Mathf.Clamp01((wx - origin.x) / size.x);
            float v = Mathf.Clamp01((wz - origin.z) / size.z);
            return t.terrainData.GetSteepness(u, v);
        }

        static List<GameObject> ForestPrefabs()
        {
            var found = new List<GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab tree"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Editor/")) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) found.Add(go);
                if (found.Count >= 12) break;
            }
            return found;
        }

        #endregion

        #region Paint and water

        /// <summary>
        /// Paint the ground from the SAME region kinds that sculpted it, so the
        /// look and the rules cannot drift apart: what reads as rock is what the
        /// map calls impassable, and a lakebed reads as bare ground.
        ///
        /// MapGenKit.PaintGround does the blending; this only answers its two
        /// questions — how walled is this point, and how bare.
        /// </summary>
        static void Paint(Terrain terrain)
        {
            string folder = MapFolderOf(terrain);
            if (folder == null)
            {
                Debug.LogWarning("[RegionGen] could not resolve a map folder — skipping paint.");
                return;
            }

            MapGenKit.PaintGround(terrain.terrainData, new MapGenKit.PaintSpec
            {
                MapFolder = folder,
                Size = Mathf.RoundToInt(terrain.terrainData.size.x),

                // Walled where the map says nothing may pass.
                NoWalk = (wx, wz) =>
                {
                    var k = KindAt(wx, wz);
                    return k == Kind.Mountain || k == Kind.Obstacle ? 1f : 0f;
                },

                // Bare where the water sits: this is the lakebed, and it has to
                // read as riverbed rather than as drowned grass.
                Dirt = (wx, wz) => KindAt(wx, wz) == Kind.Water ? 1f : 0f,
            });
        }

        static Kind KindAt(float wx, float wz)
        {
            int r = RegionMap.RawRegionAt(wx, wz);
            return r == RegionMap.None ? Kind.Normal : RegionMap.KindOf(r);
        }

        /// <summary>
        /// The folder the map's own assets live in — where PaintGround writes
        /// its TerrainLayers. Taken from the TerrainData asset, which is always
        /// beside the scene for a generated map.
        /// </summary>
        static string MapFolderOf(Terrain terrain)
        {
            string path = AssetDatabase.GetAssetPath(terrain.terrainData);
            if (!string.IsNullOrEmpty(path)) return System.IO.Path.GetDirectoryName(path).Replace('\\', '/');

            var scene = terrain.gameObject.scene;
            return string.IsNullOrEmpty(scene.path)
                ? null
                : System.IO.Path.GetDirectoryName(scene.path).Replace('\\', '/');
        }

        /// <summary>
        /// One water plane per water region, over that region's own bounds.
        ///
        /// The surface sits at the SHORE, sampled around the polygon's rim: the
        /// excavation cut the middle down, so the rim is what the water should
        /// come up to. Taking the region centre instead would put the surface at
        /// the bottom of the hole it just dug.
        /// </summary>
        static int FloodWater(RegionSeedMarker[] markers, GameObject root,
                              Vector2 min, Vector2 max)
        {
            var parent = new GameObject("Water");
            parent.transform.SetParent(root.transform, false);

            int made = 0;

            foreach (var m in markers)
            {
                if (m.Kind != Kind.Water) continue;
                if (m.Shape == null || m.Shape.Length < 3) continue;

                PolyBounds(m.Shape, out Vector2 lo, out Vector2 hi);
                lo = Vector2.Max(lo, min);
                hi = Vector2.Min(hi, max);
                if (hi.x - lo.x < 1f || hi.y - lo.y < 1f) continue;

                float shore = 0f;
                foreach (var p in m.Shape) shore += MapGenKit.SampleHeight(p.x, p.y);
                shore /= m.Shape.Length;

                var go = new GameObject($"WaterPlane_{m.RegionName}");
                go.transform.SetParent(parent.transform, false);

                var plane = go.AddComponent<TheWaningBorder.World.Terrain.WaterPlane>();
                plane.Initialize(lo, hi, shore);
                made++;
            }
            return made;
        }

        #endregion

        #region Geometry

        static bool InBounds(float x, float z, Vector2 min, Vector2 max) =>
            x >= min.x && x <= max.x && z >= min.y && z <= max.y;

        static void PolyBounds(Vector2[] poly, out Vector2 lo, out Vector2 hi)
        {
            lo = new Vector2(float.MaxValue, float.MaxValue);
            hi = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in poly)
            {
                lo = Vector2.Min(lo, p);
                hi = Vector2.Max(hi, p);
            }
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
                best = Mathf.Min(best, DistanceToSegment(x, z, poly[j], poly[i]));
            return best;
        }

        #endregion
    }
}
