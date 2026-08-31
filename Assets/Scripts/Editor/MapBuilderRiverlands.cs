// MapBuilderRiverlands.cs
// EDITOR-ONLY: generate "Sundered Reach" — the 512 m, 6-player reference map.
//   Waning Border > Maps > Build Sundered Reach (512m, 6 players)
//
// Built to docs/Design/Regions.md third pass:
//
//   * 512 x 512 m, 6 players, 25 territories (players * 4 + 1)
//   * a NATURE RING around the edge — unwalkable, unclaimable, no culture and
//     no curse look, explored from the start but never lit by vision. It exists
//     so the map ends in something that reads as terrain rather than a ruler cut
//   * MOUNTAINS between the home sectors — impassable AND unclaimable, the
//     structural dividers that give territory borders something real to follow
//   * FORESTS inside the home sectors — impassable but CLAIMABLE; they take
//     their owner's culture decorations and produce supplies
//   * curse NODES at the centre of some territories, from which waves attack
//     neighbouring territories
//
// The layout is POLAR, not hand-typed: 6-fold rotational symmetry is generated
// from one sector definition, so every player's opening is identical by
// construction rather than by me typing six sets of coordinates correctly.
// Editing one radius moves all six of a thing.
//
// Re-runnable: overwrites the terrain asset and scene in place. Afterwards run
// "Bake Map Info From Open Scene".

