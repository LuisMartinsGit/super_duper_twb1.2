// FeraldisIdentities.cs — the third part of FeraldisKitAuthor (2026-10-13):
// ONE IDENTITY PER FERALDIS BUILDING, readable by silhouette + roof alone.
//
// The first pass gave nearly every building the same template (half-timber
// hall, brown plank Castle_Roof, antler gables) and same-footprint buildings
// were indistinguishable from the RTS camera. Each building now owns a roof
// TYPE and VALUE and a shape no other Feraldis building uses:
//
//   Fortress        stone KEEP, dark shingle PYRAMID roof, log towers, stake ring
//   Longhouse       low boat-hull hall under a sweeping green TURF roof, antler
//                   + dragon-prow gables (the only antler hall)
//   Great Forge     no pitched roof — stone furnace CHIMNEYS (ember vents,
//                   smoke, fire) over flat IRON canopies on posts
//   Hall of Axes    open shooting yard: plank lean-to GALLERY, target butts,
//                   racks, a Synty watchtower
//   Rig Yard        skeletal: HIDE awnings, a timber CRANE, half-built rig frames
//   Engine Works    the tallest skeleton: a timber GANTRY crane and a treadwheel
//                   over the siege engine
//   Slave Pens      no house: a stake STOCKADE with cages and a guard hut
//   Work Camp       HIDE TENT camp, drying rack, lean-to, log piles
//   Mine            headframe + adit (kept), plank cap at Lv2
//   Blood Altar     stepped STONE platform in a ring of standing stones
//   War Totem       a CLUSTER of three skull-studded poles
//   Hunting Lodge   a great hide TENT with meat racks and a spit
//   Logging Station open SAWPIT with a crane and log stacks
//   Fiendstone Keep black STONE-AND-IRON citadel: dark castle-kit curtain and
//                   round towers, furnace spire with fire, ember and rune glow
//
// Roof materials across the set: turf (mid green), dark shingle, iron (dark
// metal), hide (tan), mid planks, stone, none. The level rules stand: every
// level keeps the same ground slab and grows its BODY, and the identity holds
// across levels. The old template builders in the other two files were
// replaced by these.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static partial class FeraldisKitAuthor
    {
        const string C_Batt = "Castle/SM_Bld_Castle_Battlements_01";
        const string C_Wall1 = "Castle/SM_Bld_Castle_Wall_01";
        const string Bld_TentOpen = "Buildings/SM_Bld_Tent_Open_01";
        const string Bld_TentSmall = "Buildings/SM_Bld_Tent_Small_01";
        const string P_Tether = "Props/SM_Prop_Tether_Beam_01";
        const string P_WindmillBig = "Props/SM_Prop_Windmill_03";
        const string P_WindmillSmall = "Props/SM_Prop_Windmill_01";
        const string P_Bath = "Props/Furniture/SM_Prop_Bath_01";
        const string B_DummyArchery = "Props/BattleGround/SM_Prop_Dummy_Archery_01";
        const string FX_Fire = "FX/FX_Fire_01";

        static Material _ferHide, _ferIron, _ferTurf, _ferRune, _ferHideDark, _ferCanvas;

        static void EnsureIdentityMaterials()
        {
            EnsureExtraMaterials();
            _ferHide = AssetDatabase.LoadAssetAtPath<Material>(FerMatDir + "Fer_Hide.mat");
            _ferIron = AssetDatabase.LoadAssetAtPath<Material>(FerMatDir + "Fer_Iron.mat");
            _ferTurf = AssetDatabase.LoadAssetAtPath<Material>(FerMatDir + "Fer_Turf.mat");
            _ferRune = AssetDatabase.LoadAssetAtPath<Material>(FerMatDir + "Fer_Rune.mat");
            if (_ferHide == null || _ferIron == null || _ferTurf == null || _ferRune == null)
                throw new Exception("Fer_Hide / Fer_Iron / Fer_Turf / Fer_Rune palette materials missing");
            _ferHideDark = Tinted("Fer_HideDark", _ferHide, new Color(0.30f, 0.21f, 0.15f));
            _ferCanvas = Tinted("Fer_Canvas", _ferHide, new Color(0.58f, 0.56f, 0.49f));
        }

        /// <summary>A palette material derived from <paramref name="from"/> with another base colour, created beside it once.</summary>
        static Material Tinted(string name, Material from, Color color)
        {
            string path = FerMatDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(from); AssetDatabase.CreateAsset(m, path); }
            m.shader = from.shader;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ═════════════════════════════════════════════════════════════════
        // Identity helpers
        // ═════════════════════════════════════════════════════════════════

        /// <summary>A flat canopy (plank deck, or iron sheeting with <paramref name="mat"/>) on four square posts.</summary>
        static void Canopy(Transform lv, string tag, float cx, float cz, float hx, float hz, float baseY, float h, Material mat = null)
        {
            foreach (var (px, pz) in new[] { (-hx + 0.3f, -hz + 0.3f), (hx - 0.3f, -hz + 0.3f), (hx - 0.3f, hz - 0.3f), (-hx + 0.3f, hz - 0.3f) })
                Put(lv, C_PillarSq, tag + "Post", new Vector3(cx + px, baseY, cz + pz), 0f, new Vector3(0.45f, h / 5.05f, 0.45f));
            Put(lv, C_FloorWood, tag + "Sheet", new Vector3(cx + hx, baseY + h, cz + hz), 0f, new Vector3(2f * hx / 5f, 1f, 2f * hz / 5f), Style.Feraldis, null, mat);
        }

        /// <summary>A plank LEAN-TO on posts: the deck tilted down toward the open side (+Z when yaw 0).</summary>
        static void LeanTo(Transform lv, string tag, float cx, float cz, float hx, float hz, float baseY, float hHigh, float yaw = 0f)
        {
            float hLow = hHigh - 2f * hz * 0.35f;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            foreach (var (px, pz, h) in new[] { (-hx + 0.3f, -hz + 0.3f, hHigh), (hx - 0.3f, -hz + 0.3f, hHigh), (hx - 0.3f, hz - 0.3f, hLow), (-hx + 0.3f, hz - 0.3f, hLow) })
                Put(lv, C_PillarSq, tag + "Post", new Vector3(cx, baseY, cz) + rot * new Vector3(px, 0f, pz), yaw, new Vector3(0.4f, h / 5.05f, 0.4f));
            float tilt = Mathf.Atan2(hHigh - hLow, 2f * hz) * Mathf.Rad2Deg;
            // Deck pivot at its +X +Z corner (the low side); tilt about X so the far -Z end rises.
            Put(lv, C_FloorWood, tag + "Deck", new Vector3(cx, baseY, cz) + rot * new Vector3(hx, hLow + 0.1f, hz), yaw, new Vector3(2f * hx / 5f, 1f, 2.1f * hz / 5f), Style.Feraldis, new Vector3(tilt, 0f, 0f));
        }

        /// <summary>A timber crane: a post with a tilted boom, pulley block and hanging chain.</summary>
        static void Crane(Transform lv, string tag, Vector3 foot, float h, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Put(lv, C_PillarSq, tag + "Post", foot, yaw, new Vector3(0.5f, h / 5.05f, 0.5f));
            Put(lv, C_PanelWood, tag + "Boom", foot + new Vector3(0f, h - 0.3f, 0f) + rot * new Vector3(0.2f, 0f, 0.15f), yaw, new Vector3(0.9f, 0.45f, 0.9f), Style.Feraldis, new Vector3(0f, 0f, -28f));
            Put(lv, P_Tether, tag + "Pulley", foot + new Vector3(0f, h + 1.6f, 0f) + rot * new Vector3(-3.6f, 0f, 0f), yaw + 90f, Vector3.one * 1.3f);
            Put(lv, P_ChainDrop, tag + "Chain", foot + new Vector3(0f, h + 1.4f, 0f) + rot * new Vector3(-3.6f, 0f, 0.3f), yaw, Vector3.one * 1.2f);
        }

        /// <summary>A hide tent (the kit's open pavilion in the palette's hide).</summary>
        static void HideTent(Transform lv, string tag, Vector3 pos, float yaw, float scale, bool big = false, Material hide = null)
            => Put(lv, big ? Bld_TentSmall : Bld_TentOpen, tag, pos, yaw, Vector3.one * scale, Style.Feraldis, null, hide ?? _ferHide);

        /// <summary>A hide awning on two poles.</summary>
        static void HideAwning(Transform lv, string tag, Vector3 pos, float yaw, float scale)
            => Put(lv, P_Awning, tag, pos, yaw, Vector3.one * scale, Style.Feraldis, null, _ferCanvas);

        /// <summary>A stake stockade round (cx,cz) with half size <paramref name="half"/>: 2.5 m stake
        /// pieces scaled to the side, open on the south when <paramref name="gate"/>.</summary>
        static void Stockade(Transform lv, string tag, float cx, float cz, float half, float height, bool gate)
        {
            float side = 2f * half;
            int n = Mathf.Max(1, Mathf.RoundToInt(side / 2.25f));
            float len = side / n;
            // Spike_Wall_01 runs +X 2.5 m from its pivot; yaw -90 runs +Z, 180 runs -X, 90 runs -Z.
            for (int i = 0; i < n; i++)
            {
                float t = i * len;
                Put(lv, B_SpikeWall1, tag + "W", new Vector3(cx - half, 0.15f, cz - half + t), -90f, new Vector3(len / 2.5f, height, 1.1f));
                Put(lv, B_SpikeWall1, tag + "E", new Vector3(cx + half, 0.15f, cz - half + t), -90f, new Vector3(len / 2.5f, height, 1.1f));
                Put(lv, B_SpikeWall1, tag + "N", new Vector3(cx - half + t, 0.15f, cz + half), 0f, new Vector3(len / 2.5f, height, 1.1f));
                if (!gate || (i != n / 2 && !(n == 1)))
                    Put(lv, B_SpikeWall1, tag + "S", new Vector3(cx - half + t, 0.15f, cz - half), 0f, new Vector3(len / 2.5f, height, 1.1f));
            }
        }

        /// <summary>A stone furnace chimney: a dark stack with ember vents, a fire crown and smoke.</summary>
        static void Chimney(Transform lv, string tag, Vector3 foot, float w, float h, bool fire = true)
        {
            Put(lv, C_Block, tag + "Stack", foot, 0f, new Vector3(w / 5.5f, h / 5f, w / 5.5f));
            for (int side = 0; side < 4; side++)
            {
                var rot = Quaternion.Euler(0f, side * 90f, 0f);
                Put(lv, C_Block, tag + "Vent", foot + new Vector3(0f, h * 0.35f, 0f) + rot * new Vector3(0f, 0f, w * 0.5f + 0.02f), side * 90f,
                    new Vector3(w * 0.22f / 5.5f, h * 0.3f / 5f, 0.1f / 5.5f), Style.Feraldis, null, _ferEmber);
            }
            Put(lv, FX_Smoke, tag + "Smoke", foot + new Vector3(0f, h + 0.4f, 0f), 0f, Vector3.one * (0.5f + w * 0.2f));
            if (fire) Put(lv, FX_Fire, tag + "Fire", foot + new Vector3(0f, h - 0.1f, 0f), 0f, Vector3.one * (0.4f + w * 0.25f));
        }

        /// <summary>A rack of posts and a beam with pelts / meat hung from it.</summary>
        static void DryingRack(Transform lv, string tag, Vector3 a, Vector3 b, float h, bool meat)
        {
            Vector3 d = b - a; float len = d.magnitude; float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            Put(lv, C_PillarSq, tag + "PostA", a, 0f, new Vector3(0.3f, h / 5.05f, 0.3f));
            Put(lv, C_PillarSq, tag + "PostB", b, 0f, new Vector3(0.3f, h / 5.05f, 0.3f));
            Put(lv, C_PanelWood, tag + "Beam", b + new Vector3(0f, h - 0.4f, 0f), yaw - 90f, new Vector3(len / 5f, 0.4f, 1f));
            int n = Mathf.Max(1, Mathf.RoundToInt(len / 1.2f));
            for (int i = 0; i < n; i++)
            {
                var p = Vector3.Lerp(a, b, (i + 0.5f) / n) + new Vector3(0f, h - 0.5f, 0f);
                if (meat) Put(lv, i % 2 == 0 ? P_MeatHanger : P_PigHanging, tag + "Hang", p, yaw, Vector3.one * 0.9f);
                else Put(lv, P_Pelt, tag + "Pelt", p, yaw, Vector3.one * 2.6f, Style.Feraldis, new Vector3(90f, 0f, 0f));
            }
        }

        // ═════════════════════════════════════════════════════════════════
        // The identities
        // ═════════════════════════════════════════════════════════════════

        /// <summary>FORTRESS: a stone keep under a dark shingle pyramid, log towers, stake ring (11 m plinth).</summary>
        static void BuildFeraldisFortress(Transform lv, int level)
        {
            const float P = 0.6f;
            Put(lv, C_Block, "Plinth", Vector3.zero, 0f, new Vector3(11f / 5.5f, P / 5f, 11f / 5.5f));
            float kw = level == 1 ? 6f : level == 2 ? 6.5f : 7f;
            float kh = level == 1 ? 4.5f : level == 2 ? 7f : 9f;
            // The keep: a dark stone block, battlemented at 2+, a set-back upper storey at 3.
            Put(lv, C_Block, "Keep", new Vector3(0f, P, 0.6f), 0f, new Vector3(kw / 5.5f, kh / 5f, kw / 5.5f));
            Put(lv, C_DoorSingle, "KeepDoor", new Vector3(0.9f, P + 0.1f, 0.6f - kw * 0.5f - 0.12f), 0f, new Vector3(1.8f, 1.5f, 1f));
            float roofBase = P + kh;
            if (level >= 2)
            {
                float e = kw * 0.5f - 0.2f;
                foreach (var (a, b) in new[] { (new Vector3(-e, roofBase, 0.6f - e), new Vector3(e, roofBase, 0.6f - e)), (new Vector3(e, roofBase, 0.6f - e), new Vector3(e, roofBase, 0.6f + e)),
                                               (new Vector3(e, roofBase, 0.6f + e), new Vector3(-e, roofBase, 0.6f + e)), (new Vector3(-e, roofBase, 0.6f + e), new Vector3(-e, roofBase, 0.6f - e)) })
                    Span(lv, C_Batt, "KeepBatt", a, b, 5f);
            }
            if (level >= 3)
            {
                Put(lv, C_Block, "KeepUpper", new Vector3(0f, roofBase, 0.6f), 0f, new Vector3(5f / 5.5f, 3f / 5f, 5f / 5.5f));
                roofBase += 3f;
                float rw = 5f;
                Put(lv, C_CapSquare, "KeepPyramid", new Vector3(0f, roofBase - 0.1f, 0.6f), 0f, new Vector3(rw * 1.08f / 2.74f, 4.5f / 7.42f, rw * 1.08f / 2.74f));
            }
            else
                Put(lv, C_CapSquare, "KeepPyramid", new Vector3(0f, roofBase - 0.1f, 0.6f), 0f, new Vector3(kw * 1.08f / 2.74f, (level == 1 ? 3.2f : 4f) / 7.42f, kw * 1.08f / 2.74f));
            // Stake ring along the plinth edge, open at the south gate.
            foreach (var (pos, yaw, len) in new[] {
                (new Vector3(-5.0f, P, -5.0f), -90f, 1f), (new Vector3(-5.0f, P, 0f), -90f, 1f),
                (new Vector3(5.0f, P, -5.0f), -90f, 1f), (new Vector3(5.0f, P, 0f), -90f, 1f),
                (new Vector3(-5.0f, P, 5.0f), 0f, 1f), (new Vector3(0f, P, 5.0f), 0f, 1f),
                (new Vector3(-5.0f, P, -5.0f), 0f, 0.55f), (new Vector3(2.25f, P, -5.0f), 0f, 0.55f) })
                Put(lv, B_SpikeWall2, "Ring", pos, yaw, new Vector3(len, 0.85f, 1.2f));
            // Log towers: two at the front, four from level 2, bigger at 3.
            int storeys = level == 1 ? 2 : 3;
            float th = level == 3 ? 1.25f : 1.05f;
            LogTower(lv, "TowerSW", -4.1f, -4.1f, th, P, storeys, spire: false);
            LogTower(lv, "TowerSE", 4.1f, -4.1f, th, P, storeys, spire: false);
            if (level >= 2) { LogTower(lv, "TowerNW", -4.1f, 4.1f, th, P, storeys, spire: false); LogTower(lv, "TowerNE", 4.1f, 4.1f, th, P, storeys, spire: false); }
            // The gate front.
            Put(lv, B_Brazier, "BrazierW", new Vector3(-1.9f, P, -4.2f), 0f, Vector3.one * 1.9f);
            Put(lv, B_Brazier, "BrazierE", new Vector3(1.9f, P, -4.2f), 0f, Vector3.one * 1.9f);
            Banner(lv, "PlayerColor_BannerW", new Vector3(-2.6f, P, 0.6f - kw * 0.5f - 0.5f), 0f, 1.7f, _ferCloth);
            Banner(lv, "PlayerColor_BannerE", new Vector3(2.6f, P, 0.6f - kw * 0.5f - 0.5f), 0f, 1.7f, _ferCloth);
            Put(lv, BN_FlagH, "PlayerColor_KeepCloth", new Vector3(1.4f, P + kh * 0.7f, 0.6f - kw * 0.5f - 0.3f), 0f, new Vector3(0.6f, 0.5f, 0.6f), Style.Neutral, null, _ferCloth);
            Put(lv, P_Torch, "TorchW", new Vector3(-1.6f, P + 2.2f, 0.6f - kw * 0.5f - 0.15f), 0f, Vector3.one * 1.3f);
            Put(lv, P_Torch, "TorchE", new Vector3(1.6f, P + 2.2f, 0.6f - kw * 0.5f - 0.15f), 0f, Vector3.one * 1.3f);
            Put(lv, P_DeerHead, "GateTrophy", new Vector3(0f, P + 3.2f, 0.6f - kw * 0.5f - 0.1f), 0f, Vector3.one * 1.4f);
            Put(lv, P_Skull, "SkullA", new Vector3(-4.8f, P, -4.8f), 30f, Vector3.one * 2.2f);
            Put(lv, P_Skull, "SkullB", new Vector3(4.8f, P, -4.8f), -30f, Vector3.one * 2.2f);
            if (level >= 2)
            {
                Put(lv, P_Chain, "ChainA", new Vector3(-2.0f, P + kh - 0.6f, 0.6f - kw * 0.5f - 0.3f), 0f, Vector3.one * 1.2f);
                Put(lv, P_Chain, "ChainB", new Vector3(2.0f, P + kh - 0.6f, 0.6f - kw * 0.5f - 0.3f), 0f, Vector3.one * 1.2f);
                Put(lv, P_Gallows, "Gallows", new Vector3(-4.6f, P, 0.6f), 90f, Vector3.one * 1.0f);
                Put(lv, P_CageMan, "Cage", new Vector3(-4.6f, P + 0.6f, 1.6f), 0f, Vector3.one * 0.7f);
            }
            if (level >= 3)
            {
                FlagMast(lv, "GreatFlag", new Vector3(-4.6f, P, -2.0f), 13f, 0.9f);
                Put(lv, W_Shield, "ShieldA", new Vector3(-1.2f, P + 5.5f, 0.6f - kw * 0.5f - 0.15f), 0f, Vector3.one * 1.6f);
                Put(lv, W_Shield, "ShieldB", new Vector3(1.2f, P + 5.5f, 0.6f - kw * 0.5f - 0.15f), 0f, Vector3.one * 1.6f);
                Put(lv, E_DeadTree, "DeadTree", new Vector3(4.7f, P, 0.8f), 40f, Vector3.one * 0.6f);
                Put(lv, DB_Pile, "Bodies", new Vector3(4.2f, P, -0.4f), 60f, Vector3.one * 0.55f);
                Put(lv, B_SpikeFort, "FriseW", new Vector3(-3.0f, P + 0.66f, -4.4f), 0f, Vector3.one * 0.9f);
                Put(lv, B_SpikeFort, "FriseE", new Vector3(3.0f, P + 0.66f, -4.4f), 0f, Vector3.one * 0.9f);
            }
        }

        /// <summary>LONGHOUSE: a low boat-hull hall under a sweeping turf roof, antler and dragon-prow gables.</summary>
        static void BuildLonghouse(Transform lv, int level)
        {
            Yard(lv, 8f);
            BoatHall(lv, "Hall", 0.3f, level == 1 ? 0f : -0.9f, level == 1 ? 2.5f : 3.75f, 2.4f, 0.2f);
            float porchX = 0.3f - (level == 1 ? 2.5f : 3.75f);
            Put(lv, B_Brazier, "Brazier", new Vector3(porchX - 0.35f, 0.2f, level == 1 ? 2.3f : 1.6f), 0f, Vector3.one * 1.5f);
            Banner(lv, "PlayerColor_Banner", new Vector3(porchX - 0.1f, 0.2f, (level == 1 ? 0f : -0.9f) - 3.0f), 90f, 1.5f, _ferCloth);
            Put(lv, B_WeaponRack, "RackA", new Vector3(1.8f, 0.2f, (level == 1 ? 0f : -0.9f) - 3.3f), 0f, Vector3.one * 1.0f);
            if (level == 1) return;
            // Lv2: the longer hull and a shield row, racks, a stake line.
            for (int i = 0; i < 3; i++)
                Put(lv, W_Shield, "Shield", new Vector3(-2.2f + i * 2.2f, 1.3f, -0.9f - 2.4f - 0.4f), 0f, Vector3.one * 1.4f);
            Put(lv, B_WeaponRack, "RackB", new Vector3(-1.4f, 0.2f, -0.9f - 3.3f), 0f, Vector3.one * 1.0f);
            Put(lv, B_SpikeWall1, "StakesE", new Vector3(3.6f, 0.2f, -3.6f), -90f, new Vector3(0.9f, 0.7f, 1.2f));
            if (level == 2)
            {
                Put(lv, P_Torch, "TorchA", new Vector3(-3.0f, 1.4f, -0.9f - 2.6f), 0f, Vector3.one * 1.2f);
                return;
            }
            // Lv3: a second hull behind the first (the hall of the war-band), a
            // raised central lantern on the great hall, the mast and skull posts.
            BoatHall(lv, "Annex", 0.6f, 2.9f, 2.5f, 1.3f, 0.2f, raise: 0f);
            Put(lv, C_Block, "Lantern", new Vector3(0.3f, 3.3f, -0.9f), 0f, new Vector3(3f / 5.5f, 1.2f / 5f, 1.4f / 5.5f));
            Put(lv, H_Thatch, "LanternTurfS", new Vector3(-1.2f, 5.0f, -0.9f), -90f, new Vector3(0.5f, 0.4f, 1.2f), Style.Feraldis, null, _ferTurf);
            Put(lv, H_Thatch, "LanternTurfN", new Vector3(1.8f, 5.0f, -0.9f), 90f, new Vector3(0.5f, 0.4f, 1.2f), Style.Feraldis, null, _ferTurf);
            FlagMast(lv, "GreatFlag", new Vector3(-3.7f, 0.2f, 3.7f), 6.5f, 0.7f);
            SkullPost(lv, new Vector3(3.8f, 0.2f, 3.8f));
            Put(lv, P_Cauldron, "Cauldron", new Vector3(3.6f, 0.2f, 1.2f), 0f, Vector3.one * 1.2f);
        }

        /// <summary>
        /// The boat hull: low dark walls (1.5 m), a sweeping turf roof whose
        /// eaves come down to knee height, antler finials and dragon-prow
        /// heads at both gables. hx along X in 2.5 m modules, hz across.
        /// </summary>
        static void BoatHall(Transform lv, string tag, float cx, float cz, float hx, float hz, float baseY, float raise = 0f)
        {
            int nx = Mathf.Max(1, Mathf.RoundToInt(hx * 2f / W)), nz = Mathf.Max(1, Mathf.RoundToInt(hz * 2f / W));
            float ex = nx * W * 0.5f, ez = nz * W * 0.5f;
            const float wallH = 0.6f;    // of 2.5 m
            string[] Row(int n) { var r = new string[n]; for (int i = 0; i < n; i++) r[i] = H_WallBeams; return r; }
            Put(lv, C_Block, tag + "Footing", new Vector3(cx, baseY - 0.2f, cz), 0f, new Vector3((2f * ex + 0.6f) / 5.5f, 0.35f / 5f, (2f * ez + 0.6f) / 5.5f));
            Ring(lv, tag + "W", cx, cz, ex, ez, baseY, Row(nx), Row(nz), Row(nx), Row(nz), Style.Feraldis, wallH);
            Put(lv, C_DoorSingle, tag + "Door", new Vector3(cx - ex - 0.1f, baseY, cz + 0.6f), 90f, new Vector3(1.1f, 0.62f, 1f));
            float top = baseY + W * wallH;
            // Turf: the kit thatch at a shallow pitch — across scaled to reach
            // 0.5 m past the wall, drop scaled so the eave hangs at ~0.6 m.
            float across = ez + 0.7f;
            float ridgeY = top + 1.9f;                    // the rise above the low wall: the roof IS the hall
            float sx = across / 2.56f, sy = (ridgeY - (baseY + 0.5f)) / 2.68f;   // eave hangs at knee height
            for (int i = 0; i < nx; i++)
            {
                float x0 = cx - ex + i * W, x1 = x0 + W;
                Put(lv, H_Thatch, tag + "TurfS", new Vector3(x0, ridgeY, cz), -90f, new Vector3(sx, sy, 1f), Style.Feraldis, null, _ferTurf);
                Put(lv, H_Thatch, tag + "TurfN", new Vector3(x1, ridgeY, cz), 90f, new Vector3(sx, sy, 1f), Style.Feraldis, null, _ferTurf);
                Put(lv, H_ThatchCap, tag + "Cap", new Vector3(x1, ridgeY, cz), 90f, Vector3.one, Style.Feraldis, null, _ferTurf);
            }
            Put(lv, C_PanelWood, tag + "Ridge", new Vector3(cx + ex + 0.3f, ridgeY - 0.1f, cz), 0f, new Vector3((2f * ex + 0.6f) / 5f, 0.6f, 0.7f));
            Gable(lv, tag + "GableW", new Vector3(cx - ex, top, cz), ez, ridgeY - top, 90f);
            Gable(lv, tag + "GableE", new Vector3(cx + ex, top, cz), ez, ridgeY - top, -90f);
            // Dragon prows: a deer head projecting from each gable peak, antlers above.
            Put(lv, P_DeerHead, tag + "ProwW", new Vector3(cx - ex - 0.05f, ridgeY - 0.5f, cz), 90f, Vector3.one * 2.0f, Style.Feraldis, new Vector3(-25f, 0f, 0f));
            Put(lv, P_DeerHead, tag + "ProwE", new Vector3(cx + ex + 0.05f, ridgeY - 0.5f, cz), -90f, Vector3.one * 2.0f, Style.Feraldis, new Vector3(-25f, 0f, 0f));
            AntlerFinial(lv, tag + "AntlerW", new Vector3(cx - ex + 1.0f, ridgeY - 0.1f, cz), -90f, 2.2f);
            AntlerFinial(lv, tag + "AntlerE", new Vector3(cx + ex - 1.0f, ridgeY - 0.1f, cz), 90f, 2.2f);
        }

        /// <summary>GREAT FORGE: stone furnace chimneys over flat iron canopies — no pitched roof (10.5 m plinth).</summary>
        static void BuildGreatForge(Transform lv, int level)
        {
            const float P = 0.5f;
            Put(lv, C_Block, "Plinth", Vector3.zero, 0f, new Vector3(10.5f / 5.5f, P / 5f, 10.5f / 5.5f));
            // A low dark stone back wall along the north.
            Span(lv, C_Wall1, "BackWall", new Vector3(-4.6f, P, 4.4f), new Vector3(4.6f, P, 4.4f), 5f, Style.Feraldis, 0.5f);
            // Chimneys: one, two, three — the great one in the middle at 3.
            Chimney(lv, "ChimneyW", new Vector3(-3.3f, P, 2.6f), 2.2f, level == 1 ? 6f : 7f);
            if (level >= 2) Chimney(lv, "ChimneyE", new Vector3(3.3f, P, 2.6f), 2.2f, 7.5f);
            if (level >= 3) Chimney(lv, "ChimneyGreat", new Vector3(0f, P, 2.9f), 3.2f, 11f);
            // Iron canopies on posts over the working floor.
            Canopy(lv, "CanopyW", -2.4f, -1.4f, 2.0f, 1.7f, P, 3.6f, _ferIron);
            if (level >= 2) Canopy(lv, "CanopyE", 2.4f, -1.4f, 2.0f, 1.7f, P, 3.9f, _ferIron);
            Put(lv, P_Forge2, "Hearth", new Vector3(-2.4f, P, -0.4f), 0f, Vector3.one * 1.0f);
            Put(lv, P_Anvil, "AnvilA", new Vector3(-1.2f, P, -2.2f), 30f, Vector3.one * 1.3f);
            Put(lv, P_Bath, "Quench", new Vector3(-3.6f, P, -2.4f), 90f, Vector3.one * 1.1f, Style.Feraldis, null, _ferIron);
            Put(lv, P_Bellows, "Bellows", new Vector3(-4.0f, P, 0.8f), 90f, Vector3.one * 0.55f);
            Put(lv, P_CageMan, "SlaveCage", new Vector3(4.2f, P, 4.0f), 0f, Vector3.one * 0.75f);
            Banner(lv, "PlayerColor_BannerW", new Vector3(-0.6f, P, -4.3f), 0f, 1.5f, _ferCloth);
            Banner(lv, "PlayerColor_BannerE", new Vector3(0.6f, P, -4.3f), 0f, 1.5f, _ferCloth);
            Put(lv, P_Chain, "ChainA", new Vector3(-2.4f, P + 3.3f, -3.2f), 0f, Vector3.one * 1.2f);
            for (int i = 0; i < 5; i++) Put(lv, P_OreIron, "Ore", new Vector3(3.6f + (i % 2) * 0.4f, P, -3.6f + (i % 3) * 0.4f), i * 40f, Vector3.one * 3.5f);
            if (level == 1) return;
            Put(lv, P_Anvil, "AnvilB", new Vector3(2.4f, P, -2.2f), -30f, Vector3.one * 1.3f);
            Put(lv, P_Workbench, "Workbench", new Vector3(3.8f, P, -0.4f), -90f, Vector3.one * 0.85f);
            Put(lv, P_GrindWheel, "Grindstone", new Vector3(1.0f, P, -0.2f), 20f, Vector3.one * 1.2f);
            Put(lv, P_Chain, "ChainB", new Vector3(2.4f, P + 3.6f, -3.2f), 0f, Vector3.one * 1.2f);
            Put(lv, P_Cage2, "CageB", new Vector3(-4.3f, P, 4.2f), 30f, Vector3.one * 1.1f);
            if (level == 2) return;
            Put(lv, P_Chain, "StackChainW", new Vector3(-1.65f, P + 7.2f, 2.7f), 0f, new Vector3(0.55f, 1.2f, 1f));
            Put(lv, P_Chain, "StackChainE", new Vector3(1.65f, P + 7.2f, 2.7f), 0f, new Vector3(0.55f, 1.2f, 1f));
            FlagMast(lv, "GreatFlag", new Vector3(4.4f, P, -4.4f), 10f, 0.8f);
            Put(lv, P_Cauldron, "Cauldron", new Vector3(0f, P, -0.6f), 0f, Vector3.one * 1.3f);
            Put(lv, DB_Pile, "Bodies", new Vector3(4.3f, P, 1.8f), 60f, Vector3.one * 0.5f);
        }

        /// <summary>HALL OF AXES: an open shooting yard — a plank lean-to gallery, target butts, racks, a watchtower.</summary>
        static void BuildHallOfAxes(Transform lv, int level)
        {
            Yard(lv, 8f);
            float glen = level == 1 ? 2.6f : 3.7f;
            LeanTo(lv, "Gallery", -2.9f, 0.2f, glen, 1.5f, 0.2f, 3.8f, yaw: 90f);
            // The butts line: a stake fence along the north edge, the targets in front of it.
            Put(lv, B_SpikeWall1, "ButtsFenceA", new Vector3(-1.0f, 0.2f, 4.0f), 0f, new Vector3(1f, 0.8f, 1.1f));
            Put(lv, B_SpikeWall1, "ButtsFenceB", new Vector3(1.5f, 0.2f, 4.0f), 0f, new Vector3(1f, 0.8f, 1.1f));
            Put(lv, B_WeaponRack, "RackA", new Vector3(-2.9f, 0.2f, -glen + 0.5f), 90f, Vector3.one * 1.0f);
            Put(lv, B_WeaponRack, "RackB", new Vector3(-2.9f, 0.2f, glen - 0.5f), 90f, Vector3.one * 1.0f);
            // Target butts: straw targets on a line at the east.
            int targets = level == 1 ? 3 : 4;
            for (int i = 0; i < targets; i++)
                Put(lv, B_Target, "Target", new Vector3(-1.5f + i * 1.7f, 0.2f, 3.2f), 180f, Vector3.one * 1.9f);
            Put(lv, P_Fence, "Line", new Vector3(2.4f, 0.65f, -1.2f), 0f, new Vector3(1.6f, 0.8f, 1f));
            Put(lv, C_PillarSq, "AxePost", new Vector3(3.6f, 0.2f, 1.6f), 0f, new Vector3(0.45f, 0.75f, 0.45f));
            Put(lv, W_Axe, "PostAxeA", new Vector3(3.3f, 2.6f, 1.6f), 90f, Vector3.one * 1.8f, Style.Feraldis, new Vector3(0f, 0f, -35f));
            Put(lv, W_Axe, "PostAxeB", new Vector3(3.9f, 2.6f, 1.6f), -90f, Vector3.one * 1.8f, Style.Feraldis, new Vector3(0f, 0f, 35f));
            Put(lv, P_Skull, "PostSkull", new Vector3(3.6f, 3.95f, 1.6f), 0f, Vector3.one * 2.4f);
            Put(lv, B_Brazier, "Brazier", new Vector3(-0.6f, 0.2f, -3.7f), 0f, Vector3.one * 1.4f);
            Banner(lv, "PlayerColor_Banner", new Vector3(-0.8f, 0.2f, 3.8f), 0f, 1.5f, _ferCloth);
            Put(lv, W_Axe, "AxeA", new Vector3(-4.0f, 1.0f, 0.2f), 90f, Vector3.one * 1.5f);
            if (level == 1) return;
            Put(lv, B_DummyArchery, "DummyA", new Vector3(1.2f, 0.5f, -3.4f), 20f, Vector3.one * 1.2f);
            Put(lv, B_Dummy, "DummyB", new Vector3(2.6f, 0.2f, -3.4f), -30f, Vector3.one * 1.1f);
            Put(lv, P_Pelt, "Pelt", new Vector3(-3.3f, 2.6f, 0.4f), 90f, Vector3.one * 2.6f, Style.Feraldis, new Vector3(90f, 0f, 0f));
            LogTower(lv, "Watch", 3.3f, -3.3f, 0.9f, 0.2f, level == 2 ? 2 : 3, spire: false);
            if (level == 2) return;
            LeanTo(lv, "GalleryS", 0.0f, -3.4f, 1.8f, 1.0f, 0.2f, 3.2f, yaw: 0f);
            FlagMast(lv, "GreatFlag", new Vector3(-3.7f, 0.2f, -3.7f), 7.5f, 0.7f);
            SkullPost(lv, new Vector3(3.8f, 0.2f, 3.8f));
        }

        /// <summary>RIG YARD: hide awnings, a timber crane, half-built rig frames, a cart — a skeletal yard.</summary>
        static void BuildRigYard(Transform lv, int level)
        {
            Yard(lv, 8f, 0.25f);
            HideAwning(lv, "AwningA", new Vector3(-2.4f, 0.25f, 2.4f), 0f, 1.2f);
            Crane(lv, "Crane", new Vector3(0.8f, 0.25f, 0.9f), level == 1 ? 4.5f : level == 2 ? 5.5f : 6.5f, -45f);
            RigFrame(lv, "FrameA", new Vector3(-0.4f, 0.25f, -1.0f), 1.5f, 1.0f, 2.2f);
            Put(lv, V_Cart, "Cart", new Vector3(-2.6f, 0.25f, -2.4f), 90f, Vector3.one * 0.85f);
            Put(lv, P_WindmillSmall, "Wheel", new Vector3(3.4f, 0.25f, -3.0f), 60f, Vector3.one * 1.6f, Style.Feraldis, new Vector3(0f, 0f, 18f));
            Put(lv, P_BarrelStack, "Barrels", new Vector3(-3.5f, 0.25f, 0.2f), 0f, Vector3.one * 0.6f);
            Banner(lv, "PlayerColor_Banner", new Vector3(-3.7f, 0.25f, -3.7f), 0f, 1.5f, _ferCloth);
            if (level == 1) return;
            HideAwning(lv, "AwningB", new Vector3(2.3f, 0.25f, -2.6f), 180f, 1.1f);
            Put(lv, SE_Ram, "RamRig", new Vector3(2.2f, 0.25f, 0.6f), 90f, Vector3.one * 0.5f);
            RigFrame(lv, "FrameB", new Vector3(-2.2f, 0.25f, -0.2f), 1.2f, 0.8f, 1.8f);
            Put(lv, P_Anvil, "Anvil", new Vector3(0.2f, 0.25f, 2.4f), 30f, Vector3.one * 1.2f);
            if (level == 2) return;
            HideAwning(lv, "AwningC", new Vector3(-2.8f, 0.25f, -0.6f), 90f, 1.1f);
            Put(lv, SE_Ballista, "Ballista", new Vector3(-0.2f, 0.25f, -2.9f), 150f, Vector3.one * 0.55f);
            FlagMast(lv, "GreatFlag", new Vector3(3.8f, 0.25f, 3.8f), 8f, 0.7f);
            SkullPost(lv, new Vector3(-3.7f, 0.25f, 3.7f));
        }

        /// <summary>A half-built rig: four posts and the beams of a box frame.</summary>
        static void RigFrame(Transform lv, string tag, Vector3 at, float hx, float hz, float h)
        {
            foreach (var (px, pz) in new[] { (-hx, -hz), (hx, -hz), (hx, hz), (-hx, hz) })
                Put(lv, C_PillarSq, tag + "Post", at + new Vector3(px, 0f, pz), 0f, new Vector3(0.3f, h / 5.05f, 0.3f));
            Put(lv, C_PanelWood, tag + "BeamS", at + new Vector3(hx, h - 0.4f, -hz), 0f, new Vector3(2f * hx / 5f, 0.35f, 0.6f));
            Put(lv, C_PanelWood, tag + "BeamN", at + new Vector3(-hx, h - 0.4f, hz), 180f, new Vector3(2f * hx / 5f, 0.35f, 0.6f));
            Put(lv, C_PanelWood, tag + "BeamW", at + new Vector3(-hx, h - 0.4f, -hz), 90f, new Vector3(2f * hz / 5f, 0.35f, 0.6f));
            Put(lv, C_PanelWood, tag + "BeamE", at + new Vector3(hx, h - 0.4f, hz), -90f, new Vector3(2f * hz / 5f, 0.35f, 0.6f));
        }

        /// <summary>ENGINE WORKS: a tall timber gantry crane and treadwheel over the siege engine — the tallest skeleton.</summary>
        static void BuildEngineWorks(Transform lv, int level)
        {
            Yard(lv, 8f, 0.25f);
            float gh = level == 1 ? 6f : level == 2 ? 7.5f : 9.5f;
            const float gx = 2.4f, gz = 2.0f;
            foreach (var (px, pz) in new[] { (-gx, -gz), (gx, -gz), (gx, gz), (-gx, gz) })
                Put(lv, C_PillarSq, "GantryPost", new Vector3(px, 0.25f, pz), 0f, new Vector3(0.5f, gh / 5.05f, 0.5f));
            Put(lv, C_PanelWood, "GantryS", new Vector3(gx, 0.25f + gh - 0.5f, -gz), 0f, new Vector3(2f * gx / 5f, 0.5f, 0.8f));
            Put(lv, C_PanelWood, "GantryN", new Vector3(-gx, 0.25f + gh - 0.5f, gz), 180f, new Vector3(2f * gx / 5f, 0.5f, 0.8f));
            Put(lv, C_PanelWood, "GantryW", new Vector3(-gx, 0.25f + gh - 0.5f, -gz), 90f, new Vector3(2f * gz / 5f, 0.5f, 0.8f));
            Put(lv, C_PanelWood, "GantryE", new Vector3(gx, 0.25f + gh - 0.5f, gz), -90f, new Vector3(2f * gz / 5f, 0.5f, 0.8f));
            Put(lv, C_PanelWood, "GantryMid", new Vector3(gx, 0.25f + gh - 0.5f, 0f), 0f, new Vector3(2f * gx / 5f, 0.5f, 0.8f));
            Put(lv, P_Tether, "Pulley", new Vector3(0f, 0.25f + gh - 0.3f, 0.2f), 0f, Vector3.one * 1.4f);
            Put(lv, P_ChainDrop, "Hoist", new Vector3(0.6f, 0.25f + gh - 0.5f, 0.1f), 0f, Vector3.one * (gh / 4f));
            // Mid rails: a second tier of beams half-way up, so it reads as a scaffold.
            float mid = 0.25f + gh * 0.5f;
            Put(lv, C_PanelWood, "RailS", new Vector3(gx, mid, -gz), 0f, new Vector3(2f * gx / 5f, 0.35f, 0.6f));
            Put(lv, C_PanelWood, "RailN", new Vector3(-gx, mid, gz), 180f, new Vector3(2f * gx / 5f, 0.35f, 0.6f));
            Put(lv, C_PanelWood, "RailW", new Vector3(-gx, mid, -gz), 90f, new Vector3(2f * gz / 5f, 0.35f, 0.6f));
            Put(lv, C_PanelWood, "RailE", new Vector3(gx, mid, gz), -90f, new Vector3(2f * gz / 5f, 0.35f, 0.6f));
            Put(lv, SE_Catapult, "Catapult", new Vector3(0f, 0.25f, 0f), 90f, Vector3.one * 0.6f);
            Put(lv, P_CrateStack, "Crates", new Vector3(-3.4f, 0.25f, -3.2f), 40f, Vector3.one * 0.7f);
            Put(lv, P_Anvil, "Anvil", new Vector3(3.3f, 0.25f, -3.0f), 0f, Vector3.one * 1.3f);
            Banner(lv, "PlayerColor_Banner", new Vector3(3.7f, 0.25f, 3.6f), 0f, 1.5f, _ferCloth);
            Put(lv, B_Brazier, "Brazier", new Vector3(-3.6f, 0.25f, 3.4f), 0f, Vector3.one * 1.4f);
            if (level == 1) return;
            // The treadwheel: the kit's great windmill wheel stood on an axle beside the gantry.
            Put(lv, P_WindmillBig, "Treadwheel", new Vector3(3.6f, 0.25f + 3.4f, 0.4f), 90f, Vector3.one * 0.5f);
            Put(lv, C_PillarSq, "WheelPost", new Vector3(3.6f, 0.25f, 0.4f), 0f, new Vector3(0.4f, 3.4f / 5.05f, 0.4f));
            Put(lv, SE_Ballista, "Ballista", new Vector3(-2.0f, 0.25f, -3.2f), -120f, Vector3.one * 0.55f);
            Put(lv, P_Cauldron, "HotOil", new Vector3(-3.5f, 0.25f, 0.2f), 0f, Vector3.one * 1.2f);
            if (level == 2) return;
            Put(lv, P_WindmillBig, "TreadwheelB", new Vector3(-3.6f, 0.25f + 3.4f, 0.8f), 90f, Vector3.one * 0.5f);
            Put(lv, C_PillarSq, "WheelPostB", new Vector3(-3.6f, 0.25f, 0.8f), 0f, new Vector3(0.4f, 3.4f / 5.05f, 0.4f));
            FlagMast(lv, "GreatFlag", new Vector3(-3.8f, 0.25f, -3.8f), 11f, 0.7f);
            SkullPost(lv, new Vector3(3.8f, 0.25f, -3.8f));
            Put(lv, P_Chain, "GantryChain", new Vector3(0f, 0.25f + gh - 1.2f, -gz - 0.3f), 0f, new Vector3(1.6f, 1.3f, 1f));
        }

        /// <summary>SLAVE PENS (4 m): a stake stockade round cages and a guard hut — no house.</summary>
        static void BuildSlavePens(Transform lv, int level)
        {
            Yard(lv, 4f, 0.15f);
            Stockade(lv, "Pen", 0f, 0f, 2.1f, level == 3 ? 0.85f : 0.6f, gate: true);
            Put(lv, P_CageMan, "CageA", new Vector3(-0.9f, 0.15f, 0.8f), 20f, Vector3.one * 0.75f);
            Put(lv, C_PillarSq, "WhipPost", new Vector3(0.4f, 0.15f, 1.2f), 0f, new Vector3(0.25f, 0.5f, 0.25f));
            Put(lv, P_ChainDrop, "WhipChain", new Vector3(0.4f, 2.6f, 1.1f), 0f, Vector3.one * 0.8f);
            Banner(lv, "PlayerColor_Banner", new Vector3(1.6f, 0.15f, -1.7f), 0f, 1.0f, _ferCloth);
            if (level == 1) return;
            Put(lv, P_Cage2, "CageB", new Vector3(1.1f, 0.15f, 0.3f), -30f, Vector3.one * 1.1f);
            LogTower(lv, "GuardHut", 1.3f, 1.3f, 0.55f, 0.15f, 1, spire: false);
            Put(lv, P_Torch, "Torch", new Vector3(1.3f, 1.3f, 0.6f), 0f, Vector3.one * 1.0f);
            if (level == 2) return;
            Put(lv, P_CageMan, "CageC", new Vector3(-1.2f, 0.15f, -0.9f), 60f, Vector3.one * 0.75f);
            Put(lv, P_Gallows, "Gallows", new Vector3(-1.4f, 0.15f, 1.5f), 200f, Vector3.one * 0.6f);
            SkullPost(lv, new Vector3(-1.9f, 0.15f, -1.9f), 2.6f);
            Put(lv, P_Chain, "Chain", new Vector3(0f, 2.2f, -2.1f), 0f, new Vector3(1.4f, 0.8f, 1f));
        }

        /// <summary>MINE (4 m): the stone adit under the timber headframe; a plank cap at Lv2, the headframe tower at Lv3.</summary>
        static void BuildMine(Transform lv, int level)
        {
            Yard(lv, 4f, 0.15f);
            float aw = level == 1 ? 2.4f : 3.2f, ah = level == 1 ? 1.7f : 2.4f, ad = level == 1 ? 1.8f : 2.4f;
            Put(lv, C_Block, "Adit", new Vector3(0f, 0.15f, 0.8f), 0f, new Vector3(aw / 5.5f, ah / 5f, ad / 5.5f));
            Put(lv, C_DoorSingle, "AditMouth", new Vector3(0.75f, 0.2f, 0.8f - ad * 0.5f - 0.1f), 0f, new Vector3(1.45f, (ah - 0.5f) / 2.35f, 1f));
            float hh = level == 1 ? 2.8f : 4.2f;
            Put(lv, C_PillarSq, "HeadPostA", new Vector3(-1.5f, 0.15f, -0.5f), 0f, new Vector3(0.4f, hh / 5.05f, 0.4f));
            Put(lv, C_PillarSq, "HeadPostB", new Vector3(1.5f, 0.15f, -0.5f), 0f, new Vector3(0.4f, hh / 5.05f, 0.4f));
            Put(lv, C_PanelWood, "HeadBeam", new Vector3(1.7f, 0.15f + hh - 0.2f, -0.65f), 0f, new Vector3(0.68f, 0.45f, 1f));
            Put(lv, P_Crate, "Crate", new Vector3(-1.6f, 0.15f, 1.5f), 20f, Vector3.one * 0.7f);
            for (int i = 0; i < 3; i++)
                Put(lv, P_OreIron, "Ore", new Vector3(1.3f + (i % 2) * 0.4f, 0.15f, 1.0f + i * 0.4f), i * 50f, Vector3.one * 4f);
            Put(lv, P_Torch, "Torch", new Vector3(-1.2f, 1.6f, 0.8f - ad * 0.5f - 0.1f), 0f, Vector3.one * 1.1f);
            Banner(lv, "PlayerColor_Banner", new Vector3(-1.8f, 0.15f, -1.7f), 0f, 1.0f, _ferCloth);
            if (level == 1) return;
            Put(lv, P_ChainDrop, "Hoist", new Vector3(0f, 0.15f + hh - 0.2f, -0.5f), 0f, Vector3.one * 1.2f);
            if (level == 2) Put(lv, C_FloorWood, "HeadCap", new Vector3(1.9f, 0.15f + hh + 0.1f, 1.6f), 0f, new Vector3(0.76f, 1f, 0.56f), Style.Feraldis, new Vector3(8f, 0f, 0f));
            Put(lv, P_BarrelStack, "Barrels", new Vector3(1.6f, 0.15f, -1.4f), 90f, Vector3.one * 0.45f);
            Put(lv, V_Cart, "Cart", new Vector3(0.2f, 0.15f, -1.6f), 90f, Vector3.one * 0.45f);
            if (level == 2) return;
            LogTower(lv, "Headframe", 0f, 0.8f, 1.0f, 0.15f + ah, 2, spire: false);
            Put(lv, B_SpikeWall1, "StakesN", new Vector3(1.9f, 0.15f, 1.95f), 180f, new Vector3(0.9f, 0.6f, 1.1f));
            SkullPost(lv, new Vector3(-1.8f, 0.15f, 1.8f), 2.2f);
        }

        /// <summary>WORK CAMP (4 m): a hide tent camp — tents, a drying rack, a lean-to, log piles.</summary>
        static void BuildWorkCamp(Transform lv, int level)
        {
            Yard(lv, 4f, 0.15f);
            HideTent(lv, "Tent", new Vector3(-0.7f, 0.15f, 0.4f), 20f, level == 1 ? 0.65f : 0.75f);
            Put(lv, P_WoodPile, "Logs", new Vector3(1.4f, 0.15f, -1.4f), 70f, Vector3.one * 0.8f);
            Put(lv, P_SackStack, "Sacks", new Vector3(1.5f, 0.15f, 0.6f), 20f, Vector3.one * 0.7f);
            Banner(lv, "PlayerColor_Banner", new Vector3(-1.8f, 0.15f, -1.7f), 0f, 1.0f, _ferCloth);
            if (level == 1) return;
            DryingRack(lv, "Rack", new Vector3(0.6f, 0.15f, 1.9f), new Vector3(2.0f, 0.15f, 1.9f), 2.2f, meat: false);
            Put(lv, P_CageMan, "Cage", new Vector3(1.6f, 0.15f, -0.4f), 30f, Vector3.one * 0.65f);
            Put(lv, H_Thatch, "LeanTo", new Vector3(-1.2f, 1.8f, -1.2f), 180f, new Vector3(0.45f, 0.35f, 0.5f), Style.Feraldis, null, _ferHide);
            Put(lv, H_Beam, "LeanPost", new Vector3(-0.2f, 0.15f, -1.0f), 0f, new Vector3(1f, 0.7f, 1f));
            if (level == 2) return;
            HideTent(lv, "TentB", new Vector3(1.2f, 0.15f, 1.0f), -40f, 0.5f);
            Put(lv, C_PillarSq, "OverseerPost", new Vector3(-1.8f, 0.15f, 1.8f), 0f, new Vector3(0.3f, 0.7f, 0.3f));
            Put(lv, P_ChainDrop, "OverseerChain", new Vector3(-1.8f, 3.6f, 1.7f), 0f, Vector3.one * 1.0f);
            Put(lv, B_SpikeWall1, "Stakes", new Vector3(-1.95f, 0.15f, -1.95f), 0f, new Vector3(0.8f, 0.6f, 1.1f));
            SkullPost(lv, new Vector3(1.9f, 0.15f, -1.9f), 2.0f);
        }

        /// <summary>BLOOD ALTAR (8 m): a stepped stone platform in a ring of standing stones — no timber.</summary>
        static void BuildBloodAltar(Transform lv, int level)
        {
            Yard(lv, 8f, 0.2f);
            Put(lv, C_Block, "TierA", new Vector3(0f, 0.2f, 0f), 0f, new Vector3(6.4f / 5.5f, 0.5f / 5f, 6.4f / 5.5f));
            Put(lv, C_Block, "TierB", new Vector3(0f, 0.7f, 0f), 0f, new Vector3(4.8f / 5.5f, 0.5f / 5f, 4.8f / 5.5f));
            Put(lv, C_Block, "TierC", new Vector3(0f, 1.2f, 0f), 0f, new Vector3(3.2f / 5.5f, 0.5f / 5f, 3.2f / 5.5f));
            Put(lv, P_StoneTable, "Altar", new Vector3(0f, 1.7f, 0f), 90f, new Vector3(1.5f, 1.3f, 1.5f));
            Put(lv, P_BloodPool, "BloodA", new Vector3(0f, 1.72f, 0f), 0f, Vector3.one * 1.5f);
            Put(lv, P_BloodPool, "BloodB", new Vector3(-1.4f, 0.72f, 1.4f), 70f, Vector3.one * 0.9f);
            Put(lv, P_BloodPool, "BloodC", new Vector3(1.6f, 0.22f, -1.8f), 20f, Vector3.one * 0.8f);
            // Standing stones with chains between them.
            const int N = 8; const float R = 3.6f;
            var tops = new Vector3[N];
            for (int i = 0; i < N; i++)
            {
                float a = (i + 0.5f) / N * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a) * R, 0.2f, Mathf.Sin(a) * R);
                float h = 3.2f + (i % 2) * 1.2f;
                Put(lv, E_Rock, "Menhir", p, -a * Mathf.Rad2Deg, new Vector3(2.4f, h / 0.27f, 2.4f), Style.Feraldis, null, _kitStone);
                tops[i] = p + new Vector3(0f, h - 0.4f, 0f);
            }
            for (int i = 0; i < N; i++)
            {
                var a = tops[i]; var b = tops[(i + 1) % N];
                var mid = (a + b) * 0.5f; var d = b - a; d.y = 0f;
                float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 90f;
                Put(lv, P_Chain, "StoneChain", new Vector3(mid.x, Mathf.Min(a.y, b.y) - 0.1f, mid.z), yaw, new Vector3(d.magnitude / 2.55f, 1.2f, 1f));
            }
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                Put(lv, P_Skull, "Skull", new Vector3(Mathf.Cos(a) * 2.0f, 0.7f, Mathf.Sin(a) * 2.0f), -a * Mathf.Rad2Deg, Vector3.one * 2.0f);
            }
            Put(lv, B_Brazier, "BrazierA", new Vector3(-2.0f, 0.2f, -2.9f), 0f, Vector3.one * 1.8f);
            Put(lv, B_Brazier, "BrazierB", new Vector3(2.0f, 0.2f, -2.9f), 0f, Vector3.one * 1.8f);
            Put(lv, P_Cauldron, "Cauldron", new Vector3(2.4f, 0.2f, 2.4f), 0f, Vector3.one * 1.3f);
            Put(lv, P_CageMan, "Cage", new Vector3(-2.5f, 0.2f, 2.3f), 0f, Vector3.one * 0.7f);
            Put(lv, DB_Pile, "Bodies", new Vector3(3.0f, 0.2f, -0.6f), 30f, Vector3.one * 0.5f);
            Put(lv, B_Relic, "RelicA", new Vector3(-0.9f, 0.65f, -3.9f), 15f, Vector3.one * 1.4f);
            Put(lv, B_Relic, "RelicB", new Vector3(0.9f, 0.65f, -3.9f), -15f, Vector3.one * 1.4f);
            Banner(lv, "PlayerColor_BannerA", new Vector3(-3.6f, 0.2f, -3.6f), 0f, 1.5f, _ferCloth);
            Banner(lv, "PlayerColor_BannerB", new Vector3(3.6f, 0.2f, -3.6f), 0f, 1.5f, _ferCloth);
            Put(lv, P_DeerHead, "Head", new Vector3(0f, 3.2f, 3.4f), 0f, Vector3.one * 1.5f);
        }

        /// <summary>WAR TOTEM (4 m): a cluster of three skull-studded poles round a blood pool.</summary>
        static void BuildWarTotem(Transform lv)
        {
            Put(lv, P_BloodPool, "Blood", new Vector3(0f, 0.02f, 0f), 0f, Vector3.one * 1.4f);
            foreach (var (pos, s, h, tag) in new[] { (new Vector3(0f, 0f, 0f), 0.42f, 1.45f, "A"), (new Vector3(-1.1f, 0f, 0.7f), 0.3f, 1.0f, "B"), (new Vector3(1.0f, 0f, 0.9f), 0.3f, 0.85f, "C") })
            {
                Put(lv, C_PillarRound, "Totem" + tag, pos, 0f, new Vector3(s, h, s));
                float top = h * 5f;
                Put(lv, P_Skull, "Skull" + tag + "1", pos + new Vector3(0f, top * 0.35f, -s * 1.1f), 0f, Vector3.one * 2.4f);
                Put(lv, P_Skull, "Skull" + tag + "2", pos + new Vector3(s * 1.1f, top * 0.62f, 0f), 90f, Vector3.one * 2.4f);
                AntlerFinial(lv, "Antler" + tag, pos + new Vector3(0f, top, 0f), 0f, tag == "A" ? 2.4f : 1.6f);
            }
            Put(lv, P_DeerHead, "Head", new Vector3(0f, 5.9f, -0.5f), 0f, Vector3.one * 1.6f);
            Put(lv, P_Chain, "ChainAB", new Vector3(-0.55f, 4.2f, 0.35f), -30f, new Vector3(0.6f, 1f, 1f));
            Put(lv, P_Chain, "ChainAC", new Vector3(0.5f, 3.8f, 0.45f), 40f, new Vector3(0.6f, 1f, 1f));
            Put(lv, B_Brazier, "BrazierA", new Vector3(-1.4f, 0f, -1.0f), 0f, Vector3.one * 1.4f);
            Put(lv, B_Brazier, "BrazierB", new Vector3(1.4f, 0f, -1.0f), 0f, Vector3.one * 1.4f);
            Banner(lv, "PlayerColor_BannerA", new Vector3(-1.6f, 0f, 1.6f), 180f, 1.3f, _ferCloth);
            Banner(lv, "PlayerColor_BannerB", new Vector3(1.6f, 0f, 1.7f), 180f, 1.3f, _ferCloth);
            Put(lv, B_Relic, "Spear", new Vector3(0.8f, 0.5f, -1.6f), 20f, Vector3.one * 1.3f);
        }

        /// <summary>HUNTING LODGE (8 m): a great hide tent, meat racks, a spit, a pelt frame — no hall.</summary>
        static void BuildHuntingLodge(Transform lv)
        {
            Yard(lv, 8f);
            HideTent(lv, "Lodge", new Vector3(0.4f, 0.2f, 0.9f), 0f, 0.95f, big: true, hide: _ferHideDark);
            DryingRack(lv, "MeatRack", new Vector3(-3.6f, 0.2f, -1.4f), new Vector3(-3.6f, 0.2f, 2.8f), 2.8f, meat: true);
            DryingRack(lv, "PeltRack", new Vector3(3.6f, 0.2f, -1.0f), new Vector3(3.6f, 0.2f, 2.6f), 2.4f, meat: false);
            Put(lv, B_Spit, "Spit", new Vector3(0.4f, 0.2f, -3.2f), 0f, Vector3.one * 1.1f);
            Put(lv, B_Firepit, "Firepit", new Vector3(0.4f, 0.3f, -3.2f), 0f, Vector3.one * 1.0f);
            Put(lv, FX_Fire, "Fire", new Vector3(0.4f, 0.35f, -3.2f), 0f, Vector3.one * 0.5f);
            Put(lv, C_PillarSq, "AntlerPost", new Vector3(-2.6f, 0.2f, -3.4f), 0f, new Vector3(0.3f, 0.65f, 0.3f));
            Put(lv, P_DeerHead, "PostHead", new Vector3(-2.6f, 3.0f, -3.5f), 0f, Vector3.one * 1.4f);
            Put(lv, E_DeadTree, "DeadTree", new Vector3(3.6f, 0.2f, -3.3f), 60f, Vector3.one * 0.55f);
            Put(lv, P_WoodPile, "Logs", new Vector3(2.6f, 0.2f, -3.4f), 10f, Vector3.one * 0.9f);
            Banner(lv, "PlayerColor_Banner", new Vector3(-3.7f, 0.2f, -3.7f), 0f, 1.4f, _ferCloth);
            Put(lv, P_Torch, "Torch", new Vector3(1.9f, 1.6f, -2.2f), 0f, Vector3.one * 1.2f);
        }

        /// <summary>LOGGING STATION (8 m): an open sawpit with a crane and log stacks.</summary>
        static void BuildLoggingStation(Transform lv)
        {
            Yard(lv, 8f);
            // The pit: a dark stone-lined rim with a log laid across it.
            Put(lv, C_Block, "PitRim", new Vector3(-0.6f, 0.2f, 0.4f), 0f, new Vector3(4.2f / 5.5f, 0.35f / 5f, 2.0f / 5.5f));
            Put(lv, C_Block, "PitFloor", new Vector3(-0.6f, 0.2f, 0.4f), 0f, new Vector3(3.4f / 5.5f, 0.05f / 5f, 1.2f / 5.5f), Style.Feraldis, null, _ferTurf);
            Put(lv, P_Log, "PitLog", new Vector3(-0.6f, 0.75f, 0.4f), 90f, new Vector3(0.7f, 0.7f, 0.7f));
            Crane(lv, "Crane", new Vector3(1.4f, 0.2f, 1.6f), 5f, 135f);
            LeanTo(lv, "SawShed", -2.6f, -2.4f, 1.2f, 1.4f, 0.2f, 2.8f, yaw: 0f);
            Put(lv, P_GroundLogs, "Logs", new Vector3(2.6f, 0.4f, -0.6f), 90f, new Vector3(0.45f, 0.7f, 0.5f));
            Put(lv, P_WoodPile, "PileA", new Vector3(3.0f, 0.2f, -3.0f), 10f, Vector3.one * 1.1f);
            Put(lv, P_WoodPile, "PileB", new Vector3(1.4f, 0.2f, -3.3f), -5f, Vector3.one * 1.1f);
            foreach (var (px, pz, yaw) in new[] { (-3.4f, 2.6f, 0f), (-1.6f, 3.2f, 40f), (-3.6f, 0.6f, 120f) })
                Put(lv, G_Stump, "Stump", new Vector3(px, 0.2f, pz), yaw, Vector3.one * 1.3f);
            Put(lv, W_Axe, "Axe", new Vector3(-1.6f, 1.1f, 3.2f), 0f, Vector3.one * 1.3f, Style.Feraldis, new Vector3(0f, 0f, -55f));
            Put(lv, V_Cart, "Cart", new Vector3(-0.2f, 0.2f, -2.6f), 0f, Vector3.one * 0.8f);
            Put(lv, B_Brazier, "Brazier", new Vector3(3.5f, 0.2f, 1.0f), 0f, Vector3.one * 1.3f);
            Banner(lv, "PlayerColor_Banner", new Vector3(-3.8f, 0.2f, -3.7f), 0f, 1.4f, _ferCloth);
        }

        /// <summary>The Feraldis VEILSTONE MINE: the dark headframe over a cyan outcrop, a slave cage, the plank cap.</summary>
        static void BuildVeilstoneMineFeraldis(Transform lv)
        {
            Yard(lv, 4f, 0.15f);
            Put(lv, C_Block, "Adit", new Vector3(0f, 0.15f, 0.8f), 0f, new Vector3(3.2f / 5.5f, 2.4f / 5f, 2.4f / 5.5f));
            Put(lv, C_DoorSingle, "AditMouth", new Vector3(0.75f, 0.2f, -0.5f), 0f, new Vector3(1.45f, 0.8f, 1f));
            Put(lv, C_PillarSq, "HeadPostA", new Vector3(-1.5f, 0.15f, -0.5f), 0f, new Vector3(0.4f, 0.83f, 0.4f));
            Put(lv, C_PillarSq, "HeadPostB", new Vector3(1.5f, 0.15f, -0.5f), 0f, new Vector3(0.4f, 0.83f, 0.4f));
            Put(lv, C_PanelWood, "HeadBeam", new Vector3(1.7f, 4.15f, -0.65f), 0f, new Vector3(0.68f, 0.45f, 1f));
            Put(lv, P_ChainDrop, "Hoist", new Vector3(0f, 4.15f, -0.5f), 0f, Vector3.one * 1.2f);
            Put(lv, C_FloorWood, "HeadCap", new Vector3(1.9f, 4.4f, 1.6f), 0f, new Vector3(0.76f, 1f, 0.56f), Style.Feraldis, new Vector3(8f, 0f, 0f));
            for (int i = 0; i < 5; i++)
                Put(lv, i % 2 == 0 ? I_Crystal3 : I_Crystal5, "Veilstone", new Vector3(1.1f + (i % 3) * 0.35f, 0.15f, 1.0f + (i / 3) * 0.6f), i * 40f, Vector3.one * 3.6f, Style.Feraldis, null, _ferVeilstone);
            Put(lv, P_CageMan, "SlaveCage", new Vector3(-1.6f, 0.15f, 1.5f), 30f, Vector3.one * 0.65f);
            Put(lv, V_Cart, "Cart", new Vector3(0.2f, 0.15f, -1.6f), 90f, Vector3.one * 0.45f);
            Put(lv, P_Torch, "Torch", new Vector3(-1.2f, 1.6f, -0.5f), 0f, Vector3.one * 1.1f);
            Banner(lv, "PlayerColor_Banner", new Vector3(-1.8f, 0.15f, -1.7f), 0f, 1.0f, _ferCloth);
            SkullPost(lv, new Vector3(1.9f, 0.15f, -1.8f), 2.0f);
        }

        /// <summary>
        /// FIENDSTONE KEEP (12 m): a black stone-and-iron citadel. Dark
        /// castle-kit curtain with battlements, four round stone towers with
        /// iron spikes, the stepped keep rising to a furnace spire with fire,
        /// ember vents and red rune panels, chains, stakes, braziers — nothing
        /// of the Fortress's log towers or shingle roofs.
        /// </summary>
        static void BuildFiendstoneKeep(Transform lv)
        {
            const float P = 0.8f;
            var g1 = Group(lv, "1_Plinth");
            Put(g1, C_Block, "Plinth", Vector3.zero, 0f, new Vector3(13f / 5.5f, P / 5f, 13f / 5.5f));
            Put(g1, P_BloodPool, "GateBlood", new Vector3(0f, P + 0.02f, -4.6f), 0f, Vector3.one * 1.3f);

            // 2 — the curtain: dark kit walls round +-4.9, battlemented, the big gate in the south.
            var g2 = Group(lv, "2_Curtain");
            const float E = 4.9f; const float wallH = 0.8f;   // of 5 m -> 4 m
            void Curtain(string tag, Vector3 a, Vector3 b)
            {
                Span(g2, C_Wall1, tag, a, b, 5f, Style.Feraldis, wallH);
                Span(g2, C_Batt, tag + "Batt", a + new Vector3(0f, 5f * wallH, 0f), b + new Vector3(0f, 5f * wallH, 0f), 5f);
            }
            Curtain("CurtainW", new Vector3(-E, P, -E + 1.2f), new Vector3(-E, P, E - 1.2f));
            Curtain("CurtainE", new Vector3(E, P, E - 1.2f), new Vector3(E, P, -E + 1.2f));
            Curtain("CurtainN", new Vector3(-E + 1.2f, P, E), new Vector3(E - 1.2f, P, E));
            Curtain("CurtainSE", new Vector3(E - 1.2f, P, -E), new Vector3(2.0f, P, -E));
            Curtain("CurtainSW", new Vector3(-2.0f, P, -E), new Vector3(-E + 1.2f, P, -E));
            Put(g2, C_DoorBig, "Gate", new Vector3(2.0f, P, -E - 0.1f), 0f, new Vector3(0.8f, 0.8f, 1f));
            for (int i = 0; i < 4; i++)
            {
                var rot = Quaternion.Euler(0f, i * 90f, 0f);
                Put(g2, C_Block, "Rune", rot * new Vector3(3.4f, 0f, -E - 0.3f) + new Vector3(0f, P + 1.6f, 0f), i * 90f, new Vector3(0.9f / 5.5f, 1.6f / 5f, 0.1f / 5.5f), Style.Feraldis, null, _ferRune);
            }
            // Stakes outside the curtain, iron chevaux at the gate.
            foreach (var (pos, yaw, len) in new[] {
                (new Vector3(-6.0f, P, -6.0f), -90f, 1f), (new Vector3(-6.0f, P, -1.0f), -90f, 1f), (new Vector3(-6.0f, P, 4.0f), -90f, 0.45f),
                (new Vector3(6.0f, P, -6.0f), -90f, 1f), (new Vector3(6.0f, P, -1.0f), -90f, 1f), (new Vector3(6.0f, P, 4.0f), -90f, 0.45f),
                (new Vector3(-6.0f, P, 6.0f), 0f, 1f), (new Vector3(-1.0f, P, 6.0f), 0f, 1f), (new Vector3(4.0f, P, 6.0f), 0f, 0.45f),
                (new Vector3(-6.0f, P, -6.0f), 0f, 0.55f), (new Vector3(3.2f, P, -6.0f), 0f, 0.55f) })
                Put(g2, B_SpikeWall2, "Stakes", pos, yaw, new Vector3(len, 1.0f, 1.3f));
            Put(g2, B_SpikeFort, "FriseA", new Vector3(-2.4f, P + 0.7f, -5.6f), 0f, Vector3.one * 1.0f);
            Put(g2, B_SpikeFort, "FriseB", new Vector3(2.4f, P + 0.7f, -5.6f), 0f, Vector3.one * 1.0f);

            // 3 — four round stone towers in the dark kit, spiked crowns, braziers.
            var g3 = Group(lv, "3_Towers");
            foreach (var (px, pz) in new[] { (-E, -E), (E, -E), (E, E), (-E, E) })
            {
                float top = StoneTower(g3, "Tower", new Vector3(px, P, pz), 2, 0.55f, cone: false, style: Style.Feraldis);
                Put(g3, B_SpikeFort, "TowerSpikes", new Vector3(px, top + 0.3f, pz), 45f, Vector3.one * 1.0f);
                Put(g3, B_Brazier, "TowerBrazier", new Vector3(px, top - 0.2f, pz), 0f, Vector3.one * 1.6f);
                Put(g3, FX_Fire, "TowerFire", new Vector3(px, top + 0.3f, pz), 0f, Vector3.one * 0.5f);
            }

            // 4 — the stepped keep behind the gate court.
            var g4 = Group(lv, "4_Keep");
            Put(g4, C_Block, "KeepLower", new Vector3(0f, P, 1.2f), 0f, new Vector3(6.4f / 5.5f, 7f / 5f, 5.6f / 5.5f));
            float k1 = P + 7f;
            foreach (var (a, b) in new[] { (new Vector3(-3.0f, k1, -1.4f), new Vector3(3.0f, k1, -1.4f)), (new Vector3(3.0f, k1, -1.4f), new Vector3(3.0f, k1, 3.8f)),
                                           (new Vector3(3.0f, k1, 3.8f), new Vector3(-3.0f, k1, 3.8f)), (new Vector3(-3.0f, k1, 3.8f), new Vector3(-3.0f, k1, -1.4f)) })
                Span(g4, C_Batt, "KeepBatt", a, b, 5f);
            Put(g4, C_Block, "KeepUpper", new Vector3(0f, k1, 1.4f), 0f, new Vector3(4.2f / 5.5f, 4f / 5f, 4.2f / 5.5f));
            for (int side = 0; side < 4; side++)
            {
                var rot = Quaternion.Euler(0f, side * 90f, 0f);
                for (int k = 0; k < 2; k++)
                    Put(g4, C_Block, "Vent", new Vector3(0f, P + 2.8f, 1.2f) + rot * new Vector3(-1.2f + k * 2.4f, 0f, 2.82f), side * 90f, new Vector3(0.5f / 5.5f, 2.4f / 5f, 0.12f / 5.5f), Style.Feraldis, null, _ferEmber);
                Put(g4, C_Block, "RuneUpper", new Vector3(0f, k1 + 1.5f, 1.4f) + rot * new Vector3(0f, 0f, 2.12f), side * 90f, new Vector3(0.7f / 5.5f, 1.6f / 5f, 0.12f / 5.5f), Style.Feraldis, null, _ferRune);
            }
            Put(g4, C_DoorBig, "KeepGate", new Vector3(2.0f, P, 1.2f - 2.8f - 0.1f), 0f, new Vector3(0.8f, 0.8f, 1f));

            // 5 — the furnace spire: the stack, ember vents on every face, the fire crown and smoke.
            var g5 = Group(lv, "5_Spire");
            float k2 = k1 + 4f;
            Put(g5, C_Block, "StackA", new Vector3(0f, k2, 1.4f), 0f, new Vector3(3.0f / 5.5f, 4.5f / 5f, 3.0f / 5.5f));
            Put(g5, C_Block, "StackB", new Vector3(0f, k2 + 4.5f, 1.4f), 0f, new Vector3(2.2f / 5.5f, 3.5f / 5f, 2.2f / 5.5f));
            for (int side = 0; side < 4; side++)
            {
                var rot = Quaternion.Euler(0f, side * 90f, 0f);
                Put(g5, C_Block, "SpireVent", new Vector3(0f, k2 + 1.2f, 1.4f) + rot * new Vector3(0f, 0f, 1.52f), side * 90f, new Vector3(0.6f / 5.5f, 2.4f / 5f, 0.12f / 5.5f), Style.Feraldis, null, _ferEmber);
                Put(g5, C_Block, "SpireVentB", new Vector3(0f, k2 + 5.2f, 1.4f) + rot * new Vector3(0f, 0f, 1.12f), side * 90f, new Vector3(0.45f / 5.5f, 2.0f / 5f, 0.12f / 5.5f), Style.Feraldis, null, _ferEmber);
            }
            float spireTop = k2 + 8f;
            Put(g5, B_Brazier, "Crown", new Vector3(0f, spireTop, 1.4f), 0f, Vector3.one * 3.0f);
            Put(g5, FX_Fire, "CrownFire", new Vector3(0f, spireTop + 1.2f, 1.4f), 0f, Vector3.one * 1.6f);
            Put(g5, FX_Smoke, "CrownSmoke", new Vector3(0f, spireTop + 2.2f, 1.4f), 0f, Vector3.one * 1.6f);
            Put(g5, B_SpikeFort, "CrownSpikes", new Vector3(0f, spireTop + 0.4f, 1.4f), 45f, Vector3.one * 1.3f);
            Put(g5, P_Skull, "CrownSkullA", new Vector3(-0.9f, spireTop - 0.2f, 0.3f), 0f, Vector3.one * 3f);
            Put(g5, P_Skull, "CrownSkullB", new Vector3(0.9f, spireTop - 0.2f, 0.3f), 0f, Vector3.one * 3f);
            foreach (var (px, pz) in new[] { (-E, -E), (E, -E), (E, E), (-E, E) })
            {
                var from = new Vector3(0f, k2 + 3.8f, 1.4f);
                var to = new Vector3(px, P + 11.5f, pz);
                var mid = (from + to) * 0.5f; var d = to - from; d.y = 0f;
                float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 90f;
                Put(g5, P_Chain, "SpireChain", new Vector3(mid.x, Mathf.Min(from.y, to.y) + 0.3f, mid.z), yaw, new Vector3(d.magnitude / 2.55f, 2.2f, 1f));
            }

            // 6 — braziers, banners, the great flag, trophies, the gallows, the pile.
            var g6 = Group(lv, "6_Props");
            Put(g6, B_Brazier, "GateBrazierA", new Vector3(-3.4f, P, -5.4f), 0f, Vector3.one * 2.2f);
            Put(g6, B_Brazier, "GateBrazierB", new Vector3(3.4f, P, -5.4f), 0f, Vector3.one * 2.2f);
            Put(g6, FX_Fire, "GateFireA", new Vector3(-3.4f, P + 1.1f, -5.4f), 0f, Vector3.one * 0.6f);
            Put(g6, FX_Fire, "GateFireB", new Vector3(3.4f, P + 1.1f, -5.4f), 0f, Vector3.one * 0.6f);
            Banner(g6, "PlayerColor_BannerA", new Vector3(-3.0f, P, -4.5f), 0f, 1.8f, _ferCloth);
            Banner(g6, "PlayerColor_BannerB", new Vector3(3.0f, P, -4.5f), 0f, 1.8f, _ferCloth);
            Put(g6, BN_FlagH, "PlayerColor_KeepCloth", new Vector3(1.6f, P + 5.2f, 1.2f - 2.8f - 0.25f), 0f, new Vector3(0.8f, 0.6f, 0.7f), Style.Neutral, null, _ferCloth);
            FlagMast(g6, "GreatFlag", new Vector3(0f, k2 + 4.5f, 1.4f), 5.5f, 1.1f);
            Put(g6, P_DeerHead, "GateTrophy", new Vector3(0f, P + 4.2f, -E - 0.2f), 0f, Vector3.one * 1.8f);
            Put(g6, P_Gallows, "Gallows", new Vector3(-3.2f, P, -2.6f), 90f, Vector3.one * 1.1f);
            Put(g6, P_CageMan, "Cage", new Vector3(-3.2f, P + 0.7f, -1.6f), 0f, Vector3.one * 0.8f);
            Put(g6, DB_Pile, "Bodies", new Vector3(3.4f, P, -2.2f), 30f, Vector3.one * 0.6f);
            Put(g6, P_Cauldron, "Cauldron", new Vector3(3.6f, P, -0.8f), 0f, Vector3.one * 1.3f);
            Put(g6, P_Chain, "GateChainA", new Vector3(-1.2f, P + 3.6f, -E - 0.4f), 0f, Vector3.one * 1.2f);
            Put(g6, P_Chain, "GateChainB", new Vector3(3.6f, P + 3.6f, -E - 0.4f), 0f, Vector3.one * 1.2f);
        }

        // ═════════════════════════════════════════════════════════════════
        // The line-up: every Feraldis building at Lv2 (or its one form)
        // ═════════════════════════════════════════════════════════════════

        [MenuItem("Waning Border/Art/Render Feraldis Lineup")]
        public static void RenderLineup()
        {
            string outDir = Path.GetFullPath(PreviewDir);
            Directory.CreateDirectory(outDir);
            var report = new List<string>();
            string ghSo = TT + "Age0/Buildings/GatherersHut/GatherersHut.asset", templeSo = TT + "Age0/Buildings/TempleOfRidan/TempleOfRidan.asset";
            var items = new List<(string name, Func<GameObject> make, Vector2 fp)>
            {
                ("Fortress", () => ShownVariant(FortressPrefabPath, Cultures.Feraldis, 2), FootprintOf(TT + "Age0/Buildings/Fortress/Fortress.asset")),
                ("GreatForge", () => ShownVariant(FER + "GreatForge/GreatForge.prefab", Cultures.Feraldis, 2), FootprintOf(FER + "GreatForge/GreatForge.asset")),
                ("FiendstoneKeep", () => ShownVariant(SoPrefabPath(TT + "Age0/Buildings/FiendstoneKeep/FiendstoneKeep.asset"), Cultures.None, 0), FootprintOf(TT + "Age0/Buildings/FiendstoneKeep/FiendstoneKeep.asset")),
                ("Longhouse", () => ShownVariant(SoPrefabPath(BarracksSoPath), Cultures.Feraldis, 2), FootprintOf(BarracksSoPath)),
                ("HallOfAxes", () => ShownVariant(FER + "HallOfAxes/HallOfAxes.prefab", Cultures.Feraldis, 2), FootprintOf(FER + "HallOfAxes/HallOfAxes.asset")),
                ("RigYard", () => ShownVariant(FER + "Pasture/RigYard.prefab", Cultures.Feraldis, 2), FootprintOf(FER + "Pasture/Pasture.asset")),
                ("EngineWorks", () => ShownVariant(FER + "SiegeYard/EngineWorks.prefab", Cultures.Feraldis, 2), FootprintOf(FER + "SiegeYard/SiegeYard.asset")),
                ("BloodAltar", () => ShownVariant(SoPrefabPath(templeSo), Cultures.Feraldis, 1), FootprintOf(templeSo)),
                ("HuntingLodge", () => Instance(FER + "HuntingLodge/HuntingLodge.prefab"), FootprintOf(FER + "HuntingLodge/HuntingLodge.asset")),
                ("LoggingStation", () => Instance(FER + "LoggingStation/LoggingStation.prefab"), FootprintOf(FER + "LoggingStation/LoggingStation.asset")),
                ("SlavePens", () => Instance(SoPrefabPath(FER + "SlavePens/SlavePens_Lvl2.asset")), FootprintOf(TT + "Age0/Buildings/Hut/Hut.asset")),
                ("WorkCamp", () => ShownVariant(SoPrefabPath(ghSo), Cultures.Feraldis, 2), FootprintOf(ghSo)),
                ("Mine", () => ShownVariant(SoPrefabPath(TT + "Age0/Buildings/Mine/Mine.asset"), Cultures.Feraldis, 2), FootprintOf(TT + "Age0/Buildings/Mine/Mine.asset")),
                ("VeilstoneMine", () => ShownVariant(SoPrefabPath(TT + "Age0/Buildings/VeilstoneMine/VeilstoneMine.asset"), Cultures.Feraldis, 1), FootprintOf(TT + "Age0/Buildings/VeilstoneMine/VeilstoneMine.asset")),
                ("WarTotem", () => Instance(FER + "WarTotem/WarTotem.prefab"), FootprintOf(FER + "WarTotem/WarTotem.asset")),
            };
            // The in-game pitch and FOV; at the default 37.5 m the frame holds
            // ~49 m, so the two rows are shot from 70 m (noted) and from the
            // max zoom-out (80 m, tilt 70).
            try { report.Add(RenderGrid("Lineup_Feraldis", outDir, items, 74f, 47f)); } catch (Exception e) { report.Add("FAIL lineup: " + e.Message); }
            try { report.Add(RenderGrid("Lineup_Feraldis_far", outDir, items, 80f, 70f)); } catch (Exception e) { report.Add("FAIL lineup far: " + e.Message); }
            string text = "[FeraldisKit] lineup\n" + string.Join("\n", report);
            Debug.Log(text);
            try { File.WriteAllText(Path.Combine(outDir, "lineup_report.txt"), text); } catch (Exception) { }
        }

        static string RenderGrid(string name, string outDir, List<(string name, Func<GameObject> make, Vector2 fp)> items, float dist, float tilt)
        {
            var created = new List<GameObject>();
            var stage = new Vector3(0f, -4000f, 0f);
            try
            {
                int perRow = (items.Count + 2) / 3;
                var rowW = new float[3];
                for (int i = 0; i < items.Count; i++) rowW[i / perRow] += Mathf.Max(items[i].fp.x, items[i].fp.y) + 3.5f;
                float x = 0f; var sizes = new List<string>();
                for (int i = 0; i < items.Count; i++)
                {
                    int row = i / perRow, col = i % perRow;
                    if (col == 0) x = -rowW[row] * 0.5f;
                    var it = items[i];
                    var go = it.make();
                    created.Add(go);
                    float fit = PresentationSpawnSystem.ComputeFootprintFitForSize(go, it.fp.x, it.fp.y, out Vector3 offset);
                    go.transform.localScale = Vector3.one * fit;
                    float w = Mathf.Max(it.fp.x, it.fp.y);
                    float cx = x + w * 0.5f;
                    go.transform.position = stage + new Vector3(cx, 0f, row * 17f - 10f) - new Vector3(offset.x, 0f, offset.z) * fit;
                    x += w + 3.5f;
                    TheWaningBorder.Rendering.BuildingFactionColorMarker.Apply(go, new Color(0.85f, 0.15f, 0.15f));
                    sizes.Add($"{it.name} fit {fit:F2}");
                }
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.hideFlags = HideFlags.HideAndDontSave; created.Add(ground);
                ground.transform.position = stage + new Vector3(0f, -0.02f, 8f);
                ground.transform.localScale = new Vector3(40f, 1f, 40f);
                var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                gm.SetColor("_BaseColor", new Color(0.30f, 0.40f, 0.28f)); gm.SetFloat("_Smoothness", 0.05f);
                ground.GetComponent<Renderer>().sharedMaterial = gm;
                UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());
                var camGo = new GameObject("LineupCam") { hideFlags = HideFlags.HideAndDontSave }; created.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 40f; cam.nearClipPlane = 0.3f; cam.farClipPlane = 600f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.10f, 0.12f, 0.18f, 1f);
                var focus = stage + new Vector3(0f, 2f, 7f);
                var back = new Vector3(0f, Mathf.Sin(tilt * Mathf.Deg2Rad), -Mathf.Cos(tilt * Mathf.Deg2Rad));
                camGo.transform.position = focus + back * dist;
                camGo.transform.rotation = Quaternion.LookRotation(-back, Vector3.up);
                var sunGo = new GameObject("LineupSun") { hideFlags = HideFlags.HideAndDontSave }; created.Add(sunGo);
                var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.7f; sun.color = new Color(1f, 0.94f, 0.86f); sun.shadows = LightShadows.Soft;
                sunGo.transform.rotation = Quaternion.Euler(50f, -40f, 0f);
                var fillGo = new GameObject("LineupFill") { hideFlags = HideFlags.HideAndDontSave }; created.Add(fillGo);
                var fill = fillGo.AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = 0.45f; fill.color = new Color(0.75f, 0.8f, 1f);
                fillGo.transform.rotation = Quaternion.Euler(35f, 140f, 0f);
                bool fog = RenderSettings.fog; var ambMode = RenderSettings.ambientMode; var ambCol = RenderSettings.ambientLight;
                RenderSettings.fog = false; RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.42f, 0.48f, 0.58f);
                var rt = new RenderTexture(2560, 1440, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt; cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                RenderSettings.fog = fog; RenderSettings.ambientMode = ambMode; RenderSettings.ambientLight = ambCol;
                cam.targetTexture = null;
                string file = Path.Combine(outDir, name + ".png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex); rt.Release(); UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(gm);
                return $"OK {name}: {string.Join(", ", sizes)} -> {file}";
            }
            finally { foreach (var c in created) if (c != null) UnityEngine.Object.DestroyImmediate(c); }
        }
    }
}
