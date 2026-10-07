// MapBuilderMirrorMarches.cs
// EDITOR-ONLY: generate "Mirror Marches" — the 768 m, 8-player FAIR map.
//   Waning Border > Maps > Build Mirror Marches (768m, 8 players)
//
// EIGHT SEATS 2026-10-07 (developer: "change the map so it supports 8
// players"). The map grew to a 12 x 12 lattice of the same 64 m squares
// (768 m), so a seat has about the ground a 4-player seat had on the 512 m
// map. Each quadrant holds TWO homes (2 x 2 squares), one on each outer edge,
// mirror images of each other across the quadrant's diagonal — neither may
// straddle a mirror axis, or TerritoryResources stops copying the quadrant's
// nodes and the seats stop opening on the same ground. Between a quadrant's
// two homes, in the map corner, is a contested 2 x 2 Iron rich block. Seats
// 1-4 are one per quadrant, so a 4-player match on it is still spread out.
// Everything below about the 512 m / 52-territory layout is history.
//
// HALVED 2026-10-07 (developer: "the match feels sluggish — reduce the map
// by half, keep the layout, just smaller territories"): the same 52
// territories in the same pattern at half the size — 64-cell homes, 32-cell
// squares. A home still holds the AI main camp (66 m across) with room to
// spare; its nodes line the home's edge (TerritoryResources.homeNodeMinOffset).
//
// THE DESIGN (2026-10-05): a test bed where the seat cannot decide the match.
//   * 256 x 256 build cells (512 m), mirrored across BOTH axes. Height,
//     territory shapes and territory types are mirror-symmetric by
//     construction, and TerritoryResources detects the symmetry and copies
//     one quadrant's nodes to the other three, so every seat opens on the
//     same ground.
//   * Territories are authored RECTANGLES (RegionSeedMarker.Shape), not
//     Voronoi cells. Along each edge: 64 | 32 32 32 32 | 64 cells. Each
//     player starts in a 64 x 64-cell corner territory; every other
//     territory is a 32 x 32-cell square — 4 homes + 48 squares.
//   * The curse holds the 4 central squares (Veilstone rich: they start
//     cursed). No territory is Empty. Per quadrant: Start, 2 Normal,
//     4 Normal + veilstone, 2 Normal + iron, 2 Iron rich, 1 Sanctum, and
//     the curse square.
//   * Open ground: no ridges, no forests, no field flora. Trees grow only on
//     the raised rim band, which is impassable anyway — a tree is an
//     impassable cell, and an unmirrored scatter would break the symmetry.
//
// Re-runnable: overwrites the terrain asset and scene in place.

