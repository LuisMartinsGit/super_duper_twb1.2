// MapBuilderVeilmarch.cs
// EDITOR-ONLY: generate "Veilmarch" — the 1024 m, 4-player open-field map.
//   Waning Border > Maps > Build Veilmarch (1024m, 4 players)
//
// THE DESIGN (2026-08-29):
//   * 1024 x 1024 m — 4.0x Sundered Crown's area. Open plains, not a maze.
//   * HOME territories are LARGE, everything else is SMALL: three small
//     territories are worth one home, expressed through Voronoi seed spacing
//     (home seeds get ~1.7x the clearance of field seeds; area goes with the
//     square). The build VERIFIES the ratio numerically instead of trusting
//     the constants — see ValidateRegionAreas.
//   * VEILSTEEL EXISTS ONLY IN THE CENTRE RING — four small "Veilfield"
//     territories around the middle; late tech means marching to the centre
//     and holding it. VEILSTONE IS MAP-WIDE (Regions.md §3, 2026-08-31):
//     every home authors an outcropping, the Veilfields keep theirs, and
//     the runtime coverage pass tops the map up to 50% of all territories —
//     veilstone is the army economy AND the ground the curse can conquer.
//   * THE DEAD CENTRE IS CURSE ONLY — one territory ("The Scar") holding THE
//     SINGLE PURE NODE (the verb objective + Shardroot host) at its centre.
//     Well domination is a fight over one place, and the curse's territorial
//     expansion radiates from here.
//   * CHOKEPOINTS on an open map: a broken ridge ring at r=280 pierced by one
//     gate per home (on the home's axis) and one per corner, with forest
//     stands narrowing the home gates. Everything outside the ring is open
//     field.
//   * FULLY DECORATED: MapGenKit flora — trees/rocks/bushes as terrain tree
//     instances (each occupying one impassable build cell), grass as waving
//     terrain detail, plus dense forest stands inside the NatureRegion discs
//     and a thick tree band along the Nature ring.
//
// The layout is POLAR with 4-fold symmetry: every player's opening is
// identical by construction. Homes sit on the CARDINAL axes; the rich centre
// ring and the corner fields sit on the diagonals.
//
// Re-runnable: overwrites the terrain asset and scene in place.

