// AlanthorKitAuthor — every Age 0 and Alanthor building, sect building and
// wall part as an EDITABLE prefab of nested Synty Fantasy Kingdom pieces
// (2026-10-27; developer: "Delete all buildings ... Create all buildings
// again, following the themes"). One menu rebuilds the lot, deletes the
// retired Blender art (FBX / .blend) beside the new prefabs, wires every SO,
// and renders a level comparison or four-side preview of each into
// Temp/alanthor-kit-previews/.
//
//   Waning Border > Art > Author Age 0 + Alanthor Art (Synty)
//
// Themes
//   Age 0 — dark ages: mud yards, plank hoarding walls on beam posts, thatch
//   gables, post-and-rail fences, a player-colour pennant. The Hut
//   (HouseKitAuthor) is the idiom's reference.
//   Alanthor — white stucco castle walls, red Roman columns, teal tile
//   pyramids and gables, stone platforms inside a low stone wall with a stair,
//   torii gates and hanging lanterns, player-colour flags. The House
//   (HouseKitAuthor) is the idiom's reference. Levels grow tiers, wings and
//   props inside the SAME plot, never the plot (the footprint fit stays 1).
//
// Rules the pieces obey: every model fills its footprint edge to edge and is
// placed by MEASURED bounds (PutAt); no node is named "roof" (the
// faction-colour pass paints such nodes slate); Age 0 art uses no culture's
// material (edition A strips the other cultures).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Data;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static partial class FeraldisKitAuthor
    {
        const string AGE0 = TT + "Age0/Buildings/";
        const string ALA = TT + "Civs/Alanthor/Buildings/";
        const string SECTS = TT + "Sects/";
        const string AlaPreviewDir = "Temp/alanthor-kit-previews";

        // extra pieces
        const string C_Batt1 = "Castle/SM_Bld_Castle_Battlements_01";
        const string C_BattCornerS = "Castle/SM_Bld_Castle_Battlements_S_Corner_01";
        const string C_Spire = "Castle/SM_Bld_Castle_Roof_Cap_Square_05";
        const string P_MarketStall = "Props/SM_Prop_Market_Stall_01";
        const string P_WeaponRack5 = "Props/SM_Prop_Weapon_Rack_05";
        const string P_StatueKnight = "Props/SM_Prop_Statue_Knight_01";
        const string P_StatueHero = "Props/SM_Prop_Statue_Hero_01";
        const string P_Books = "Props/SM_Prop_Books_01";
        const string P_HayCart = "Props/SM_Prop_Hay_Cart_01";
        const string P_CrateStack2 = "Props/SM_Prop_Crate_Stack_02";
        const string Bld_TentSquare = "Buildings/SM_Bld_Tent_Square_01";
        const string V_Wheelbarrow = "Vehicles/SM_Veh_Wheelbarrow_01";
        const string P_Ladder = "Props/SM_Prop_Ladder_01";

        static Material _age0DarkStone, _age0Slate, _alCloth, _age0Veil;

        // ═════════════════════════════════════════════════════════════════
        [MenuItem("Waning Border/Art/Author Age 0 + Alanthor Art (Synty)")]
        public static void AuthorAge0Alanthor()
        {
            Report.Clear(); _failures = 0;
            try
            {
                EnsureThemeMaterials();
                Age0Forms();
                AlanthorBuildings();
                SectBuildings();
                WallSets();
                DeleteRetiredArt();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                BuildingArtBinder.Run();            // base SO wiring for the wall parts, audit
                RenderAge0Alanthor();
            }
            catch (Exception e) { Fail("AuthorAge0Alanthor", e.ToString()); }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string text = $"[AlaKit] done — {Report.Count} entries, {_failures} failure(s)\n" + string.Join("\n", Report);
            Debug.Log(text);
            try { Directory.CreateDirectory("Temp"); File.WriteAllText("Temp/alanthor-kit-report.txt", text); } catch (Exception) { }
        }

        static void EnsureThemeMaterials()
        {
            HouseLineupScene.EnsureHutMaterials(new System.Text.StringBuilder());
            _synAtlas = Need(HutDir + "Art/Materials/Syn_Atlas.mat");
            _age0Mud = Need(HutDir + "Art/Materials/Age0_Mud.mat");
            _alStucco = Need(HouseDir + "Art/Materials/Al_Stucco.mat");
            _alStone = Need(HouseDir + "Art/Materials/Al_Stone.mat");
            _alTile = Need(HouseDir + "Art/Materials/Al_Tile.mat");
            _alRed = Need(HouseDir + "Art/Materials/Al_Red.mat");
            var shader = Shader.Find(BuildingLitShader);
            if (shader == null) throw new Exception("shader missing: " + BuildingLitShader);
            Directory.CreateDirectory(Age0MatDir);
            _age0DarkStone = ThemeMat(shader, Age0MatDir + "Age0_DarkStone.mat", KitTex + "Castle/Wall_Dungeon_01.png", new Color(0.55f, 0.55f, 0.6f), 0.08f);
            _age0Slate = ThemeMat(shader, Age0MatDir + "Age0_Slate.mat", KitTex + "Castle/Roof_Slat_02.png", new Color(0.22f, 0.22f, 0.27f), 0.15f);
            _age0Cloth = AssetDatabase.LoadAssetAtPath<Material>(Age0MatDir + "Age0_PlayerColor_Cloth.mat");
            if (_age0Cloth == null) throw new Exception("Age0_PlayerColor_Cloth.mat missing (run Author Feraldis Building Art once)");
            string alCloth = HouseDir + "Art/Materials/Al_PlayerColor_Cloth.mat";
            _alCloth = AssetDatabase.LoadAssetAtPath<Material>(alCloth);
            if (_alCloth == null) { _alCloth = new Material(_age0Cloth) { name = "Al_PlayerColor_Cloth" }; AssetDatabase.CreateAsset(_alCloth, alCloth); }
            _age0Veil = AssetDatabase.LoadAssetAtPath<Material>(AGE0 + "VeilstoneMine/Materials/Age0_Veilstone.mat");
            if (_age0Veil == null) throw new Exception("Age0_Veilstone.mat missing");
            EnsureExtraMaterials();   // _alGold
        }

        static Material ThemeMat(Shader shader, string path, string texPath, Color tint, float smooth)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null) throw new Exception("kit texture missing: " + texPath);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) }; AssetDatabase.CreateAsset(m, path); }
            m.shader = shader; m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ═════════════════════════════════════════════════════════════════
        // Shared themed primitives
        // ═════════════════════════════════════════════════════════════════

        static GameObject PutM(Transform parent, string piece, string name, Vector3 pos, float yaw, Vector3 scale, Material mat)
            => Put(parent, piece, name, pos, yaw, scale, Style.Neutral, null, mat);

        /// <summary>A flat slab the size of the plot (x: -hx..hx, z: z0..z1), named Yard for CheckReach.</summary>
        static void Slab(Transform g, string name, float hx, float z0, float z1, float h, Material mat)
            => PutAt(g, C_FloorStone1, name, new Vector3(0f, 0f, (z0 + z1) * 0.5f), 0f, new Vector3(2f * hx / 5f, h / 0.28f, (z1 - z0) / 5f), mat);

        /// <summary>The House's low stone wall round a platform, a gap of `gap` in the south side.</summary>
        static void StoneRing(Transform g, float hx, float z0, float z1, float y, float gap)
        {
            float e = hx - 0.11f, fz = z0 + 0.11f, bz = z1 - 0.11f;
            var s = new Vector3(0.8f, 0.45f, 0.3f);
            float w = (e - gap / 2) / 2.5f;
            PutAt(g, H_Base, "RingSa", new Vector3(-(e + gap / 2) / 2f, y, fz), 0f, new Vector3(w, s.y, s.z), _synAtlas, true);
            PutAt(g, H_Base, "RingSb", new Vector3((e + gap / 2) / 2f, y, fz), 0f, new Vector3(w, s.y, s.z), _synAtlas, true);
            PutAt(g, H_Base, "RingN", new Vector3(0f, y, bz), 0f, new Vector3(2f * e / 2.5f, s.y, s.z), _synAtlas, true);
            PutAt(g, H_Base, "RingE", new Vector3(e, y, (fz + bz) / 2f), 90f, new Vector3((bz - fz) / 2.5f, s.y, s.z), _synAtlas, false);
            PutAt(g, H_Base, "RingW", new Vector3(-e, y, (fz + bz) / 2f), 90f, new Vector3((bz - fz) / 2.5f, s.y, s.z), _synAtlas, false);
        }

        /// <summary>Stone platform with ring and stair: the Alanthor ground of a plot of half-size `half`.</summary>
        static void AlanthorGround(Transform g, float half, float platTop = 0.28f, float apron = 0.5f, float gap = 1.4f)
        {
            float z0 = -half + apron, z1 = half;
            Slab(g, "Yard", half, z0, z1, platTop, _alStone);
            StoneRing(g, half, z0, z1, platTop, gap);
            PutAt(g, H_StairsStone, "Stair", new Vector3(0f, 0f, -half + 0.31f), 90f, new Vector3(0.62f / 1.87f, platTop / 1.27f, gap / 1.88f * 0.7f), _synAtlas);
            foreach (var sx in new[] { -1f, 1f })
                PutAt(g, E_Bush, "Bush", new Vector3(sx * (gap / 2 + 0.6f), 0f, -half + 0.34f), 0f, Vector3.one * 0.17f, _synAtlas);
        }

        /// <summary>A stucco box: castle walls round (cx,cz) with half sizes hx,hz,
        /// pieces no longer than 4.2 m, the arched door in the middle of the south
        /// side, arched windows elsewhere, red columns at the corners. Returns the wall top.</summary>
        static float StuccoBox(Transform walls, float cx, float cz, float hx, float hz, float wallH, float y,
                               bool door, bool windows, string tag, bool columns = true)
        {
            float sy = wallH / 5f;
            foreach (var (side, yaw, len, alongX) in new[] { ("S", 0f, 2f * hx, true), ("E", 90f, 2f * hz, false), ("N", 180f, 2f * hx, true), ("W", 270f, 2f * hz, false) })
            {
                int n = Mathf.Max(1, Mathf.CeilToInt(len / 4.2f));
                float seg = len / n;
                for (int i = 0; i < n; i++)
                {
                    float along = -len / 2 + seg * (i + 0.5f);
                    bool mid = i == n / 2;
                    string piece = side == "S" ? (door && mid ? C_WallDoorArch : (windows && !mid ? C_WallWindowArch : C_WallPlain))
                                 : side == "N" ? (windows && mid && n > 1 ? C_WallWindowArch : C_WallPlain)
                                 : (windows ? C_WallWindowArch : C_WallPlain);
                    Vector3 c = side switch
                    {
                        "S" => new Vector3(cx + along, y, cz - hz), "E" => new Vector3(cx + hx, y, cz + along),
                        "N" => new Vector3(cx - along, y, cz + hz), _ => new Vector3(cx - hx, y, cz - along),
                    };
                    PutAt(walls, piece, tag + "Wall" + side + Letter(i), c, yaw, new Vector3(seg / 5f, sy, 0.6f), _alStucco, alongX);
                }
            }
            if (columns)
            {
                float ps = Mathf.Clamp(0.42f * Mathf.Min(hx, hz) / 1.5f, 0.17f, 0.3f);
                foreach (var (px, pz) in new[] { (-hx, -hz), (hx, -hz), (hx, hz), (-hx, hz) })
                    PutAt(walls, C_PillarStoneRound, tag + "Column", new Vector3(cx + px, y, cz + pz), 0f, new Vector3(ps, sy, ps), _alRed);
            }
            return y + wallH;
        }

        /// <summary>Teal tile pyramid over a square: the castle cap with its skirt sunk inside the walls.</summary>
        static float TileCap(Transform roof, float cx, float cz, float half, float top, string tag, float riseK = 0.8f)
        {
            float cs = (half + 0.12f) / 1.22f, rise = (half + 0.3f) * riseK, cy = rise / 0.68f;
            PutAt(roof, C_CapSquare1, tag + "Cap", new Vector3(cx, top - 0.39f * cy + 0.02f, cz), 0f, new Vector3(cs, cy, cs), _alTile);
            return top + rise;
        }

        /// <summary>Teal tile gable, ridge along X from x0 to x1 over half-depth hz (SteepRoof in the Alanthor tile).</summary>
        static float TileGable(Transform roof, string tag, float x0, float x1, float z, float hz, float eaveY, float rise)
        {
            float ridgeY = eaveY + rise, len = x1 - x0;
            float sx = len / 5f, sy = (ridgeY - eaveY) / 5f, sz = hz / 2.5f;
            PutM(roof, C_RoofS, tag + "SlopeS", new Vector3(x0, eaveY, z + hz), 0f, new Vector3(sx, sy, sz), _alTile);
            PutM(roof, C_RoofS, tag + "SlopeN", new Vector3(x1, eaveY, z - hz), 180f, new Vector3(sx, sy, sz), _alTile);
            PutM(roof, C_RoofSEnd, tag + "TrimSW", new Vector3(x0, eaveY, z + hz), 0f, new Vector3(1f, sy, sz), _alTile);
            PutM(roof, C_RoofSEnd, tag + "TrimSE", new Vector3(x1, eaveY, z + hz), 0f, new Vector3(1f, sy, sz), _alTile);
            PutM(roof, C_RoofSEnd, tag + "TrimNE", new Vector3(x1, eaveY, z - hz), 180f, new Vector3(1f, sy, sz), _alTile);
            PutM(roof, C_RoofSEnd, tag + "TrimNW", new Vector3(x0, eaveY, z - hz), 180f, new Vector3(1f, sy, sz), _alTile);
            return ridgeY;
        }

        /// <summary>A hall: stucco box plus a tile gable (ridge along X) or, when square, a tile pyramid.</summary>
        static float AlHall(Transform walls, Transform roof, float cx, float cz, float hx, float hz, float wallH, float y,
                            bool door, bool windows, string tag)
        {
            float top = StuccoBox(walls, cx, cz, hx, hz, wallH, y, door, windows, tag);
            if (Mathf.Abs(hx - hz) < 0.3f) return TileCap(roof, cx, cz, hx, top, tag);
            return TileGable(roof, tag, cx - hx - 0.35f, cx + hx + 0.35f, cz, hz + 0.35f, top - 0.05f, (hz + 0.35f) * 0.9f);
        }

        /// <summary>A pagoda: `tiers` square stucco tiers shrinking upward, each set into the tile cap below.</summary>
        static float Pagoda(Transform walls, Transform roof, float cx, float cz, float half, float baseY, int tiers, string tag, bool door = true)
        {
            float h = half, y = baseY, wallH = 2.5f, top = baseY;
            for (int t = 0; t < tiers; t++)
            {
                float wt = StuccoBox(walls, cx, cz, h, h, wallH, y, door && t == 0, true, tag + "T" + t);
                float apex = TileCap(roof, cx, cz, h, wt, tag + "T" + t);
                top = apex;
                y = wt + 0.35f * (h + 0.3f) * 0.8f / 0.8f * 0.55f + 0.2f;   // the next tier sits into this cap
                y = wt + 0.55f * (h / 1.5f);
                h *= 0.62f; wallH = Mathf.Max(1.0f, wallH * 0.62f);
            }
            return top;
        }

        static void Torii(Transform props, float cx, float z, float halfW, float h)
        {
            foreach (var sx in new[] { -1f, 1f })
                PutAt(props, C_PillarStoneRound, "Torii", new Vector3(cx + sx * halfW, 0f, z), 0f, new Vector3(0.17f * h / 2.5f, h / 5f, 0.17f * h / 2.5f), _alRed);
            PutCentred(props, P_TBeam, "ToriiLintel", new Vector3(cx, h + 0.05f, z), 0f, new Vector3(0.6f, 0.55f, (2 * halfW + 1.1f) / 5f), _alRed, true);
            PutCentred(props, P_TBeam, "ToriiTie", new Vector3(cx, h - 0.5f, z), 0f, new Vector3(0.45f, 0.4f, (2 * halfW + 0.5f) / 5f), _alRed, true);
            foreach (var sx in new[] { -1f, 1f })
                PutCentred(props, P_BracketLamp, "ToriiLamp", new Vector3(cx + sx * halfW * 0.55f, h - 0.95f, z), 0f, Vector3.one * 0.6f, _synAtlas);
        }

        static void WallLamp(Transform props, Vector3 at) => PutCentred(props, P_BracketLamp, "Lamp", at, 0f, Vector3.one * 0.75f, _synAtlas);
        static void GateLamp(Transform props, Vector3 at) => PutAt(props, P_StreetLamp1, "GateLamp", at, 0f, Vector3.one * 0.65f, _synAtlas);

        /// <summary>A red mast with a player-colour flag (the faction-colour slot).</summary>
        static void AlFlag(Transform parent, string tag, Vector3 foot, float height, float flagScale = 0.6f)
        {
            PutM(parent, C_PillarSq, tag + "Mast", foot, 0f, new Vector3(0.25f, height / 5.05f, 0.25f), _alRed);
            Put(parent, BN_Flag, "PlayerColor_" + tag, foot + new Vector3(0.1f, height - 0.1f, 0f), 0f, Vector3.one * flagScale, Style.Neutral, null, _alCloth);
        }

        /// <summary>Round castle tower in stucco with a teal cone (the Vault's towers).</summary>
        static float StuccoTower(Transform parent, string tag, Vector3 centre, int storeys, float scale, bool cone)
        {
            var root = Group(parent, tag);
            root.localPosition = centre; root.localScale = Vector3.one * scale;
            float y = 0f;
            for (int s = 0; s < storeys; s++, y += 5f)
                for (int k = 0; k < 4; k++)
                {
                    var rot = Quaternion.Euler(0f, k * 90f, 0f);
                    PutM(root, C_CornerS, "Storey", -(rot * new Vector3(-2.5f, 0f, 0f)) + new Vector3(0f, y, 0f), k * 90f, Vector3.one, _alStucco);
                }
            for (int k = 0; k < 4; k++)
            {
                var rot = Quaternion.Euler(0f, k * 90f, 0f);
                PutM(root, C_FloorRoundS, "Floor", new Vector3(0f, y, 0f), k * 90f, Vector3.one, _alStucco);
                PutM(root, C_CrownS, "Crown", -(rot * new Vector3(-2.5f, 0f, 0f)) + new Vector3(0f, y, 0f), k * 90f, Vector3.one, _alStucco);
            }
            float top = y + 1.38f;
            if (cone)
            {
                float k2 = (2.75f * 2f * 0.95f) / 2.28f;
                PutM(root, C_ConeRoof, "Cone", new Vector3(0f, top - 1.0f, 0f), 0f, new Vector3(k2, (2.75f * 2.4f) / 6.87f, k2), _alTile);
                top += 2.75f * 2.4f - 1.0f;
            }
            return centre.y + top * scale;
        }

        // ── Age 0 primitives ─────────────────────────────────────────────

        /// <summary>Plank hall of nx x nz 2.5 m modules: hoarding walls on beam posts under a thatch gable (ridge along X).</summary>
        static float PlankHall(Transform walls, Transform roof, float cx, float cz, int nx, int nz, float baseY, bool door = true)
        {
            float hx = nx * W * 0.5f, hz = nz * W * 0.5f, wallH = 2.0f;
            ThatchHall(walls, roof, cx, cz, nx, nz, baseY, walls: false, door: false, postHeight: wallH);
            foreach (var (side, yaw, n, alongX) in new[] { ("S", 0f, nx, true), ("E", 90f, nz, false), ("N", 180f, nx, true), ("W", 270f, nz, false) })
                for (int i = 0; i < n; i++)
                {
                    float along = -(side == "S" || side == "N" ? hx : hz) + W * (i + 0.5f);
                    Vector3 c = side switch
                    {
                        "S" => new Vector3(cx + along, baseY, cz - hz), "E" => new Vector3(cx + hx, baseY, cz + along),
                        "N" => new Vector3(cx - along, baseY, cz + hz), _ => new Vector3(cx - hx, baseY, cz - along),
                    };
                    PutAt(walls, C_HoardWall, "Wall" + side + Letter(i), c, yaw, new Vector3(1f, wallH / 2.21f, 1f), _synAtlas, alongX);
                }
            if (door) PutAt(walls, C_DoorSingle, "Door", new Vector3(cx - 0.05f, baseY, cz - hz - 0.09f), 0f, new Vector3(1f, 0.85f, 1f), _synAtlas, true);
            return baseY + wallH + 2.68f / 2.5f * (hz + 0.2f);
        }

        static void MudYard(Transform g, float half) => Slab(g, "Yard", half, -half, half, 0.14f, _age0Mud);
        static void Pennant(Transform g, Vector3 at, float s = 1.1f) => Banner(g, "PlayerColor_Pennant", at, 0f, s, _age0Cloth);

        /// <summary>Rectangular fence ring (post-and-rail), gate in the south side at `gateX`.</summary>
        static void FenceRect(Transform parent, float hx, float z0, float z1, float y, float gateX)
        {
            float e = 0.1f;
            void Run(string side, Vector3 a, Vector3 b, bool alongX)
            {
                float len = Vector3.Distance(a, b); int n = Mathf.Max(1, Mathf.RoundToInt(len / 2f));
                for (int i = 0; i < n; i++)
                {
                    Vector3 c = Vector3.Lerp(a, b, (i + 0.5f) / n);
                    float span = len / n;
                    if (side == "S" && Mathf.Abs(c.x - gateX) < span * 0.5f)
                        PutAt(parent, P_FenceGateH, "Gate", c, 0f, new Vector3(span / 2.61f, 0.85f, 1f), _synAtlas, alongX);
                    else
                        PutAt(parent, P_Fence, "Fence" + side + Letter(i), c, 0f, new Vector3(span / 2.48f, 0.7f, 1f), _synAtlas, alongX);
                }
            }
            Run("S", new Vector3(-hx + e, y, z0 + e), new Vector3(hx - e, y, z0 + e), true);
            Run("N", new Vector3(-hx + e, y, z1 - e), new Vector3(hx - e, y, z1 - e), true);
            Run("E", new Vector3(hx - e, y, z0 + e), new Vector3(hx - e, y, z1 - e), false);
            Run("W", new Vector3(-hx + e, y, z0 + e), new Vector3(-hx + e, y, z1 - e), false);
        }

        // ═════════════════════════════════════════════════════════════════
        // Age 0 forms
        // ═════════════════════════════════════════════════════════════════

        static void Age0Forms()
        {
            PlainInPlace(AGE0 + "Barracks/Art/Barracks_Age_0.prefab", AGE0 + "Barracks/Barracks.asset", "Barracks_Age_0", BuildBarracks0);
            PlainInPlace(AGE0 + "GatherersHut/Art/GatherersHut_Age_0.prefab", AGE0 + "GatherersHut/GatherersHut.asset", "GatherersHut_Age_0", BuildGatherers0);
            PlainInPlace(AGE0 + "Mine/Art/Mine_Age_0.prefab", AGE0 + "Mine/Mine.asset", "Mine_Age_0", t => BuildMine0(t, false));
            PlainInPlace(AGE0 + "VeilstoneMine/Art/VeilstoneMine_Age_0.prefab", AGE0 + "VeilstoneMine/VeilstoneMine.asset", "VeilstoneMine_Age_0", t => BuildMine0(t, true));
            PlainInPlace(AGE0 + "TempleOfRidan/Art/TempleOfRidan_Age_0.prefab", AGE0 + "TempleOfRidan/TempleOfRidan.asset", "TempleOfRidan_Age_0", BuildTemple0);
            PlainInPlace(AGE0 + "Fortress/Art/Fortress_Age_0.prefab", AGE0 + "Fortress/Fortress.asset", "Fortress_Age_0", BuildShelter0);
            PlainInPlace(AGE0 + "FiendstoneKeep/Art/FiendstoneKeep_Age_0.prefab", AGE0 + "FiendstoneKeep/FiendstoneKeep.asset", "FiendstoneKeep_Age_0", BuildKeep0);
            PlainInPlace(AGE0 + "VaultOfAlmierra/Art/VaultOfAlmierra_Age_0.prefab", AGE0 + "VaultOfAlmierra/VaultOfAlmierra.asset", "VaultOfAlmierra_Age_0", t => BuildVault(t, 1, true));
        }

        /// <summary>Barracks (8 m): a two-module plank drill hall at the back, racks, targets, a dummy, a firepit, fenced.</summary>
        static void BuildBarracks0(Transform root)
        {
            float H = 4f, yY = 0.14f;
            var g1 = Group(root, "1_Ground"); MudYard(g1, H);
            var g2 = Group(root, "2_Walls"); var g3 = Group(root, "3_Thatch");
            PlankHall(g2, g3, 0f, 1.5f, 2, 1, yY);
            var g4 = Group(root, "4_Props");
            FenceRect(g4, H, -H, H, yY, 0.5f);
            PutAt(g4, B_WeaponRack, "RackA", new Vector3(-3.1f, yY, -0.6f), 20f, Vector3.one, _synAtlas);
            PutAt(g4, B_WeaponRack, "RackB", new Vector3(3.1f, yY, -0.6f), -20f, Vector3.one, _synAtlas);
            PutAt(g4, B_Target, "TargetA", new Vector3(-3.0f, yY, -2.6f), -90f, Vector3.one * 1.2f, _synAtlas);
            PutAt(g4, B_Target, "TargetB", new Vector3(-1.6f, yY, -3.0f), -90f, Vector3.one * 1.2f, _synAtlas);
            PutAt(g4, B_Dummy, "Dummy", new Vector3(2.6f, yY, -2.6f), 70f, Vector3.one, _synAtlas);
            PutAt(g4, B_Firepit, "Firepit", new Vector3(0f, yY, -1.5f), 0f, Vector3.one * 1.1f, _synAtlas);
            PutAt(g4, P_CrateStack, "Crates", new Vector3(3.2f, yY, 3.2f), 30f, Vector3.one * 0.7f, _synAtlas);
            PutAt(g4, P_WoodPile, "Woodpile", new Vector3(-3.2f, yY, 3.2f), 90f, Vector3.one * 0.6f, _synAtlas);
            Pennant(Group(root, "5_Pennant"), new Vector3(1.3f, yY, -3.2f), 1.3f);
        }

        /// <summary>Gatherer's Hut (4 m): a plank store room, lean-to, hay, sacks, baskets, fenced.</summary>
        static void BuildGatherers0(Transform root)
        {
            float H = 2f, yY = 0.14f;
            var g1 = Group(root, "1_Ground"); MudYard(g1, H);
            var g2 = Group(root, "2_Walls"); var g3 = Group(root, "3_Thatch");
            PlankHall(g2, g3, -0.4f, 0.4f, 1, 1, yY);
            var g4 = Group(root, "4_Props");
            FenceRect(g4, H, -H, H, yY, 0.9f);
            PutM(g4, H_Thatch, "LeanTo", new Vector3(0.95f, yY + 2.2f, 1.55f), 0f, new Vector3(0.5f, 0.4f, 0.75f), _synAtlas);
            PutAt(g4, H_Beam, "LeanPostA", new Vector3(1.75f, yY, 1.5f), 0f, new Vector3(0.9f, 0.5f, 0.9f), _synAtlas);
            PutAt(g4, H_Beam, "LeanPostB", new Vector3(1.75f, yY, -0.2f), 0f, new Vector3(0.9f, 0.5f, 0.9f), _synAtlas);
            PutAt(g4, P_HayBale, "Hay", new Vector3(1.4f, yY, 0.6f), 20f, Vector3.one * 0.6f, _synAtlas);
            PutAt(g4, P_SackStack, "Sacks", new Vector3(-1.4f, yY, -1.4f), 30f, Vector3.one * 0.7f, _synAtlas);
            PutAt(g4, P_Basket, "BasketA", new Vector3(1.1f, yY, -1.4f), 0f, Vector3.one * 1.1f, _synAtlas);
            PutAt(g4, V_Wheelbarrow, "Barrow", new Vector3(1.4f, yY, -0.5f), 110f, Vector3.one * 0.7f, _synAtlas);
            Pennant(Group(root, "5_Pennant"), new Vector3(-1.5f, yY, 1.5f), 0.9f);
        }

        /// <summary>Mine (4 m): a stone adit under a beam headframe, ore or veilstone heaped at its foot, a cart, fenced on three sides.</summary>
        static void BuildMine0(Transform root, bool veil)
        {
            float H = 2f, yY = 0.14f;
            var g1 = Group(root, "1_Ground"); MudYard(g1, H);
            var g2 = Group(root, "2_Adit");
            PutAt(g2, C_Block, "Adit", new Vector3(0f, yY, 0.9f), 0f, new Vector3(2.6f / 5.5f, 1.9f / 5f, 1.8f / 5.5f), _age0DarkStone);
            PutAt(g2, C_DoorSingle, "AditMouth", new Vector3(0f, yY, -0.05f), 0f, new Vector3(1.5f, 0.6f, 1f), _synAtlas, true);
            var g3 = Group(root, "3_Headframe");
            PutAt(g3, H_Beam, "HeadPostA", new Vector3(-1.5f, yY, -0.4f), 0f, new Vector3(1.1f, 1.2f, 1.1f), _synAtlas);
            PutAt(g3, H_Beam, "HeadPostB", new Vector3(1.5f, yY, -0.4f), 0f, new Vector3(1.1f, 1.2f, 1.1f), _synAtlas);
            PutCentred(g3, C_PanelWood, "HeadBeam", new Vector3(0f, yY + 3.0f, -0.4f), 0f, new Vector3(0.68f, 0.4f, 1f), _synAtlas, true);
            PutCentred(g3, P_ChainDrop, "Hoist", new Vector3(0f, yY + 2.3f, -0.4f), 0f, Vector3.one * 0.9f, _synAtlas);
            var g4 = Group(root, "4_Props");
            FenceRect(g4, H, -H, H, yY, 0f);
            PutAt(g4, P_Crate, "Crate", new Vector3(-1.5f, yY, 1.4f), 20f, Vector3.one * 0.7f, _synAtlas);
            PutAt(g4, V_Cart, "Cart", new Vector3(0.9f, yY, -1.4f), 90f, Vector3.one * 0.45f, _synAtlas);
            for (int i = 0; i < 4; i++)
            {
                var pos = new Vector3(1.1f + (i % 2) * 0.4f, yY, 0.8f + (i / 2) * 0.5f);
                if (veil) PutAt(g4, i % 2 == 0 ? I_Crystal3 : I_Crystal5, "Veilstone", pos, i * 50f, Vector3.one * 3.4f, _age0Veil);
                else PutAt(g4, P_OreIron, "Ore", pos, i * 50f, Vector3.one * 3.6f, _synAtlas);
            }
            Pennant(Group(root, "5_Pennant"), new Vector3(-1.5f, yY, -1.5f), 0.9f);
        }

        /// <summary>Temple of Ridan (8 m): a thatched canopy on beam posts over a crystal shrine, candles, a bell, standing stones.</summary>
        static void BuildTemple0(Transform root)
        {
            float H = 4f, yY = 0.14f;
            var g1 = Group(root, "1_Ground"); MudYard(g1, H); Cobbles(g1, 0f, 0f, 2, 2);
            var g2 = Group(root, "2_Posts"); var g3 = Group(root, "3_Thatch");
            ThatchHall(g2, g3, 0f, 0f, 2, 2, yY, walls: false, door: false, postHeight: 3.6f);
            PutAt(g2, P_StatuePlinth, "Plinth", new Vector3(0f, yY, 0.6f), 0f, Vector3.one * 1.2f, _synAtlas);
            var g4 = Group(root, "4_Shrine");
            PutAt(g4, I_Crystal3, "Crystal", new Vector3(0f, yY + 1.0f, 0.6f), 20f, Vector3.one * 4.5f, _age0Veil);
            PutAt(g4, I_Crystal5, "CrystalB", new Vector3(0.5f, yY + 1.0f, 0.9f), -30f, Vector3.one * 2.5f, _age0Veil);
            PutAt(g4, P_Candles, "CandlesA", new Vector3(-1.2f, yY, -1.4f), 0f, Vector3.one * 2.2f, _synAtlas);
            PutAt(g4, P_Candles, "CandlesB", new Vector3(1.3f, yY, -1.2f), 50f, Vector3.one * 2.2f, _synAtlas);
            PutAt(g4, H_Beam, "BellPostA", new Vector3(3.4f, yY, 2.4f), 0f, new Vector3(1f, 1.1f, 1f), _synAtlas);
            PutAt(g4, H_Beam, "BellPostB", new Vector3(3.4f, yY, 0.6f), 0f, new Vector3(1f, 1.1f, 1f), _synAtlas);
            PutCentred(g4, C_PanelWood, "BellBeam", new Vector3(3.4f, yY + 2.75f, 1.5f), 90f, new Vector3(0.38f, 0.4f, 1f), _synAtlas, false);
            PutCentred(g4, P_Bell, "Bell", new Vector3(3.4f, yY + 2.2f, 1.5f), 90f, Vector3.one * 0.8f, _synAtlas);
            PutAt(g4, E_Rock, "StoneA", new Vector3(-3.2f, yY, 2.6f), 20f, new Vector3(2.4f, 6f, 2.4f), _synAtlas);
            PutAt(g4, E_Rock, "StoneB", new Vector3(-3.3f, yY, -2.6f), -40f, new Vector3(2.2f, 5f, 2.2f), _synAtlas);
            PutAt(g4, E_Rock, "StoneC", new Vector3(3.2f, yY, -2.8f), 60f, new Vector3(2.0f, 4.5f, 2.0f), _synAtlas);
            FenceRect(g4, H, -H, H, yY, 0f);
            Pennant(Group(root, "5_Pennant"), new Vector3(1.6f, yY, -3.2f), 1.3f);
        }

        /// <summary>The Shelter (10 m): a 2 x 2-module plank hall with thatch, lean-tos, a camp firepit, fenced with a gate.</summary>
        static void BuildShelter0(Transform root)
        {
            float H = 5f, yY = 0.14f;
            var g1 = Group(root, "1_Ground"); MudYard(g1, H); Cobbles(g1, 0f, 0.8f, 2, 2);
            var g2 = Group(root, "2_Walls"); var g3 = Group(root, "3_Thatch");
            PlankHall(g2, g3, 0f, 0.8f, 2, 2, yY);
            var g4 = Group(root, "4_Leantos");
            PutM(g4, H_Thatch, "LeanToW", new Vector3(-2.55f, yY + 2.1f, 2.0f), 0f, new Vector3(0.75f, 0.45f, 1.0f), _synAtlas);
            PutAt(g4, H_Beam, "LeanPostWa", new Vector3(-4.4f, yY, 1.9f), 0f, new Vector3(1f, 0.5f, 1f), _synAtlas);
            PutAt(g4, H_Beam, "LeanPostWb", new Vector3(-4.4f, yY, -0.4f), 0f, new Vector3(1f, 0.5f, 1f), _synAtlas);
            PutM(g4, H_Thatch, "LeanToE", new Vector3(2.55f, yY + 2.1f, -0.4f), 180f, new Vector3(0.75f, 0.45f, 1.0f), _synAtlas);
            PutAt(g4, H_Beam, "LeanPostEa", new Vector3(4.4f, yY, 2.0f), 0f, new Vector3(1f, 0.5f, 1f), _synAtlas);
            PutAt(g4, H_Beam, "LeanPostEb", new Vector3(4.4f, yY, -0.3f), 0f, new Vector3(1f, 0.5f, 1f), _synAtlas);
            PutAt(g4, P_CrateStack, "Crates", new Vector3(3.6f, yY, 1.0f), 30f, Vector3.one * 0.8f, _synAtlas);
            PutAt(g4, P_BarrelStack, "Barrels", new Vector3(-3.6f, yY, -3.3f), 60f, Vector3.one * 0.7f, _synAtlas);
            PutAt(g4, P_WoodPile, "Woodpile", new Vector3(3.6f, yY, -3.6f), 10f, Vector3.one * 0.8f, _synAtlas);
            var g5 = Group(root, "5_Camp");
            PutAt(g5, B_Firepit, "Firepit", new Vector3(0f, yY, -3.2f), 0f, Vector3.one * 1.3f, _synAtlas);
            PutAt(g5, P_Log, "BenchA", new Vector3(-1.9f, yY, -3.1f), 100f, new Vector3(0.6f, 0.6f, 0.45f), _synAtlas);
            PutAt(g5, P_Log, "BenchB", new Vector3(1.9f, yY, -2.8f), 70f, new Vector3(0.6f, 0.6f, 0.45f), _synAtlas);
            var g6 = Group(root, "6_Fence");
            FenceRect(g6, H, -H, H, yY, 0.5f);
            Pennant(Group(root, "7_Pennant"), new Vector3(2.4f, yY, -4.3f), 1.3f);
        }

        /// <summary>Fiendstone Keep (12 m): a dark stone keep — battlemented block, four round towers, a furnace spire.</summary>
        static void BuildKeep0(Transform root)
        {
            float H = 6f;
            var g1 = Group(root, "1_Plinth");
            Slab(g1, "Yard", H, -H, H, 0.4f, _age0DarkStone);
            var g2 = Group(root, "2_Walls");
            float hx = 3.6f, top = 0.4f;
            foreach (var (side, yaw, alongX) in new[] { ("S", 0f, true), ("E", 90f, false), ("N", 180f, true), ("W", 270f, false) })
                for (int i = 0; i < 2; i++)
                {
                    float along = -hx + hx * (i + 0.5f);
                    Vector3 c = side switch
                    {
                        "S" => new Vector3(along, top, -hx), "E" => new Vector3(hx, top, along),
                        "N" => new Vector3(-along, top, hx), _ => new Vector3(-hx, top, -along),
                    };
                    string piece = side == "S" && i == 1 ? C_WallDoorArch : C_Wall1;
                    PutAt(g2, piece, "Wall" + side + Letter(i), c, yaw, new Vector3(hx / 5f, 1.1f, 0.7f), _age0DarkStone, alongX);
                }
            PutAt(g2, C_Block, "Core", new Vector3(0f, top, 0f), 0f, new Vector3((2 * hx - 0.4f) / 5.5f, 5.4f / 5f, (2 * hx - 0.4f) / 5.5f), _age0DarkStone);
            var g3 = Group(root, "3_Crown");
            float crown = top + 5.5f;
            foreach (var (side, yaw, alongX) in new[] { ("S", 0f, true), ("E", 90f, false), ("N", 180f, true), ("W", 270f, false) })
            {
                Vector3 c = side switch
                {
                    "S" => new Vector3(0f, crown, -hx), "E" => new Vector3(hx, crown, 0f),
                    "N" => new Vector3(0f, crown, hx), _ => new Vector3(-hx, crown, 0f),
                };
                PutAt(g3, C_Batt1, "Batt" + side, c, yaw, new Vector3(2 * hx / 5f, 0.9f, 0.9f), _age0DarkStone, alongX);
            }
            var g4 = Group(root, "4_Towers");
            foreach (var (px, pz, tag) in new[] { (-4.4f, -4.4f, "SW"), (4.4f, -4.4f, "SE"), (4.4f, 4.4f, "NE"), (-4.4f, 4.4f, "NW") })
                StoneTower(g4, "Tower" + tag, new Vector3(px, 0.4f, pz), 2, 0.5f, cone: false, Style.Neutral);
            var g5 = Group(root, "5_Spire");
            PutAt(g5, C_Spire, "FurnaceSpire", new Vector3(0f, crown - 0.2f, 0.5f), 0f, new Vector3(1.4f, 0.9f, 1.4f), _age0Slate);
            Put(g5, FX_Smoke, "Smoke", new Vector3(0f, crown + 6.3f, 0.5f), 0f, Vector3.one * 2f, Style.Neutral);
            var g6 = Group(root, "6_Props");
            PutAt(g6, P_Torch, "TorchA", new Vector3(-1.4f, top + 1.6f, -hx - 0.2f), 0f, Vector3.one * 1.2f, _synAtlas);
            PutAt(g6, P_Torch, "TorchB", new Vector3(1.4f, top + 1.6f, -hx - 0.2f), 0f, Vector3.one * 1.2f, _synAtlas);
            PutAt(g6, B_Brazier, "BrazierA", new Vector3(-2.6f, 0.4f, -5.2f), 0f, Vector3.one * 1.6f, _synAtlas);
            PutAt(g6, B_Brazier, "BrazierB", new Vector3(2.6f, 0.4f, -5.2f), 0f, Vector3.one * 1.6f, _synAtlas);
            Pennant(Group(root, "7_Pennant"), new Vector3(0f, crown, 0f), 1.6f);
        }

        /// <summary>The Vault of Almiérra (8 m, Alanthor): a stucco treasury-cathedral — traceried nave, two front
        /// towers under teal cones, the crossing tower under a gilded dome, red columns and statues at the steps.</summary>
        static void BuildVault(Transform root, int level, bool age0)
        {
            const float P = 0.6f;
            float hx = 3.6f, hz = 2.4f;
            var g1 = Group(root, "1_Plinth");
            Slab(g1, "Yard", 4f, -4f, 4f, P, _alStone);
            if (!age0) StoneRing(g1, 4f, -4f, 4f, P, 2.2f);
            var g2 = Group(root, "2_Walls");
            float wallH = 5.0f + 0.5f * (level - 1);
            foreach (var (side, yaw, len, alongX) in new[] { ("S", 0f, 2f * hx, true), ("E", 90f, 2f * hz, false), ("N", 180f, 2f * hx, true), ("W", 270f, 2f * hz, false) })
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(len / 3.6f)); float seg = len / n;
                for (int i = 0; i < n; i++)
                {
                    float along = -len / 2 + seg * (i + 0.5f);
                    Vector3 c = side switch
                    {
                        "S" => new Vector3(along, P, -hz), "E" => new Vector3(hx, P, along),
                        "N" => new Vector3(-along, P, hz), _ => new Vector3(-hx, P, -along),
                    };
                    PutAt(g2, C_WallWinTall, "Nave" + side + Letter(i), c, yaw, new Vector3(seg / 5f, wallH / 10f, 0.6f), _alStucco, alongX);
                }
            }
            PutAt(g2, C_DoorBig, "VaultDoor", new Vector3(0f, P, -hz - 0.25f), 0f, new Vector3(0.6f, 0.6f, 0.8f), _alStucco, true);
            foreach (var (px, pz) in new[] { (-hx, -hz), (hx, -hz), (hx, hz), (-hx, hz) })
                PutAt(g2, C_PillarStoneRound, "Column", new Vector3(px, P, pz), 0f, new Vector3(0.28f, wallH / 5f, 0.28f), _alRed);
            var g3 = Group(root, "3_Towers");
            float ts = 0.42f + 0.04f * (level - 1);
            StuccoTower(g3, "TowerW", new Vector3(-2.9f, P, -2.4f), 2, ts, true);
            StuccoTower(g3, "TowerE", new Vector3(2.9f, P, -2.4f), 2, ts, true);
            var g4 = Group(root, "4_Cap");
            TileGable(g4, "Nave", -hx - 0.3f, hx + 0.3f, 0f, hz + 0.3f, P + wallH - 0.1f, (hz + 0.3f) * 1.1f);
            var g5 = Group(root, "5_Dome");
            float drumTop = StuccoTower(g5, "Crossing", new Vector3(0f, P, 1.2f), level >= 3 ? 3 : 2, 0.55f, false);
            PutAt(g5, C_OnionDome, "Dome", new Vector3(0f, drumTop - 0.3f, 1.2f), 0f, new Vector3(0.95f, 0.85f, 0.95f), _alGold);
            var g6 = Group(root, "6_Props");
            PutAt(g6, P_StatueAngel, "AngelW", new Vector3(-2.0f, P, -3.4f), 20f, Vector3.one * 0.8f, _synAtlas);
            PutAt(g6, P_StatueAngel, "AngelE", new Vector3(2.0f, P, -3.4f), -20f, Vector3.one * 0.8f, _synAtlas);
            PutAt(g6, P_OreGold, "GoldA", new Vector3(-1.2f, P, -3.2f), 0f, Vector3.one * 2.5f, _synAtlas);
            PutAt(g6, P_OreGold, "GoldB", new Vector3(1.3f, P, -3.3f), 40f, Vector3.one * 2.2f, _synAtlas);
            WallLamp(g6, new Vector3(-1.1f, P + 3.2f, -hz - 0.25f)); WallLamp(g6, new Vector3(1.1f, P + 3.2f, -hz - 0.25f));
            if (level >= 2) { AlFlag(g6, "FlagW", new Vector3(-3.7f, P, 3.5f), 5f); AlFlag(g6, "FlagE", new Vector3(3.7f, P, 3.5f), 5f); }
            if (level >= 3) Torii(g6, 0f, -3.75f, 1.0f, 2.8f);
            if (age0) Pennant(Group(root, "7_Pennant"), new Vector3(3.5f, P, -3.5f), 1.2f);
        }

        // ═════════════════════════════════════════════════════════════════
        // Alanthor buildings
        // ═════════════════════════════════════════════════════════════════

        static void AlanthorBuildings()
        {
            AlLevels("Garrison", "Garrison", BuildGarrison);
            AlLevels("Guild", "Guild", BuildGuild);
            AlLevels("Mine", "Mine", BuildAlMine);
            AlLevels("Tower", "WatchTower", BuildWatchTower);
            AlLevels("TradingOutpost", "TradingOutpost", BuildTradingOutpost);
            AlLevels("Fortress", "Fortress", BuildAlFortress);
            AlLevels("ArcheryRange", "ArcheryRange", BuildArcheryRange);
            AlLevels("RoyalStable", "RoyalStable", BuildRoyalStable);
            AlLevels("SiegeYard", "SiegeYard", BuildSiegeYard);
            AlLevels("VaultOfAlmierra", "VaultOfAlmierra", (t, l) => BuildVault(t, l, false));
            // construction forms of the Alanthor-only buildings
            AlFrameForm("ArcheryRange", "ArcheryRange", 8f);
            AlFrameForm("RoyalStable", "Alanthor_RoyalStable", 8f);
            AlFrameForm("SiegeYard", "Alanthor_SiegeYard", 8f);
            AlFrameForm("Tower", "Alanthor_Tower", 4f);
            AlFrameForm("TradingOutpost", "Alanthor_TradingOutpost", 4f);
            // the Alanthor Temple is the Age 0 Temple's culture look
            try
            {
                var root = new GameObject("alanthor_TempleOfRidan_lv1");
                BuildAlTemple(root.transform);
                var prefab = Save(root, AGE0 + "TempleOfRidan/Art/alanthor_TempleOfRidan_lv1.prefab");
                var so = AssetDatabase.LoadAssetAtPath<BuildingDefSO>(AGE0 + "TempleOfRidan/TempleOfRidan.asset");
                so.culturePrefabs.RemoveAll(c => c.culture == "Alanthor");
                so.culturePrefabs.Add(new BuildingDefSO.CulturePrefab { culture = "Alanthor", prefab = prefab });
                EditorUtility.SetDirty(so);
                Report.Add("OK Temple/Alanthor culture look -> alanthor_TempleOfRidan_lv1.prefab");
            }
            catch (Exception e) { Fail("AlTemple", e.ToString()); }
        }

        static void AlLevels(string folder, string stem, Action<Transform, int> build)
        {
            for (int l = 1; l <= 3; l++)
            {
                int level = l;
                string so = ALA + folder + "/" + LevelSoName(folder, l);
                PlainInPlace(ALA + $"{folder}/Art/alanthor_{stem}_lv{l}.prefab", so, $"alanthor_{stem}_lv{l}", t => build(t, level));
            }
        }

        static string LevelSoName(string folder, int l) => folder switch
        {
            "Tower" => $"WatchTower_Lvl{l}.asset",
            _ => $"{folder}_Lvl{l}.asset",
        };

        /// <summary>The construction form of an Alanthor-only building: a stone plinth, red posts and rafters.</summary>
        static void AlFrameForm(string folder, string id, float size)
        {
            PlainInPlace(ALA + $"{folder}/Art/{id}_Age_0.prefab", ALA + $"{folder}/{folder}.asset", id + "_Age_0", root =>
            {
                float h = size * 0.5f - 0.5f;
                var g1 = Group(root, "1_Plinth"); Slab(g1, "Yard", size * 0.5f, -size * 0.5f, size * 0.5f, 0.28f, _alStone);
                var g2 = Group(root, "2_Posts");
                foreach (var (px, pz) in new[] { (-h, -h), (h, -h), (h, h), (-h, h) })
                    PutAt(g2, C_PillarStoneRound, "Post", new Vector3(px, 0.28f, pz), 0f, new Vector3(0.2f, 0.5f, 0.2f), _alRed);
                var g3 = Group(root, "3_Rafters");
                PutCentred(g3, C_PanelWood, "RafterA", new Vector3(0f, 2.9f, -h), 0f, new Vector3(2f * h / 5f, 0.5f, 1f), _synAtlas, true);
                PutCentred(g3, C_PanelWood, "RafterB", new Vector3(0f, 2.9f, h), 180f, new Vector3(2f * h / 5f, 0.5f, 1f), _synAtlas, true);
                PutAt(g3, P_CrateStack2, "Crates", new Vector3(h - 0.6f, 0.28f, -h + 0.9f), 20f, Vector3.one * 0.6f, _synAtlas);
                PutAt(g3, P_Ladder, "Ladder", new Vector3(-h + 0.5f, 0.28f, 0f), 90f, new Vector3(0.8f, 0.55f, 0.8f), _synAtlas);
            });
        }

        /// <summary>Garrison (8 m, the Alanthor Barracks): a drill yard before a long hall; L2 a loft and front towers; L3 taller towers and a torii.</summary>
        static void BuildGarrison(Transform root, int level)
        {
            float H = 4f, P = 0.28f;
            var g1 = Group(root, "1_Ground"); AlanthorGround(g1, H, P, 0.5f, 1.8f);
            var walls = Group(root, "2_Walls"); var roof = Group(root, "3_Cap");
            float top = AlHall(walls, roof, 0f, 1.7f, 3.4f, 1.6f, 2.6f, P, true, true, "Hall");
            var props = Group(root, "6_Props");
            PutAt(props, P_WeaponRack5, "RackA", new Vector3(-3.2f, P, -1.4f), 90f, Vector3.one * 0.9f, _synAtlas);
            PutAt(props, B_Target, "Target", new Vector3(3.1f, P, -1.2f), -90f, Vector3.one * 1.1f, _synAtlas);
            PutAt(props, B_Dummy, "Dummy", new Vector3(2.2f, P, -2.2f), 60f, Vector3.one, _synAtlas);
            WallLamp(props, new Vector3(-0.9f, P + 2.0f, 1.7f - 1.6f - 0.25f)); WallLamp(props, new Vector3(0.9f, P + 2.0f, 1.7f - 1.6f - 0.25f));
            if (level >= 2)
            {
                var loft = Group(root, "4_Loft"); var loftCap = Group(root, "5_LoftCap");
                AlHall(loft, loftCap, 0f, 1.7f, 1.6f, 1.0f, 1.5f, top - 0.2f + 0.5f, false, true, "Loft");
                foreach (var sx in new[] { -1f, 1f })
                    Pagoda(loft, loftCap, sx * 3.0f, -2.2f, 0.75f, P, level >= 3 ? 2 : 1, "Tower" + (sx < 0 ? "W" : "E"), false);
            }
            if (level >= 3)
            {
                Torii(props, 0f, -3.75f, 1.1f, 2.8f);
                AlFlag(props, "Flag", new Vector3(-3.5f, P, 3.4f), 5f);
            }
            else GateLamp(props, new Vector3(1.6f, 0f, -3.7f));
        }

        /// <summary>Guild (4 m, the Alanthor Gatherer's Hut): the House with a market stall and stores; L2 a loft; L3 a pagoda tier.</summary>
        static void BuildGuild(Transform root, int level)
        {
            float H = 2f, P = 0.28f;
            var g1 = Group(root, "1_Ground"); AlanthorGround(g1, H);
            var walls = Group(root, "2_Walls"); var roof = Group(root, "3_Cap");
            float top = StuccoBox(walls, 0.35f, 0.35f, 1.15f, 1.15f, 2.4f, P, true, true, "Hall");
            float apex = TileCap(roof, 0.35f, 0.35f, 1.15f, top, "Hall");
            var props = Group(root, "6_Props");
            PutAt(props, P_CrateStack2, "Crates", new Vector3(-1.35f, P, 0.7f), 20f, Vector3.one * 0.55f, _synAtlas);
            PutAt(props, P_Basket, "Basket", new Vector3(-1.3f, P, 1.5f), 0f, Vector3.one * 1.1f, _synAtlas);
            PutAt(props, P_SackStack, "Sacks", new Vector3(-1.3f, P, -0.8f), 30f, Vector3.one * 0.6f, _synAtlas);
            WallLamp(props, new Vector3(1.1f, P + 1.9f, 0.35f - 1.15f - 0.25f));
            if (level >= 2)
            {
                var loft = Group(root, "4_Loft"); var loftCap = Group(root, "5_LoftCap");
                float t2 = StuccoBox(loft, 0.35f, 0.35f, 0.7f, 0.7f, 1.4f, top + 0.5f, false, true, "Loft");
                float a2 = TileCap(loftCap, 0.35f, 0.35f, 0.7f, t2, "Loft");
                if (level >= 3)
                {
                    float t3 = StuccoBox(loft, 0.35f, 0.35f, 0.42f, 0.42f, 1.0f, t2 + 0.3f, false, false, "Top");
                    TileCap(loftCap, 0.35f, 0.35f, 0.42f, t3, "Top");
                    AlFlag(props, "Flag", new Vector3(1.7f, P, 1.7f), 3.5f, 0.45f);
                }
            }
        }

        /// <summary>Mine (4 m, Alanthor): a stone adit under a red headframe and teal hood; L2 a bigger hood; L3 a pagoda headframe.</summary>
        static void BuildAlMine(Transform root, int level)
        {
            float H = 2f, P = 0.28f;
            var g1 = Group(root, "1_Ground"); Slab(g1, "Yard", H, -H, H, P, _alStone); StoneRing(g1, H, -H, H, P, 1.6f);
            var g2 = Group(root, "2_Adit");
            PutAt(g2, C_Block, "Adit", new Vector3(0f, P, 1.0f), 0f, new Vector3(2.6f / 5.5f, 1.9f / 5f, 1.6f / 5.5f), _alStucco);
            PutAt(g2, C_DoorSingle, "AditMouth", new Vector3(0f, P, 0.15f), 0f, new Vector3(1.5f, 0.6f, 1f), _synAtlas, true);
            var g3 = Group(root, "3_Headframe"); var cap = Group(root, "5_Cap");
            float frameH = 2.6f + 0.4f * (level - 1);
            foreach (var (px, pz) in new[] { (-1.3f, -0.6f), (1.3f, -0.6f), (1.3f, 1.4f), (-1.3f, 1.4f) })
                PutAt(g3, C_PillarStoneRound, "Post", new Vector3(px, P, pz), 0f, new Vector3(0.14f, frameH / 5f, 0.14f), _alRed);
            if (level < 3) TileCap(cap, 0f, 0.4f, 1.3f + 0.2f * (level - 1), P + frameH - 0.05f, "Hood", 0.6f);
            else Pagoda(g3, cap, 0f, 0.4f, 1.3f, P + frameH - 2.5f + 0.5f, 2, "Head", false);
            PutCentred(g3, P_ChainDrop, "Hoist", new Vector3(0f, P + frameH - 0.8f, 0.4f), 0f, Vector3.one * 0.9f, _synAtlas);
            var g6 = Group(root, "6_Props");
            PutAt(g6, V_Cart, "Cart", new Vector3(-1.2f, P, -1.4f), 90f, Vector3.one * 0.45f, _synAtlas);
            for (int i = 0; i < 2 + level; i++)
                PutAt(g6, P_OreIron, "Ore", new Vector3(1.0f + (i % 2) * 0.4f, P, -1.5f + (i / 2) * 0.45f), i * 50f, Vector3.one * 3.2f, _synAtlas);
            WallLamp(g6, new Vector3(-1.0f, P + 1.6f, 0.15f));
        }

        /// <summary>Watch Tower (4 m): a stucco pagoda tower — 2, 3 and 4 tiers — on a stone base with lanterns.</summary>
        static void BuildWatchTower(Transform root, int level)
        {
            float H = 2f, P = 0.28f;
            var g1 = Group(root, "1_Ground"); Slab(g1, "Yard", H, -H, H, P, _alStone); StoneRing(g1, H, -H, H, P, 1.2f);
            var walls = Group(root, "2_Walls"); var cap = Group(root, "3_Cap");
            float h = 1.3f, y = P, wallH = 2.8f, top = 0f;
            for (int t = 0; t < level + 1; t++)
            {
                float wt = StuccoBox(walls, 0f, 0.1f, h, h, wallH, y, t == 0, true, "T" + t);
                top = TileCap(cap, 0f, 0.1f, h, wt, "T" + t, 0.7f);
                y = wt + 0.45f * h; h *= 0.78f; wallH = Mathf.Max(1.4f, wallH * 0.8f);
            }
            var props = Group(root, "6_Props");
            WallLamp(props, new Vector3(-0.9f, P + 2.2f, 0.1f - 1.3f - 0.25f)); WallLamp(props, new Vector3(0.9f, P + 2.2f, 0.1f - 1.3f - 0.25f));
            PutAt(props, P_Ladder, "Ladder", new Vector3(1.55f, P, 0.6f), 90f, new Vector3(0.8f, 0.7f, 0.8f), _synAtlas);
            if (level >= 2) PutAt(props, B_Brazier, "Brazier", new Vector3(-1.5f, P, -1.4f), 0f, Vector3.one * 1.2f, _synAtlas);
            if (level >= 3) AlFlag(props, "Flag", new Vector3(1.6f, P, 1.6f), 3.6f, 0.45f);
        }

        /// <summary>Trading Outpost (4 m): the House as a counting house with stalls and a flag; L2 a loft; L3 a pagoda tier.</summary>
        static void BuildTradingOutpost(Transform root, int level)
        {
            float H = 2f, P = 0.28f;
            var g1 = Group(root, "1_Ground"); AlanthorGround(g1, H);
            var walls = Group(root, "2_Walls"); var roof = Group(root, "3_Cap");
            float top = StuccoBox(walls, -0.3f, 0.4f, 1.1f, 1.1f, 2.4f, P, true, true, "Hall");
            TileCap(roof, -0.3f, 0.4f, 1.1f, top, "Hall");
            var props = Group(root, "6_Props");
            PutAt(props, P_SackStack, "Sacks", new Vector3(1.35f, P, 0.6f), -30f, Vector3.one * 0.6f, _synAtlas);
            PutAt(props, P_Barrel1, "Barrel", new Vector3(1.4f, P, 1.4f), 0f, Vector3.one * 0.6f, _synAtlas);
            PutAt(props, P_CrateStack, "Crates", new Vector3(1.4f, P, -1.0f), 20f, Vector3.one * 0.55f, _synAtlas);
            WallLamp(props, new Vector3(0.5f, P + 1.9f, 0.4f - 1.1f - 0.25f));
            if (level >= 2)
            {
                var loft = Group(root, "4_Loft"); var loftCap = Group(root, "5_LoftCap");
                float t2 = StuccoBox(loft, -0.3f, 0.4f, 0.68f, 0.68f, 1.4f, top + 0.5f, false, true, "Loft");
                TileCap(loftCap, -0.3f, 0.4f, 0.68f, t2, "Loft");
                if (level >= 3)
                {
                    float t3 = StuccoBox(loft, -0.3f, 0.4f, 0.4f, 0.4f, 1.0f, t2 + 0.3f, false, false, "Top");
                    TileCap(loftCap, -0.3f, 0.4f, 0.4f, t3, "Top");
                }
            }
            AlFlag(props, "Flag", new Vector3(1.7f, P, 1.7f), 3.4f + 0.4f * level, 0.45f);
        }

        /// <summary>Fortress (10 m, Alanthor): a pagoda keep on a walled platform with a torii; L2 three tiers and front towers; L3 four tiers and four towers.</summary>
        static void BuildAlFortress(Transform root, int level)
        {
            float H = 5f, P = 0.35f;
            var g1 = Group(root, "1_Ground"); AlanthorGround(g1, H, P, 0.6f, 2.2f);
            var walls = Group(root, "2_Walls"); var cap = Group(root, "3_Cap");
            Pagoda(walls, cap, 0f, 0.8f, 2.4f, P, level + 1, "Keep");
            var props = Group(root, "6_Props");
            Torii(props, 0f, -4.7f, 1.4f, 3.2f);
            WallLamp(props, new Vector3(-1.4f, P + 2.0f, 0.8f - 2.4f - 0.25f)); WallLamp(props, new Vector3(1.4f, P + 2.0f, 0.8f - 2.4f - 0.25f));
            PutAt(props, P_StatueKnight, "StatueW", new Vector3(-2.6f, P, -3.3f), 20f, Vector3.one * 0.8f, _synAtlas);
            PutAt(props, P_StatueKnight, "StatueE", new Vector3(2.6f, P, -3.3f), -20f, Vector3.one * 0.8f, _synAtlas);
            if (level >= 2)
            {
                var tw = Group(root, "4_Towers"); var tc = Group(root, "5_TowerCaps");
                foreach (var sx in new[] { -1f, 1f }) Pagoda(tw, tc, sx * 3.9f, -3.3f, 0.85f, P, 2, "TowerS" + (sx < 0 ? "W" : "E"), false);
                if (level >= 3)
                    foreach (var sx in new[] { -1f, 1f }) Pagoda(tw, tc, sx * 3.9f, 3.9f, 0.85f, P, 2, "TowerN" + (sx < 0 ? "W" : "E"), false);
            }
            AlFlag(props, "FlagW", new Vector3(-4.4f, P, 4.4f), 5.5f); AlFlag(props, "FlagE", new Vector3(4.4f, P, 4.4f), 5.5f);
        }

        /// <summary>Archery Range (8 m): an open shooting gallery along the back wall, targets at the far end, a lodge; L2 a loft; L3 a pagoda tower.</summary>
        static void BuildArcheryRange(Transform root, int level)
        {
            float H = 4f, P = 0.28f;
            var g1 = Group(root, "1_Ground"); AlanthorGround(g1, H, P, 0.5f, 1.8f);
            var walls = Group(root, "2_Walls"); var roof = Group(root, "3_Cap");
            // the lodge at the west end, the gallery (columns + tile gable) east of it
            float top = AlHall(walls, roof, -2.2f, 1.9f, 1.4f, 1.4f, 2.4f, P, true, true, "Lodge");
            foreach (var px in new[] { -0.2f, 1.6f, 3.4f })
                foreach (var pz in new[] { 0.8f, 3.0f })
                    PutAt(walls, C_PillarStoneRound, "GalleryColumn", new Vector3(px, P, pz), 0f, new Vector3(0.14f, 0.46f, 0.14f), _alRed);
            TileGable(roof, "Gallery", -0.5f, 3.7f, 1.9f, 1.3f, P + 2.3f, 1.0f);
            var props = Group(root, "6_Props");
            for (int i = 0; i < 3; i++) PutAt(props, B_Target, "Target" + Letter(i), new Vector3(-2.6f + i * 1.3f, P, -2.6f), 180f, Vector3.one * 1.1f, _synAtlas);
            PutAt(props, B_DummyArchery, "Dummy", new Vector3(3.0f, P, -2.6f), 180f, Vector3.one, _synAtlas);
            PutAt(props, P_WeaponRack5, "Rack", new Vector3(3.4f, P, -0.6f), -90f, Vector3.one * 0.8f, _synAtlas);
            WallLamp(props, new Vector3(-1.3f, P + 1.9f, 1.9f - 1.4f - 0.25f));
            if (level >= 2)
            {
                var loft = Group(root, "4_Loft"); var loftCap = Group(root, "5_LoftCap");
                float t2 = StuccoBox(loft, -2.2f, 1.9f, 0.8f, 0.8f, 1.4f, top + 0.5f, false, true, "Loft");
                float a2 = TileCap(loftCap, -2.2f, 1.9f, 0.8f, t2, "Loft");
                if (level >= 3) { float t3 = StuccoBox(loft, -2.2f, 1.9f, 0.5f, 0.5f, 1.0f, t2 + 0.3f, false, false, "Top"); TileCap(loftCap, -2.2f, 1.9f, 0.5f, t3, "Top"); }
            }
            if (level >= 3) Torii(props, 0f, -3.75f, 1.1f, 2.8f); else GateLamp(props, new Vector3(1.6f, 0f, -3.7f));
        }

        /// <summary>Royal Stable (8 m): an open-fronted stable hall on red columns, hitching posts, hay cart; L2 a second bay; L3 a tower and torii.</summary>
        static void BuildRoyalStable(Transform root, int level)
        {
            float H = 4f, P = 0.28f;
            var g1 = Group(root, "1_Ground"); AlanthorGround(g1, H, P, 0.5f, 1.8f);
            var walls = Group(root, "2_Walls"); var roof = Group(root, "3_Cap");
            float top = StuccoBox(walls, 0f, 2.0f, 3.4f, 1.3f, 2.4f, P, false, false, "Stable", columns: false);
            foreach (var px in new[] { -3.4f, -1.7f, 0f, 1.7f, 3.4f })
                PutAt(walls, C_PillarStoneRound, "Column", new Vector3(px, P, 0.7f), 0f, new Vector3(0.16f, 0.5f, 0.16f), _alRed);
            TileGable(roof, "Stable", -3.75f, 3.75f, 2.0f, 1.65f, top - 0.05f, 1.5f);
            var props = Group(root, "6_Props");
            foreach (var px in new[] { -2.4f, -0.8f, 0.8f, 2.4f })
                PutAt(props, P_Hitching, "Hitch", new Vector3(px, P, -0.6f), 0f, Vector3.one * 0.8f, _synAtlas);
            PutAt(props, P_HayCart, "HayCart", new Vector3(2.5f, P, -2.3f), 70f, Vector3.one * 0.6f, _synAtlas);
            PutAt(props, P_HayPile, "Hay", new Vector3(-2.6f, P, -2.2f), 0f, Vector3.one * 0.8f, _synAtlas);
            PutAt(props, P_Barrel1, "Trough", new Vector3(-3.3f, P, 0.0f), 0f, new Vector3(0.7f, 0.5f, 0.7f), _synAtlas);
            if (level >= 2)
            {
                var loft = Group(root, "4_Loft"); var loftCap = Group(root, "5_LoftCap");
                float t2 = StuccoBox(loft, 0f, 2.0f, 1.6f, 0.8f, 1.3f, top + 0.6f, false, true, "Loft");
                TileGable(loftCap, "Loft", -1.9f, 1.9f, 2.0f, 1.1f, t2 - 0.05f, 1.0f);
                if (level >= 3) Pagoda(loft, loftCap, -3.1f, 3.1f, 0.7f, P, 2, "Tower", false);
            }
            if (level >= 3) Torii(props, 0f, -3.75f, 1.1f, 2.8f); else GateLamp(props, new Vector3(1.6f, 0f, -3.7f));
        }

        /// <summary>Siege Yard (8 m): a workshop hall, a crane, engines in the yard — catapult, then a ballista, then a ram.</summary>
        static void BuildSiegeYard(Transform root, int level)
        {
            float H = 4f, P = 0.28f;
            var g1 = Group(root, "1_Ground"); AlanthorGround(g1, H, P, 0.5f, 2.2f);
            var walls = Group(root, "2_Walls"); var roof = Group(root, "3_Cap");
            float top = AlHall(walls, roof, 1.9f, 1.9f, 1.6f, 1.6f, 2.6f, P, true, true, "Shop");
            PutAt(walls, C_PillarStoneRound, "CranePost", new Vector3(-2.6f, P, 2.6f), 0f, new Vector3(0.18f, 0.9f, 0.18f), _alRed);
            PutCentred(walls, C_PanelWood, "CraneBoom", new Vector3(-1.4f, P + 4.2f, 2.0f), 30f, new Vector3(0.6f, 0.35f, 1f), _synAtlas, true);
            PutCentred(walls, P_ChainDrop, "CraneHook", new Vector3(-0.4f, P + 3.2f, 1.4f), 0f, Vector3.one * 0.9f, _synAtlas);
            var props = Group(root, "6_Props");
            PutAt(props, SE_Catapult, "Catapult", new Vector3(-2.2f, P, -1.4f), 200f, Vector3.one * 0.6f, _synAtlas);
            PutAt(props, P_Anvil, "Anvil", new Vector3(3.3f, P, -0.4f), 0f, Vector3.one * 0.9f, _synAtlas);
            PutAt(props, P_CrateStack2, "Crates", new Vector3(-3.3f, P, 0.8f), 30f, Vector3.one * 0.6f, _synAtlas);
            WallLamp(props, new Vector3(1.0f, P + 2.0f, 1.9f - 1.6f - 0.25f));
            if (level >= 2) PutAt(props, SE_Ballista, "Ballista", new Vector3(1.5f, P, -2.2f), 190f, Vector3.one * 0.6f, _synAtlas);
            if (level >= 3)
            {
                var loft = Group(root, "4_Loft"); var loftCap = Group(root, "5_LoftCap");
                float t2 = StuccoBox(loft, 1.9f, 1.9f, 0.9f, 0.9f, 1.4f, top + 0.5f, false, true, "Loft");
                TileCap(loftCap, 1.9f, 1.9f, 0.9f, t2, "Loft");
                AlFlag(props, "Flag", new Vector3(-3.5f, P, -2.8f), 4.5f);
            }
            else GateLamp(props, new Vector3(1.8f, 0f, -3.7f));
        }

        /// <summary>The Alanthor Temple (8 m): a colonnaded stucco shrine under a teal pyramid, the crystal on its plinth, torii, candles, a bell.</summary>
        static void BuildAlTemple(Transform root)
        {
            float H = 4f, P = 0.35f;
            var g1 = Group(root, "1_Ground"); AlanthorGround(g1, H, P, 0.6f, 2.2f);
            var walls = Group(root, "2_Walls"); var roof = Group(root, "3_Cap");
            float top = StuccoBox(walls, 0f, 0.9f, 2.2f, 2.0f, 2.8f, P, true, true, "Shrine");
            foreach (var px in new[] { -1.5f, -0.5f, 0.5f, 1.5f })
                PutAt(walls, C_PillarStoneRound, "Colonnade", new Vector3(px, P, -2.0f), 0f, new Vector3(0.14f, 0.6f, 0.14f), _alRed);
            TileCap(roof, 0f, 0.9f, 2.3f, top, "Shrine", 0.9f);
            var props = Group(root, "6_Props");
            PutAt(props, P_StatuePlinth, "Plinth", new Vector3(0f, P, -2.9f), 0f, Vector3.one * 1.0f, _synAtlas);
            PutAt(props, I_Crystal3, "Crystal", new Vector3(0f, P + 0.9f, -2.9f), 20f, Vector3.one * 3.8f, _age0Veil);
            PutAt(props, P_Candles, "CandlesA", new Vector3(-1.4f, P, -3.0f), 0f, Vector3.one * 2f, _synAtlas);
            PutAt(props, P_Candles, "CandlesB", new Vector3(1.4f, P, -3.0f), 50f, Vector3.one * 2f, _synAtlas);
            Torii(props, 0f, -3.75f, 1.2f, 3.0f);
            PutCentred(props, P_Bell, "Bell", new Vector3(3.2f, P + 2.6f, 3.0f), 0f, Vector3.one * 0.8f, _synAtlas);
            PutAt(props, C_PillarStoneRound, "BellPostA", new Vector3(2.6f, P, 3.0f), 0f, new Vector3(0.12f, 0.62f, 0.12f), _alRed);
            PutAt(props, C_PillarStoneRound, "BellPostB", new Vector3(3.8f, P, 3.0f), 0f, new Vector3(0.12f, 0.62f, 0.12f), _alRed);
            PutCentred(props, C_PanelWood, "BellBeam", new Vector3(3.2f, P + 3.1f, 3.0f), 0f, new Vector3(0.3f, 0.35f, 1f), _alRed, true);
            AlFlag(props, "FlagW", new Vector3(-3.5f, P, 3.4f), 4.5f);
        }

        // ═════════════════════════════════════════════════════════════════
        // Sect buildings (Alanthor-era, the stucco idiom with a sacred note)
        // ═════════════════════════════════════════════════════════════════

        static void SectBuildings()
        {
            SectForm(SECTS + "Antiquity/Buildings/Reliquary/", "Reliquary", "Sect_Reliquary", 4f, (r, h) =>
            {
                var w = Group(r, "2_Walls"); var c = Group(r, "3_Cap"); var p = Group(r, "6_Props");
                float top = StuccoBox(w, 0f, 0.9f, 2.0f, 1.6f, 2.6f, 0.28f, true, true, "Hall");
                TileGable(c, "Hall", -2.35f, 2.35f, 0.9f, 1.95f, top - 0.05f, 1.6f);
                PutAt(p, P_StatueKing, "King", new Vector3(0f, 0.28f, -2.6f), 0f, Vector3.one * 0.85f, _synAtlas);
                PutAt(p, P_Books, "Books", new Vector3(-3.0f, 0.28f, -2.4f), 20f, Vector3.one * 1.5f, _synAtlas);
                Torii(p, 0f, -3.75f, 1.1f, 2.8f);
            });
            SectForm(SECTS + "Fortitude/Buildings/Stonehold/", "Stonehold", "Sect_Stonehold", 4f, (r, h) =>
            {
                var w = Group(r, "2_Walls"); var c = Group(r, "3_Cap"); var p = Group(r, "6_Props");
                float top = StuccoBox(w, 0f, 0.8f, 2.4f, 2.2f, 3.2f, 0.28f, true, false, "Hold");
                foreach (var (side, yaw, alongX) in new[] { ("S", 0f, true), ("E", 90f, false), ("N", 180f, true), ("W", 270f, false) })
                {
                    Vector3 cc = side switch { "S" => new Vector3(0f, top, 0.8f - 2.2f), "E" => new Vector3(2.4f, top, 0.8f), "N" => new Vector3(0f, top, 3.0f), _ => new Vector3(-2.4f, top, 0.8f) };
                    PutAt(w, C_Batt1, "Batt" + side, cc, yaw, new Vector3((alongX ? 4.8f : 4.4f) / 5f, 0.7f, 0.8f), _alStucco, alongX);
                }
                TileCap(c, 0f, 0.8f, 1.2f, top + 0.6f, "Hold", 0.7f);
                PutAt(p, P_StatueKnight, "KnightW", new Vector3(-1.4f, 0.28f, -2.6f), 15f, Vector3.one * 0.8f, _synAtlas);
                PutAt(p, P_StatueKnight, "KnightE", new Vector3(1.4f, 0.28f, -2.6f), -15f, Vector3.one * 0.8f, _synAtlas);
                AlFlag(p, "Flag", new Vector3(3.4f, 0.28f, 3.4f), 4.5f);
            });
            SectForm(SECTS + "Prosperity/Buildings/Treasury/", "Treasury", "Sect_Treasury", 4f, (r, h) =>
            {
                var w = Group(r, "2_Walls"); var c = Group(r, "3_Cap"); var p = Group(r, "6_Props");
                float top = StuccoBox(w, 0f, 0.8f, 2.0f, 2.0f, 2.8f, 0.28f, true, true, "Treasury");
                float drum = StuccoTower(c, "Drum", new Vector3(0f, top - 0.3f, 0.8f), 1, 0.4f, false);
                PutAt(c, C_OnionDome, "Dome", new Vector3(0f, drum - 0.2f, 0.8f), 0f, new Vector3(0.7f, 0.65f, 0.7f), _alGold);
                PutAt(p, P_OreGold, "GoldA", new Vector3(-1.2f, 0.28f, -2.5f), 0f, Vector3.one * 2.4f, _synAtlas);
                PutAt(p, P_OreGold, "GoldB", new Vector3(1.3f, 0.28f, -2.6f), 40f, Vector3.one * 2.2f, _synAtlas);
                PutAt(p, P_Chest, "Chest", new Vector3(3.0f, 0.28f, -2.4f), -30f, Vector3.one * 0.9f, _synAtlas);
                Torii(p, 0f, -3.75f, 1.1f, 2.8f);
            });
            SectForm(SECTS + "Renewal/Buildings/MendingHall/", "MendingHall", "Sect_MendingHall", 4f, (r, h) =>
            {
                var w = Group(r, "2_Walls"); var c = Group(r, "3_Cap"); var p = Group(r, "6_Props");
                float top = StuccoBox(w, 0f, 0.9f, 2.6f, 1.6f, 2.6f, 0.28f, true, true, "Hall");
                TileGable(c, "Hall", -2.95f, 2.95f, 0.9f, 1.95f, top - 0.05f, 1.5f);
                PutCentred(p, P_Bell, "Bell", new Vector3(0f, top + 2.1f, 0.9f), 0f, Vector3.one * 0.9f, _synAtlas);
                PutAt(p, P_Bath, "Bath", new Vector3(-2.6f, 0.28f, -2.4f), 90f, Vector3.one * 0.8f, _synAtlas);
                PutAt(p, P_Candles, "Candles", new Vector3(2.6f, 0.28f, -2.5f), 0f, Vector3.one * 2f, _synAtlas);
                WallLamp(p, new Vector3(-1.2f, 0.28f + 2.0f, 0.9f - 1.6f - 0.25f)); WallLamp(p, new Vector3(1.2f, 0.28f + 2.0f, 0.9f - 1.6f - 0.25f));
            });
            SectForm(SECTS + "War/Buildings/MusterYard/", "MusterYard", "Sect_MusterYard", 4f, (r, h) =>
            {
                var w = Group(r, "2_Walls"); var c = Group(r, "3_Cap"); var p = Group(r, "6_Props");
                foreach (var px in new[] { -3.0f, -1.0f, 1.0f, 3.0f })
                    foreach (var pz in new[] { 1.4f, 3.2f })
                        PutAt(w, C_PillarStoneRound, "Column", new Vector3(px, 0.28f, pz), 0f, new Vector3(0.16f, 0.6f, 0.16f), _alRed);
                TileGable(c, "Yard", -3.6f, 3.6f, 2.3f, 1.4f, 0.28f + 2.95f, 1.1f);
                PutAt(p, P_WeaponRack5, "RackA", new Vector3(-2.6f, 0.28f, -0.6f), 0f, Vector3.one * 0.9f, _synAtlas);
                PutAt(p, P_WeaponRack5, "RackB", new Vector3(2.6f, 0.28f, -0.6f), 0f, Vector3.one * 0.9f, _synAtlas);
                PutAt(p, B_Dummy, "DummyA", new Vector3(-1.0f, 0.28f, -2.4f), 160f, Vector3.one, _synAtlas);
                PutAt(p, B_Dummy, "DummyB", new Vector3(1.0f, 0.28f, -2.4f), 200f, Vector3.one, _synAtlas);
                PutAt(p, P_StatueHero, "Hero", new Vector3(0f, 0.28f, 2.3f), 0f, Vector3.one * 0.9f, _synAtlas);
                AlFlag(p, "FlagW", new Vector3(-3.4f, 0.28f, -3.3f), 4.5f); AlFlag(p, "FlagE", new Vector3(3.4f, 0.28f, -3.3f), 4.5f);
            });
            SectForm(SECTS + "Renewal/Abilities/DeployFieldHospital/", "FieldHospital", "Renewal_FieldHospital", 2f, (r, h) =>
            {
                var w = Group(r, "2_Tent"); var p = Group(r, "6_Props");
                PutAt(w, Bld_TentSquare, "Tent", new Vector3(0f, 0.14f, 0.2f), 0f, Vector3.one * 0.55f, _synAtlas);
                PutAt(p, P_Bath, "Cot", new Vector3(-1.3f, 0.14f, -1.2f), 90f, Vector3.one * 0.6f, _synAtlas);
                PutAt(p, P_Candles, "Candles", new Vector3(1.3f, 0.14f, -1.3f), 0f, Vector3.one * 1.6f, _synAtlas);
                AlFlag(p, "Flag", new Vector3(1.6f, 0.14f, 1.6f), 3.0f, 0.4f);
            }, mud: true);
            SectForm(SECTS + "Renewal/Abilities/RaiseAnew/", "RenewalTower", "Renewal_Tower", 2f, (r, h) =>
            {
                var w = Group(r, "2_Walls"); var c = Group(r, "3_Cap"); var p = Group(r, "6_Props");
                float hh = 1.2f, y = 0.28f, wallH = 2.6f;
                for (int t = 0; t < 2; t++) { float wt = StuccoBox(w, 0f, 0.1f, hh, hh, wallH, y, t == 0, true, "T" + t); TileCap(c, 0f, 0.1f, hh, wt, "T" + t, 0.7f); y = wt + 0.45f * hh; hh *= 0.78f; wallH *= 0.8f; }
                WallLamp(p, new Vector3(0.8f, 0.28f + 2.0f, 0.1f - 1.2f - 0.25f));
            });
            SectForm(SECTS + "Renewal/Abilities/RaiseAnew/", "RenewalFortification", "Renewal_Fortification", 3f, (r, h) =>
            {
                var w = Group(r, "2_Walls"); var p = Group(r, "6_Props");
                float top = StuccoBox(w, 0f, 0f, 2.6f, 2.6f, 2.0f, 0.28f, false, false, "Bastion", columns: true);
                foreach (var (side, yaw, alongX) in new[] { ("S", 0f, true), ("E", 90f, false), ("N", 180f, true), ("W", 270f, false) })
                {
                    Vector3 cc = side switch { "S" => new Vector3(0f, top, -2.6f), "E" => new Vector3(2.6f, top, 0f), "N" => new Vector3(0f, top, 2.6f), _ => new Vector3(-2.6f, top, 0f) };
                    PutAt(w, C_Batt1, "Batt" + side, cc, yaw, new Vector3(5.2f / 5f, 0.7f, 0.8f), _alStucco, alongX);
                }
                PutAt(w, C_FloorStone1, "Deck", new Vector3(0f, top - 0.3f, 0f), 0f, new Vector3(1.0f, 0.6f, 1.0f), _alStone);
                PutAt(p, B_Brazier, "Brazier", new Vector3(0f, top - 0.0f, 0f), 0f, Vector3.one * 1.2f, _synAtlas);
                AlFlag(p, "Flag", new Vector3(2.0f, top, 2.0f), 3.0f, 0.4f);
            });
            SectForm(SECTS + "Renewal/Abilities/RaiseAnew/", "RenewalFortress", "Renewal_Fortress", 4f, (r, h) =>
            {
                var w = Group(r, "2_Walls"); var c = Group(r, "3_Cap"); var p = Group(r, "6_Props");
                Pagoda(w, c, 0f, 0.6f, 2.0f, 0.28f, 2, "Keep");
                Torii(p, 0f, -3.75f, 1.1f, 2.8f);
                AlFlag(p, "FlagW", new Vector3(-3.4f, 0.28f, 3.4f), 4.5f); AlFlag(p, "FlagE", new Vector3(3.4f, 0.28f, 3.4f), 4.5f);
            });
        }

        static void SectForm(string dir, string stem, string id, float half, Action<Transform, float> build, bool mud = false)
        {
            string so = dir + (stem.StartsWith("Renewal") ? stem.Replace("Renewal", "Renewal") : stem) + ".asset";
            if (!File.Exists(so)) so = Directory.GetFiles(dir, "*.asset").Select(f => f.Replace('\\', '/')).FirstOrDefault(f => File.ReadAllText(f).Contains("  id: " + id + "\n")) ?? so;
            PlainInPlace(dir + $"Art/{id}_Age_0.prefab", so, id + "_Age_0", root =>
            {
                var g1 = Group(root, "1_Ground");
                if (mud) MudYard(g1, half); else AlanthorGround(g1, half, 0.28f, half >= 4f ? 0.5f : 0.4f, half >= 4f ? 1.8f : 1.2f);
                build(root, half);
            });
        }

        // ═════════════════════════════════════════════════════════════════
        // Walls: the palisade (level 0, timber) and the stone wall (levels 1-3)
        // ═════════════════════════════════════════════════════════════════

        static void WallSets()
        {
            try
            {
                _wallPieces.Clear(); _collectWall = true;
                var pieces = AssetDatabase.FindAssets("t:WallPieceArtSO", new[] { TT })
                    .Select(g => AssetDatabase.LoadAssetAtPath<TheWaningBorder.Rendering.WallPieceArtSO>(AssetDatabase.GUIDToAssetPath(g)))
                    .Where(p => p != null).ToList();
                foreach (var piece in pieces)
                {
                    string dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(piece)).Replace('\\', '/') + "/";
                    string name = $"{(piece.level == 0 ? "Palisade" : "alanthor_StoneWall")}_{piece.part}_L{piece.level}";
                    GameObject built = piece.part switch
                    {
                        TheWaningBorder.Rendering.WallModuleArt.WallPart.Curtain => piece.level == 0 ? PalisadeCurtain() : StoneCurtain(piece.level),
                        TheWaningBorder.Rendering.WallModuleArt.WallPart.Hub => piece.level == 0 ? PalisadeHub() : StoneHub(piece.level),
                        TheWaningBorder.Rendering.WallModuleArt.WallPart.Gate => piece.level == 0 ? PalisadeGate() : StoneGate(piece.level),
                        TheWaningBorder.Rendering.WallModuleArt.WallPart.Tower => StoneWallTower(piece.level),
                        _ => StoneMount(piece.level),
                    };
                    built.name = name;
                    foreach (var f in Directory.GetFiles(dir).Where(f => f.EndsWith(".fbx") || f.EndsWith(".blend")))
                        if (AssetDatabase.DeleteAsset(f.Replace('\\', '/'))) Report.Add("OK removed " + f);
                    var prefab = Save(built, dir + name + ".prefab");
                    piece.prefab = prefab; EditorUtility.SetDirty(piece);
                    Report.Add($"OK wall {piece.part} L{piece.level} -> {dir}{name}.prefab");
                }
                _collectWall = false;
                MakeWallMeshesReadable();
                TheWaningBorder.Rendering.WallModuleArt.Invalidate();
            }
            catch (Exception e) { _collectWall = false; Fail("WallSets", e.ToString()); }
        }

        // kit metres: module M = 5 (game 3 m), walk at Walk = 5 (game 3 m); X = the run, +Z = the enemy side
        const float StoneDepth = 3.0f;

        /// <summary>A stone module from x0 to x1: stucco core (the Curtain_ part), stone walk, battlements both faces.</summary>
        static void StoneRun(Transform t, string tag, float x0, float x1, int level)
        {
            float len = x1 - x0, mid = (x0 + x1) * 0.5f, half = StoneDepth * 0.5f;
            PutAt(t, C_Block, "Curtain_Core" + tag, new Vector3(mid, 0f, 0f), 0f, new Vector3(len / 5.5f, (Walk - 0.1f) / 5f, StoneDepth / 5.5f), _alStucco);
            PutAt(t, C_FloorStone1, "Walk" + tag, new Vector3(mid, Walk - 0.3f, 0f), 0f, new Vector3(len / 5f, 0.6f, (StoneDepth + 0.3f) / 5f), _alStone);
            PutAt(t, C_Batt1, "BattA" + tag, new Vector3(mid, Walk - 0.3f, -half - 0.1f), 180f, new Vector3(len / 5f, 0.75f, 0.6f), _alStucco, true);
            PutAt(t, C_Batt1, "BattB" + tag, new Vector3(mid, Walk - 0.3f, half + 0.1f), 0f, new Vector3(len / 5f, 0.75f, 0.6f), _alStucco, true);
            if (level >= 2)   // battlement hoods: a teal tile hood over the walk
                TileGable(t, "Hood" + tag, x0 + 0.3f, x1 - 0.3f, 0f, half + 0.5f, Walk + 2.4f, 1.2f);
            if (level >= 3)
                foreach (var px in new[] { x0 + 0.8f, x1 - 0.8f })
                    PutCentred(t, P_BracketLamp, "Lamp" + tag, new Vector3(px, Walk + 1.5f, -half - 0.5f), 0f, Vector3.one * 1.2f, _synAtlas);
            if (level >= 2)
                foreach (var px in new[] { x0 + 0.3f, x1 - 0.3f })
                    foreach (var pz in new[] { -half - 0.2f, half + 0.2f })
                        PutAt(t, C_PillarStoneRound, "HoodPost" + tag, new Vector3(px, Walk - 0.3f, pz), 0f, new Vector3(0.14f, 0.55f, 0.14f), _alRed);
        }

        static GameObject StoneCurtain(int level)
        {
            var root = new GameObject("StoneCurtain");
            StoneRun(root.transform, "", -M * 0.5f, M * 0.5f, level);
            var along = new GameObject("Along").transform; along.SetParent(root.transform, false); along.localRotation = Quaternion.Euler(0f, 90f, 0f);
            return root;
        }

        static GameObject StoneHub(int level)
        {
            var root = new GameObject("StoneHub"); var t = root.transform;
            float hs = 2.8f, h = Walk + 1.2f + 0.6f * (level - 1);
            PutAt(t, C_Block, "Core", Vector3.zero, 0f, new Vector3(2f * hs / 5.5f, h / 5f, 2f * hs / 5.5f), _alStucco);
            foreach (var (px, pz) in new[] { (-hs, -hs), (hs, -hs), (hs, hs), (-hs, hs) })
                PutAt(t, C_PillarStoneRound, "Column", new Vector3(px, 0f, pz), 0f, new Vector3(0.22f, h / 5f, 0.22f), _alRed);
            foreach (var (side, yaw, alongX) in new[] { ("S", 0f, true), ("E", 90f, false), ("N", 180f, true), ("W", 270f, false) })
            {
                Vector3 cc = side switch { "S" => new Vector3(0f, h, -hs), "E" => new Vector3(hs, h, 0f), "N" => new Vector3(0f, h, hs), _ => new Vector3(-hs, h, 0f) };
                PutAt(t, C_Batt1, "Batt" + side, cc, yaw, new Vector3(2f * hs / 5f, 0.75f, 0.6f), _alStucco, alongX);
            }
            PutAt(t, C_FloorStone1, "Deck", new Vector3(0f, h - 0.3f, 0f), 0f, new Vector3(2f * hs / 5f, 0.6f, 2f * hs / 5f), _alStone);
            if (level >= 2) TileCap(t, 0f, 0f, hs * 0.75f, h + 0.9f, "Hub", 0.7f);
            if (level >= 3) AlFlag(t, "Hub", new Vector3(0f, h, 0f), 4.5f, 0.8f);
            Put(t, BN_Battle, "PlayerColor_Banner", new Vector3(0f, h - 0.3f, -hs - 0.3f), 180f, Vector3.one * 1.5f, Style.Neutral, null, _alCloth);
            return root;
        }

        static GameObject StoneGate(int level)
        {
            var root = new GameObject("StoneGate"); var t = root.transform;
            StoneRun(t, "L", -M * 1.5f, -M * 0.5f, level);
            StoneRun(t, "R", M * 0.5f, M * 1.5f, level);
            float half = StoneDepth * 0.5f, h = Walk + 1.0f;
            // the gatehouse: two stucco towers astride the passage, an arch over it
            foreach (var (px, tag) in new[] { (-M * 0.5f - 1.2f, "L"), (M * 0.5f + 1.2f, "R") })
            {
                PutAt(t, C_Block, "Tower" + tag, new Vector3(px, 0f, 0f), 0f, new Vector3(2.4f / 5.5f, (h + 2.0f) / 5f, (StoneDepth + 1.4f) / 5.5f), _alStucco);
                TileCap(t, px, 0f, 1.5f, h + 2.0f, "Tower" + tag, 0.7f);
            }
            // the lintel over the passage: stucco from above the doors to the walk, the full depth of the gatehouse
            float leafTop = 4.6f;
            PutAt(t, C_Block, "Lintel", new Vector3(0f, leafTop, 0f), 0f, new Vector3((M + 0.4f) / 5.5f, (Walk - leafTop + 0.2f) / 5f, (StoneDepth + 0.6f) / 5.5f), _alStucco);
            PutAt(t, C_FloorStone1, "PassageDeck", new Vector3(0f, Walk - 0.3f, 0f), 0f, new Vector3(M / 5f, 0.6f, (StoneDepth + 0.3f) / 5f), _alStone);
            PutAt(t, C_Batt1, "BattA", new Vector3(0f, Walk - 0.3f, -half - 0.1f), 180f, new Vector3(M / 5f, 0.75f, 0.6f), _alStucco, true);
            PutAt(t, C_Batt1, "BattB", new Vector3(0f, Walk - 0.3f, half + 0.1f), 0f, new Vector3(M / 5f, 0.75f, 0.6f), _alStucco, true);
            // the doors: hinge edge at the pivot, spanning -X
            float leafW = 2.3f, leafH = 4.4f;
            Put(t, C_DoorSingle, "DoorLeft", new Vector3(-M * 0.5f + 0.95f, 0.1f, 0f), 180f, new Vector3(leafW / 1.03f, leafH / 2.35f, 1.6f), Style.Neutral, null, _synAtlas);
            Put(t, C_DoorSingle, "DoorRight", new Vector3(M * 0.5f - 0.95f, 0.1f, 0f), 0f, new Vector3(leafW / 1.03f, leafH / 2.35f, 1.6f), Style.Neutral, null, _synAtlas);
            foreach (var (px, tag) in new[] { (-M * 0.5f - 1.2f, "L"), (M * 0.5f + 1.2f, "R") })
                Put(t, BN_Battle, "PlayerColor_Banner" + tag, new Vector3(px, h + 1.5f, half + 0.9f), 0f, Vector3.one * 1.5f, Style.Neutral, null, _alCloth);
            foreach (var sx in new[] { -1f, 1f })
                PutCentred(t, P_BracketLamp, "GateLamp", new Vector3(sx * (M * 0.5f - 0.3f), Walk - 1.2f, half + 0.9f), 0f, Vector3.one * 1.3f, _synAtlas);
            return root;
        }

        static GameObject StoneWallTower(int level)
        {
            var root = new GameObject("StoneTower"); var t = root.transform;
            StoneRun(t, "", -M * 0.5f, M * 0.5f, level);
            float hs = 2.2f, h = Walk + 3.0f + 0.8f * (level - 1);
            PutAt(t, C_Block, "TowerCore", Vector3.zero, 0f, new Vector3(2f * hs / 5.5f, h / 5f, 2f * hs / 5.5f), _alStucco);
            foreach (var (px, pz) in new[] { (-hs, -hs), (hs, -hs), (hs, hs), (-hs, hs) })
                PutAt(t, C_PillarStoneRound, "Column", new Vector3(px, 0f, pz), 0f, new Vector3(0.2f, h / 5f, 0.2f), _alRed);
            TileCap(t, 0f, 0f, hs + 0.2f, h, "Tower", 0.8f);
            foreach (var pz in new[] { -hs - 0.4f, hs + 0.4f })
                PutCentred(t, P_BracketLamp, "Lamp", new Vector3(0f, h - 1.2f, pz), 0f, Vector3.one * 1.3f, _synAtlas);
            Put(t, BN_Battle, "PlayerColor_Banner", new Vector3(0f, h - 2.5f, -hs - 0.3f), 180f, Vector3.one * 1.5f, Style.Neutral, null, _alCloth);
            return root;
        }

        static GameObject StoneMount(int level)
        {
            var root = new GameObject("StoneMount"); var t = root.transform;
            float depth = 6.5f;
            PutAt(t, C_Block, "Curtain_Bastion", Vector3.zero, 0f, new Vector3(depth / 5.5f, (Walk - 0.1f) / 5f, 1f), _alStucco);
            PutAt(t, C_FloorStone1, "Deck", new Vector3(0f, Walk - 0.3f, 0f), 0f, new Vector3(depth / 5f, 0.6f, 1.1f), _alStone);
            PutAt(t, C_Batt1, "BattA", new Vector3(0f, Walk - 0.3f, -2.75f), 180f, new Vector3(depth / 5f, 0.75f, 0.6f), _alStucco, true);
            PutAt(t, C_Batt1, "BattB", new Vector3(0f, Walk - 0.3f, 2.75f), 0f, new Vector3(depth / 5f, 0.75f, 0.6f), _alStucco, true);
            var deck = new GameObject("Deck"); deck.transform.SetParent(t, false); deck.transform.localPosition = new Vector3(0f, Walk, 0f);
            return root;
        }

        /// <summary>A timber module: plank hoarding faces both sides on a core, a plank walk, stakes along the enemy face.</summary>
        static void PalisadeRun(Transform t, string tag, float x0, float x1)
        {
            float len = x1 - x0, mid = (x0 + x1) * 0.5f, half = 1.6f;
            PutAt(t, C_Block, "Curtain_Core" + tag, new Vector3(mid, 0f, 0f), 0f, new Vector3(len / 5.5f, (Walk - 0.2f) / 5f, (2f * half - 0.4f) / 5.5f), _age0DarkStone);
            for (int course = 0; course < 2; course++)
            {
                PutAt(t, C_HoardWall, "PlanksA" + tag + course, new Vector3(mid, course * 2.2f, -half), 180f, new Vector3(len / 2.5f, 1.05f, 1f), _synAtlas, true);
                PutAt(t, C_HoardWall, "PlanksB" + tag + course, new Vector3(mid, course * 2.2f, half), 0f, new Vector3(len / 2.5f, 1.05f, 1f), _synAtlas, true);
            }
            PutAt(t, C_FloorWood, "Walk" + tag, new Vector3(mid, Walk - 0.2f, 0f), 0f, new Vector3(len / 5f, 0.5f, 2f * half / 5f), _synAtlas);
            PutAt(t, B_SpikeWall2, "Stakes" + tag, new Vector3(mid, Walk - 0.4f, half + 0.2f), 0f, new Vector3(len / 5f, 0.7f, 1.2f), _synAtlas, true);
            foreach (var px in new[] { x0 + 0.3f, x1 - 0.3f })
                foreach (var pz in new[] { -half, half })
                    PutAt(t, C_PillarRound, "Post" + tag, new Vector3(px, 0f, pz), 0f, new Vector3(0.6f, (Walk + 0.6f) / 5f, 0.6f), _synAtlas);
        }

        static GameObject PalisadeCurtain()
        {
            var root = new GameObject("PalisadeCurtain");
            PalisadeRun(root.transform, "", -M * 0.5f, M * 0.5f);
            var along = new GameObject("Along").transform; along.SetParent(root.transform, false); along.localRotation = Quaternion.Euler(0f, 90f, 0f);
            return root;
        }

        static GameObject PalisadeHub()
        {
            var root = new GameObject("PalisadeHub"); var t = root.transform;
            float hs = 2.6f, h = Walk + 0.6f;
            PutAt(t, C_Block, "Core", Vector3.zero, 0f, new Vector3(2f * hs / 5.5f, (h - 0.2f) / 5f, 2f * hs / 5.5f), _age0DarkStone);
            foreach (var (side, yaw, alongX) in new[] { ("S", 0f, true), ("E", 90f, false), ("N", 180f, true), ("W", 270f, false) })
                for (int course = 0; course < 2; course++)
                {
                    Vector3 cc = side switch { "S" => new Vector3(0f, course * 2.2f, -hs), "E" => new Vector3(hs, course * 2.2f, 0f), "N" => new Vector3(0f, course * 2.2f, hs), _ => new Vector3(-hs, course * 2.2f, 0f) };
                    PutAt(t, C_HoardWall, "Planks" + side + course, cc, yaw, new Vector3(2f * hs / 2.5f, 1.05f, 1f), _synAtlas, alongX);
                }
            PutAt(t, C_FloorWood, "Walk", new Vector3(0f, h - 0.3f, 0f), 0f, new Vector3(2f * hs / 5f, 0.5f, 2f * hs / 5f), _synAtlas);
            foreach (var (px, pz) in new[] { (-hs, -hs), (hs, -hs), (hs, hs), (-hs, hs) })
                PutAt(t, C_PillarRound, "CornerLog", new Vector3(px, 0f, pz), 0f, new Vector3(0.7f, (h + 0.9f) / 5f, 0.7f), _synAtlas);
            foreach (var (side, yaw, alongX) in new[] { ("S", 0f, true), ("E", 90f, false), ("N", 180f, true), ("W", 270f, false) })
            {
                Vector3 cc = side switch { "S" => new Vector3(0f, h - 0.4f, -hs + 0.3f), "E" => new Vector3(hs - 0.3f, h - 0.4f, 0f), "N" => new Vector3(0f, h - 0.4f, hs - 0.3f), _ => new Vector3(-hs + 0.3f, h - 0.4f, 0f) };
                PutAt(t, B_SpikeWall2, "Parapet" + side, cc, yaw, new Vector3(2f * hs / 5f, 0.7f, 1.2f), _synAtlas, alongX);
            }
            PutAt(t, B_Brazier, "Brazier", new Vector3(0f, h - 0.3f, 0f), 0f, Vector3.one * 2.0f, _synAtlas);
            Banner(t, "PlayerColor_Banner", new Vector3(0f, h - 0.3f, -hs - 0.3f), 180f, 1.6f, _age0Cloth);
            return root;
        }

        static GameObject PalisadeGate()
        {
            var root = new GameObject("PalisadeGate"); var t = root.transform;
            PalisadeRun(t, "L", -M * 1.5f, -M * 0.5f);
            PalisadeRun(t, "R", M * 0.5f, M * 1.5f);
            float half = 1.6f;
            foreach (var (px, tag) in new[] { (-M * 0.5f + 0.5f, "L"), (M * 0.5f - 0.5f, "R") })
            {
                PutAt(t, C_PillarRound, "GatePost" + tag + "A", new Vector3(px, 0f, -half + 0.4f), 0f, new Vector3(0.75f, (Walk + 2.6f) / 5f, 0.75f), _synAtlas);
                PutAt(t, C_PillarRound, "GatePost" + tag + "B", new Vector3(px, 0f, half - 0.4f), 0f, new Vector3(0.75f, (Walk + 2.6f) / 5f, 0.75f), _synAtlas);
            }
            PutAt(t, C_FloorWood, "PassageDeck", new Vector3(0f, Walk - 0.2f, 0f), 0f, new Vector3(M / 5f, 0.5f, 2f * half / 5f), _synAtlas);
            PutCentred(t, C_PanelWood, "LintelA", new Vector3(0f, Walk - 0.8f, -half + 0.1f), 0f, new Vector3(M / 5f, 0.6f, 1f), _synAtlas, true);
            PutCentred(t, C_PanelWood, "LintelB", new Vector3(0f, Walk - 0.8f, half - 0.1f), 180f, new Vector3(M / 5f, 0.6f, 1f), _synAtlas, true);
            PutAt(t, B_SpikeWall2, "ParapetB", new Vector3(0f, Walk - 0.4f, half + 0.2f), 0f, new Vector3(M / 5f, 0.7f, 1.2f), _synAtlas, true);
            float leafW = 2.3f, leafH = 4.4f;
            Put(t, C_DoorSingle, "DoorLeft", new Vector3(-M * 0.5f + 0.95f, 0.1f, 0f), 180f, new Vector3(leafW / 1.03f, leafH / 2.35f, 1.6f), Style.Neutral, null, _synAtlas);
            Put(t, C_DoorSingle, "DoorRight", new Vector3(M * 0.5f - 0.95f, 0.1f, 0f), 0f, new Vector3(leafW / 1.03f, leafH / 2.35f, 1.6f), Style.Neutral, null, _synAtlas);
            PutAt(t, P_Torch2, "TorchL", new Vector3(-M * 0.5f - 0.4f, Walk - 1.6f, half + 0.5f), 0f, Vector3.one * 1.2f, _synAtlas);
            PutAt(t, P_Torch2, "TorchR", new Vector3(M * 0.5f + 0.4f, Walk - 1.6f, half + 0.5f), 0f, Vector3.one * 1.2f, _synAtlas);
            Banner(t, "PlayerColor_BannerL", new Vector3(-M * 0.5f - 1.5f, Walk - 0.2f, half + 0.3f), 0f, 1.6f, _age0Cloth);
            Banner(t, "PlayerColor_BannerR", new Vector3(M * 0.5f + 1.5f, Walk - 0.2f, half + 0.3f), 0f, 1.6f, _age0Cloth);
            return root;
        }

        // ═════════════════════════════════════════════════════════════════
        // Retired art and previews
        // ═════════════════════════════════════════════════════════════════

        static void DeleteRetiredArt()
        {
            var dirs = new List<string>();
            foreach (var b in new[] { "Barracks", "GatherersHut", "Mine", "VeilstoneMine", "TempleOfRidan", "Fortress", "FiendstoneKeep", "VaultOfAlmierra" })
                dirs.Add(AGE0 + b + "/Art");
            foreach (var b in new[] { "ArcheryRange", "Fortress", "Garrison", "Guild", "Mine", "RoyalStable", "SiegeYard", "Tower", "TradingOutpost", "VaultOfAlmierra" })
            { dirs.Add(ALA + b + "/Art"); dirs.Add(ALA + b); }
            dirs.AddRange(new[] { SECTS + "Antiquity/Buildings/Reliquary/Art", SECTS + "Fortitude/Buildings/Stonehold/Art", SECTS + "Prosperity/Buildings/Treasury/Art",
                                  SECTS + "Renewal/Abilities/DeployFieldHospital/Art", SECTS + "Renewal/Abilities/RaiseAnew/Art", SECTS + "Renewal/Buildings/MendingHall/Art",
                                  SECTS + "War/Buildings/MusterYard/Art" });
            foreach (var d in dirs)
            {
                if (!Directory.Exists(d)) continue;
                foreach (var f in Directory.GetFiles(d).Where(f => f.EndsWith(".fbx") || f.EndsWith(".blend")))
                    if (AssetDatabase.DeleteAsset(f.Replace('\\', '/'))) Report.Add("OK removed " + f.Replace('\\', '/'));
            }
            // the old base prefab of the Trading Outpost (its form is Art/Alanthor_TradingOutpost_Age_0.prefab now)
            if (AssetDatabase.DeleteAsset(ALA + "TradingOutpost/TradingOutpost.prefab")) Report.Add("OK removed the old TradingOutpost.prefab");
        }

        static void RenderAge0Alanthor()
        {
            Directory.CreateDirectory(AlaPreviewDir);
            var dir = new Vector3(0.6f, 0.75f, -1f);
            foreach (var (label, path) in new (string, string)[]
            {
                ("Barracks0", AGE0 + "Barracks/Art/Barracks_Age_0.prefab"), ("Gatherers0", AGE0 + "GatherersHut/Art/GatherersHut_Age_0.prefab"),
                ("Mine0", AGE0 + "Mine/Art/Mine_Age_0.prefab"), ("VeilMine0", AGE0 + "VeilstoneMine/Art/VeilstoneMine_Age_0.prefab"),
                ("Temple0", AGE0 + "TempleOfRidan/Art/TempleOfRidan_Age_0.prefab"), ("Shelter", AGE0 + "Fortress/Art/Fortress_Age_0.prefab"),
                ("Keep", AGE0 + "FiendstoneKeep/Art/FiendstoneKeep_Age_0.prefab"), ("Vault0", AGE0 + "VaultOfAlmierra/Art/VaultOfAlmierra_Age_0.prefab"),
                ("TempleAl", AGE0 + "TempleOfRidan/Art/alanthor_TempleOfRidan_lv1.prefab"),
                ("Reliquary", SECTS + "Antiquity/Buildings/Reliquary/Art/Sect_Reliquary_Age_0.prefab"), ("Stonehold", SECTS + "Fortitude/Buildings/Stonehold/Art/Sect_Stonehold_Age_0.prefab"),
                ("Treasury", SECTS + "Prosperity/Buildings/Treasury/Art/Sect_Treasury_Age_0.prefab"), ("MendingHall", SECTS + "Renewal/Buildings/MendingHall/Art/Sect_MendingHall_Age_0.prefab"),
                ("MusterYard", SECTS + "War/Buildings/MusterYard/Art/Sect_MusterYard_Age_0.prefab"), ("FieldHospital", SECTS + "Renewal/Abilities/DeployFieldHospital/Art/Renewal_FieldHospital_Age_0.prefab"),
                ("RenewalTower", SECTS + "Renewal/Abilities/RaiseAnew/Art/Renewal_Tower_Age_0.prefab"), ("RenewalFortification", SECTS + "Renewal/Abilities/RaiseAnew/Art/Renewal_Fortification_Age_0.prefab"),
                ("RenewalFortress", SECTS + "Renewal/Abilities/RaiseAnew/Art/Renewal_Fortress_Age_0.prefab"),
                ("Frame_Tower", ALA + "Tower/Art/Alanthor_Tower_Age_0.prefab"),
            })
            {
                Report.Add(RenderOne(label, path, -1, 0, AlaPreviewDir, dir, ""));
                Report.Add(RenderOne(label, path, -1, 0, AlaPreviewDir, new Vector3(-0.6f, 0.75f, 1f), "_back"));
            }
            foreach (var (folder, stem, fp) in new (string, string, float)[]
            {
                ("Garrison", "Garrison", 8f), ("Guild", "Guild", 4f), ("Mine", "Mine", 4f), ("Tower", "WatchTower", 4f), ("TradingOutpost", "TradingOutpost", 4f),
                ("Fortress", "Fortress", 10f), ("ArcheryRange", "ArcheryRange", 8f), ("RoyalStable", "RoyalStable", 8f), ("SiegeYard", "SiegeYard", 8f), ("VaultOfAlmierra", "VaultOfAlmierra", 8f),
            })
            {
                string f = folder, s = stem;
                try { Report.Add(RenderComparison("Al_" + stem, AlaPreviewDir, new Vector2(fp, fp), l => Instance(ALA + $"{f}/Art/alanthor_{s}_lv{l}.prefab"))); }
                catch (Exception e) { Report.Add("FAIL compare " + stem + ": " + e.Message); }
            }
            foreach (var (label, path) in new (string, string)[]
            {
                ("Wall_PalisadeCurtain", AGE0 + "Palisade/Lvl0/Segment/Palisade_Curtain_L0.prefab"), ("Wall_PalisadeHub", AGE0 + "Palisade/Lvl0/Hub/Palisade_Hub_L0.prefab"),
                ("Wall_PalisadeGate", AGE0 + "Palisade/Lvl0/Gate/Palisade_Gate_L0.prefab"),
                ("Wall_StoneCurtain1", ALA + "StoneWall/Lvl1/Segment/alanthor_StoneWall_Curtain_L1.prefab"), ("Wall_StoneHub1", ALA + "StoneWall/Lvl1/Hub/alanthor_StoneWall_Hub_L1.prefab"),
                ("Wall_StoneGate1", ALA + "StoneWall/Lvl1/Gate/alanthor_StoneWall_Gate_L1.prefab"), ("Wall_StoneTower1", ALA + "StoneWall/Lvl1/Tower/alanthor_StoneWall_Tower_L1.prefab"),
                ("Wall_StoneCurtain3", ALA + "StoneWall/Lvl3/Segment/alanthor_StoneWall_Curtain_L3.prefab"), ("Wall_StoneGate3", ALA + "StoneWall/Lvl3/Gate/alanthor_StoneWall_Gate_L3.prefab"),
                ("Wall_StoneMount3", ALA + "StoneWall/Lvl3/Trebuchet/alanthor_StoneWall_TrebuchetMount_L3.prefab"),
            })
                Report.Add(RenderOne(label, path, -1, 0, AlaPreviewDir, dir, ""));
        }
    }
}
