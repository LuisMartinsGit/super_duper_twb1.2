// Age0KitAuthor.cs — the second half of FeraldisKitAuthor (2026-10-13):
//
//   * every remaining AGE 0 building's Lv0 rebuilt from the Synty kit in the
//     culture-NEUTRAL survivors' idiom the Shelter set (stock palette, timber
//     and thatch, rough stone), parts grouped 1_..N_ for the construction
//     rise, on the constant-slab rule (see BuildFeraldisFortress):
//       Barracks, Hut, Gatherer's Hut, Temple of Ridan — the Lv0 branch of
//         their existing multi-variant prefab (the Hut's and Barracks's Lv0
//         live inside an imported FBX node, which is unpacked so the old
//         meshes can go; the Alanthor branches are untouched)
//       Mine, Veilstone Mine — NEW variant prefabs on their SOs (they drew the
//         procedural MineVisual pithead; an SO prefab with a Lv0 supersedes it
//         in PresentationSpawnSystem)
//   * the Feraldis forms those buildings lacked in the variant path: the Hut's
//     Slave Pens and the Mine's levels move INTO the prefabs (the per-level
//     ladder prefabs on the level SOs are cleared), the Veilstone Mine gets one
//     Feraldis form (no level SOs exist — flagged)
//   * the two LANDMARKS, one finished monumental form each, no levels:
//       Fiendstone Keep (Feraldis)  — a black-stone forge-citadel
//       Vault of Almiérra (Alanthor) — a fortified treasury-cathedral in the
//         castle kit the rest of the Alanthor set uses
//     Both drew procedural placeholders (FiendstoneKeepVisual,
//     CreateProceduralVault); an SO prefab with a Lv0 supersedes those too.
//
// Run through Waning Border > Art > Author Feraldis Kit Art (AuthorAll) or the
// two menus below; previews: Render Age 0 Comparisons.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static partial class FeraldisKitAuthor
    {
        const string C_WallWinTall = "Castle/SM_Bld_Castle_Wall_Window_Fancy_Tall_01";
        const string C_DoorBig = "Castle/SM_Bld_Castle_Wall_Door_Big_01";
        const string C_Archway = "Castle/SM_Bld_Castle_Wall_Archway_01";
        const string C_CornerS = "Castle/SM_Bld_Castle_Wall_Corner_S_01";
        const string C_CrownS = "Castle/SM_Bld_Castle_Battlements_S_Corner_01";
        const string C_FloorRoundS = "Castle/SM_Bld_Castle_Floor_Stone_Round_S_01";
        const string C_RoofL = "Castle/SM_Bld_Castle_Roof_L_01";
        const string C_RoofLEnd = "Castle/SM_Bld_Castle_Roof_L_End_01";
        const string C_ConeRoof = "Castle/SM_Bld_Castle_Roof_Cap_Round_05";
        const string C_OnionDome = "Castle/SM_Bld_Castle_Roof_Cap_Round_06";
        const string C_PillarStoneRound = "Castle/SM_Bld_Castle_Pillar_Stone_Round_01";
        const string P_StatueKing = "Props/SM_Prop_Statue_King_01";
        const string P_StatueAngel = "Props/SM_Prop_Statue_Angel_01";
        const string P_StatuePlinth = "Props/SM_Prop_Statue_Plinth_02";
        const string P_Candles = "Props/SM_Prop_Candle_Melted_Group_01";
        const string P_Bell = "Props/SM_Prop_Bell_01";
        const string P_OreGold = "Props/SM_Prop_Ore_Gold_01";
        const string P_Chest = "Props/Furniture/SM_Prop_Chest_03";
        const string P_StreetLamp = "Props/SM_Prop_Street_Lamp_02";
        const string P_HayBale = "Props/SM_Prop_Hay_Bale_01";
        const string P_Basket = "Props/SM_Prop_Basket_01";
        const string P_BracketLamp = "Props/SM_Prop_Bracket_Lamp_01";
        const string I_Crystal3 = "Items/SM_Item_Crystal_03";
        const string I_Crystal5 = "Items/SM_Item_Crystal_05";
        const string I_Ingot = "Items/SM_Item_Ingot_Gold_01";
        const string AlGoldMatPath = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/VaultOfAlmierra/Materials/Al_Gold.mat";

        static Material _ferVeilstone, _ferEmber, _alGold;

        // ═════════════════════════════════════════════════════════════════
        // Menus
        // ═════════════════════════════════════════════════════════════════

        [MenuItem("Waning Border/Art/Author Age 0 Kit Art")]
        public static void AuthorAge0()
        {
            Report.Clear(); _failures = 0;
            try { EnsureMaterials(); EnsureIdentityMaterials(); Age0Buildings(); Landmarks(); }
            catch (Exception e) { Fail("AuthorAge0", e.ToString()); }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string text = $"[Age0Kit] done — {Report.Count} entries, {_failures} failure(s)\n" + string.Join("\n", Report);
            Debug.Log(text);
            try { File.WriteAllText("Temp/age0-kit-report.txt", text); } catch (Exception) { }
        }

        static void EnsureExtraMaterials()
        {
            _ferVeilstone = AssetDatabase.LoadAssetAtPath<Material>(FerMatDir + "Fer_Veilstone.mat");
            _ferEmber = AssetDatabase.LoadAssetAtPath<Material>(FerMatDir + "Fer_Ember.mat");
            if (_ferVeilstone == null || _ferEmber == null) throw new Exception("Fer_Veilstone / Fer_Ember palette materials missing");
            var shader = Shader.Find(BuildingLitShader);
            Directory.CreateDirectory(Path.GetDirectoryName(AlGoldMatPath));
            _alGold = AssetDatabase.LoadAssetAtPath<Material>(AlGoldMatPath);
            if (_alGold == null) { _alGold = new Material(shader) { name = "Al_Gold" }; AssetDatabase.CreateAsset(_alGold, AlGoldMatPath); }
            _alGold.SetColor("_BaseColor", new Color(0.86f, 0.66f, 0.22f));
            _alGold.SetFloat("_Smoothness", 0.3f);
            _alGold.SetFloat("_Metallic", 0.3f);
            EditorUtility.SetDirty(_alGold);
        }

        static void Age0Buildings()
        {
            EnsureExtraMaterials();
            Lv0InPlace(TT + "Age0/Buildings/Barracks/Barracks.asset", "Barracks", BuildAge0Barracks, null);
            // The Hut stays on the LEGACY LADDER: Hut.asset referenced the
            // single-mesh Hut.fbx (no variant layout), so Alanthor's levels come
            // from the House_Lvl1..3 prefabs and Feraldis's from SlavePens_1..3
            // through BuildingPrefabSwapSystem.ResolvePrefab. A variant prefab
            // would make the in-place path win and hide Alanthor's House art,
            // so the Age 0 form is a PLAIN prefab (numbered groups, no Lv0
            // node) and the ladders are left exactly as they are.
            PlainAge0(TT + "Age0/Buildings/Hut/Hut.asset", TT + "Age0/Buildings/Hut/HutAge0.prefab", "Hut", BuildAge0Hut);
            Lv0InPlace(TT + "Age0/Buildings/GatherersHut/GatherersHut.asset", "GatherersHut", BuildAge0GatherersHut, null);
            Lv0InPlace(TT + "Age0/Buildings/TempleOfRidan/TempleOfRidan.asset", "TempleOfRidan", BuildAge0Temple, null);
            NewVariant(TT + "Age0/Buildings/Mine/Mine.asset", TT + "Age0/Buildings/Mine/Mine.prefab", "Mine",
                       lv => BuildAge0Mine(lv, false), (lv, l) => BuildMine(lv, l), 3);
            NewVariant(TT + "Age0/Buildings/VeilstoneMine/VeilstoneMine.asset", TT + "Age0/Buildings/VeilstoneMine/VeilstoneMine.prefab", "VeilstoneMine",
                       lv => BuildAge0Mine(lv, true), (lv, l) => BuildVeilstoneMineFeraldis(lv), 1);
            Report.Add("NOTE VeilstoneMine: no Feraldis level SOs exist (Civs/Feraldis/Buildings has no VeilstoneMine/), so it carries ONE Feraldis form (Feraldis/Lv1) — levels are a data/design decision");

            // The Mine's Feraldis levels live in its variant prefab now: the
            // per-level ladder prefabs on the Mine level SOs go. (The Slave
            // Pens ladder on the Hut level SOs stays — see the Hut note above.)
            for (int l = 1; l <= 3; l++)
            {
                ClearLevelPrefab(FER + $"Mine/Mine_Lvl{l}.asset");
                string p = FER + $"Mine/Art/Mine_{l}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(p) != null && AssetDatabase.DeleteAsset(p)) Report.Add("OK removed ladder prefab " + p);
            }
        }

        static void Landmarks()
        {
            EnsureExtraMaterials();
            NewVariant(TT + "Age0/Buildings/FiendstoneKeep/FiendstoneKeep.asset", TT + "Age0/Buildings/FiendstoneKeep/FiendstoneKeep.prefab", "FiendstoneKeep",
                       BuildFiendstoneKeep, null, 0);
            NewVariant(TT + "Age0/Buildings/VaultOfAlmierra/VaultOfAlmierra.asset", TT + "Age0/Buildings/VaultOfAlmierra/VaultOfAlmierra.prefab", "VaultOfAlmierra",
                       BuildVaultOfAlmierra, null, 0);
        }

        // ═════════════════════════════════════════════════════════════════
        // Paths
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Rebuild the Lv0 branch of an Age 0 building's existing variant prefab
        /// (and, with <paramref name="feraldis"/>, its Feraldis/Lv1..3). The Lv0
        /// container may be an imported FBX node (x100, -90 deg): the nested
        /// instance is unpacked so its old Lv0 meshes can be removed, and the
        /// new groups are put back in the prefab's own frame. Alanthor / Runai
        /// branches are not touched.
        /// </summary>
        static void Lv0InPlace(string soPath, string stem, Action<Transform> buildLv0, Action<Transform, int> feraldis)
        {
            try
            {
                string prefabPath = SoPrefabPath(soPath);
                if (string.IsNullOrEmpty(prefabPath)) throw new Exception("SO has no prefab: " + soPath);
                bool rewire = false;
                if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    // The SO references the imported MODEL itself (the Hut's
                    // Hut.fbx): a model cannot be edited, so the sibling
                    // <Stem>.prefab that nests the variants model is rebuilt and
                    // the SO is pointed at it.
                    string sibling = Path.GetDirectoryName(soPath).Replace('\\', '/') + "/" + stem + ".prefab";
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(sibling) == null) throw new Exception($"SO prefab is a model ({prefabPath}) and no {sibling} exists");
                    Report.Add($"NOTE {stem}: SO referenced the model {prefabPath}; rebuilt {sibling} and re-pointed the SO at it");
                    prefabPath = sibling;
                    rewire = true;
                }
                var contents = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    Transform container = FindContainerWithLv0(contents.transform, 2);
                    if (container == null) throw new Exception("no Lv0 container in " + prefabPath);
                    if (PrefabUtility.IsPartOfPrefabInstance(container.gameObject))
                    {
                        var instRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(container.gameObject);
                        if (instRoot != null && instRoot != contents)
                        {
                            PrefabUtility.UnpackPrefabInstance(instRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                            Report.Add($"OK {stem}: unpacked the imported model node so its Lv0 could be rebuilt (Alanthor meshes kept as they were)");
                        }
                    }
                    Transform lv0 = FindChild(container, "Lv0");
                    Clear(lv0);
                    ResetToPrefabFrame(lv0, contents.transform);
                    lv0.gameObject.SetActive(true);
                    buildLv0(lv0);
                    CheckReach(stem, 0, lv0);
                    if (feraldis != null)
                    {
                        Transform fer = FindChild(container, "Feraldis") ?? Group(container, "Feraldis");
                        Clear(fer);
                        for (int l = 1; l <= 3; l++)
                        {
                            var lv = Group(fer, "Lv" + l);
                            ResetToPrefabFrame(lv, contents.transform);
                            feraldis(lv, l);
                            CheckReach(stem, l, lv);
                        }
                        fer.gameObject.SetActive(true);
                    }
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabPath, out bool ok);
                    if (!ok) throw new Exception("SaveAsPrefabAsset failed for " + prefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                if (rewire) WireBuildingSo(soPath, AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
                Report.Add($"OK {stem}: Lv0 rebuilt from the kit{(feraldis != null ? " + Feraldis/Lv1..3" : "")} in {prefabPath}");
            }
            catch (Exception e) { Fail(stem, e.ToString()); }
        }

        /// <summary>A PLAIN prefab (no variant layout — numbered groups at the
        /// root) for a building that must stay on the legacy per-level ladder.</summary>
        static void PlainAge0(string soPath, string prefabPath, string stem, Action<Transform> build)
        {
            try
            {
                var root = new GameObject(stem);
                build(root.transform);
                CheckReach(stem, 0, root.transform);
                var prefab = Save(root, prefabPath);
                WireBuildingSo(soPath, prefab);
                Report.Add($"OK {stem}: plain Age 0 prefab -> {prefabPath}; SO prefab wired (levels stay on the ladder)");
            }
            catch (Exception e) { Fail(stem, e.ToString()); }
        }

        /// <summary>A NEW variant prefab for a building that had none (its SO
        /// prefab was null and it drew a procedural visual): Lv0 + Feraldis/Lv1..N
        /// (N = 0 for a landmark's single form), empty Runai / Alanthor branches,
        /// wired to the SO. Rebuilt in place when the file already exists.</summary>
        static void NewVariant(string soPath, string prefabPath, string stem, Action<Transform> buildLv0, Action<Transform, int> feraldis, int levels)
        {
            try
            {
                var root = new GameObject(stem);
                var lv0 = Group(root.transform, "Lv0");
                buildLv0(lv0);
                CheckReach(stem, 0, lv0);
                Group(root.transform, "Runai");
                Group(root.transform, "Alanthor");
                var fer = Group(root.transform, "Feraldis");
                for (int l = 1; l <= levels; l++) { var lv = Group(fer, "Lv" + l); feraldis(lv, l); CheckReach(stem, l, lv); }
                var prefab = Save(root, prefabPath);
                WireBuildingSo(soPath, prefab);
                Report.Add($"OK {stem}: variant prefab (Lv0{(levels > 0 ? $" + Feraldis/Lv1..{levels}" : ", single form")}) -> {prefabPath}; SO prefab wired");
            }
            catch (Exception e) { Fail(stem, e.ToString()); }
        }

        // ═════════════════════════════════════════════════════════════════
        // Neutral kit helpers
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// A survivors' thatched hall in the stock palette: nx x nz modules of
        /// 2.5 m (door on the south face), corner posts, gables on +-X and the
        /// kit thatch at 45 deg with ridge caps. <paramref name="walls"/> false
        /// gives an open canopy on posts. Walls go under <paramref name="wallsParent"/>,
        /// the roof under <paramref name="roofParent"/> (two rise groups).
        /// Returns the ridge height.
        /// </summary>
        static float ThatchHall(Transform wallsParent, Transform roofParent, float cx, float cz, int nx, int nz, float baseY,
                                bool walls = true, bool door = true, float postHeight = 0f)
        {
            var S = Style.Neutral;
            float ex = nx * W * 0.5f, ez = nz * W * 0.5f;
            float top = baseY + (walls ? W : postHeight);
            string[] Row(int n, string mid, string centre = null)
            {
                var r = new string[n];
                for (int i = 0; i < n; i++) r[i] = mid;
                if (centre != null) r[n / 2] = centre;
                return r;
            }
            if (walls)
            {
                Ring(wallsParent, "Wall", cx, cz, ex, ez, baseY,
                     Row(nx, H_WallBeams, door ? H_WallDoor : H_WallWin), Row(nz, H_Wall), Row(nx, H_WallWin), Row(nz, H_WallBeams), S);
            }
            foreach (var (px, pz) in new[] { (-ex, -ez), (ex, -ez), (ex, ez), (-ex, ez) })
                Put(wallsParent, H_Beam, "Post", new Vector3(cx + px, baseY, cz + pz), 0f, new Vector3(1.2f, (top - baseY) / W, 1.2f), S);
            // Thatch at 45 deg: the piece drops 2.68 over 2.56 m from the ridge.
            float sAcross = (ez + 0.2f) / 2.56f;
            float rise = 2.68f * sAcross;
            float ridgeY = top + rise;
            Gable(roofParent, "GableW", new Vector3(cx - ex, top, cz), ez, rise, 90f, S);
            Gable(roofParent, "GableE", new Vector3(cx + ex, top, cz), ez, rise, -90f, S);
            for (int i = 0; i < nx; i++)
            {
                float x0 = cx - ex + i * W, x1 = x0 + W;
                // yaw -90: drops toward -Z (south), runs +X from its pivot; yaw 90: drops toward +Z, runs -X.
                Put(roofParent, H_Thatch, "ThatchS", new Vector3(x0, ridgeY, cz), -90f, new Vector3(sAcross, sAcross, 1f), S);
                Put(roofParent, H_Thatch, "ThatchN", new Vector3(x1, ridgeY, cz), 90f, new Vector3(sAcross, sAcross, 1f), S);
                Put(roofParent, H_ThatchCap, "Cap", new Vector3(x1, ridgeY, cz), 90f, new Vector3(1f, 1f, 1f), S);
            }
            return ridgeY;
        }

        /// <summary>A neutral cobbled patch (2.5 m stone tiles) covering nx x nz modules.</summary>
        static void Cobbles(Transform parent, float cx, float cz, int nx, int nz)
        {
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                    Put(parent, H_FloorStone, "Cobbles", new Vector3(cx - nx * W * 0.5f + (i + 1) * W, 0.2f, cz - nz * W * 0.5f + (j + 1) * W), 0f, null, Style.Neutral);
        }

        // ═════════════════════════════════════════════════════════════════
        // Age 0 Lv0 forms
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Barracks (8 m): a thatched drill hall, weapon racks, straw targets, a rail fence.</summary>
        static void BuildAge0Barracks(Transform lv0)
        {
            var S = Style.Neutral;
            var g1 = Group(lv0, "1_Ground"); Yard(g1, 8f, 0.2f, S); Cobbles(g1, 0f, 0.8f, 2, 2);
            var g2 = Group(lv0, "2_Walls"); var g3 = Group(lv0, "3_Roof");
            ThatchHall(g2, g3, 0f, 0.8f, 2, 2, 0.2f);
            var g4 = Group(lv0, "4_Yard");
            Put(g4, B_WeaponRack, "RackA", new Vector3(-3.2f, 0.2f, -2.6f), 20f, Vector3.one * 1.0f, S);
            Put(g4, B_WeaponRack, "RackB", new Vector3(3.2f, 0.2f, -2.6f), -20f, Vector3.one * 1.0f, S);
            Put(g4, B_Target, "TargetA", new Vector3(3.6f, 0.2f, 1.2f), -90f, Vector3.one * 1.3f, S);
            Put(g4, B_Target, "TargetB", new Vector3(3.6f, 0.2f, 3.2f), -90f, Vector3.one * 1.3f, S);
            Put(g4, B_Dummy, "Dummy", new Vector3(-3.4f, 0.2f, 1.4f), 70f, Vector3.one * 1.0f, S);
            Put(g4, P_Fence, "FenceA", new Vector3(-2.6f, 0.65f, -4.0f), 0f, null, S);
            Put(g4, P_Fence, "FenceB", new Vector3(2.6f, 0.65f, -4.0f), 0f, null, S);
            Put(g4, P_CrateStack, "Crates", new Vector3(-3.4f, 0.2f, 3.4f), 30f, Vector3.one * 0.7f, S);
            Put(g4, B_Firepit, "Firepit", new Vector3(0f, 0.3f, -3.0f), 0f, Vector3.one * 1.1f, S);
            var g5 = Group(lv0, "5_Pennant");
            Banner(g5, "PlayerColor_Banner", new Vector3(0f, 0.2f, -4.0f), 0f, 1.3f, _age0Cloth);
        }

        /// <summary>Hut (4 m): one thatched room, a woodpile, a water barrel.</summary>
        static void BuildAge0Hut(Transform lv0)
        {
            var S = Style.Neutral;
            var g1 = Group(lv0, "1_Ground"); Yard(g1, 4f, 0.2f, S);
            var g2 = Group(lv0, "2_Walls"); var g3 = Group(lv0, "3_Roof");
            ThatchHall(g2, g3, -0.3f, 0.3f, 1, 1, 0.2f);
            var g4 = Group(lv0, "4_Props");
            Put(g4, P_WoodPile, "Woodpile", new Vector3(1.6f, 0.2f, -1.2f), 80f, Vector3.one * 0.8f, S);
            Put(g4, P_Basket, "Basket", new Vector3(-1.7f, 0.2f, -1.4f), 0f, Vector3.one * 1.3f, S);
            Put(g4, P_Fence, "Fence", new Vector3(0.9f, 0.65f, 1.95f), 0f, new Vector3(0.8f, 0.8f, 1f), S);
            var g5 = Group(lv0, "5_Pennant");
            Banner(g5, "PlayerColor_Banner", new Vector3(1.6f, 0.2f, 0.6f), 0f, 0.9f, _age0Cloth);
        }

        /// <summary>Gatherer's Hut (4 m): a thatched store room, a drying lean-to, hay, sacks and baskets.</summary>
        static void BuildAge0GatherersHut(Transform lv0)
        {
            var S = Style.Neutral;
            var g1 = Group(lv0, "1_Ground"); Yard(g1, 4f, 0.2f, S);
            var g2 = Group(lv0, "2_Walls"); var g3 = Group(lv0, "3_Roof");
            ThatchHall(g2, g3, -0.5f, 0.5f, 1, 1, 0.2f);
            var g4 = Group(lv0, "4_Store");
            Put(g4, H_Thatch, "LeanTo", new Vector3(0.8f, 2.4f, 1.5f), 0f, new Vector3(0.55f, 0.4f, 0.8f), S);
            Put(g4, H_Beam, "LeanPostA", new Vector3(2.0f, 0.2f, 1.4f), 0f, new Vector3(1f, 0.5f, 1f), S);
            Put(g4, H_Beam, "LeanPostB", new Vector3(2.0f, 0.2f, -0.4f), 0f, new Vector3(1f, 0.5f, 1f), S);
            Put(g4, P_HayBale, "Hay", new Vector3(1.5f, 0.74f, 0.4f), 20f, Vector3.one * 0.9f, S);
            Put(g4, P_SackStack, "Sacks", new Vector3(-1.4f, 0.2f, -1.5f), 30f, Vector3.one * 0.8f, S);
            Put(g4, P_Basket, "BasketA", new Vector3(0.6f, 0.2f, -1.6f), 0f, Vector3.one * 1.3f, S);
            Put(g4, P_Basket, "BasketB", new Vector3(1.2f, 0.2f, -1.5f), 40f, Vector3.one * 1.1f, S);
            var g5 = Group(lv0, "5_Pennant");
            Banner(g5, "PlayerColor_Banner", new Vector3(-1.8f, 0.2f, -1.4f), 0f, 0.9f, _age0Cloth);
        }

        /// <summary>Temple of Ridan (8 m): a cobbled precinct, a thatched canopy on stone pillars over a
        /// crystal shrine, candles, a bell, standing stones.</summary>
        static void BuildAge0Temple(Transform lv0)
        {
            var S = Style.Neutral;
            var g1 = Group(lv0, "1_Ground"); Yard(g1, 8f, 0.2f, S); Cobbles(g1, 0f, 0f, 2, 2);
            var g2 = Group(lv0, "2_Pillars");
            foreach (var (px, pz) in new[] { (-2.5f, -2.5f), (2.5f, -2.5f), (2.5f, 2.5f), (-2.5f, 2.5f) })
                Put(g2, C_PillarStoneRound, "Pillar", new Vector3(px, 0.2f, pz), 0f, new Vector3(0.45f, 0.76f, 0.45f), S);
            Put(g2, P_StatuePlinth, "Plinth", new Vector3(0f, 0.38f, 0.6f), 0f, Vector3.one * 1.2f, S);
            var g3 = Group(lv0, "3_Roof");
            ThatchHall(g2, g3, 0f, 0f, 2, 2, 0.2f, walls: false, postHeight: 3.8f);
            var g4 = Group(lv0, "4_Shrine");
            Put(g4, I_Crystal3, "Crystal", new Vector3(0f, 2.4f, 0.6f), 20f, Vector3.one * 4.5f, S);
            Put(g4, I_Crystal5, "CrystalB", new Vector3(0.5f, 2.4f, 0.9f), -30f, Vector3.one * 2.5f, S);
            Put(g4, P_Candles, "CandlesA", new Vector3(-1.2f, 0.25f, -1.4f), 0f, Vector3.one * 2.2f, S);
            Put(g4, P_Candles, "CandlesB", new Vector3(1.3f, 0.25f, -1.2f), 50f, Vector3.one * 2.2f, S);
            Put(g4, P_Candles, "CandlesC", new Vector3(0f, 0.25f, -2.0f), 90f, Vector3.one * 1.8f, S);
            Put(g4, H_Beam, "BellPostA", new Vector3(3.6f, 0.2f, 2.4f), 0f, new Vector3(1f, 1.1f, 1f), S);
            Put(g4, H_Beam, "BellPostB", new Vector3(3.6f, 0.2f, 0.6f), 0f, new Vector3(1f, 1.1f, 1f), S);
            Put(g4, C_PanelWood, "BellBeam", new Vector3(3.45f, 2.9f, 2.5f), 90f, new Vector3(0.38f, 0.4f, 1f), S);
            Put(g4, P_Bell, "Bell", new Vector3(3.6f, 2.8f, 1.5f), 90f, Vector3.one * 0.8f, S);
            Put(g4, E_Rock, "StoneA", new Vector3(-3.4f, 0.2f, 2.6f), 20f, new Vector3(3f, 7f, 3f), S);
            Put(g4, E_Rock, "StoneB", new Vector3(-3.6f, 0.2f, -2.6f), -40f, new Vector3(2.6f, 5.5f, 2.6f), S);
            Put(g4, P_Torch, "TorchA", new Vector3(-2.5f, 2.0f, -2.75f), 0f, Vector3.one * 1.2f, S);
            Put(g4, P_Torch, "TorchB", new Vector3(2.5f, 2.0f, -2.75f), 0f, Vector3.one * 1.2f, S);
            var g5 = Group(lv0, "5_Pennant");
            Banner(g5, "PlayerColor_Banner", new Vector3(3.6f, 0.2f, -3.2f), 0f, 1.3f, _age0Cloth);
        }

        /// <summary>The Age 0 mine (4 m): a stone adit under a two-post pithead, ore (or veilstone) heaped at its foot.</summary>
        static void BuildAge0Mine(Transform lv0, bool veilstone)
        {
            var S = Style.Neutral;
            var g1 = Group(lv0, "1_Ground"); Yard(g1, 4f, 0.2f, S);
            var g2 = Group(lv0, "2_Adit");
            Put(g2, C_Block, "Adit", new Vector3(0f, 0.2f, 0.9f), 0f, new Vector3(2.6f / 5.5f, 1.9f / 5f, 1.8f / 5.5f), S);
            Put(g2, C_DoorSingle, "AditMouth", new Vector3(0.8f, 0.25f, -0.05f), 0f, new Vector3(1.55f, 0.6f, 1f), S);
            var g3 = Group(lv0, "3_Headframe");
            Put(g3, H_Beam, "HeadPostA", new Vector3(-1.5f, 0.2f, -0.4f), 0f, new Vector3(1.2f, 1.25f, 1.2f), S);
            Put(g3, H_Beam, "HeadPostB", new Vector3(1.5f, 0.2f, -0.4f), 0f, new Vector3(1.2f, 1.25f, 1.2f), S);
            Put(g3, C_PanelWood, "HeadBeam", new Vector3(1.7f, 3.1f, -0.55f), 0f, new Vector3(0.68f, 0.4f, 1f), S);
            Put(g3, P_ChainDrop, "Hoist", new Vector3(0f, 3.2f, -0.4f), 0f, Vector3.one * 0.9f, S);
            var g4 = Group(lv0, "4_Props");
            Put(g4, P_Crate, "Crate", new Vector3(-1.6f, 0.2f, 1.4f), 20f, Vector3.one * 0.7f, S);
            Put(g4, V_Cart, "Cart", new Vector3(0.3f, 0.2f, -1.5f), 90f, Vector3.one * 0.45f, S);
            for (int i = 0; i < 4; i++)
            {
                var pos = new Vector3(1.2f + (i % 2) * 0.4f, 0.2f, 0.9f + (i / 2) * 0.5f);
                if (veilstone) Put(g4, i % 2 == 0 ? I_Crystal3 : I_Crystal5, "Veilstone", pos, i * 50f, Vector3.one * 3.6f, S, null, _ferVeilstone);
                else Put(g4, P_OreIron, "Ore", pos, i * 50f, Vector3.one * 4f, S);
            }
            Put(g4, P_Torch, "Torch", new Vector3(-1.1f, 1.6f, -0.05f), 0f, Vector3.one * 1.0f, S);
            var g5 = Group(lv0, "5_Pennant");
            Banner(g5, "PlayerColor_Banner", new Vector3(-1.8f, 0.2f, -1.6f), 0f, 0.9f, _age0Cloth);
        }

        // ═════════════════════════════════════════════════════════════════
        // The landmarks — one monumental form each
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// A round castle-kit tower (four quarter pieces turned about a shared
        /// centre, crown of battlements, round floor cap, optional slate cone)
        /// at <paramref name="scale"/> of the kit: radius 2.75 x scale. Returns
        /// the top height. The Alanthor idiom — used by the Vault only.
        /// </summary>
        static float StoneTower(Transform parent, string tag, Vector3 centre, int storeys, float scale, bool cone, Style style = Style.Neutral)
        {
            var root = Group(parent, tag);
            root.localPosition = centre;
            root.localScale = Vector3.one * scale;
            const float M5 = 5f;
            float y = 0f;
            for (int s = 0; s < storeys; s++, y += M5)
                for (int k = 0; k < 4; k++)
                {
                    var rot = Quaternion.Euler(0f, k * 90f, 0f);
                    Put(root, C_CornerS, "Storey", -(rot * new Vector3(-2.5f, 0f, 0f)) + new Vector3(0f, y, 0f), k * 90f, null, style);
                }
            for (int k = 0; k < 4; k++)
            {
                var rot = Quaternion.Euler(0f, k * 90f, 0f);
                Put(root, C_FloorRoundS, "Floor", new Vector3(0f, y, 0f), k * 90f, null, style);
                Put(root, C_CrownS, "Crown", -(rot * new Vector3(-2.5f, 0f, 0f)) + new Vector3(0f, y, 0f), k * 90f, null, style);
            }
            float top = y + 1.38f;
            if (cone)
            {
                float k2 = (2.75f * 2f * 0.95f) / 2.28f;
                Put(root, C_ConeRoof, "Cone", new Vector3(0f, top - 1.0f, 0f), 0f, new Vector3(k2, (2.75f * 2.4f) / 6.87f, k2), style);
                top += 2.75f * 2.4f - 1.0f;
            }
            return centre.y + top * scale;
        }

        /// <summary>
        /// The Vault of Almiérra (Alanthor, 8 m): a fortified treasury-cathedral
        /// in the castle kit — pale stone, slate roofs (player-tinted like the
        /// rest of the Alanthor set), gold. A nave of tall traceried windows
        /// under a steep slate roof, two round towers with cones at the front,
        /// the crossing tower behind under a gilded onion dome, the vault door
        /// between angel statues, lamps and gold at the steps. Parts grouped
        /// 1_..6_ for the construction rise; banners are "Stripe_" parts.
        /// </summary>
        static void BuildVaultOfAlmierra(Transform lv)
        {
            var S = Style.Neutral;
            const float P = 0.6f;
            const float K = 0.75f;                 // kit 5 m pieces at 0.75 = 3.75 m
            float hx = 3.75f, hz = 2.5f;           // nave 7.5 x 5
            var g1 = Group(lv, "1_Plinth");
            Put(g1, C_Block, "Plinth", Vector3.zero, 0f, new Vector3(9f / 5.5f, P / 5f, 9f / 5.5f), S);
            Put(g1, C_Block, "Step", new Vector3(0f, 0f, -4.05f), 0f, new Vector3(4f / 5.5f, 0.3f / 5f, 0.8f / 5.5f), S);

            // 2 — the nave walls: tall traceried windows all round, the vault
            // door in the south face. Kit walls are 5 m pieces with the pivot at
            // +X running -X and the outside on -Z (Span's convention).
            var g2 = Group(lv, "2_Walls");
            float wallH = 10f * 0.56f;            // 5.6 m
            void Face(string tag, Vector3 a, Vector3 b)
            {
                Vector3 d = (a - b).normalized;
                float yaw = Mathf.Atan2(d.z, -d.x) * Mathf.Rad2Deg;
                float len = Vector3.Distance(a, b);
                int n = Mathf.Max(1, Mathf.RoundToInt(len / 3.75f));
                for (int i = 0; i < n; i++)
                    Put(g2, C_WallWinTall, tag + Letter(i), b + (a - b) * (i / (float)n), yaw, new Vector3(len / n / 5f, 0.56f, 1f), S);
            }
            Face("S", new Vector3(-hx, P, -hz), new Vector3(hx, P, -hz));
            Face("W", new Vector3(-hx, P, hz), new Vector3(-hx, P, -hz));
            Face("N", new Vector3(hx, P, hz), new Vector3(-hx, P, hz));
            Face("E", new Vector3(hx, P, -hz), new Vector3(hx, P, hz));
            Put(g2, C_DoorBig, "VaultDoor", new Vector3(1.8f, P, -hz - 0.3f), 0f, new Vector3(0.72f, 0.72f, 1f), S);
            Put(g2, C_Block, "Core", Vector3.zero + new Vector3(0f, P, 0f), 0f, new Vector3((2f * hx - 0.3f) / 5.5f, wallH / 5f, (2f * hz - 0.3f) / 5.5f), S);

            // 3 — the two front towers.
            var g3 = Group(lv, "3_Towers");
            float ts = 0.52f;
            StoneTower(g3, "TowerW", new Vector3(-3.05f, P, -2.55f), 2, ts, cone: true);
            StoneTower(g3, "TowerE", new Vector3(3.05f, P, -2.55f), 2, ts, cone: true);

            // 4 — the nave roof: slate at 56 deg, gable trims, a lantern ridge.
            var g4 = Group(lv, "4_Roof");
            float top = P + wallH;
            float sy = 0.75f;                     // 3.75 m rise over 2.5 m
            Put(g4, C_RoofL, "SlopeS", new Vector3(hx + 0.2f, top - 0.1f, -hz - 0.2f), 180f, new Vector3((2f * hx + 0.4f) / 5f, sy, (hz + 0.2f) / 5f), S);
            Put(g4, C_RoofL, "SlopeN", new Vector3(-hx - 0.2f, top - 0.1f, hz + 0.2f), 0f, new Vector3((2f * hx + 0.4f) / 5f, sy, (hz + 0.2f) / 5f), S);
            Put(g4, C_RoofLEnd, "TrimSW", new Vector3(-hx - 0.2f, top - 0.1f, hz + 0.2f), 0f, new Vector3(1f, sy, (hz + 0.2f) / 5f), S);
            Put(g4, C_RoofLEnd, "TrimSE", new Vector3(hx + 0.2f, top - 0.1f, hz + 0.2f), 0f, new Vector3(1f, sy, (hz + 0.2f) / 5f), S);
            Put(g4, C_RoofLEnd, "TrimNE", new Vector3(hx + 0.2f, top - 0.1f, -hz - 0.2f), 180f, new Vector3(1f, sy, (hz + 0.2f) / 5f), S);
            Put(g4, C_RoofLEnd, "TrimNW", new Vector3(-hx - 0.2f, top - 0.1f, -hz - 0.2f), 180f, new Vector3(1f, sy, (hz + 0.2f) / 5f), S);

            // 5 — the crossing tower and its gilded dome.
            var g5 = Group(lv, "5_Dome");
            float drumTop = StoneTower(g5, "Crossing", new Vector3(0f, P, 1.4f), 3, 0.62f, cone: false);
            Put(g5, C_OnionDome, "Dome", new Vector3(0f, drumTop - 0.3f, 1.4f), 0f, new Vector3(1.05f, 0.9f, 1.05f), S, null, _alGold);
            Put(g5, C_OnionDome, "FinialW", new Vector3(-3.05f, P + 9.2f, -2.55f), 0f, new Vector3(0.25f, 0.25f, 0.25f), S, null, _alGold);
            Put(g5, C_OnionDome, "FinialE", new Vector3(3.05f, P + 9.2f, -2.55f), 0f, new Vector3(0.25f, 0.25f, 0.25f), S, null, _alGold);

            // 6 — statues, lamps, gold and the banners (Stripe_ = player colour).
            var g6 = Group(lv, "6_Props");
            Put(g6, P_StatueAngel, "AngelW", new Vector3(-2.2f, P, -3.6f), 20f, Vector3.one * 0.9f, S);
            Put(g6, P_StatueAngel, "AngelE", new Vector3(2.2f, P, -3.6f), -20f, Vector3.one * 0.9f, S);
            Put(g6, P_StatueKing, "King", new Vector3(0f, P, 3.9f), 180f, Vector3.one * 1.3f, S);
            Put(g6, P_StreetLamp, "LampW", new Vector3(-1.2f, P + 0.25f, -4.1f), 0f, Vector3.one * 1.0f, S);
            Put(g6, P_StreetLamp, "LampE", new Vector3(1.2f, P + 0.25f, -4.1f), 0f, Vector3.one * 1.0f, S);
            Put(g6, P_OreGold, "GoldA", new Vector3(-0.8f, P, -3.4f), 0f, Vector3.one * 3.5f, S);
            Put(g6, P_OreGold, "GoldB", new Vector3(0.9f, P, -3.5f), 60f, Vector3.one * 3f, S);
            Put(g6, P_Chest, "ChestA", new Vector3(3.9f, P, 1.2f), -90f, Vector3.one * 1.3f, S);
            Put(g6, P_Chest, "ChestB", new Vector3(-3.9f, P, 1.2f), 90f, Vector3.one * 1.3f, S);
            Put(g6, I_Ingot, "IngotA", new Vector3(3.9f, P + 0.9f, 1.2f), 20f, Vector3.one * 3f, S);
            Put(g6, I_Ingot, "IngotB", new Vector3(-3.9f, P + 0.9f, 1.2f), -30f, Vector3.one * 3f, S);
            Put(g6, BN_Battle, "Stripe_BannerW", new Vector3(-3.05f, P + 5.6f, -4.0f), 0f, Vector3.one * 1.4f, S);
            Put(g6, BN_Battle, "Stripe_BannerE", new Vector3(3.05f, P + 5.6f, -4.0f), 0f, Vector3.one * 1.4f, S);
            Put(g6, BN_FlagH, "Stripe_NaveCloth", new Vector3(1.4f, top - 1.0f, -hz - 0.35f), 0f, new Vector3(0.7f, 0.55f, 0.7f), S);
            Put(g6, P_BracketLamp, "SconceW", new Vector3(-2.9f, P + 3.2f, -hz - 0.3f), 0f, Vector3.one * 1.2f, S);
            Put(g6, P_BracketLamp, "SconceE", new Vector3(2.9f, P + 3.2f, -hz - 0.3f), 0f, Vector3.one * 1.2f, S);
        }

        // ═════════════════════════════════════════════════════════════════
        // Comparisons: Age 0 Lv0 | Feraldis levels, the Alanthor branches, the landmarks
        // ═════════════════════════════════════════════════════════════════

        [MenuItem("Waning Border/Art/Render Age 0 Comparisons")]
        public static void RenderAge0Comparisons()
        {
            string outDir = Path.GetFullPath(PreviewDir);
            Directory.CreateDirectory(outDir);
            var report = new List<string>();
            void Row(string name, string soPath, string prefabPath, int lv0, byte culture, int levels, string suffix = "", float dist = 37.5f, float tilt = 47f)
            {
                try
                {
                    var cols = new List<Func<GameObject>>();
                    if (lv0 >= 0) cols.Add(() => ShownVariant(prefabPath, Cultures.None, 0));
                    for (int l = 1; l <= levels; l++) { int ll = l; cols.Add(() => ShownVariant(prefabPath, culture, ll)); }
                    report.Add(RenderColumns(name + suffix, outDir, FootprintOf(soPath), cols, dist, tilt));
                }
                catch (Exception e) { report.Add($"FAIL {name}{suffix}: {e.Message}"); }
            }
            string fortressSo = TT + "Age0/Buildings/Fortress/Fortress.asset", barracksSo = BarracksSoPath,
                   hutSo = TT + "Age0/Buildings/Hut/Hut.asset", ghSo = TT + "Age0/Buildings/GatherersHut/GatherersHut.asset",
                   templeSo = TT + "Age0/Buildings/TempleOfRidan/TempleOfRidan.asset", mineSo = TT + "Age0/Buildings/Mine/Mine.asset",
                   vmineSo = TT + "Age0/Buildings/VeilstoneMine/VeilstoneMine.asset", keepSo = TT + "Age0/Buildings/FiendstoneKeep/FiendstoneKeep.asset",
                   vaultSo = TT + "Age0/Buildings/VaultOfAlmierra/VaultOfAlmierra.asset";
            // Age 0 Lv0 | Feraldis Lv1..N
            Row("Barracks", barracksSo, SoPrefabPath(barracksSo), 0, Cultures.Feraldis, 3);
            try
            {
                var hutCols = new List<Func<GameObject>> { () => Instance(SoPrefabPath(hutSo)) };
                for (int l = 1; l <= 3; l++) { string lp = SoPrefabPath(FER + $"SlavePens/SlavePens_Lvl{l}.asset"); if (lp != null) hutCols.Add(() => Instance(lp)); }
                report.Add(RenderColumns("Hut", outDir, FootprintOf(hutSo), hutCols, 37.5f, 47f));
                var alCols = new List<Func<GameObject>> { () => Instance(SoPrefabPath(hutSo)) };
                for (int l = 1; l <= 3; l++) { string lp = SoPrefabPath(TT + $"Civs/Alanthor/Buildings/House/House_Lvl{l}.asset"); if (lp != null) alCols.Add(() => Instance(lp)); }
                report.Add(RenderColumns("Hut_Alanthor", outDir, FootprintOf(hutSo), alCols, 37.5f, 47f));
            }
            catch (Exception e) { report.Add("FAIL Hut: " + e.Message); }
            Row("GatherersHut", ghSo, SoPrefabPath(ghSo), 0, Cultures.Feraldis, 3);
            Row("TempleOfRidan", templeSo, SoPrefabPath(templeSo), 0, Cultures.Feraldis, 1);
            Row("Mine", mineSo, SoPrefabPath(mineSo), 0, Cultures.Feraldis, 3);
            Row("VeilstoneMine", vmineSo, SoPrefabPath(vmineSo), 0, Cultures.Feraldis, 1);
            Row("Fortress", fortressSo, SoPrefabPath(fortressSo), 0, Cultures.Feraldis, 3);
            // The Alanthor branches as they stand (not changed) — the fit question.
            Row("Fortress", fortressSo, SoPrefabPath(fortressSo), 0, Cultures.Alanthor, 3, "_Alanthor");
            Row("Barracks", barracksSo, SoPrefabPath(barracksSo), 0, Cultures.Alanthor, 3, "_Alanthor");
            Row("GatherersHut", ghSo, SoPrefabPath(ghSo), 0, Cultures.Alanthor, 3, "_Alanthor");
            // The landmarks: in-game camera and the max zoom-out (zoom 80, tilt 70).
            Row("FiendstoneKeep", keepSo, SoPrefabPath(keepSo), 0, Cultures.None, 0);
            Row("FiendstoneKeep", keepSo, SoPrefabPath(keepSo), 0, Cultures.None, 0, "_far", 80f, 70f);
            Row("VaultOfAlmierra", vaultSo, SoPrefabPath(vaultSo), 0, Cultures.None, 0);
            Row("VaultOfAlmierra", vaultSo, SoPrefabPath(vaultSo), 0, Cultures.None, 0, "_far", 80f, 70f);
            string text = "[Age0Kit] comparisons\n" + string.Join("\n", report);
            Debug.Log(text);
            try { File.WriteAllText(Path.Combine(outDir, "age0_compare_report.txt"), text); } catch (Exception) { }
        }

        static GameObject ShownVariant(string prefabPath, byte culture, int level)
        {
            var go = Instance(prefabPath);
            var v = TheWaningBorder.Rendering.BuildingVariantVisual.TrySetup(go);
            if (v == null) throw new Exception("no variant layout in " + prefabPath);
            if (level > 0) v.ShowVariant(culture, level);
            return go;
        }

        /// <summary>Any number of columns side by side, each fitted to the footprint, from the given camera.</summary>
        static string RenderColumns(string name, string outDir, Vector2 footprint, List<Func<GameObject>> makers, float dist, float tilt)
        {
            var created = new List<GameObject>();
            var stage = new Vector3(0f, -4000f, 0f);
            try
            {
                float spacing = Mathf.Max(footprint.x, footprint.y) + 4f;
                var sizes = new List<string>();
                int n = makers.Count;
                for (int i = 0; i < n; i++)
                {
                    var go = makers[i]();
                    created.Add(go);
                    float fit = PresentationSpawnSystem.ComputeFootprintFitForSize(go, footprint.x, footprint.y, out Vector3 offset);
                    go.transform.localScale = Vector3.one * fit;
                    go.transform.position = stage + new Vector3((i - (n - 1) * 0.5f) * spacing, 0f, 0f) - new Vector3(offset.x, 0f, offset.z) * fit;
                    TheWaningBorder.Rendering.BuildingFactionColorMarker.Apply(go, new Color(0.85f, 0.15f, 0.15f));
                    if (MeshBounds(go, out var b)) sizes.Add($"col{i} fit {fit:F2} -> {b.size.x:F1}x{b.size.y:F1}x{b.size.z:F1}");
                }
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.hideFlags = HideFlags.HideAndDontSave;
                created.Add(ground);
                ground.transform.position = stage + new Vector3(0f, -0.02f, 0f);
                ground.transform.localScale = new Vector3(24f, 1f, 24f);
                var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                gm.SetColor("_BaseColor", new Color(0.30f, 0.40f, 0.28f));
                gm.SetFloat("_Smoothness", 0.05f);
                ground.GetComponent<Renderer>().sharedMaterial = gm;
                UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());

                var camGo = new GameObject("Age0CompareCam") { hideFlags = HideFlags.HideAndDontSave };
                created.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 40f;
                cam.nearClipPlane = 0.3f; cam.farClipPlane = 600f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.10f, 0.12f, 0.18f, 1f);
                var focus = stage + new Vector3(0f, 2f, 0f);
                var back = new Vector3(0f, Mathf.Sin(tilt * Mathf.Deg2Rad), -Mathf.Cos(tilt * Mathf.Deg2Rad));
                camGo.transform.position = focus + back * dist;
                camGo.transform.rotation = Quaternion.LookRotation(-back, Vector3.up);
                var sunGo = new GameObject("Age0CompareSun") { hideFlags = HideFlags.HideAndDontSave };
                created.Add(sunGo);
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.7f; sun.color = new Color(1f, 0.94f, 0.86f);
                sun.shadows = LightShadows.Soft;
                sunGo.transform.rotation = Quaternion.Euler(50f, -40f, 0f);
                var fillGo = new GameObject("Age0CompareFill") { hideFlags = HideFlags.HideAndDontSave };
                created.Add(fillGo);
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Directional; fill.intensity = 0.45f; fill.color = new Color(0.75f, 0.8f, 1f);
                fillGo.transform.rotation = Quaternion.Euler(35f, 140f, 0f);

                bool fog = RenderSettings.fog;
                var ambMode = RenderSettings.ambientMode; var ambCol = RenderSettings.ambientLight;
                RenderSettings.fog = false;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.42f, 0.48f, 0.58f);
                var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderSettings.fog = fog; RenderSettings.ambientMode = ambMode; RenderSettings.ambientLight = ambCol;
                cam.targetTexture = null;
                string file = Path.Combine(outDir, $"Compare_{name}.png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(gm);
                return $"OK {name}: {string.Join(" | ", sizes)} -> {file}";
            }
            finally { foreach (var c in created) if (c != null) UnityEngine.Object.DestroyImmediate(c); }
        }
    }
}
