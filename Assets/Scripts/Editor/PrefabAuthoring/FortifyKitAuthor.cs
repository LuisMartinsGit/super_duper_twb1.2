// FortifyKitAuthor — the art of the Sect of Fortitude's Fortify power
// (2026-11-11; developer: "Fortify should always have a 3x3 footprint (redraw
// the assets)"). Fortify raises one of three structures, each its own
// building (Renewal_Tower / Renewal_Fortification / Renewal_Fortress, SOs in
// Sects/Renewal/Abilities/RaiseAnew/), and every one stands on the SAME
// 3 x 3-cell (6 m x 6 m) footprint. The prefabs are composed here from nested
// Synty castle-kit pieces at their native scale on a 15 m stone plinth (the
// "Footprint" group carrying FootprintBounds), which the footprint fit draws
// at 0.4 — the detail scale of the other Alanthor buildings. Nothing leaves
// the plinth's square; the levels read by height and armament, never ground:
//   I   Tower         — a round arrow-slit tower, timber gallery, cone.
//   II  Fortification — a battlemented bastion round a raised deck with a
//                       galleried tower on it, braziers at the corners.
//   III Fortress      — a two-storey curtain, four coned corner turrets and a
//                       square keep with a gold corbel band under a spire.
// Alanthor look (Art_Direction.md): castle stone, roofs on Castle_Roof_Player
// (tinted in the owner's colour), flags on Flag_Symbol_Player, warm lamps.
//
//   Waning Border > Art > Author Fortify Art (3x3)
//   Waning Border > Art > Measure Fortify Pieces   (bounds of the kit pieces)
//
// AlanthorKitAuthor no longer builds these three; this is their only author.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Data;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static partial class FeraldisKitAuthor
    {
        const string FortifyDir = TT + "Sects/Renewal/Abilities/RaiseAnew/";
        const string FortifyPreviewDir = "Temp/fortify-previews";
        const string AlanthorArtDir = TT + "Civs/Alanthor/Buildings/Art/";
        /// <summary>Half the plinth, in kit metres: 15 m, which the footprint
        /// fit draws on the 3 x 3-cell (6 m) footprint at 0.4 — the same detail
        /// scale as the other Alanthor buildings (Watch Tower 0.39, House 0.40).</summary>
        const float FortHalf = 7.5f;

        const string FK_CrownS = "Castle/K_SM_Bld_Castle_Battlements_S_Corner_01";
        const string FK_Wall = "Castle/K_SM_Bld_Castle_Wall_01";
        const string FK_Floor = "Castle/K_SM_Bld_Castle_Floor_Stone_01";
        const string FK_Cone = "Castle/K_SM_Bld_Castle_Roof_Cap_Round_04";
        const string FK_Pyramid = "Castle/K_SM_Bld_Castle_Roof_Cap_Square_04";
        const string FK_WallSlit = "Castle/K_SM_Bld_Castle_Wall_Arrowslit_01";
        const string FK_WallDoor = "Castle/K_SM_Bld_Castle_Wall_Door_01";
        const string FK_Block = "Castle/K_SM_Bld_Castle_Wall_Block_01";
        const string FK_Batt = "Castle/K_SM_Bld_Castle_Battlements_01";
        const string FK_BattPillar = "Castle/K_SM_Bld_Castle_Battlements_Pillar_01";
        const string FK_Corbels = "Castle/K_SM_Bld_Castle_Wall_Corbels_01";
        const string FK_Banner = "Props/Banners/K_SM_Prop_Flag_01";
        const string FK_Lamp = "Props/K_SM_Prop_Bracket_Lamp_01";
        const string FK_Brazier = "Props/BattleGround/K_SM_Prop_Camp_Brazier_01";

        static Material _fkRoof, _fkFlag, _fkWhite, _fkGold;

        // ═════════════════════════════════════════════════════════════════
        [MenuItem("Waning Border/Art/Author Fortify Art (3x3)")]
        public static void AuthorFortifyArt()
        {
            Report.Clear(); _failures = 0;
            try
            {
                _fkRoof = LoadMat(AlanthorArtDir + "Castle_Roof_Player.mat");
                _fkFlag = LoadMat(AlanthorArtDir + "Flag_Symbol_Player.mat");
                _fkWhite = LoadMat(AlanthorArtDir + "Castle_Wall_White.mat");
                _fkGold = LoadMat(AlanthorArtDir + "Castle_Wall_golden.mat");
                FortifyForm("RenewalTower", "Renewal_Tower", FortifyTower);
                FortifyForm("RenewalFortification", "Renewal_Fortification", FortifyBastion);
                FortifyForm("RenewalFortress", "Renewal_Fortress", FortifyKeep);
                AssetDatabase.SaveAssets();
                BakeFortifyFirePoints();
                AssetDatabase.SaveAssets();
                RenderFortifyLineup();
            }
            catch (Exception e) { Fail("AuthorFortifyArt", e.ToString()); }
            string text = $"[FortifyKit] done — {Report.Count} entries, {_failures} failure(s)\n" + string.Join("\n", Report);
            Debug.Log(text);
            try { Directory.CreateDirectory(FortifyPreviewDir); File.WriteAllText(FortifyPreviewDir + "/report.txt", text); } catch (Exception) { }
        }

        static Material LoadMat(string path)
            => AssetDatabase.LoadAssetAtPath<Material>(path) ?? throw new Exception("material missing " + path);

        static void FortifyForm(string stem, string id, Action<Transform> build)
        {
            PlainInPlace(FortifyDir + $"Art/{id}_Age_0.prefab", FortifyDir + stem + ".asset", id + "_Age_0", root =>
            {
                // The plinth IS the footprint: 3 x 3 castle floor stones, 15 m,
                // the only renderers the footprint fit measures (FootprintBounds).
                var fp = Group(root, "Footprint");
                fp.gameObject.AddComponent<TheWaningBorder.Rendering.FootprintBounds>();
                Paving(fp, "Plinth", 0f, null);
                build(root);
                CheckInside(id, root);
            });
        }

        // ── Level I: a round arrow-slit tower under a gallery and a cone ──
        static void FortifyTower(Transform root)
        {
            var walls = Group(root, "TowerWalls");
            float top = RoundTower(walls, Vector3.zero, true, new[] { "Door", "Slit", "Half" }, null, 1);
            float gallery = Gallery(Group(root, "Gallery"), new Vector3(0f, top, 0f), true);
            Cone(Group(root, "Cap"), "Cone", new Vector3(0f, gallery, 0f), 9.4f, 0.42f);
            var props = Group(root, "Props");
            Hang(props, "BannerN", new Vector3(0f, top - 1.0f, 3.95f), 180f, 0.9f);
            Hang(props, "BannerS", new Vector3(0f, top - 1.0f, -3.95f), 0f, 0.9f);
            Lamp(props, "LampE", new Vector3(3.6f, 3.0f, -1.6f));
            Lamp(props, "LampW", new Vector3(-3.6f, 3.0f, -1.6f));
            foreach (var (n, x, z) in new[] { ("PostNE", 6.4f, 6.4f), ("PostNW", -6.4f, 6.4f), ("PostSE", 6.4f, -6.4f), ("PostSW", -6.4f, -6.4f) })
                Snap(Put(props, FK_BattPillar, n, Vector3.zero, 0f, Vector3.one * 1.3f, Style.Neutral), new Vector3(x, 0f, z), false);
        }

        // ── Level II: a battlemented bastion with a galleried tower on its deck ──
        static void FortifyBastion(Transform root)
        {
            var bastion = Group(root, "Bastion");
            float wallTop = SquareCurtain(bastion, new[] { "Wall" }, _fkWhite, _fkWhite);
            Paving(bastion, "Deck", wallTop, _fkWhite, 0.95f);
            var walls = Group(root, "TowerWalls");
            float top = RoundTower(walls, new Vector3(0f, wallTop, 0f), true, new[] { "Slit", "Wall" }, _fkWhite, -1);
            float gallery = Gallery(Group(root, "Gallery"), new Vector3(0f, top, 0f), true);
            Cone(Group(root, "Cap"), "Cone", new Vector3(0f, gallery, 0f), 9.4f, 0.42f);
            var props = Group(root, "Props");
            Hang(props, "BannerN", new Vector3(-3.6f, wallTop - 0.6f, 7.35f), 180f, 0.8f);
            Hang(props, "BannerN", new Vector3(3.6f, wallTop - 0.6f, 7.35f), 180f, 0.8f);
            Hang(props, "BannerS", new Vector3(-3.6f, wallTop - 0.6f, -7.35f), 0f, 0.8f);
            Hang(props, "BannerS", new Vector3(3.6f, wallTop - 0.6f, -7.35f), 0f, 0.8f);
            Lamp(props, "LampE", new Vector3(1.9f, 3.0f, -7.35f));
            Lamp(props, "LampW", new Vector3(-1.9f, 3.0f, -7.35f));
            foreach (var (n, x, z) in new[] { ("BrazierNE", 5.3f, 5.3f), ("BrazierNW", -5.3f, 5.3f), ("BrazierSE", 5.3f, -5.3f), ("BrazierSW", -5.3f, -5.3f) })
                Snap(Put(props, FK_Brazier, n, Vector3.zero, 0f, Vector3.one * 1.6f, Style.Neutral), new Vector3(x, wallTop, z), false);
        }

        // ── Level III: a small keep inside four coned corner turrets ──────
        static void FortifyKeep(Transform root)
        {
            var curtain = Group(root, "Curtain");
            float wallTop = SquareCurtain(curtain, new[] { "Wall", "Slit" }, _fkWhite, _fkGold);
            Paving(curtain, "Deck", wallTop, _fkWhite, 0.95f);

            // Keep: five stacked solid blocks (the texture is never stretched),
            // a gold corbel band under a player-colour pyramid.
            var keep = Group(root, "Keep");
            const float side = 5.5f;
            float kTop = 0f;
            for (int i = 0; i < 5; i++)
            {
                var blk = Put(keep, FK_Block, "Storey" + Letter(i), Vector3.zero, 0f, new Vector3(side / 5.5f, 1f, side / 5.5f), Style.Neutral);
                kTop = Snap(blk, new Vector3(0f, kTop, 0f), false); Restyle(blk, _fkWhite);
            }
            foreach (var (n, yaw, dx, dz) in new[] { ("CorbelS", 0f, 0f, -1f), ("CorbelN", 180f, 0f, 1f), ("CorbelE", 90f, 1f, 0f), ("CorbelW", 270f, -1f, 0f) })
            {
                var c = Put(keep, FK_Corbels, n, Vector3.zero, yaw, new Vector3((side + 0.6f) / 5f, 1f, 1f), Style.Neutral);
                MeshBounds(c, out var cb);
                c.transform.position += new Vector3(dx * (side * 0.5f + 0.1f) - cb.center.x, kTop - cb.max.y, dz * (side * 0.5f + 0.1f) - cb.center.z);
                Restyle(c, _fkGold);
            }
            var merlons = Group(keep, "KeepMerlons");
            foreach (var (n, yaw, x, z) in new[] { ("S", 0f, 0f, -1f), ("N", 180f, 0f, 1f), ("E", 90f, 1f, 0f), ("W", 270f, -1f, 0f) })
            {
                var b = Put(merlons, FK_Batt, "Merlons" + n, Vector3.zero, yaw, new Vector3(side / 5f, 1f, 1f), Style.Neutral);
                Snap(b, new Vector3(x * (side * 0.5f - 0.25f), kTop, z * (side * 0.5f - 0.25f)), false); Restyle(b, _fkGold);
            }
            var pyr = Put(Group(root, "Cap"), FK_Pyramid, "Spire", Vector3.zero, 0f, new Vector3((side - 1.0f) / 3.74f, 0.75f, (side - 1.0f) / 3.74f), Style.Neutral);
            Snap(pyr, new Vector3(0f, kTop, 0f), false); Restyle(pyr, null);

            // Four corner turrets (the kit's small tower), cones in the player colour.
            const float r = 2.75f, c0 = FortHalf - r;
            foreach (var (n, x, z) in new[] { ("NE", c0, c0), ("NW", -c0, c0), ("SE", c0, -c0), ("SW", -c0, -c0) })
            {
                var w = Group(root, "TowerWalls" + n);
                float t = RoundTower(w, new Vector3(x, 0f, z), false, new[] { "Wall", "Slit", "Slit" }, _fkWhite, -1);
                var crown = Group(root, "Crown" + n);
                for (int k = 0; k < 4; k++)
                {
                    var rot = Quaternion.Euler(0f, k * 90f, 0f);
                    var go = Put(crown, FK_CrownS, "Crown" + Letter(k), new Vector3(x, t, z) + rot * new Vector3(2.5f, 0f, 0f), k * 90f, Vector3.one, Style.Neutral);
                    Restyle(go, _fkGold);
                }
                Cone(Group(root, "Cap" + n), "Cone", new Vector3(x, t + 0.6f, z), 2f * r - 0.4f, 0.55f);
            }
            var props = Group(root, "Props");
            foreach (var (n, yaw, x, z) in new[] { ("BannerS", 0f, 0f, -1f), ("BannerN", 180f, 0f, 1f), ("BannerE", 270f, 1f, 0f), ("BannerW", 90f, -1f, 0f) })
                Hang(props, n, new Vector3(x * (side * 0.5f + 0.1f), kTop - 2.2f, z * (side * 0.5f + 0.1f)), yaw, 0.85f);
            Hang(props, "BannerGateE", new Vector3(2.6f, wallTop - 0.8f, -7.35f), 0f, 0.8f);
            Hang(props, "BannerGateW", new Vector3(-2.6f, wallTop - 0.8f, -7.35f), 0f, 0.8f);
            Lamp(props, "LampE", new Vector3(1.4f, 3.0f, -7.35f));
            Lamp(props, "LampW", new Vector3(-1.4f, 3.0f, -7.35f));
        }

        // ═════════════════════════════════════════════════════════════════
        // Fortify helpers. Native-scale castle kit, measured 2026-11-11 with
        // Measure Fortify Pieces: a Corner_S quarter is 5 m tall, 2.5 m from
        // the tower axis to the wall (Corner_M: 3.75 m), its pivot that far out
        // along local +X; Floor_Stone_01 is a 5 m tile; walls are 5 m modules.
        // ═════════════════════════════════════════════════════════════════

        /// <summary>3 x 3 Floor_Stone tiles (15 m at <paramref name="scale"/> 1)
        /// whose top is at <paramref name="topY"/>.</summary>
        static void Paving(Transform parent, string name, float topY, Material mat, float scale = 1f)
        {
            float step = 5f * scale;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    var t = Put(parent, FK_Floor, name + Letter(i + 1) + Letter(j + 1), Vector3.zero, 0f, new Vector3(scale, 1f, scale), Style.Neutral);
                    MeshBounds(t, out var b);
                    t.transform.position += new Vector3(i * step - b.center.x, topY - b.max.y, j * step - b.center.z);
                    Restyle(t, mat);
                }
        }

        /// <summary>A round tower of four Corner quarters per course (the kit's
        /// M tower, 8 m across, or S, 5.5 m), standing on <paramref name="baseAt"/>,
        /// a stone floor on top. Course kinds: Wall, Slit, Door (the door only on
        /// quarter <paramref name="doorQuarter"/>), Half. Returns the top of the walls.</summary>
        static float RoundTower(Transform parent, Vector3 baseAt, bool medium, string[] courses, Material wall, int doorQuarter)
        {
            string size = medium ? "M" : "S";
            float reach = medium ? 3.75f : 2.5f;
            float y = baseAt.y;
            for (int c = 0; c < courses.Length; c++)
            {
                float top = y;
                for (int k = 0; k < 4; k++)
                {
                    string kind = courses[c] == "Door" && k != doorQuarter ? "Wall" : courses[c];
                    string piece = kind switch
                    {
                        "Slit" => $"Castle/K_SM_Bld_Castle_Wall_Corner_{size}_Arrowslit_01",
                        "Door" => $"Castle/K_SM_Bld_Castle_Wall_Corner_{size}_Door_01",
                        "Half" => $"Castle/K_SM_Bld_Castle_Wall_Half_Corner_{size}_01",
                        _ => $"Castle/K_SM_Bld_Castle_Wall_Corner_{size}_01",
                    };
                    var rot = Quaternion.Euler(0f, k * 90f, 0f);
                    var go = Put(parent, piece, $"Course{Letter(c)}{Letter(k)}",
                                 new Vector3(baseAt.x, y, baseAt.z) + rot * new Vector3(reach, 0f, 0f),
                                 k * 90f, Vector3.one, Style.Neutral);
                    Restyle(go, wall);
                    MeshBounds(go, out var b); top = Mathf.Max(top, b.max.y);
                }
                y = top;
            }
            for (int k = 0; k < 4; k++)
            {
                var f = Put(parent, $"Castle/K_SM_Bld_Castle_Floor_Stone_Round_{size}_01", "Floor" + Letter(k),
                            new Vector3(baseAt.x, y - 0.03f, baseAt.z), k * 90f, Vector3.one, Style.Neutral);
                Restyle(f, wall);
            }
            return y;
        }

        /// <summary>The kit's timber fighting gallery (hoarding) round a tower
        /// top; named Hoarding_Corner so the fire-point baker shoots from it.
        /// Returns the height a cone sits at (the gallery's eaves).</summary>
        static float Gallery(Transform parent, Vector3 at, bool medium)
        {
            float reach = medium ? 3.75f : 2.5f;
            string piece = medium ? "Castle/K_SM_Bld_Castle_Hoarding_Corner_M_01" : "Castle/K_SM_Bld_Castle_Hoarding_Corner_S_01";
            for (int k = 0; k < 4; k++)
            {
                var rot = Quaternion.Euler(0f, k * 90f, 0f);
                var go = Put(parent, piece, "Hoarding_Corner" + Letter(k), at + rot * new Vector3(reach, 0f, 0f), k * 90f, Vector3.one, Style.Neutral);
                Restyle(go, null);
            }
            return at.y + 2.0f;
        }

        /// <summary>A player-colour cone of <paramref name="diameter"/> standing on <paramref name="at"/>.</summary>
        static void Cone(Transform parent, string name, Vector3 at, float diameter, float heightScale)
        {
            float k = diameter / 2.94f;
            var go = Put(parent, FK_Cone, name, Vector3.zero, 0f, new Vector3(k, k * heightScale, k), Style.Neutral);
            Snap(go, at, false); Restyle(go, null);
        }

        /// <summary>A 15 m square curtain on the plinth: three 5 m modules a
        /// side, one course per entry of <paramref name="courses"/> (Wall or
        /// Slit; the south middle module of the first course is the door),
        /// battlements and corner pillars on top in <paramref name="trim"/>.
        /// The outer face is 0.25 m inside the footprint, room for banners and
        /// lamps. Returns the wall-walk height.</summary>
        static float SquareCurtain(Transform parent, string[] courses, Material wall, Material trim)
        {
            const float c = FortHalf - 0.5f;               // wall centre line
            const float module = 2f * (FortHalf - 0.25f) / 3f;
            var sides = new[] { ("S", 0f, 0f, -1f), ("N", 180f, 0f, 1f), ("E", 90f, 1f, 0f), ("W", 270f, -1f, 0f) };
            float y = 0f;
            for (int course = 0; course < courses.Length; course++)
            {
                float top = y;
                foreach (var (side, yaw, nx, nz) in sides)
                    for (int m = -1; m <= 1; m++)
                    {
                        bool door = course == 0 && side == "S" && m == 0;
                        string piece = door ? FK_WallDoor : courses[course] == "Slit" && m != 0 ? FK_WallSlit : FK_Wall;
                        var w = Put(parent, piece, $"Wall{side}{Letter(course)}{Letter(m + 1)}", Vector3.zero, yaw, new Vector3(module / 5f, 1f, 1f), Style.Neutral);
                        // along the side: x for N/S walls, z for E/W walls
                        Vector3 at = nz != 0f ? new Vector3(m * module, y, nz * c) : new Vector3(nx * c, y, m * module);
                        top = Mathf.Max(top, Snap(w, at, false)); Restyle(w, wall);
                    }
                y = top;
            }
            foreach (var (side, yaw, nx, nz) in sides)
                for (int m = -1; m <= 1; m++)
                {
                    var b = Put(parent, FK_Batt, $"Merlons{side}{Letter(m + 1)}", Vector3.zero, yaw, new Vector3(module / 5f, 1f, 1f), Style.Neutral);
                    Vector3 at = nz != 0f ? new Vector3(m * module, y, nz * c) : new Vector3(nx * c, y, m * module);
                    Snap(b, at, false); Restyle(b, trim ?? wall);
                }
            foreach (var (n, x, z) in new[] { ("NE", c, c), ("NW", -c, c), ("SE", c, -c), ("SW", -c, -c) })
            {
                var p = Put(parent, FK_BattPillar, "Pillar" + n, Vector3.zero, 0f, Vector3.one, Style.Neutral);
                Snap(p, new Vector3(x, y, z), false); Restyle(p, trim ?? wall);
            }
            return y;
        }

        /// <summary>A hanging player-colour banner whose top is at <paramref name="top"/>.</summary>
        static void Hang(Transform parent, string name, Vector3 top, float yaw, float scale)
        {
            var go = Put(parent, FK_Banner, name, Vector3.zero, yaw, Vector3.one * scale, Style.Neutral);
            MeshBounds(go, out var b);
            go.transform.position += new Vector3(top.x - b.center.x, top.y - b.max.y, top.z - b.center.z);
            Restyle(go, null);
        }

        static void Lamp(Transform parent, string name, Vector3 centre)
        {
            var go = Put(parent, FK_Lamp, name, Vector3.zero, 0f, Vector3.one * 0.7f, Style.Neutral);
            MeshBounds(go, out var b);
            go.transform.position += centre - b.center;
        }

        /// <summary>Moves <paramref name="go"/> so its bounds stand centred on
        /// (x, z) of <paramref name="at"/> with the bottom (or, with
        /// <paramref name="byTop"/>, the top) at its y. Returns the new top.</summary>
        static float Snap(GameObject go, Vector3 at, bool byTop)
        {
            MeshBounds(go, out var b);
            float dy = byTop ? at.y - b.max.y : at.y - b.min.y;
            go.transform.position += new Vector3(at.x - b.center.x, dy, at.z - b.center.z);
            return b.max.y + dy;
        }

        /// <summary>Castle stone -> <paramref name="wall"/> (when given), every
        /// castle roof -> the player roof, every symbol flag -> the player flag.</summary>
        static void Restyle(GameObject go, Material wall)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (m == null) continue;
                    Material to = null;
                    if (m.name.StartsWith("Castle_Roof", StringComparison.OrdinalIgnoreCase)) to = _fkRoof;
                    else if (m.name.StartsWith("Flag_Symbol", StringComparison.OrdinalIgnoreCase)) to = _fkFlag;
                    else if (wall != null && m.name.StartsWith("Castle_Wall_01", StringComparison.OrdinalIgnoreCase)) to = wall;
                    if (to != null && to != m) { mats[i] = to; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        /// <summary>Every renderer must stay inside the 6 m square (taller is fine).</summary>
        static void CheckInside(string id, Transform root)
        {
            MeshBounds(root.gameObject, out var b);
            Report.Add($"{id}: bounds min {b.min.ToString("F2")} max {b.max.ToString("F2")}");
            if (b.min.x < -FortHalf - 0.05f || b.max.x > FortHalf + 0.05f || b.min.z < -FortHalf - 0.05f || b.max.z > FortHalf + 0.05f)
                Fail(id, $"spills out of the {2 * FortHalf} m footprint: {b.min} .. {b.max}");
        }

        /// <summary>Fire points for the three SOs only — the shared baker
        /// rewrites every shooter in the game, which is not this tool's to touch.</summary>
        static void BakeFortifyFirePoints()
        {
            var measure = typeof(BuildingFirePointBaker).GetMethod("Measure",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (measure == null) { Fail("firepoints", "BuildingFirePointBaker.Measure not found"); return; }
            foreach (var stem in new[] { "RenewalTower", "RenewalFortification", "RenewalFortress" })
            {
                var so = AssetDatabase.LoadAssetAtPath<BuildingDefSO>(FortifyDir + stem + ".asset");
                if (so == null || so.prefab == null) { Fail(stem, "SO or prefab missing for fire points"); continue; }
                so.firePoints = (Vector3[])measure.Invoke(null, new object[] { so.prefab, so.id, so.id, Cultures.None });
                EditorUtility.SetDirty(so);
                Report.Add($"{stem}: footprint {so.footprintCells} cells, fire points " + string.Join(" ", so.firePoints.Select(p => p.ToString("F2"))));
            }
        }

        /// <summary>The three Fortify levels in a row between an Alanthor Watch
        /// Tower (L1) and House (L1), each drawn at its in-game footprint fit on
        /// a dark pad the size of its footprint -> Temp/fortify-previews/Lineup.png.</summary>
        [MenuItem("Waning Border/Art/Render Fortify Lineup")]
        public static void RenderFortifyLineup()
        {
            Directory.CreateDirectory(FortifyPreviewDir);
            GameObject Level(string path) => AssetDatabase.LoadAssetAtPath<BuildingLevelDefSO>(path)?.prefab;
            GameObject Base(string stem) => AssetDatabase.LoadAssetAtPath<BuildingDefSO>(FortifyDir + stem + ".asset")?.prefab;
            var rows = new List<(GameObject prefab, string id, byte culture)>
            {
                (Level(ALA + "Tower/WatchTower_Lvl1.asset"), "Alanthor_Tower", Cultures.Alanthor),
                (Base("RenewalTower"), "Renewal_Tower", Cultures.None),
                (Base("RenewalFortification"), "Renewal_Fortification", Cultures.None),
                (Base("RenewalFortress"), "Renewal_Fortress", Cultures.None),
                (Level(ALA + "House/House_Lvl1.asset"), "Hut", Cultures.Alanthor),
            };
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                LineupShot(rows, 64, 64, null, 30f);      // warm-up: the first offscreen render draws placeholders
                LineupShot(rows, 2400, 1100, FortifyPreviewDir + "/Lineup.png", 30f);
                LineupShot(rows, 2400, 1100, FortifyPreviewDir + "/Lineup_High.png", 55f);
            }
            finally { ShaderUtil.allowAsyncCompilation = async; }
        }

        static void LineupShot(List<(GameObject prefab, string id, byte culture)> rows, int w, int h, string outPng, float tilt)
        {
            var created = new List<GameObject>();
            var stage = new Vector3(0f, -4200f, 0f);
            Material Flat(Color c)
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave };
                m.SetColor("_BaseColor", c);
                return m;
            }
            try
            {
                const float spacing = 9f;
                float x = -(rows.Count - 1) * spacing * 0.5f;
                var log = new List<string>();
                foreach (var row in rows)
                {
                    if (row.prefab == null) { log.Add($"{row.id}: no prefab"); x += spacing; continue; }
                    var go = UnityEngine.Object.Instantiate(row.prefab);
                    go.hideFlags = HideFlags.HideAndDontSave;
                    created.Add(go);
                    var size = BuildingSizeConfig.GetSize(row.id);
                    float scale = PresentationSpawnSystem.ComputeBuildingViewScale(go, row.prefab, row.id, row.culture, size.x, size.y, out _, out Vector3 offset);
                    go.transform.localScale = Vector3.one * scale;
                    go.transform.position = stage + new Vector3(x, 0f, 0f) - offset * scale;
                    TheWaningBorder.Rendering.BuildingFactionColorMarker.Apply(go, new Color(0.85f, 0.15f, 0.15f));
                    var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    pad.hideFlags = HideFlags.HideAndDontSave; created.Add(pad);
                    pad.transform.position = stage + new Vector3(x, -0.05f, 0f);
                    pad.transform.localScale = new Vector3(size.x, 0.06f, size.y);
                    pad.GetComponent<Renderer>().sharedMaterial = Flat(new Color(0.12f, 0.13f, 0.11f));
                    if (MeshBounds(go, out var b))
                        log.Add($"{row.id}: footprint {size.x}x{size.y} m, scale {scale:F2}, drawn {b.size.x:F1} x {b.size.y:F1} x {b.size.z:F1} (x {b.min.x - x - stage.x:F2}..{b.max.x - x - stage.x:F2}, z {b.min.z - stage.z:F2}..{b.max.z - stage.z:F2})");
                    x += spacing;
                }
                if (outPng != null) Report.Add("lineup: " + string.Join("\n  ", log));

                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.hideFlags = HideFlags.HideAndDontSave; created.Add(ground);
                ground.transform.position = stage + new Vector3(0f, -0.1f, 0f);
                ground.transform.localScale = new Vector3(14f, 1f, 14f);
                ground.GetComponent<Renderer>().sharedMaterial = Flat(new Color(0.32f, 0.40f, 0.28f));

                var camGo = new GameObject("FortifyCam") { hideFlags = HideFlags.HideAndDontSave }; created.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 30f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 500f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.55f, 0.66f, 0.78f, 1f);
                const float dist = 62f;
                var focus = stage + new Vector3(0f, 5f, 0f);
                var back = new Vector3(0f, Mathf.Sin(tilt * Mathf.Deg2Rad), -Mathf.Cos(tilt * Mathf.Deg2Rad));
                camGo.transform.position = focus + back * dist;
                camGo.transform.rotation = Quaternion.LookRotation(-back, Vector3.up);
                var sunGo = new GameObject("FortifySun") { hideFlags = HideFlags.HideAndDontSave }; created.Add(sunGo);
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.6f; sun.color = new Color(1f, 0.95f, 0.86f);
                sun.shadows = LightShadows.Soft;
                sunGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
                var fillGo = new GameObject("FortifyFill") { hideFlags = HideFlags.HideAndDontSave }; created.Add(fillGo);
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Directional; fill.intensity = 0.45f; fill.color = new Color(0.78f, 0.84f, 1f);
                fillGo.transform.rotation = Quaternion.Euler(30f, 140f, 0f);

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

        static readonly string[] FortifyMeasureList =
        {
            "Castle/K_SM_Bld_Castle_Wall_Corner_S_01", "Castle/K_SM_Bld_Castle_Wall_Corner_S_Arrowslit_01",
            "Castle/K_SM_Bld_Castle_Wall_Corner_S_Door_01", "Castle/K_SM_Bld_Castle_Wall_Half_Corner_S_01",
            "Castle/K_SM_Bld_Castle_Hoarding_Corner_S_01", "Castle/K_SM_Bld_Castle_Battlements_S_Corner_01",
            "Castle/K_SM_Bld_Castle_Wall_Corner_M_01", "Castle/K_SM_Bld_Castle_Wall_Half_Corner_M_01",
            "Castle/K_SM_Bld_Castle_Roof_Cap_Round_01", "Castle/K_SM_Bld_Castle_Roof_Cap_Round_02",
            "Castle/K_SM_Bld_Castle_Roof_Cap_Round_03", "Castle/K_SM_Bld_Castle_Roof_Cap_Round_04",
            "Castle/K_SM_Bld_Castle_Roof_Cap_Square_01", "Castle/K_SM_Bld_Castle_Roof_Cap_Square_02",
            "Castle/K_SM_Bld_Castle_Roof_Cap_Square_03", "Castle/K_SM_Bld_Castle_Roof_Cap_Square_04",
            "Castle/K_SM_Bld_Castle_Floor_Stone_01", "Castle/K_SM_Bld_Castle_Floor_Stone_Round_S_01",
            "Castle/K_SM_Bld_Castle_Battlements_01", "Castle/K_SM_Bld_Castle_Battlements_01_Half",
            "Castle/K_SM_Bld_Castle_Battlements_03", "Castle/K_SM_Bld_Castle_Battlements_Pillar_01",
            "Castle/K_SM_Bld_Castle_Wall_01", "Castle/K_SM_Bld_Castle_Wall_04", "Castle/K_SM_Bld_Castle_Wall_Arrowslit_01",
            "Castle/K_SM_Bld_Castle_Wall_Door_01", "Castle/K_SM_Bld_Castle_Wall_Block_01", "Castle/K_SM_Bld_Castle_Wall_Bottom_01",
            "Castle/K_SM_Bld_Castle_Wall_Tower_S_01", "Castle/K_SM_Bld_Castle_Wall_Corbels_01",
            "Castle/K_SM_Bld_Castle_Hoarding_01", "Castle/K_SM_Bld_Castle_Hoarding_Corner_M_01",
            "Castle/K_SM_Bld_Castle_Pillar_Stone_01", "Castle/K_SM_Bld_Keep_Pillar_01",
            "Castle/K_SM_Bld_Castle_Door_Single_01", "Castle/K_SM_Bld_Stairs_Straight_01",
            "Props/Banners/K_SM_Prop_Banner_01", "Props/Banners/K_SM_Prop_Flag_01", "Props/Banners/K_SM_Prop_Flag_12",
            "Props/Banners/K_SM_Prop_Flag_Wall_Attachment_01", "Props/Banners/K_SM_Prop_Flag_Pole_01",
            "Props/K_SM_Prop_Bracket_Lamp_01", "Props/K_SM_Prop_Torch_01", "Props/BattleGround/K_SM_Prop_Camp_Brazier_01",
        };

        [MenuItem("Waning Border/Art/Measure Fortify Pieces")]
        public static void MeasureFortifyPieces()
        {
            var sb = new StringBuilder();
            var holder = new GameObject("FortifyMeasure");
            try
            {
                foreach (var piece in FortifyMeasureList)
                {
                    try
                    {
                        foreach (float yaw in new[] { 0f, 90f })
                        {
                            var go = Put(holder.transform, piece, "m", Vector3.zero, yaw, Vector3.one, Style.Neutral);
                            MeshBounds(go, out var b);
                            var mats = go.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                                .Where(m => m != null).Select(m => m.name).Distinct();
                            sb.AppendLine($"{piece} yaw{yaw}: min {b.min.ToString("F2")} max {b.max.ToString("F2")} size {b.size.ToString("F2")} mats [{string.Join(",", mats)}]");
                            UnityEngine.Object.DestroyImmediate(go);
                        }
                    }
                    catch (Exception e) { sb.AppendLine($"{piece}: FAIL {e.Message}"); }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
            File.WriteAllText("Temp/fortify-measure.txt", sb.ToString());
            Debug.Log("[FortifyKit] measured -> Temp/fortify-measure.txt");
        }
    }
}
