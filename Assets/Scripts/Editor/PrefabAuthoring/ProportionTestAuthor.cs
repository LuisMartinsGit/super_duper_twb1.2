// ProportionTestAuthor — PROPORTION TESTS of Alanthor buildings (2026-11-11:
// the Garrison; then the Fortress). The developer finds the buildings
// "chunky": they are composed from real-size Synty pieces on a plinth 2.5x
// the footprint and the footprint fit draws the whole prefab at 0.36-0.40, so
// a 2 m Spearman stands beside 1 m doors on 2 m storeys. This author rebuilds
// a ladder by the rule the classic RTS buildings follow — shrink the
// BUILDING, not its PARTS:
//   1. plinth = footprint / 0.8 (the "Footprint" group with FootprintBounds):
//      detail scale 0.80, not 0.40;
//   2. a door a soldier fits through (the kit's big door / gate, ~3 m drawn);
//   3. fewer bays and storeys, not smaller ones;
//   4. a tall silhouette: steep roofs, a tower / chimney / flag per level;
//   5. a yard: the body covers ~60 % of the plinth, the rest is open ground
//      with props scaled 1 / 0.80 so they draw at UNIT scale;
//   6. narrow, uneven massing.
// Every level of a ladder shares one frame (plinth centred on the origin,
// floor at y = 0) and one plinth, so the ladder takes one fit and one offset.
//
// One PtSpec per building (footprint, plinth, Build function, where the old
// prefabs are); the helpers, the acceptance report and the comparison renders
// are shared. To add a building: write its PtBuild<Name>(root, level) and add
// a row to PtSpecs.
//
// The prefabs go to <Building>/ProportionTest/<Name>_PT_Lvl1..3.prefab, BESIDE
// the developer's hand-authored level prefabs, which this file never opens for
// writing; no SO is rebound. (ProportionVideoProbe swaps them in for one play
// session, in memory only.)
//
//   Waning Border > Art > Author Proportion Tests (Garrison + Fortress)
//   Waning Border > Art > Proportion Test > Render Comparisons
//   Waning Border > Art > Proportion Test > Measure Pieces
// Output (report.txt, PNGs): the TWB_OUT folder when set, else Temp/proportion-test.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static partial class FeraldisKitAuthor
    {
        sealed class PtSpec
        {
            public string Name;                 // "Garrison"
            public string Dir;                  // the cultured building's folder
            public string OldFormat;            // file name of the old level prefab, {0} = level
            public float Foot;                  // footprint, metres
            public float Half;                  // half the plinth, kit metres
            public Action<Transform, int> Build;
            public string[] BodyGroups;         // root children that are "body" (the rest is yard)
            public string WidthGroup;           // the group whose width the height is compared with
            public string DoorNode;             // the piece whose *Door* child renderers are the leaves
            public Vector3[] NewSpots, OldSpots; // scale figures, drawn metres from the pad centre (y 1 = on the plinth)
            public string NewPath(int level) => $"{Dir}ProportionTest/{Name}_PT_Lvl{level}.prefab";
            public string OldPath(int level) => Dir + string.Format(OldFormat, level);
            public float K => 2f * Half / Foot;
        }

        const string PtAla = TT + "Civs/Alanthor/Buildings/";
        const string PtSpearmanSo = TT + "Age0/Buildings/Barracks/Units/Spearman/Spearman.asset";
        const string PtArcherSo = TT + "Civs/Alanthor/Buildings/ArcheryRange/Units/Archer/Archer.asset";

        static readonly PtSpec PtGarrison = new PtSpec
        {
            Name = "Garrison", Dir = PtAla + "Garrison/", OldFormat = "Garrison_3 {0}.prefab", Foot = 8f, Half = 5f,
            Build = PtBuildGarrison, BodyGroups = new[] { "Hall", "Tower", "LeanTo" }, WidthGroup = "Hall", DoorNode = "HallDoor",
            NewSpots = new[] { new Vector3(0.75f, 1f, -2.75f), new Vector3(2.15f, 1f, -3.05f) },
            OldSpots = new[] { new Vector3(-2.6f, 0f, -4.8f), new Vector3(-1.4f, 0f, -5.0f), new Vector3(-0.6f, 1f, -1.2f), new Vector3(0.7f, 1f, -1.7f) },
        };
        static readonly PtSpec PtFortress = new PtSpec
        {
            Name = "Fortress", Dir = PtAla + "Fortress/", OldFormat = "Fortress {0}.prefab", Foot = 10f, Half = 6.25f,
            Build = PtBuildFortress, BodyGroups = new[] { "Keep", "KeepTop", "Gatehouse", "Turret" }, WidthGroup = "Keep", DoorNode = "Gate",
            NewSpots = new[] { new Vector3(0.1f, 1f, -3.1f), new Vector3(1.3f, 1f, -2.9f), new Vector3(-3.3f, 1f, -2.0f), new Vector3(-2.2f, 1f, -2.5f) },
            OldSpots = new[] { new Vector3(-0.6f, 0f, -5.8f), new Vector3(0.8f, 0f, -6.0f) },
        };
        static readonly PtSpec[] PtSpecs = { PtGarrison, PtFortress };

        /// <summary>The spec being built: the helpers read its plinth and prop scale.</summary>
        static PtSpec _pt = PtGarrison;
        static float PtK => _pt.K;

        // Garrison: hall centre lines (kit metres; composed with the front toward -Z, turned on save).
        const float PtHallW = -0.65f, PtHallE = 4.35f, PtHallF = -1.9f, PtHallB = 4.35f;
        const float PtHallCx = (PtHallW + PtHallE) * 0.5f;
        const float PtTowX = -2.6f, PtTowZ = 2.55f, PtTowHalf = 2f;

        const string PK_DoorBig = "Castle/SM_Bld_Castle_Wall_Door_Big_01";
        const string PK_Gate = "Castle/SM_Bld_Castle_Wall_Gate_01";
        const string PK_Slit = "Castle/SM_Bld_Castle_Wall_Arrowslit_03";
        const string PK_Window = "Castle/SM_Bld_Castle_Wall_Window_01";
        const string PK_WindowSmall = "Castle/SM_Bld_Castle_Wall_Window_03";
        const string PK_Bay = "Castle/SM_Bld_Castle_Wall_Cut_04";
        const string PK_Half = "Castle/SM_Bld_Castle_Wall_Cut_01";
        const string PK_Low = "Castle/SM_Bld_Castle_Wall_Cut_02";
        const string PK_LowHalf = "Castle/SM_Bld_Castle_Wall_Cut_03";
        const string PK_BattHalf = "Castle/SM_Bld_Castle_Battlements_01_Half";
        const string PK_Slope = "Castle/SM_Bld_Castle_Roof_S_01";
        const string PK_Verge = "Castle/SM_Bld_Castle_Roof_S_End_01";
        const string PK_Shed = "Castle/SM_Bld_Castle_Roof_S_Half_01";
        const string PK_PillarRound = "Castle/SM_Bld_Castle_Pillar_Stone_Round_01";
        const string PK_PillarSquare = "Castle/SM_Bld_Castle_Pillar_Stone_01";
        const string PK_Post = "Castle/SM_Bld_Castle_Pillar_Wood_02";
        const string PK_FlagSide = "Props/Banners/SM_Prop_Flag_Horizontal_01";
        const string PK_Fence = "Props/SM_Prop_Fence_01";
        const string PK_Rack = "Props/BattleGround/SM_Prop_Camp_Weapon_Rack_01";
        const string PK_RackLow = "Props/BattleGround/SM_Prop_Weapon_Rack_02";
        const string PK_DummyA = "Props/BattleGround/SM_Prop_Dummy_Armour_01";
        const string PK_DummyB = "Props/BattleGround/SM_Prop_Dummy_Armour_02";
        const string PK_DummyArch = "Props/BattleGround/SM_Prop_Dummy_Archery_01";
        const string PK_Barrel = "Props/SM_Prop_Barrel_01";
        const string PK_BarrelArrows = "Props/SM_Prop_Barrel_Arrows_01";
        const string PK_Crate = "Props/SM_Prop_Crate_Wood_01";
        const string PK_Sacks = "Props/SM_Prop_Sack_Stack_01";
        const string PK_StreetLamp = "Props/SM_Prop_Street_Lamp_01";
        const string PK_Armor = "Props/SM_Prop_Knight_Armor_01";
        const string PK_WarTable = "Props/Furniture/SM_Prop_Table_War_Preset_01";
        const string PK_HayCart = "Props/SM_Prop_Hay_Cart_01";
        const string PK_Statue = "Props/SM_Prop_Statue_Knight_01";
        const string PK_StatuePlinth = "Props/SM_Prop_Statue_Plinth_01";
        const string PK_Ivy = "Environments/SM_Env_Ivy_01";

        static string PtOut => Environment.GetEnvironmentVariable("TWB_OUT") is { Length: > 0 } s ? s : "Temp/proportion-test";

        // ═════════════════════════════════════════════════════════════════
        [MenuItem("Waning Border/Art/Author Proportion Tests (Garrison + Fortress)")]
        public static void AuthorProportionTests()
        {
            Report.Clear(); _failures = 0;
            try
            {
                _fkRoof = LoadMat(AlanthorArtDir + "Castle_Roof_Player.mat");
                _fkFlag = LoadMat(AlanthorArtDir + "Flag_Symbol_Player.mat");
                _fkWhite = LoadMat(AlanthorArtDir + "Castle_Wall_White.mat");
                _fkGold = LoadMat(AlanthorArtDir + "Castle_Wall_golden.mat");
                foreach (var spec in PtSpecs)
                {
                    _pt = spec;
                    string folder = spec.Dir + "ProportionTest";
                    if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(spec.Dir.TrimEnd('/'), "ProportionTest");
                    for (int l = 1; l <= 3; l++)
                    {
                        var root = new GameObject($"{spec.Name}_PT_Lvl{l}");
                        try
                        {
                            PtPlinth(root.transform);
                            spec.Build(root.transform, l);
                            // The Build functions compose with the front toward -Z; the
                            // presentation turns every building view half a turn
                            // (PresentationSpawnSystem.BuildingVisualOffsetY180), so the
                            // game's front is the model's +Z: turn each group about the
                            // plinth centre. Tweak pieces inside the groups, as authored.
                            for (int c = 0; c < root.transform.childCount; c++)
                                root.transform.GetChild(c).localRotation = Quaternion.Euler(0f, 180f, 0f);
                            PtCheckInside(root.name, root.transform);
                        }
                        catch (Exception) { UnityEngine.Object.DestroyImmediate(root); throw; }
                        Save(root, spec.NewPath(l));
                        Report.Add($"OK {spec.Name}_PT_Lvl{l} -> {spec.NewPath(l)}");
                    }
                }
                AssetDatabase.SaveAssets();
                foreach (var spec in PtSpecs) PtReport(spec);
                RenderProportionTests();
            }
            catch (Exception e) { Fail("ProportionTest", e.ToString()); }
            string text = $"[ProportionTest] done — {Report.Count} entries, {_failures} failure(s)\n" + string.Join("\n", Report);
            Debug.Log(text);
            try { Directory.CreateDirectory(PtOut); File.WriteAllText(PtOut + "/report.txt", text); } catch (Exception) { }
        }

        /// <summary>The plinth IS the footprint: castle floor stones covering
        /// 2 x Half, the only renderers the footprint fit measures.</summary>
        static void PtPlinth(Transform root)
        {
            var fp = Group(root, "Footprint");
            fp.gameObject.AddComponent<TheWaningBorder.Rendering.FootprintBounds>();
            int n = Mathf.CeilToInt(2f * _pt.Half / 5f - 0.001f);
            float tile = 2f * _pt.Half / n;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    Snap(Put(fp, FK_Floor, "Plinth" + Letter(i) + Letter(j), Vector3.zero, 0f, new Vector3(tile / 5f, 1f, tile / 5f), Style.Neutral),
                         new Vector3(-_pt.Half + tile * (i + 0.5f), 0f, -_pt.Half + tile * (j + 0.5f)), true);
        }

        // ═════════════════════════════════════════════════════════════════
        // GARRISON. Kit metres; the plinth is x, z in [-5, 5].
        //   Hall   — east / back: 5.5 x 6.75 m, gable and big door to the south.
        //   Tower  — north-west corner, 4 m square, behind the training court.
        //   LeanTo — a timber weapons shed on the hall's west wall.
        //   Yard   — the south strip and the west court, fenced, with the props.
        // ═════════════════════════════════════════════════════════════════
        static void PtBuildGarrison(Transform root, int level)
        {
            Material wall = level >= 2 ? _fkWhite : null;      // L1 bare castle stone, L2+ white plaster

            // ── Hall ────────────────────────────────────────────────────
            var hall = Group(root, "Hall");
            float hallLen = PtHallB - PtHallF, hallZc = (PtHallF + PtHallB) * 0.5f;
            PtWall(hall, "HallDoor", PK_DoorBig, new Vector3(PtHallCx, 0f, PtHallF), 180f, 5f, wall);
            PtWall(hall, "BackWall", PK_WindowSmall, new Vector3(PtHallCx, 0f, PtHallB), 0f, 5f, wall);
            foreach (var (tag, x, yaw) in new[] { ("West", PtHallW, 270f), ("East", PtHallE, 90f) })
            {
                PtWall(hall, tag + "Wall", tag == "East" ? PK_Window : PK_Slit, new Vector3(x, 0f, PtHallF + 2.5f), yaw, 5f, wall);
                PtWall(hall, tag + "Bay", PK_Bay, new Vector3(x, 0f, PtHallB - 0.625f), yaw, 1.25f, wall);
            }
            float wallH = 5f;
            if (level >= 3)
            {
                // A half storey under the roof: the hall grows up, never out.
                PtWall(hall, "LoftFront", PK_Low, new Vector3(PtHallCx, wallH, PtHallF), 180f, 5f, wall);
                PtWall(hall, "LoftBack", PK_Low, new Vector3(PtHallCx, wallH, PtHallB), 0f, 5f, wall);
                PtWall(hall, "LoftWest", PK_Low, new Vector3(PtHallW, wallH, hallZc), 270f, hallLen, wall);
                PtWall(hall, "LoftEast", PK_Low, new Vector3(PtHallE, wallH, hallZc), 90f, hallLen, wall);
                wallH += 2.5f;
            }
            foreach (var (n, x, z) in new[] { ("SW", PtHallW, PtHallF), ("SE", PtHallE, PtHallF), ("NW", PtHallW, PtHallB), ("NE", PtHallE, PtHallB) })
                PtPillar(hall, "Quoin" + n, PK_PillarRound, x, z, 0f, wallH, 0.8f, null);
            // The back and the east face are not bare: ivy on the stone.
            PtIvy(hall, "IvyBackA", new Vector3(PtHallCx + 1.6f, 1.5f, PtHallB + 0.25f), 0f, 1.3f);
            PtIvy(hall, "IvyBackB", new Vector3(PtHallCx - 1.5f, 3.2f, PtHallB + 0.25f), 0f, 1.0f);
            PtIvy(hall, "IvyEast", new Vector3(PtHallE + 0.25f, 1.6f, PtHallB - 1.0f), 90f, 1.3f);

            var cap = Group(root, "HallCap");
            float ridge = PtGable(cap, "Hall", PtHallCx, PtHallF - 0.3f, PtHallB + 0.25f, wallH - 0.3f, 3.8f, 3.05f, wall);
            PtChimney(cap, "Chimney", PtHallCx - 1.5f, 3.1f, wallH - 0.4f, ridge + 0.7f);

            // ── Tower ───────────────────────────────────────────────────
            var tower = Group(root, "Tower");
            var towerCap = Group(root, "TowerCap");
            var cloth = Group(root, "Cloth");
            string[] courses = level == 1 ? new[] { "Win" } : level == 2 ? new[] { "Win", "Low" } : new[] { "Win", "Win" };
            float towerTop = PtTower(tower, "Tower", PtTowX, PtTowZ, PtTowHalf, 0f, courses, null, wall);
            if (level == 1)
            {
                // A crenellated stub with the colours on a mast.
                PtCrenels(tower, "Crown", PtTowX, PtTowZ, PtTowHalf, towerTop, null);
                PtMast(cloth, "Mast", new Vector3(PtTowX + 0.9f, towerTop, PtTowZ), 4.2f, 0.55f);
            }
            else if (level == 2)
            {
                // A plaster storey, merlons and a pyramid inside them.
                PtCrenels(tower, "Crown", PtTowX, PtTowZ, PtTowHalf, towerTop, wall);
                PtCap(towerCap, "Spire", PtTowX, PtTowZ, towerTop, 3.1f, 3.4f);
            }
            else
            {
                // Gilded corbels under plaster merlons, a spire (kept below the Fortress's).
                PtCorbels(tower, "Corbel", PtTowX, PtTowZ, PtTowHalf, towerTop, _fkGold);
                PtCrenels(tower, "Crown", PtTowX, PtTowZ, PtTowHalf, towerTop, wall);
                PtCap(towerCap, "Spire", PtTowX, PtTowZ, towerTop, 3.1f, 3.2f);
                Hang(cloth, "TowerBannerS", new Vector3(PtTowX, towerTop - 2.2f, PtTowZ - PtTowHalf - 0.2f), 0f, 0.55f);
                Hang(cloth, "TowerBannerW", new Vector3(PtTowX - PtTowHalf - 0.18f, towerTop - 2.2f, PtTowZ), 90f, 0.55f);
            }

            // ── Lean-to: the weapons shed on the hall's west wall ───────
            var shed = Group(root, "LeanTo");
            const float shedX0 = -3.0f, shedX1 = PtHallW - 0.25f, shedZ0 = -1.8f, shedZ1 = 0.4f;
            var slope = Put(shed, PK_Shed, "ShedSlope", Vector3.zero, 270f, new Vector3((shedZ1 - shedZ0) / 5f, 1.2f / 2.5f, (shedX1 - shedX0) / 2.5f), Style.Neutral);
            Snap(slope, new Vector3((shedX0 + shedX1) * 0.5f, 2.9f, (shedZ0 + shedZ1) * 0.5f), false); Restyle(slope, null);
            foreach (var (n, z) in new[] { ("PostS", shedZ0 + 0.15f), ("PostN", shedZ1 - 0.15f) })
            {
                if (level == 1) PtPillar(shed, n, PK_Post, shedX0 + 0.25f, z, 0f, 3.0f, 0.3f, null);
                else PtPillar(shed, n, PK_PillarRound, shedX0 + 0.3f, z, 0f, 3.0f, 0.55f, wall);
            }

            // ── Cloth: the player colour ────────────────────────────────
            Hang(cloth, "GableBanner", new Vector3(PtHallCx, wallH + 2.7f, PtHallF - 0.75f), 0f, 0.45f);
            if (level >= 2)
            {
                Hang(cloth, "DoorBannerW", new Vector3(PtHallW + 0.62f, 4.7f, PtHallF - 0.5f), 0f, 0.4f);
                Hang(cloth, "DoorBannerE", new Vector3(PtHallE - 0.62f, 4.7f, PtHallF - 0.5f), 0f, 0.4f);
            }

            // ── Yard: unit-scale props ──────────────────────────────────
            var yard = Group(root, "Yard");
            float e = _pt.Half - 0.15f;
            PtFence(yard, "FenceW", -e, -e, -e, 0.4f);
            PtFence(yard, "FenceS", -e, -e, -0.5f, -e);
            PtFence(yard, "FenceSE", 3.4f, -e, e, -e);
            PtFence(yard, "FenceE", e, -e, e, -2.5f);
            PtProp(yard, PK_Rack, "SpearRack", -1.75f, -0.7f, 90f);
            PtProp(yard, PK_DummyA, "DummyA", -3.7f, -1.3f, 120f);
            PtProp(yard, PK_DummyArch, "DummyArchery", -2.2f, -3.7f, 15f, 1f, -0.2f);
            PtProp(yard, PK_Barrel, "BarrelA", -4.2f, 0.0f, 0f);
            PtProp(yard, PK_Crate, "Crate", 4.1f, -3.3f, 20f, 0.8f);
            PtProp(yard, PK_BarrelArrows, "ArrowBarrel", 3.9f, -2.5f, 0f);
            PtProp(yard, FK_Brazier, "Brazier", -0.1f, -2.7f, 0f, 1.3f);
            PtLamp(yard, "DoorLampW", new Vector3(PtHallW + 0.62f, 2.2f, PtHallF - 0.55f));
            PtLamp(yard, "DoorLampE", new Vector3(PtHallE - 0.62f, 2.2f, PtHallF - 0.55f));
            if (level >= 2)
            {
                PtProp(yard, PK_DummyB, "DummyB", -3.9f, -3.3f, 60f);
                PtProp(yard, PK_Barrel, "BarrelB", -3.45f, 0.05f, 40f);
                PtProp(yard, PK_StreetLamp, "GateLamp", -0.45f, -4.2f, 180f, 1f, -0.2f);
            }
            if (level >= 3)
            {
                PtProp(yard, PK_RackLow, "SwordRack", -4.3f, -2.3f, 90f, 0.9f);
                PtProp(yard, PK_Armor, "ArmourStand", 3.75f, -4.2f, 200f, 0.9f);
                PtProp(yard, PK_StreetLamp, "GateLampE", 3.45f, -4.2f, 180f, 1f, -0.2f);
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // FORTRESS — the capital. Kit metres; the plinth is x, z in [-6.25, 6.25].
        //   Keep      — north-east: 8 m square, round quoins, merlons, a pyramid;
        //               L2 a second full storey; L3 an inset top tower and spire.
        //   Gatehouse — the south front: the kit's gate (double doors behind a
        //               portcullis) between two turrets.
        //   Turret    — the north-west corner tower.
        //   Parapet   — a breast-high crenellated wall round the two courts, so
        //               the courtyard and its props stay in view.
        // ═════════════════════════════════════════════════════════════════
        const float PtKeepX = 2.0f, PtKeepZ = 2.0f, PtKeepHalf = 3.75f;      // centre lines; outer faces +0.25
        const float PtGateX = PtKeepX - 1.25f, PtGateZ = -5.2f;

        static void PtBuildFortress(Transform root, int level)
        {
            Material wall = level >= 2 ? _fkWhite : null;
            float H = _pt.Half;
            var cloth = Group(root, "Cloth");

            // ── Keep ────────────────────────────────────────────────────
            var keep = Group(root, "Keep");
            float y = PtKeepCourse(keep, "Base", 0f, false, (side, wide) => wide ? (side == "S" ? PK_DoorBig : PK_Slit) : PK_Half, null);
            if (level == 1) y = PtKeepCourse(keep, "Loft", y, true, (side, wide) => wide ? PK_Low : PK_LowHalf, null);
            else y = PtKeepCourse(keep, "Upper", y, false, (side, wide) => wide ? (side == "S" || side == "W" ? PK_Window : PK_WindowSmall) : PK_Half, wall);
            float keepTop = y;
            foreach (var (n, sx, sz) in new[] { ("SW", -1f, -1f), ("SE", 1f, -1f), ("NW", -1f, 1f), ("NE", 1f, 1f) })
                PtPillar(keep, "Quoin" + n, PK_PillarRound, PtKeepX + sx * PtKeepHalf, PtKeepZ + sz * PtKeepHalf, 0f, keepTop, 0.95f, null);
            PtCrenelRun(keep, "Crown", PtKeepX, PtKeepZ, PtKeepHalf, keepTop, wall);
            float dk = (2f * PtKeepHalf - 0.3f) / 5f;
            var deck = Put(keep, FK_Floor, "KeepDeck", Vector3.zero, 0f, new Vector3(dk, 1f, dk), Style.Neutral);
            Snap(deck, new Vector3(PtKeepX, keepTop + 0.03f, PtKeepZ), true); Restyle(deck, wall);
            PtIvy(keep, "IvyNorth", new Vector3(PtKeepX + 1.4f, 1.6f, PtKeepZ + PtKeepHalf + 0.25f), 0f, 1.5f);
            PtIvy(keep, "IvyEast", new Vector3(PtKeepX + PtKeepHalf + 0.25f, 1.7f, PtKeepZ - 0.8f), 90f, 1.5f);
            PtIvy(keep, "IvyWest", new Vector3(PtKeepX - PtKeepHalf - 0.25f, 1.4f, PtKeepZ + 2.3f), 270f, 1.2f);

            var top = Group(root, "KeepTop");
            var topCap = Group(root, "KeepCap");
            if (level < 3)
            {
                // L1 an open fighting deck under the colours; L2 a spire.
                if (level == 1) PtMast(cloth, "Mast", new Vector3(PtKeepX, keepTop, PtKeepZ), 4.2f, 0.7f);
                else PtCap(topCap, "Spire", PtKeepX, PtKeepZ, keepTop, 3.8f, 4.6f);
            }
            else
            {
                // The top tower: one more storey, set in from the merlons, gilded crown, spire.
                float t = PtTower(top, "Top", PtKeepX, PtKeepZ, 2.25f, keepTop, new[] { "Win" }, wall, wall);
                PtCorbels(top, "TopCorbel", PtKeepX, PtKeepZ, 2.25f, t, _fkGold);
                PtCrenels(top, "TopCrown", PtKeepX, PtKeepZ, 2.25f, t, wall);
                PtCap(topCap, "Spire", PtKeepX, PtKeepZ, t, 3.5f, 4.2f);
                Hang(cloth, "TopBannerS", new Vector3(PtKeepX, t - 1.9f, PtKeepZ - 2.25f - 0.2f), 0f, 0.5f);
                Hang(cloth, "TopBannerW", new Vector3(PtKeepX - 2.25f - 0.2f, t - 1.9f, PtKeepZ), 90f, 0.5f);
            }
            if (level >= 2)
            {
                // The colours down the keep's front, either side of the door bay.
                Hang(cloth, "KeepBannerA", new Vector3(PtKeepX - 3.0f, keepTop - 0.4f, PtKeepZ - PtKeepHalf - 0.45f), 0f, 0.5f);
                Hang(cloth, "KeepBannerB", new Vector3(PtKeepX + 2.45f, keepTop - 0.4f, PtKeepZ - PtKeepHalf - 0.45f), 0f, 0.5f);
                Hang(cloth, "KeepBannerW", new Vector3(PtKeepX - PtKeepHalf - 0.45f, keepTop - 0.4f, PtKeepZ + 2.45f), 90f, 0.5f);
            }

            // ── Gatehouse ───────────────────────────────────────────────
            var gate = Group(root, "Gatehouse");
            float gateTop = PtWall(gate, "Gate", PK_Gate, new Vector3(PtGateX, -0.27f, PtGateZ), 180f, 5.67f, wall);
            string[] flank = level >= 2 ? new[] { "Slit", "Low" } : new[] { "Slit" };
            foreach (var (n, x) in new[] { ("W", PtGateX - 3.85f), ("E", PtGateX + 3.85f) })
            {
                float ft = PtTower(gate, "Flank" + n, x, PtGateZ + 0.5f, 1.25f, 0f, flank, null, wall);
                PtCrenels(gate, "FlankCrown" + n, x, PtGateZ + 0.5f, 1.25f, ft, wall);
                if (level >= 2) PtCap(Group(root, "FlankCap" + n), "Spire", x, PtGateZ + 0.5f, ft, 1.9f, level >= 3 ? 3.0f : 2.4f);
                Hang(cloth, "GateBanner" + n, new Vector3(x, 4.6f, PtGateZ + 0.5f - 1.25f - 0.2f), 0f, 0.4f);
            }
            PtLamp(gate, "GateLampW", new Vector3(PtGateX - 2.2f, 2.6f, PtGateZ - 0.75f));
            PtLamp(gate, "GateLampE", new Vector3(PtGateX + 2.2f, 2.6f, PtGateZ - 0.75f));

            // ── North-west turret ───────────────────────────────────────
            var turret = Group(root, "Turret");
            float tx = -H + 1.65f, tz = H - 1.65f;
            float tt = PtTower(turret, "Turret", tx, tz, 1.5f, 0f, level >= 2 ? new[] { "Win", "Low" } : new[] { "Win" }, null, wall);
            PtCrenels(turret, "TurretCrown", tx, tz, 1.5f, tt, wall);
            if (level >= 2) PtCap(Group(root, "TurretCap"), "Spire", tx, tz, tt, 2.3f, level >= 3 ? 3.4f : 2.8f);

            // ── Parapet round the courts ────────────────────────────────
            var parapet = Group(root, "Parapet");
            float p = H - 0.3f;
            PtParapet(parapet, "ParapetS", -p, -p, PtGateX - 3.85f - 1.25f, -p, wall);
            PtParapet(parapet, "ParapetW", -p, -p, -p, tz - 1.5f, wall);
            PtParapet(parapet, "ParapetN", tx + 1.5f, p, PtKeepX - PtKeepHalf - 0.25f, p, wall);
            PtParapet(parapet, "ParapetE", p, PtGateZ + 0.5f + 1.25f, p, PtKeepZ - PtKeepHalf - 0.25f, wall);
            var post = Put(parapet, FK_BattPillar, "PostSW", Vector3.zero, 0f, Vector3.one, Style.Neutral);
            Snap(post, new Vector3(-p + 0.08f, 0f, -p + 0.08f), false); Restyle(post, wall);

            // ── Courts: unit-scale props ────────────────────────────────
            var yard = Group(root, "Yard");
            PtProp(yard, FK_Brazier, "BrazierW", PtGateX - 1.9f, PtKeepZ - PtKeepHalf - 0.9f, 0f, 1.3f);
            PtProp(yard, FK_Brazier, "BrazierE", PtGateX + 1.9f, PtKeepZ - PtKeepHalf - 0.9f, 0f, 1.3f);
            PtProp(yard, PK_Rack, "SpearRack", -5.0f, -2.9f, 90f);
            PtProp(yard, PK_DummyA, "DummyA", -3.4f, -3.9f, 150f);
            PtProp(yard, PK_Barrel, "BarrelA", -2.5f, 3.6f, 0f);
            PtProp(yard, PK_Barrel, "BarrelB", -2.55f, 2.75f, 30f);
            PtProp(yard, PK_Crate, "Crate", -2.7f, 1.7f, 15f, 0.8f);
            PtProp(yard, PK_Sacks, "Sacks", 5.3f, -2.6f, 0f, 0.9f);
            PtProp(yard, PK_BarrelArrows, "ArrowBarrel", 4.3f, -2.6f, 0f);
            if (level >= 2)
            {
                PtProp(yard, PK_WarTable, "WarTable", -4.2f, 0.4f, 90f, 0.62f);
                PtProp(yard, PK_DummyB, "DummyB", -4.6f, -4.4f, 40f);
                PtProp(yard, PK_StreetLamp, "CourtLamp", -2.6f, -1.5f, 90f, 1f, -0.2f);
            }
            if (level >= 3)
            {
                var sp = PtProp(yard, PK_StatuePlinth, "StatuePlinth", -4.1f, 2.2f, 90f, 0.6f, -0.1f);
                MeshBounds(sp, out var spb);
                PtProp(yard, PK_Statue, "Statue", -4.1f, 2.2f, 90f, 0.75f, spb.max.y);
                PtProp(yard, PK_Armor, "ArmourStand", -2.7f, 0.3f, 250f, 0.9f);
                PtProp(yard, PK_HayCart, "Cart", 4.6f, -3.0f, 90f, 0.7f);
            }
        }

        /// <summary>One storey of the keep: each side is a 5 m piece and a
        /// 2.5 m piece, chosen by <paramref name="pick"/>(side, wide).
        /// Returns the top.</summary>
        static float PtKeepCourse(Transform parent, string tag, float y, bool low, Func<string, bool, string> pick, Material mat)
        {
            float top = y;
            foreach (var (side, yaw, nx, nz) in new[] { ("S", 180f, 0f, -1f), ("N", 0f, 0f, 1f), ("E", 90f, 1f, 0f), ("W", 270f, -1f, 0f) })
            {
                // Along the side: x for the S / N faces, z for E / W. The wide
                // piece is toward -x / -z, so the keep's door faces the gate.
                Vector3 face = new Vector3(PtKeepX + nx * PtKeepHalf, y, PtKeepZ + nz * PtKeepHalf);
                Vector3 along = nz != 0f ? Vector3.right : Vector3.forward;
                top = Mathf.Max(top, PtWall(parent, tag + side + "Wide", pick(side, true), face + along * -1.25f, yaw, 5f, mat));
                PtWall(parent, tag + side + "Bay", pick(side, false), face + along * 2.5f, yaw, 2.5f, mat);
            }
            return top;
        }

        // ═════════════════════════════════════════════════════════════════
        // Helpers. Kit pivots (measured): a wall at yaw 0 faces +Z; a roof
        // slope at yaw 0 runs along X with its eave at the pivot and rises
        // toward -Z. Everything is placed by MEASURED bounds (Snap).
        // ═════════════════════════════════════════════════════════════════

        /// <summary>A wall piece <paramref name="len"/> long whose base centre
        /// is <paramref name="at"/>, outside toward <paramref name="yaw"/>
        /// (0 = +Z, 90 = +X, 180 = -Z, 270 = -X). Returns its top.</summary>
        static float PtWall(Transform parent, string name, string piece, Vector3 at, float yaw, float len, Material wall)
        {
            var go = Put(parent, piece, name, Vector3.zero, yaw, Vector3.one, Style.Neutral);
            MeshBounds(go, out var b);
            float native = Mathf.Abs(Mathf.Cos(yaw * Mathf.Deg2Rad)) > 0.5f ? b.size.x : b.size.z;
            go.transform.localScale = new Vector3(len / native, 1f, 1f);
            float top = Snap(go, at, false);
            Restyle(go, wall);
            return top;
        }

        static void PtPillar(Transform parent, string name, string piece, float x, float z, float y, float height, float width, Material mat)
        {
            var go = Put(parent, piece, name, Vector3.zero, 0f, Vector3.one, Style.Neutral);
            MeshBounds(go, out var b);
            go.transform.localScale = new Vector3(width / b.size.x, height / b.size.y, width / b.size.z);
            Snap(go, new Vector3(x, y, z), false);
            Restyle(go, mat);
        }

        /// <summary>A steep gable with its ridge along Z over x =
        /// <paramref name="cx"/>: two Roof_S slopes and four verge ends.
        /// Returns the ridge height.</summary>
        static float PtGable(Transform parent, string tag, float cx, float z0, float z1, float eaveY, float rise, float half, Material infill)
        {
            float len = z1 - z0, zc = (z0 + z1) * 0.5f;
            foreach (var (side, yaw, sx) in new[] { ("W", 270f, -1f), ("E", 90f, 1f) })
            {
                var s = Put(parent, PK_Slope, tag + "Slope" + side, Vector3.zero, yaw, new Vector3(len / 5f, rise / 5f, half / 2.5f), Style.Neutral);
                Snap(s, new Vector3(cx + sx * half * 0.5f, eaveY, zc), false); Restyle(s, null);
                foreach (var (end, z) in new[] { ("F", z0 + 0.25f), ("B", z1 - 0.25f) })
                {
                    var g = Put(parent, PK_Verge, tag + "Verge" + side + end, Vector3.zero, yaw, new Vector3(1f, rise / 5f, half / 2.5f), Style.Neutral);
                    Snap(g, new Vector3(cx + sx * half * 0.5f, eaveY, z), false); Restyle(g, infill);
                }
            }
            return eaveY + rise;
        }

        /// <summary>A square tower of one wall a side per course (Slit / Low /
        /// Win), a stone deck on top. Returns the wall top.</summary>
        static float PtTower(Transform parent, string tag, float cx, float cz, float half, float y0, string[] courses, Material lower, Material upper)
        {
            var sides = new[] { ("S", 180f, 0f, -1f), ("N", 0f, 0f, 1f), ("E", 90f, 1f, 0f), ("W", 270f, -1f, 0f) };
            float y = y0;
            bool narrow = half <= 1.3f;      // a 2.5 m face: the half pieces, never a squeezed 5 m one
            for (int c = 0; c < courses.Length; c++)
            {
                float top = y;
                foreach (var (side, yaw, nx, nz) in sides)
                {
                    string piece = courses[c] switch
                    {
                        "Slit" => PK_Slit,
                        "Low" => narrow ? PK_LowHalf : PK_Low,
                        "Win" => side == "S" || side == "W" ? PK_Window : PK_Slit,
                        _ => narrow ? PK_Half : FK_Wall,
                    };
                    top = Mathf.Max(top, PtWall(parent, $"{tag}{Letter(c)}{side}", piece,
                        new Vector3(cx + nx * (half - 0.25f), y, cz + nz * (half - 0.25f)), yaw, 2f * half - 0.02f, c == 0 ? lower : upper));
                }
                y = top;
            }
            float k = (2f * half - 0.4f) / 5f;
            var deck = Put(parent, FK_Floor, tag + "Deck", Vector3.zero, 0f, new Vector3(k, 1f, k), Style.Neutral);
            Snap(deck, new Vector3(cx, y + 0.03f, cz), true); Restyle(deck, upper ?? lower);
            return y;
        }

        static void PtCrenels(Transform parent, string tag, float cx, float cz, float half, float y, Material mat)
        {
            foreach (var (side, yaw, nx, nz) in new[] { ("S", 180f, 0f, -1f), ("N", 0f, 0f, 1f), ("E", 90f, 1f, 0f), ("W", 270f, -1f, 0f) })
                PtWall(parent, tag + side, FK_Batt, new Vector3(cx + nx * (half - 0.25f), y, cz + nz * (half - 0.25f)), yaw, 2f * half - 0.02f, mat);
            foreach (var (n, sx, sz) in new[] { ("NE", 1f, 1f), ("NW", -1f, 1f), ("SE", 1f, -1f), ("SW", -1f, -1f) })
            {
                var p = Put(parent, FK_BattPillar, tag + "Post" + n, Vector3.zero, 0f, Vector3.one, Style.Neutral);
                Snap(p, new Vector3(cx + sx * (half - 0.3f), y, cz + sz * (half - 0.3f)), false); Restyle(p, mat);
            }
        }

        static void PtCorbels(Transform parent, string tag, float cx, float cz, float half, float y, Material mat)
        {
            foreach (var (side, yaw, nx, nz) in new[] { ("S", 180f, 0f, -1f), ("N", 0f, 0f, 1f), ("E", 90f, 1f, 0f), ("W", 270f, -1f, 0f) })
            {
                var go = Put(parent, FK_Corbels, tag + side, Vector3.zero, yaw, new Vector3((2f * half - 0.02f) / 5f, 1f, 1f), Style.Neutral);
                Snap(go, new Vector3(cx + nx * (half - 0.15f), y, cz + nz * (half - 0.15f)), true); Restyle(go, mat);
            }
        }

        /// <summary>The kit's plain tiled pyramid, <paramref name="width"/>
        /// square and <paramref name="height"/> tall, standing on y.</summary>
        static void PtCap(Transform parent, string name, float cx, float cz, float y, float width, float height)
        {
            var go = Put(parent, FK_Pyramid, name, Vector3.zero, 0f, new Vector3(width / 3.74f, height / 10.08f, width / 3.74f), Style.Neutral);
            Snap(go, new Vector3(cx, y, cz), false); Restyle(go, null);
        }

        /// <summary>A pole with a player-colour flag flying toward -X.</summary>
        static void PtMast(Transform parent, string tag, Vector3 foot, float height, float flagScale)
        {
            PtPillar(parent, tag + "Pole", PK_Post, foot.x, foot.z, foot.y, height, 0.16f, null);
            var flag = Put(parent, PK_FlagSide, tag + "Flag", Vector3.zero, 0f, Vector3.one * flagScale, Style.Neutral);
            MeshBounds(flag, out var b);
            flag.transform.position += new Vector3(foot.x - 0.05f - b.max.x, foot.y + height - 0.1f - b.max.y, foot.z - b.center.z);
            Restyle(flag, null);
        }

        /// <summary>A yard prop at UNIT scale (x the spec's K), standing on y at (x, z).</summary>
        static GameObject PtProp(Transform parent, string piece, string name, float x, float z, float yaw, float mult = 1f, float y = 0f)
        {
            var go = Put(parent, piece, name, Vector3.zero, yaw, Vector3.one * (PtK * mult), Style.Neutral);
            Snap(go, new Vector3(x, y, z), false);
            return go;
        }

        static void PtLamp(Transform parent, string name, Vector3 centre)
        {
            var go = Put(parent, FK_Lamp, name, Vector3.zero, 0f, Vector3.one, Style.Neutral);
            MeshBounds(go, out var b);
            go.transform.position += centre - b.center;
        }

        /// <summary>A waist-high timber fence from a to b (axis-aligned), its posts sunk in the plinth.</summary>
        static void PtFence(Transform parent, string tag, float ax, float az, float bx, float bz)
        {
            var a = new Vector3(ax, 0f, az); var b = new Vector3(bx, 0f, bz);
            float len = Vector3.Distance(a, b);
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 2.47f));
            float seg = len / n;
            float yaw = Mathf.Abs(bx - ax) > Mathf.Abs(bz - az) ? 0f : 90f;
            for (int i = 0; i < n; i++)
            {
                var go = Put(parent, PK_Fence, tag + Letter(i), Vector3.zero, yaw, new Vector3(seg / 2.47f, 0.9f, 1f), Style.Neutral);
                MeshBounds(go, out var fb);
                Vector3 c = Vector3.Lerp(a, b, (i + 0.5f) / n);
                go.transform.position += new Vector3(c.x - fb.center.x, -0.2f - fb.min.y, c.z - fb.center.z);
            }
        }

        /// <summary>Merlons round a square of centre-line half 3.75 (7.5 m
        /// sides): a 5 m and a 2.5 m piece a side, never stretched.</summary>
        static void PtCrenelRun(Transform parent, string tag, float cx, float cz, float half, float y, Material mat)
        {
            foreach (var (side, yaw, nx, nz) in new[] { ("S", 180f, 0f, -1f), ("N", 0f, 0f, 1f), ("E", 90f, 1f, 0f), ("W", 270f, -1f, 0f) })
            {
                Vector3 face = new Vector3(cx + nx * half, y, cz + nz * half);
                Vector3 along = nz != 0f ? Vector3.right : Vector3.forward;
                PtWall(parent, tag + side + "Wide", FK_Batt, face + along * -1.25f, yaw, 5f, mat);
                PtWall(parent, tag + side + "Bay", PK_BattHalf, face + along * 2.5f, yaw, 2.5f, mat);
            }
            foreach (var (n, sx, sz) in new[] { ("NE", 1f, 1f), ("NW", -1f, 1f), ("SE", 1f, -1f), ("SW", -1f, -1f) })
            {
                var p = Put(parent, FK_BattPillar, tag + "Post" + n, Vector3.zero, 0f, Vector3.one * 1.2f, Style.Neutral);
                Snap(p, new Vector3(cx + sx * half, y, cz + sz * half), false); Restyle(p, mat);
            }
        }

        /// <summary>A breast-high crenellated wall from a to b (axis-aligned), standing on the plinth.</summary>
        static void PtParapet(Transform parent, string tag, float ax, float az, float bx, float bz, Material mat)
        {
            var a = new Vector3(ax, 0f, az); var b = new Vector3(bx, 0f, bz);
            float len = Vector3.Distance(a, b);
            if (len < 0.3f) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 2.5f));
            float yaw = Mathf.Abs(bx - ax) > Mathf.Abs(bz - az) ? 0f : 90f;
            for (int i = 0; i < n; i++)
                PtWall(parent, tag + Letter(i), PK_BattHalf, Vector3.Lerp(a, b, (i + 0.5f) / n), yaw, len / n, mat);
        }

        /// <summary>A flat ivy panel on a wall face: centred on (x, y) of
        /// <paramref name="onFace"/>, flush with the face that looks toward
        /// <paramref name="yaw"/>, thinned so it stays inside the plinth.</summary>
        static void PtIvy(Transform parent, string name, Vector3 onFace, float yaw, float scale)
        {
            var go = Put(parent, PK_Ivy, name, Vector3.zero, yaw, new Vector3(scale, scale, 0.45f), Style.Neutral);
            MeshBounds(go, out var b);
            Vector3 d = onFace - b.center;
            int q = Mathf.RoundToInt(yaw / 90f) % 4;
            if (q == 0) d.z = onFace.z - b.min.z;
            else if (q == 1) d.x = onFace.x - b.min.x;
            else if (q == 2) d.z = onFace.z - b.max.z;
            else d.x = onFace.x - b.max.x;
            go.transform.position += d;
        }

        /// <summary>A stone stack from y0 to yTop with a coping and a pot.</summary>
        static void PtChimney(Transform parent, string name, float x, float z, float y0, float yTop)
        {
            PtPillar(parent, name + "Stack", PK_PillarSquare, x, z, y0, yTop - y0 - 0.3f, 0.75f, null);
            PtPillar(parent, name + "Coping", PK_PillarSquare, x, z, yTop - 0.3f, 0.3f, 1.05f, null);
            PtPillar(parent, name + "Pot", PK_PillarRound, x, z, yTop, 0.45f, 0.45f, null);
        }

        static void PtCheckInside(string id, Transform root)
        {
            MeshBounds(root.gameObject, out var b);
            int pieces = root.GetComponentsInChildren<Transform>(true).Count(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject));
            Report.Add($"{id}: {pieces} kit pieces, kit bounds min {b.min.ToString("F2")} max {b.max.ToString("F2")}");
            float h = _pt.Half;
            if (b.min.x < -h - 0.02f || b.max.x > h + 0.02f || b.min.z < -h - 0.02f || b.max.z > h + 0.02f)
                Fail(id, $"spills out of the {2 * h} m plinth: {b.min} .. {b.max}");
        }

        // ═════════════════════════════════════════════════════════════════
        // Acceptance measurements, old against new.
        // ═════════════════════════════════════════════════════════════════

        /// <summary>The footprint fit of a prefab on a square footprint,
        /// exactly as the presentation measures it, and its pivot offset.</summary>
        static float PtFit(GameObject prefab, float foot, out Vector3 offset)
        {
            var probe = UnityEngine.Object.Instantiate(prefab, new Vector3(0f, -4096f, 0f), Quaternion.identity);
            probe.hideFlags = HideFlags.HideAndDontSave;
            probe.transform.localScale = Vector3.one;
            float fit = PresentationSpawnSystem.ComputeFootprintFitForSize(probe, foot, foot, out offset);
            UnityEngine.Object.DestroyImmediate(probe);
            return PresentationSpawnSystem.AuthoredScaleOf(prefab) * fit;
        }

        static bool PtBounds(Transform t, out Bounds b)
        {
            b = default; bool any = false;
            if (t == null) return false;
            foreach (var r in t.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        static void PtReport(PtSpec spec)
        {
            Report.Add($"== {spec.Name}: footprint {spec.Foot} m, plinth {2 * spec.Half} m, props x{spec.K:F2}");
            Report.Add("level | drawn scale | drawn W x D x H (m) | door leaf H (m) | body share | H / body width | overhang");
            float firstFit = 0f;
            for (int l = 1; l <= 3; l++)
            {
                foreach (bool isNew in new[] { false, true })
                {
                    string path = isNew ? spec.NewPath(l) : spec.OldPath(l);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) { Fail("report", "missing " + path); continue; }
                    float fit = PtFit(prefab, spec.Foot, out var offset);
                    var go = UnityEngine.Object.Instantiate(prefab);
                    go.hideFlags = HideFlags.HideAndDontSave;
                    go.transform.localScale = Vector3.one; go.transform.position = Vector3.zero;
                    try
                    {
                        PtBounds(go.transform, out var all);
                        if (!PtBounds(go.transform.Find("Footprint"), out var slab)) slab = all;   // no marker: the whole model is the footprint
                        // Door: the tallest door LEAF (a child renderer of a door / gate piece).
                        float door = 0f;
                        foreach (var r in go.GetComponentsInChildren<Renderer>(false))
                        {
                            var p = r.transform.parent;
                            if (p == null || p == go.transform || p.GetComponent<Renderer>() == null) continue;
                            if (r.name.IndexOf("Door", StringComparison.OrdinalIgnoreCase) < 0) continue;
                            if (isNew && !IsUnder(r.transform, spec.DoorNode)) continue;
                            door = Mathf.Max(door, r.bounds.size.y);
                        }
                        string doorText = door > 0f ? (door * fit).ToString("F2") : "no leaf (kit single door 2.35 m -> " + (2.35f * fit).ToString("F2") + ")";
                        float over = Mathf.Max(Mathf.Max(slab.min.x - all.min.x, all.max.x - slab.max.x), Mathf.Max(slab.min.z - all.min.z, all.max.z - slab.max.z));
                        string share = "n/a", ratio = "n/a";
                        if (isNew)
                        {
                            var rects = new List<Bounds>();
                            foreach (var g in spec.BodyGroups)
                                if (PtBounds(go.transform.Find(g), out var gb)) rects.Add(gb);
                            int inside = 0, total = 0;
                            for (float x = slab.min.x + 0.05f; x < slab.max.x; x += 0.1f)
                                for (float z = slab.min.z + 0.05f; z < slab.max.z; z += 0.1f)
                                {
                                    total++;
                                    foreach (var rb in rects)
                                        if (x >= rb.min.x && x <= rb.max.x && z >= rb.min.z && z <= rb.max.z) { inside++; break; }
                                }
                            share = (100f * inside / Mathf.Max(1, total)).ToString("F0") + " %";
                            PtBounds(go.transform.Find(spec.WidthGroup), out var hb);
                            ratio = (all.max.y / hb.size.x).ToString("F2") + $" ({spec.WidthGroup} {hb.size.x * fit:F1} m wide)";
                            if (l == 1) firstFit = fit;
                            if (Mathf.Abs(fit - firstFit) > 0.001f) Fail("report", $"{spec.Name} L{l} fit {fit} differs from L1 {firstFit}");
                            if (fit < 0.70f) Fail("report", $"{spec.Name} L{l} detail scale {fit:F2} < 0.70");
                            if (door * fit < 2.0f) Fail("report", $"{spec.Name} L{l} door {door * fit:F2} m < 2.0 m");
                            if (over > 0.02f) Fail("report", $"{spec.Name} L{l} overhangs the plinth by {over:F2} m");
                        }
                        Report.Add($"{spec.Name} L{l} {(isNew ? "NEW" : "old")} | {fit:F2} | {all.size.x * fit:F2} x {all.size.z * fit:F2} x {all.max.y * fit:F2} | {doorText} | {share} | {ratio} | {Mathf.Max(0f, over) * fit:F2} m"
                                   + $" | offset {offset.ToString("F2")}");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(go); }
                }
            }
        }

        static bool IsUnder(Transform t, string ancestorName)
        {
            for (var p = t.parent; p != null; p = p.parent) if (p.name == ancestorName) return true;
            return false;
        }

        // ═════════════════════════════════════════════════════════════════
        // Comparison renders: every prefab at its IN-GAME scale on a pad the
        // size of the footprint, a Spearman and an Archer beside it.
        // ═════════════════════════════════════════════════════════════════
        [MenuItem("Waning Border/Art/Proportion Test/Render Comparisons")]
        public static void RenderProportionTests()
        {
            Directory.CreateDirectory(PtOut);
            (GameObject, PtSpec, bool) Row(PtSpec s, int l, bool isNew) => (AssetDatabase.LoadAssetAtPath<GameObject>(isNew ? s.NewPath(l) : s.OldPath(l)), s, isNew);
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                PtShot(new[] { Row(PtGarrison, 1, false), Row(PtGarrison, 1, true) }, 64, 64, null, 45f, 40f);      // warm-up
                foreach (var s in PtSpecs)
                {
                    for (int l = 1; l <= 3; l++)
                    {
                        var pair = new[] { Row(s, l, false), Row(s, l, true) };
                        PtShot(pair, 2200, 1200, $"{PtOut}/{s.Name}_old_vs_new_L{l}.png", 45f, 42f);
                        PtShot(pair, 2200, 1200, $"{PtOut}/{s.Name}_old_vs_new_L{l}_low.png", 45f, 16f);
                    }
                    var ladderNew = new[] { Row(s, 1, true), Row(s, 2, true), Row(s, 3, true) };
                    var ladderOld = new[] { Row(s, 1, false), Row(s, 2, false), Row(s, 3, false) };
                    PtShot(ladderNew, 2600, 1100, $"{PtOut}/{s.Name}_ladder_new.png", 45f, 42f);
                    PtShot(ladderNew, 2600, 1100, $"{PtOut}/{s.Name}_ladder_new_low.png", 45f, 16f);
                    PtShot(ladderNew, 2600, 1100, $"{PtOut}/{s.Name}_ladder_new_back.png", 225f, 35f);
                    PtShot(ladderOld, 2600, 1100, $"{PtOut}/{s.Name}_ladder_old.png", 45f, 42f);
                }
                // The town: every new level of both, the Garrison in front of its Fortress.
                var town = new[] { Row(PtGarrison, 1, true), Row(PtFortress, 1, true), Row(PtGarrison, 2, true), Row(PtFortress, 2, true), Row(PtGarrison, 3, true), Row(PtFortress, 3, true) };
                PtShot(town, 3000, 1100, $"{PtOut}/lineup_new.png", 45f, 38f);
                PtShot(town, 3000, 1100, $"{PtOut}/lineup_new_low.png", 45f, 14f);
            }
            finally { ShaderUtil.allowAsyncCompilation = async; }
            Debug.Log("[ProportionTest] renders -> " + PtOut);
        }

        /// <summary>The rows side by side across the view of a camera at
        /// <paramref name="yaw"/> (45 = the game's default, looking north-east)
        /// and <paramref name="pitch"/> degrees above the horizon.</summary>
        static void PtShot((GameObject prefab, PtSpec spec, bool isNew)[] rows, int w, int h, string outPng, float yaw, float pitch)
        {
            var created = new List<GameObject>();
            var stage = new Vector3(0f, -4200f, 0f);
            Material Flat(Color c)
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave };
                m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.1f);
                return m;
            }
            try
            {
                var rot = Quaternion.Euler(0f, yaw, 0f);
                Vector3 right = rot * Vector3.right;
                const float spacing = 16.5f;
                float maxTop = 0f;
                for (int i = 0; i < rows.Length; i++)
                {
                    var (prefab, spec, isNew) = rows[i];
                    if (prefab == null) continue;
                    Vector3 at = stage + right * ((i - (rows.Length - 1) * 0.5f) * spacing);
                    float scale = PtFit(prefab, spec.Foot, out var offset);
                    var go = UnityEngine.Object.Instantiate(prefab);
                    go.hideFlags = HideFlags.HideAndDontSave; created.Add(go);
                    // As the game draws it: fitted, and turned half a turn.
                    var half = Quaternion.Euler(0f, 180f, 0f);
                    go.transform.localScale = Vector3.one * scale;
                    go.transform.rotation = half;
                    go.transform.position = at - half * (offset * scale);
                    TheWaningBorder.Rendering.BuildingFactionColorMarker.Apply(go, new Color(0.80f, 0.14f, 0.12f));
                    if (MeshBounds(go, out var gb)) maxTop = Mathf.Max(maxTop, gb.max.y - stage.y);

                    var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    pad.hideFlags = HideFlags.HideAndDontSave; created.Add(pad);
                    pad.transform.position = at + new Vector3(0f, -0.03f, 0f);
                    pad.transform.localScale = new Vector3(spec.Foot, 0.06f, spec.Foot);
                    pad.GetComponent<Renderer>().sharedMaterial = Flat(new Color(0.14f, 0.15f, 0.12f));
                    float plinthTop = -offset.y * scale;   // the floor is y = 0 in both frames

                    var spots = isNew ? spec.NewSpots : spec.OldSpots;
                    for (int s = 0; s < spots.Length; s++)
                    {
                        string unitPath = SoPrefabPath(s % 2 == 0 ? PtSpearmanSo : PtArcherSo);
                        var up = unitPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(unitPath) : null;
                        if (up == null) continue;
                        var u = UnityEngine.Object.Instantiate(up);
                        u.hideFlags = HideFlags.HideAndDontSave; created.Add(u);
                        u.transform.position = at + new Vector3(spots[s].x, spots[s].y > 0.5f ? plinthTop : 0f, spots[s].z);
                        u.transform.rotation = Quaternion.Euler(0f, yaw + 180f + (s % 2 == 0 ? -20f : 25f), 0f);
                    }
                }

                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.hideFlags = HideFlags.HideAndDontSave; created.Add(ground);
                ground.transform.position = stage + new Vector3(0f, -0.06f, 0f);
                ground.transform.localScale = new Vector3(30f, 1f, 30f);
                ground.GetComponent<Renderer>().sharedMaterial = Flat(new Color(0.30f, 0.38f, 0.26f));

                var camGo = new GameObject("PtCam") { hideFlags = HideFlags.HideAndDontSave }; created.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 30f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 600f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.55f, 0.66f, 0.78f, 1f);
                float span = rows.Length * spacing;
                float aspect = (float)w / h;
                float distW = span * 0.5f / (Mathf.Tan(15f * Mathf.Deg2Rad) * aspect);
                float distH = (maxTop + 10f) * 0.5f / Mathf.Tan(15f * Mathf.Deg2Rad);
                float dist = Mathf.Max(distW, distH) * 1.08f;
                var look = Quaternion.Euler(pitch, yaw, 0f);
                var focus = stage + new Vector3(0f, maxTop * 0.42f, 0f);
                camGo.transform.rotation = look;
                camGo.transform.position = focus - (look * Vector3.forward) * dist;

                var sunGo = new GameObject("PtSun") { hideFlags = HideFlags.HideAndDontSave }; created.Add(sunGo);
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.6f; sun.color = new Color(1f, 0.95f, 0.86f);
                sun.shadows = LightShadows.Soft;
                sunGo.transform.rotation = Quaternion.Euler(46f, yaw - 38f, 0f);
                var fillGo = new GameObject("PtFill") { hideFlags = HideFlags.HideAndDontSave }; created.Add(fillGo);
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Directional; fill.intensity = 0.45f; fill.color = new Color(0.78f, 0.84f, 1f);
                fillGo.transform.rotation = Quaternion.Euler(30f, yaw + 140f, 0f);

                bool fog = RenderSettings.fog;
                var ambMode = RenderSettings.ambientMode; var ambCol = RenderSettings.ambientLight;
                RenderSettings.fog = false;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.46f, 0.5f, 0.58f);
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderSettings.fog = fog; RenderSettings.ambientMode = ambMode; RenderSettings.ambientLight = ambCol;
                cam.targetTexture = null;
                if (outPng != null) File.WriteAllBytes(outPng, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            }
            finally { foreach (var c in created) if (c != null) UnityEngine.Object.DestroyImmediate(c); }
        }

        // ═════════════════════════════════════════════════════════════════
        static readonly string[] PtMeasureList =
        {
            "Castle/SM_Bld_Castle_Wall_Gate_01", "Castle/SM_Bld_Castle_Wall_Gate_L_01", "Castle/SM_Bld_Castle_Wall_Archway_01", "Castle/SM_Bld_Castle_Wall_Archway_02",
            "Castle/SM_Bld_Castle_Archway_Small_01", "Castle/SM_Bld_Castle_Wall_Archway_Legs_01", "Castle/SM_Bld_Castle_Drawbridge_01",
            "Castle/SM_Bld_Castle_Wall_Window_Big_01", "Castle/SM_Bld_Castle_Wall_Window_Fancy_01", "Castle/SM_Bld_Castle_Wall_Window_03", "Castle/SM_Bld_Castle_Wall_Window_04",
            "Castle/SM_Bld_Castle_Wall_Cut_01", "Castle/SM_Bld_Castle_Wall_Cut_03", "Castle/SM_Bld_Castle_Wall_04", "Castle/SM_Bld_Castle_Wall_Fireplace_01",
            "Castle/SM_Bld_Castle_Battlements_01_Half", "Castle/SM_Bld_Castle_Battlements_03", "Castle/SM_Bld_Castle_Roof_Cap_Round_04",
            "Castle/SM_Bld_Castle_Roof_Cap_Square_06", "Castle/SM_Bld_Castle_Roof_Cap_Square_07", "Castle/SM_Bld_Castle_Roof_Cap_Square_05",
            "Props/SM_Prop_Well_01", "Props/SM_Prop_Statue_Knight_01", "Props/SM_Prop_Statue_Plinth_01", "Props/SM_Prop_Horse_Hitching_Post_01", "Props/SM_Prop_Hay_Cart_01",
            "Props/Furniture/SM_Prop_Table_War_Preset_01", "Props/Banners/SM_Prop_Flag_Pole_02", "Props/Banners/SM_Prop_Banner_01", "Props/Banners/SM_Prop_Battle_Banner_01",
            "Props/BattleGround/SM_Prop_Target_01", "Props/SM_Prop_Barrel_Stack_01", "Props/SM_Prop_Crate_Stack_02", "Props/SM_Prop_Sack_Stack_01", "Props/SM_Prop_Bracket_Pot_01_Preset_01",
            "Environments/SM_Env_Ivy_01", "Environments/SM_Env_Bush_01", "Environments/SM_Env_Planter_Base_01",
        };

        /// <summary>Bounds, child renderers and materials of the kit pieces this author uses.</summary>
        [MenuItem("Waning Border/Art/Proportion Test/Measure Pieces")]
        public static void PtMeasurePieces()
        {
            var sb = new StringBuilder();
            var holder = new GameObject("PtMeasure");
            try
            {
                foreach (var piece in PtMeasureList)
                {
                    try
                    {
                        var go = Put(holder.transform, piece, "m", Vector3.zero, 0f, Vector3.one, Style.Neutral);
                        MeshBounds(go, out var b);
                        var mats = go.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.name).Distinct();
                        sb.AppendLine($"{piece}: min {b.min.ToString("F2")} max {b.max.ToString("F2")} size {b.size.ToString("F2")} mats [{string.Join(",", mats)}]");
                        var rs = go.GetComponentsInChildren<Renderer>(true);
                        if (rs.Length > 1) foreach (var r in rs) sb.AppendLine($"    - {r.name}: min {r.bounds.min.ToString("F2")} max {r.bounds.max.ToString("F2")}");
                        UnityEngine.Object.DestroyImmediate(go);
                    }
                    catch (Exception e) { sb.AppendLine($"{piece}: FAIL {e.Message}"); }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
            Directory.CreateDirectory(PtOut);
            File.WriteAllText(PtOut + "/pt-measure.txt", sb.ToString());
            Debug.Log("[ProportionTest] measured -> " + PtOut + "/pt-measure.txt");
        }
    }
}
