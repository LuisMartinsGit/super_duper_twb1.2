// TerrainDetailLayersPass.cs
// DRESSES Yiel Lymwerra as an arid, rocky coast (docs/Design/Maps/
// Yiel_Lymwerra.md § The look): chalk cliffs over a sand beach, dry cracked
// earth on the cliff tops with sparse scrub, and ROCK OUTCROPS where the
// layout says "forest".
//
// What it does, in order:
//   1. Gives the five layers the map builder paints (Grass, Meadow = sand,
//      Rock, Dirt, NoWalk) their arid textures, tints and tile sizes.
//   2. Adds three DETAIL layers and moves paint into them:
//        Hardpan  pale stone plates: broad patches in the dry earth and a
//                 skirt round every outcrop
//        Scrub    sparse dry vegetation, in small scattered patches
//        Seabed   the sea floor under water too deep to wade
//   3. Swaps every tree instance for a rock / boulder (same place, so the
//      NoWalk cell painted under it still fits).
//   4. Thins the terrain grass blades to the Scrub patches and dries them.
//   5. Puts a TerrainLook on the scene's Terrain (anti-tiling + colour drift).
//
// WALKABILITY IS NOT TOUCHED. PassabilityGrid blocks on the weight of the
// layer named "NoWalk" and nothing else, so this pass never writes that
// slice: it only moves weight between the walkable layers of a texel, and it
// refuses to save unless the NoWalk slice is identical. Outcrops (the old
// forests) and mountains are NoWalk at full weight; deep water blocks by
// depth (WaterPlane.WalkLine), not by paint.
//
// Re-runnable, and MapBuilderYielLymwerra runs it after every rebuild.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class TerrainDetailLayersPass
    {
        const string Folder = "Assets/GameData/Scenes/Maps/Yiel Lymwerra";
        const string DataPath = Folder + "/YielLymwerra TerrainData.asset";
        const string ScenePath = Folder + "/YielLymwerra.unity";
        const string Pack = "Assets/Game Buffs/Free Stylized Textures/Textures";
        // layout.py: WATER, WADE = 3.6, 0.5
        const float WalkLine = 3.1f;

        // Layers an earlier version of this pass made; folded back and deleted.
        static readonly string[] Retired = { "Undergrowth", "Wild Grass" };

        [MenuItem("Waning Border/Maps/Yiel Lymwerra: Dress Terrain (arid coast)")]
        public static void Run()
        {
            var report = new System.Text.StringBuilder();
            // The wade line comes from the scene's water when it is open
            // (the level is tuned there), else from layout.py's numbers.
            float walkLine = WalkLine;
            var water = Object.FindFirstObjectByType<TheWaningBorder.World.Terrain.WaterPlane>();
            if (water != null) walkLine = water.WalkLine;
            report.AppendLine($"wade line {walkLine:F2} m");
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(DataPath);
            if (data == null) { Finish(report.AppendLine("FAILED: no TerrainData at " + DataPath)); return; }

            // ── 1. The looks ────────────────────────────────────────────
            Layer("Grass", "Muddy_Cracked_Sand_6", 5.3f, new Color(0.74f, 0.60f, 0.47f));
            Layer("Meadow", "Rippled_Sand_2", 6.1f, new Color(0.97f, 0.99f, 1.00f));
            Layer("Rock", "Cracked_Concrete_26", 6.7f, new Color(1.00f, 1.00f, 1.00f));
            Layer("Dirt", "Sandy_Rock_Surface_17", 5.7f, new Color(0.80f, 0.69f, 0.58f));
            Layer("NoWalk", "Cracked_Concrete_26", 8.3f, new Color(0.90f, 0.88f, 0.84f));
            var hardpan = Layer("Hardpan", "Sandy_Rock_Surface_17", 4.6f, new Color(0.90f, 0.79f, 0.66f));
            var scrub = Layer("Scrub", "Grass_37", 3.7f, new Color(0.95f, 0.78f, 0.55f));
            var seabed = Layer("Seabed", "Underwater_Rock_Surface_3", 6.4f, Color.white);

            // ── 2. The paint ────────────────────────────────────────────
            var old = data.terrainLayers;
            int oldCount = data.alphamapLayers;
            int aw = data.alphamapWidth, ah = data.alphamapHeight;
            var src = data.GetAlphamaps(0, 0, aw, ah);

            int iGrass = Find(old, "Grass"), iSand = Find(old, "Meadow"), iNoWalk = MapGenKit.FindNoWalkLayer(data);
            if (iGrass < 0 || iSand < 0 || iNoWalk < 0)
            { Finish(report.AppendLine($"FAILED: base layers missing (Grass {iGrass}, Meadow {iSand}, NoWalk {iNoWalk})")); return; }

            // Which old layers fold back into which base layer.
            var foldTo = new int[oldCount];
            for (int l = 0; l < oldCount; l++)
            {
                foldTo[l] = -1;
                if (old[l] == null) { foldTo[l] = iGrass; continue; }
                string n = old[l].name;
                if (old[l] == hardpan || old[l] == scrub || System.Array.IndexOf(Retired, n) >= 0) foldTo[l] = iGrass;
                else if (old[l] == seabed) foldTo[l] = iSand;
            }

            var layers = new List<TerrainLayer>();
            var from = new List<int>();
            for (int l = 0; l < oldCount; l++)
                if (foldTo[l] < 0) { layers.Add(old[l]); from.Add(l); }
            int baseCount = layers.Count;
            int iHard = baseCount, iScrub = baseCount + 1, iSea = baseCount + 2;
            layers.Add(hardpan); layers.Add(scrub); layers.Add(seabed);
            int nGrass = from.IndexOf(iGrass), nSand = from.IndexOf(iSand), nNoWalk = from.IndexOf(iNoWalk);

            var map = new float[ah, aw, layers.Count];
            for (int y = 0; y < ah; y++)
                for (int x = 0; x < aw; x++)
                {
                    for (int l = 0; l < baseCount; l++) map[y, x, l] = src[y, x, from[l]];
                    for (int l = 0; l < oldCount; l++)
                        if (foldTo[l] >= 0) map[y, x, from.IndexOf(foldTo[l])] += src[y, x, l];
                }

            // How close each texel is to an outcrop: 1 at the rock, 0 a few metres out.
            var near = PropProximity(data, aw, ah, 6f);

            float sizeX = data.size.x, sizeZ = data.size.z;
            var scrubAt = new float[ah, aw];
            for (int y = 0; y < ah; y++)
            {
                float v = (y + 0.5f) / ah, wz = v * sizeZ;
                for (int x = 0; x < aw; x++)
                {
                    float u = (x + 0.5f) / aw, wx = u * sizeX;
                    float h = data.GetInterpolatedHeight(u, v);

                    // Sea floor: from the wade line down, fully seabed 0.8 m below it.
                    float deep = Mathf.InverseLerp(walkLine - 0.1f, walkLine - 0.9f, h);
                    deep *= 0.75f + 0.25f * Mathf.PerlinNoise(wx * 0.11f + 3.1f, wz * 0.11f + 8.4f);
                    float sand = map[y, x, nSand];
                    float toSea = sand * Mathf.Clamp01(deep);
                    map[y, x, nSand] = sand - toSea;
                    map[y, x, iSea] = toSea;

                    float earth = map[y, x, nGrass];

                    // Hardpan: broad plates through the dry earth (three
                    // octaves, so no stretch of ground is one texture for
                    // long), and a skirt round the outcrops.
                    float broad = Mathf.PerlinNoise(wx * 0.031f + 11.7f, wz * 0.031f + 3.3f) * 0.55f
                                + Mathf.PerlinNoise(wx * 0.093f + 5.2f, wz * 0.093f + 27.8f) * 0.30f
                                + Mathf.PerlinNoise(wx * 0.31f + 61.0f, wz * 0.31f + 9.4f) * 0.15f;
                    float plates = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.40f, 0.62f, broad)) * 0.8f;
                    float skirt = near[y, x] * (0.6f + 0.6f * Mathf.PerlinNoise(wx * 0.23f + 21.3f, wz * 0.23f + 5.7f));
                    float toHard = earth * Mathf.Clamp01(Mathf.Max(plates, skirt));
                    earth -= toHard;

                    // Scrub: SPARSE. Small patches where two noises agree,
                    // away from the bare plates.
                    float region = Mathf.PerlinNoise(wx * 0.024f + 40.1f, wz * 0.024f + 17.9f);
                    float clump = Mathf.PerlinNoise(wx * 0.21f + 2.6f, wz * 0.21f + 61.2f);
                    float grow = Mathf.InverseLerp(0.36f, 0.58f, region) * Mathf.InverseLerp(0.50f, 0.68f, clump);
                    float toScrub = earth * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(grow)) * 0.9f;
                    earth -= toScrub;

                    map[y, x, nGrass] = earth;
                    map[y, x, iHard] = toHard;
                    map[y, x, iScrub] = toScrub;
                    scrubAt[y, x] = toScrub;
                }
            }

            // THE CONTRACT: the NoWalk slice is exactly what it was.
            int differs = 0;
            for (int y = 0; y < ah; y++)
                for (int x = 0; x < aw; x++)
                    if (map[y, x, nNoWalk] != src[y, x, iNoWalk]) differs++;
            if (differs != 0) { Finish(report.AppendLine($"FAILED: {differs} NoWalk texels would change; nothing saved")); return; }

            Undo.RegisterCompleteObjectUndo(data, "Dress terrain");
            data.terrainLayers = layers.ToArray();
            data.SetAlphamaps(0, 0, map);

            // ── 3. Forests become rock outcrops ─────────────────────────
            report.AppendLine(TreesToRocks(data));

            // ── 4. Grass blades only in the scrub, and dry ──────────────
            report.AppendLine(ThinGrass(data, scrubAt, aw, ah));

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            foreach (var name in Retired)
                AssetDatabase.DeleteAsset(Folder + "/" + name + ".terrainlayer");

            // ── 5. The scene's Terrain gets its TerrainLook ─────────────
            report.AppendLine(BindLook());

            // Read back: what the game will load.
            var back = data.GetAlphamaps(0, 0, aw, ah);
            int blockedBefore = 0, blockedAfter = 0, flipped = 0;
            var share = new double[layers.Count];
            for (int y = 0; y < ah; y++)
                for (int x = 0; x < aw; x++)
                {
                    bool b0 = src[y, x, iNoWalk] >= 0.5f, b1 = back[y, x, nNoWalk] >= 0.5f;
                    if (b0) blockedBefore++;
                    if (b1) blockedAfter++;
                    if (b0 != b1) flipped++;
                    for (int l = 0; l < layers.Count; l++) share[l] += back[y, x, l];
                }
            report.AppendLine($"OK {aw}x{ah} alphamap, {layers.Count} layers");
            report.AppendLine($"NoWalk texels blocked before {blockedBefore}, after {blockedAfter}, flipped {flipped}");
            for (int l = 0; l < layers.Count; l++)
                report.AppendLine($"  {layers[l].name,-12} {share[l] / (aw * (double)ah) * 100.0:F1} %");
            Finish(report);
        }

        static void Finish(System.Text.StringBuilder report)
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/terrain-detail-layers.txt", report.ToString());
            Debug.Log("[TerrainDress] " + report);
        }

        static int Find(TerrainLayer[] layers, string name)
        {
            for (int i = 0; i < layers.Length; i++)
                if (layers[i] != null && layers[i].name == name) return i;
            return -1;
        }

        static TerrainLayer Layer(string name, string texture, float tile, Color tint)
        {
            string path = Folder + "/" + name + ".terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Pack}/{texture}/{texture}_Albedo.png");
            layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Pack}/{texture}/{texture}_Normal.png");
            layer.maskMapTexture = null;
            layer.tileSize = new Vector2(tile, tile);
            layer.diffuseRemapMin = Vector4.zero;
            layer.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f);
            layer.normalScale = 0.5f;
            layer.metallic = 0f;
            layer.smoothness = 0f;
            layer.specular = new Color(0f, 0f, 0f, 1f);
            EditorUtility.SetDirty(layer);
            return layer;
        }

        static bool IsStone(GameObject prefab) =>
            prefab != null && (prefab.name.Contains("Rock") || prefab.name.Contains("Boulder"));
        // Everything that is not a stone or a bush is a tree to replace.
        static bool IsTree(GameObject prefab) =>
            prefab != null && !IsStone(prefab) && !prefab.name.Contains("Bush");

        const string RockFolder = "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments";
        static readonly string[] RockPrefabs =
        {
            "SM_Env_Rock_01", "SM_Env_Rock_02", "SM_Env_Rock_03", "SM_Env_Rock_04",
            "SM_Env_Rock_Chunk_01", "SM_Env_Rock_Chunk_02", "SM_Env_Rock_Chunk_03",
        };

        static string TreesToRocks(TerrainData data)
        {
            var protos = data.treePrototypes;
            var names = new List<string>();
            foreach (var proto in protos) names.Add(proto.prefab != null ? proto.prefab.name : "(none)");
            // The outcrop stones join the terrain's prototypes once.
            var grown = new List<TreePrototype>(protos);
            foreach (var rock in RockPrefabs)
            {
                if (names.Contains(rock)) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{RockFolder}/{rock}.prefab");
                if (prefab != null) grown.Add(new TreePrototype { prefab = prefab, bendFactor = 0f });
            }
            if (grown.Count != protos.Length) { data.treePrototypes = grown.ToArray(); protos = data.treePrototypes; }
            var stones = new List<int>();
            for (int i = 0; i < protos.Length; i++) if (IsStone(protos[i].prefab)) stones.Add(i);
            if (stones.Count == 0) return "trees: no rock prototypes on the terrain, left as they are";

            // A stone is sized to fill its 2 m cell and a little more, so a
            // packed "forest" reads as one broken outcrop.
            var fit = new float[protos.Length];
            foreach (int i in stones)
            {
                float w = MapGenKit.MeasurePrefabWidth(protos[i].prefab);
                fit[i] = w > 0.01f ? 2.6f / w : 1f;
            }

            var instances = data.treeInstances;
            var rng = new System.Random(0x7E11);
            int swapped = 0;
            for (int i = 0; i < instances.Length; i++)
            {
                var t = instances[i];
                if (t.prototypeIndex < 0 || t.prototypeIndex >= protos.Length) continue;
                if (!IsTree(protos[t.prototypeIndex].prefab)) continue;
                int p = stones[rng.Next(stones.Count)];
                float s = fit[p] * (0.75f + (float)rng.NextDouble() * 0.9f);
                t.prototypeIndex = p;
                t.widthScale = s;
                t.heightScale = s * (0.8f + (float)rng.NextDouble() * 0.7f);
                t.rotation = (float)(rng.NextDouble() * System.Math.PI * 2.0);
                instances[i] = t;
                swapped++;
            }
            if (swapped > 0) data.SetTreeInstances(instances, false);
            return $"trees: {swapped} of {instances.Length} instances became rocks ({stones.Count} rock prototypes; had: {string.Join(", ", names)})";
        }

        static string ThinGrass(TerrainData data, float[,] scrubAt, int aw, int ah)
        {
            var protos = data.detailPrototypes;
            for (int i = 0; i < protos.Length; i++)
            {
                protos[i].healthyColor = new Color(0.78f, 0.70f, 0.42f, 1f);
                protos[i].dryColor = new Color(0.86f, 0.74f, 0.46f, 1f);
            }
            data.detailPrototypes = protos;

            int dw = data.detailWidth, dh = data.detailHeight, kept = 0, cut = 0;
            for (int l = 0; l < protos.Length; l++)
            {
                var d = data.GetDetailLayer(0, 0, dw, dh, l);
                for (int y = 0; y < dh; y++)
                    for (int x = 0; x < dw; x++)
                    {
                        if (d[y, x] == 0) continue;
                        float s = scrubAt[Mathf.Min(ah - 1, y * ah / dh), Mathf.Min(aw - 1, x * aw / dw)];
                        if (s < 0.3f) { d[y, x] = 0; cut++; } else kept++;
                    }
                data.SetDetailLayer(0, 0, l, d);
            }
            return $"grass blades: {kept} cells kept (in scrub), {cut} cleared";
        }

        static string BindLook()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "look: skipped (play mode)";
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrain = Object.FindFirstObjectByType<Terrain>();
            if (terrain == null) return "look: no Terrain in " + ScenePath;
            var look = terrain.GetComponent<TheWaningBorder.World.Terrain.TerrainLook>();
            if (look == null) look = terrain.gameObject.AddComponent<TheWaningBorder.World.Terrain.TerrainLook>();
            look.antiTile = 1f;
            look.antiTileCells = 0.45f;
            look.macroVariation = 0.13f;
            look.cliffProjection = true;
            // drift patches about 30 m across
            look.macroCells = new Vector2(terrain.terrainData.size.x / 30f, terrain.terrainData.size.z / 30f);
            look.Apply();
            EditorUtility.SetDirty(look);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return "look: TerrainLook set on '" + terrain.name + "'";
        }

        static float[,] PropProximity(TerrainData data, int aw, int ah, float radiusMetres)
        {
            var near = new float[ah, aw];
            float mx = data.size.x / aw, mz = data.size.z / ah;
            int rx = Mathf.CeilToInt(radiusMetres / mx), rz = Mathf.CeilToInt(radiusMetres / mz);
            foreach (var tree in data.treeInstances)
            {
                int cx = Mathf.Clamp(Mathf.FloorToInt(tree.position.x * aw), 0, aw - 1);
                int cy = Mathf.Clamp(Mathf.FloorToInt(tree.position.z * ah), 0, ah - 1);
                for (int dy = -rz; dy <= rz; dy++)
                {
                    int y = cy + dy; if (y < 0 || y >= ah) continue;
                    for (int dx = -rx; dx <= rx; dx++)
                    {
                        int x = cx + dx; if (x < 0 || x >= aw) continue;
                        float d = Mathf.Sqrt(dx * mx * dx * mx + dy * mz * dy * mz) / radiusMetres;
                        float w = 1f - Mathf.Clamp01(d);
                        if (w > near[y, x]) near[y, x] = w;
                    }
                }
            }
            return near;
        }
    }
}
