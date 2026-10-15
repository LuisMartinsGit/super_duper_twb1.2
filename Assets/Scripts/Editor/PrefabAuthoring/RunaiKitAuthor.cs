// RunaiKitAuthor.cs
// The Runai art (docs/Design/Age_1_Runai.md §1.1, 2026-10-14): monumental
// sandstone, Arabian / Egyptian temple forms — domes, minarets, open
// colonnades and pointed iwans — dressed in coloured silk awnings, with
// balloon-suspended parts from the higher levels. Palette: sandstone, silver,
// teal (drapery), purple (decorations); the player colour is cloth (banners).
//
// Composed, like the Feraldis set, from the Synty POLYGON Fantasy Kingdom kit
// (a partial of FeraldisKitAuthor so it shares Put / Span / Save / the level
// and preview helpers). The kit has no dome or balloon: a dome is the castle
// kit's low round cap stretched tall on a stone drum, an onion is a scaled
// sphere, a balloon is a sphere envelope on rope tethers. No Alanthor
// composition is reused (Alanthor = grey castle blocks, slate cones).
//
// Paths (see the per-building notes in Author):
//   Fortress / Route Guard (Barracks) / Trading Post (Gatherer's Hut):
//     a Runai/Lv1..3 branch inside the shared Age 0 prefab.
//   Arrowyard, Grazing Grounds, Wagonworks, Sanctuary: a variant prefab
//     (Lv0 construction frame + Runai/Lv1..3) in the building's own folder.
//   Thessara's Crossing: one form (landmark).
//   Mine: a Runai/Lv1..3 branch inside the Age 0 Mine prefab (a variant
//     prefab is switched in place, so a level-SO ladder would never show).
//   Waystation (the Runai Hut): one prefab per level on the level SOs — the
//     Hut's SO model has no variant layout, so its ladder IS the path.
//
// Run: Waning Border > Art > Author Runai Kit Art (idempotent), then
// Waning Border > Art > Render Runai Level Comparisons -> Temp/runai-kit-previews/.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static partial class FeraldisKitAuthor
    {
        const string RU = TT + "Civs/Runai/Buildings/";
        const string RuMatDir = RU + "Art/Materials/";
        const string RuPreviewDir = "Temp/runai-kit-previews";
        const string RuReportPath = "Temp/runai-kit-report.txt";

        // ── kit pieces (measured with Preview Kit Pieces) ──
        const string R_Col = "Castle/SM_Bld_Castle_Pillar_Stone_Round_03";     // 0.59 w, 5.05 h, pivot at the foot
        const string R_ColFat = "Castle/SM_Bld_Castle_Pillar_Stone_Round_01";  // 1.65 w, 5.0 h — the drum
        const string R_Dome = "Castle/SM_Bld_Castle_Roof_Cap_Round_01";        // 2.57 w, 0.81 h — a low dome
        const string R_Spire = "Castle/SM_Bld_Castle_Roof_Cap_Round_03";       // 2.57 w, 7.2 h — finial / minaret top
        const string R_Arch = "Castle/SM_Bld_Castle_Wall_Archway_01";          // 5 x 5 x 0.5, pivot at +X end
        const string R_ArchPt = "Castle/SM_Bld_Castle_Wall_Archway_02";        // pointed iwan arch
        const string R_Disc = "Castle/SM_Bld_Castle_Floor_Stone_Round_M_01";   // 3.75 disc, pivot at its (+X,-Z) corner
        const string R_Pavilion = "Buildings/SM_Bld_Tent_Open_02";            // 3.25 w, 4.14 h silk pavilion
        const string R_Awning = "Props/SM_Prop_Awning_03";                     // 3.44 w awning
        const string R_Curtain = "Props/SM_Prop_Curtain_Large_01";             // 4.1 w drape
        const string R_Stall = "Props/SM_Prop_Market_Stall_01";               // 2.7 x 2.9 x 4.25
        const string R_Rug = "Props/SM_Prop_Rug_01_Draped_01";
        const string R_Pot = "Props/SM_Prop_Pot_01";
        const string R_Planter = "Environments/SM_Env_Planter_Pot_01";
        const string R_Obelisk = "Castle/SM_Bld_Keep_Pillar_Cap_01";           // 1.36 x 3.13 stepped obelisk

        static Material _ruSand, _ruPaving, _ruStucco, _ruKit, _ruSilver, _ruTeal, _ruPurple, _ruDomeTeal, _ruRope, _ruCloth, _ruGlow;

        // ═════════════════════════════════════════════════════════════════
        [MenuItem("Waning Border/Art/Author Runai Kit Art")]
        public static void AuthorRunai()
        {
            Report.Clear(); _failures = 0;
            try
            {
                EnsureMaterials();          // Feraldis set: Put's Feraldis branch needs them loaded
                EnsureRunaiMaterials();
                RuBranch(TT + "Age0/Buildings/Fortress/Fortress.asset", "RunaiFortress", 3, BuildRuFortress,
                         RU + "Fortress/Fortress_Lvl{0}.asset");
                RuBranch(BarracksSoPath, "RouteGuard", 3, BuildRouteGuard, RU + "RouteGuard/RouteGuard_Lvl{0}.asset");
                RuBranch(TT + "Age0/Buildings/GatherersHut/GatherersHut.asset", "TradingPost", 3, BuildTradingPost,
                         RU + "TradingPost/TradingPost_Lvl{0}.asset");
                RuBranch(TT + "Age0/Buildings/TempleOfRidan/TempleOfRidan.asset", "RunaiTemple", 1, (lv, l) => BuildRuTemple(lv), null);
                RuVariant(RU + "Arrowyard/Arrowyard.prefab", RU + "Arrowyard/Arrowyard.asset", "Arrowyard", 8f, BuildArrowyard);
                RuVariant(RU + "GrazingGrounds/GrazingGrounds.prefab", RU + "GrazingGrounds/GrazingGrounds.asset", "GrazingGrounds", 8f, BuildGrazingGrounds);
                RuVariant(RU + "SiegeWorkshop/Wagonworks.prefab", RU + "SiegeWorkshop/SiegeWorkshop.asset", "Wagonworks", 8f, BuildWagonworks);
                RuVariant(RU + "Sanctuary/Sanctuary.prefab", RU + "Sanctuary/Sanctuary.asset", "Sanctuary", 4f, BuildSanctuary);
                PlainInPlace(RU + "ThessarasCrossing/ThessarasCrossing.prefab", TT + "Age0/Buildings/ThessarasCrossing/ThessarasCrossing.asset",
                             "ThessarasCrossing", BuildCrossing);
                LevelPrefabs(RU + "Waystation/Art/", "Waystation", RU + "Waystation/Waystation_Lvl{0}.asset", BuildWaystation);
                // The Mine's SO prefab is a variant layout (Age0KitAuthor), and a
                // variant prefab is switched IN PLACE — the level-SO ladder is
                // never read for it. So the Runai mine is a branch, not a ladder.
                RuBranch(TT + "Age0/Buildings/Mine/Mine.asset", "RunaiMine", 3, BuildRuMine, RU + "Mine/Mine_Lvl{0}.asset");
            }
            catch (Exception e) { Fail("AuthorRunai", e.ToString()); }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string text = $"[RunaiKit] done — {Report.Count} entries, {_failures} failure(s)\n" + string.Join("\n", Report);
            Debug.Log(text);
            try { File.WriteAllText(RuReportPath, text); } catch (Exception) { }
        }

        // ═════════════════════════════════════════════════════════════════
        // Materials — sandstone, silver, teal silk, purple
        // ═════════════════════════════════════════════════════════════════

        static void EnsureRunaiMaterials()
        {
            var shader = Shader.Find(BuildingLitShader);
            if (shader == null) throw new Exception("shader missing: " + BuildingLitShader);
            Directory.CreateDirectory(RuMatDir);
            _ruSand = RuMat(shader, "Ru_Sandstone", KitTex + "Castle/Wall_Stucco_01.png", new Color(1.06f, 0.90f, 0.67f), 0.08f, 0f);
            _ruPaving = RuMat(shader, "Ru_Paving", KitTex + "Castle/Wall_Large_Brick_01.png", new Color(1.30f, 1.08f, 0.76f), 0.06f, 0f);
            _ruStucco = RuMat(shader, "Ru_Stucco", KitTex + "Castle/Wall_Stucco_01.png", new Color(1.12f, 1.03f, 0.88f), 0.08f, 0f);
            _ruKit = RuMat(shader, "Ru_Kit", KitTex + "Alts/PolygonFantasyKingdom_01_C.png", new Color(1.00f, 0.96f, 0.90f), 0.10f, 0f);
            _ruSilver = RuMat(shader, "Ru_Silver", null, new Color(0.80f, 0.83f, 0.88f), 0.65f, 0.85f);
            _ruTeal = RuMat(shader, "Ru_Silk_Teal", null, new Color(0.05f, 0.56f, 0.53f), 0.30f, 0f);
            _ruPurple = RuMat(shader, "Ru_Silk_Purple", null, new Color(0.44f, 0.17f, 0.58f), 0.30f, 0f);
            _ruDomeTeal = RuMat(shader, "Ru_Glaze_Teal", null, new Color(0.10f, 0.64f, 0.62f), 0.60f, 0.05f);
            _ruRope = RuMat(shader, "Ru_Rope", null, new Color(0.42f, 0.33f, 0.22f), 0.05f, 0f);
            _ruGlow = RuMat(shader, "Ru_Lantern", null, new Color(1.00f, 0.80f, 0.45f), 0.2f, 0f);
            _ruGlow.EnableKeyword("_EMISSION");
            _ruGlow.SetColor("_EmissionColor", new Color(2.4f, 1.6f, 0.7f));
            EditorUtility.SetDirty(_ruGlow);

            // The player colour lives in the cloth: a *playercolor* material is
            // the named slot BuildingFactionColorMarker paints (and it turns the
            // atlas hue swap off for the whole building).
            string clothPath = RuMatDir + "Ru_PlayerColor_Cloth.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(clothPath) == null)
                AssetDatabase.CopyAsset(FerMatDir + "Fer_PlayerColor_Cloth.mat", clothPath);
            _ruCloth = AssetDatabase.LoadAssetAtPath<Material>(clothPath);
            if (_ruCloth == null) throw new Exception("could not make " + clothPath);
        }

        static Material RuMat(Shader shader, string name, string texPath, Color tint, float smoothness, float metallic)
        {
            string path = RuMatDir + name + ".mat";
            Texture2D tex = null;
            if (texPath != null)
            {
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (tex == null) throw new Exception("kit texture missing: " + texPath);
            }
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader) { name = name }; AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != shader) m.shader = shader;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
            Report.Add("OK material " + path);
            return m;
        }

        /// <summary>Style.Runai in Put: the kit atlas to the cream/teal 01_C
        /// alt, castle stone to sandstone, castle slate (caps, cones) to silver.</summary>
        static Material RunaiRestyle(Material m)
        {
            if (m.name.StartsWith("PolygonFantasyKingdom_Mat", StringComparison.OrdinalIgnoreCase)) return _ruKit;
            if (m.name.StartsWith("Castle_Roof", StringComparison.OrdinalIgnoreCase)) return _ruSilver;
            if (m.name.StartsWith("Castle_", StringComparison.OrdinalIgnoreCase)) return _ruSand;
            return null;
        }

        // ═════════════════════════════════════════════════════════════════
        // Runai vocabulary
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Put with the Runai palette — and no node left named *roof*
        /// (BuildingFactionColorMarker paints those flat slate).</summary>
        static GameObject RuPut(Transform p, string piece, string name, Vector3 pos, float yaw = 0f,
                                Vector3? scale = null, Material mat = null, Vector3? euler = null)
        {
            var go = Put(p, piece, name, pos, yaw, scale, Style.Runai, euler, mat);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t.name.IndexOf("roof", StringComparison.OrdinalIgnoreCase) >= 0)
                    t.name = Regex.Replace(t.name, "roof", "Cap", RegexOptions.IgnoreCase);
            return go;
        }

        /// <summary>A solid block: centre of its base at <paramref name="c"/>.</summary>
        static GameObject RuBox(Transform p, string name, Vector3 c, Vector3 size, float yaw = 0f, Material mat = null)
            => RuPut(p, C_Block, name, c, yaw, new Vector3(size.x / 5.5f, size.y / 5f, size.z / 5.5f), mat);

        /// <summary>The paved yard every level shares: footprint + 1 m (see the level rule).</summary>
        static void RuYard(Transform lv, float footprint)
            => RuBox(lv, "Yard", Vector3.zero, new Vector3(footprint + 1f, 0.2f, footprint + 1f), 0f, _ruPaving);

        static GameObject Column(Transform p, string name, Vector3 foot, float h, float w = 0.55f, Material mat = null)
            => RuPut(p, R_Col, name, foot, 0f, new Vector3(w / 0.59f, h / 5.05f, w / 0.59f), mat ?? _ruStucco);

        static GameObject Drum(Transform p, string name, Vector3 foot, float r, float h)
            => RuPut(p, R_ColFat, name, foot, 0f, new Vector3(2f * r / 1.65f, h / 5f, 2f * r / 1.65f), _ruSand);

        /// <summary>A dome of radius r and rise h standing on <paramref name="foot"/>,
        /// with a silver finial. Silver unless <paramref name="mat"/> says otherwise.</summary>
        static float Dome(Transform p, string name, Vector3 foot, float r, float h, Material mat = null, bool finial = true)
        {
            RuPut(p, R_Dome, name, foot, 0f, new Vector3(2f * r / 2.57f, h / 0.81f, 2f * r / 2.57f), mat ?? _ruSilver);
            float top = foot.y + h;
            if (finial)
            {
                float s = Mathf.Clamp(r * 0.13f, 0.08f, 0.45f);
                RuPut(p, R_Spire, name + "Finial", new Vector3(foot.x, top - 0.08f, foot.z), 0f, Vector3.one * s, _ruSilver);
                top += 7.2f * s;
            }
            return top;
        }

        /// <summary>An onion dome: a sphere a little taller than wide, half sunk, with a finial.</summary>
        static void Onion(Transform p, string name, Vector3 foot, float r, Material mat)
        {
            Sphere(p, name, foot + new Vector3(0f, r * 0.55f, 0f), new Vector3(2f * r, 2.4f * r, 2f * r), mat);
            float s = Mathf.Clamp(r * 0.12f, 0.06f, 0.35f);
            RuPut(p, R_Spire, name + "Finial", foot + new Vector3(0f, r * 1.65f, 0f), 0f, Vector3.one * s, _ruSilver);
        }

        static GameObject Sphere(Transform p, string name, Vector3 centre, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(p, false);
            go.transform.localPosition = centre;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        /// <summary>A minaret: slender shaft, a gallery bulge and a silver spire. Returns its top.</summary>
        static float Minaret(Transform p, string tag, float x, float z, float y0, float h)
        {
            float shaft = h - 2.4f;
            RuBox(p, tag + "Base", new Vector3(x, y0, z), new Vector3(1.3f, 0.7f, 1.3f), 0f, _ruSand);
            Column(p, tag + "Shaft", new Vector3(x, y0, z), shaft, 0.95f, _ruStucco);
            RuPut(p, R_Dome, tag + "Gallery", new Vector3(x, y0 + shaft - 0.15f, z), 0f, new Vector3(0.62f, 0.45f, 0.62f), _ruSand);
            RuPut(p, R_Spire, tag + "Spire", new Vector3(x, y0 + shaft + 0.15f, z), 0f, Vector3.one * 0.31f, _ruSilver);
            return y0 + shaft + 0.15f + 7.2f * 0.31f;
        }

        /// <summary>Columns from a to b (n of them, ends included) under a stone lintel.</summary>
        static void Colonnade(Transform p, string tag, Vector3 a, Vector3 b, int n, float h, float w = 0.5f)
        {
            for (int i = 0; i < n; i++)
            {
                var foot = Vector3.Lerp(a, b, n == 1 ? 0.5f : i / (float)(n - 1));
                Column(p, tag + "Col" + Letter(i), foot, h, w);
            }
            Lintel(p, tag + "Lintel", a + Vector3.up * h, b + Vector3.up * h, 0.4f, w + 0.25f);
        }

        static void Lintel(Transform p, string name, Vector3 a, Vector3 b, float h, float depth)
        {
            var d = b - a; d.y = 0f;
            float len = d.magnitude;
            float yaw = Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg;
            RuBox(p, name, (a + b) * 0.5f, new Vector3(len + depth, h, depth), yaw, _ruSand);
        }

        /// <summary>An arched wall from a to b (the archway's -Z face out), height h.</summary>
        static void Arcade(Transform p, string tag, Vector3 a, Vector3 b, float h, bool pointed = false)
            => Span(p, pointed ? R_ArchPt : R_Arch, tag, a, b, 5f, Style.Runai, h / 5f);

        static GameObject Silk(Transform p, string piece, string name, Vector3 pos, float yaw, float scale, Material mat)
            => RuPut(p, piece, name, pos, yaw, Vector3.one * scale, mat);

        static GameObject RuBanner(Transform p, string name, Vector3 pos, float yaw, float scale = 1f)
            => Put(p, BN_Battle, name, pos, yaw, Vector3.one * scale, Style.Neutral, null, _ruCloth);

        /// <summary>A thin rope from a to b.</summary>
        static void Rope(Transform p, string name, Vector3 a, Vector3 b)
        {
            var d = b - a;
            var go = RuPut(p, R_Col, name, a, 0f, new Vector3(0.06f / 0.59f, d.magnitude / 5.05f, 0.06f / 0.59f), _ruRope);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
        }

        /// <summary>
        /// A silk balloon of radius r centred at <paramref name="centre"/>: the
        /// envelope, a silver crown, a glowing lantern basket under it, and a rope
        /// from the basket to every anchor — what holds the balloon-suspended
        /// parts of the higher levels.
        /// </summary>
        static void Balloon(Transform p, string tag, Vector3 centre, float r, Material envelope, params Vector3[] anchors)
        {
            Sphere(p, tag + "Envelope", centre, new Vector3(2f * r, 2.3f * r, 2f * r), envelope);
            RuPut(p, R_Dome, tag + "Crown", centre + new Vector3(0f, 1.0f * r, 0f), 0f, new Vector3(0.45f * r, 0.5f, 0.45f * r), _ruSilver);
            var basket = centre - new Vector3(0f, 1.15f * r + 0.6f, 0f);
            Sphere(p, tag + "Lantern", basket, Vector3.one * Mathf.Max(0.25f, 0.28f * r), _ruGlow);
            Rope(p, tag + "Hang", basket, centre - new Vector3(0f, 1.1f * r, 0f));
            for (int i = 0; i < anchors.Length; i++) Rope(p, tag + "Tether" + Letter(i), anchors[i], basket);
        }

        /// <summary>A round stone platform of diameter d, centred at <paramref name="c"/>.</summary>
        static void Platform(Transform p, string name, Vector3 c, float d)
        {
            float s = d / 3.75f;
            RuPut(p, R_Disc, name, c + new Vector3(-1.875f * s, 0f, 1.875f * s), 0f, new Vector3(s, 1f, s), _ruSand);
        }

        // ═════════════════════════════════════════════════════════════════
        // Paths into the game
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Write Runai/Lv1..levels into the variant prefab an Age 0
        /// building's SO points at (the cultured building is the same entity),
        /// and clear the Runai level SOs' own prefab so the branch is the path.</summary>
        static void RuBranch(string soPath, string stem, int levels, Action<Transform, int> build, string levelSoFormat)
        {
            try
            {
                string prefabPath = SoPrefabPath(soPath);
                if (string.IsNullOrEmpty(prefabPath) || !prefabPath.EndsWith(".prefab")) throw new Exception("SO prefab is not a prefab: " + soPath);
                var contents = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    Transform container = FindContainerWithLv0(contents.transform, 2);
                    if (container == null) throw new Exception("no Lv0 container in " + prefabPath);
                    var stray = FindChild(contents.transform, "Runai");
                    if (stray != null && stray != container && stray.childCount == 0) UnityEngine.Object.DestroyImmediate(stray.gameObject);
                    Transform ru = FindChild(container, "Runai") ?? Group(container, "Runai");
                    Clear(ru);
                    for (int l = 1; l <= levels; l++)
                    {
                        var lv = Group(ru, "Lv" + l);
                        ResetToPrefabFrame(lv, contents.transform);
                        build(lv, l);
                        CheckReach(stem, l, lv);
                    }
                    ru.gameObject.SetActive(true);
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabPath, out bool ok);
                    if (!ok) throw new Exception("SaveAsPrefabAsset failed for " + prefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                Report.Add($"OK {stem}: Runai/Lv1..{levels} written into {prefabPath}");
                if (levelSoFormat != null)
                    for (int l = 1; l <= levels; l++)
                    {
                        string p = string.Format(levelSoFormat, l);
                        if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(p) != null) ClearLevelPrefab(p);
                    }
            }
            catch (Exception e) { Fail(stem, e.ToString()); }
        }

        /// <summary>A Runai-only building's own variant prefab: Lv0 construction
        /// frame + Runai/Lv1..3, the SO re-pointed at it.</summary>
        static void RuVariant(string prefabPath, string soPath, string stem, float size, Action<Transform, int> build)
        {
            try
            {
                var root = new GameObject(stem);
                RuFrame(Group(root.transform, "Lv0"), size);
                var ru = Group(root.transform, "Runai");
                for (int l = 1; l <= 3; l++) { var lv = Group(ru, "Lv" + l); build(lv, l); CheckReach(stem, l, lv); }
                var prefab = Save(root, prefabPath);
                WireBuildingSo(soPath, prefab);
                Report.Add($"OK {stem}: variant prefab -> {prefabPath}");
            }
            catch (Exception e) { Fail(stem, e.ToString()); }
        }

        /// <summary>Lv0 under construction: a sandstone plinth, four columns, a
        /// timber scaffold beam — numbered for the construction rise.</summary>
        static void RuFrame(Transform lv0, float size)
        {
            float h = size * 0.5f - 0.6f;
            RuBox(Group(lv0, "1_Plinth"), "Plinth", Vector3.zero, new Vector3(size, 0.4f, size), 0f, _ruSand);
            var cols = Group(lv0, "2_Columns");
            foreach (var (px, pz) in new[] { (-h, -h), (h, -h), (h, h), (-h, h) })
                Column(cols, "Post", new Vector3(px, 0.4f, pz), size * 0.35f, 0.5f);
            var beams = Group(lv0, "3_Scaffold");
            Put(beams, C_PanelWood, "BeamA", new Vector3(h, size * 0.35f + 0.4f, -h - 0.15f), 0f, new Vector3(2f * h / 5f, 0.4f, 1f), Style.Neutral);
            Put(beams, C_PanelWood, "BeamB", new Vector3(-h, size * 0.35f + 0.4f, h + 0.15f), 180f, new Vector3(2f * h / 5f, 0.4f, 1f), Style.Neutral);
        }

        // ═════════════════════════════════════════════════════════════════
        // The buildings (front = -Z, the side the camera sees)
        // ═════════════════════════════════════════════════════════════════

        /// <summary>The Runai Fortress (10 m): a domed palace behind a pointed
        /// iwan, minarets at the corners, silk at its flanks; L3 adds a pavilion
        /// hanging from balloons over the court.</summary>
        static void BuildRuFortress(Transform lv, int level)
        {
            RuYard(lv, 10f);
            float plinth = level == 1 ? 8.4f : 9.4f;
            RuBox(lv, "Plinth", new Vector3(0f, 0.2f, 0f), new Vector3(plinth, 0.6f, plinth), 0f, _ruSand);
            const float y = 0.8f;
            float hw = level == 1 ? 6f : 7f, hh = level == 1 ? 3.2f : 3.8f, hd = level == 1 ? 5.4f : 6.0f;
            RuBox(lv, "Hall", new Vector3(0f, y, 0.6f), new Vector3(hw, hh, hd), 0f, _ruStucco);
            // A parapet of purple-capped merlons along the hall's top edge.
            for (int i = 0; i < 6; i++)
            {
                float x = -hw * 0.5f + 0.4f + i * (hw - 0.8f) / 5f;
                RuBox(lv, "Merlon" + Letter(i), new Vector3(x, y + hh, 0.6f - hd * 0.5f + 0.2f), new Vector3(0.45f, 0.45f, 0.35f), 0f, _ruPurple);
            }
            float dr = level == 1 ? 2.3f : level == 2 ? 2.7f : 2.8f;
            float drumH = level == 3 ? 1.9f : 0.9f;
            Drum(lv, "Drum", new Vector3(0f, y + hh, 0.8f), dr, drumH);
            Dome(lv, "GreatDome", new Vector3(0f, y + hh + drumH, 0.8f), dr + 0.15f, level == 1 ? 2.5f : 3.3f);

            // The iwan: a pointed portal proud of the facade, purple drapes in it.
            float iw = level == 1 ? 4.4f : 5f, ih = level == 1 ? 4.2f : 5.0f;
            float fz = 0.6f - hd * 0.5f - 0.35f;
            Arcade(lv, "Iwan", new Vector3(-iw * 0.5f, y, fz), new Vector3(iw * 0.5f, y, fz), ih, true);
            RuBox(lv, "IwanPylonW", new Vector3(-iw * 0.5f - 0.35f, y, fz), new Vector3(0.7f, ih + 0.6f, 0.9f), 0f, _ruSand);
            RuBox(lv, "IwanPylonE", new Vector3(iw * 0.5f + 0.35f, y, fz), new Vector3(0.7f, ih + 0.6f, 0.9f), 0f, _ruSand);
            RuBanner(lv, "PlayerColor_BannerW", new Vector3(-iw * 0.5f - 0.35f, y + ih - 0.2f, fz - 0.5f), 0f);
            RuBanner(lv, "PlayerColor_BannerE", new Vector3(iw * 0.5f + 0.35f, y + ih - 0.2f, fz - 0.5f), 0f);
            if (level >= 2)
                Silk(lv, R_Curtain, "IwanDrape", new Vector3(iw * 0.5f - 0.2f, y, fz + 0.75f), 0f, 0.9f, _ruPurple);

            // Minarets: two at the front at L1, four from L2, taller at L3.
            float mh = level == 1 ? 6.6f : level == 2 ? 8.4f : 10.2f;
            float mx = level == 1 ? 3.7f : 4.15f;
            Minaret(lv, "MinaretSW", -mx, -mx, y, mh);
            Minaret(lv, "MinaretSE", mx, -mx, y, mh);
            if (level >= 2)
            {
                Minaret(lv, "MinaretNW", -mx, mx, y, mh);
                Minaret(lv, "MinaretNE", mx, mx, y, mh);
                // Teal-glazed side kiosks.
                foreach (var (sx, nm) in new[] { (-3.1f, "KioskW"), (3.1f, "KioskE") })
                {
                    RuBox(lv, nm, new Vector3(sx, y, -1.6f), new Vector3(1.7f, 2.4f, 1.7f), 0f, _ruStucco);
                    Dome(lv, nm + "Dome", new Vector3(sx, y + 2.4f, -1.6f), 0.9f, 1.1f, _ruDomeTeal);
                }
            }
            // Silk at the flanks, pots at the steps.
            Silk(lv, R_Awning, "AwningW", new Vector3(-hw * 0.5f - 0.2f, y - 0.3f, 2.2f), 90f, 0.8f, _ruTeal);
            Silk(lv, R_Awning, "AwningE", new Vector3(hw * 0.5f + 0.2f, y - 0.3f, 2.2f), -90f, 0.8f, _ruTeal);
            foreach (var (px, nm) in new[] { (-1.4f, "PlanterW"), (1.4f, "PlanterE") })
                RuPut(lv, R_Planter, nm, new Vector3(px * 2.1f, y, fz - 0.9f));

            if (level == 3)
            {
                // The hanging pavilion: a stone platform carrying a silk pavilion,
                // held over the court by two balloons tethered to the minarets.
                var deck = new Vector3(-1.6f, 11.6f, 3.2f);
                Platform(lv, "SkyDeck", deck, 2.6f);
                Silk(lv, R_Pavilion, "SkyPavilion", deck + new Vector3(0f, 0.05f, 0f), 0f, 0.55f, _ruTeal);
                Balloon(lv, "BalloonA", deck + new Vector3(-0.9f, 4.6f, 0f), 1.25f, _ruPurple,
                        deck + new Vector3(-1.1f, 0.1f, 0f), new Vector3(-mx, mh + y - 0.3f, mx));
                Balloon(lv, "BalloonB", deck + new Vector3(1.0f, 4.0f, -0.4f), 1.0f, _ruTeal,
                        deck + new Vector3(1.1f, 0.1f, 0f));
            }
        }

        /// <summary>The Route Guard (the Barracks, 8 m): a walled drill court
        /// behind a gate of two pylons, a colonnaded barrack hall at the back.</summary>
        static void BuildRouteGuard(Transform lv, int level)
        {
            RuYard(lv, 8f);
            float hallH = level == 1 ? 2.9f : 3.5f;
            RuBox(lv, "Hall", new Vector3(0f, 0.2f, 2.5f), new Vector3(7.2f, hallH, 2.6f), 0f, _ruStucco);
            Colonnade(lv, "Portico", new Vector3(-3.2f, 0.2f, 0.85f), new Vector3(3.2f, 0.2f, 0.85f), level == 1 ? 4 : 5, hallH - 0.4f);
            Lintel(lv, "PorticoSlab", new Vector3(-3.4f, hallH - 0.2f + 0.2f, 1.55f), new Vector3(3.4f, hallH - 0.2f + 0.2f, 1.55f), 0.25f, 1.6f);
            Dome(lv, "HallDome", new Vector3(0f, 0.2f + hallH, 2.5f), level == 1 ? 1.1f : 1.4f, level == 1 ? 1.1f : 1.5f);
            // Court walls and the pylon gate.
            RuBox(lv, "WallW", new Vector3(-3.8f, 0.2f, -1.2f), new Vector3(0.45f, 1.5f, 4.6f), 0f, _ruSand);
            RuBox(lv, "WallE", new Vector3(3.8f, 0.2f, -1.2f), new Vector3(0.45f, 1.5f, 4.6f), 0f, _ruSand);
            RuBox(lv, "WallSW", new Vector3(-2.75f, 0.2f, -3.75f), new Vector3(2.4f, 1.5f, 0.45f), 0f, _ruSand);
            RuBox(lv, "WallSE", new Vector3(2.75f, 0.2f, -3.75f), new Vector3(2.4f, 1.5f, 0.45f), 0f, _ruSand);
            RuBox(lv, "PylonW", new Vector3(-1.3f, 0.2f, -3.75f), new Vector3(0.9f, 3.3f, 0.9f), 0f, _ruSand);
            RuBox(lv, "PylonE", new Vector3(1.3f, 0.2f, -3.75f), new Vector3(0.9f, 3.3f, 0.9f), 0f, _ruSand);
            Lintel(lv, "GateLintel", new Vector3(-1.3f, 3.5f, -3.75f), new Vector3(1.3f, 3.5f, -3.75f), 0.45f, 1.0f);
            RuBanner(lv, "PlayerColor_GateW", new Vector3(-1.3f, 3.3f, -4.25f), 0f);
            RuBanner(lv, "PlayerColor_GateE", new Vector3(1.3f, 3.3f, -4.25f), 0f);
            // The drill court.
            Put(lv, B_WeaponRack, "Rack", new Vector3(-2.6f, 0.2f, -1.3f), 90f, Vector3.one, Style.Runai);
            Put(lv, B_Dummy, "DummyA", new Vector3(1.6f, 0.2f, -1.8f), 200f, Vector3.one, Style.Runai);
            if (level >= 2)
            {
                Put(lv, B_Dummy, "DummyB", new Vector3(2.7f, 0.2f, -0.8f), 160f, Vector3.one, Style.Runai);
                Silk(lv, R_Awning, "CourtAwning", new Vector3(-2.3f, 0.2f, -2.6f), 0f, 0.7f, _ruTeal);
                RuBox(lv, "TowerE", new Vector3(3.15f, 0.2f, 3.0f), new Vector3(1.6f, 5.4f, 1.6f), 0f, _ruSand);
                Dome(lv, "TowerEDome", new Vector3(3.15f, 5.6f, 3.0f), 0.85f, 1.0f, _ruDomeTeal);
            }
            if (level == 3)
            {
                RuBox(lv, "TowerW", new Vector3(-3.15f, 0.2f, 3.0f), new Vector3(1.6f, 5.4f, 1.6f), 0f, _ruSand);
                Dome(lv, "TowerWDome", new Vector3(-3.15f, 5.6f, 3.0f), 0.85f, 1.0f, _ruDomeTeal);
                Balloon(lv, "Lookout", new Vector3(0f, 9.6f, 2.6f), 1.0f, _ruPurple,
                        new Vector3(-3.15f, 6.4f, 3.0f), new Vector3(3.15f, 6.4f, 3.0f));
            }
        }

        /// <summary>The Trading Post (the Gatherer's Hut, 4 m): a silk market
        /// stall and its goods; a domed kiosk at L2, a signal balloon at L3.</summary>
        static void BuildTradingPost(Transform lv, int level)
        {
            RuYard(lv, 4f);
            RuPut(lv, R_Stall, "Stall", new Vector3(-0.4f, 0.2f, 0.1f), 90f, Vector3.one * 0.78f);
            Silk(lv, R_Awning, "Awning", new Vector3(0.8f, 0.2f, -1.1f), 0f, 0.6f, _ruTeal);
            Put(lv, P_SackStack, "Sacks", new Vector3(1.2f, 0.2f, -0.6f), 30f, Vector3.one * 0.8f, Style.Runai);
            Put(lv, P_Crate, "Crate", new Vector3(1.3f, 0.2f, 0.6f), 10f, Vector3.one * 0.8f, Style.Runai);
            RuPut(lv, R_Pot, "PotA", new Vector3(-1.6f, 0.2f, -1.5f), 0f, Vector3.one * 1.6f);
            RuBanner(lv, "PlayerColor_Banner", new Vector3(-1.7f, 2.4f, -1.6f), 0f, 0.8f);
            if (level >= 2)
            {
                RuBox(lv, "Kiosk", new Vector3(1.0f, 0.2f, 1.2f), new Vector3(1.5f, 1.8f, 1.5f), 0f, _ruStucco);
                Dome(lv, "KioskDome", new Vector3(1.0f, 2.0f, 1.2f), 0.8f, 0.9f, level == 3 ? _ruSilver : _ruDomeTeal);
            }
            if (level == 3)
                Balloon(lv, "Sign", new Vector3(-0.9f, 5.0f, 0.9f), 0.7f, _ruPurple, new Vector3(-0.9f, 0.2f, 1.6f), new Vector3(1.0f, 3.6f, 1.2f));
        }

        /// <summary>The Temple (the Temple of Ridan for a Runai faction): an
        /// Egyptian-style temple — a pylon gate flanked by obelisks, a
        /// hypostyle hall of columns behind it, a teal-domed sanctum at the back.</summary>
        static void BuildRuTemple(Transform lv)
        {
            const float F = 6f;
            RuYard(lv, F);
            RuBox(lv, "Plinth", new Vector3(0f, 0.2f, 0.4f), new Vector3(5.6f, 0.4f, 5.2f), 0f, _ruSand);
            // The pylon: two battered towers either side of the gate.
            RuBox(lv, "PylonW", new Vector3(-1.75f, 0.2f, -2.4f), new Vector3(1.9f, 4.2f, 0.9f), 0f, _ruSand);
            RuBox(lv, "PylonE", new Vector3(1.75f, 0.2f, -2.4f), new Vector3(1.9f, 4.2f, 0.9f), 0f, _ruSand);
            RuBox(lv, "PylonCapW", new Vector3(-1.75f, 4.4f, -2.4f), new Vector3(2.1f, 0.3f, 1.05f), 0f, _ruPurple);
            RuBox(lv, "PylonCapE", new Vector3(1.75f, 4.4f, -2.4f), new Vector3(2.1f, 0.3f, 1.05f), 0f, _ruPurple);
            Lintel(lv, "GateLintel", new Vector3(-0.8f, 3.2f, -2.4f), new Vector3(0.8f, 3.2f, -2.4f), 0.5f, 0.9f);
            RuBanner(lv, "PlayerColor_BannerW", new Vector3(-1.75f, 3.9f, -2.9f), 0f, 0.9f);
            RuBanner(lv, "PlayerColor_BannerE", new Vector3(1.75f, 3.9f, -2.9f), 0f, 0.9f);
            RuPut(lv, R_Obelisk, "ObeliskW", new Vector3(-2.75f, 0.2f, -3.2f), 0f, new Vector3(0.38f, 0.95f, 0.38f), _ruStucco);
            RuPut(lv, R_Obelisk, "ObeliskE", new Vector3(2.75f, 0.2f, -3.2f), 0f, new Vector3(0.38f, 0.95f, 0.38f), _ruStucco);
            // The hypostyle hall: two rows of columns under a stone roof slab.
            Colonnade(lv, "HallW", new Vector3(-1.3f, 0.6f, -1.3f), new Vector3(-1.3f, 0.6f, 1.6f), 3, 2.8f, 0.5f);
            Colonnade(lv, "HallE", new Vector3(1.3f, 0.6f, -1.3f), new Vector3(1.3f, 0.6f, 1.6f), 3, 2.8f, 0.5f);
            RuBox(lv, "HallSlab", new Vector3(0f, 3.4f, 0.15f), new Vector3(3.6f, 0.3f, 3.4f), 0f, _ruSand);
            Silk(lv, R_Curtain, "Veil", new Vector3(1.0f, 0.6f, 1.7f), 0f, 0.5f, _ruPurple);
            // The sanctum.
            RuBox(lv, "Sanctum", new Vector3(0f, 0.6f, 2.35f), new Vector3(2.4f, 2.6f, 1.6f), 0f, _ruStucco);
            Dome(lv, "SanctumDome", new Vector3(0f, 3.7f, 2.35f), 1.0f, 1.2f, _ruDomeTeal);
            Put(lv, B_Brazier, "BrazierW", new Vector3(-0.9f, 0.6f, -1.8f), 0f, Vector3.one * 1.3f, Style.Runai);
            Put(lv, B_Brazier, "BrazierE", new Vector3(0.9f, 0.6f, -1.8f), 0f, Vector3.one * 1.3f, Style.Runai);
        }

        /// <summary>The Arrowyard (8 m): a covered colonnade gallery facing a
        /// target range; a loggia above at L2, an archers' tower and lookout balloon at L3.</summary>
        static void BuildArrowyard(Transform lv, int level)
        {
            RuYard(lv, 8f);
            RuBox(lv, "BackWall", new Vector3(0f, 0.2f, 3.6f), new Vector3(7.6f, 3.3f, 0.5f), 0f, _ruStucco);
            Colonnade(lv, "Gallery", new Vector3(-3.5f, 0.2f, 1.6f), new Vector3(3.5f, 0.2f, 1.6f), 6, 2.9f);
            RuBox(lv, "GallerySlab", new Vector3(0f, 3.1f, 2.6f), new Vector3(7.8f, 0.35f, 2.4f), 0f, _ruSand);
            Silk(lv, R_Awning, "AwningA", new Vector3(-2.0f, 0.3f, 1.0f), 180f, 0.8f, _ruTeal);
            Silk(lv, R_Awning, "AwningB", new Vector3(2.0f, 0.3f, 1.0f), 180f, 0.8f, _ruPurple);
            for (int i = 0; i < 3; i++)
                Put(lv, B_Target, "Target" + Letter(i), new Vector3(-2.4f + i * 2.4f, 0.2f, -3.3f), 180f, Vector3.one, Style.Runai);
            RuBanner(lv, "PlayerColor_Banner", new Vector3(-3.6f, 3.0f, 1.3f), 0f, 0.9f);
            if (level >= 2)
            {
                Colonnade(lv, "Loggia", new Vector3(-3.3f, 3.45f, 2.0f), new Vector3(3.3f, 3.45f, 2.0f), 5, 1.9f, 0.4f);
                RuBox(lv, "LoggiaSlab", new Vector3(0f, 5.35f, 2.7f), new Vector3(7.4f, 0.3f, 2.0f), 0f, _ruSand);
                Dome(lv, "LoggiaDomeW", new Vector3(-3.0f, 5.65f, 2.7f), 0.7f, 0.8f, _ruDomeTeal);
                Dome(lv, "LoggiaDomeE", new Vector3(3.0f, 5.65f, 2.7f), 0.7f, 0.8f, _ruDomeTeal);
            }
            if (level == 3)
            {
                RuBox(lv, "Tower", new Vector3(3.1f, 0.2f, -2.9f), new Vector3(1.7f, 6.2f, 1.7f), 0f, _ruSand);
                RuBox(lv, "TowerCrown", new Vector3(3.1f, 6.4f, -2.9f), new Vector3(2.1f, 0.45f, 2.1f), 0f, _ruPurple);
                Dome(lv, "TowerDome", new Vector3(3.1f, 6.85f, -2.9f), 0.9f, 1.1f);
                Balloon(lv, "Lookout", new Vector3(-0.8f, 9.4f, 2.2f), 1.0f, _ruTeal, new Vector3(-3.0f, 6.4f, 2.7f), new Vector3(3.0f, 6.4f, 2.7f));
            }
        }

        /// <summary>The Grazing Grounds (8 m): an arcaded stable at the back, a
        /// railed paddock with hay in front, the horse-master's silk pavilion.</summary>
        static void BuildGrazingGrounds(Transform lv, int level)
        {
            RuYard(lv, 8f);
            RuBox(lv, "StableBack", new Vector3(0f, 0.2f, 3.4f), new Vector3(7.6f, 3.0f, 0.6f), 0f, _ruStucco);
            Arcade(lv, "Stable", new Vector3(-3.7f, 0.2f, 1.5f), new Vector3(3.7f, 0.2f, 1.5f), 3.0f);
            RuBox(lv, "StableSlab", new Vector3(0f, 3.2f, 2.45f), new Vector3(7.8f, 0.35f, 2.4f), 0f, _ruSand);
            // Paddock rails.
            RuBox(lv, "RailW", new Vector3(-3.8f, 0.2f, -1.4f), new Vector3(0.2f, 1.0f, 5.0f), 0f, _ruSand);
            RuBox(lv, "RailE", new Vector3(3.8f, 0.2f, -1.4f), new Vector3(0.2f, 1.0f, 5.0f), 0f, _ruSand);
            RuBox(lv, "RailS", new Vector3(-1.9f, 0.2f, -3.85f), new Vector3(3.6f, 1.0f, 0.2f), 0f, _ruSand);
            Put(lv, P_HayPile, "Hay", new Vector3(2.4f, 0.2f, -2.4f), 0f, Vector3.one, Style.Runai);
            Put(lv, P_Hitching, "Hitch", new Vector3(0.3f, 0.2f, 0.4f), 0f, Vector3.one, Style.Runai);
            Silk(lv, R_Pavilion, "Pavilion", new Vector3(-1.9f, 0.2f, -1.8f), 0f, 0.62f, _ruPurple);
            RuBanner(lv, "PlayerColor_Banner", new Vector3(3.6f, 3.0f, 1.2f), 0f, 0.9f);
            if (level >= 2)
            {
                Drum(lv, "Drum", new Vector3(0f, 3.55f, 2.45f), 1.2f, 0.6f);
                Dome(lv, "StableDome", new Vector3(0f, 4.15f, 2.45f), 1.35f, 1.6f, _ruDomeTeal);
                Silk(lv, R_Awning, "Awning", new Vector3(2.4f, 0.2f, -0.6f), 180f, 0.7f, _ruTeal);
            }
            if (level == 3)
            {
                Minaret(lv, "Tower", -3.3f, 2.6f, 3.55f, 5.4f);
                Balloon(lv, "Herald", new Vector3(2.4f, 8.6f, 2.2f), 1.0f, _ruTeal, new Vector3(3.6f, 3.55f, 3.2f), new Vector3(0f, 5.6f, 2.45f));
            }
        }

        /// <summary>The Wagonworks (8 m): a great silk canopy over a wagon on
        /// the slip, a balloon envelope laid out for sewing; a workshop hall at
        /// L2; a Sky Barge prototype riding its balloon at L3.</summary>
        static void BuildWagonworks(Transform lv, int level)
        {
            RuYard(lv, 8f);
            Silk(lv, R_Pavilion, "Canopy", new Vector3(-1.4f, 0.2f, 0.9f), 0f, 1.45f, _ruTeal);
            Put(lv, V_Cart, "Wagon", new Vector3(-1.4f, 0.2f, 0.9f), 90f, Vector3.one * 0.8f, Style.Runai);
            // A balloon envelope half-filled on the slip, held down by ropes.
            Sphere(lv, "EnvelopeFill", new Vector3(2.1f, 1.25f, -2.5f), new Vector3(2.6f, 2.1f, 2.1f), _ruPurple);
            Rope(lv, "EnvelopeTieA", new Vector3(1.0f, 0.2f, -3.4f), new Vector3(1.6f, 1.9f, -2.9f));
            Rope(lv, "EnvelopeTieB", new Vector3(3.3f, 0.2f, -1.6f), new Vector3(2.8f, 1.9f, -2.1f));
            Put(lv, P_CrateStack, "Crates", new Vector3(-3.15f, 0.2f, -2.95f), 0f, Vector3.one * 0.8f, Style.Runai);
            // A crane: a column and a beam.
            Column(lv, "CraneMast", new Vector3(3.2f, 0.2f, 2.8f), 4.6f, 0.4f, _ruSand);
            Lintel(lv, "CraneBoom", new Vector3(3.2f, 4.6f, 2.8f), new Vector3(0.6f, 4.6f, 2.8f), 0.25f, 0.3f);
            Rope(lv, "CraneHook", new Vector3(0.9f, 2.4f, 2.8f), new Vector3(0.9f, 4.55f, 2.8f));
            RuBanner(lv, "PlayerColor_Banner", new Vector3(3.2f, 3.6f, 2.4f), 0f, 0.8f);
            if (level >= 2)
            {
                RuBox(lv, "Hall", new Vector3(2.6f, 0.2f, 0.4f), new Vector3(2.8f, 3.0f, 3.4f), 0f, _ruStucco);
                Arcade(lv, "HallArch", new Vector3(1.2f, 0.2f, -1.4f), new Vector3(4.0f, 0.2f, -1.4f), 2.6f);
                Dome(lv, "HallDome", new Vector3(2.6f, 3.2f, 0.4f), 1.2f, 1.3f, _ruDomeTeal);
            }
            if (level == 3)
            {
                var deck = new Vector3(-1.2f, 7.4f, -1.4f);
                Platform(lv, "BargeDeck", deck, 2.4f);
                RuBox(lv, "BargeHull", deck + new Vector3(0f, -0.55f, 0f), new Vector3(2.2f, 0.5f, 1.2f), 0f, _ruSand);
                Balloon(lv, "Barge", deck + new Vector3(0f, 3.4f, 0f), 1.4f, _ruPurple,
                        deck + new Vector3(-1.0f, 0.1f, 0f), deck + new Vector3(1.0f, 0.1f, 0f));
                Rope(lv, "Mooring", new Vector3(-3.3f, 0.2f, -3.3f), deck + new Vector3(-0.8f, -0.3f, -0.4f));
            }
        }

        /// <summary>The Sanctuary (4 m, atop a veilstone outcrop): a domed shrine
        /// (qubba) with purple decoration; a colonnade ring at L2; lantern
        /// balloons floating round it at L3.</summary>
        static void BuildSanctuary(Transform lv, int level)
        {
            RuYard(lv, 4f);
            RuBox(lv, "Plinth", new Vector3(0f, 0.2f, 0f), new Vector3(3.6f, 0.45f, 3.6f), 0f, _ruSand);
            RuBox(lv, "Shrine", new Vector3(0f, 0.65f, 0f), new Vector3(2.4f, 2.1f, 2.4f), 0f, _ruStucco);
            Arcade(lv, "Door", new Vector3(-0.8f, 0.65f, -1.25f), new Vector3(0.8f, 0.65f, -1.25f), 1.8f, true);
            float drum = level == 3 ? 0.9f : 0.4f;
            Drum(lv, "Drum", new Vector3(0f, 2.75f, 0f), 1.1f, drum);
            Dome(lv, "Dome", new Vector3(0f, 2.75f + drum, 0f), 1.3f, 1.5f, _ruDomeTeal);
            foreach (var (px, pz, nm) in new[] { (-1.55f, -1.55f, "ObeliskSW"), (1.55f, -1.55f, "ObeliskSE"), (1.55f, 1.55f, "ObeliskNE"), (-1.55f, 1.55f, "ObeliskNW") })
                RuPut(lv, R_Obelisk, nm, new Vector3(px, 0.65f, pz), 0f, new Vector3(0.3f, 0.42f, 0.3f), _ruPurple);
            RuPut(lv, R_Rug, "Rug", new Vector3(0f, 0.68f, -1.6f), 0f, new Vector3(0.6f, 1f, 0.6f), _ruPurple);
            RuBanner(lv, "PlayerColor_Banner", new Vector3(1.0f, 2.6f, -1.3f), 0f, 0.6f);
            if (level >= 2)
            {
                float r = 1.75f;
                var pts = new[] { new Vector3(-r, 0.65f, -r), new Vector3(r, 0.65f, -r), new Vector3(r, 0.65f, r), new Vector3(-r, 0.65f, r) };
                for (int i = 0; i < 4; i++) Colonnade(lv, "Ring" + Letter(i), pts[i], pts[(i + 1) % 4], 3, 2.2f, 0.32f);
                Silk(lv, R_Curtain, "Drape", new Vector3(0.95f, 0.65f, -r + 0.25f), 0f, 0.45f, _ruPurple);
            }
            if (level == 3)
            {
                Balloon(lv, "LanternW", new Vector3(-1.6f, 6.2f, -0.6f), 0.45f, _ruPurple, new Vector3(-1.75f, 2.9f, -1.75f));
                Balloon(lv, "LanternE", new Vector3(1.6f, 6.6f, 0.4f), 0.45f, _ruTeal, new Vector3(1.75f, 2.9f, 1.75f));
                Balloon(lv, "LanternN", new Vector3(-0.3f, 7.2f, 1.6f), 0.4f, _ruPurple, new Vector3(-1.75f, 2.9f, 1.75f));
            }
        }

        /// <summary>Thessara's Crossing (8 m landmark): the great domed market
        /// gate — a pointed iwan on every side, four tall minarets, silk stalls
        /// in the corners, two balloons riding from the minarets.</summary>
        static void BuildCrossing(Transform root)
        {
            RuYard(root, 8f);
            RuBox(root, "StepA", new Vector3(0f, 0.2f, 0f), new Vector3(8.2f, 0.4f, 8.2f), 0f, _ruSand);
            RuBox(root, "StepB", new Vector3(0f, 0.6f, 0f), new Vector3(6.8f, 0.4f, 6.8f), 0f, _ruSand);
            const float y = 1.0f;
            RuBox(root, "Core", new Vector3(0f, y, 0f), new Vector3(4.2f, 4.2f, 4.2f), 0f, _ruStucco);
            for (int i = 0; i < 4; i++)
            {
                float yaw = i * 90f;
                var q = Quaternion.Euler(0f, yaw, 0f);
                var a = q * new Vector3(-1.7f, 0f, -2.45f) + Vector3.up * y;
                var b = q * new Vector3(1.7f, 0f, -2.45f) + Vector3.up * y;
                Arcade(root, "Iwan" + Letter(i), a, b, 3.8f, true);
                Dome(root, "IwanDome" + Letter(i), q * new Vector3(0f, 0f, -1.9f) + Vector3.up * (y + 4.2f), 0.8f, 0.8f, _ruDomeTeal, false);
            }
            Drum(root, "Drum", new Vector3(0f, y + 4.2f, 0f), 2.1f, 1.3f);
            for (int i = 0; i < 8; i++)
            {
                float ang = i * 45f * Mathf.Deg2Rad;
                RuBox(root, "DrumRib" + Letter(i), new Vector3(Mathf.Cos(ang) * 2.15f, y + 4.2f, Mathf.Sin(ang) * 2.15f),
                      new Vector3(0.25f, 1.35f, 0.25f), -i * 45f, _ruPurple);
            }
            Dome(root, "GreatDome", new Vector3(0f, y + 5.5f, 0f), 2.3f, 3.4f);
            foreach (var (mx, mz, nm) in new[] { (-3.3f, -3.3f, "MinaretSW"), (3.3f, -3.3f, "MinaretSE"), (3.3f, 3.3f, "MinaretNE"), (-3.3f, 3.3f, "MinaretNW") })
                Minaret(root, nm, mx, mz, 0.2f, 11f);
            RuPut(root, R_Stall, "StallW", new Vector3(-3.0f, 1.0f, -0.4f), 90f, Vector3.one * 0.55f);
            RuPut(root, R_Stall, "StallE", new Vector3(3.0f, 1.0f, 0.4f), -90f, Vector3.one * 0.55f);
            Silk(root, R_Awning, "AwningS", new Vector3(-2.6f, 1.0f, -3.0f), 0f, 0.55f, _ruTeal);
            Silk(root, R_Awning, "AwningN", new Vector3(2.6f, 1.0f, 3.0f), 180f, 0.55f, _ruPurple);
            RuBanner(root, "PlayerColor_BannerW", new Vector3(-1.9f, 4.6f, -2.75f), 0f);
            RuBanner(root, "PlayerColor_BannerE", new Vector3(1.9f, 4.6f, -2.75f), 0f);
            Balloon(root, "BalloonW", new Vector3(-2.4f, 15.2f, 2.4f), 1.2f, _ruPurple, new Vector3(-3.3f, 8.9f, 3.3f));
            Balloon(root, "BalloonE", new Vector3(2.6f, 14.2f, -2.2f), 1.0f, _ruTeal, new Vector3(3.3f, 8.9f, -3.3f));
        }

        /// <summary>The Waystation (the Runai Hut, 4 m): a road tower — sandstone
        /// shaft, arched door, silk awning and a lantern under a small dome;
        /// taller with a balcony at L2, a signal balloon riding above it at L3.</summary>
        static void BuildWaystation(Transform root, int level)
        {
            RuYard(root, 4f);
            float h = level == 1 ? 4.6f : level == 2 ? 6.0f : 7.0f;
            RuBox(root, "Base", new Vector3(0f, 0.2f, 0f), new Vector3(2.4f, 0.5f, 2.4f), 0f, _ruSand);
            RuBox(root, "Shaft", new Vector3(0f, 0.7f, 0f), new Vector3(1.7f, h, 1.7f), 0f, _ruStucco);
            Arcade(root, "Door", new Vector3(-0.55f, 0.7f, -0.9f), new Vector3(0.55f, 0.7f, -0.9f), 1.7f);
            RuBox(root, "Crown", new Vector3(0f, 0.7f + h, 0f), new Vector3(2.2f, 0.4f, 2.2f), 0f, _ruPurple);
            for (int i = 0; i < 4; i++)
            {
                var q = Quaternion.Euler(0f, i * 90f, 0f);
                RuBox(root, "Merlon" + Letter(i), q * new Vector3(0.8f, 0f, -0.8f) + Vector3.up * (1.1f + h), new Vector3(0.4f, 0.45f, 0.4f), 0f, _ruSand);
            }
            Sphere(root, "Lantern", new Vector3(0f, 1.45f + h, 0f), Vector3.one * 0.45f, _ruGlow);
            Dome(root, "Dome", new Vector3(0f, 1.6f + h, 0f), 0.7f, 0.8f, level == 1 ? _ruDomeTeal : null);
            Silk(root, R_Awning, "Awning", new Vector3(0.9f, 0.2f, -1.0f), 0f, 0.5f, _ruTeal);
            RuBanner(root, "PlayerColor_Banner", new Vector3(-0.95f, 0.7f + h * 0.75f, -0.95f), 45f, 0.7f);
            if (level >= 2)
            {
                Platform(root, "Balcony", new Vector3(0f, 0.7f + h * 0.62f, 0f), 2.3f);
                Silk(root, R_Curtain, "Drape", new Vector3(0.9f, 0.7f + h * 0.62f - 1.7f, -0.86f), 0f, 0.36f, _ruPurple);
            }
            if (level == 3)
                Balloon(root, "Signal", new Vector3(0.4f, 1.6f + h + 3.6f, 0.4f), 0.85f, _ruPurple, new Vector3(0.8f, 1.1f + h, 0.8f));
        }

        /// <summary>The Runai Mine (4 m): a sandstone pithead round the shaft
        /// under a silk awning, ore crates and a hand cart; a headframe and a
        /// domed hut at L2; a balloon hoist over the shaft at L3.</summary>
        static void BuildRuMine(Transform root, int level)
        {
            RuYard(root, 4f);
            RuBox(root, "CurbN", new Vector3(0f, 0.2f, 1.0f), new Vector3(2.6f, 0.7f, 0.4f), 0f, _ruSand);
            RuBox(root, "CurbW", new Vector3(-1.1f, 0.2f, 0f), new Vector3(0.4f, 0.7f, 2.4f), 0f, _ruSand);
            RuBox(root, "CurbE", new Vector3(1.1f, 0.2f, 0f), new Vector3(0.4f, 0.7f, 2.4f), 0f, _ruSand);
            Silk(root, R_Awning, "Awning", new Vector3(0f, 0.2f, -0.9f), 0f, 0.65f, _ruTeal);
            Put(root, P_OreIron, "Ore", new Vector3(-1.4f, 0.2f, -1.5f), 20f, Vector3.one * 0.9f, Style.Runai);
            Put(root, P_Crate, "Crate", new Vector3(1.5f, 0.2f, -1.4f), -10f, Vector3.one * 0.7f, Style.Runai);
            RuBanner(root, "PlayerColor_Banner", new Vector3(1.3f, 1.9f, 1.25f), 0f, 0.6f);
            if (level >= 2)
            {
                Column(root, "FrameW", new Vector3(-0.9f, 0.9f, 0.3f), 3.0f, 0.3f, _ruSand);
                Column(root, "FrameE", new Vector3(0.9f, 0.9f, 0.3f), 3.0f, 0.3f, _ruSand);
                Lintel(root, "FrameBeam", new Vector3(-0.9f, 3.9f, 0.3f), new Vector3(0.9f, 3.9f, 0.3f), 0.3f, 0.35f);
                Rope(root, "HoistRope", new Vector3(0f, 1.0f, 0.3f), new Vector3(0f, 3.9f, 0.3f));
                RuBox(root, "Hut", new Vector3(-1.2f, 0.2f, 1.25f), new Vector3(1.2f, 1.6f, 1.2f), 0f, _ruStucco);
                Dome(root, "HutDome", new Vector3(-1.2f, 1.8f, 1.25f), 0.65f, 0.7f, _ruDomeTeal);
            }
            if (level == 3)
                Balloon(root, "Hoist", new Vector3(0f, 7.6f, 0.3f), 0.95f, _ruPurple, new Vector3(-0.9f, 4.0f, 0.3f), new Vector3(0.9f, 4.0f, 0.3f));
        }

        // ═════════════════════════════════════════════════════════════════
        // Previews
        // ═════════════════════════════════════════════════════════════════

        [MenuItem("Waning Border/Art/Render Runai Level Comparisons")]
        public static void RenderRunaiComparisons()
        {
            string outDir = Path.GetFullPath(RuPreviewDir);
            Directory.CreateDirectory(outDir);
            var report = new List<string>();
            void Variant(string name, string prefabPath, string soPath)
            {
                try { report.Add(RenderComparison(name, outDir, FootprintOf(soPath), l => ShownRunai(prefabPath, l))); }
                catch (Exception e) { report.Add($"FAIL {name}: {e.Message}"); }
            }
            void Ladder(string name, string dir, string stem, string soPath)
            {
                try { report.Add(RenderComparison(name, outDir, FootprintOf(soPath), l => Instance($"{dir}{stem}_{l}.prefab"))); }
                catch (Exception e) { report.Add($"FAIL {name}: {e.Message}"); }
            }
            Variant("RuFortress", FortressPrefabPath, TT + "Age0/Buildings/Fortress/Fortress.asset");
            Variant("RouteGuard", SoPrefabPath(BarracksSoPath), BarracksSoPath);
            Variant("TradingPost", SoPrefabPath(TT + "Age0/Buildings/GatherersHut/GatherersHut.asset"), TT + "Age0/Buildings/GatherersHut/GatherersHut.asset");
            Variant("RunaiTemple", SoPrefabPath(TT + "Age0/Buildings/TempleOfRidan/TempleOfRidan.asset"), TT + "Age0/Buildings/TempleOfRidan/TempleOfRidan.asset");
            Variant("Arrowyard", RU + "Arrowyard/Arrowyard.prefab", RU + "Arrowyard/Arrowyard.asset");
            Variant("GrazingGrounds", RU + "GrazingGrounds/GrazingGrounds.prefab", RU + "GrazingGrounds/GrazingGrounds.asset");
            Variant("Wagonworks", RU + "SiegeWorkshop/Wagonworks.prefab", RU + "SiegeWorkshop/SiegeWorkshop.asset");
            Variant("Sanctuary", RU + "Sanctuary/Sanctuary.prefab", RU + "Sanctuary/Sanctuary.asset");
            Ladder("Waystation", RU + "Waystation/Art/", "Waystation", TT + "Age0/Buildings/Hut/Hut.asset");
            Variant("RunaiMine", SoPrefabPath(TT + "Age0/Buildings/Mine/Mine.asset"), TT + "Age0/Buildings/Mine/Mine.asset");
            try
            {
                string crossing = RU + "ThessarasCrossing/ThessarasCrossing.prefab";
                report.Add(RenderComparison("Crossing", outDir, FootprintOf(TT + "Age0/Buildings/ThessarasCrossing/ThessarasCrossing.asset"), l => Instance(crossing)));
            }
            catch (Exception e) { report.Add("FAIL Crossing: " + e.Message); }
            string text = "[RunaiKit] level comparisons\n" + string.Join("\n", report);
            Debug.Log(text);
            try { File.WriteAllText(Path.Combine(outDir, "compare_report.txt"), text); } catch (Exception) { }
        }

        static GameObject ShownRunai(string prefabPath, int level)
        {
            var go = Instance(prefabPath);
            var v = TheWaningBorder.Rendering.BuildingVariantVisual.TrySetup(go);
            if (v == null) throw new Exception("no variant layout in " + prefabPath);
            v.ShowVariant(Cultures.Runai, level);
            return go;
        }
    }
}
