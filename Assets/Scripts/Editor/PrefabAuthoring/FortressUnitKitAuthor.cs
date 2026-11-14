// FortressUnitKitAuthor — the Alanthor Fortress, "PT3" (2026-11-11): a
// Roman-Japanese walled compound composed 1 : 1 from the UNIT KIT
// (tools/blender/alanthor/build_alanthor_unit_kit.py, UnitKitBinder.cs) with
// Synty props at their native size. Approach B: purpose-built roofs and thin
// parts; nothing here is the thick castle kit.
//
// The capital of the turtle civ — strength without a block:
//   Podium     — a battered dry-stone base (ishigaki / Roman podium), 1 m
//                above ground and 1.3 m below it (the foundation skirt), the
//                court floor raised onto it so the game camera sees in.
//   Compound   — a thin white plaster wall on the podium, loop slits at head
//                height, a narrow kawara coping, the PLAYER-COLOUR band
//                painted at chest height. 3.6 m above ground.
//   Gate       — recessed behind steps (a small masugata): a marble portico
//                (slender columns, entablature; a pediment at L3) under a
//                curved kawara roof; studded leaves 2.2 m tall.
//   Yagura     — slim two-storey corner turrets, each a skirt and a hip cap.
//   Keep       — the tenshu at the back of the court on its own stone base:
//                2 tiers at L1, 3 at L2, 4 at L3, every tier stepping in under
//                its own kawara skirt; a hipped top with gilt ridge fins.
//   Colonnade  — marble columns under a lean-to along the west wall (L2+).
//   Court      — open, with unit-scale military props.
// Every level shares one frame; the "Footprint" group is a 10 m ground patch
// flush with the terrain (no slab), so the fit is 1.0 and the skirt stays
// underground. Composed with the front toward -Z, turned on save (the game
// turns every building view half a turn).
//
//   Waning Border > Art > Proportion Test > Author Fortress PT3 (unit kit)
//   Waning Border > Art > Proportion Test > Render Fortress PT3
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static partial class FeraldisKitAuthor
    {
        const float U3Edge = 4.60f;     // the podium's top outer edge (its foot is on the 5 m line)
        const float U3Wall = 4.43f;     // the compound wall's centre line
        const float U3Court = 1.2f;     // the court floor
        const float U3WallH = 2.3f;
        const float U3GateZ = -3.17f;   // the gate, recessed behind the steps
        const float U3Notch = 1.52f;    // half the stair notch, cheeks included
        const float U3KeepZ = 1.85f;
        const float U3TurretHalf = 1.1f;

        static string P3Path(int level) => PtFortress.Dir + $"ProportionTest/Fortress_PT3_Lvl{level}.prefab";
        static Dictionary<string, (string[] slots, int planes)> _ukManifest;
        static int _ukPlanes, _ukCopingPlanes, _ukKawaraPieces;

        /// <summary>A unit-kit piece as a nested prefab instance; local y up, its outside toward -Z at yaw 0.</summary>
        static GameObject UK(Transform parent, string piece, string name, Vector3 pos, float yaw = 0f, Vector3? scale = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UnitKitBinder.PiecePath("UK_" + piece));
            if (prefab == null) throw new Exception("missing unit-kit piece UK_" + piece + " (run the Blender script, then Bind Unit Kit)");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.name = name;
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = scale ?? Vector3.one;
            if (_ukManifest != null && _ukManifest.TryGetValue("UK_" + piece, out var info) && info.planes > 0)
            {
                _ukKawaraPieces++;
                if (piece.StartsWith("Kawara_Coping")) _ukCopingPlanes += info.planes; else _ukPlanes += info.planes;
            }
            return go;
        }

        /// <summary>A point on side S / N / W / E: `along` the side, `across` out from the centre; and the yaw that turns a piece's outside outward.</summary>
        static (Vector3 pos, float yaw) U3Side(string side, float along, float across, float y) => side switch
        {
            "S" => (new Vector3(along, y, -across), 0f),
            "N" => (new Vector3(along, y, across), 180f),
            "W" => (new Vector3(-across, y, along), 90f),
            _ => (new Vector3(across, y, along), 270f),
        };

        /// <summary>Pieces of native length `module` laid from a to b along a side, scaled evenly to fit.</summary>
        static void U3Lay(Transform parent, string tag, string side, float a, float b, float across, float y, float module, Func<int, string> piece)
        {
            float len = b - a;
            if (len < 0.2f) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(len / module));
            for (int i = 0; i < n; i++)
            {
                var (pos, yaw) = U3Side(side, a + len * (i + 0.5f) / n, across, y);
                UK(parent, piece(i), $"{tag}{side}{Letter(i)}", pos, yaw, new Vector3(len / n / module, 1f, 1f));
            }
        }

        [MenuItem("Waning Border/Art/Proportion Test/Author Fortress PT3 (unit kit)")]
        public static void AuthorFortressUnitKit()
        {
            Report.Clear(); _failures = 0;
            try
            {
                _fkRoof = LoadMat(AlanthorArtDir + "Castle_Roof_Player.mat");
                _fkFlag = LoadMat(AlanthorArtDir + "Flag_Symbol_Player.mat");
                _ukManifest = UnitKitBinder.ReadManifest(out float eave, out Vector2 tile);
                Report.Add($"unit kit: {_ukManifest.Count} pieces, eave edge {eave * 100f:F1} cm, tile {tile.x * 100f:F0} x {tile.y * 100f:F0} cm");
                _pt = PtFortress;
                var planes = new int[4]; var kawara = new int[4];
                for (int l = 1; l <= 3; l++)
                {
                    var root = new GameObject($"Fortress_PT3_Lvl{l}");
                    try
                    {
                        _ukPlanes = 0; _ukCopingPlanes = 0; _ukKawaraPieces = 0;
                        U3Build(root.transform, l);
                        planes[l] = _ukPlanes; kawara[l] = _ukKawaraPieces;
                        for (int c = 0; c < root.transform.childCount; c++)
                            root.transform.GetChild(c).localRotation = Quaternion.Euler(0f, 180f, 0f);
                        MeshBounds(root, out var b);
                        int pieces = root.GetComponentsInChildren<Transform>(true).Count(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject));
                        Report.Add($"Fortress_PT3_Lvl{l}: {pieces} pieces ({kawara[l]} kawara pieces: {planes[l]} roof planes + {_ukCopingPlanes} coping slopes), bounds min {b.min.ToString("F2")} max {b.max.ToString("F2")}");
                    }
                    catch (Exception) { UnityEngine.Object.DestroyImmediate(root); throw; }
                    Save(root, P3Path(l));
                }
                AssetDatabase.SaveAssets();
                U3Report(planes, eave, tile);
                RenderFortressUnitKit();
            }
            catch (Exception e) { Fail("FortressPT3", e.ToString()); }
            string text = $"[ProportionTest] PT3 done — {Report.Count} entries, {_failures} failure(s)\n" + string.Join("\n", Report);
            Debug.Log(text);
            try { Directory.CreateDirectory(PtOut); File.WriteAllText(PtOut + "/report_pt3.txt", text); } catch (Exception) { }
        }

        // ═════════════════════════════════════════════════════════════════
        static void U3Build(Transform root, int level)
        {
            var fp = Group(root, "Footprint");
            fp.gameObject.AddComponent<TheWaningBorder.Rendering.FootprintBounds>();
            UK(fp, "Ground_Patch_10", "GroundPatch", Vector3.zero);

            // Which corners carry a yagura.
            var turrets = new List<(string n, float sx, float sz, float half, bool tall)> { ("SW", -1f, -1f, U3TurretHalf, true), ("SE", 1f, -1f, U3TurretHalf, true) };
            if (level >= 3) { turrets.Add(("NW", -1f, 1f, 0.8f, false)); turrets.Add(("NE", 1f, 1f, 0.8f, false)); }
            bool Has(float sx, float sz) => turrets.Any(t => t.sx == sx && t.sz == sz);
            // Where the wall stops against the turret on that corner (the wall line itself where there is none).
            float End(float sx, float sz) { foreach (var t in turrets) if (t.sx == sx && t.sz == sz) return U3Wall - 2f * t.half + 0.07f; return U3Wall; }

            // ── Podium: the battered stone base, 1.3 m into the ground ──
            var podium = Group(root, "Podium");
            float inner = U3Edge - 0.35f;
            foreach (var (n, sx, sz, yaw) in new[] { ("SW", -1f, -1f, 0f), ("NW", -1f, 1f, 90f), ("NE", 1f, 1f, 180f), ("SE", 1f, -1f, 270f) })
                UK(podium, "Base_Corner", "BaseCorner" + n, new Vector3(sx * U3Edge, 0f, sz * U3Edge), yaw);
            foreach (var side in new[] { "W", "N", "E" })
                U3Lay(podium, "Base", side, -inner, inner, U3Edge, 0f, 2.5f, _ => "Base_Run_25");
            U3Lay(podium, "BaseL", "S", -inner, -U3Notch, U3Edge, 0f, 1.875f, i => i == 0 ? "Base_Run_25" : "Base_Run_125");
            U3Lay(podium, "BaseR", "S", U3Notch, inner, U3Edge, 0f, 1.875f, i => i == 0 ? "Base_Run_125" : "Base_Run_25");
            // (the two S runs are one full and one half piece each: scale them by their own lengths)
            U3FixRunScale(podium, "BaseL", -inner, -U3Notch); U3FixRunScale(podium, "BaseR", U3Notch, inner);
            UK(podium, "Base_Steps_24", "Steps", new Vector3(0f, 0f, -U3Edge));
            float landing = -U3Edge + 1.28f;          // where the steps reach the court
            float span = 2f * inner;
            UK(podium, "Court_Floor_90", "CourtFloor", new Vector3(0f, U3Court, (landing + inner) * 0.5f), 0f, new Vector3(span / 9f, 1f, (inner - landing) / 9f));
            foreach (float sx in new[] { -1f, 1f })
                UK(podium, "Court_Floor_90", "CourtFloor" + (sx < 0 ? "W" : "E"), new Vector3(sx * (U3Notch + inner) * 0.5f, U3Court, (landing - inner) * 0.5f), 0f,
                   new Vector3((inner - U3Notch) / 9f, 1f, (landing + inner) / 9f));

            // ── Compound wall: thin plaster, loop slits, the band, a coping ──
            var wall = Group(root, "CompoundWall");
            var coping = Group(root, "WallCoping");
            float top = U3Court + U3WallH;
            void WallRun(string tag, string side, float a, float b)
            {
                if (b - a < 1.9f) U3Lay(wall, tag, side, a, b, U3Wall, U3Court, 1.25f, _ => "Plaster_Wall_125");
                else U3Lay(wall, tag, side, a, b, U3Wall, U3Court, 2.5f, i => i % 2 == 0 ? "Plaster_Wall_25_Slit" : "Plaster_Wall_25");
                U3Lay(coping, tag + "Coping", side, a, b, U3Wall, top, 2.5f, _ => "Kawara_Coping_25");
            }
            // along runs west->east on S / N, south->north on W / E
            WallRun("WallL", "S", -End(-1, -1), -U3Notch);
            WallRun("WallR", "S", U3Notch, End(1, -1));
            WallRun("Wall", "N", -End(-1, 1), End(1, 1));
            WallRun("Wall", "W", -End(-1, -1), End(-1, 1));
            WallRun("Wall", "E", -End(1, -1), End(1, 1));
            foreach (var (n, sx, sz) in new[] { ("SW", -1f, -1f), ("NW", -1f, 1f), ("NE", 1f, 1f), ("SE", 1f, -1f) })
            {
                if (Has(sx, sz)) continue;
                UK(wall, "Timber_Post_23", "CornerPost" + n, new Vector3(sx * U3Wall, U3Court, sz * U3Wall));
                UK(coping, "Kawara_Coping_Corner", "CopingCorner" + n, new Vector3(sx * U3Wall, top, sz * U3Wall));
            }
            // The gate recess: return walls back to the gate line, the gate's own flanks.
            float retLen = U3Wall + U3GateZ;          // from the wall line back to the gate
            foreach (float sx in new[] { -1f, 1f })
            {
                string s = sx < 0 ? "W" : "E";
                var at = new Vector3(sx * U3Notch, U3Court, -U3Wall + retLen * 0.5f);
                UK(wall, "Plaster_Wall_125", "Return" + s, at, sx < 0 ? 270f : 90f, new Vector3(retLen / 1.25f, 1f, 1f));
                UK(coping, "Kawara_Coping_25", "ReturnCoping" + s, new Vector3(at.x, top, at.z), 90f, new Vector3(retLen / 2.5f, 1f, 1f));
                UK(wall, "Timber_Post_23", "ReturnPost" + s, new Vector3(sx * U3Notch, U3Court, -U3Wall));
                UK(wall, "Keep_Wall_10", "GateFlank" + s, new Vector3(sx * (1.1f + U3Notch) * 0.5f, U3Court, U3GateZ), 0f, new Vector3(U3Notch - 1.1f, U3WallH / 2f, 1f));
            }

            // ── Gate: marble portico, kawara roof, a soldier's door ─────
            var gate = Group(root, "Gatehouse");
            UK(gate, "Gate_Frame_22", "GateFrame", new Vector3(0f, U3Court, U3GateZ));
            UK(gate, "Gate_Leaf_Pair_22", "GateLeaf", new Vector3(0f, U3Court, U3GateZ));
            UK(gate, "Keep_Wall_10", "GateHead", new Vector3(0f, U3Court + 2.58f, U3GateZ), 0f, new Vector3(2f * U3Notch, 0.24f, 1f));
            const float colZ = -4.78f, colX = 1.34f, colH = 2.6f;
            float entY = U3Court + 0.07f + colH;
            foreach (float sx in new[] { -1f, 1f })
            {
                UK(gate, "Marble_Column_26", "PorticoColumn" + (sx < 0 ? "W" : "E"), new Vector3(sx * colX, U3Court + 0.07f, colZ));
                if (level >= 2)
                {
                    UK(gate, "Marble_Column_26", "PorticoColumnIn" + (sx < 0 ? "W" : "E"), new Vector3(sx * colX, U3Court, U3GateZ - 0.35f), 0f, new Vector3(1f, 2.67f / 2.6f, 1f));
                    float mid = (colZ + U3GateZ - 0.35f) * 0.5f, len = Mathf.Abs(colZ - (U3GateZ - 0.35f)) + 0.3f;
                    UK(gate, "Marble_Entablature_25", "PorticoSide" + (sx < 0 ? "W" : "E"), new Vector3(sx * colX, entY, mid), sx < 0 ? 90f : 270f, new Vector3(len / 2.5f, 1f, 1f));
                }
            }
            UK(gate, "Marble_Entablature_25", "PorticoFront", new Vector3(0f, entY, colZ), 0f, new Vector3((2f * colX + 0.5f) / 2.5f, 1f, 1f));
            if (level >= 3) UK(gate, "Marble_Pediment_32", "PorticoPediment", new Vector3(0f, entY + 0.30f, colZ));
            var gateCap = Group(root, "GateCap");
            UK(gateCap, "Kawara_Gate", "GateKawara", new Vector3(0f, entY + 0.30f, -3.62f));
            if (level >= 2)
                foreach (float sx in new[] { -1f, 1f })
                    UK(gate, "Lantern_Hanging", "GateLantern" + (sx < 0 ? "W" : "E"), new Vector3(sx * 0.75f, entY - 0.05f, colZ + 0.45f));

            // ── Yagura: slim corner turrets ─────────────────────────────
            foreach (var (n, sx, sz, half, tall) in turrets)
                U3Turret(root, "Yagura" + n, sx * (U3Wall + 0.07f - half), sz * (U3Wall + 0.07f - half), half, tall);

            // ── Keep: the tenshu ────────────────────────────────────────
            var keep = Group(root, "Keep");
            var keepCaps = Group(root, "KeepCaps");
            var cloth = Group(root, "Cloth");
            UK(keep, "Base_Keep_52x44", "KeepBase", new Vector3(0f, U3Court, U3KeepZ));
            float y = U3Court + 1.0f;
            (float w, float d, float h)[] tiers = { (4.6f, 3.8f, 2.1f), (3.8f, 3.0f, 1.9f), (3.0f, 2.4f, 1.75f), (2.2f, 1.8f, 1.6f) };
            string[] skirts = { "Kawara_Skirt_A", "Kawara_Skirt_B", "Kawara_Skirt_C" };
            string[] tops = { null, "Kawara_Top_A", "Kawara_Top_B", "Kawara_Top_C" };
            int count = level + 1;
            for (int t = 0; t < count; t++)
            {
                var (w, d, h) = tiers[t];
                U3Tier(keep, "Tier" + Letter(t), 0f, U3KeepZ, w, d, y, h, t == 0);
                y += h;
                if (t < count - 1) UK(keepCaps, skirts[t], "TierKawara" + Letter(t), new Vector3(0f, y, U3KeepZ));
                else
                {
                    UK(keepCaps, tops[t], "KeepTop", new Vector3(0f, y - 0.04f, U3KeepZ));
                    if (level >= 3)      // gilt fins on the capital's highest ridge only
                    {
                        UK(keepCaps, "Ridge_Ornament", "KeepFinE", new Vector3(0.6f, y + 1.27f, U3KeepZ), 0f, Vector3.one * 1.25f);
                        UK(keepCaps, "Ridge_Ornament", "KeepFinW", new Vector3(-0.6f, y + 1.27f, U3KeepZ), 180f, Vector3.one * 1.25f);
                    }
                }
            }
            UK(keep, "Door_Single_22", "KeepDoor", new Vector3(0f, U3Court + 1.0f, U3KeepZ - tiers[0].d * 0.5f - 0.03f));
            for (int k = 0; k < 3; k++)      // marble steps up the keep base
                UK(keep, "Marble_Entablature_25", "KeepStep" + Letter(k), new Vector3(0f, U3Court + 0.02f + 0.32f * k, U3KeepZ - 2.2f - 0.62f + 0.3f * k), 0f, new Vector3(0.56f, 1.05f, 1.6f));
            float kFront = U3KeepZ - tiers[0].d * 0.5f - 0.09f;
            Hang(cloth, "KeepBannerW", new Vector3(-1.5f, U3Court + 3.0f, kFront), 0f, 0.26f);
            Hang(cloth, "KeepBannerE", new Vector3(1.5f, U3Court + 3.0f, kFront), 0f, 0.26f);
            if (level >= 2)
                foreach (float sx in new[] { -1f, 1f })
                    UK(keep, "Lantern_Stone", "KeepLantern" + (sx < 0 ? "W" : "E"), new Vector3(sx * 2.75f, U3Court, U3KeepZ - 2.2f - 0.45f));

            // ── Colonnade: the Roman half, along the west wall ──────────
            if (level >= 2)
            {
                var stoa = Group(root, "Colonnade");
                var stoaCap = Group(root, "ColonnadeCap");
                float[] zs = level == 2 ? new[] { -2.9f, -1.7f, -0.5f } : new[] { -2.9f, -1.7f, -0.5f, 0.7f, 1.9f };
                const float cx = -3.3f;
                for (int i = 0; i < zs.Length; i++)
                    UK(stoa, "Marble_Column_22", "Column" + Letter(i), new Vector3(cx, U3Court, zs[i]));
                float z0 = zs[0] - 0.3f, z1 = zs[zs.Length - 1] + 0.3f, zm = (z0 + z1) * 0.5f, len = z1 - z0;
                UK(stoa, "Marble_Entablature_25", "Architrave", new Vector3(cx, U3Court + 2.2f, zm), 270f, new Vector3(len / 2.5f, 1f, 1f));
                float back = -U3Wall + 0.1f;
                string lean = level == 2 ? "Kawara_LeanTo_25" : "Kawara_LeanTo_50";
                float native = level == 2 ? 2.5f : 5.0f;
                UK(stoaCap, lean, "ColonnadeKawara", new Vector3(back, U3Court + 2.5f, zm), 270f, new Vector3((len + 0.3f) / native, 1f, (cx + 0.3f - back) / 1.45f));
            }

            // ── Cloth and standards ─────────────────────────────────────
            Hang(cloth, "GateBannerW", new Vector3(-(U3Notch + 0.75f), top - 0.25f, -U3Wall - 0.10f), 0f, 0.24f);
            Hang(cloth, "GateBannerE", new Vector3(U3Notch + 0.75f, top - 0.25f, -U3Wall - 0.10f), 0f, 0.24f);
            var yard = Group(root, "Yard");
            if (level >= 2)
                foreach (float sx in new[] { -1f, 1f })
                {
                    UK(yard, "Banner_Pole", "BannerPole" + (sx < 0 ? "W" : "E"), new Vector3(sx * 2.6f, U3Court, -2.9f));
                    Hang(cloth, "PoleBanner" + (sx < 0 ? "W" : "E"), new Vector3(sx * 2.6f, U3Court + 3.16f, -2.94f), 0f, 0.22f);
                }
            if (level >= 3) UK(yard, "Standard_Eagle", "Standard", new Vector3(0f, U3Court, -1.7f));

            // ── Court: Synty props at their native (unit) size ──────────
            U3Prop(yard, FK_Brazier, "BrazierW", -1.9f, U3KeepZ - 2.2f - 0.5f, 0f, 1.1f);
            U3Prop(yard, FK_Brazier, "BrazierE", 1.9f, U3KeepZ - 2.2f - 0.5f, 0f, 1.1f);
            U3Prop(yard, PK_Rack, "SpearRack", 3.9f, -1.9f, 90f);
            U3Prop(yard, PK_DummyA, "DummyA", 3.4f, -0.4f, 200f);
            U3Prop(yard, PK_Barrel, "BarrelA", 4.0f, 1.2f, 0f);
            U3Prop(yard, PK_Barrel, "BarrelB", 3.95f, 1.95f, 30f);
            if (level >= 2)
            {
                U3Prop(yard, PK_DummyB, "DummyB", 3.2f, 0.9f, 150f);
                U3Prop(yard, PK_Crate, "Crate", 3.9f, 2.9f, 15f, 0.8f);
                U3Prop(yard, PK_RackLow, "SwordRack", -4.0f, -1.2f, 90f, 0.9f);
            }
            if (level >= 3)
            {
                U3Prop(yard, PK_Armor, "ArmourStand", -4.0f, 0.4f, 90f, 0.9f);
                U3Prop(yard, PK_BarrelArrows, "ArrowBarrel", 3.6f, -2.8f, 0f);
                U3Prop(yard, PK_Sacks, "Sacks", -3.9f, 3.4f, 0f, 0.9f);
            }
        }

        /// <summary>The two south base runs are a full and a half piece: give each its share of the span.</summary>
        static void U3FixRunScale(Transform podium, string tag, float a, float b)
        {
            float len = b - a, k = len / 3.75f;
            var first = podium.Find(tag + "S" + Letter(0)); var second = podium.Find(tag + "S" + Letter(1));
            if (first == null || second == null) return;
            bool firstFull = tag == "BaseL";
            float l0 = (firstFull ? 2.5f : 1.25f) * k, l1 = (firstFull ? 1.25f : 2.5f) * k;
            first.localScale = new Vector3(k, 1f, 1f); second.localScale = new Vector3(k, 1f, 1f);
            first.localPosition = new Vector3(a + l0 * 0.5f, 0f, -U3Edge);
            second.localPosition = new Vector3(a + l0 + l1 * 0.5f, 0f, -U3Edge);
        }

        /// <summary>One storey of thin plaster wall round a w x d rectangle: 1 m modules with windows and slits by turns, timber corner posts.</summary>
        static void U3Tier(Transform parent, string tag, float cx, float cz, float w, float d, float y, float h, bool ground)
        {
            foreach (var (side, yaw, nx, nz) in P2Sides)
            {
                float len = nz != 0f ? w : d, half = nz != 0f ? d * 0.5f : w * 0.5f;
                int n = Mathf.Max(1, Mathf.RoundToInt(len));
                Vector3 along = nz != 0f ? Vector3.right : Vector3.forward;
                for (int i = 0; i < n; i++)
                {
                    bool mid = n % 2 == 1 && i == n / 2;
                    bool alt = i % 2 == (n % 2 == 1 ? 1 : 0);
                    // The ground storey shows only loop slits; windows are for the storeys above.
                    string piece = ground ? (mid || !alt ? "Keep_Wall_10" : "Keep_Wall_10_Slit") : alt ? "Keep_Wall_10_Window" : "Keep_Wall_10";
                    var pos = new Vector3(cx + nx * (half - 0.06f), y, cz + nz * (half - 0.06f)) + along * ((i - (n - 1) * 0.5f) * len / n);
                    UK(parent, piece, $"{tag}{side}{Letter(i)}", pos, yaw + 180f, new Vector3(len / n, h / 2f, 1f));
                }
            }
            foreach (var (n2, sx, sz) in new[] { ("SW", -1f, -1f), ("SE", 1f, -1f), ("NW", -1f, 1f), ("NE", 1f, 1f) })
                UK(parent, "Timber_Post_20", tag + "Post" + n2, new Vector3(cx + sx * (w * 0.5f - 0.05f), y, cz + sz * (d * 0.5f - 0.05f)), 0f, new Vector3(1f, h / 2f, 1f));
        }

        /// <summary>A yagura on the corner (sx, sz): a storey level with the wall, a skirt, a smaller storey, a hip cap.</summary>
        static void U3Turret(Transform root, string tag, float cx, float cz, float half, bool tall)
        {
            var g = Group(root, tag);
            var cap = Group(root, tag + "Cap");
            float s = 2f * half, y = U3Court + U3WallH;
            // The lower storey is compound wall: the same banded plaster, a loop slit a side.
            foreach (var (side, yaw, nx, nz) in P2Sides)
                UK(g, "Plaster_Wall_125_Slit", tag + "Low" + side, new Vector3(cx + nx * (half - 0.07f), U3Court, cz + nz * (half - 0.07f)), yaw + 180f, new Vector3(s / 1.25f, 1f, 1f));
            foreach (var (n2, px, pz) in new[] { ("SW", -1f, -1f), ("SE", 1f, -1f), ("NW", -1f, 1f), ("NE", 1f, 1f) })
                UK(g, "Timber_Post_23", tag + "Post" + n2, new Vector3(cx + px * (half - 0.06f), U3Court, cz + pz * (half - 0.06f)));
            if (!tall)
            {
                // A low guard turret: the hip cap straight on the wall-high storey.
                UK(cap, "Kawara_Cap_26", tag + "Kawara", new Vector3(cx, y - 0.04f, cz), 0f, new Vector3((s + 0.75f) / 2.6f, 0.85f, (s + 0.75f) / 2.6f));
                return;
            }
            UK(cap, "Kawara_Skirt_Turret", tag + "Skirt", new Vector3(cx, y, cz), 0f, new Vector3(0.955f, 1f, 0.955f));
            U3Tier(g, tag + "High", cx, cz, 1.72f, 1.72f, y, 1.9f, false);
            UK(cap, "Kawara_Cap_26", tag + "Kawara", new Vector3(cx, y + 1.9f - 0.04f, cz));
        }

        /// <summary>A Synty prop at its native size standing on the court.</summary>
        static void U3Prop(Transform parent, string piece, string name, float x, float z, float yaw, float mult = 1f)
        {
            var go = Put(parent, piece, name, Vector3.zero, yaw, Vector3.one * mult, Style.Neutral);
            Snap(go, new Vector3(x, U3Court, z), false);
        }

        // ═════════════════════════════════════════════════════════════════
        static void U3Report(int[] planes, float eave, Vector2 tile)
        {
            Report.Add("== Fortress PT3 (unit kit) — metres as drawn (fit 1.0), heights above GROUND");
            for (int l = 1; l <= 3; l++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(P3Path(l));
                if (prefab == null) { Fail("PT3", "missing " + P3Path(l)); continue; }
                float fit = PtFit(prefab, PtFortress.Foot, out var offset);
                var go = UnityEngine.Object.Instantiate(prefab);
                go.hideFlags = HideFlags.HideAndDontSave; go.transform.localScale = Vector3.one; go.transform.position = Vector3.zero;
                try
                {
                    PtBounds(go.transform, out var all);
                    PtBounds(go.transform.Find("Footprint"), out var slab);
                    PtBounds(go.transform.Find("Podium"), out var podium);
                    PtBounds(go.transform.Find("WallCoping"), out var coping);
                    PtBounds(go.transform.Find("Keep"), out var keep);
                    PtBounds(go.transform.Find("KeepCaps"), out var keepCaps);
                    float ground = offset.y;
                    float leaf = 0f;
                    foreach (var r in go.GetComponentsInChildren<Renderer>(false)) if (r.name == "GateLeaf") leaf = r.bounds.size.y;
                    float over = Mathf.Max(Mathf.Max(slab.min.x - all.min.x, all.max.x - slab.max.x), Mathf.Max(slab.min.z - all.min.z, all.max.z - slab.max.z));
                    float slabBeyond = Mathf.Max(slab.size.x - podium.size.x, slab.size.z - podium.size.z) * 0.5f;
                    // Open court: floor inside the wall line not under the keep base, a turret, the stair notch or the colonnade.
                    var solid = new List<Bounds>();
                    foreach (Transform t in go.transform)
                        if (t.name.StartsWith("Yagura") && !t.name.EndsWith("Cap") && PtBounds(t, out var tb)) solid.Add(tb);
                    var kb = go.transform.Find("Keep/KeepBase"); if (kb != null && PtBounds(kb, out var kbb)) solid.Add(kbb);
                    if (PtBounds(go.transform.Find("Colonnade"), out var cb)) solid.Add(cb);
                    if (PtBounds(go.transform.Find("Podium/Steps"), out var sb)) solid.Add(sb);
                    int open = 0, total = 0;
                    for (float x = slab.min.x + 0.05f; x < slab.max.x; x += 0.1f)
                        for (float z = slab.min.z + 0.05f; z < slab.max.z; z += 0.1f)
                        {
                            total++;
                            if (Mathf.Abs(x) > U3Wall - 0.07f || Mathf.Abs(z) > U3Wall - 0.07f) continue;
                            bool blocked = false;
                            foreach (var b in solid) if (x >= b.min.x && x <= b.max.x && z >= b.min.z && z <= b.max.z) { blocked = true; break; }
                            if (!blocked) open++;
                        }
                    float wallH = (coping.max.y - ground) * fit, skirt = (ground - podium.min.y) * fit;
                    float bulk = (Mathf.Max(keep.max.y, keepCaps.max.y) - ground) * fit, tip = (all.max.y - ground) * fit, openPct = 100f * open / Mathf.Max(1, total);
                    float wallInside = (coping.max.y - U3Court) * fit;
                    Report.Add($"Fortress L{l} PT3 | fit {fit:F2} | {all.size.x * fit:F2} x {all.size.z * fit:F2} | leaf {leaf * fit:F2} | compound wall {wallH:F2} above ground ({wallInside:F2} above the court) | skirt {skirt:F2} below ground"
                             + $" | slab beyond the wall base {Mathf.Max(0f, slabBeyond):F2} | court {openPct:F0} % open | eave edge {eave * 100f:F1} cm | tile {tile.x * 100f:F0} x {tile.y * 100f:F0} cm | roof planes {planes[l]} (copings not counted)"
                             + $" | bulk {bulk:F2}, tip {tip:F2} | overhang {Mathf.Max(0f, over) * fit:F2}");
                    if (leaf * fit < 2.0f || leaf * fit > 2.4f) Fail("PT3", $"L{l} leaf {leaf * fit:F2} not in 2.0-2.4");
                    if (wallH < 3.5f || wallH > 4.5f) Fail("PT3", $"L{l} wall {wallH:F2} not in 3.5-4.5");
                    if (skirt < 1f) Fail("PT3", $"L{l} skirt {skirt:F2} < 1");
                    if (slabBeyond > 0.02f) Fail("PT3", $"L{l} slab beyond the walls {slabBeyond:F2}");
                    if (openPct < 20f) Fail("PT3", $"L{l} court {openPct:F0} % < 20");
                    if (eave > 0.08f) Fail("PT3", "eave edge > 8 cm");
                    if (tile.x > 0.25f || tile.y > 0.25f) Fail("PT3", "tiles > 25 cm");
                    if (planes[l] < (l == 1 ? 6 : l == 3 ? 14 : 8)) Fail("PT3", $"L{l} roof planes {planes[l]} (copings not counted)");
                    if (bulk > 11.5f) Fail("PT3", $"L{l} bulk {bulk:F2} > 11.5");
                    if (tip > 13.5f) Fail("PT3", $"L{l} tip {tip:F2} > 13.5");
                    if (over > 0.02f) Fail("PT3", $"L{l} overhang {over:F2}");
                    if (Mathf.Abs(fit - 1f) > 0.01f) Fail("PT3", $"L{l} fit {fit:F3} is not 1.0");
                }
                finally { UnityEngine.Object.DestroyImmediate(go); }
            }
        }

        // ═════════════════════════════════════════════════════════════════
        [MenuItem("Waning Border/Art/Proportion Test/Render Fortress PT3")]
        public static void RenderFortressUnitKit()
        {
            Directory.CreateDirectory(PtOut);
            GameObject L(string p) => AssetDatabase.LoadAssetAtPath<GameObject>(p);
            var atGate = new[] { new Vector3(-0.8f, 0f, -5.9f), new Vector3(0.9f, 0f, -6.1f) };
            var onSteps = new[] { new Vector3(-0.55f, 0f, -5.7f), new Vector3(0.75f, 0f, -6.0f) };
            var red = new Color(0.80f, 0.14f, 0.12f); var blue = new Color(0.16f, 0.36f, 0.85f);
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                _p2Tint = red;
                P2Shot(new[] { (L(P3Path(1)), onSteps) }, 64, 64, null, 45f, 40f, null, 0f);      // warm-up
                var four = new[] { (L(PtFortress.OldPath(3)), atGate), (L(PtFortress.NewPath(3)), atGate), (L(P2Path(3)), atGate), (L(P3Path(3)), onSteps) };
                P2Shot(four, 3400, 1200, $"{PtOut}/Fortress_four_way_L3.png", 45f, 42f, null, 0f);
                P2Shot(four, 3400, 1200, $"{PtOut}/Fortress_four_way_L3_low.png", 45f, 15f, null, 0f);
                var ladder = new[] { (L(P3Path(1)), onSteps), (L(P3Path(2)), onSteps), (L(P3Path(3)), onSteps) };
                P2Shot(ladder, 2800, 1200, $"{PtOut}/Fortress_PT3_ladder.png", 45f, 42f, null, 0f);
                P2Shot(ladder, 2800, 1200, $"{PtOut}/Fortress_PT3_ladder_low.png", 45f, 15f, null, 0f);
                P2Shot(ladder, 2800, 1200, $"{PtOut}/Fortress_PT3_ladder_back.png", 225f, 35f, null, 0f);
                var one = new[] { (L(P3Path(3)), onSteps) };
                P2Shot(one, 2000, 1200, $"{PtOut}/gate_PT3.png", 24f, 10f, new Vector3(0f, 2.4f, -4.6f), 15f);
                P2Shot(one, 2000, 1200, $"{PtOut}/court_PT3.png", 45f, 48f, new Vector3(0f, 3.5f, 0f), 34f);
                P2Shot(one, 2000, 1200, $"{PtOut}/Fortress_PT3_L3_Red.png", 45f, 40f, new Vector3(0f, 4.5f, 0f), 36f);
                _p2Tint = blue;
                P2Shot(one, 2000, 1200, $"{PtOut}/Fortress_PT3_L3_Blue.png", 45f, 40f, new Vector3(0f, 4.5f, 0f), 36f);
                _p2Tint = red;
                // Copper against veilstone (the game's glowing cyan): the mine is drawn large here, for colour only.
                var veil = L(TT + "Age0/Buildings/VeilstoneMine/VeilstoneMine.prefab");
                if (veil != null) P2Shot(new[] { (L(P3Path(3)), onSteps), (veil, new Vector3[0]) }, 2600, 1200, $"{PtOut}/copper_vs_veilstone.png", 45f, 40f, null, 0f);
                U3KitSheet($"{PtOut}/kit_sheet.png");
            }
            finally { ShaderUtil.allowAsyncCompilation = async; _p2Tint = red; }
            Debug.Log("[ProportionTest] PT3 renders -> " + PtOut);
        }

        /// <summary>Every unit-kit piece laid out in rows, a Spearman for scale.</summary>
        static void U3KitSheet(string outPng)
        {
            var manifest = UnitKitBinder.ReadManifest(out _, out _);
            var holder = new GameObject("KitSheet") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                float x = -19f, z = 9f, rowDepth = 0f;
                foreach (var name in manifest.Keys.Where(k => k != "UK_Ground_Patch_10" && k != "UK_Court_Floor_90"))
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UnitKitBinder.PiecePath(name));
                    if (prefab == null) continue;
                    var go = UnityEngine.Object.Instantiate(prefab, holder.transform);
                    MeshBounds(go, out var b);
                    float w = b.size.x + 0.7f;
                    if (x + w > 19f) { x = -19f; z -= rowDepth + 0.9f; rowDepth = 0f; }
                    go.transform.position += new Vector3(x + w * 0.5f - b.center.x, Mathf.Max(0f, -b.min.y), z - b.size.z * 0.5f - b.center.z);
                    rowDepth = Mathf.Max(rowDepth, b.size.z);
                    x += w;
                }
                string unitPath = SoPrefabPath(PtSpearmanSo);
                var up = unitPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(unitPath) : null;
                if (up != null) { var u = UnityEngine.Object.Instantiate(up, holder.transform); u.transform.position = new Vector3(-20f, 0f, 9.5f); }
                P2ShotObject(holder, 3400, 1900, outPng, 25f, 40f, new Vector3(-1f, 0.5f, 2.5f), 46f);
            }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
        }
    }
}