using System.Collections.Generic;
using TheWaningBorder.World.MapMarkers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ResourceType = TheWaningBorder.World.MapMarkers.RegionSeedMarker.ResourceType;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapBuilderMirrorMarches
    {
        private const string MapName = "Mirror Marches";
        private const string SceneName = "MirrorMarches";
        private const string Folder = "Assets/GameData/Scenes/Maps/Mirror Marches";
        private const string TerrainMatPath = "Assets/Resources/TWBTerrain.mat";

        // ── dimensions ──────────────────────────────────────────────────────
        private const float MapMetres = 768f;
        private const float Half = MapMetres * 0.5f;
        private const float MaxHeight = 60f;
        private const int HeightRes = 1025;     // 0.75 m per texel; texel 512 sits on the axis
        private const int AlphaRes = 1024;      // 0.75 m per texel
        private const int DetailRes = 512;
        /// <summary>A home territory's side (a quarter of the map) and a
        /// square territory's side (an eighth).</summary>
        private const float Square = MapMetres / Lattice;
        private const float HomeSide = Square * 2f;
        /// <summary>Squares along a side, and half that (the mirror axis).</summary>
        private const int Lattice = 12;
        private const int HalfLattice = Lattice / 2;

        // Heights against PassabilityGrid/RegionMap thresholds (Water 4 m,
        // Mountain 24 m).
        private const float PlainY = 8f;
        private const float RimY = 40f;
        // The rim is kept thin: the edge squares are only 64 m deep, and the
        // main camp's wall ring stands 32 m from a home's outer edges.
        private const float RimMetres = 6f;
        private const float RimRamp = 10f;

        private static readonly Faction[] StartFactions =
        {
            Faction.Blue, Faction.Red, Faction.Green, Faction.Yellow,
            Faction.Purple, Faction.Orange, Faction.Teal, Faction.White,
        };

        // ── entry points ────────────────────────────────────────────────────

        [MenuItem("Waning Border/Maps/Build Mirror Marches (768m, 8 players)")]
        public static void Build()
        {
            if (!EditorUtility.DisplayDialog(MapName,
                    $"Generate {MapName}?\n\n" +
                    "  768 x 768 m, 8 players, mirrored on both axes\n" +
                    "  two edge homes per quadrant, 32-cell squares, curse in the centre 4\n\n" +
                    $"Overwrites {SceneName}.unity and its TerrainData.",
                    "Build", "Cancel"))
                return;
            BuildInternal();
        }

        /// <summary>Batch entry: build the map, then bake its lobby assets.</summary>
        public static void BuildAndBake()
        {
            BuildInternal();
            MapGenKit.BakeLobbyAssets(MapName);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }

        private static void BuildInternal()
        {
            MapAssetFolders.Ensure(Folder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // No gameplay scene ships a camera (the rig builds its own).
            foreach (var stray in Object.FindObjectsByType<Camera>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            var terrain = MapGenKit.BuildTerrain(new MapGenKit.TerrainSpec
            {
                MapFolder = Folder,
                SceneName = SceneName,
                Size = (int)MapMetres,
                HeightmapRes = HeightRes,
                AlphamapRes = AlphaRes,
                DetailRes = DetailRes,
                MaxHeight = MaxHeight,
                Height = HeightAt,
            });

            var mat = AssetDatabase.LoadAssetAtPath<Material>(TerrainMatPath);
            if (mat != null) terrain.materialTemplate = mat;
            else Debug.LogWarning($"[{MapName}] {TerrainMatPath} not found — overlays will not render.");

            MapGenKit.BuildLighting();
            PlaceMarkers();
            AssignLayers(terrain.terrainData);

            MapGenKit.ScatterFlora(terrain, new MapGenKit.FloraSpec
            {
                MapFolder = Folder,
                Size = (int)MapMetres,
                Seed = 0x3A1F,
                TreeCount = 900,
                TreeScale = 0.5f,
                GrassScale = 2.0f,
                DetailDensity = 2,
                CanPlant = CanPlant,
            });

            // The flora pass's NoWalk patches are stripped again (2026-10-06,
            // MapNoWalkCleaner): the developer wants this map free.
            MapNoWalkCleaner.Clear(terrain.terrainData);
            int bad = Validate();

            EditorSceneManager.MarkSceneDirty(scene);
            string scenePath = $"{Folder}/{SceneName}.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            bool registered = MapGenKit.RegisterInBuildSettings(scenePath);
            if (!MapRegistry.ShouldShip(scenePath))
                Debug.LogError($"[{MapName}] NOT IN THE SHIP GATE — add \"{SceneName}\" to " +
                               "MapRegistry.ShippingMapScenes or MapSceneSync strips it again.");
            if (bad > 0)
                Debug.LogError($"[{MapName}] {bad} validation problem(s) — see Console.");

            MapGenKit.ReportLobbyReadiness(MapName, MapName, Folder, scenePath, registered);
        }

        // ── terrain ─────────────────────────────────────────────────────────

        /// <summary>Ground height at world (wx, wz). A function of |x| and |z|
        /// only, so the ground is mirrored across both axes exactly.</summary>
        private static float HeightAt(float wx, float wz)
        {
            float ax = Mathf.Abs(wx), az = Mathf.Abs(wz);
            float y = Mathf.Max(PlainY + Noise(ax, az), 7.0f);
            float rim = RimMask(ax, az);
            if (rim > 0f) y = Mathf.Lerp(y, RimY, MapGenKit.SmoothStep(rim));
            return y;
        }

        private static float RimMask(float ax, float az)
        {
            float inner = Half - RimMetres - RimRamp;
            float d = Mathf.Max(ax - inner, az - inner, 0f);
            return Mathf.Clamp01(d / RimRamp);
        }

        /// <summary>Gentle relief, read in mirrored coordinates.</summary>
        private static float Noise(float ax, float az)
        {
            float nx = ax / MapMetres, nz = az / MapMetres;
            float a = Mathf.PerlinNoise(nx * 9f + 11.7f, nz * 9f + 3.1f) - 0.5f;
            float b = Mathf.PerlinNoise(nx * 29f + 5.2f, nz * 29f + 9.4f) - 0.5f;
            return a * 2.4f + b * 0.8f;
        }

        private static TerrainLayer LayerFrom(string sampleName, string niceName, float tile)
        {
            var src = AssetDatabase.LoadAssetAtPath<TerrainLayer>(
                $"Assets/MISC/TerrainSampleAssets/TerrainLayers/{sampleName}.terrainlayer");
            if (src == null)
            {
                Debug.LogWarning($"[{MapName}] sample layer {sampleName} missing.");
                return null;
            }
            string path = $"{Folder}/{niceName}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = src.diffuseTexture;
            layer.normalMapTexture = null;
            layer.maskMapTexture = null;
            layer.tileSize = new Vector2(tile, tile);
            layer.tileOffset = Vector2.zero;
            layer.metallic = 0f;
            layer.smoothness = 0f;
            layer.smoothnessSource = TerrainLayerSmoothnessSource.Constant;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        /// <summary>Two grasses on broad noise, dry patches and heather, rock on
        /// the rim — all read in mirrored coordinates, so the ground reads
        /// the same from every seat. NoWalk (the asset NAME is the contract
        /// PassabilityGrid looks for) is painted only by the flora pass.</summary>
        private static void AssignLayers(TerrainData data)
        {
            var grassA = LayerFrom("Grass_A_TerrainLayer", "GrassA", 13f);
            var grassB = LayerFrom("Grass_B_TerrainLayer", "GrassB", 11f);
            var dry = LayerFrom("Grass_Dry_TerrainLayer", "GrassDry", 12f);
            var heath = LayerFrom("Heather_TerrainLayer", "Heather", 9f);
            var rock = LayerFrom("Rock_TerrainLayer", "Rock", 22f);
            var noWalk = LayerFrom("Rock_TerrainLayer", "NoWalk", 27f);

            var list = new List<TerrainLayer>();
            foreach (var l in new[] { grassA, grassB, dry, heath, rock, noWalk })
                if (l != null) list.Add(l);
            if (list.Count == 0) { Debug.LogError($"[{MapName}] no terrain layers built."); return; }
            data.terrainLayers = list.ToArray();

            int iA = list.IndexOf(grassA), iB = list.IndexOf(grassB),
                iDry = list.IndexOf(dry), iHth = list.IndexOf(heath), iRck = list.IndexOf(rock);
            int lc = list.Count;
            var map = new float[AlphaRes, AlphaRes, lc];
            var w = new float[lc];

            for (int z = 0; z < AlphaRes; z++)
            {
                float wz = -Half + (z + 0.5f) * (MapMetres / AlphaRes);
                for (int x = 0; x < AlphaRes; x++)
                {
                    float wx = -Half + (x + 0.5f) * (MapMetres / AlphaRes);
                    float ax = Mathf.Abs(wx), az = Mathf.Abs(wz);
                    float h = HeightAt(wx, wz);
                    for (int l = 0; l < lc; l++) w[l] = 0f;

                    float nA = Mathf.PerlinNoise(ax * 0.011f + 31.7f, az * 0.011f + 11.2f);
                    float blend = nA * nA * (3f - 2f * nA);
                    if (iA >= 0) w[iA] = 1f - blend;
                    if (iB >= 0) w[iB] = blend;
                    float nD = Mathf.PerlinNoise(ax * 0.005f + 77f, az * 0.005f + 41f);
                    if (iDry >= 0 && nD > 0.56f) w[iDry] = Mathf.InverseLerp(0.56f, 0.78f, nD) * 0.9f;
                    float nH = Mathf.PerlinNoise(ax * 0.028f + 5f, az * 0.028f + 91f);
                    if (iHth >= 0 && nH > 0.7f) w[iHth] = Mathf.InverseLerp(0.7f, 0.92f, nH) * 0.55f;
                    if (iRck >= 0 && h > 16f) w[iRck] = Mathf.InverseLerp(16f, 24f, h) * 1.8f;

                    float sum = 0f;
                    for (int l = 0; l < lc; l++) sum += w[l];
                    if (sum <= 0f) { if (iA >= 0) w[iA] = sum = 1f; }
                    for (int l = 0; l < lc; l++) map[z, x, l] = w[l] / sum;
                }
            }
            data.SetAlphamaps(0, 0, map);
        }

        /// <summary>Trees only on the raised rim, which is impassable already.</summary>
        private static bool CanPlant(float wx, float wz)
            => RimMask(Mathf.Abs(wx), Mathf.Abs(wz)) > 0.6f;

        // ── markers ─────────────────────────────────────────────────────────

        /// <summary>Quadrant coordinate of lattice index g: 0 at the map
        /// edge, HalfLattice - 1 beside the mirror axis.</summary>
        private static int Q(int g) => g < HalfLattice ? g : Lattice - 1 - g;

        /// <summary>A home square: (qi, qj) with one coordinate 0-1 (the
        /// outer edge) and the other 3-4.</summary>
        private static bool IsHomeCell(int qi, int qj)
        {
            int a = Mathf.Min(qi, qj), b = Mathf.Max(qi, qj);
            return a <= 1 && (b == 3 || b == 4);
        }

        /// <summary>The contested corner block between a quadrant's two homes.</summary>
        private static bool IsCornerCell(int qi, int qj) => qi <= 1 && qj <= 1;

        /// <summary>
        /// The type of a single square at quadrant coordinate (qi, qj), 0 at
        /// the map edge and 5 beside the axis. Symmetric in (qi, qj), so a
        /// quadrant's two homes (mirror images across its diagonal) see the
        /// same ground.
        /// </summary>
        private static ResourceType TypeOf(int qi, int qj)
        {
            int a = Mathf.Min(qi, qj), b = Mathf.Max(qi, qj);
            if (a == 5 && b == 5) return ResourceType.VeilstoneRich;    // the curse: centre 4
            if (a == 3 && b == 3) return ResourceType.Sanctum;          // between the two homes' fronts
            if (a == 4 && b == 4) return ResourceType.IronRich;
            if (a <= 1 && b == 2) return a == 0 ? ResourceType.Normal : ResourceType.NormalVeilstone;
            if (a <= 1 && b == 5) return a == 0 ? ResourceType.NormalIron : ResourceType.Normal;
            if (a == 2 && b == 2) return ResourceType.Normal;
            if (a == 2 && (b == 3 || b == 4)) return ResourceType.NormalVeilstone;   // a home's front
            if (a == 2 && b == 5) return ResourceType.NormalIron;
            if (a == 3 && b == 4) return ResourceType.Normal;
            if (a == 3 && b == 5) return ResourceType.NormalVeilstone;
            return ResourceType.Normal;                                   // (4, 5)
        }

        private static void PlaceMarkers()
        {
            var startsRoot = new GameObject("PlayerStarts").transform;
            var regionRoot = new GameObject("Regions").transform;
            int idx = 0;

            // The eight homes. (sx, sz) is the quadrant; edgeX = the home on
            // the quadrant's west/east map edge, otherwise its south/north
            // edge. Seats 1-4 take one home per quadrant, turning round the
            // map, so a 4-player match is spread out too.
            var homes = new[]
            {
                (-1, -1, true,  "Southwest (west edge)"),  (1, -1, false, "Southeast (south edge)"),
                (1, 1, true,    "Northeast (east edge)"),   (-1, 1, false, "Northwest (north edge)"),
                (1, -1, true,   "Southeast (east edge)"),   (1, 1, false,  "Northeast (north edge)"),
                (-1, 1, true,   "Northwest (west edge)"),   (-1, -1, false, "Southwest (south edge)"),
            };
            for (int c = 0; c < homes.Length; c++)
            {
                var (sx, sz, edgeX, name) = homes[c];
                // Quadrant block origin: (0, 3) on the west edge, (3, 0) on the south edge.
                int qx = edgeX ? 0 : 3, qz = edgeX ? 3 : 0;
                int gx = sx < 0 ? qx : Lattice - 2 - qx;
                int gz = sz < 0 ? qz : Lattice - 2 - qz;
                var centre = new Vector2(-Half + (gx + 1) * Square, -Half + (gz + 1) * Square);
                var go = NewMarker($"P{c + 1} Start ({StartFactions[c]}) - {name}", centre, startsRoot);
                go.AddComponent<PlayerStartMarker>().Faction = StartFactions[c];
                NewSeed(regionRoot, ref idx, centre, $"{name} Home", Block(gx, gz, 2, 2),
                        ResourceType.Start, RegionSeedMarker.RegionKind.PlayerStart);
            }

            // The four contested corners, 2 x 2 squares each.
            foreach (var (sx, sz, name) in new[] { (-1, -1, "Southwest"), (1, -1, "Southeast"), (1, 1, "Northeast"), (-1, 1, "Northwest") })
            {
                int gx = sx < 0 ? 0 : Lattice - 2, gz = sz < 0 ? 0 : Lattice - 2;
                var centre = new Vector2(-Half + (gx + 1) * Square, -Half + (gz + 1) * Square);
                NewSeed(regionRoot, ref idx, centre, $"{name} Ironhold", Block(gx, gz, 2, 2),
                        ResourceType.IronRich, RegionSeedMarker.RegionKind.Normal);
            }

            // Every other square.
            for (int gz = 0; gz < Lattice; gz++)
                for (int gx = 0; gx < Lattice; gx++)
                {
                    int qi = Q(gx), qj = Q(gz);
                    if (IsHomeCell(qi, qj) || IsCornerCell(qi, qj)) continue;
                    float x0 = -Half + gx * Square, z0 = -Half + gz * Square;
                    var centre = new Vector2(x0 + Square * 0.5f, z0 + Square * 0.5f);
                    var type = TypeOf(qi, qj);
                    string label = type switch
                    {
                        ResourceType.VeilstoneRich => "Heart",
                        ResourceType.Sanctum => "Sanctum",
                        ResourceType.IronRich => "Ironfield",
                        ResourceType.NormalIron => "Iron March",
                        ResourceType.NormalVeilstone => "Veil March",
                        _ => "March",
                    };
                    NewSeed(regionRoot, ref idx, centre, $"{label} {(char)('A' + gx)}{gz + 1}",
                            Block(gx, gz, 1, 1), type, RegionSeedMarker.RegionKind.Normal);
                }
        }

        // ── natural borders (2026-10-07) ────────────────────────────────────
        // Developer: "randomize the boundaries of the territories a bit so it
        // looks more natural." The territories are still the 8 x 8 square
        // lattice (homes are 2 x 2 blocks of it), but every lattice corner is
        // nudged by up to CornerJitter and every lattice edge is cut into
        // EdgeSegments pieces that wobble by up to EdgeWobble. All of it is
        // generated in ONE quadrant's coordinates and mirrored across both
        // axes, so the map stays exactly fair; corners on a mirror axis slide
        // only along it, corners on the map's edge only along the edge, and
        // edges lying on an axis or the map edge stay straight. Neighbours
        // share each edge point for point, so the territories still tile.

        private const float CornerJitter = 10f;
        private const float EdgeWobble = 5f;
        private const int EdgeSegments = 4;
        private const uint BorderSeed = 0x5EEDu;

        /// <summary>A deterministic value in [-1, 1] for an integer key.</summary>
        private static float Hash(int a, int b, int c, int d)
        {
            uint h = BorderSeed;
            h = (h ^ (uint)a) * 0x9E3779B1u; h ^= h >> 15;
            h = (h ^ (uint)b) * 0x85EBCA77u; h ^= h >> 13;
            h = (h ^ (uint)c) * 0xC2B2AE3Du; h ^= h >> 16;
            h = (h ^ (uint)d) * 0x27D4EB2Fu; h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f;
        }

        /// <summary>Lattice corner (i, j), i/j in 0..Lattice, after its nudge.</summary>
        private static Vector2 Corner(int i, int j)
        {
            const int H = HalfLattice;
            int ci = Mathf.Abs(i - H), cj = Mathf.Abs(j - H);
            float sx = Mathf.Sign(i - H), sz = Mathf.Sign(j - H);
            float dx = (ci == 0 || ci == H) ? 0f : Hash(ci, cj, 1, 0) * CornerJitter;
            float dz = (cj == 0 || cj == H) ? 0f : Hash(ci, cj, 2, 0) * CornerJitter;
            return new Vector2(-Half + i * Square + sx * dx, -Half + j * Square + sz * dz);
        }

        /// <summary>The points of the lattice edge from (i0, j0) to (i1, j1)
        /// (one step apart), first corner included, last excluded.</summary>
        private static void EdgePoints(int i0, int j0, int i1, int j1, List<Vector2> into)
        {
            Vector2 a = Corner(i0, j0), bb = Corner(i1, j1);
            bool horizontal = j0 == j1;
            // Canonical (one-quadrant) key of the edge and its direction there.
            const int H = HalfLattice;
            int ca0 = Mathf.Abs(i0 - H), cb0 = Mathf.Abs(j0 - H), ca1 = Mathf.Abs(i1 - H), cb1 = Mathf.Abs(j1 - H);
            bool flip = horizontal ? ca1 < ca0 : cb1 < cb0;
            int ka = Mathf.Min(ca0, ca1), kb = Mathf.Min(cb0, cb1);
            // The wobble is perpendicular to the edge; none on an axis or the map edge.
            int fixedIdx = horizontal ? j0 : i0;
            int cFixed = Mathf.Abs(fixedIdx - H);
            bool straight = cFixed == 0 || cFixed == H;
            float sPerp = Mathf.Sign(fixedIdx - H);
            into.Add(a);
            for (int k = 1; k < EdgeSegments; k++)
            {
                float t = k / (float)EdgeSegments;
                var p = Vector2.Lerp(a, bb, t);
                if (!straight)
                {
                    int kc = flip ? EdgeSegments - k : k;
                    float n = Hash(ka, kb, horizontal ? 3 : 4, kc) * EdgeWobble * sPerp;
                    if (horizontal) p.y += n; else p.x += n;
                }
                into.Add(p);
            }
        }

        /// <summary>The outline of the block of lattice cells starting at
        /// (gx, gz), w x h cells, counter-clockwise from its SW corner.</summary>
        private static Vector2[] Block(int gx, int gz, int w, int h)
        {
            var pts = new List<Vector2>();
            for (int i = gx; i < gx + w; i++) EdgePoints(i, gz, i + 1, gz, pts);                 // south, west -> east
            for (int j = gz; j < gz + h; j++) EdgePoints(gx + w, j, gx + w, j + 1, pts);         // east, south -> north
            for (int i = gx + w; i > gx; i--) EdgePoints(i, gz + h, i - 1, gz + h, pts);         // north, east -> west
            for (int j = gz + h; j > gz; j--) EdgePoints(gx, j, gx, j - 1, pts);                 // west, north -> south
            return pts.ToArray();
        }

        private static float PolygonArea(Vector2[] r)
        {
            float a = 0f;
            for (int i = 0, j = r.Length - 1; i < r.Length; j = i++)
                a += r[j].x * r[i].y - r[i].x * r[j].y;
            return Mathf.Abs(a) * 0.5f;
        }

        /// <summary>A rectangle outline, counter-clockwise from its SW corner.</summary>
        private static Vector2[] Rect(float x0, float z0, float w, float h)
            => new[] { new Vector2(x0, z0), new Vector2(x0 + w, z0),
                       new Vector2(x0 + w, z0 + h), new Vector2(x0, z0 + h) };

        private static void NewSeed(Transform parent, ref int index, Vector2 p, string name,
            Vector2[] shape, ResourceType type, RegionSeedMarker.RegionKind kind)
        {
            var go = NewMarker($"Region {index:00} - {name}", p, parent);
            var seed = go.AddComponent<RegionSeedMarker>();
            seed.RegionName = name;
            seed.Shape = shape;
            seed.Resources = type;
            seed.Kind = kind;
            index++;
        }

        private static GameObject NewMarker(string name, Vector2 p, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(p.x, HeightAt(p.x, p.y), p.y);
            return go;
        }

        // ── validation ──────────────────────────────────────────────────────

        /// <summary>The layout rules, checked rather than assumed: 52
        /// territories that tile the map exactly, every type mirrored across
        /// both axes, no Empty, the curse on exactly the centre four, and
        /// every start on standable ground.</summary>
        private static int Validate()
        {
            int bad = 0;
            var seeds = Object.FindObjectsByType<RegionSeedMarker>(FindObjectsSortMode.None);
            // 8 homes + 4 corner blocks + 96 single squares.
            const int Want = 108;
            if (seeds.Length != Want)
            { Debug.LogError($"[{MapName}] {seeds.Length} territories, want {Want}."); bad++; }

            float area = 0f;
            var byCentre = new Dictionary<Vector2Int, ResourceType>();
            int curse = 0;
            foreach (var s in seeds)
            {
                var r = s.Shape;
                area += PolygonArea(r);
                // The seed point (the cell's nominal centre) is exact and
                // mirrored; the outline is jittered.
                var c = new Vector2Int(Mathf.RoundToInt(s.transform.position.x),
                                       Mathf.RoundToInt(s.transform.position.z));
                byCentre[c] = s.Resources;
                if (s.Resources == ResourceType.Empty || s.Resources == ResourceType.Auto)
                { Debug.LogError($"[{MapName}] {s.name} is {s.Resources}."); bad++; }
                if (s.Resources == ResourceType.VeilstoneRich)
                {
                    curse++;
                    float hs = Square * 0.5f;
                    if (c.x * c.x + c.y * c.y > 2 * hs * hs + 1)
                    { Debug.LogError($"[{MapName}] curse territory {s.name} is not central."); bad++; }
                }
            }
            if (Mathf.Abs(area - MapMetres * MapMetres) > 1f)
            { Debug.LogError($"[{MapName}] territories cover {area:0} m², map is {MapMetres * MapMetres:0}."); bad++; }
            if (curse != 4)
            { Debug.LogError($"[{MapName}] {curse} curse territories, want 4."); bad++; }
            foreach (var kv in byCentre)
            {
                var c = kv.Key;
                foreach (var m in new[] { new Vector2Int(-c.x, c.y), new Vector2Int(c.x, -c.y), new Vector2Int(-c.x, -c.y) })
                    if (!byCentre.TryGetValue(m, out var t) || t != kv.Value)
                    { Debug.LogError($"[{MapName}] territory at {c} has no {kv.Value} mirror at {m}."); bad++; }
            }
            foreach (var st in Object.FindObjectsByType<PlayerStartMarker>(FindObjectsSortMode.None))
            {
                float y = HeightAt(st.transform.position.x, st.transform.position.z);
                if (y <= 4f + 2.85f || y >= 24f - 2.85f)
                { Debug.LogError($"[{MapName}] {st.name} on unstandable ground (y={y:0.0})."); bad++; }
            }
            int starts = Object.FindObjectsByType<PlayerStartMarker>(FindObjectsSortMode.None).Length;
            if (starts != 8)
            { Debug.LogError($"[{MapName}] {starts} player starts, want 8."); bad++; }
            if (bad == 0) Debug.Log($"[{MapName}] layout validated: {Want} territories, 8 starts, mirrored, curse centre 4.");
            return bad;
        }
    }
}