using System.Collections.Generic;
using TheWaningBorder.World.MapMarkers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapBuilderVeilmarch
    {
        private const string MapName = "Veilmarch";
        private const string SceneName = "Veilmarch";
        private const string Folder = "Assets/GameData/Scenes/Maps/Veilmarch";
        private const string LayerFolder = "Assets/GameData/Scenes/Maps/Twin Spans";
        private const string TerrainMatPath = "Assets/Resources/TWBTerrain.mat";

        // ── dimensions ──────────────────────────────────────────────────────
        private const float MapMetres = 1024f;
        private const float MaxHeight = 60f;
        private const int HeightRes = 1025;     // 1 m per texel
        // 1 m per texel — REQUIRED at this size: NoWalk under trees is painted
        // per 2 m build cell, and an alphamap texel larger than the cell would
        // bleed the block onto walkable neighbours.
        private const int AlphaRes = 1024;
        // 1024 -> 512 (2026-08-31 perf pass): quarter the waving-grass
        // patches. Detail grass is decoration; passability and claims never
        // read it.
        private const int DetailRes = 512;

        // Heights against PassabilityGrid/RegionMap thresholds (Water 4 m,
        // Mountain 24 m): above 24 or below 4 is impassable AND unclaimable.
        private const float PlainY = 8f;
        private const float RidgeY = 34f;
        private const float NatureY = 40f;
        private const float NatureRingMetres = 40f;

        // ── polar layout (metres from map centre; 4 sectors) ────────────────
        private const int Sectors = 4;

        private const float ScarWellR = 55f;      // wells, inside The Scar
        private const float CentreRingR = 210f;   // Veilfield seeds (diagonals)
        private const float HomeR = 385f;         // home seeds + starts (cardinals)
        private const float CornerR = 440f;       // corner field seeds (diagonals)
        private const float DiagMidR = 315f;      // field seeds splitting ring->corner
        private const float FarCornerXY = 425f;   // far-corner seeds, (x, x) diagonal
        private static readonly Vector2 RimFlank = new Vector2(455f, 295f);

        // The ridge ring and its gates.
        private const float RidgeRingR = 280f;
        private const float RidgeBlobSize = 55f;
        private const float RidgeBlobOffDeg = 25f;   // blobs at cardinal +/-25 deg

        // Forest stands: gate-narrowing pairs, one wood per home, one per corner.
        private const float GateForestR = 300f;
        private const float GateForestOffDeg = 13f;
        private const float GateForestSize = 26f;
        private const float HomeForestSize = 28f;
        private const float CornerForestSize = 30f;

        private static readonly Faction[] StartFactions =
            { Faction.Blue, Faction.Red, Faction.Green, Faction.Yellow };

        /// <summary>Sector names, counter-clockwise from due east (cardinals).</summary>
        private static readonly string[] SectorNames =
            { "Eastmarch", "Northmarch", "Westmarch", "Southmarch" };

        /// <summary>Diagonal names, counter-clockwise from north-east.</summary>
        private static readonly string[] DiagNames =
            { "Northeast", "Northwest", "Southwest", "Southeast" };

        /// <summary>Ground the flora pass must keep clear: (world pos, radius).</summary>
        private static readonly List<(Vector2 pos, float r)> _keepClear = new();

        /// <summary>Forest stand discs, for the dense-fill pass: (pos, radius).</summary>
        private static readonly List<(Vector2 pos, float r)> _forests = new();

        // ── entry points ────────────────────────────────────────────────────

        [MenuItem("Waning Border/Maps/Build Veilmarch (1024m, 4 players)")]
        public static void Build()
        {
            // DisplayDialog auto-CANCELS under -batchmode (it does not
            // confirm), so the dialog lives only on this menu path and
            // BuildAndBake calls BuildInternal directly.
            if (!EditorUtility.DisplayDialog(MapName,
                    $"Generate {MapName}?\n\n" +
                    $"  {MapMetres:0} x {MapMetres:0} m, {Sectors} players\n" +
                    "  Large homes, small fields (3 small = 1 large)\n" +
                    "  Veilstone/veilsteel centre-only; curse-only Scar\n\n" +
                    $"Overwrites {SceneName}.unity and its TerrainData.",
                    "Build", "Cancel"))
                return;
            BuildInternal();
        }

        private static void BuildInternal()
        {
            _keepClear.Clear();
            _forests.Clear();

            MapAssetFolders.Ensure(Folder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                                                    NewSceneMode.Single);

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
            else Debug.LogWarning($"[{MapName}] {TerrainMatPath} not found — the TWB culture / " +
                                  "blood / curse / region overlays will not render.");

            MapGenKit.BuildLighting();

            PlaceMarkers();

            // AFTER the markers: the organic paint puts a distinct floor
            // under every forest disc, and _forests is only filled by
            // PlaceMarkers. Still before ScatterFlora, whose NoWalk paint
            // must land on the finished alphamap.
            AssignLayers(terrain.terrainData);

            // Flora AFTER markers so the keep-clear list is complete, and
            // AFTER AssignLayers so the NoWalk paint under trees lands on the
            // finished alphamap rather than being overwritten by it.
            MapGenKit.ScatterFlora(terrain, new MapGenKit.FloraSpec
            {
                MapFolder = Folder,
                Size = (int)MapMetres,
                Seed = 0x7E11,
                // 7000 -> 2400 attempts and grass density halved
                // (2026-08-31 perf pass): tree INSTANCES are the map's
                // render weight — the field pass plus the stand fill put
                // 8-10k on the board and Veilmarch played "extremely laggy".
                // Impassability comes from the NatureRegion markers, not
                // from tree instances, so thinner woods change nothing but
                // the frame time.
                TreeCount = 2400,          // attempts; acceptance shapes density
                TreeScale = 0.5f,
                GrassScale = 2.0f,
                DetailDensity = 2,
                CanPlant = CanPlant,
            });
            FillForestStands(terrain.terrainData);

            int bad = ValidatePlacements();
            bad += ValidateRegionAreas();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, $"{Folder}/{SceneName}.unity");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string scenePath = $"{Folder}/{SceneName}.unity";
            bool registered = MapGenKit.RegisterInBuildSettings(scenePath);
            if (!MapRegistry.ShouldShip(scenePath))
                Debug.LogError($"[{MapName}] NOT IN THE SHIP GATE — add \"{SceneName}\" to " +
                               "MapRegistry.ShippingMapScenes or MapSceneSync strips it again.");
            if (bad > 0)
                Debug.LogError($"[{MapName}] {bad} validation problem(s) — see Console.");

            MapGenKit.ReportLobbyReadiness(MapName, MapName, Folder, scenePath, registered);
        }

        /// <summary>Batch entry: build the map, then bake its lobby assets.
        /// Dialogs auto-confirm under -batchmode.</summary>
        public static void BuildAndBake()
        {
            BuildInternal();
            MapGenKit.BakeLobbyAssets(MapName);
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }

        // ── polar helpers (world metres, centre origin) ─────────────────────

        private static float CardinalAngle(int sector) => sector * Mathf.PI * 2f / Sectors;
        private static float DiagAngle(int i) => (i + 0.5f) * Mathf.PI * 2f / Sectors;

        private static Vector2 Polar(float radius, float angle)
            => new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);

        private static Vector2 Rotate(Vector2 p, float angle)
            => new Vector2(p.x * Mathf.Cos(angle) - p.y * Mathf.Sin(angle),
                           p.x * Mathf.Sin(angle) + p.y * Mathf.Cos(angle));

        // ── terrain ─────────────────────────────────────────────────────────

        /// <summary>Ground height in world metres at world (wx, wz).</summary>
        private static float HeightAt(float wx, float wz)
        {
            // The floor keeps noise dips clear of the validator's water margin
            // (4 m water + 2.85 m slack): a plain that wobbles down to 6.8 m is
            // walkable but reads as "nearly water" to every placement check.
            float y = Mathf.Max(PlainY + Noise(wx, wz), 7.0f);

            // Nature ring — the map ends in a raised wooded band, not a cut.
            float ring = NatureRingMask(wx, wz);
            if (ring > 0f) y = Mathf.Lerp(y, NatureY, MapGenKit.SmoothStep(ring));

            // The broken ridge ring: two blobs per sector at cardinal +/-25
            // degrees. The gaps ON the cardinals are each home's gate to the
            // centre; the gaps on the diagonals are the corner routes.
            float up = 0f;
            for (int i = 0; i < Sectors; i++)
            {
                float a = CardinalAngle(i);
                float off = RidgeBlobOffDeg * Mathf.Deg2Rad;
                up = Mathf.Max(up, Blob(wx, wz, Polar(RidgeRingR, a - off), RidgeBlobSize));
                up = Mathf.Max(up, Blob(wx, wz, Polar(RidgeRingR, a + off), RidgeBlobSize));
            }
            if (up > 0f) y = Mathf.Lerp(y, RidgeY, MapGenKit.SmoothStep(up));

            return y;
        }

        private static float NatureRingMask(float wx, float wz)
        {
            float half = MapMetres * 0.5f;
            float dx = Mathf.Max(wx - (half - NatureRingMetres), -half - wx + NatureRingMetres, 0f);
            float dz = Mathf.Max(wz - (half - NatureRingMetres), -half - wz + NatureRingMetres, 0f);
            return Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz) / 60f);
        }

        private static float Blob(float wx, float wz, Vector2 centre, float radius)
        {
            float d = MapGenKit.Dist(wx, wz, centre.x, centre.y);
            return Mathf.Clamp01((radius - d) / (radius * 0.35f));
        }

        private static float Noise(float wx, float wz)
        {
            float nx = wx / MapMetres + 0.5f, nz = wz / MapMetres + 0.5f;
            float a = Mathf.PerlinNoise(nx * 9f + 11.7f, nz * 9f + 3.1f) - 0.5f;
            float b = Mathf.PerlinNoise(nx * 29f + 5.2f, nz * 29f + 9.4f) - 0.5f;
            return a * 4.2f + b * 1.2f;
        }

        /// <summary>
        /// A matte copy of a terrain-sample layer, authored into the map
        /// folder. The shipped layers read smoothness from the OPAQUE ALPHA
        /// of their albedo — smoothness ~1.0, the plastic shine (2026-08-31
        /// directive #2). Constant smoothness zero kills it at the data
        /// level, whatever material renders the terrain.
        /// </summary>
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
            // NO normal maps (2026-08-31 GPU pass): 8 layers x (albedo +
            // normal) doubled the terrain's per-pixel samples, and at RTS
            // camera height per-texel ground normals are invisible — the
            // heightmap normal carries the lighting. Halves terrain
            // fragment cost across most of the screen.
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

        /// <summary>
        /// ORGANIC GROUND (2026-08-31 directives #2-#4). Eight matte layers
        /// blended by layered noise instead of one hard layer per texel:
        /// two grasses trading by broad noise, dry worn patches, heather
        /// sprinkles, a soil shoreline, rock over the ridge band — and a
        /// DISTINCT FOREST FLOOR painted under every stand disc, so forests
        /// read as their own ground exactly where the marker makes them
        /// impassable.
        /// </summary>
        private static void AssignLayers(TerrainData data)
        {
            var grassA = LayerFrom("Grass_A_TerrainLayer", "GrassA", 13f);
            var grassB = LayerFrom("Grass_B_TerrainLayer", "GrassB", 11f);
            var dry = LayerFrom("Grass_Dry_TerrainLayer", "GrassDry", 12f);
            var heath = LayerFrom("Heather_TerrainLayer", "Heather", 9f);
            var floor = LayerFrom("Muddy_TerrainLayer", "ForestFloor", 11f);
            var shore = LayerFrom("Soil_Rocks_TerrainLayer", "Shore", 12f);
            var rock = LayerFrom("Rock_TerrainLayer", "Rock", 22f);
            // THE BLOCKING LAYER: the asset NAME is the contract —
            // PassabilityGrid.LoadNoWalkMask looks for "nowalk".
            var noWalk = LayerFrom("Rock_TerrainLayer", "NoWalk", 27f);

            var list = new List<TerrainLayer>();
            foreach (var l in new[] { grassA, grassB, dry, heath, floor, shore, rock, noWalk })
                if (l != null) list.Add(l);
            if (list.Count < 8)
            {
                Debug.LogError($"[{MapName}] only {list.Count}/8 layers built — " +
                               "check Assets/MISC/TerrainSampleAssets/TerrainLayers.");
                if (list.Count == 0) return;
            }
            data.terrainLayers = list.ToArray();

            int iA = list.IndexOf(grassA), iB = list.IndexOf(grassB),
                iDry = list.IndexOf(dry), iHth = list.IndexOf(heath),
                iFlr = list.IndexOf(floor), iSho = list.IndexOf(shore),
                iRck = list.IndexOf(rock);

            int lc = list.Count;
            var map = new float[AlphaRes, AlphaRes, lc];
            var w = new float[lc];
            float half = MapMetres * 0.5f;

            for (int z = 0; z < AlphaRes; z++)
            {
                float wz = -half + (z / (float)(AlphaRes - 1)) * MapMetres;
                for (int x = 0; x < AlphaRes; x++)
                {
                    float wx = -half + (x / (float)(AlphaRes - 1)) * MapMetres;
                    float h = HeightAt(wx, wz);
                    for (int l = 0; l < lc; l++) w[l] = 0f;

                    // Base: two grasses trading on broad noise.
                    float nA = Mathf.PerlinNoise(wx * 0.011f + 31.7f, wz * 0.011f + 11.2f);
                    float blend = nA * nA * (3f - 2f * nA);
                    if (iA >= 0) w[iA] = 1f - blend;
                    if (iB >= 0) w[iB] = blend;

                    // Dry, worn patches on a wider wavelength.
                    float nD = Mathf.PerlinNoise(wx * 0.005f + 77f, wz * 0.005f + 41f);
                    if (iDry >= 0 && nD > 0.56f)
                        w[iDry] = Mathf.InverseLerp(0.56f, 0.78f, nD) * 0.9f;

                    // Heather sprinkles, tight noise, sparse.
                    float nH = Mathf.PerlinNoise(wx * 0.028f + 5f, wz * 0.028f + 91f);
                    if (iHth >= 0 && nH > 0.7f)
                        w[iHth] = Mathf.InverseLerp(0.7f, 0.92f, nH) * 0.55f;

                    // Shoreline soil where the ground dips toward water.
                    if (iSho >= 0 && h < 6f)
                        w[iSho] = Mathf.InverseLerp(6f, 3.5f, h) * 1.4f;

                    // Rock over the ridge/ring band.
                    if (iRck >= 0 && h > 19f)
                        w[iRck] = Mathf.InverseLerp(19f, 26f, h) * 1.8f;

                    // FOREST FLOOR: dominant inside every stand disc, fading
                    // over a 5 m fringe — the woods own their ground.
                    if (iFlr >= 0)
                        foreach (var (c, r) in _forests)
                        {
                            float dx = wx - c.x, dz = wz - c.y;
                            float d = Mathf.Sqrt(dx * dx + dz * dz);
                            if (d > r + 3f) continue;
                            float tIn = d <= r - 2f ? 1f
                                : Mathf.InverseLerp(r + 3f, r - 2f, d);
                            w[iFlr] = Mathf.Max(w[iFlr], tIn * 2.4f);
                        }

                    float sum = 0f;
                    for (int l = 0; l < lc; l++) sum += w[l];
                    if (sum <= 0f) { if (iA >= 0) w[iA] = sum = 1f; }
                    for (int l = 0; l < lc; l++) map[z, x, l] = w[l] / sum;
                }
            }
            data.SetAlphamaps(0, 0, map);
        }

        // ── markers ─────────────────────────────────────────────────────────

        private static void PlaceMarkers()
        {
            // Player starts on the cardinal axes.
            var startsRoot = new GameObject("PlayerStarts").transform;
            for (int i = 0; i < Sectors; i++)
            {
                var p = Polar(HomeR, CardinalAngle(i));
                var go = NewMarker($"P{i + 1} Start ({StartFactions[i]}) - {SectorNames[i]}",
                                   p, startsRoot);
                go.AddComponent<PlayerStartMarker>().Faction = StartFactions[i];
                _keepClear.Add((p, 34f));
            }

            // ── region seeds. Creation order IS the region id (the registry
            // sorts by GameObject name, and the "Region NN" prefix pins it).
            //
            // The size rule is spacing: home seeds keep ~260-330 m of clearance
            // while field seeds sit ~150-230 m apart, and Voronoi area goes
            // with the square — that is the "3 small = 1 large" of the design.
            var regionRoot = new GameObject("Regions").transform;
            int idx = 0;

            // 00 — The Scar: curse only. Nothing else is placed here.
            NewSeed(regionRoot, ref idx, Vector2.zero, "The Scar");

            // 01-04 — the Veilfields: the ONLY veilstone/veilsteel on the map.
            for (int i = 0; i < Sectors; i++)
                NewSeed(regionRoot, ref idx, Polar(CentreRingR, DiagAngle(i)),
                        $"{DiagNames[i]} Veilfield");

            // 05-08 — the home territories (large).
            for (int i = 0; i < Sectors; i++)
                NewSeed(regionRoot, ref idx, Polar(HomeR, CardinalAngle(i)),
                        $"{SectorNames[i]} Home");

            // 09-12 — corner fields (small).
            for (int i = 0; i < Sectors; i++)
                NewSeed(regionRoot, ref idx, Polar(CornerR, DiagAngle(i)),
                        $"{DiagNames[i]} Corner");

            // 13-20 — rim flanks (small), two hugging each home along the map
            // edge so the home region cannot balloon down the rim.
            for (int i = 0; i < Sectors; i++)
            {
                float a = CardinalAngle(i);
                NewSeed(regionRoot, ref idx,
                        Rotate(new Vector2(RimFlank.x, RimFlank.y), a), $"{SectorNames[i]} Rim North");
                NewSeed(regionRoot, ref idx,
                        Rotate(new Vector2(RimFlank.x, -RimFlank.y), a), $"{SectorNames[i]} Rim South");
            }

            // 21-24 — diagonal fields between the Veilfields and the corners.
            // 25-28 — far corners, the map-corner wedges.
            //
            // These exist to SHRINK the smalls: without them the first area
            // audit measured home/small at 1.45 because the Veilfield and
            // corner cells sprawled — a fat "small" region is just a large
            // region with a small name. Splitting them is what makes the
            // 3-small-=-1-large rule true on the ground.
            for (int i = 0; i < Sectors; i++)
                NewSeed(regionRoot, ref idx, Polar(DiagMidR, DiagAngle(i)),
                        $"{DiagNames[i]} Field");
            for (int i = 0; i < Sectors; i++)
                NewSeed(regionRoot, ref idx,
                        Rotate(new Vector2(FarCornerXY, FarCornerXY), CardinalAngle(i)),
                        $"{DiagNames[i]} Far Corner");

            // The partition, for seat-inside-region placement and validation.
            // SORT BY NAME before Configure. FindObjectsByType returns
            // arbitrary order, but region ids at runtime come from
            // MapMarkerRegistry's name-ordinal sort — the "Region NN" prefix
            // pins creation order. Feeding Configure the unsorted list made
            // every id in validation a scramble: the first build reported
            // wells "in region 18" that were sitting dead centre.
            var seeds = new List<RegionSeedMarker>(
                Object.FindObjectsByType<RegionSeedMarker>(FindObjectsSortMode.None));
            seeds.Sort((x, y) => string.CompareOrdinal(x.gameObject.name, y.gameObject.name));
            var seedWorld = new List<Vector2>();
            var seedNames = new List<string>();
            foreach (var seed in seeds)
            {
                var wp = seed.transform.position;
                seedWorld.Add(new Vector2(wp.x, wp.z));
                seedNames.Add(seed.RegionName);
            }
            TheWaningBorder.World.Regions.RegionMap.Configure(seedWorld, seedNames);

            // ── the PURE NODE: ONE well at the map's dead centre
            // (Regions.md §3, 2026-08-31 — "a single pure node in the centre
            // territory"). AuthoredPosition makes it the map's COMPLETE well
            // list. It is the verb-victory objective and the Shardroot host,
            // and the territory holding it is the seed the curse expands
            // from; the old four-well ring is superseded.
            var wellRoot = new GameObject("Wells").transform;
            {
                var p = Vector2.zero;
                var go = NewMarker("Node 00", p, wellRoot);
                go.AddComponent<BorderNodeMarker>().AuthoredPosition = true;
                _keepClear.Add((p, 26f));
            }

            // ── forests. Gate stands narrow each home's cardinal lane; one
            // wood per home and per corner feeds the Sawyer and breaks up the
            // open field. All impassable via NatureRegionBootstrap.
            var natureRoot = new GameObject("NatureRegions").transform;
            for (int i = 0; i < Sectors; i++)
            {
                float a = CardinalAngle(i);
                float off = GateForestOffDeg * Mathf.Deg2Rad;
                Forest(natureRoot, $"Gate Wood {SectorNames[i]} N",
                       Polar(GateForestR, a + off), GateForestSize);
                Forest(natureRoot, $"Gate Wood {SectorNames[i]} S",
                       Polar(GateForestR, a - off), GateForestSize);
                Forest(natureRoot, $"Home Wood {SectorNames[i]}",
                       Polar(398f, a + 16f * Mathf.Deg2Rad), HomeForestSize);
                Forest(natureRoot, $"Corner Wood {DiagNames[i]}",
                       Polar(502f, DiagAngle(i)), CornerForestSize);
            }

            // ── resources ──
            var resRoot = new GameObject("Resources").transform;

            for (int i = 0; i < Sectors; i++)
            {
                float a = CardinalAngle(i);
                var hp = Polar(HomeR, a);
                int home = TheWaningBorder.World.Regions.RegionMap.RegionAt(hp.x, hp.y);

                // Home: iron, supply AND veilstone (Regions.md §3, 2026-08-31:
                // every starter territory MUST carry a veilstone outcropping —
                // the old no-home-veilstone authoring is superseded). FOUR
                // supply nodes — the home half of the node-quota rule
                // (Regions.md §4): a start territory supports twice the huts
                // and twice the base supply tick of the small fields around it.
                Iron(resRoot, $"Iron {SectorNames[i]} a", SeatIn(home, 340f, a - 0.10f), 30);
                Iron(resRoot, $"Iron {SectorNames[i]} b", SeatIn(home, 340f, a + 0.10f), 30);
                Veilstone(resRoot, $"Veilstone {SectorNames[i]} Home", SeatIn(home, 388f, a - 0.10f));
                Supply(resRoot, $"Supply {SectorNames[i]} a", SeatIn(home, 352f, a - 0.15f));
                Supply(resRoot, $"Supply {SectorNames[i]} b", SeatIn(home, 396f, a + 0.05f));
                Supply(resRoot, $"Supply {SectorNames[i]} c", SeatIn(home, 416f, a - 0.05f));
                Supply(resRoot, $"Supply {SectorNames[i]} d", SeatIn(home, 372f, a + 0.15f));

                // Veilfield: the map's only veilstone and veilsteel.
                float d = DiagAngle(i);
                var vp = Polar(CentreRingR, d);
                int field = TheWaningBorder.World.Regions.RegionMap.RegionAt(vp.x, vp.y);
                Veilstone(resRoot, $"Veilstone {DiagNames[i]} a", SeatIn(field, 195f, d - 0.16f));
                Veilstone(resRoot, $"Veilstone {DiagNames[i]} b", SeatIn(field, 195f, d + 0.16f));
                Veilsteel(resRoot, $"Veilsteel {DiagNames[i]}", SeatIn(field, 228f, d));
                Supply(resRoot, $"Supply {DiagNames[i]} Veilfield a", SeatIn(field, 232f, d - 0.24f));
                Supply(resRoot, $"Supply {DiagNames[i]} Veilfield b", SeatIn(field, 205f, d + 0.26f));

                // Corner field: iron + supply.
                var cp = Polar(CornerR, d);
                int corner = TheWaningBorder.World.Regions.RegionMap.RegionAt(cp.x, cp.y);
                Iron(resRoot, $"Iron {DiagNames[i]} Corner", SeatIn(corner, 452f, d), 24);
                Supply(resRoot, $"Supply {DiagNames[i]} Corner a", SeatIn(corner, 430f, d - 0.12f));
                Supply(resRoot, $"Supply {DiagNames[i]} Corner b", SeatIn(corner, 462f, d + 0.10f));

                // Diagonal field + far corner: iron + supply each.
                var mp = Polar(DiagMidR, d);
                int mid = TheWaningBorder.World.Regions.RegionMap.RegionAt(mp.x, mp.y);
                Iron(resRoot, $"Iron {DiagNames[i]} Field", SeatIn(mid, DiagMidR + 14f, d), 24);
                Supply(resRoot, $"Supply {DiagNames[i]} Field a", SeatIn(mid, DiagMidR - 12f, d - 0.08f));
                Supply(resRoot, $"Supply {DiagNames[i]} Field b", SeatIn(mid, DiagMidR + 4f, d + 0.09f));

                var fc = Rotate(new Vector2(FarCornerXY, FarCornerXY), a);
                int far = TheWaningBorder.World.Regions.RegionMap.RegionAt(fc.x, fc.y);
                float fa = Mathf.Atan2(fc.y, fc.x);
                float fr = fc.magnitude;
                Iron(resRoot, $"Iron {DiagNames[i]} Far", SeatIn(far, fr - 16f, fa), 24);
                Supply(resRoot, $"Supply {DiagNames[i]} Far a", SeatIn(far, fr - 30f, fa + 0.04f));
                Supply(resRoot, $"Supply {DiagNames[i]} Far b", SeatIn(far, fr + 8f, fa - 0.03f));

                // Rim flanks: iron + supply each.
                for (int s = 0; s < 2; s++)
                {
                    var rf = Rotate(new Vector2(RimFlank.x, s == 0 ? RimFlank.y : -RimFlank.y), a);
                    int rim = TheWaningBorder.World.Regions.RegionMap.RegionAt(rf.x, rf.y);
                    string tag = s == 0 ? "N" : "S";
                    float ra = Mathf.Atan2(rf.y, rf.x);
                    float rr = rf.magnitude;
                    Iron(resRoot, $"Iron {SectorNames[i]} Rim {tag}", SeatIn(rim, rr - 18f, ra), 24);
                    Supply(resRoot, $"Supply {SectorNames[i]} Rim {tag} a", SeatIn(rim, rr + 12f, ra + 0.05f));
                    Supply(resRoot, $"Supply {SectorNames[i]} Rim {tag} b", SeatIn(rim, rr - 34f, ra - 0.06f));
                }
            }

            // The Scar is curse ground, but it is still a TERRITORY, and the
            // node-quota rule (Regions.md §4) exempts nobody: whoever clears
            // and claims it gets a working region — 2 supply nodes and an
            // iron node, ringed at ScarWellR around the central pure node.
            Iron(resRoot, "Iron The Scar", SeatIn(0, ScarWellR, 0f), 24);
            Supply(resRoot, "Supply The Scar a", SeatIn(0, ScarWellR, Mathf.PI * 0.5f));
            Supply(resRoot, "Supply The Scar b", SeatIn(0, ScarWellR, Mathf.PI));
        }

        /// <summary>
        /// A point at roughly (radius, angle) GUARANTEED inside region
        /// <paramref name="region"/> on standable ground — region boundaries
        /// are domain-warped, so polar arithmetic alone can land next door.
        /// Walks toward the region's seed until the partition agrees.
        /// </summary>
        private static Vector2 SeatIn(int region, float radius, float angle)
        {
            var ideal = Polar(radius, angle);
            if (region < 0) return ideal;

            var seed = TheWaningBorder.World.Regions.RegionMap.SeedOf(region);
            var seedP = new Vector2(seed.x, seed.y);
            for (int step = 0; step <= 24; step++)
            {
                var p = Vector2.Lerp(ideal, seedP, step / 24f);
                float y = HeightAt(p.x, p.y);
                if (y <= 4f + 2.85f || y >= 24f - 2.85f) continue;
                if (TheWaningBorder.World.Regions.RegionMap.RegionAt(p.x, p.y) == region)
                    return p;
            }
            Debug.LogWarning($"[{MapName}] could not seat a marker inside region {region} — " +
                             "using the ideal position.");
            return ideal;
        }

        private static void Forest(Transform parent, string name, Vector2 p, float radius)
        {
            var go = NewMarker(name, p, parent);
            var m = go.AddComponent<NatureRegionMarker>();
            m.Kind = NatureRegionMarker.NatureKind.Forest;
            m.Radius = radius;
            _forests.Add((p, radius));
        }

        private static void Iron(Transform parent, string name, Vector2 p, int deposits)
        {
            var m = NewMarker(name, p, parent).AddComponent<IronPatchMarker>();
            m.DepositCount = deposits;
            _keepClear.Add((p, 14f));
        }

        private static void Veilstone(Transform parent, string name, Vector2 p)
        {
            NewMarker(name, p, parent).AddComponent<VeilstoneOutcroppingMarker>();
            _keepClear.Add((p, 14f));
        }

        private static void Veilsteel(Transform parent, string name, Vector2 p)
        {
            var m = NewMarker(name, p, parent).AddComponent<VeilsteelDepositMarker>();
            // The authored list IS the map's veilsteel: suppress the
            // 1-in-3-territories coverage top-up that would scatter deposits
            // into the very regions this map keeps deliberately bare.
            m.MapExclusive = true;
            _keepClear.Add((p, 14f));
        }

        private static void Supply(Transform parent, string name, Vector2 p)
        {
            NewMarker(name, p, parent).AddComponent<SupplyNodeMarker>();
            _keepClear.Add((p, 12f));
        }

        private static void NewSeed(Transform parent, ref int index, Vector2 p, string name)
        {
            var go = NewMarker($"Region {index:00} - {name}", p, parent);
            go.AddComponent<RegionSeedMarker>().RegionName = name;
            _keepClear.Add((p, 8f));
            index++;
        }

        private static GameObject NewMarker(string name, Vector2 p, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(p.x, HeightAt(p.x, p.y), p.y);
            return go;
        }

        // ── flora shaping ───────────────────────────────────────────────────

        /// <summary>
        /// Deterministic density shaping for the single global flora pass:
        /// nothing on protected ground, everything on the Nature ring band,
        /// nothing inside forest discs (the dense-fill pass owns those), and a
        /// hash-thinned ~22% across the open field so the plains stay OPEN.
        /// </summary>
        private static bool CanPlant(float wx, float wz)
        {
            foreach (var (p, r) in _keepClear)
                if (MapGenKit.Dist(wx, wz, p.x, p.y) < r) return false;

            foreach (var (p, r) in _forests)
                if (MapGenKit.Dist(wx, wz, p.x, p.y) < r + 2f) return false;

            if (NatureRingMask(wx, wz) > 0.15f)
                return Hash(wx, wz) % 100 < 65;    // thick edge band

            float y = HeightAt(wx, wz);
            if (y > 24f) return Hash(wx, wz) % 100 < 45;  // scree on the ridges

            return Hash(wx, wz) % 100 < 22;        // open field: sparse
        }

        /// <summary>Deterministic per-cell hash — Random here would make every
        /// build a different map.</summary>
        private static uint Hash(float wx, float wz)
        {
            int cx = Mathf.FloorToInt(wx / 2f), cz = Mathf.FloorToInt(wz / 2f);
            uint h = (uint)(cx * 73856093 ^ cz * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return h;
        }

        /// <summary>
        /// Densely fill each NatureRegion disc with tree instances so the
        /// impassable stands READ as forests rather than empty circles. No
        /// NoWalk paint needed: NatureRegionBootstrap blocks the discs at
        /// runtime. Scales are cloned from instances the MapGenKit pass
        /// already placed, so stand trees match the field trees.
        /// </summary>
        private static void FillForestStands(TerrainData data)
        {
            var protos = data.treePrototypes;
            if (protos == null || protos.Length == 0 || _forests.Count == 0) return;

            // Which prototypes are actual trees (not rocks/bushes), and a
            // representative scale per prototype from the global pass.
            var treeProtoIdx = new List<int>();
            for (int i = 0; i < protos.Length; i++)
            {
                string n = protos[i].prefab != null ? protos[i].prefab.name : "";
                if (n.Contains("Tree") || n.Contains("Pine")) treeProtoIdx.Add(i);
            }
            if (treeProtoIdx.Count == 0) return;

            var scaleOf = new Dictionary<int, float>();
            foreach (var t in data.treeInstances)
                if (!scaleOf.ContainsKey(t.prototypeIndex))
                    scaleOf[t.prototypeIndex] = t.widthScale;

            var all = new List<TreeInstance>(data.treeInstances);
            float half = MapMetres * 0.5f;

            foreach (var (c, r) in _forests)
            {
                int cells = Mathf.CeilToInt(r / 2f);
                for (int gz = -cells; gz <= cells; gz++)
                    for (int gx = -cells; gx <= cells; gx++)
                    {
                        float wx = c.x + gx * 2f, wz = c.y + gz * 2f;
                        if (MapGenKit.Dist(wx, wz, c.x, c.y) > r - 1f) continue;
                        if (Mathf.Abs(wx) > half - 2f || Mathf.Abs(wz) > half - 2f) continue;
                        // 55% -> 30% of cells (2026-08-31 perf pass): a
                        // stand still reads as solid forest at 30%, and the
                        // block comes from the marker either way.
                        if (Hash(wx, wz) % 100 >= 30) continue;   // ~30% of cells

                        int proto = treeProtoIdx[(int)(Hash(wx + 7f, wz - 3f)
                                                       % (uint)treeProtoIdx.Count)];
                        float s = (scaleOf.TryGetValue(proto, out float w) ? w : 0.6f)
                                  * (0.9f + (Hash(wx - 5f, wz + 9f) % 100) * 0.002f);
                        all.Add(new TreeInstance
                        {
                            position = new Vector3((wx + half) / MapMetres, 0f,
                                                   (wz + half) / MapMetres),
                            prototypeIndex = proto,
                            widthScale = s,
                            heightScale = s * 0.5f,
                            rotation = (Hash(wx + 1f, wz + 1f) % 628) * 0.01f,
                            color = Color.white,
                            lightmapColor = Color.white,
                        });
                    }
            }
            data.SetTreeInstances(all.ToArray(), true);
        }

        // ── validation ──────────────────────────────────────────────────────

        private static int ValidatePlacements()
        {
            int bad = 0;

            void Check(string what, Vector3 world)
            {
                float y = HeightAt(world.x, world.z);
                string why = null;
                if (y <= 4f + 2.85f) why = $"under water (y={y:0.0} m)";
                else if (y >= 24f - 2.85f) why = $"on ridge / Nature ring (y={y:0.0} m)";
                if (why == null) return;
                Debug.LogError($"[{MapName}] {what} at ({world.x:0}, {world.z:0}) is {why}.");
                bad++;
            }

            foreach (var m in Object.FindObjectsByType<MapMarker>(FindObjectsSortMode.None))
            {
                if (m is NatureRegionMarker) continue;
                Check(m.name, m.transform.position);
            }

            // The design rules, verified rather than assumed:
            //   homes have iron AND veilstone (Regions.md §3, 2026-08-31 —
            //   every starter territory carries an outcropping); veilsteel
            //   ONLY in the Veilfields (regions 1-4); EXACTLY ONE well — the
            //   pure node — in The Scar (region 0); and the node quotas of
            //   Regions.md §4 — 2 supply nodes per territory (4 in a home),
            //   1-4 ore nodes everywhere. The 50% veilstone coverage rule is
            //   runtime-topped-up (ResourceNodeCoverage), so it is not
            //   asserted here; the authored minimum is.
            var iron = Object.FindObjectsByType<IronPatchMarker>(FindObjectsSortMode.None);
            var supply = Object.FindObjectsByType<SupplyNodeMarker>(FindObjectsSortMode.None);
            var veilstone = Object.FindObjectsByType<VeilstoneOutcroppingMarker>(FindObjectsSortMode.None);
            var veilsteel = Object.FindObjectsByType<VeilsteelDepositMarker>(FindObjectsSortMode.None);

            var homeRegions = new HashSet<int>();
            foreach (var start in Object.FindObjectsByType<PlayerStartMarker>(FindObjectsSortMode.None))
            {
                var sp = start.transform.position;
                int home = TheWaningBorder.World.Regions.RegionMap.RegionAt(sp.x, sp.z);
                if (home < 0)
                { Debug.LogError($"[{MapName}] {start.name} is in no region."); bad++; continue; }
                homeRegions.Add(home);
                if (CountIn(iron, home) == 0)
                { Debug.LogError($"[{MapName}] home {home} ({start.name}) has NO IRON."); bad++; }
                if (CountIn(veilstone, home) == 0)
                { Debug.LogError($"[{MapName}] home {home} ({start.name}) has NO VEILSTONE — " +
                                 "every starter territory must carry an outcropping."); bad++; }
            }

            int regions = TheWaningBorder.World.Regions.RegionMap.Count;
            for (int r = 0; r < regions; r++)
            {
                int wantSupply = homeRegions.Contains(r) ? 4 : 2;
                int sup = CountIn(supply, r);
                if (sup != wantSupply)
                {
                    Debug.LogError($"[{MapName}] region {r} has {sup} supply node(s) — " +
                                   $"the quota is exactly {wantSupply}.");
                    bad++;
                }
                int ore = CountIn(iron, r) + CountIn(veilstone, r) + CountIn(veilsteel, r);
                if (ore < 1 || ore > 4)
                {
                    Debug.LogError($"[{MapName}] region {r} has {ore} ore node(s) — " +
                                   "the quota is 1-4.");
                    bad++;
                }
            }

            // Veilstone is map-wide now (Regions.md §3) — no centre-ring
            // exclusivity to assert. Veilsteel keeps its.
            foreach (var v in Object.FindObjectsByType<VeilsteelDepositMarker>(FindObjectsSortMode.None))
                bad += RequireCentre(v.name, v.transform.position, "veilsteel");

            var wells = Object.FindObjectsByType<BorderNodeMarker>(FindObjectsSortMode.None);
            if (wells.Length != 1)
            {
                Debug.LogError($"[{MapName}] {wells.Length} well marker(s) — this map carries " +
                               "EXACTLY ONE pure node, in the centre territory.");
                bad++;
            }
            foreach (var w in wells)
            {
                int r = TheWaningBorder.World.Regions.RegionMap.RegionAt(
                    w.transform.position.x, w.transform.position.z);
                if (r != 0)
                {
                    Debug.LogError($"[{MapName}] {w.name} is in region {r} — the pure node " +
                                   "belongs in The Scar (region 0) only.");
                    bad++;
                }
            }
            return bad;
        }

        private static int RequireCentre(string name, Vector3 pos, string kind)
        {
            int r = TheWaningBorder.World.Regions.RegionMap.RegionAt(pos.x, pos.z);
            if (r >= 1 && r <= Sectors) return 0;
            Debug.LogError($"[{MapName}] {name} is in region {r} — {kind} is exclusive to the " +
                           $"Veilfields (regions 1-{Sectors}).");
            return 1;
        }

        private static int CountIn(MapMarker[] markers, int region)
        {
            int n = 0;
            foreach (var m in markers)
                if (TheWaningBorder.World.Regions.RegionMap.RegionAt(
                        m.transform.position.x, m.transform.position.z) == region) n++;
            return n;
        }

        /// <summary>
        /// Measure the partition instead of trusting the seed constants:
        /// sample claimable ground on a 4 m grid, sum area per region, and
        /// check homes really are about three small territories each.
        /// </summary>
        private static int ValidateRegionAreas()
        {
            const int N = 256;
            var area = new Dictionary<int, float>();
            float cell = MapMetres / N;
            for (int z = 0; z < N; z++)
                for (int x = 0; x < N; x++)
                {
                    float wx = (x + 0.5f) * cell - MapMetres * 0.5f;
                    float wz = (z + 0.5f) * cell - MapMetres * 0.5f;
                    float y = HeightAt(wx, wz);
                    if (y <= 4f || y >= 24f) continue;   // unclaimable
                    int r = TheWaningBorder.World.Regions.RegionMap.RegionAt(wx, wz);
                    if (r < 0) continue;
                    area.TryGetValue(r, out float a);
                    area[r] = a + cell * cell;
                }

            float homeSum = 0f; int homes = 0;
            float smallSum = 0f; int smalls = 0;
            foreach (var kv in area)
            {
                if (kv.Key >= 1 + Sectors && kv.Key < 1 + Sectors * 2) { homeSum += kv.Value; homes++; }
                else if (kv.Key != 0) { smallSum += kv.Value; smalls++; }
            }
            if (homes == 0 || smalls == 0)
            { Debug.LogError($"[{MapName}] area audit found no regions."); return 1; }

            float homeAvg = homeSum / homes, smallAvg = smallSum / smalls;
            float ratio = homeAvg / smallAvg;
            Debug.Log($"[{MapName}] AREA AUDIT: home avg {homeAvg / 10000f:0.0} ha, " +
                      $"small avg {smallAvg / 10000f:0.0} ha, ratio {ratio:0.00} " +
                      $"(design: 3 small = 1 large).");
            if (ratio < 2.3f || ratio > 4.2f)
            {
                Debug.LogError($"[{MapName}] home/small area ratio {ratio:0.00} is outside " +
                               "2.3-4.2 — adjust seed spacing (area goes with spacing squared).");
                return 1;
            }
            return 0;
        }
    }
}