using System.Collections.Generic;
using TheWaningBorder.World.MapMarkers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class MapBuilderRiverlands
    {
        private const string MapName = "Sundered Reach";
        private const string SceneName = "SunderedReach";
        private const string Folder = "Assets/GameData/Scenes/Maps/Sundered Reach";
        private const string LayerFolder = "Assets/GameData/Scenes/Maps/Twin Spans";
        private const string TerrainMatPath = "Assets/Resources/TWBTerrain.mat";

        // ── dimensions ──────────────────────────────────────────────────────
        private const float MapMetres = 512f;
        private const float MaxHeight = 60f;
        private const int HeightRes = 1025;      // ~0.5 m per texel
        private const int AlphaRes = 512;

        // Heights chosen against PassabilityGrid's thresholds:
        //   WaterHeight = 4 m, MountainHeight = 24 m, MaxWalkableSlope = 1.0
        // RegionMap.IsClaimable uses the same two numbers, so anything raised
        // above 24 m or sunk below 4 m is automatically BOTH impassable and
        // unclaimable — which is exactly what mountains and the Nature ring are.
        private const float PlainY = 8f;
        private const float LakeY = 1.0f;
        private const float RidgeY = 34f;
        private const float NatureY = 40f;

        // ── polar layout (metres from map centre) ───────────────────────────
        private const int Sectors = 6;                // one per player
        private const float NatureRingMetres = 34f;   // band thickness at the edge

        private const float RingA = 65f;    // inner territories, around the centre
        private const float RingB = 120f;   // mid territories (offset half a sector)
        private const float RingC = 170f;   // HOME ring — the player starts
        private const float RingD = 210f;   // outer territories (offset half a sector)

        private const float ForestRadius = 125f;   // inside each home sector
        private const float MountainRadius = 170f; // BETWEEN home sectors
        private const float LakeRadius = 72f;

        private const float ForestSize = 30f;
        private const float MountainSize = 32f;
        private const float LakeSize = 24f;

        private static readonly Faction[] StartFactions =
        {
            Faction.Blue, Faction.Red, Faction.Green,
            Faction.Yellow, Faction.Purple, Faction.Orange,
        };

        /// <summary>Sector names, counter-clockwise from due east.</summary>
        private static readonly string[] SectorNames =
        { "Eastwatch", "Northmarch", "Highfell", "Westward", "Southrest", "Lowmoor" };

        // ── entry point ─────────────────────────────────────────────────────

        [MenuItem("Waning Border/Maps/Build Sundered Reach (512m, 6 players)")]
        public static void Build()
        {
            if (!EditorUtility.DisplayDialog(MapName,
                    $"Generate {MapName}?\n\n" +
                    $"  {MapMetres:0} x {MapMetres:0} m\n" +
                    $"  {Sectors} players, 6-fold symmetry\n" +
                    $"  {Sectors * 4 + 1} territories\n" +
                    $"  {NatureRingMetres:0} m Nature ring\n\n" +
                    $"Overwrites {SceneName}.unity and its TerrainData.",
                    "Build", "Cancel"))
                return;

            MapAssetFolders.Ensure(Folder);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                                                    NewSceneMode.Single);

            var data = BuildTerrainData();
            string dataPath = $"{Folder}/{SceneName} TerrainData.asset";
            AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CreateAsset(data, dataPath);

            var terrainGo = Terrain.CreateTerrainGameObject(data);
            terrainGo.name = "Terrain";
            terrainGo.transform.position = new Vector3(-MapMetres * 0.5f, 0f, -MapMetres * 0.5f);

            var terrain = terrainGo.GetComponent<Terrain>();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(TerrainMatPath);
            if (mat != null) terrain.materialTemplate = mat;
            else Debug.LogWarning($"[{MapName}] {TerrainMatPath} not found — the TWB culture / " +
                                  "blood / curse / region overlays will not render.");

            PlaceMarkers();
            int bad = ValidatePlacements();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, $"{Folder}/{SceneName}.unity");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            bool gated = !MapRegistry.ShouldShip($"{Folder}/{SceneName}.unity");
            string next = "Next step:\n    Waning Border > Maps > Bake Map Info From Open Scene";
            if (gated)
            {
                Debug.LogError($"[{MapName}] NOT IN THE SHIP GATE. \"{SceneName}\" is missing from " +
                               "MapRegistry.ShippingMapScenes, so MapSceneSync keeps it OUT of " +
                               "Build Settings (and strips it again if added by hand) and it will " +
                               "not appear in the skirmish menu.");
                next = $"FIRST: add \"{SceneName}\" to MapRegistry.ShippingMapScenes\n\n" + next;
            }
            if (bad > 0)
                next = $"WARNING: {bad} marker(s) on unusable ground - see Console\n\n" + next;

            Debug.Log($"[{MapName}] Built {MapMetres:0} m, {Sectors} starts, " +
                      $"{Sectors * 4 + 1} territories.");
            EditorUtility.DisplayDialog(MapName, $"{MapName} built.\n\n{next}", "OK");
        }

        // ── polar helpers ───────────────────────────────────────────────────

        /// <summary>Sector angle in radians. <paramref name="half"/> offsets by
        /// half a sector — how the "between the players" rings and the mountain
        /// dividers are placed.</summary>
        private static float Angle(int sector, bool half = false)
            => (sector + (half ? 0.5f : 0f)) * Mathf.PI * 2f / Sectors;

        /// <summary>Polar (metres from centre) to normalized map coords.</summary>
        private static Vector2 Polar(float radius, float angle)
            => new Vector2(0.5f + Mathf.Cos(angle) * radius / MapMetres,
                           0.5f + Mathf.Sin(angle) * radius / MapMetres);

        // ── terrain ─────────────────────────────────────────────────────────

        private static TerrainData BuildTerrainData()
        {
            var data = new TerrainData
            {
                heightmapResolution = HeightRes,
                alphamapResolution = AlphaRes,
                baseMapResolution = 1024,
            };
            data.SetDetailResolution(1024, 16);
            data.size = new Vector3(MapMetres, MaxHeight, MapMetres);

            var h = new float[HeightRes, HeightRes];
            for (int z = 0; z < HeightRes; z++)
            {
                float nz = z / (float)(HeightRes - 1);
                for (int x = 0; x < HeightRes; x++)
                {
                    // Unity's heightmap is indexed [z, x].
                    h[z, x] = HeightAt(x / (float)(HeightRes - 1), nz) / MaxHeight;
                }
            }
            data.SetHeights(0, 0, h);
            AssignLayers(data);
            return data;
        }

        private static float HeightAt(float nx, float nz)
        {
            float y = PlainY + Noise(nx, nz);

            // Nature ring — raised above MountainHeight, so it is impassable and
            // unclaimable by the same rule mountains are. Rounded so the map
            // ends in a curve rather than a corner.
            float ring = NatureRingMask(nx, nz);
            if (ring > 0f) y = Mathf.Lerp(y, NatureY, Smooth(ring));

            // Mountains between the home sectors.
            float up = 0f;
            for (int i = 0; i < Sectors; i++)
            {
                var c = Polar(MountainRadius, Angle(i, half: true));
                up = Mathf.Max(up, Blob(nx, nz, c, MountainSize / MapMetres));
            }
            if (up > 0f) y = Mathf.Lerp(y, RidgeY, Smooth(up));

            // Lakes, sunk last so one never sits on a ridge.
            float down = 0f;
            for (int i = 0; i < Sectors; i += 2)
            {
                var c = Polar(LakeRadius, Angle(i, half: true));
                down = Mathf.Max(down, Blob(nx, nz, c, LakeSize / MapMetres));
            }
            if (down > 0f) y = Mathf.Lerp(y, LakeY, Smooth(down));

            return y;
        }

        /// <summary>0 inside the play field, ramping to 1 across the Nature band.</summary>
        private static float NatureRingMask(float nx, float nz)
        {
            float inset = NatureRingMetres / MapMetres;
            const float corner = 0.085f;
            float dx = Mathf.Max(inset - nx, nx - (1f - inset), 0f);
            float dz = Mathf.Max(inset - nz, nz - (1f - inset), 0f);
            return Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz) / corner);
        }

        /// <summary>Soft-edged disc coverage, 0..1.</summary>
        private static float Blob(float nx, float nz, Vector2 centre, float radius)
        {
            float d = Mathf.Sqrt((nx - centre.x) * (nx - centre.x) + (nz - centre.y) * (nz - centre.y));
            return Mathf.Clamp01((radius - d) / (radius * 0.35f));
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        private static float Noise(float nx, float nz)
        {
            float a = Mathf.PerlinNoise(nx * 6f + 11.7f, nz * 6f + 3.1f) - 0.5f;
            float b = Mathf.PerlinNoise(nx * 17f + 5.2f, nz * 17f + 9.4f) - 0.5f;
            return a * 4.5f + b * 1.2f;
        }

        private static void AssignLayers(TerrainData data)
        {
            var names = new[] { "Grass", "Dirt", "Rock", "NoWalk" };
            var layers = new List<TerrainLayer>();
            foreach (var n in names)
            {
                var l = AssetDatabase.LoadAssetAtPath<TerrainLayer>($"{LayerFolder}/{n}.terrainlayer");
                if (l != null) layers.Add(l);
            }
            if (layers.Count == 0)
            {
                Debug.LogWarning($"[{MapName}] No terrain layers under {LayerFolder} — terrain " +
                                 "will be untextured.");
                return;
            }
            data.terrainLayers = layers.ToArray();

            int lc = layers.Count;
            var map = new float[AlphaRes, AlphaRes, lc];
            for (int z = 0; z < AlphaRes; z++)
            {
                float nz = z / (float)(AlphaRes - 1);
                for (int x = 0; x < AlphaRes; x++)
                {
                    float y = HeightAt(x / (float)(AlphaRes - 1), nz);
                    int pick = 0;                                   // Grass
                    if (y < 4f) pick = Mathf.Min(1, lc - 1);        // Dirt  (lake bed)
                    else if (y > 24f) pick = Mathf.Min(2, lc - 1);  // Rock  (mountain / ring)
                    for (int l = 0; l < lc; l++) map[z, x, l] = l == pick ? 1f : 0f;
                }
            }
            data.SetAlphamaps(0, 0, map);
        }

        // ── markers ─────────────────────────────────────────────────────────

        private static void PlaceMarkers()
        {
            // Player starts — one per sector on the home ring.
            var startsRoot = new GameObject("PlayerStarts").transform;
            for (int i = 0; i < Sectors; i++)
            {
                var p = Polar(RingC, Angle(i));
                var go = NewMarker($"P{i + 1} Start ({StartFactions[i]}) - {SectorNames[i]}",
                                   p.x, p.y, startsRoot);
                go.AddComponent<PlayerStartMarker>().Faction = StartFactions[i];
            }

            // Forests — one inside each home sector. Impassable but CLAIMABLE:
            // they take their territory owner's culture and produce supplies
            // (Regions.md §1, §4), which is why they are markers and not terrain.
            var natureRoot = new GameObject("NatureRegions").transform;
            for (int i = 0; i < Sectors; i++)
            {
                var p = Polar(ForestRadius, Angle(i));
                var go = NewMarker($"Forest - {SectorNames[i]}", p.x, p.y, natureRoot);
                var m = go.AddComponent<NatureRegionMarker>();
                m.Kind = NatureRegionMarker.NatureKind.Forest;
                m.Radius = ForestSize;
            }

            // 25 territories: centre + four rings of six.
            var regionRoot = new GameObject("Regions").transform;
            int idx = 0;
            NewSeed(regionRoot, ref idx, new Vector2(0.5f, 0.5f), "The Hollow");
            for (int i = 0; i < Sectors; i++)
                NewSeed(regionRoot, ref idx, Polar(RingA, Angle(i)), $"Inner {SectorNames[i]}");
            for (int i = 0; i < Sectors; i++)
                NewSeed(regionRoot, ref idx, Polar(RingB, Angle(i, half: true)), "");
            for (int i = 0; i < Sectors; i++)
                NewSeed(regionRoot, ref idx, Polar(RingC, Angle(i)), $"{SectorNames[i]} Home");
            for (int i = 0; i < Sectors; i++)
                NewSeed(regionRoot, ref idx, Polar(RingD, Angle(i, half: true)), "");

            // Curse Nodes sit at the CENTRE of a territory (Regions.md §3) and
            // send waves at its neighbours. Placed on the centre territory and on
            // every other mid-ring territory, so each of the six players has
            // exactly one Node adjacent to their home — SHARED with a neighbour,
            // which is what makes a Node contested ground rather than one
            // player's private problem.
            var wellRoot = new GameObject("Wells").transform;
            var nodes = new List<Vector2> { new Vector2(0.5f, 0.5f) };
            for (int i = 0; i < Sectors; i += 2) nodes.Add(Polar(RingB, Angle(i, half: true)));
            for (int i = 0; i < nodes.Count; i++)
            {
                var go = NewMarker($"Node {i:00}", nodes[i].x, nodes[i].y, wellRoot);
                go.AddComponent<BorderNodeMarker>().AuthoredPosition = true;
            }

            // Resources. Under Regions.md §2 an Age 0 player is confined to
            // their start territory, and under §4 income is the TERRITORY tick
            // -- so a home territory without iron and veilstone is not "a weak
            // start", it is a player who can never build anything that needs
            // either. Every home therefore gets both, and the placement is
            // VERIFIED rather than assumed (see PlaceInHome).
            var resRoot = new GameObject("Resources").transform;

            // The partition has to exist before we can ask which territory a
            // point falls in. Seeds are in world space; the generator works in
            // normalized, hence the conversion.
            //
            // SORT BY NAME before Configure. FindObjectsByType returns
            // arbitrary order, but region ids at runtime come from
            // MapMarkerRegistry's name-ordinal sort — the "Region NN" prefix
            // pins creation order. Feeding Configure the unsorted list makes
            // every id in validation a scramble (Veilmarch hit exactly this:
            // its first build reported wells "in region 18" that were sitting
            // dead centre).
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

            // Node quotas (Regions.md §4): every territory carries exactly 2
            // supply nodes — a home 4 — and 1-4 ore nodes. The runtime top-up
            // would fill a shortfall, but an authored map should not lean on
            // the safety net, so every one of the 25 territories is stocked
            // here and ValidateTerritoryNodes counts them all.
            for (int i = 0; i < Sectors; i++)
            {
                float a = Angle(i);
                float ha = Angle(i, half: true);
                int home = RegionAtNorm(Polar(RingC, a));

                Iron(resRoot, $"Iron Home {i}a", PlaceInHome(home, RingC - 26f, a - 0.13f));
                Iron(resRoot, $"Iron Home {i}b", PlaceInHome(home, RingC - 26f, a + 0.13f));

                var v = PlaceInHome(home, RingD - 12f, a);
                NewMarker($"Veilstone Home {i}", v.x, v.y, resRoot)
                    .AddComponent<VeilstoneOutcroppingMarker>();

                Supply(resRoot, $"Supply Home {i}a", PlaceInHome(home, RingC - 34f, a - 0.20f));
                Supply(resRoot, $"Supply Home {i}b", PlaceInHome(home, RingC - 8f, a + 0.22f));
                Supply(resRoot, $"Supply Home {i}c", PlaceInHome(home, RingC + 18f, a - 0.16f));
                Supply(resRoot, $"Supply Home {i}d", PlaceInHome(home, RingC + 26f, a + 0.10f));

                // Contested ground between the players — seated INSIDE its
                // mid territory, or the domain warp can hand it (and the ore
                // quota it satisfies) to a neighbour.
                int mid = RegionAtNorm(Polar(RingB, ha));
                Iron(resRoot, $"Iron Mid {i}", PlaceInHome(mid, RingB, ha + 0.10f));

                // Inner ring: three carry the map's veilsteel (below); the
                // other three get iron so no territory is ore-less.
                int inner = RegionAtNorm(Polar(RingA, a));
                Supply(resRoot, $"Supply Inner {i}a", PlaceInHome(inner, RingA - 14f, a - 0.30f));
                Supply(resRoot, $"Supply Inner {i}b", PlaceInHome(inner, RingA + 12f, a + 0.28f));
                if (i % 2 == 1)
                    Iron(resRoot, $"Iron Inner {i}", PlaceInHome(inner, RingA + 6f, a));

                Supply(resRoot, $"Supply Mid {i}a", PlaceInHome(mid, RingB - 16f, ha - 0.16f));
                Supply(resRoot, $"Supply Mid {i}b", PlaceInHome(mid, RingB + 14f, ha + 0.20f));

                int outer = RegionAtNorm(Polar(RingD, ha));
                Iron(resRoot, $"Iron Outer {i}", PlaceInHome(outer, RingD + 8f, ha));
                Supply(resRoot, $"Supply Outer {i}a", PlaceInHome(outer, RingD - 12f, ha - 0.18f));
                Supply(resRoot, $"Supply Outer {i}b", PlaceInHome(outer, RingD + 22f, ha + 0.16f));
            }
            for (int i = 0; i < Sectors; i += 2)
            {
                var p = Polar(RingA, Angle(i));
                NewMarker($"Veilsteel {i}", p.x, p.y, resRoot).AddComponent<VeilsteelDepositMarker>();
            }

            // The Hollow: the centre well territory — but a territory still,
            // and the quota exempts nobody. Whoever clears it gets working
            // ground.
            int hollow = RegionAtNorm(new Vector2(0.5f, 0.5f));
            Iron(resRoot, "Iron Hollow", PlaceInHome(hollow, 26f, 0.7f));
            Supply(resRoot, "Supply Hollow a", PlaceInHome(hollow, 26f, 2.8f));
            Supply(resRoot, "Supply Hollow b", PlaceInHome(hollow, 26f, 4.9f));
        }

        /// <summary>Region under a NORMALIZED map point.</summary>
        private static int RegionAtNorm(Vector2 n)
        {
            var w = ToWorld(n.x, n.y);
            return TheWaningBorder.World.Regions.RegionMap.RegionAt(w.x, w.z);
        }

        /// <summary>
        /// A point at roughly (radius, angle) that is GUARANTEED to sit inside
        /// territory <paramref name="home"/> and on passable ground.
        ///
        /// Not a formality. Territory boundaries are domain-warped by up to
        /// +/-42 m per axis (RegionMap), which is larger than the margin between
        /// a home deposit and the neighbouring territory -- so a deposit placed
        /// by polar arithmetic alone can silently land next door, and the
        /// player it was meant for starts with no iron at all. Rather than pick
        /// radii that happen to survive the warp, walk toward the home seed
        /// until the partition itself agrees.
        /// </summary>
        private static Vector2 PlaceInHome(int home, float radius, float angle)
        {
            var ideal = Polar(radius, angle);
            if (home < 0) return ideal;

            var seed = TheWaningBorder.World.Regions.RegionMap.SeedOf(home);
            var seedN = new Vector2(seed.x / MapMetres + 0.5f, seed.y / MapMetres + 0.5f);

            // 24 steps from the ideal spot to the seed itself. The seed is by
            // definition inside its own territory, so this always terminates.
            for (int step = 0; step <= 24; step++)
            {
                var p = Vector2.Lerp(ideal, seedN, step / 24f);
                var w = ToWorld(p.x, p.y);
                float y = HeightAt(p.x, p.y);
                if (y <= 4f + 2.85f || y >= 24f - 2.85f) continue;      // unusable ground
                if (TheWaningBorder.World.Regions.RegionMap.RegionAt(w.x, w.z) == home)
                    return p;
            }
            Debug.LogWarning($"[{MapName}] could not seat a deposit inside territory {home} — " +
                             "falling back to the ideal position.");
            return ideal;
        }

        private static void Iron(Transform parent, string name, Vector2 p)
        {
            var m = NewMarker(name, p.x, p.y, parent).AddComponent<IronPatchMarker>();
            m.DepositCount = 30;
        }

        private static void Supply(Transform parent, string name, Vector2 p)
            => NewMarker(name, p.x, p.y, parent).AddComponent<SupplyNodeMarker>();

        private static void NewSeed(Transform parent, ref int index, Vector2 p, string name)
        {
            var go = NewMarker($"Region {index:00}{(string.IsNullOrEmpty(name) ? "" : " - " + name)}",
                               p.x, p.y, parent);
            go.AddComponent<RegionSeedMarker>().RegionName = name;
            index++;
        }

        private static GameObject NewMarker(string name, float nx, float nz, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = ToWorld(nx, nz);
            return go;
        }

        private static Vector3 ToWorld(float nx, float nz)
            => new Vector3((nx - 0.5f) * MapMetres, HeightAt(nx, nz), (nz - 0.5f) * MapMetres);

        /// <summary>
        /// Every authored point must stand on ground a unit could stand on.
        /// A territory seed inside a lake can never be claimed, an iron patch
        /// inside a mountain is unminable, a start in the Nature ring is
        /// unplayable. The polar layout means a radius typo moves six markers at
        /// once, so this runs on every build rather than trusting the constants.
        /// </summary>
        private static int ValidatePlacements()
        {
            int bad = 0;

            void Check(string what, Vector3 world)
            {
                float nx = world.x / MapMetres + 0.5f;
                float nz = world.z / MapMetres + 0.5f;
                float y = HeightAt(nx, nz);
                string why = null;
                if (y <= 4f + 2.85f) why = $"under water (y={y:0.0} m)";
                else if (y >= 24f - 2.85f) why = $"on mountain / Nature ring (y={y:0.0} m)";
                if (why == null) return;
                Debug.LogError($"[{MapName}] {what} at ({nx:0.000}, {nz:0.000}) is {why}.");
                bad++;
            }

            foreach (var m in Object.FindObjectsByType<MapMarker>(FindObjectsSortMode.None))
            {
                // Forests are SUPPOSED to be impassable, and they sit on ordinary
                // ground anyway; everything else must be standable.
                if (m is NatureRegionMarker) continue;
                Check(m.name, m.transform.position);
            }

            bad += ValidateHomeResources();
            bad += ValidateTerritoryNodes();
            return bad;
        }

        /// <summary>
        /// The node quotas of Regions.md §4, counted over every territory:
        /// exactly 2 supply nodes (4 where a player starts), and 1-4 ore
        /// nodes. The runtime top-up would fill a shortfall anyway, but an
        /// authored map should not lean on the safety net.
        /// </summary>
        private static int ValidateTerritoryNodes()
        {
            int bad = 0;
            var iron = Object.FindObjectsByType<IronPatchMarker>(FindObjectsSortMode.None);
            var supply = Object.FindObjectsByType<SupplyNodeMarker>(FindObjectsSortMode.None);
            var veilstone = Object.FindObjectsByType<VeilstoneOutcroppingMarker>(FindObjectsSortMode.None);
            var veilsteel = Object.FindObjectsByType<VeilsteelDepositMarker>(FindObjectsSortMode.None);

            var homes = new HashSet<int>();
            foreach (var start in Object.FindObjectsByType<PlayerStartMarker>(FindObjectsSortMode.None))
            {
                var sp = start.transform.position;
                int r = TheWaningBorder.World.Regions.RegionMap.RegionAt(sp.x, sp.z);
                if (r >= 0) homes.Add(r);
            }

            int regions = TheWaningBorder.World.Regions.RegionMap.Count;
            for (int r = 0; r < regions; r++)
            {
                int wantSupply = homes.Contains(r) ? 4 : 2;
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
            return bad;
        }

        private static int CountIn(MapMarker[] markers, int region)
        {
            int n = 0;
            for (int i = 0; i < markers.Length; i++)
            {
                var p = markers[i].transform.position;
                if (TheWaningBorder.World.Regions.RegionMap.RegionAt(p.x, p.z) == region) n++;
            }
            return n;
        }

        /// <summary>
        /// Every home territory must contain at least one iron AND one
        /// veilstone deposit.
        ///
        /// This is a playability requirement, not polish: Regions.md §2 confines
        /// an Age 0 player to their start territory, and §4 makes income the
        /// territory tick -- so a home with no iron is a player who cannot build
        /// anything that costs iron, for the whole of the first age, with no way
        /// to go and get some.
        /// </summary>
        private static int ValidateHomeResources()
        {
            int bad = 0;
            var iron = Object.FindObjectsByType<IronPatchMarker>(FindObjectsSortMode.None);
            var veil = Object.FindObjectsByType<VeilstoneOutcroppingMarker>(FindObjectsSortMode.None);

            foreach (var start in Object.FindObjectsByType<PlayerStartMarker>(FindObjectsSortMode.None))
            {
                var sp = start.transform.position;
                int home = TheWaningBorder.World.Regions.RegionMap.RegionAt(sp.x, sp.z);
                if (home < 0)
                {
                    Debug.LogError($"[{MapName}] {start.name} is not inside any territory.");
                    bad++;
                    continue;
                }

                if (!AnyIn(iron, home))
                {
                    Debug.LogError($"[{MapName}] home territory {home} ({start.name}) has NO IRON — " +
                                   "an Age 0 player there cannot build anything costing iron.");
                    bad++;
                }
                if (!AnyIn(veil, home))
                {
                    Debug.LogError($"[{MapName}] home territory {home} ({start.name}) has NO VEILSTONE.");
                    bad++;
                }
            }
            return bad;
        }

        private static bool AnyIn(MapMarker[] markers, int territory)
        {
            for (int i = 0; i < markers.Length; i++)
            {
                var p = markers[i].transform.position;
                if (TheWaningBorder.World.Regions.RegionMap.RegionAt(p.x, p.z) == territory)
                    return true;
            }
            return false;
        }
    }
}
