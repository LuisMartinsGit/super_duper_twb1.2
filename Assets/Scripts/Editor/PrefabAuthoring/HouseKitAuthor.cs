// HouseKitAuthor — the Hut line as EDITABLE prefabs (2026-10-27): the Age 0
// Hut and the Alanthor House at levels 1-3, each a prefab of nested Synty
// Fantasy Kingdom piece instances under numbered rise groups, so every wall,
// post, lamp and bush is a child the developer can move in the Editor. The
// Blender-joined FBX route for these four is retired here: the author deletes
// the FBX / .blend beside the prefabs and wires the SOs to the prefabs.
//
//   Waning Border > Art > Author House Line (Hut + Alanthor House)
//
// Every model fills the Hut's 2 x 2-cell plot (4 m x 4 m, Build_Grid.md) edge
// to edge, and any ground the building does not stand on is fenced. Pieces are
// placed by their MEASURED bounds (PutAt), never by assumed pivots. Materials:
// Syn_Atlas / Age0_Mud under the Hut, Al_Stucco / Al_Stone / Al_Tile / Al_Red
// under the House — BuildingLit over one Synty texture each, made by
// HouseLineupScene.EnsureHutMaterials. Previews go to Temp/house-kit-previews/.
// No node is named "roof": BuildingFactionColorMarker paints such nodes slate.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static partial class FeraldisKitAuthor
    {
        const string HutDir = TT + "Age0/Buildings/Hut/";
        const string HouseDir = TT + "Civs/Alanthor/Buildings/House/";
        const string HousePreviewDir = "Temp/house-kit-previews";

        // pieces (KitRoot-relative)
        const string E_Bush = "Environments/SM_Env_Bush_01";
        const string P_FenceGateH = "Props/SM_Prop_Fence_Gate_01";
        const string P_Barrel1 = "Props/SM_Prop_Barrel_01";
        const string P_StreetLamp1 = "Props/SM_Prop_Street_Lamp_01";
        const string P_TBeam = "Props/SM_Prop_Beam_01";
        const string C_WallPlain = "Castle/SM_Bld_Castle_Wall_01";
        const string C_WallDoorArch = "Castle/SM_Bld_Castle_Wall_Door_01";
        const string C_WallWindowArch = "Castle/SM_Bld_Castle_Wall_Window_01";
        const string C_CapSquare1 = "Castle/SM_Bld_Castle_Roof_Cap_Square_01";
        const string C_FloorStone1 = "Castle/SM_Bld_Castle_Floor_Stone_01";
        const string H_StairsStone = "Buildings/House/SM_Bld_House_Stairs_Stone_01";

        const float Plot = 2f;             // half-size of the 4 m plot

        static Material _synAtlas, _age0Mud, _alStucco, _alStone, _alTile, _alRed;

        [MenuItem("Waning Border/Art/Author House Line (Hut + Alanthor House)")]
        public static void AuthorHouseLine()
        {
            Report.Clear(); _failures = 0;
            try
            {
                HouseLineupScene.EnsureHutMaterials(new System.Text.StringBuilder());
                _synAtlas = Need(HutDir + "Art/Materials/Syn_Atlas.mat");
                _age0Mud = Need(HutDir + "Art/Materials/Age0_Mud.mat");
                _alStucco = Need(HouseDir + "Art/Materials/Al_Stucco.mat");
                _alStone = Need(HouseDir + "Art/Materials/Al_Stone.mat");
                _alTile = Need(HouseDir + "Art/Materials/Al_Tile.mat");
                _alRed = Need(HouseDir + "Art/Materials/Al_Red.mat");

                PlainInPlace(HutDir + "Art/Hut_Age_0.prefab", HutDir + "Hut.asset", "Hut_Age_0", BuildHutKit);
                for (int l = 1; l <= 3; l++)
                {
                    int level = l;
                    PlainInPlace(HouseDir + $"Art/alanthor_House_lv{l}.prefab", HouseDir + $"House_Lvl{l}.asset",
                                 $"alanthor_House_lv{l}", t => BuildHouseKit(t, level));
                }
                // The joined-mesh FBX route is retired for these four.
                foreach (var p in new[] { HutDir + "Art/Hut_Age_0.fbx", HutDir + "Art/Hut_Age_0.blend",
                                          HouseDir + "Art/alanthor_House_lv1.fbx", HouseDir + "Art/alanthor_House_lv1.blend",
                                          HouseDir + "Art/alanthor_House_lv2.fbx", HouseDir + "Art/alanthor_House_lv2.blend",
                                          HouseDir + "Art/alanthor_House_lv3.fbx", HouseDir + "Art/alanthor_House_lv3.blend" })
                    if (AssetDatabase.LoadMainAssetAtPath(p) != null && AssetDatabase.DeleteAsset(p)) Report.Add("OK removed " + p);
                AssetDatabase.SaveAssets();
                RenderHouseLine();
            }
            catch (Exception e) { Fail("AuthorHouseLine", e.ToString()); }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string text = $"[HouseKit] done — {Report.Count} entries, {_failures} failure(s)\n" + string.Join("\n", Report);
            Debug.Log(text);
            try { Directory.CreateDirectory("Temp"); File.WriteAllText("Temp/house-kit-report.txt", text); } catch (Exception) { }
        }

        static Material Need(string path)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) throw new Exception("material missing: " + path);
            return m;
        }

        // ── placement by measured bounds ─────────────────────────────────

        /// <summary>A piece whose renderer bounds are centred on (x, z) of
        /// <paramref name="at"/> and stand on its y. With <paramref name="alongX"/>
        /// the piece is turned so its longer ground axis runs along X.</summary>
        static GameObject PutAt(Transform parent, string piece, string name, Vector3 at, float yaw, Vector3 scale,
                                Material mat, bool? alongX = null)
        {
            var go = Put(parent, piece, name, Vector3.zero, yaw, scale, Style.Neutral, null, mat);
            if (!MeshBounds(go, out var b)) throw new Exception("no renderers in " + piece);
            if (alongX.HasValue && (b.size.x >= b.size.z) != alongX.Value)
            {
                go.transform.localRotation = Quaternion.Euler(0f, yaw + 90f, 0f);
                MeshBounds(go, out b);
            }
            go.transform.position += new Vector3(at.x - b.center.x, at.y - b.min.y, at.z - b.center.z);
            return go;
        }

        /// <summary>As PutAt, but the bounds CENTRE lands on <paramref name="at"/> (hung and set-in pieces).</summary>
        static GameObject PutCentred(Transform parent, string piece, string name, Vector3 at, float yaw, Vector3 scale,
                                     Material mat, bool? alongX = null)
        {
            var go = PutAt(parent, piece, name, at, yaw, scale, mat, alongX);
            MeshBounds(go, out var b);
            go.transform.position += new Vector3(0f, at.y - b.center.y, 0f);
            return go;
        }

        /// <summary>Synty post-and-rail fence on the plot edge: two spans a side,
        /// the gate on the south-east span. Posts stand ON the ground (the kit's
        /// buried 0.52 m is lifted out, the piece scaled down to compensate).</summary>
        static void FenceRing(Transform parent, float half, float y)
        {
            float e = half - 0.1f;
            foreach (var (side, yaw) in new[] { ("S", 0f), ("E", 90f), ("N", 180f), ("W", 270f) })
                for (int j = 0; j < 2; j++)
                {
                    float along = (j - 0.5f) * 1.98f;
                    Vector3 c = side switch
                    {
                        "S" => new Vector3(along, y, -e), "E" => new Vector3(e, y, along),
                        "N" => new Vector3(-along, y, e), _ => new Vector3(-e, y, -along),
                    };
                    bool alongX = side == "S" || side == "N";
                    if (side == "S" && j == 1)
                        PutAt(parent, P_FenceGateH, "Gate", c, 0f, new Vector3(1.98f / 2.61f, 0.85f, 1f), _synAtlas, alongX);
                    else
                        PutAt(parent, P_Fence, "Fence" + side + Letter(j), c, 0f, new Vector3(1.98f / 2.48f, 0.7f, 1f), _synAtlas, alongX);
                }
        }

        // ── the Hut ──────────────────────────────────────────────────────

        /// <summary>Hut (4 m, Age 0): a mud yard, plank hoarding walls round a
        /// 2.5 m square on beam posts with a door leaf, a thatch gable, and a
        /// fenced yard with woodpile, barrel, basket, hay and bushes.</summary>
        static void BuildHutKit(Transform root)
        {
            float yY = 0.14f;                       // yard top
            var g1 = Group(root, "1_Ground");
            PutAt(g1, C_FloorStone1, "Yard", new Vector3(0f, 0f, 0f), 0f, new Vector3(2f * Plot / 5f, 0.5f, 2f * Plot / 5f), _age0Mud);

            // the body, a few degrees off square on its plot
            var body = Group(root, "2_Walls"); var roof = Group(root, "3_Thatch");
            float cz = 0.25f, half = 1.25f, wallH = 2.0f;
            ThatchHall(body, roof, 0f, cz, 1, 1, yY, walls: false, door: false, postHeight: wallH);
            foreach (var (side, yaw) in new[] { ("S", 0f), ("E", 90f), ("N", 180f), ("W", 270f) })
            {
                Vector3 c = side switch
                {
                    "S" => new Vector3(0f, yY, cz - half), "E" => new Vector3(half, yY, cz),
                    "N" => new Vector3(0f, yY, cz + half), _ => new Vector3(-half, yY, cz),
                };
                PutAt(body, C_HoardWall, "Wall" + side, c, yaw, new Vector3(1f, wallH / 2.21f, 1f), _synAtlas, side == "S" || side == "N");
            }
            PutAt(body, C_DoorSingle, "Door", new Vector3(-0.05f, yY, cz - half - 0.09f), 0f, new Vector3(1f, 0.85f, 1f), _synAtlas, true);
            foreach (var g in new[] { body, roof }) g.localRotation = Quaternion.Euler(0f, 4f, 0f);

            var props = Group(root, "4_Props");
            FenceRing(props, Plot, yY);
            PutAt(props, P_WoodPile, "Woodpile", new Vector3(1.45f, yY, 1.35f), 90f, Vector3.one * 0.55f, _synAtlas);
            PutAt(props, P_Barrel1, "Barrel", new Vector3(-1.55f, yY, 1.45f), 0f, Vector3.one * 0.65f, _synAtlas);
            PutAt(props, P_Basket, "Basket", new Vector3(-1.1f, yY, 1.6f), 0f, Vector3.one * 1.1f, _synAtlas);
            PutAt(props, P_HayBale, "Hay", new Vector3(1.5f, yY, -1.45f), 20f, Vector3.one * 0.55f, _synAtlas);
            PutAt(props, E_Bush, "Bush", new Vector3(-1.55f, yY, -1.45f), 0f, Vector3.one * 0.22f, _synAtlas);
        }

        // ── the Alanthor House ───────────────────────────────────────────

        const float PlatTop = 0.28f;
        const float PlatZ0 = -1.5f, PlatZ1 = 2f;   // the platform leaves a 0.5 m apron at the front
        const float HallHalf = 1.5f;                // 3 m body
        const float HallZ = 0.2f;                   // hall centre on the plot

        /// <summary>Stucco castle walls round a square (half-size h) at the given
        /// wall height, red columns at the corners, a teal tile pyramid on top.
        /// Returns the wall top.</summary>
        static float StuccoHall(Transform walls, Transform roof, float cz, float h, float wallH, float y, bool door, bool windows, string tag)
        {
            float sx = 2f * h / 5f, sy = wallH / 5f;
            foreach (var (side, yaw) in new[] { ("S", 0f), ("E", 90f), ("N", 180f), ("W", 270f) })
            {
                string piece = side == "S" ? (door ? C_WallDoorArch : C_WallPlain)
                             : side == "N" ? C_WallPlain : (windows ? C_WallWindowArch : C_WallPlain);
                Vector3 c = side switch
                {
                    "S" => new Vector3(0f, y, cz - h), "E" => new Vector3(h, y, cz),
                    "N" => new Vector3(0f, y, cz + h), _ => new Vector3(-h, y, cz),
                };
                PutAt(walls, piece, tag + "Wall" + side, c, yaw, new Vector3(sx, sy, 0.6f), _alStucco, side == "S" || side == "N");
            }
            float ps = 0.42f * sx;
            foreach (var (px, pz) in new[] { (-h, -h), (h, -h), (h, h), (-h, h) })
                PutAt(walls, C_PillarStoneRound, tag + "Column", new Vector3(px, y, cz + pz), 0f, new Vector3(ps, sy, ps), _alRed);
            float top = y + wallH;
            // the square cap: its 0.39-tall skirt is sunk inside the walls so only the pyramid shows
            float cs = (h + 0.12f) / 1.22f, rise = (h + 0.3f) * 0.8f, cy = rise / 0.68f;
            // never name a node "roof": BuildingFactionColorMarker paints such nodes slate
            PutAt(roof, C_CapSquare1, tag + "Cap", new Vector3(0f, top - 0.39f * cy + 0.02f, cz), 0f, new Vector3(cs, cy, cs), _alTile);
            return top;
        }

        static void BuildHouseKit(Transform root, int level)
        {
            var g1 = Group(root, "1_Ground");
            // platform 4 x 3.5 m with a low stone wall on its edge and a stair down the front
            PutAt(g1, C_FloorStone1, "Yard", new Vector3(0f, 0f, (PlatZ0 + PlatZ1) * 0.5f), 0f,
                  new Vector3(2f * Plot / 5f, 1f, (PlatZ1 - PlatZ0) / 5f), _alStone);
            float e = Plot - 0.11f, fz = PlatZ0 + 0.11f, bz = PlatZ1 - 0.11f, gap = 1.4f;
            var ring = new Vector3(0.8f, 0.45f, 0.3f);
            PutAt(g1, H_Base, "RingSa", new Vector3(-(e + gap / 2) / 2f, PlatTop, fz), 0f, new Vector3((e - gap / 2) / 2.5f, ring.y, ring.z), _synAtlas, true);
            PutAt(g1, H_Base, "RingSb", new Vector3((e + gap / 2) / 2f, PlatTop, fz), 0f, new Vector3((e - gap / 2) / 2.5f, ring.y, ring.z), _synAtlas, true);
            PutAt(g1, H_Base, "RingN", new Vector3(0f, PlatTop, bz), 0f, new Vector3(2f * e / 2.5f, ring.y, ring.z), _synAtlas, true);
            PutAt(g1, H_Base, "RingE", new Vector3(e, PlatTop, (fz + bz) / 2f), 90f, new Vector3((bz - fz) / 2.5f, ring.y, ring.z), _synAtlas, false);
            PutAt(g1, H_Base, "RingW", new Vector3(-e, PlatTop, (fz + bz) / 2f), 90f, new Vector3((bz - fz) / 2.5f, ring.y, ring.z), _synAtlas, false);
            PutAt(g1, H_StairsStone, "Stair", new Vector3(0f, 0f, -Plot + 0.31f), 90f, new Vector3(0.62f / 1.87f, PlatTop / 1.27f, 0.55f), _synAtlas);

            var walls = Group(root, "2_Walls"); var roof = Group(root, "3_Cap");
            float top1 = StuccoHall(walls, roof, HallZ, HallHalf, 2.5f, PlatTop, true, true, "");
            var props = Group(root, "6_Props");
            PutCentred(props, P_BracketLamp, "DoorLampR", new Vector3(0.95f, PlatTop + 1.9f, HallZ - HallHalf - 0.25f), 0f, Vector3.one * 0.75f, _synAtlas);
            PutAt(props, E_Bush, "BushL", new Vector3(-1.3f, 0f, -1.66f), 0f, Vector3.one * 0.17f, _synAtlas);
            PutAt(props, E_Bush, "BushR", new Vector3(1.3f, 0f, -1.66f), 0f, Vector3.one * 0.17f, _synAtlas);
            if (level >= 2)
            {
                PutCentred(props, P_BracketLamp, "DoorLampL", new Vector3(-0.95f, PlatTop + 1.9f, HallZ - HallHalf - 0.25f), 0f, Vector3.one * 0.75f, _synAtlas);
                PutAt(props, E_Bush, "BushL2", new Vector3(-1.74f, 0f, -1.72f), 0f, Vector3.one * 0.11f, _synAtlas);
                PutAt(props, E_Bush, "BushR2", new Vector3(1.74f, 0f, -1.72f), 0f, Vector3.one * 0.11f, _synAtlas);
                var loft = Group(root, "4_Loft"); var loftRoof = Group(root, "5_LoftCap");
                float h2 = 0.9f;
                float top2 = StuccoHall(loft, loftRoof, HallZ, h2, 1.5f, top1 + 0.55f, false, true, "Loft");
                if (level == 2)
                    foreach (var sx in new[] { -1f, 1f })
                        PutAt(props, P_StreetLamp1, "GateLamp", new Vector3(sx * 1.05f, 0f, -Plot + 0.3f), 0f, Vector3.one * 0.65f, _synAtlas);
                if (level >= 3)
                {
                    var tier = Group(root, "4_Top"); var tierRoof = Group(root, "5_TopCap");
                    StuccoHall(tier, tierRoof, HallZ, 0.6f, 1.1f, top2 + 0.35f, false, false, "Top");
                    // the torii: two red columns and two red beams across the stair
                    float gz = -Plot + 0.3f;
                    foreach (var sx in new[] { -1f, 1f })
                        PutAt(props, C_PillarStoneRound, "Torii", new Vector3(sx * 0.85f, 0f, gz), 0f, new Vector3(0.17f, 0.5f, 0.17f), _alRed);
                    PutCentred(props, P_TBeam, "ToriiLintel", new Vector3(0f, 2.55f, gz), 0f, new Vector3(0.6f, 0.55f, 0.54f), _alRed, true);
                    PutCentred(props, P_TBeam, "ToriiTie", new Vector3(0f, 2.05f, gz), 0f, new Vector3(0.45f, 0.4f, 0.44f), _alRed, true);
                    foreach (var sx in new[] { -1f, 1f })
                        PutCentred(props, P_BracketLamp, "ToriiLamp", new Vector3(sx * 0.5f, 1.65f, gz), 0f, Vector3.one * 0.6f, _synAtlas);
                }
            }
        }

        // ── previews: every prefab from four sides and above ─────────────

        static void RenderHouseLine()
        {
            Directory.CreateDirectory(HousePreviewDir);
            var views = new (string tag, Vector3 dir)[]
            {
                ("S", new Vector3(0f, 0.75f, -1f)), ("E", new Vector3(1f, 0.75f, 0f)),
                ("N", new Vector3(0f, 0.75f, 1f)), ("W", new Vector3(-1f, 0.75f, 0f)), ("Top", new Vector3(0f, 1f, -0.001f)),
            };
            var items = new (string label, string path)[]
            {
                ("Hut", HutDir + "Art/Hut_Age_0.prefab"), ("HouseLv1", HouseDir + "Art/alanthor_House_lv1.prefab"),
                ("HouseLv2", HouseDir + "Art/alanthor_House_lv2.prefab"), ("HouseLv3", HouseDir + "Art/alanthor_House_lv3.prefab"),
            };
            foreach (var (label, path) in items)
                foreach (var (tag, dir) in views)
                    Report.Add(RenderOne(label, path, -1, 0, HousePreviewDir, dir, "_" + tag));
        }
    }
}
