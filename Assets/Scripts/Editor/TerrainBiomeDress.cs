// TerrainBiomeDress.cs
// THE YIEL LYMWERRA KIT ON EVERY MAP, ONE BIOME EACH (2026-11-14, developer:
// "reskin all maps with the same kit as Yiel Lymwerra, but not the same
// biomes"). The kit is TerrainDetailLayersPass's: the stylized texture pack on
// the five layers the map builder paints, three DETAIL layers that break the
// ground up (broad patches, sparse clumps, the bed under deep water), dried or
// thinned grass blades, optionally rocks for trees, and a TerrainLook on the
// Terrain (anti-tiling, colour drift, cliff side-projection).
//
//   Fiendstone Pass   highland forest: needle litter, moss, grey cliffs
//   Moatwatch         green pasture: lush grass, clover, trodden earth
//   Highfold          snowfield: snow, tundra, ice sheets, cold rock
//   Broken Oath       autumn heath: rust grass, heather, pale clay
//   Trine Hollow      ash basin: cinder ground, ember cracks, basalt; its
//                     forests stand as rock outcrops
//
// THE V2 MAPS GET BIOMES OF THEIR OWN (2026-11-14, developer: "reskin the maps
// using the textures from the same pack as Yiel Lymwerra's. Make different
// biomes. Use trees and rocks"). Each v2 biome also names its TREE and ROCK
// palette and the share of the planted props that stand as rocks; the pass
// re-dresses every prop IN PLACE (same spot, same footprint width), so the
// NoWalk cells the builder painted under them are untouched. No v2 biome
// uses what the art direction reserves (Art_Direction.md §6.3): no dead or
// burnt trees (Blighted ground), no bones, no ember cracks, no purple or
// cyan crystal.
//   Moatwatch v2        wetland fen: olive grass, peat, mud; thin trees, bushes
//   Highfold v2         alpine pine highlands: pines, snow on the heights
//   Broken Oath v2      red-rock canyon: sand, sandstone; boulders, few trees
//   Trine Hollow v2     mossy boulder woods: deep green, big trees, boulders
//   Fiendstone Pass v2  autumn broadleaf valley: gold grass, red leaf litter
//
// WALKABILITY IS NOT TOUCHED: the NoWalk slice is never written and the pass
// refuses to save if it would differ (the same contract as the Yiel pass).
// Re-runnable; MapBuilderFromLayout runs it after every rebuild of a map that
// has a biome here. Reports to Temp/terrain-biome-<scene>.txt.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheWaningBorder.Core.Maps.EditorTools
{
    public static class TerrainBiomeDress
    {
        const string Pack = "Assets/Game Buffs/Free Stylized Textures/Textures";

        struct Look
        {
            public string name, texture; public float tile; public Color tint;
            public Look(string name, string texture, float tile, float r, float g, float b)
            { this.name = name; this.texture = texture; this.tile = tile; tint = new Color(r, g, b); }
        }

        sealed class Biome
        {
            public string mapFolder, sceneName;
            public Look grass, meadow, rock, dirt, noWalk;
            /// <summary>Broad patches through the open ground.</summary>
            public Look patches; public float patchAmount = 0.8f, patchFrom = 0.40f, patchTo = 0.62f;
            /// <summary>The patches also ring every tree / outcrop.</summary>
            public bool patchSkirt;
            /// <summary>Small scattered clumps.</summary>
            public Look clumps; public float clumpAmount = 0.9f, clumpFrom = 0.50f, clumpTo = 0.68f;
            public Look bed;
            public bool treesToRocks;
            /// <summary>Grass blades: 0 keep them all, 1 only in the clumps, 2 none.</summary>
            public int blades;
            public Color bladeHealthy, bladeDry;
            public float antiTileCells = 0.45f, macroVariation = 0.13f, driftMetres = 30f;
            public int seed;
            /// <summary>The biome's props (v2, 2026-11-14): tree and rock prefab
            /// names under the Synty environment folder. Null keeps the
            /// builder's mix. rockShare: the share of planted TREES that stand
            /// as rocks instead (the mountain scatter stays rock).</summary>
            public string[] trees, rocks;
            public float rockShare;

            /// <summary>This biome on another map (a v2 map wears its original's look).</summary>
            public Biome For(string folder, string scene)
            {
                var copy = (Biome)MemberwiseClone();
                copy.mapFolder = folder;
                copy.sceneName = scene;
                return copy;
            }
        }

        static readonly Biome[] Biomes =
        {
            new Biome
            {
                mapFolder = "Assets/GameData/Scenes/Maps/Fiendstone Pass", sceneName = "FiendstonePass", seed = 11,
                grass = new Look("Grass", "Forest_Ground_38", 5.9f, 0.58f, 0.70f, 0.60f),
                meadow = new Look("Meadow", "Grass_37", 4.7f, 0.52f, 0.74f, 0.50f),
                rock = new Look("Rock", "Cliff_Rock_Surface_21", 7.3f, 0.92f, 0.94f, 1.00f),
                dirt = new Look("Dirt", "Rocky_Dirt_2", 5.3f, 0.86f, 0.80f, 0.72f),
                noWalk = new Look("NoWalk", "Cliff_Rock_Surface_21", 8.9f, 0.78f, 0.80f, 0.86f),
                patches = new Look("Moss", "Grass_37", 3.9f, 0.42f, 0.58f, 0.36f), patchAmount = 0.75f,
                clumps = new Look("Scree", "Rocky_Dirt_2", 4.3f, 0.94f, 0.92f, 0.90f), clumpAmount = 0.7f,
                bed = new Look("Riverbed", "Underwater_Rock_Surface_3", 6.4f, 0.92f, 1.00f, 0.96f),
                blades = 0, bladeHealthy = new Color(0.36f, 0.58f, 0.26f), bladeDry = new Color(0.50f, 0.62f, 0.30f),
            },
            new Biome
            {
                mapFolder = "Assets/GameData/Scenes/Maps/Moatwatch", sceneName = "Moatwatch", seed = 23,
                grass = new Look("Grass", "Grass_37", 5.1f, 0.80f, 1.00f, 0.60f),
                meadow = new Look("Meadow", "Grass_37", 3.6f, 1.00f, 1.00f, 0.56f),
                rock = new Look("Rock", "Cliff_Rock_Surface_21", 6.9f, 1.00f, 0.97f, 0.90f),
                dirt = new Look("Dirt", "Rocky_Dirt_2", 5.5f, 0.92f, 0.82f, 0.66f),
                noWalk = new Look("NoWalk", "Cliff_Rock_Surface_21", 8.3f, 0.88f, 0.86f, 0.80f),
                patches = new Look("Clover", "Forest_Ground_38", 4.9f, 0.70f, 1.00f, 0.56f), patchAmount = 0.6f, patchFrom = 0.46f, patchTo = 0.66f,
                clumps = new Look("Trodden", "Rocky_Dirt_2", 3.7f, 0.96f, 0.86f, 0.68f), clumpAmount = 0.65f, clumpFrom = 0.56f, clumpTo = 0.72f,
                bed = new Look("Moatbed", "Underwater_Rock_Surface_3", 6.4f, 0.86f, 1.00f, 0.90f),
                blades = 0, bladeHealthy = new Color(0.42f, 0.72f, 0.24f), bladeDry = new Color(0.62f, 0.76f, 0.30f),
                macroVariation = 0.16f,
            },
            new Biome
            {
                mapFolder = "Assets/GameData/Scenes/Maps/Highfold", sceneName = "Highfold", seed = 37,
                grass = new Look("Grass", "Dirty_Snow_2", 6.3f, 1.00f, 1.00f, 1.00f),
                meadow = new Look("Meadow", "Arctic_Forest_Ground_12", 5.2f, 1.00f, 1.00f, 1.00f),
                rock = new Look("Rock", "Cliff_Rock_Surface_21", 7.1f, 0.84f, 0.90f, 1.00f),
                dirt = new Look("Dirt", "Rocky_Dirt_2", 5.6f, 0.78f, 0.82f, 0.90f),
                noWalk = new Look("NoWalk", "Cliff_Rock_Surface_21", 8.7f, 0.72f, 0.78f, 0.90f),
                patches = new Look("Tundra", "Arctic_Forest_Ground_12", 4.4f, 0.94f, 0.98f, 1.00f), patchAmount = 0.7f, patchSkirt = true,
                clumps = new Look("Ice", "Cracked_Ice_4", 5.0f, 0.92f, 0.98f, 1.00f), clumpAmount = 0.8f, clumpFrom = 0.54f, clumpTo = 0.70f,
                bed = new Look("Icebed", "Cracked_Ice_4", 7.2f, 0.70f, 0.86f, 0.96f),
                blades = 2, bladeHealthy = new Color(0.80f, 0.86f, 0.84f), bladeDry = new Color(0.88f, 0.90f, 0.88f),
                macroVariation = 0.08f,
            },
            new Biome
            {
                mapFolder = "Assets/GameData/Scenes/Maps/Broken Oath", sceneName = "BrokenOath", seed = 41,
                grass = new Look("Grass", "Grass_37", 5.4f, 1.00f, 0.50f, 0.20f),
                meadow = new Look("Meadow", "Grass_37", 3.8f, 1.00f, 0.64f, 0.24f),
                rock = new Look("Rock", "Sandy_Rock_Surface_17", 6.8f, 0.96f, 0.80f, 0.70f),
                dirt = new Look("Dirt", "Muddy_Cracked_Sand_6", 5.6f, 0.92f, 0.80f, 0.68f),
                noWalk = new Look("NoWalk", "Sandy_Rock_Surface_17", 8.4f, 0.80f, 0.66f, 0.58f),
                patches = new Look("Heather", "Forest_Ground_38", 4.6f, 1.00f, 0.42f, 0.44f), patchAmount = 0.7f,
                clumps = new Look("Clay", "Muddy_Cracked_Sand_6", 4.1f, 1.00f, 0.90f, 0.78f), clumpAmount = 0.75f, patchSkirt = true,
                bed = new Look("Tarnbed", "Underwater_Rock_Surface_3", 6.4f, 1.00f, 0.92f, 0.80f),
                blades = 0, bladeHealthy = new Color(0.78f, 0.52f, 0.20f), bladeDry = new Color(0.86f, 0.66f, 0.26f),
                macroVariation = 0.17f,
            },
            new Biome
            {
                mapFolder = "Assets/GameData/Scenes/Maps/Trine Hollow", sceneName = "TrineHollow", seed = 53,
                grass = new Look("Grass", "Rocky_Dirt_2", 5.8f, 0.56f, 0.52f, 0.52f),
                meadow = new Look("Meadow", "Forest_Ground_38", 4.9f, 0.62f, 0.60f, 0.50f),
                rock = new Look("Rock", "Cliff_Rock_Surface_21", 7.0f, 0.58f, 0.54f, 0.56f),
                dirt = new Look("Dirt", "Coal_Surface_2", 5.2f, 0.90f, 0.86f, 0.84f),
                noWalk = new Look("NoWalk", "Coal_Surface_2", 8.6f, 0.80f, 0.76f, 0.76f),
                patches = new Look("Cinder", "Coal_Surface_2", 4.7f, 1.00f, 0.94f, 0.90f), patchAmount = 0.75f, patchSkirt = true,
                clumps = new Look("Embers", "Lava_And_Pavement_2", 5.5f, 1.00f, 0.86f, 0.78f), clumpAmount = 0.8f, clumpFrom = 0.60f, clumpTo = 0.74f,
                bed = new Look("Sump", "Underwater_Rock_Surface_3", 6.4f, 0.60f, 0.60f, 0.66f),
                treesToRocks = true,
                blades = 1, bladeHealthy = new Color(0.40f, 0.36f, 0.30f), bladeDry = new Color(0.52f, 0.44f, 0.34f),
                macroVariation = 0.10f,
            },

            // ── The v2 maps' own biomes (2026-11-14) ──────────────────────
            new Biome
            {
                mapFolder = "Assets/GameData/Scenes/Maps/Moatwatch_v2", sceneName = "Moatwatch_v2", seed = 61,
                grass = new Look("Grass", "Grass_37", 5.3f, 0.50f, 0.62f, 0.30f),
                meadow = new Look("Meadow", "Forest_Ground_38", 4.4f, 0.56f, 0.62f, 0.36f),
                rock = new Look("Rock", "Cliff_Rock_Surface_21", 7.0f, 0.78f, 0.82f, 0.76f),
                dirt = new Look("Dirt", "Forest_Ground_38", 5.2f, 0.46f, 0.40f, 0.30f),
                noWalk = new Look("NoWalk", "Forest_Ground_38", 6.8f, 0.52f, 0.56f, 0.40f),
                patches = new Look("Peat", "Forest_Ground_38", 4.1f, 0.48f, 0.46f, 0.34f), patchAmount = 0.7f, patchSkirt = true,
                clumps = new Look("Mud", "Rocky_Dirt_2", 3.8f, 0.46f, 0.42f, 0.34f), clumpAmount = 0.65f, clumpFrom = 0.58f, clumpTo = 0.74f,
                bed = new Look("Fenbed", "Underwater_Rock_Surface_3", 6.0f, 0.70f, 0.80f, 0.62f),
                blades = 0, bladeHealthy = new Color(0.46f, 0.58f, 0.26f), bladeDry = new Color(0.62f, 0.62f, 0.32f),
                macroVariation = 0.15f,
                trees = new[] { "SM_Env_Tree_Thin_01", "SM_Env_Tree_Thin_02", "SM_Env_Tree_Thin_03", "SM_Env_Tree_Thin_04",
                                "SM_Env_Tree_Thin_05", "SM_Env_Tree_Thin_06", "SM_Env_Tree_Round_02", "SM_Env_Tree_Round_03" },
                rocks = new[] { "SM_Env_Bush_Cluster_01", "SM_Env_Bush_Cluster_03", "SM_Env_Bush_Cluster_05",
                                "SM_Env_Bush_04", "SM_Env_Bush_06", "SM_Env_Rock_Chunk_01", "SM_Env_Rock_Chunk_02" },
                rockShare = 0.35f,
            },
            new Biome
            {
                mapFolder = "Assets/GameData/Scenes/Maps/Highfold_v2", sceneName = "Highfold_v2", seed = 67,
                grass = new Look("Grass", "Arctic_Forest_Ground_12", 5.6f, 0.86f, 0.96f, 0.86f),
                meadow = new Look("Meadow", "Grass_37", 4.8f, 0.52f, 0.70f, 0.56f),
                rock = new Look("Rock", "Cliff_Rock_Surface_21", 7.2f, 0.86f, 0.90f, 0.98f),
                dirt = new Look("Dirt", "Rocky_Dirt_2", 5.4f, 0.84f, 0.84f, 0.86f),
                noWalk = new Look("NoWalk", "Dirty_Snow_2", 7.6f, 1.00f, 1.00f, 1.00f),
                patches = new Look("Heath", "Grass_37", 4.2f, 0.46f, 0.62f, 0.44f), patchAmount = 0.65f,
                clumps = new Look("Snowdrift", "Dirty_Snow_2", 4.6f, 1.00f, 1.00f, 1.00f), clumpAmount = 0.3f, clumpFrom = 0.68f, clumpTo = 0.80f,
                bed = new Look("Tarnbed", "Underwater_Rock_Surface_3", 6.4f, 0.80f, 0.90f, 1.00f),
                blades = 1, bladeHealthy = new Color(0.44f, 0.66f, 0.34f), bladeDry = new Color(0.60f, 0.70f, 0.42f),
                macroVariation = 0.12f,
                trees = new[] { "SM_Env_Tree_Pine_Cluster_01", "SM_Env_Tree_Pine_Cluster_02",
                                "SM_Env_Tree_Pine_Cluster_04", "SM_Env_Tree_Pine_Cluster_05" },
                rocks = new[] { "SM_Env_Rock_01", "SM_Env_Rock_02", "SM_Env_Rock_03", "SM_Env_Rock_04",
                                "SM_Env_Rock_Chunk_02", "SM_Env_Rock_Chunk_03" },
                rockShare = 0.25f,
            },
            new Biome
            {
                mapFolder = "Assets/GameData/Scenes/Maps/Broken Oath_v2", sceneName = "BrokenOath_v2", seed = 71,
                grass = new Look("Grass", "Rippled_Sand_2", 5.6f, 1.00f, 0.82f, 0.62f),
                meadow = new Look("Meadow", "Muddy_Cracked_Sand_6", 4.6f, 1.00f, 0.74f, 0.56f),
                rock = new Look("Rock", "Sandy_Rock_Surface_17", 6.6f, 1.00f, 0.70f, 0.54f),
                dirt = new Look("Dirt", "Rocky_Dirt_2", 5.2f, 1.00f, 0.76f, 0.60f),
                noWalk = new Look("NoWalk", "Sandy_Rock_Surface_17", 8.2f, 0.92f, 0.60f, 0.48f),
                patches = new Look("Redsand", "Muddy_Cracked_Sand_6", 4.3f, 1.00f, 0.64f, 0.46f), patchAmount = 0.7f, patchSkirt = true,
                clumps = new Look("Scrub", "Grass_37", 3.9f, 0.86f, 0.78f, 0.42f), clumpAmount = 0.6f, clumpFrom = 0.60f, clumpTo = 0.74f,
                bed = new Look("Wadibed", "Rippled_Sand_2", 6.2f, 0.92f, 0.78f, 0.62f),
                blades = 1, bladeHealthy = new Color(0.64f, 0.58f, 0.30f), bladeDry = new Color(0.80f, 0.66f, 0.36f),
                macroVariation = 0.18f,
                trees = new[] { "SM_Env_Tree_Round_01", "SM_Env_Tree_Round_04", "SM_Env_Tree_Thin_02" },
                rocks = new[] { "SM_Env_Rock_01", "SM_Env_Rock_02", "SM_Env_Rock_03", "SM_Env_Rock_04",
                                "SM_Env_Rock_Chunk_01", "SM_Env_Rock_Chunk_02", "SM_Env_Rock_Chunk_03",
                                "SM_Env_Rock_Cliff_01", "SM_Env_Rock_Cliff_03", "SM_Env_Bush_Small_02" },
                rockShare = 0.7f,
            },
            new Biome
            {
                mapFolder = "Assets/GameData/Scenes/Maps/Trine Hollow_v2", sceneName = "TrineHollow_v2", seed = 73,
                grass = new Look("Grass", "Forest_Ground_38", 5.4f, 0.62f, 0.78f, 0.50f),
                meadow = new Look("Meadow", "Grass_37", 4.4f, 0.48f, 0.72f, 0.42f),
                rock = new Look("Rock", "Cliff_Rock_Surface_21", 7.0f, 0.70f, 0.80f, 0.68f),
                dirt = new Look("Dirt", "Rocky_Dirt_2", 5.0f, 0.80f, 0.80f, 0.72f),
                noWalk = new Look("NoWalk", "Cliff_Rock_Surface_21", 8.4f, 0.64f, 0.74f, 0.60f),
                patches = new Look("Moss", "Grass_37", 3.7f, 0.40f, 0.62f, 0.34f), patchAmount = 0.75f, patchSkirt = true,
                clumps = new Look("Stones", "Rocky_Dirt_2", 4.4f, 0.86f, 0.86f, 0.80f), clumpAmount = 0.6f, clumpFrom = 0.60f, clumpTo = 0.74f,
                bed = new Look("Poolbed", "Underwater_Rock_Surface_3", 6.4f, 0.70f, 0.84f, 0.70f),
                blades = 0, bladeHealthy = new Color(0.34f, 0.56f, 0.26f), bladeDry = new Color(0.46f, 0.62f, 0.30f),
                macroVariation = 0.13f,
                trees = new[] { "SM_Env_Tree_Large_01", "SM_Env_Tree_Round_01", "SM_Env_Tree_Round_02", "SM_Env_Tree_Round_03" },
                rocks = new[] { "SM_Env_Rock_01", "SM_Env_Rock_02", "SM_Env_Rock_03", "SM_Env_Rock_04",
                                "SM_Env_Rock_Cliff_02", "SM_Env_Rock_Cliff_04", "SM_Env_Bush_Cluster_02" },
                rockShare = 0.45f,
            },
            new Biome
            {
                mapFolder = "Assets/GameData/Scenes/Maps/Fiendstone Pass_v2", sceneName = "FiendstonePass_v2", seed = 79,
                grass = new Look("Grass", "Forest_Ground_38", 5.5f, 1.00f, 0.80f, 0.52f),
                meadow = new Look("Meadow", "Grass_37", 4.6f, 1.00f, 0.80f, 0.32f),
                rock = new Look("Rock", "Cliff_Rock_Surface_21", 7.1f, 0.96f, 0.90f, 0.84f),
                dirt = new Look("Dirt", "Rocky_Dirt_2", 5.3f, 0.94f, 0.82f, 0.68f),
                noWalk = new Look("NoWalk", "Forest_Ground_38", 7.4f, 0.90f, 0.56f, 0.32f),
                patches = new Look("Leaves", "Forest_Ground_38", 3.9f, 1.00f, 0.56f, 0.30f), patchAmount = 0.75f, patchSkirt = true,
                clumps = new Look("Goldgrass", "Grass_37", 4.2f, 1.00f, 0.84f, 0.30f), clumpAmount = 0.6f, clumpFrom = 0.58f, clumpTo = 0.72f,
                bed = new Look("Riverbed", "Underwater_Rock_Surface_3", 6.4f, 0.96f, 0.92f, 0.80f),
                blades = 0, bladeHealthy = new Color(0.70f, 0.62f, 0.28f), bladeDry = new Color(0.86f, 0.62f, 0.28f),
                macroVariation = 0.16f,
                trees = new[] { "SM_Env_Tree_Round_01", "SM_Env_Tree_Round_02", "SM_Env_Tree_Round_03", "SM_Env_Tree_Round_04",
                                "SM_Env_Tree_Large_01", "SM_Env_Tree_Large_02", "SM_Env_Big_Tree_01" },
                rocks = new[] { "SM_Env_Rock_01", "SM_Env_Rock_03", "SM_Env_Rock_Chunk_01", "SM_Env_Rock_Chunk_03",
                                "SM_Env_Bush_Flowers_02", "SM_Env_Bush_Cluster_04" },
                rockShare = 0.2f,
            },
        };

        [MenuItem("Waning Border/Maps/Dress All Maps (biome per map)")]
        public static void RunAll()
        {
            foreach (var b in Biomes) Run(b);
        }

        [MenuItem("Waning Border/Maps/Dress Fiendstone Pass (highland forest)")]
        public static void RunFiendstone() => Run(Biomes[0]);
        [MenuItem("Waning Border/Maps/Dress Moatwatch (green pasture)")]
        public static void RunMoatwatch() => Run(Biomes[1]);
        [MenuItem("Waning Border/Maps/Dress Highfold (snowfield)")]
        public static void RunHighfold() => Run(Biomes[2]);
        [MenuItem("Waning Border/Maps/Dress Broken Oath (autumn heath)")]
        public static void RunBrokenOath() => Run(Biomes[3]);
        [MenuItem("Waning Border/Maps/Dress Trine Hollow (ash basin)")]
        public static void RunTrineHollow() => Run(Biomes[4]);
        [MenuItem("Waning Border/Maps/v2/Dress all v2 maps (their own biomes)")]
        public static void RunAllV2()
        {
            foreach (var b in Biomes) if (b.sceneName.EndsWith("_v2")) Run(b);
        }

        /// <summary>The map builder's hook: dress the map when it has a biome.</summary>
        public static void RunIfKnown(string sceneName)
        {
            foreach (var b in Biomes) if (b.sceneName == sceneName) { Run(b); return; }
            // A v2 map (2026-11-14, the half-size redesigns: scene "<Original>_v2"
            // in folder "<Original folder>_v2") wears its original's biome.
            const string V2 = "_v2";
            if (!sceneName.EndsWith(V2)) return;
            string original = sceneName.Substring(0, sceneName.Length - V2.Length);
            foreach (var b in Biomes)
                if (b.sceneName == original) { Run(b.For(b.mapFolder + V2, sceneName)); return; }
        }

        static void Run(Biome biome)
        {
            var report = new System.Text.StringBuilder(biome.sceneName + "\n");
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Finish(biome, report.AppendLine("FAILED: play mode")); return; }
            string dataPath = $"{biome.mapFolder}/{biome.sceneName} TerrainData.asset";
            string scenePath = $"{biome.mapFolder}/{biome.sceneName}.unity";
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
            if (data == null) { Finish(biome, report.AppendLine("FAILED: no TerrainData at " + dataPath)); return; }

            // The scene is opened first: the wade line is read off its water.
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != scenePath) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var water = Object.FindFirstObjectByType<TheWaningBorder.World.Terrain.WaterPlane>();
            bool hasWater = water != null;
            float walkLine = hasWater ? water.WalkLine : float.MinValue;
            report.AppendLine(hasWater ? $"wade line {walkLine:F2} m" : "no water in the scene");

            // ── 1. The looks ────────────────────────────────────────────
            var own = new Dictionary<string, TerrainLayer>
            {
                ["Grass"] = Layer(biome, biome.grass), ["Meadow"] = Layer(biome, biome.meadow),
                ["Rock"] = Layer(biome, biome.rock), ["Dirt"] = Layer(biome, biome.dirt),
                ["NoWalk"] = Layer(biome, biome.noWalk),
            };
            var patches = Layer(biome, biome.patches);
            var clumps = Layer(biome, biome.clumps);
            var bed = Layer(biome, biome.bed);

            // ── 2. The paint ────────────────────────────────────────────
            var old = data.terrainLayers;
            int oldCount = data.alphamapLayers;
            int aw = data.alphamapWidth, ah = data.alphamapHeight;
            var src = data.GetAlphamaps(0, 0, aw, ah);

            int iGrass = Find(old, "Grass"), iMeadow = Find(old, "Meadow"), iDirt = Find(old, "Dirt");
            int iNoWalk = MapGenKit.FindNoWalkLayer(data);
            // A map whose open ground is some other asset (Highfold's was a
            // stray demo "Sand" layer): the first layer that is none of the
            // named ones is the ground, and takes the map's own Grass below.
            if (iGrass < 0)
                for (int l = 0; l < old.Length && iGrass < 0; l++)
                    if (old[l] != null && l != iMeadow && l != iDirt && l != iNoWalk && l != Find(old, "Rock")
                        && old[l].name != biome.patches.name && old[l].name != biome.clumps.name && old[l].name != biome.bed.name)
                    { iGrass = l; report.AppendLine($"ground layer was '{old[l].name}' ({AssetDatabase.GetAssetPath(old[l])}); now the map's Grass"); }
            if (iGrass < 0 || iNoWalk < 0)
            {
                var have = new List<string>();
                foreach (var l in old) have.Add(l != null ? l.name + " @ " + AssetDatabase.GetAssetPath(l) : "(null)");
                Finish(biome, report.AppendLine($"FAILED: base layers missing (Grass {iGrass}, NoWalk {iNoWalk}); terrain has: {string.Join("; ", have)}"));
                return;
            }

            // A detail layer from an earlier run folds back into the grass.
            var foldTo = new int[oldCount];
            for (int l = 0; l < oldCount; l++)
            {
                foldTo[l] = -1;
                if (old[l] == null || old[l] == patches || old[l] == clumps || old[l] == bed) foldTo[l] = iGrass;
            }
            var layers = new List<TerrainLayer>();
            var from = new List<int>();
            for (int l = 0; l < oldCount; l++)
                if (foldTo[l] < 0) { layers.Add(old[l]); from.Add(l); }
            int baseCount = layers.Count;
            int iPatch = baseCount, iClump = baseCount + 1, iBed = baseCount + 2;
            layers.Add(patches); layers.Add(clumps); layers.Add(bed);
            int nGrass = from.IndexOf(iGrass), nNoWalk = from.IndexOf(iNoWalk);
            // Every base layer is the MAP'S OWN asset (the ones dressed above).
            layers[nGrass] = own["Grass"];
            for (int l = 0; l < baseCount; l++)
                if (layers[l] != null && l != nGrass && own.TryGetValue(layers[l].name, out var mine)) layers[l] = mine;
            int nMeadow = iMeadow >= 0 ? from.IndexOf(iMeadow) : -1, nDirt = iDirt >= 0 ? from.IndexOf(iDirt) : -1;

            var map = new float[ah, aw, layers.Count];
            for (int y = 0; y < ah; y++)
                for (int x = 0; x < aw; x++)
                {
                    for (int l = 0; l < baseCount; l++) map[y, x, l] = src[y, x, from[l]];
                    for (int l = 0; l < oldCount; l++)
                        if (foldTo[l] >= 0) map[y, x, nGrass] += src[y, x, l];
                }

            var near = biome.patchSkirt ? PropProximity(data, aw, ah, 6f) : null;
            float sizeX = data.size.x, sizeZ = data.size.z, o = biome.seed * 7.31f;
            var clumpAt = new float[ah, aw];
            for (int y = 0; y < ah; y++)
            {
                float v = (y + 0.5f) / ah, wz = v * sizeZ;
                for (int x = 0; x < aw; x++)
                {
                    float u = (x + 0.5f) / aw, wx = u * sizeX;

                    // The bed: every walkable ground layer gives way to it
                    // below the wade line, fully 0.8 m under.
                    if (hasWater)
                    {
                        float h = data.GetInterpolatedHeight(u, v);
                        float deep = Mathf.InverseLerp(walkLine - 0.1f, walkLine - 0.9f, h);
                        deep = Mathf.Clamp01(deep * (0.75f + 0.25f * Mathf.PerlinNoise(wx * 0.11f + 3.1f + o, wz * 0.11f + 8.4f)));
                        if (deep > 0f)
                        {
                            float taken = 0f;
                            taken += Take(map, y, x, nGrass, deep);
                            if (nMeadow >= 0) taken += Take(map, y, x, nMeadow, deep);
                            if (nDirt >= 0) taken += Take(map, y, x, nDirt, deep);
                            map[y, x, iBed] = taken;
                        }
                    }

                    float ground = map[y, x, nGrass];

                    // Broad patches, three octaves, so no stretch of ground
                    // is one texture for long.
                    float broad = Mathf.PerlinNoise(wx * 0.031f + 11.7f + o, wz * 0.031f + 3.3f) * 0.55f
                                + Mathf.PerlinNoise(wx * 0.093f + 5.2f, wz * 0.093f + 27.8f + o) * 0.30f
                                + Mathf.PerlinNoise(wx * 0.31f + 61.0f + o, wz * 0.31f + 9.4f) * 0.15f;
                    float plates = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(biome.patchFrom, biome.patchTo, broad)) * biome.patchAmount;
                    float skirt = near != null
                        ? near[y, x] * (0.6f + 0.6f * Mathf.PerlinNoise(wx * 0.23f + 21.3f, wz * 0.23f + 5.7f + o)) : 0f;
                    float toPatch = ground * Mathf.Clamp01(Mathf.Max(plates, skirt));
                    ground -= toPatch;

                    // Sparse clumps where two noises agree.
                    float region = Mathf.PerlinNoise(wx * 0.024f + 40.1f + o, wz * 0.024f + 17.9f);
                    float clump = Mathf.PerlinNoise(wx * 0.21f + 2.6f, wz * 0.21f + 61.2f + o);
                    float grow = Mathf.InverseLerp(0.36f, 0.58f, region) * Mathf.InverseLerp(biome.clumpFrom, biome.clumpTo, clump);
                    float toClump = ground * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(grow)) * biome.clumpAmount;
                    ground -= toClump;

                    map[y, x, nGrass] = ground;
                    map[y, x, iPatch] = toPatch;
                    map[y, x, iClump] = toClump;
                    clumpAt[y, x] = toClump;
                }
            }

            // THE CONTRACT: the NoWalk slice is exactly what it was.
            int differs = 0;
            for (int y = 0; y < ah; y++)
                for (int x = 0; x < aw; x++)
                    if (map[y, x, nNoWalk] != src[y, x, iNoWalk]) differs++;
            if (differs != 0) { Finish(biome, report.AppendLine($"FAILED: {differs} NoWalk texels would change; nothing saved")); return; }

            data.terrainLayers = layers.ToArray();
            data.SetAlphamaps(0, 0, map);

            if (biome.treesToRocks) report.AppendLine(TreesToRocks(data, biome.seed));
            if (biome.trees != null || biome.rocks != null) report.AppendLine(Reflora(data, biome));
            report.AppendLine(Blades(data, biome, clumpAt, aw, ah));

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            // ── The scene's Terrain gets its TerrainLook ────────────────
            var terrain = Object.FindFirstObjectByType<Terrain>();
            if (terrain == null) report.AppendLine("look: no Terrain in " + scenePath);
            else
            {
                var look = terrain.GetComponent<TheWaningBorder.World.Terrain.TerrainLook>();
                if (look == null) look = terrain.gameObject.AddComponent<TheWaningBorder.World.Terrain.TerrainLook>();
                look.antiTile = 1f;
                look.antiTileCells = biome.antiTileCells;
                look.macroVariation = biome.macroVariation;
                look.cliffProjection = true;
                look.macroCells = new Vector2(data.size.x / biome.driftMetres, data.size.z / biome.driftMetres);
                look.Apply();
                EditorUtility.SetDirty(look);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                report.AppendLine("look: TerrainLook set on '" + terrain.name + "'");
            }

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
                report.AppendLine($"  {(layers[l] != null ? layers[l].name : "(null)"),-12} {share[l] / (aw * (double)ah) * 100.0:F1} %");
            Finish(biome, report);
        }

        /// <summary>Moves <paramref name="share"/> of one layer's weight out; returns what left.</summary>
        static float Take(float[,,] map, int y, int x, int layer, float share)
        {
            float w = map[y, x, layer] * share;
            map[y, x, layer] -= w;
            return w;
        }

        static void Finish(Biome biome, System.Text.StringBuilder report)
        {
            Directory.CreateDirectory("Temp");
            File.WriteAllText($"Temp/terrain-biome-{biome.sceneName}.txt", report.ToString());
            Debug.Log("[TerrainBiome] " + report);
        }

        static int Find(TerrainLayer[] layers, string name)
        {
            for (int i = 0; i < layers.Length; i++)
                if (layers[i] != null && layers[i].name == name) return i;
            return -1;
        }

        static TerrainLayer Layer(Biome biome, Look look)
        {
            string path = biome.mapFolder + "/" + look.name + ".terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Pack}/{look.texture}/{look.texture}_Albedo.png");
            layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Pack}/{look.texture}/{look.texture}_Normal.png");
            if (layer.diffuseTexture == null) Debug.LogError($"[TerrainBiome] no texture {look.texture} for layer {look.name}");
            layer.maskMapTexture = null;
            layer.tileSize = new Vector2(look.tile, look.tile);
            layer.diffuseRemapMin = Vector4.zero;
            layer.diffuseRemapMax = new Vector4(look.tint.r, look.tint.g, look.tint.b, 1f);
            layer.normalScale = 0.5f;
            layer.metallic = 0f;
            layer.smoothness = 0f;
            layer.specular = new Color(0f, 0f, 0f, 1f);
            EditorUtility.SetDirty(layer);
            return layer;
        }

        static bool IsStone(GameObject prefab) =>
            prefab != null && (prefab.name.Contains("Rock") || prefab.name.Contains("Boulder"));
        static bool IsTree(GameObject prefab) =>
            prefab != null && !IsStone(prefab) && !prefab.name.Contains("Bush");

        const string RockFolder = "Assets/Synty/PolygonFantasyKingdom/Prefabs/Environments";
        static readonly string[] RockPrefabs =
        {
            "SM_Env_Rock_01", "SM_Env_Rock_02", "SM_Env_Rock_03", "SM_Env_Rock_04",
            "SM_Env_Rock_Chunk_01", "SM_Env_Rock_Chunk_02", "SM_Env_Rock_Chunk_03",
        };

        /// <summary>The biome's props (v2 maps): every planted prop is re-dressed
        /// IN PLACE from the biome's palettes — a tree becomes one of its trees,
        /// or (rockShare of them) one of its rocks; a rock stays a rock. The
        /// world width stays what the builder gave it, so the NoWalk cell under
        /// it stays honest; height keeps the builder's height-to-width ratio.</summary>
        static string Reflora(TerrainData data, Biome biome)
        {
            var protos = new List<TreePrototype>(data.treePrototypes);
            int Ensure(string name)
            {
                for (int i = 0; i < protos.Count; i++)
                    if (protos[i].prefab != null && protos[i].prefab.name == name) return i;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{RockFolder}/{name}.prefab");
                if (prefab == null) return -1;
                protos.Add(new TreePrototype { prefab = prefab, bendFactor = IsStone(prefab) ? 0f : 0.2f });
                return protos.Count - 1;
            }
            var treeIdx = new List<int>();
            var rockIdx = new List<int>();
            if (biome.trees != null) foreach (var n in biome.trees) { int i = Ensure(n); if (i >= 0) treeIdx.Add(i); }
            if (biome.rocks != null) foreach (var n in biome.rocks) { int i = Ensure(n); if (i >= 0) rockIdx.Add(i); }
            data.treePrototypes = protos.ToArray();
            var all = data.treePrototypes;
            var width = new float[all.Length];
            for (int i = 0; i < all.Length; i++)
            {
                float w = all[i].prefab != null ? MapGenKit.MeasurePrefabWidth(all[i].prefab) : 0f;
                width[i] = w > 0.01f ? w : 1f;
            }

            var rng = new System.Random(0x2B10 + biome.seed);
            var inst = data.treeInstances;
            int toTree = 0, toRock = 0;
            for (int i = 0; i < inst.Length; i++)
            {
                var t = inst[i];
                if (t.prototypeIndex < 0 || t.prototypeIndex >= all.Length) continue;
                bool wasStone = !IsTree(all[t.prototypeIndex].prefab);
                bool rock = wasStone || rng.NextDouble() < biome.rockShare;
                var pool = rock ? rockIdx : treeIdx;
                if (pool.Count == 0) continue;
                int p = pool[rng.Next(pool.Count)];
                float worldW = width[t.prototypeIndex] * t.widthScale;
                float ratio = t.widthScale > 1e-4f ? t.heightScale / t.widthScale : 1f;
                float s = worldW / width[p];
                if (rock && !wasStone) { ratio = 0.7f + (float)rng.NextDouble() * 0.6f; }
                t.prototypeIndex = p;
                t.widthScale = s;
                t.heightScale = s * ratio;
                t.rotation = (float)(rng.NextDouble() * System.Math.PI * 2.0);
                inst[i] = t;
                if (rock) toRock++; else toTree++;
            }
            data.SetTreeInstances(inst, false);
            return $"props: {toTree} trees and {toRock} rocks from the biome's palettes (of {inst.Length})";
        }

        static string TreesToRocks(TerrainData data, int seed)
        {
            var protos = data.treePrototypes;
            var names = new List<string>();
            foreach (var proto in protos) names.Add(proto.prefab != null ? proto.prefab.name : "(none)");
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

            var fit = new float[protos.Length];
            foreach (int i in stones)
            {
                float w = MapGenKit.MeasurePrefabWidth(protos[i].prefab);
                fit[i] = w > 0.01f ? 2.6f / w : 1f;
            }
            var instances = data.treeInstances;
            var rng = new System.Random(0x7E11 + seed);
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
            return $"trees: {swapped} of {instances.Length} instances became rocks";
        }

        static string Blades(TerrainData data, Biome biome, float[,] clumpAt, int aw, int ah)
        {
            var protos = data.detailPrototypes;
            for (int i = 0; i < protos.Length; i++)
            {
                protos[i].healthyColor = biome.bladeHealthy;
                protos[i].dryColor = biome.bladeDry;
            }
            data.detailPrototypes = protos;
            if (biome.blades == 0) return "grass blades: kept, recoloured";

            int dw = data.detailWidth, dh = data.detailHeight, kept = 0, cut = 0;
            for (int l = 0; l < protos.Length; l++)
            {
                var d = data.GetDetailLayer(0, 0, dw, dh, l);
                for (int y = 0; y < dh; y++)
                    for (int x = 0; x < dw; x++)
                    {
                        if (d[y, x] == 0) continue;
                        bool keep = biome.blades == 1
                            && clumpAt[Mathf.Min(ah - 1, y * ah / dh), Mathf.Min(aw - 1, x * aw / dw)] >= 0.3f;
                        if (!keep) { d[y, x] = 0; cut++; } else kept++;
                    }
                data.SetDetailLayer(0, 0, l, d);
            }
            return $"grass blades: {kept} cells kept, {cut} cleared";
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
