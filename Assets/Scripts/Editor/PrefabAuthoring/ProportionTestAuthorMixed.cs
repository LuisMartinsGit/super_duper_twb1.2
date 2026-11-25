// ProportionTestAuthorMixed — the Fortress by the MIXED-SCALE recipe
// (2026-11-11, "Redo it"; reference: Sperasoft's AoE4 Ottoman wonder).
// The 0.80-everywhere test (ProportionTestAuthor, Fortress_PT) was wrong in
// two ways: big detail all the way up, so it needed 15 m of bulk to look
// grand, and a 3 m gate leaf under full-scale walls rising straight up.
// PT2 mixes scales inside one building:
//   1. RING   — one low storey at unit scale round the edge (0.80 drawn):
//               a 4-4.5 m wall with small head-height windows and slits.
//   2. PORTAL — a soldier-sized door leaf inside an oversized frame: the
//               kit's gate arch with its doors and portcullis switched off,
//               a gabled porch over it, the real door wall set back inside.
//   3. CORE   — a MINIATURE keep set back against the rear wall, built from
//               the same pieces at 0.60 / 0.55 / 0.50 of the ring (0.48 /
//               0.44 / 0.40 drawn), stepped in three tiers, with MANY small
//               repeats: rows of little windows, merlons, slender turrets.
//   4. HEIGHT from thin elements: turrets, pinnacles, one thin spire; the
//               bulk stops near 10.5 m, the spire tip under 14 m.
//   5. HOLLOW — an open court between ring and core, unit-scale props in it.
//   6. SKIRT  — the ring wall continues 1.3 m below the floor all round (a
//               group OUTSIDE "Footprint", so the footprint fit — which
//               stands the model on the Footprint group's lowest point —
//               leaves it underground): the building sits on slopes.
//   7. COLOUR — player colour on the many small roofs (Castle_Roof_Player,
//               as the existing Alanthor art), a thin tiled coping band on
//               the ring, and banners.
// Prefabs: Fortress/ProportionTest/Fortress_PT2_Lvl1..3.prefab, beside the PT
// (0.80) and the hand-authored ones; nothing else is written.
//
//   Waning Border > Art > Proportion Test > Author Fortress PT2 (mixed scale)
//   Waning Border > Art > Proportion Test > Render Fortress Three-Way
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
        const string PtSwordsmanSo = TT + "Civs/Alanthor/Buildings/Garrison/Units/Swordsman/Swordsman.asset";
        const string PK_DoorWall = "Castle/K_SM_Bld_Castle_Wall_Door_01";
        const string PK_DoorLeaf = "Castle/K_SM_Bld_Castle_Door_Single_01";
        const string PK_Coping = "Castle/K_SM_Bld_Castle_Battlements_02_Half";
        const string PK_Knob = "Castle/K_SM_Bld_Castle_Battlements_Pillar_03";

        /// <summary>Ring wall centre line, kit metres from the plinth centre.</summary>
        const float P2Ring = 5.9f;
        /// <summary>The core's tier scales relative to the ring's pieces.</summary>
        static readonly float[] P2Tier = { 0.60f, 0.55f, 0.50f };
        const float P2CoreZ = 2.55f;      // the core's centre: against the rear wall
        const float P2SkirtDepth = 1.6f;  // below the floor (the plinth is 0.28 thick)

        static string P2Path(int level) => PtFortress.Dir + $"ProportionTest/Fortress_PT2_Lvl{level}.prefab";

        [MenuItem("Waning Border/Art/Proportion Test/Author Fortress PT2 (mixed scale)")]
        public static void AuthorFortressMixed()
        {
            Report.Clear(); _failures = 0;
            try
            {
                _fkRoof = LoadMat(AlanthorArtDir + "Castle_Roof_Player.mat");
                _fkFlag = LoadMat(AlanthorArtDir + "Flag_Symbol_Player.mat");
                _fkWhite = LoadMat(AlanthorArtDir + "Castle_Wall_White.mat");
                _fkGold = LoadMat(AlanthorArtDir + "Castle_Wall_golden.mat");
                _pt = PtFortress;
                for (int l = 1; l <= 3; l++)
                {
                    var root = new GameObject($"Fortress_PT2_Lvl{l}");
                    try
                    {
                        PtPlinth(root.transform);
                        P2Build(root.transform, l);
                        for (int c = 0; c < root.transform.childCount; c++)      // the game's front is the model's +Z
                            root.transform.GetChild(c).localRotation = Quaternion.Euler(0f, 180f, 0f);
                        PtCheckInside(root.name, root.transform);
                    }
                    catch (Exception) { UnityEngine.Object.DestroyImmediate(root); throw; }
                    Save(root, P2Path(l));
                    Report.Add($"OK Fortress_PT2_Lvl{l} -> {P2Path(l)}");
                }
                AssetDatabase.SaveAssets();
                P2Report();
                RenderFortressThreeWay();
            }
            catch (Exception e) { Fail("FortressPT2", e.ToString()); }
            string text = $"[ProportionTest] PT2 done — {Report.Count} entries, {_failures} failure(s)\n" + string.Join("\n", Report);
            Debug.Log(text);
            try { Directory.CreateDirectory(PtOut); File.WriteAllText(PtOut + "/report_pt2.txt", text); } catch (Exception) { }
        }

        // ═════════════════════════════════════════════════════════════════
        // Kit metres; the plinth is x, z in [-6.25, 6.25]; composed with the
        // front toward -Z (turned on save).
        // ═════════════════════════════════════════════════════════════════
        static void P2Build(Transform root, int level)
        {
            Material wall = level >= 2 ? _fkWhite : null;
            var cloth = Group(root, "Cloth");

            // ── Skirt: the ring continues below the floor ───────────────
            var skirt = Group(root, "Skirt");
            foreach (var (side, yaw, nx, nz) in P2Sides)
            {
                var s = Put(skirt, FK_Wall, "Skirt" + side, Vector3.zero, yaw, new Vector3((2f * P2Ring + 0.6f) / 5f, P2SkirtDepth / 5f, 1.2f), Style.Neutral);
                Snap(s, new Vector3(nx * P2Ring, 0.02f, nz * P2Ring), true);
            }

            // ── Ring: one storey at unit scale ──────────────────────────
            var ring = Group(root, "Ring");
            const float portalHalf = 2.84f;       // the gate arch is 5.67 wide
            float ringTop = 0f;
            foreach (var (side, yaw, nx, nz) in P2Sides)
            {
                Vector3 along = nz != 0f ? Vector3.right : Vector3.forward;
                Vector3 mid = new Vector3(nx * P2Ring, 0f, nz * P2Ring);
                if (side == "S")
                {
                    // Either side of the portal: one small-window bay.
                    float len = P2Ring - portalHalf;
                    foreach (float sx in new[] { -1f, 1f })
                    {
                        ringTop = PtWall(ring, "RingWinS" + (sx < 0 ? "a" : "b"), PK_WindowSmall, mid + along * (sx * (portalHalf + len * 0.5f)), yaw, len, wall);
                        P2Coping(ring, "CopingS" + (sx < 0 ? "a" : "b"), mid + along * (sx * (portalHalf + len * 0.5f)), yaw, len, ringTop);
                    }
                    continue;
                }
                // Four bays a side, windows and slits by turns: small, at head height.
                float bay = 2f * P2Ring / 4f;
                for (int i = 0; i < 4; i++)
                {
                    bool win = i % 2 == (side == "N" ? 1 : 0);
                    ringTop = PtWall(ring, (win ? "RingWin" : "RingSlit") + side + Letter(i), win ? PK_WindowSmall : PK_Slit, mid + along * ((i - 1.5f) * bay), yaw, bay, wall);
                }
                P2Coping(ring, "Coping" + side, mid, yaw, 2f * P2Ring, ringTop);
            }

            // Corners: piers at L1, slender turrets from L2.
            var corners = Group(root, "RingTurrets");
            foreach (var (n, sx, sz) in new[] { ("SW", -1f, -1f), ("SE", 1f, -1f), ("NW", -1f, 1f), ("NE", 1f, 1f) })
            {
                float x = sx * (P2Ring - 0.25f), z = sz * (P2Ring - 0.25f);
                if (level == 1)
                {
                    PtPillar(corners, "Pier" + n, PK_PillarSquare, x, z, -0.2f, ringTop + 0.75f, 0.85f, null);
                    P2Cone(corners, "PierCap" + n, x, z, ringTop + 0.55f, 1.1f, 1.1f);
                }
                else P2Turret(corners, "RingTurret" + n, x, z, -0.2f, level == 2 ? 7.4f : 8.8f, 0.85f, level == 2 ? 2.4f : 3.0f, wall);
            }

            // ── Portal: a soldier's door inside an oversized arch ───────
            var portal = Group(root, "Portal");
            var arch = Put(portal, PK_Gate, "PortalArch", Vector3.zero, 180f, Vector3.one, Style.Neutral);
            foreach (var t in arch.GetComponentsInChildren<Transform>(true))
                if (t != arch.transform && (t.name.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0 || t.name.IndexOf("Portcullis", StringComparison.OrdinalIgnoreCase) >= 0))
                    t.gameObject.SetActive(false);                       // the arch only: no giant leaves
            float archTop = Snap(arch, new Vector3(0f, -0.2f, -5.6f), false); Restyle(arch, wall);
            const float recess = 1.25f, k = 1.15f;                       // the door wall stands back; x1.15 = a 2.16 m leaf as drawn
            float doorZ = -P2Ring + recess;
            var doorWall = Put(portal, PK_DoorWall, "PortalDoorWall", Vector3.zero, 180f, Vector3.one * k, Style.Neutral);
            Snap(doorWall, new Vector3(0f, 0f, doorZ), false); Restyle(doorWall, wall);
            var leaf = Put(portal, PK_DoorLeaf, "GateLeaf", Vector3.zero, 180f, Vector3.one * k, Style.Neutral);
            Snap(leaf, new Vector3(0f, 0.1f, doorZ), false);
            foreach (float sx in new[] { -1f, 1f })
            {
                PtWall(portal, "PortalCheek" + (sx < 0 ? "W" : "E"), PK_Bay, new Vector3(sx * 2.6f, 0f, -P2Ring + recess * 0.5f), sx < 0 ? 270f : 90f, recess, wall);
                PtLamp(portal, "PortalLamp" + (sx < 0 ? "W" : "E"), new Vector3(sx * 1.25f, 2.3f, doorZ - 0.45f));
            }
            // The porch: a steep little gable over the recess, ridge front to back.
            var porch = Group(root, "PortalCap");
            float porchTop = PtGable(porch, "Porch", 0f, -6.1f, doorZ + 0.45f, archTop - 0.35f, 2.3f, 3.15f, wall);
            P2Cone(porch, "PorchFinial", 0f, -P2Ring + 0.2f, porchTop - 0.15f, 0.5f, 1.3f);
            Hang(cloth, "PortalBannerW", new Vector3(-(portalHalf + 1.45f), 4.4f, -P2Ring - 0.3f), 0f, 0.36f);
            Hang(cloth, "PortalBannerE", new Vector3(portalHalf + 1.45f, 4.4f, -P2Ring - 0.3f), 0f, 0.36f);

            // ── Core: the miniature keep, three tiers ───────────────────
            var core = Group(root, "Core");
            var coreCaps = Group(root, "CoreCaps");
            float s1 = P2Tier[0], s2 = P2Tier[1], s3 = P2Tier[2];
            // Tier 1: 3 x 2 modules, two storeys of small windows.
            float y1 = P2Tier1(core, "TierA", 0f, P2CoreZ, 3, 2, s1, 0f, new[] { "Win", "Win" }, wall, true);
            P2Merlons(core, "TierACrown", 0f, P2CoreZ, 3, 2, s1, y1, wall);
            float hx1 = 1.5f * 5f * s1, hz1 = 1f * 5f * s1;
            // Tier 2: 2 x 1 modules.
            string[] c2 = level == 1 ? new[] { "Win" } : new[] { "Win", "Low" };
            float y2 = P2Tier1(core, "TierB", 0f, P2CoreZ, 2, 1, s2, y1, c2, wall, false);
            float hx2 = 1f * 5f * s2, hz2 = 0.5f * 5f * s2;
            float bulkTop;
            if (level == 1)
            {
                // A modest keep: a hipped cap on the second tier, two turrets on the first.
                bulkTop = y2;
                P2Merlons(core, "TierBCrown", 0f, P2CoreZ, 2, 1, s2, y2, wall);
                foreach (float sx in new[] { -1f, 1f })
                    PtCap(coreCaps, "KeepCap" + (sx < 0 ? "W" : "E") + "Spire", sx * hx2 * 0.5f, P2CoreZ, y2, 2f * hz2 - 0.7f, 2.4f);
                foreach (float sx in new[] { -1f, 1f })
                    P2Turret(coreCaps, "CoreTurretA" + (sx < 0 ? "W" : "E"), sx * hx1, P2CoreZ - hz1, 0f, y1 + 1.6f, 0.6f, 1.6f, wall);
            }
            else
            {
                P2Merlons(core, "TierBCrown", 0f, P2CoreZ, 2, 1, s2, y2, wall);
                // Tier 3: one module, the top tower.
                float y3 = P2Tier1(core, "TierC", 0f, P2CoreZ, 1, 1, s3, y2, level == 2 ? new[] { "Low" } : new[] { "Win" }, wall, false);
                if (level >= 3) P2Band(core, "TierCBand", 0f, P2CoreZ, 2.5f * s3, y3, s3);
                P2Merlons(core, "TierCCrown", 0f, P2CoreZ, 1, 1, s3, y3, wall);
                bulkTop = y3 + 1.38f * s3;
                P2Cone(coreCaps, "KeepSpire", 0f, P2CoreZ, y3, level == 2 ? 1.7f : 1.5f, level == 2 ? 3.2f : 17.1f - y3);
                // Slender turrets on every tier corner: the repeats that read as size.
                foreach (var (n, sx, sz) in new[] { ("SW", -1f, -1f), ("SE", 1f, -1f), ("NW", -1f, 1f), ("NE", 1f, 1f) })
                {
                    P2Turret(coreCaps, "CoreTurretA" + n, sx * hx1, P2CoreZ + sz * hz1, 0f, y1 + 1.7f, 0.6f, 1.7f, wall);
                    P2Turret(coreCaps, "CoreTurretB" + n, sx * hx2, P2CoreZ + sz * hz2, y1, y2 - y1 + 1.4f, 0.5f, 1.5f, wall);
                    if (level >= 3) P2Turret(coreCaps, "CorePinnacle" + n, sx * 2.5f * s3, P2CoreZ + sz * 2.5f * s3, y2, y3 - y2 + 1.3f, 0.32f, 1.3f, wall);
                }
                // Small roofs on the first tier's shoulders, either side of the second tier.
                foreach (float sx in new[] { -1f, 1f })
                    PtCap(coreCaps, "ShoulderCap" + (sx < 0 ? "W" : "E"), sx * (hx2 + (hx1 - hx2) * 0.5f), P2CoreZ, y1, (hx1 - hx2) - 0.5f, 1.5f);
                Hang(cloth, "CoreBannerW", new Vector3(-hx2 * 0.5f, y2 - 0.3f, P2CoreZ - hz2 - 0.2f), 0f, 0.3f);
                Hang(cloth, "CoreBannerE", new Vector3(hx2 * 0.5f, y2 - 0.3f, P2CoreZ - hz2 - 0.2f), 0f, 0.3f);
                if (level >= 3)
                {
                    Hang(cloth, "CoreBannerA", new Vector3(-hx1 + 1.5f, y1 - 0.2f, P2CoreZ - hz1 - 0.2f), 0f, 0.3f);
                    Hang(cloth, "CoreBannerB", new Vector3(hx1 - 1.5f, y1 - 0.2f, P2CoreZ - hz1 - 0.2f), 0f, 0.3f);
                    PtMast(cloth, "MastW", new Vector3(-hx1 + 0.9f, y1, P2CoreZ), 2.6f, 0.36f);
                }
            }
            Report.Add($"Fortress_PT2_Lvl{level}: ring top {ringTop + 0.44f:F2}, tiers {y1:F2} / {y2:F2}, bulk top {bulkTop:F2} (kit m)");

            // ── Court: unit-scale props, in view between the portal and the core ──
            var yard = Group(root, "Yard");
            float coreFront = P2CoreZ - hz1;
            PtProp(yard, FK_Brazier, "BrazierW", -1.6f, coreFront - 0.9f, 0f, 1.3f);
            PtProp(yard, FK_Brazier, "BrazierE", 1.6f, coreFront - 0.9f, 0f, 1.3f);
            PtProp(yard, PK_Rack, "SpearRack", -4.6f, -1.0f, 90f);
            PtProp(yard, PK_DummyA, "DummyA", -3.9f, -3.4f, 150f);
            PtProp(yard, PK_Barrel, "BarrelA", 4.9f, -0.4f, 0f);
            PtProp(yard, PK_Barrel, "BarrelB", 4.85f, -1.3f, 30f);
            PtProp(yard, PK_Crate, "Crate", 4.7f, -2.4f, 15f, 0.8f);
            if (level >= 2)
            {
                PtProp(yard, PK_DummyB, "DummyB", -2.9f, -2.5f, 40f);
                PtProp(yard, PK_BarrelArrows, "ArrowBarrel", -4.9f, -4.6f, 0f);
                PtProp(yard, PK_Sacks, "Sacks", 4.6f, -4.5f, 0f, 0.9f);
            }
            if (level >= 3)
            {
                var sp = PtProp(yard, PK_StatuePlinth, "StatuePlinth", 3.2f, -3.4f, 180f, 0.6f, -0.1f);
                MeshBounds(sp, out var spb);
                PtProp(yard, PK_Statue, "Statue", 3.2f, -3.4f, 180f, 0.75f, spb.max.y);
                PtProp(yard, PK_Armor, "ArmourStand", -4.8f, 0.6f, 90f, 0.9f);
            }
        }

        static readonly (string side, float yaw, float nx, float nz)[] P2Sides =
            { ("S", 180f, 0f, -1f), ("N", 0f, 0f, 1f), ("E", 90f, 1f, 0f), ("W", 270f, -1f, 0f) };

        /// <summary>The thin player-colour band: a tiled coping along a ring wall top.</summary>
        static void P2Coping(Transform parent, string tag, Vector3 mid, float yaw, float len, float y)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 2.5f));
            Vector3 along = Mathf.Abs(Mathf.Cos(yaw * Mathf.Deg2Rad)) > 0.5f ? Vector3.right : Vector3.forward;
            for (int i = 0; i < n; i++)
            {
                var c = Put(parent, PK_Coping, tag + Letter(i), Vector3.zero, yaw, new Vector3(len / n / 2.5f, 1f, 0.8f), Style.Neutral);
                Snap(c, new Vector3(mid.x, y, mid.z) + along * ((i - (n - 1) * 0.5f) * len / n), false);
                foreach (var r in c.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int m = 0; m < mats.Length; m++) mats[m] = _fkRoof;
                    r.sharedMaterials = mats;
                }
            }
        }

        /// <summary>One tier of the core: nx x nz modules of 5 m pieces at the
        /// uniform scale <paramref name="s"/>, one course per entry (Win: a
        /// window or a slit by turns; Low: the half-height course). The pieces
        /// are scaled WHOLE, never stretched. Returns the top.</summary>
        static float P2Tier1(Transform parent, string tag, float cx, float cz, int nx, int nz, float s, float y0, string[] courses, Material mat, bool door)
        {
            float m = 5f * s, hx = nx * m * 0.5f, hz = nz * m * 0.5f, y = y0;
            for (int c = 0; c < courses.Length; c++)
            {
                float top = y;
                foreach (var (side, yaw, ox, oz) in P2Sides)
                {
                    int n = oz != 0f ? nx : nz;
                    Vector3 along = oz != 0f ? Vector3.right : Vector3.forward;
                    Vector3 mid = new Vector3(cx + ox * (hx - 0.25f * s), y, cz + oz * (hz - 0.25f * s));
                    for (int i = 0; i < n; i++)
                    {
                        bool low = courses[c] == "Low";
                        bool isDoor = door && c == 0 && side == "S" && i == n / 2;
                        bool win = (i + c) % 2 == 0;
                        string piece = low ? PK_Low : isDoor ? PK_DoorBig : win ? PK_Window : PK_Slit;
                        string name = (low ? "CoreLow" : isDoor ? "CoreDoor" : win ? "CoreWin" : "CoreSlit") + tag + side + Letter(c) + Letter(i);
                        var go = Put(parent, piece, name, Vector3.zero, yaw, Vector3.one * s, Style.Neutral);
                        top = Mathf.Max(top, Snap(go, mid + along * ((i - (n - 1) * 0.5f) * m), false));
                        Restyle(go, mat);
                    }
                }
                y = top;
            }
            var deck = Put(parent, FK_Floor, tag + "Deck", Vector3.zero, 0f, new Vector3((2f * hx - 0.2f) / 5f, 1f, (2f * hz - 0.2f) / 5f), Style.Neutral);
            Snap(deck, new Vector3(cx, y + 0.02f, cz), true); Restyle(deck, mat);
            return y;
        }

        /// <summary>Small merlons round a tier top, one piece per module at the tier's scale.</summary>
        static void P2Merlons(Transform parent, string tag, float cx, float cz, int nx, int nz, float s, float y, Material mat)
        {
            float m = 5f * s, hx = nx * m * 0.5f, hz = nz * m * 0.5f;
            foreach (var (side, yaw, ox, oz) in P2Sides)
            {
                int n = oz != 0f ? nx : nz;
                Vector3 along = oz != 0f ? Vector3.right : Vector3.forward;
                Vector3 mid = new Vector3(cx + ox * (hx - 0.25f * s), y, cz + oz * (hz - 0.25f * s));
                for (int i = 0; i < n; i++)
                {
                    var go = Put(parent, FK_Batt, tag + side + Letter(i), Vector3.zero, yaw, Vector3.one * s, Style.Neutral);
                    Snap(go, mid + along * ((i - (n - 1) * 0.5f) * m), false); Restyle(go, mat);
                }
            }
        }

        /// <summary>A gilded corbel band under a tier's crown.</summary>
        static void P2Band(Transform parent, string tag, float cx, float cz, float half, float y, float s)
        {
            foreach (var (side, yaw, ox, oz) in P2Sides)
            {
                var go = Put(parent, FK_Corbels, tag + side, Vector3.zero, yaw, Vector3.one * s, Style.Neutral);
                Snap(go, new Vector3(cx + ox * (half - 0.1f * s), y, cz + oz * (half - 0.1f * s)), true); Restyle(go, _fkGold);
            }
        }

        /// <summary>A slender round turret: a shaft, a collar knob and a thin cone.</summary>
        static void P2Turret(Transform parent, string tag, float x, float z, float y0, float height, float dia, float cone, Material mat)
        {
            PtPillar(parent, tag + "Shaft", PK_PillarRound, x, z, y0, height, dia, mat);
            var knob = Put(parent, PK_Knob, tag + "Collar", Vector3.zero, 0f, Vector3.one * (dia * 1.25f / 1.36f), Style.Neutral);
            Snap(knob, new Vector3(x, y0 + height - 0.25f * dia, z), true); Restyle(knob, mat);
            P2Cone(parent, tag + "Cone", x, z, y0 + height - 0.05f, dia * 1.35f, cone);
        }

        /// <summary>A thin player-colour cone (the kit's round cap).</summary>
        static void P2Cone(Transform parent, string name, float x, float z, float y, float dia, float height)
        {
            var go = Put(parent, FK_Cone, name, Vector3.zero, 0f, new Vector3(dia / 2.94f, height / 9.41f, dia / 2.94f), Style.Neutral);
            Snap(go, new Vector3(x, y, z), false); Restyle(go, null);
        }

        // ═════════════════════════════════════════════════════════════════
        // Acceptance: PT2 beside PT (0.80) and the hand-authored Fortress.
        // ═════════════════════════════════════════════════════════════════
        static void P2Report()
        {
            float foot = PtFortress.Foot;
            Report.Add("== Fortress: old | PT (0.80) | PT2 (mixed) — drawn metres on the 10 m footprint, heights above GROUND");
            for (int l = 1; l <= 3; l++)
            {
                foreach (var (label, path) in new[] { ("old", PtFortress.OldPath(l)), ("PT ", PtFortress.NewPath(l)), ("PT2", P2Path(l)) })
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) { Fail("report", "missing " + path); continue; }
                    float fit = PtFit(prefab, foot, out var offset);
                    var go = UnityEngine.Object.Instantiate(prefab);
                    go.hideFlags = HideFlags.HideAndDontSave; go.transform.localScale = Vector3.one; go.transform.position = Vector3.zero;
                    try
                    {
                        PtBounds(go.transform, out var all);
                        if (!PtBounds(go.transform.Find("Footprint"), out var slab)) slab = all;
                        float ground = offset.y;                                 // the fit stands this height on the terrain
                        float over = Mathf.Max(Mathf.Max(slab.min.x - all.min.x, all.max.x - slab.max.x), Mathf.Max(slab.min.z - all.min.z, all.max.z - slab.max.z));
                        float leaf = 0f;
                        foreach (var r in go.GetComponentsInChildren<Renderer>(false))
                        {
                            bool isLeaf = label == "PT2" ? r.name == "GateLeaf"
                                : r.name.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0 && r.transform.parent != null && r.transform.parent != go.transform && r.transform.parent.GetComponent<Renderer>() != null;
                            if (isLeaf) leaf = Mathf.Max(leaf, r.bounds.size.y);
                        }
                        string line = $"Fortress L{l} {label} | fit {fit:F2} | {all.size.x * fit:F2} x {all.size.z * fit:F2} | top {(all.max.y - ground) * fit:F2} | leaf {leaf * fit:F2} | overhang {Mathf.Max(0f, over) * fit:F2}";
                        if (label == "PT2")
                        {
                            PtBounds(go.transform.Find("Ring"), out var ring);
                            PtBounds(go.transform.Find("Portal"), out var portal);
                            PtBounds(go.transform.Find("PortalCap"), out var porch);
                            PtBounds(go.transform.Find("Core"), out var core);
                            PtBounds(go.transform.Find("Skirt"), out var skirt);
                            var coreT = go.transform.Find("Core"); var capsT = go.transform.Find("CoreCaps");
                            int windows = coreT.GetComponentsInChildren<Transform>(true).Count(t => t.parent == coreT && (t.name.StartsWith("CoreWin") || t.name.StartsWith("CoreSlit")));
                            int merlons = coreT.GetComponentsInChildren<Transform>(true).Count(t => t.parent == coreT && t.name.Contains("Crown"));
                            int roofs = capsT.GetComponentsInChildren<Transform>(true).Count(t => t.parent == capsT && (t.name.EndsWith("Cone") || t.name.EndsWith("Spire") || t.name.StartsWith("ShoulderCap")));
                            // Open court: floor inside the ring not under the core, the portal or a corner turret.
                            var solid = new List<Bounds> { core, portal };
                            if (PtBounds(go.transform.Find("RingTurrets"), out _))
                                foreach (Transform t in go.transform.Find("RingTurrets")) if (PtBounds(t, out var tb)) solid.Add(tb);
                            float inner = P2Ring - 0.25f; int open = 0, total = 0;
                            for (float x = slab.min.x + 0.05f; x < slab.max.x; x += 0.1f)
                                for (float z = slab.min.z + 0.05f; z < slab.max.z; z += 0.1f)
                                {
                                    total++;
                                    if (Mathf.Abs(x) > inner || Mathf.Abs(z) > inner) continue;
                                    bool blocked = false;
                                    foreach (var b in solid) if (x >= b.min.x && x <= b.max.x && z >= b.min.z && z <= b.max.z) { blocked = true; break; }
                                    if (!blocked) open++;
                                }
                            float wallH = (ring.max.y - ground) * fit, portalH = (porch.max.y - ground) * fit, archH = (portal.max.y - ground) * fit;
                            float bulk = (core.max.y - ground) * fit, tip = (all.max.y - ground) * fit, skirtDepth = (ground - skirt.min.y) * fit;
                            float openPct = 100f * open / Mathf.Max(1, total);
                            line += $" | ring wall {wallH:F2} | arch {archH:F2}, porch {portalH:F2} = {portalH / Mathf.Max(0.01f, leaf * fit):F1} x leaf | core scale {P2Tier[0] * fit:F2}/{P2Tier[1] * fit:F2}/{P2Tier[2] * fit:F2} vs ring {fit:F2}"
                                  + $" | bulk {bulk:F2}, tip {tip:F2} | core windows+slits {windows}, small roofs {roofs}, merlon runs {merlons} | court {openPct:F0} % open | skirt {skirtDepth:F2} below ground";
                            if (wallH < 4f || wallH > 5f) Fail("PT2", $"L{l} ring wall {wallH:F2} m not in 4-5");
                            if (leaf * fit < 2.0f || leaf * fit > 2.4f) Fail("PT2", $"L{l} leaf {leaf * fit:F2} m not in 2.0-2.4");
                            if (portalH < 1.6f * leaf * fit) Fail("PT2", $"L{l} portal {portalH:F2} < 1.6 x leaf");
                            if (bulk > 11f) Fail("PT2", $"L{l} bulk {bulk:F2} m > 11");
                            if (tip > 14f) Fail("PT2", $"L{l} tip {tip:F2} m > 14");
                            if (l == 3 && windows + roofs < 30) Fail("PT2", $"L3 repeats {windows + roofs} < 30");
                            if (openPct < 20f) Fail("PT2", $"L{l} court {openPct:F0} % < 20");
                            if (skirtDepth < 1f) Fail("PT2", $"L{l} skirt {skirtDepth:F2} m < 1");
                            if (over > 0.02f) Fail("PT2", $"L{l} overhang {over:F2}");
                        }
                        Report.Add(line);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(go); }
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // Renders: old | PT | PT2 as the game draws them, soldiers at the gate.
        // ═════════════════════════════════════════════════════════════════
        [MenuItem("Waning Border/Art/Proportion Test/Render Fortress Three-Way")]
        public static void RenderFortressThreeWay()
        {
            Directory.CreateDirectory(PtOut);
            GameObject L(string p) => AssetDatabase.LoadAssetAtPath<GameObject>(p);
            // Drawn metres from the pad centre, in front of each gate (the front is world -Z).
            var atGate = new[] { new Vector3(-0.8f, 0f, -5.9f), new Vector3(0.9f, 0f, -6.1f) };
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                P2Shot(new[] { (L(P2Path(1)), atGate) }, 64, 64, null, 45f, 40f, null, 0f);      // warm-up
                for (int l = 1; l <= 3; l++)
                {
                    var rows = new[] { (L(PtFortress.OldPath(l)), atGate), (L(PtFortress.NewPath(l)), atGate), (L(P2Path(l)), atGate) };
                    P2Shot(rows, 2800, 1200, $"{PtOut}/Fortress_three_way_L{l}.png", 45f, 42f, null, 0f);
                    P2Shot(rows, 2800, 1200, $"{PtOut}/Fortress_three_way_L{l}_low.png", 45f, 15f, null, 0f);
                }
                var ladder = new[] { (L(P2Path(1)), atGate), (L(P2Path(2)), atGate), (L(P2Path(3)), atGate) };
                P2Shot(ladder, 2800, 1200, $"{PtOut}/Fortress_PT2_ladder.png", 45f, 42f, null, 0f);
                P2Shot(ladder, 2800, 1200, $"{PtOut}/Fortress_PT2_ladder_low.png", 45f, 15f, null, 0f);
                P2Shot(ladder, 2800, 1200, $"{PtOut}/Fortress_PT2_ladder_back.png", 225f, 35f, null, 0f);
                var one = new[] { (L(P2Path(3)), atGate) };
                P2Shot(one, 2000, 1200, $"{PtOut}/gate_PT2.png", 22f, 9f, new Vector3(0f, 2.6f, -5.2f), 17f);
                P2Shot(one, 2000, 1200, $"{PtOut}/court_PT2.png", 45f, 50f, new Vector3(0f, 3f, 0f), 34f);
            }
            finally { ShaderUtil.allowAsyncCompilation = async; }
            Debug.Log("[ProportionTest] three-way renders -> " + PtOut);
        }

        /// <summary>Rows across the view at the game's fit and half turn, on
        /// 10 m pads; a Spearman / Swordsman at each row's spots. An explicit
        /// focus (from the stage centre) and distance frame a close shot.</summary>
        /// <summary>The faction colour the shots paint the buildings.</summary>
        static Color _p2Tint = new Color(0.80f, 0.14f, 0.12f);

        /// <summary>A scene object (already built, at 1 : 1) shot the same way: moved to the stage, lit, rendered.</summary>
        static void P2ShotObject(GameObject obj, int w, int h, string outPng, float yaw, float pitch, Vector3 focusAt, float distance)
        {
            _p2Extra = obj;
            try { P2Shot(new (GameObject, Vector3[])[0], w, h, outPng, yaw, pitch, focusAt, distance); }
            finally { _p2Extra = null; }
        }
        static GameObject _p2Extra;

        static void P2Shot((GameObject prefab, Vector3[] spots)[] rows, int w, int h, string outPng, float yaw, float pitch, Vector3? focusAt, float distance)
        {
            var created = new List<GameObject>();
            var stage = new Vector3(0f, -4200f, 0f);
            float foot = PtFortress.Foot;
            Material Flat(Color c)
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave };
                m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.1f);
                return m;
            }
            try
            {
                Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                const float spacing = 17.5f;
                float maxTop = 0f;
                var half = Quaternion.Euler(0f, 180f, 0f);
                for (int i = 0; i < rows.Length; i++)
                {
                    var (prefab, spots) = rows[i];
                    if (prefab == null) continue;
                    Vector3 at = stage + right * ((i - (rows.Length - 1) * 0.5f) * spacing);
                    float scale = PtFit(prefab, foot, out var offset);
                    var go = UnityEngine.Object.Instantiate(prefab);
                    go.hideFlags = HideFlags.HideAndDontSave; created.Add(go);
                    go.transform.localScale = Vector3.one * scale;
                    go.transform.rotation = half;
                    go.transform.position = at - half * (offset * scale);
                    TheWaningBorder.Rendering.BuildingFactionColorMarker.Apply(go, _p2Tint);
                    if (MeshBounds(go, out var gb)) maxTop = Mathf.Max(maxTop, gb.max.y - stage.y);
                    var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    pad.hideFlags = HideFlags.HideAndDontSave; created.Add(pad);
                    pad.transform.position = at + new Vector3(0f, -0.03f, 0f);
                    pad.transform.localScale = new Vector3(foot, 0.06f, foot);
                    pad.GetComponent<Renderer>().sharedMaterial = Flat(new Color(0.14f, 0.15f, 0.12f));
                    for (int s = 0; s < spots.Length; s++)
                    {
                        string unitPath = SoPrefabPath(s % 2 == 0 ? PtSpearmanSo : PtSwordsmanSo);
                        var up = unitPath != null ? AssetDatabase.LoadAssetAtPath<GameObject>(unitPath) : null;
                        if (up == null) continue;
                        var u = UnityEngine.Object.Instantiate(up);
                        u.hideFlags = HideFlags.HideAndDontSave; created.Add(u);
                        u.transform.position = at + spots[s];
                        u.transform.rotation = Quaternion.Euler(0f, 180f + (s % 2 == 0 ? -25f : 30f), 0f);
                    }
                }
                if (_p2Extra != null) _p2Extra.transform.position = stage;
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
                float aspect = (float)w / h;
                float distW = Mathf.Max(1, rows.Length) * spacing * 0.5f / (Mathf.Tan(15f * Mathf.Deg2Rad) * aspect);
                float distH = (maxTop + 10f) * 0.5f / Mathf.Tan(15f * Mathf.Deg2Rad);
                float dist = distance > 0f ? distance : Mathf.Max(distW, distH) * 1.08f;
                var look = Quaternion.Euler(pitch, yaw, 0f);
                var focus = stage + (focusAt ?? new Vector3(0f, maxTop * 0.42f, 0f));
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
    }
}
