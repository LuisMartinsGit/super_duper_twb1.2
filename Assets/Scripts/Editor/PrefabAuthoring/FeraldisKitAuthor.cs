// FeraldisKitAuthor.cs
// Composes building art from the Synty POLYGON Fantasy Kingdom kit
// (2026-10-13, "use Synty as much as you can" — supersedes the Blender
// generators of FeraldisArtAuthor for the pieces authored here):
//
//   * the Age 0 SHELTER — Lv0 of Age0/Buildings/Fortress/Fortress.prefab:
//     a culture-neutral survivors' refuge (timber hall, lean-tos, firepit,
//     rough fence, crates), parts grouped 1_..N_ for the construction rise.
//     Default Synty palette (thatch is the kit's muted straw, not the plastic
//     yellow of the Blender one).
//   * the FERALDIS FORTRESS — Feraldis/Lv1..3 of that prefab: a stronghold
//     from level 1 (stone plinth, two-storey dark timber hall under a steep
//     shingle roof, antler gables, timber watch towers, braziers, stake
//     lines, chains), growing a ridge tower, gallows and chimney at 2 and a
//     spired keep, wings and more iron at 3.
//   * the FERALDIS LONGHOUSE — Feraldis/Lv1..3 of the Age 0 Barracks prefab
//     (the Barracks becomes the Longhouse at age-up): a great Viking hall
//     with hung shields, weapon racks, antlers; an annex at 2, a watch tower
//     and great flag at 3. The Feraldis Barracks level SOs' `prefab` is
//     cleared so the in-prefab branch is the only path.
//   * the FERALDIS WALL set — Civs/Feraldis/Buildings/Wall/Timber/: a heavy
//     log wall (stake faces on a rubble core, timber walk, chains, cheval de
//     frise), log blockhouse hub, log gatehouse with swinging doors, wall
//     tower and engine mount — bound into WallModuleArt.asset `cultureSets`
//     for Cultures.Feraldis.
//
// Palette: the kit's 04_B atlas (dark slate plaster, dark brown tile,
// purple cloth) through Fer_Kit_Dark, Wall_Dungeon stone darkened through
// Fer_Kit_Stone, Roof_Slat_02 darkened through Fer_Kit_Shingle (deliberately
// NOT named *roof* — BuildingFactionColorMarker paints *roof* nodes flat
// slate and tints *Castle_Roof* materials in the player colour). The player
// colour is cloth: Fer_PlayerColor_Cloth / Age0_PlayerColor_Cloth on the
// banners (the named slot that switches the atlas hue-swap off).
//
// Alanthor's Fortress branch is never touched. No Feraldis piece reuses an
// Alanthor composition: Alanthor is the stone castle kit (blocks, round
// towers, slate cones); Feraldis is timber, stakes, shingles and iron.
//
// Every nested kit instance is renamed digit-free: BuildingRiseData takes
// the FIRST digit run anywhere in a name as the rise label, so a stray
// "SM_Bld_House_Wall_01" would rise in slot 1 whatever group it sits in.
//
// Run: Waning Border > Art > Author Feraldis Kit Art (idempotent), then
// Waning Border > Art > Render Feraldis Kit Previews -> Temp/feraldis-kit-previews/.
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
        const string KitRoot = "Assets/Synty/PolygonFantasyKingdom/Prefabs/";
        const string KitTex = "Assets/Synty/PolygonFantasyKingdom/Textures/";
        const string TT = "Assets/GameData/TechTree/";
        const string FER = TT + "Civs/Feraldis/Buildings/";
        const string FerMatDir = FER + "Art/Materials/";
        const string Age0MatDir = TT + "Age0/Buildings/Fortress/Art/Materials/";
        const string FortressPrefabPath = TT + "Age0/Buildings/Fortress/Fortress.prefab";
        const string BarracksSoPath = TT + "Age0/Buildings/Barracks/Barracks.asset";
        const string WallDir = FER + "Wall/Timber/";
        const string WallArtConfigPath = TT + "Age0/Buildings/Wall/WallModuleArt.asset";
        const string BuildingLitShader = "TheWaningBorder/BuildingLit";
        const string ReportPath = "Temp/feraldis-kit-report.txt";
        const string PreviewDir = "Temp/feraldis-kit-previews";

        // ── kit pieces (paths under Prefabs/, no extension) ──
        const string H_Wall = "Buildings/House/SM_Bld_House_Wall_01";
        const string H_WallBeams = "Buildings/House/SM_Bld_House_Wall_SupportBeams_01";
        const string H_WallWin = "Buildings/House/SM_Bld_House_Wall_Window_01";
        const string H_WallDoor = "Buildings/House/SM_Bld_House_Wall_Door_01";
        const string H_Peak = "Buildings/House/SM_Bld_House_Wall_Peak_01";
        const string H_Base = "Buildings/House/SM_Bld_House_Base_Wall_01";
        const string H_Thatch = "Buildings/House/SM_Bld_House_Roof_Thatch_01";
        const string H_ThatchCap = "Buildings/House/SM_Bld_House_Roof_Thatch_Peak_Cap_01";
        const string H_Beam = "Buildings/House/SM_Bld_House_WoodBeam_01";
        const string H_FloorStone = "Buildings/House/SM_Bld_House_Floor_Stone_01";
        const string H_Chimney = "Buildings/House/SM_Bld_House_Chimney_Stone_01";
        const string C_RoofS = "Castle/SM_Bld_Castle_Roof_S_01";
        const string C_RoofSEnd = "Castle/SM_Bld_Castle_Roof_S_End_01";
        const string C_Block = "Castle/SM_Bld_Castle_Wall_Block_01";
        const string C_PillarRound = "Castle/SM_Bld_Castle_Pillar_Wood_Round_01";
        const string C_PillarSq = "Castle/SM_Bld_Castle_Pillar_Wood_02";
        const string C_FloorWood = "Castle/SM_Bld_Castle_Floor_Wood_01";
        const string C_HoardWall = "Castle/SM_Bld_Castle_Hoarding_Wood_Wall_01";
        const string C_PanelWood = "Castle/SM_Bld_Castle_Wall_Panel_Wood_01";
        const string C_DoorSingle = "Castle/SM_Bld_Castle_Door_Single_01";
        const string C_CapSquare = "Castle/SM_Bld_Castle_Roof_Cap_Square_05";
        const string B_SpikeWall1 = "Props/BattleGround/SM_Prop_Spike_Wall_01";
        const string B_SpikeWall2 = "Props/BattleGround/SM_Prop_Spike_Wall_02";
        const string B_SpikeFort = "Props/BattleGround/SM_Prop_Spike_Fortification_01";
        const string B_Brazier = "Props/BattleGround/SM_Prop_Camp_Brazier_01";
        const string B_Firepit = "Props/BattleGround/SM_Prop_Camp_Firepit_01";
        const string B_WeaponRack = "Props/BattleGround/SM_Prop_Camp_Weapon_Rack_01";
        const string B_Dummy = "Props/BattleGround/SM_Prop_Dummy_Armour_01";
        const string P_Fence = "Props/SM_Prop_Fence_03";
        const string P_FenceGate = "Props/SM_Prop_Fence_Gate_01";
        const string P_Log = "Props/SM_Prop_Log_01";
        const string P_GroundLogs = "Props/SM_Prop_Ground_Logs_01";
        const string P_CrateStack = "Props/SM_Prop_Crate_Stack_01";
        const string P_BarrelStack = "Props/SM_Prop_Barrel_Stack_01";
        const string P_WoodPile = "Props/SM_Prop_Wood_Pile_01";
        const string P_Torch = "Props/SM_Prop_Torch_01";
        const string P_Torch2 = "Props/SM_Prop_Torch_02";
        const string P_Chain = "Props/SM_Prop_Chain_Hanging_01";
        const string P_ChainDrop = "Props/SM_Prop_Chain_Hanging_04";
        const string P_Antler = "Props/SM_Prop_Animal_Head_Antler_01";
        const string P_DeerHead = "Props/SM_Prop_Animal_Head_Deer_01";
        const string P_Skull = "Props/SM_Prop_Candle_Skull_01";
        const string P_CageMan = "Props/SM_Prop_Cage_Man_01";
        const string P_Cauldron = "Props/SM_Prop_Cauldron_01";
        const string P_Gallows = "Props/SM_Prop_Gallows_Single_01";
        const string P_Awning = "Props/SM_Prop_Awning_01";
        const string P_Anvil = "Props/SM_Prop_Anvil_01";
        const string P_Pelt = "Props/SM_Prop_Animal_Pelt_01";
        const string BN_Battle = "Props/Banners/SM_Prop_Battle_Banner_01";
        const string BN_Flag = "Props/Banners/SM_Prop_Flag_01";
        const string BN_FlagH = "Props/Banners/SM_Prop_Flag_Horizontal_01";
        const string W_Shield = "Weapons/SM_Wep_Shield_05";
        const string W_Axe = "Weapons/SM_Wep_Axe_07";
        const string E_DeadTree = "Environments/SM_Env_Tree_Dead_01";
        const string E_Rock = "Environments/SM_Env_Rock_01";
        const string Bld_Tower1 = "Buildings/SM_Bld_Wooden_Tower_01";
        const string Bld_Tower2 = "Buildings/SM_Bld_Wooden_Tower_02";
        const string FX_Smoke = "FX/FX_Smoke_Chimney_01";
        const string DB_Pile = "Props/DeadBodies/SM_Prop_Dead_Body_Pile_01";
        const string B_Target = "Props/BattleGround/SM_Prop_Target_01";
        const string P_BloodPool = "Props/SM_Prop_BloodPool_01";
        const string P_MeatHanger = "Props/SM_Prop_Meat_Hanger_01_Preset";
        const string P_PigHanging = "Props/SM_Prop_Pig_Hanging_01";
        const string P_StoneTable = "Props/Furniture/SM_Prop_Table_Stone_01";
        const string P_Forge2 = "Props/SM_Prop_Forge_02";
        const string P_Bellows = "Props/SM_Prop_Bellows_01";
        const string P_GrindWheel = "Props/SM_Prop_Grinding_Wheel_01";
        const string P_Workbench = "Props/SM_Prop_Workbench_Forge_01_Preset";
        const string P_Cage2 = "Props/SM_Prop_Cage_02";
        const string P_HayPile = "Props/SM_Prop_Hay_Pile_01";
        const string P_Hitching = "Props/SM_Prop_Horse_Hitching_Post_01";
        const string P_SackStack = "Props/SM_Prop_Sack_Stack_01";
        const string P_OreIron = "Props/SM_Prop_Ore_Iron_01";
        const string P_Crate = "Props/SM_Prop_Crate_Wood_01";
        const string B_Relic = "Props/BattleGround/SM_Prop_Battle_Relic_01";
        const string B_Spit = "Props/BattleGround/SM_Prop_Spit_Roaster_01";
        const string SE_Catapult = "SiegeEngines/SM_Wep_Catapult_01";
        const string SE_Ballista = "SiegeEngines/SM_Wep_Ballista_Mobile_01";
        const string SE_Ram = "SiegeEngines/SM_Wep_Rammer_01";
        const string V_Cart = "Vehicles/SM_Veh_Cart_02";
        const string G_Stump = "Generic/SM_Generic_TreeStump_01";

        /// <summary>House kit module: 2.5 m, pivot at the +X end, running to -X.</summary>
        const float W = 2.5f;

        enum Style { Neutral, Feraldis, Runai }

        static readonly List<string> Report = new List<string>();
        static int _failures;
        static Material _kitDark, _kitStone, _kitShingle, _ferCloth, _age0Cloth;
        static readonly HashSet<string> _wallPieces = new HashSet<string>();
        static bool _collectWall;

        // ═════════════════════════════════════════════════════════════════
        [MenuItem("Waning Border/Art/Author Feraldis Kit Art")]
        public static void AuthorAll()
        {
            Report.Clear(); _failures = 0;
            try
            {
                EnsureMaterials(); EnsureIdentityMaterials();
                Fortress();
                Longhouse();
                WallSet();
                OtherBuildings();
                Age0Buildings();
                Landmarks();
            }
            catch (Exception e) { Fail("AuthorAll", e.ToString()); }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string text = $"[FeraldisKit] done — {Report.Count} entries, {_failures} failure(s)\n" + string.Join("\n", Report);
            Debug.Log(text);
            try { File.WriteAllText(ReportPath, text); } catch (Exception) { }
        }

        // ═════════════════════════════════════════════════════════════════
        // Materials
        // ═════════════════════════════════════════════════════════════════

        static void EnsureMaterials()
        {
            var shader = Shader.Find(BuildingLitShader);
            if (shader == null) throw new Exception("shader missing: " + BuildingLitShader);
            Directory.CreateDirectory(FerMatDir);
            _kitDark = KitMat(shader, "Fer_Kit_Dark", KitTex + "Alts/PolygonFantasyKingdom_04_B.png", new Color(0.92f, 0.88f, 0.86f), 0.12f);
            _kitStone = KitMat(shader, "Fer_Kit_Stone", KitTex + "Castle/Wall_Dungeon_01.png", new Color(0.52f, 0.52f, 0.56f), 0.1f);
            _kitShingle = KitMat(shader, "Fer_Kit_Shingle", KitTex + "Castle/Roof_Slat_02.png", new Color(0.30f, 0.29f, 0.31f), 0.15f);
            _ferCloth = AssetDatabase.LoadAssetAtPath<Material>(FerMatDir + "Fer_PlayerColor_Cloth.mat");
            _age0Cloth = AssetDatabase.LoadAssetAtPath<Material>(Age0MatDir + "Age0_PlayerColor_Cloth.mat");
            if (_ferCloth == null) throw new Exception("Fer_PlayerColor_Cloth.mat missing — run Author Feraldis Building Art once");
            if (_age0Cloth == null) throw new Exception("Age0_PlayerColor_Cloth.mat missing — run Author Feraldis Building Art once");
        }

        static Material KitMat(Shader shader, string name, string texPath, Color tint, float smoothness)
        {
            string path = FerMatDir + name + ".mat";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null) throw new Exception("kit texture missing: " + texPath);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader) { name = name }; AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != shader) m.shader = shader;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.SetColor("_EmissionColor", Color.black);
            m.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
            Report.Add("OK material " + path);
            return m;
        }

        // ═════════════════════════════════════════════════════════════════
        // Kit placement
        // ═════════════════════════════════════════════════════════════════

        static GameObject Load(string piece)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(KitRoot + piece + ".prefab");
            if (p == null) throw new Exception("missing kit piece " + piece);
            return p;
        }

        /// <summary>
        /// A kit piece as a nested prefab instance under <paramref name="parent"/>,
        /// renamed <paramref name="name"/> with every descendant made digit-free
        /// (the rise label rule), colliders dropped, materials restyled.
        /// </summary>
        static GameObject Put(Transform parent, string piece, string name, Vector3 pos, float yaw = 0f,
                              Vector3? scale = null, Style style = Style.Feraldis, Vector3? euler = null,
                              Material forceMat = null)
        {
            var prefab = Load(piece);
            if (_collectWall) _wallPieces.Add(piece);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(parent, false);
            go.name = name;
            go.transform.localPosition = pos;
            var e = euler ?? Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(e.x, yaw + e.y, e.z);
            go.transform.localScale = scale ?? Vector3.one;
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(col, true);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t == go.transform) continue;
                string clean = Regex.Replace(t.name, "[0-9]+", "");
                if (clean != t.name) t.name = clean;
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    Material to = forceMat;
                    if (to == null && style == Style.Feraldis)
                    {
                        if (m.name.StartsWith("PolygonFantasyKingdom_Mat", StringComparison.OrdinalIgnoreCase)) to = _kitDark;
                        else if (m.name.StartsWith("Castle_Wall", StringComparison.OrdinalIgnoreCase)) to = _kitStone;
                        else if (m.name.StartsWith("Castle_Roof", StringComparison.OrdinalIgnoreCase)) to = _kitShingle;
                    }
                    else if (to == null && style == Style.Runai) to = RunaiRestyle(m);
                    if (to != null && to != m) { mats[i] = to; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            return go;
        }

        /// <summary>A 2.5 m house-kit piece (pivot at +X end, running to -X)
        /// laid from <paramref name="a"/> to <paramref name="b"/>: pivot at b,
        /// its -X end at a. The piece's -Z face is the outside.</summary>
        static GameObject Span(Transform parent, string piece, string name, Vector3 a, Vector3 b, float module,
                               Style style = Style.Feraldis, float heightScale = 1f)
        {
            Vector3 d = (a - b).normalized;
            float yaw = Mathf.Atan2(d.z, -d.x) * Mathf.Rad2Deg;
            float len = Vector3.Distance(a, b);
            var go = Put(parent, piece, name, b, yaw, new Vector3(len / module, heightScale, 1f), style);
            return go;
        }

        /// <summary>
        /// Walls round a rectangle (centre cx,cz; half sizes hx,hz; base y), one
        /// piece per 2.5 m, outside faces outward. Sides are south, west, north,
        /// east; each side's pieces run clockwise seen from above.
        /// </summary>
        static void Ring(Transform parent, string tag, float cx, float cz, float hx, float hz, float y,
                         string[] south, string[] west, string[] north, string[] east, Style style = Style.Feraldis,
                         float heightScale = 1f)
        {
            Side(parent, tag + "S", new Vector3(cx + hx, y, cz - hz), new Vector3(cx - hx, y, cz - hz), south, style, heightScale);
            Side(parent, tag + "W", new Vector3(cx - hx, y, cz - hz), new Vector3(cx - hx, y, cz + hz), west, style, heightScale);
            Side(parent, tag + "N", new Vector3(cx - hx, y, cz + hz), new Vector3(cx + hx, y, cz + hz), north, style, heightScale);
            Side(parent, tag + "E", new Vector3(cx + hx, y, cz + hz), new Vector3(cx + hx, y, cz - hz), east, style, heightScale);
        }

        /// <summary>Pieces laid from <paramref name="from"/> to <paramref name="to"/>, each 2.5 m; the first piece sits at `from`.</summary>
        static void Side(Transform parent, string tag, Vector3 from, Vector3 to, string[] pieces, Style style, float heightScale)
        {
            Vector3 dir = (to - from).normalized;
            for (int i = 0; i < pieces.Length; i++)
            {
                if (string.IsNullOrEmpty(pieces[i])) continue;
                Vector3 b = from + dir * (W * i);
                Vector3 a = from + dir * (W * (i + 1));
                Span(parent, pieces[i], $"{tag}{Letter(i)}", a, b, W, style, heightScale);
            }
        }

        static string Letter(int i) => ((char)('a' + i)).ToString();

        static Transform Group(Transform parent, string name)
        {
            var g = new GameObject(name).transform;
            g.SetParent(parent, false);
            return g;
        }

        static GameObject Banner(Transform parent, string name, Vector3 pos, float yaw, float scale, Material cloth)
            => Put(parent, BN_Battle, name, pos, yaw, Vector3.one * scale, Style.Neutral, null, cloth);

        /// <summary>
        /// A gabled roof over a hall: ridge along X from x0 to x1 at ridgeY,
        /// eaves at eaveY, half-depth hz. Castle_Roof_S pieces (5 m run, 2.5 m
        /// deep, 5 m rise at scale 1) stretched to the hall. Pieces are 5 m
        /// along X with the pivot at the EAVE corner, rising toward -Z.
        /// </summary>
        static void SteepRoof(Transform parent, string tag, float x0, float x1, float z, float hz, float eaveY, float ridgeY,
                              Style style = Style.Feraldis)
        {
            float len = x1 - x0;
            float sx = len / 5f, sy = (ridgeY - eaveY) / 5f, sz = hz / 2.5f;
            // South slope (eave at z - hz, rising toward z).
            Put(parent, C_RoofS, tag + "SlopeS", new Vector3(x0, eaveY, z + hz), 0f, new Vector3(sx, sy, sz), style);
            // North slope: turned 180, so its run goes -X from x1 and it rises toward -z... mirrored to +z.
            Put(parent, C_RoofS, tag + "SlopeN", new Vector3(x1, eaveY, z - hz), 180f, new Vector3(sx, sy, sz), style);
            // Gable trims at both ends of both slopes.
            Put(parent, C_RoofSEnd, tag + "TrimSW", new Vector3(x0, eaveY, z + hz), 0f, new Vector3(1f, sy, sz), style);
            Put(parent, C_RoofSEnd, tag + "TrimSE", new Vector3(x1, eaveY, z + hz), 0f, new Vector3(1f, sy, sz), style);
            Put(parent, C_RoofSEnd, tag + "TrimNE", new Vector3(x1, eaveY, z - hz), 180f, new Vector3(1f, sy, sz), style);
            Put(parent, C_RoofSEnd, tag + "TrimNW", new Vector3(x0, eaveY, z - hz), 180f, new Vector3(1f, sy, sz), style);
        }

        /// <summary>A gable triangle (two mirrored Wall_Peak quarters): apex
        /// column at <paramref name="apexBase"/>, half-span hz, rise h, its
        /// outside facing the way a wall piece at <paramref name="yaw"/> faces
        /// (0 = -Z south, 180 = +Z, 90 = -X west, -90 = +X east).</summary>
        static void Gable(Transform parent, string tag, Vector3 apexBase, float hz, float h, float yaw,
                          Style style = Style.Feraldis)
        {
            float sx = hz / 1.25f, sy = h / 1.25f;
            // Peak quarter: vertical edge at its pivot, slope descending toward local -X.
            Put(parent, H_Peak, tag + "A", apexBase, yaw, new Vector3(sx, sy, 1f), style);
            Put(parent, H_Peak, tag + "B", apexBase, yaw, new Vector3(-sx, sy, 1f), style);
        }

        static void AntlerFinial(Transform parent, string name, Vector3 pos, float yaw, float scale)
            => Put(parent, P_Antler, name, pos, yaw, Vector3.one * scale, Style.Feraldis, new Vector3(-20f, 0f, 0f));

        /// <summary>A square timber mast of <paramref name="height"/> at
        /// <paramref name="foot"/> with the great player-colour flag (Flag_01
        /// hangs 5 m from its pivot) flying from its top.</summary>
        static void FlagMast(Transform parent, string tag, Vector3 foot, float height, float flagScale)
        {
            Put(parent, C_PillarSq, tag + "Mast", foot, 0f, new Vector3(0.3f, height / 5.05f, 0.3f));
            Put(parent, BN_Flag, "PlayerColor_" + tag, foot + new Vector3(0.1f, height - 0.1f, 0f), 0f, Vector3.one * flagScale, Style.Neutral, null, _ferCloth);
        }

        // ═════════════════════════════════════════════════════════════════
        // The Fortress: Lv0 Shelter + Feraldis/Lv1..3
        // ═════════════════════════════════════════════════════════════════

        static void Fortress()
        {
            try
            {
                var contents = PrefabUtility.LoadPrefabContents(FortressPrefabPath);
                try
                {
                    Transform container = FindContainerWithLv0(contents.transform, 2);
                    if (container == null) throw new Exception("no Lv0 container in " + FortressPrefabPath);

                    Transform lv0 = FindChild(container, "Lv0");
                    Clear(lv0);
                    BuildShelter(lv0);
                    lv0.gameObject.SetActive(true);

                    Transform al = FindChild(container, "Alanthor");
                    Report.Add(al != null ? $"OK Fortress: Alanthor branch untouched ({al.childCount} levels)" : "WARN Fortress: no Alanthor branch");

                    Transform fer = FindChild(container, "Feraldis") ?? Group(container, "Feraldis");
                    Clear(fer);
                    for (int l = 1; l <= 3; l++) { var lvn = Group(fer, "Lv" + l); BuildFeraldisFortress(lvn, l); CheckReach("Fortress", l, lvn); }
                    fer.gameObject.SetActive(true);

                    if (FindChild(container, "Runai") == null) Group(container, "Runai");

                    PrefabUtility.SaveAsPrefabAsset(contents, FortressPrefabPath, out bool ok);
                    if (!ok) throw new Exception("SaveAsPrefabAsset failed for " + FortressPrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                Report.Add("OK Fortress: Lv0 Shelter + Feraldis/Lv1..3 rebuilt from the kit -> " + FortressPrefabPath);
                for (int l = 1; l <= 3; l++)
                {
                    ClearLevelPrefab(FER + $"Fortress/Fortress_Lvl{l}.asset");
                    ClearLevelPrefab(TT + $"Civs/Alanthor/Buildings/Fortress/Fortress_Lvl{l}.asset");
                }
            }
            catch (Exception e) { Fail("Fortress", e.ToString()); }
        }

        /// <summary>The Age 0 Shelter, 10 m footprint, culture-neutral.</summary>
        static void BuildShelter(Transform lv0)
        {
            var S = Style.Neutral;
            // 1 — ground: a cobbled patch under the hall and a few felled logs.
            var g1 = Group(lv0, "1_Ground");
            foreach (var (px, pz) in new[] { (0f, 0f), (W, 0f), (0f, W), (W, W) })
                Put(g1, H_FloorStone, "Cobbles", new Vector3(px, 0.0f, pz), 0f, null, S);
            Put(g1, P_GroundLogs, "FelledLogs", new Vector3(3.6f, 0.3f, 3.4f), 35f, new Vector3(0.45f, 0.6f, 0.45f), S);

            // 2 — hall walls, 5 x 5, door south, with corner posts.
            var g2 = Group(lv0, "2_Walls");
            Ring(g2, "Wall", 0f, 0f, W, W, 0f,
                 new[] { H_WallWin, H_WallDoor }, new[] { H_WallBeams, H_Wall },
                 new[] { H_Wall, H_Wall }, new[] { H_WallWin, H_WallBeams }, S);
            foreach (var (px, pz) in new[] { (-W, -W), (W, -W), (W, W), (-W, W) })
                Put(g2, H_Beam, "Post", new Vector3(px, 0f, pz), 0f, new Vector3(1.2f, 1f, 1.2f), S);

            // 3 — gables (ridge along Z) and the thatch.
            var g3 = Group(lv0, "3_Roof");
            Gable(g3, "GableS", new Vector3(0f, W, -W), W, W, 0f, S);
            Gable(g3, "GableN", new Vector3(0f, W, W), W, W, 180f, S);
            // Thatch: pivot at the ridge, dropping toward local -X, running -Z.
            foreach (var pz in new[] { W, 0f })
            {
                Put(g3, H_Thatch, "ThatchW", new Vector3(0f, 2f * W, pz), 0f, null, S);
                Put(g3, H_Thatch, "ThatchE", new Vector3(0f, 2f * W, pz - W), 180f, null, S);
                Put(g3, H_ThatchCap, "Cap", new Vector3(0f, 2f * W, pz), 0f, null, S);
            }

            // 4 — thatched lean-tos on both flanks (the kit's cloth awning is
            // blue, which reads as a player colour, so it is not used).
            var g4 = Group(lv0, "4_Leantos");
            Put(g4, H_Thatch, "LeanToW", new Vector3(-2.55f, 2.45f, 1.5f), 0f, new Vector3(0.95f, 0.45f, 1.2f), S);
            Put(g4, H_Beam, "LeanPostWa", new Vector3(-4.85f, 0f, 1.4f), 0f, new Vector3(1f, 0.5f, 1f), S);
            Put(g4, H_Beam, "LeanPostWb", new Vector3(-4.85f, 0f, -1.4f), 0f, new Vector3(1f, 0.5f, 1f), S);
            Put(g4, H_Thatch, "LeanToE", new Vector3(2.55f, 2.45f, -0.6f), 180f, new Vector3(0.95f, 0.45f, 1.2f), S);
            Put(g4, H_Beam, "LeanPostEa", new Vector3(4.85f, 0f, 2.3f), 0f, new Vector3(1f, 0.5f, 1f), S);
            Put(g4, H_Beam, "LeanPostEb", new Vector3(4.85f, 0f, -0.5f), 0f, new Vector3(1f, 0.5f, 1f), S);
            Put(g4, P_CrateStack, "Crates", new Vector3(3.9f, 0f, 1.0f), 30f, new Vector3(0.8f, 0.8f, 0.8f), S);
            Put(g4, P_BarrelStack, "Barrels", new Vector3(-3.7f, 0f, -3.5f), 60f, new Vector3(0.7f, 0.7f, 0.7f), S);
            Put(g4, P_WoodPile, "Woodpile", new Vector3(3.9f, 0f, -3.9f), 10f, null, S);

            // 5 — the camp: firepit and log benches in front.
            var g5 = Group(lv0, "5_Camp");
            Put(g5, B_Firepit, "Firepit", new Vector3(0f, 0.1f, -4.0f), 0f, new Vector3(1.3f, 1.3f, 1.3f), S);
            Put(g5, P_Log, "BenchA", new Vector3(-1.9f, 0.25f, -3.9f), 100f, new Vector3(0.6f, 0.6f, 0.45f), S);
            Put(g5, P_Log, "BenchB", new Vector3(1.9f, 0.25f, -3.6f), 70f, new Vector3(0.6f, 0.6f, 0.45f), S);
            Put(g5, P_Torch2, "TorchA", new Vector3(1.3f, 1.9f, -2.62f), 0f, null, S);
            Put(g5, P_Torch2, "TorchB", new Vector3(-2.62f, 1.9f, 0.8f), 90f, null, S);

            // 6 — the rough fence, open to the south with a gate.
            var g6 = Group(lv0, "6_Fence");
            const float F = 4.8f, fy = 0.45f;
            Put(g6, P_Fence, "FenceSa", new Vector3(-3.6f, fy, -F), 0f, null, S);
            Put(g6, P_Fence, "FenceSb", new Vector3(3.6f, fy, -F), 0f, null, S);
            Put(g6, P_FenceGate, "FenceGate", new Vector3(1.32f, 0.05f, -F), 0f, null, S);
            foreach (var px in new[] { -3.6f, -1.2f, 1.2f, 3.6f }) Put(g6, P_Fence, "FenceN", new Vector3(px, fy, F), 0f, null, S);
            foreach (var pz in new[] { -3.6f, -1.2f, 1.2f, 3.6f })
            {
                Put(g6, P_Fence, "FenceW", new Vector3(-F, fy, pz), 90f, null, S);
                Put(g6, P_Fence, "FenceE", new Vector3(F, fy, pz), 90f, null, S);
            }

            // 7 — the pennant: the player-colour slot.
            var g7 = Group(lv0, "7_Pennant");
            Banner(g7, "PlayerColor_Banner", new Vector3(2.4f, 0f, -4.4f), 0f, 1.3f, _age0Cloth);
        }

        /// <summary>
        /// A square log tower: plank-walled storeys (2.2 m each) on a dark
        /// stone footing with corner logs, a stake parapet, and optionally a
        /// shingle spire with antlers. Used for keeps and watch towers.
        /// </summary>
        static float LogTower(Transform lv, string tag, float cx, float cz, float half, float baseY, int storeys,
                              bool spire, float spireScale = 1f)
        {
            Put(lv, C_Block, tag + "Core", new Vector3(cx, baseY, cz), 0f, new Vector3(2f * half / 5.5f, (storeys * 2.2f) / 5f, 2f * half / 5.5f));
            for (int side = 0; side < 4; side++)
            {
                float yaw = side * 90f;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                for (int s = 0; s < storeys; s++)
                    // Hoarding_Wood_Wall sits +1.63 m along its local Z from the pivot.
                    Put(lv, C_HoardWall, tag + "Planks", new Vector3(cx, baseY, cz) + rot * new Vector3(half, s * 2.2f, -(half + 1.63f)), yaw, new Vector3(2f * half / 2.5f, 1f, 1f));
            }
            foreach (var (px, pz) in new[] { (-half, -half), (half, -half), (half, half), (-half, half) })
                Put(lv, C_PillarSq, tag + "Log", new Vector3(cx + px, baseY, cz + pz), 0f, new Vector3(0.45f, (storeys * 2.2f + 0.8f) / 5.05f, 0.45f));
            float top = baseY + storeys * 2.2f;
            Put(lv, C_FloorWood, tag + "Deck", new Vector3(cx + half, top, cz + half), 0f, new Vector3(2f * half / 5f, 1f, 2f * half / 5f));
            for (int side = 0; side < 4; side++)
            {
                float yaw = side * 90f;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                Put(lv, B_SpikeWall1, tag + "Parapet", new Vector3(cx, top - 0.2f, cz) + rot * new Vector3(-half, 0f, -half + 0.2f), yaw, new Vector3(2f * half / 2.5f, 0.5f, 1.2f));
            }
            if (spire)
            {
                float sh = 2f * half * 1.25f * spireScale;
                Put(lv, C_CapSquare, tag + "Spire", new Vector3(cx, top + 0.6f, cz), 0f, new Vector3(2f * half * 1.05f / 2.74f, sh / 7.42f, 2f * half * 1.05f / 2.74f));
                AntlerFinial(lv, tag + "Antler", new Vector3(cx, top + 0.6f + sh, cz), 0f, 2.0f);
                return top + 0.6f + sh;
            }
            Put(lv, B_Brazier, tag + "Brazier", new Vector3(cx, top, cz), 0f, Vector3.one * 1.6f);
            return top;
        }

        // ═════════════════════════════════════════════════════════════════
        // The Longhouse: Feraldis/Lv1..3 inside the Barracks prefab
        // ═════════════════════════════════════════════════════════════════

        static void Longhouse()
        {
            try
            {
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(BarracksSoPath);
                if (so == null) throw new Exception("Barracks SO missing");
                var prop = new SerializedObject(so).FindProperty("prefab");
                var prefab = prop != null ? prop.objectReferenceValue as GameObject : null;
                string prefabPath = prefab != null ? AssetDatabase.GetAssetPath(prefab) : null;
                if (string.IsNullOrEmpty(prefabPath) || !prefabPath.EndsWith(".prefab")) throw new Exception("Barracks SO prefab is not a prefab: " + prefabPath);

                var contents = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    Transform container = FindContainerWithLv0(contents.transform, 2);
                    if (container == null) throw new Exception("no Lv0 container in " + prefabPath);
                    // Stray empty culture nodes OUTSIDE the container (the prefab
                    // carried "Runai" / "Feraldis" at its root, where the variant
                    // scanner never looks) are dropped.
                    foreach (var stray in new[] { "Runai", "Feraldis" })
                    {
                        var s = FindChild(contents.transform, stray);
                        if (s != null && s != container && s.childCount == 0) UnityEngine.Object.DestroyImmediate(s.gameObject);
                    }
                    Transform fer = FindChild(container, "Feraldis") ?? Group(container, "Feraldis");
                    Clear(fer);
                    // The container is the imported FBX node (x100, -90 deg):
                    // every level node is put back at the prefab's own frame.
                    for (int l = 1; l <= 3; l++)
                    {
                        var lv = Group(fer, "Lv" + l);
                        ResetToPrefabFrame(lv, contents.transform);
                        BuildLonghouse(lv, l);
                        CheckReach("Longhouse", l, lv);
                    }
                    fer.gameObject.SetActive(true);
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabPath, out bool ok);
                    if (!ok) throw new Exception("SaveAsPrefabAsset failed for " + prefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                Report.Add("OK Longhouse: Feraldis/Lv1..3 written into " + prefabPath);

                // The in-prefab branch is the only path: the Feraldis Barracks
                // level SOs (War Hall today, Longhouse once renamed) carry no model.
                for (int l = 1; l <= 3; l++)
                {
                    foreach (var p in new[] { FER + $"WarHall/WarHall_Lvl{l}.asset", FER + $"Longhouse/Longhouse_Lvl{l}.asset" })
                        if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(p) != null) ClearLevelPrefab(p);
                }
            }
            catch (Exception e) { Fail("Longhouse", e.ToString()); }
        }

        /// <summary>Make <paramref name="t"/>'s world pose the prefab root's
        /// (identity), whatever its parent's import scale and rotation.</summary>
        static void ResetToPrefabFrame(Transform t, Transform root)
        {
            t.position = root.position;
            t.rotation = root.rotation;
            var ls = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
        }

        /// <summary>A paved yard the size of the footprint: the constant ground
        /// bounds every level shares (see BuildFeraldisFortress).</summary>
        /// <remarks>Authored ONE metre wider than the footprint and everything
        /// else is kept inside it, so the yard — not whichever prop sticks out
        /// furthest at a given level — is what the footprint fit measures. The
        /// fit is then the same constant (F / (F+1)) at every level and a
        /// bigger hall at Lv2 really is drawn bigger.</remarks>
        static void Yard(Transform lv, float size, float h = 0.2f, Style style = Style.Feraldis)
            => Put(lv, C_Block, "Yard", Vector3.zero, 0f, new Vector3((size + 1f) / 5.5f, h / 5f, (size + 1f) / 5.5f), style);

        // ═════════════════════════════════════════════════════════════════
        // The Feraldis wall set (kit metres; the game draws it at 0.6)
        // ═════════════════════════════════════════════════════════════════

        /// <summary>The kit module: 5 m of wall = the game's 3 m module.</summary>
        const float M = 5f;
        /// <summary>Wall depth across the run, kit metres (2.4 m in game).</summary>
        const float LogDepth = 4.0f;
        /// <summary>The timber walk, kit metres (= AlanthorWall.DeckHeight / 0.6).</summary>
        const float Walk = 5f;

        static void WallSet()
        {
            try
            {
                Directory.CreateDirectory(WallDir);
                _wallPieces.Clear();
                _collectWall = true;
                var curtain = Save(BuildLogCurtain(), WallDir + "Curtain_Feraldis.prefab");
                var hub = Save(BuildLogHub(), WallDir + "Hub_Feraldis.prefab");
                var gate = Save(BuildLogGate(), WallDir + "Gate_Feraldis.prefab");
                var tower = Save(BuildLogTower(), WallDir + "Tower_Feraldis.prefab");
                var mount = Save(BuildLogMount(), WallDir + "Mount_Feraldis.prefab");
                _collectWall = false;
                MakeWallMeshesReadable();
                BindWallConfig(curtain, hub, gate, tower, mount);
                Report.Add("OK Feraldis wall set -> " + WallDir + " (bound in WallModuleArt.asset cultureSets[Feraldis])");
            }
            catch (Exception e) { _collectWall = false; Fail("WallSet", e.ToString()); }
        }

        /// <summary>
        /// One module of log wall from x0 to x1: a rubble core (the
        /// "Curtain_" part the fitter measures), heavy stake faces on both
        /// sides reaching above the walk as the parapet, the plank walk, a
        /// chain along the outer face.
        /// </summary>
        static void LogRun(Transform parent, string tag, float x0, float x1)
        {
            float len = x1 - x0, mid = (x0 + x1) * 0.5f, half = LogDepth * 0.5f;
            var core = Put(parent, C_Block, "Curtain_Core" + tag, new Vector3(mid, 0f, 0f));
            core.transform.localScale = new Vector3(len / 5.5f, (Walk - 0.2f) / 5f, (LogDepth - 1.0f) / 5.5f);
            // Stake faces: Spike_Wall_02 is 5 m long from its pivot along +X, 2.9 m tall.
            var sA = Put(parent, B_SpikeWall2, "StakesA" + tag, new Vector3(x0, 0f, -half + 0.3f), 0f, new Vector3(len / 5f, 2.15f, 1.5f));
            var sB = Put(parent, B_SpikeWall2, "StakesB" + tag, new Vector3(x1, 0f, half - 0.3f), 180f, new Vector3(len / 5f, 2.15f, 1.5f));
            // The walk: Castle_Floor_Wood is 5 x 5 with the pivot at its +X +Z corner.
            var deck = Put(parent, C_FloorWood, "Walk" + tag, new Vector3(x1, Walk, half - 0.55f), 0f, new Vector3(len / 5f, 1f, (LogDepth - 1.1f) / 5f));
            Put(parent, P_Chain, "ChainA" + tag, new Vector3(mid, Walk + 0.6f, -half - 0.1f), 0f, new Vector3(len / 2.55f * 0.9f, 1.2f, 1f));
            Put(parent, P_Chain, "ChainB" + tag, new Vector3(mid, Walk + 0.6f, half + 0.1f), 180f, new Vector3(len / 2.55f * 0.9f, 1.2f, 1f));
        }

        static GameObject BuildLogCurtain()
        {
            var root = new GameObject("Curtain_Feraldis");
            LogRun(root.transform, "", -M * 0.5f, M * 0.5f);
            // Cheval de frise at the foot of the OUTER face: the module's +X is
            // the enemy's side (WallGarrison: -X is the friendly face), and the
            // fitter turns kit +Z onto +X.
            Put(root.transform, B_SpikeFort, "Frise", new Vector3(0f, 0.66f, LogDepth * 0.5f + 1.2f), 0f, Vector3.one * 1.1f);
            var along = new GameObject("Along").transform;
            along.SetParent(root.transform, false);
            along.localRotation = Quaternion.Euler(0f, 90f, 0f);   // local +Z -> +X: the run
            return root;
        }

        /// <summary>The hub: a square log blockhouse a little wider than the wall, one walk high, stake parapet, brazier, banner.</summary>
        static GameObject BuildLogHub()
        {
            var root = new GameObject("Hub_Feraldis");
            var t = root.transform;
            float hs = 3.0f;   // half size: 6 kit m square (3.6 m in game, HubWidth is 4.2)
            Put(t, C_Block, "Core", Vector3.zero, 0f, new Vector3(2f * hs / 5.5f, (Walk - 0.2f) / 5f, 2f * hs / 5.5f));
            // Plank faces on the four sides, two courses each (Hoarding_Wood_Wall sits 1.63 out from its pivot, 2.5 wide, 2.2 tall).
            for (int side = 0; side < 4; side++)
            {
                float yaw = side * 90f;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                for (int course = 0; course < 2; course++)
                {
                    // Two 2.5 m panels per 6 m side, stretched 1.2.
                    for (int k = 0; k < 2; k++)
                    {
                        Vector3 local = new Vector3(hs - k * hs, course * 2.2f, -(hs + 1.63f));
                        Put(t, C_HoardWall, "Planks", rot * local, yaw, new Vector3(1.2f, 1.0f, 1f));
                    }
                }
            }
            Put(t, C_FloorWood, "Walk", new Vector3(hs, Walk - 0.3f, hs), 0f, new Vector3(2f * hs / 5f, 1f, 2f * hs / 5f));
            foreach (var (px, pz) in new[] { (-hs, -hs), (hs, -hs), (hs, hs), (-hs, hs) })
                Put(t, C_PillarRound, "CornerLog", new Vector3(px, 0f, pz), 0f, new Vector3(0.7f, (Walk + 1.2f) / 5f, 0.7f));
            // Stake parapet round the walk.
            for (int side = 0; side < 4; side++)
            {
                float yaw = side * 90f;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                Put(t, B_SpikeWall2, "Parapet", rot * new Vector3(-hs, Walk - 0.4f, -hs + 0.3f), yaw, new Vector3(2f * hs / 5f, 0.7f, 1.4f));
            }
            Put(t, B_Brazier, "Brazier", new Vector3(0f, Walk - 0.3f, 0f), 0f, Vector3.one * 2.4f);
            Banner(t, "PlayerColor_BannerA", new Vector3(0f, Walk - 0.3f, -hs - 0.3f), 180f, 1.8f, _ferCloth);
            Banner(t, "PlayerColor_BannerB", new Vector3(0f, Walk - 0.3f, hs + 0.3f), 0f, 1.8f, _ferCloth);
            return root;
        }

        /// <summary>The gatehouse: two log modules flanking a 5 m passage under a
        /// log lintel and deck, two swinging plank doors (named Door*, hinged by
        /// WallGateDoors at their outer ends), a trophy over the gate.</summary>
        static GameObject BuildLogGate()
        {
            var root = new GameObject("Gate_Feraldis");
            var t = root.transform;
            LogRun(t, "L", -M * 1.5f, -M * 0.5f);
            LogRun(t, "R", M * 0.5f, M * 1.5f);
            float half = LogDepth * 0.5f;
            // Gate posts: round logs either side of the passage, taller than the walk.
            foreach (var (px, tag) in new[] { (-M * 0.5f + 0.5f, "L"), (M * 0.5f - 0.5f, "R") })
            {
                Put(t, C_PillarRound, "GatePost" + tag + "A", new Vector3(px, 0f, -half + 0.6f), 0f, new Vector3(0.75f, (Walk + 2.6f) / 5f, 0.75f));
                Put(t, C_PillarRound, "GatePost" + tag + "B", new Vector3(px, 0f, half - 0.6f), 0f, new Vector3(0.75f, (Walk + 2.6f) / 5f, 0.75f));
            }
            // Deck over the passage and the lintel beams on both faces.
            Put(t, C_FloorWood, "PassageDeck", new Vector3(M * 0.5f, Walk, half - 0.55f), 0f, new Vector3(1f, 1f, (LogDepth - 1.1f) / 5f));
            Put(t, C_PanelWood, "LintelA", new Vector3(M * 0.5f, Walk - 0.5f, -half + 0.2f), 0f, new Vector3(1f, 0.6f, 1f));
            Put(t, C_PanelWood, "LintelB", new Vector3(-M * 0.5f, Walk - 0.5f, half - 0.2f), 180f, new Vector3(1f, 0.6f, 1f));
            // Parapet over the passage.
            Put(t, B_SpikeWall2, "ParapetA", new Vector3(-M * 0.5f, Walk - 0.4f, -half + 0.5f), 0f, new Vector3(1f, 0.7f, 1.4f));
            Put(t, B_SpikeWall2, "ParapetB", new Vector3(M * 0.5f, Walk - 0.4f, half - 0.5f), 180f, new Vector3(1f, 0.7f, 1.4f));
            // The doors: Castle_Door_Single is 1.03 x 2.35 with its hinge edge at the pivot (x = 0, spanning -X).
            float leafW = 2.3f, leafH = 4.4f;
            Put(t, C_DoorSingle, "DoorLeft", new Vector3(-M * 0.5f + 0.95f, 0.1f, 0f), 180f, new Vector3(leafW / 1.03f, leafH / 2.35f, 1.6f));
            Put(t, C_DoorSingle, "DoorRight", new Vector3(M * 0.5f - 0.95f, 0.1f, 0f), 0f, new Vector3(leafW / 1.03f, leafH / 2.35f, 1.6f));
            // Trophy, banners and skulls face the enemy (kit +Z = module +X).
            Put(t, P_DeerHead, "Trophy", new Vector3(0f, Walk + 0.4f, half + 0.2f), 180f, Vector3.one * 2.2f);
            Put(t, B_Brazier, "BrazierL", new Vector3(-M * 0.5f - 0.2f, Walk - 0.1f, 0f), 0f, Vector3.one * 2.2f);
            Put(t, B_Brazier, "BrazierR", new Vector3(M * 0.5f + 0.2f, Walk - 0.1f, 0f), 0f, Vector3.one * 2.2f);
            Banner(t, "PlayerColor_BannerL", new Vector3(-M * 0.5f - 1.5f, Walk - 0.1f, half + 0.3f), 0f, 1.8f, _ferCloth);
            Banner(t, "PlayerColor_BannerR", new Vector3(M * 0.5f + 1.5f, Walk - 0.1f, half + 0.3f), 0f, 1.8f, _ferCloth);
            Put(t, P_Skull, "SkullL", new Vector3(-M * 0.5f + 0.5f, Walk + 2.6f, half - 0.6f), 180f, Vector3.one * 2.5f);
            Put(t, P_Skull, "SkullR", new Vector3(M * 0.5f - 0.5f, Walk + 2.6f, half - 0.6f), 180f, Vector3.one * 2.5f);
            return root;
        }

        /// <summary>A log module with a timber watch tower astride it.</summary>
        static GameObject BuildLogTower()
        {
            var root = new GameObject("Tower_Feraldis");
            var t = root.transform;
            LogRun(t, "", -M * 0.5f, M * 0.5f);
            Put(t, Bld_Tower2, "Turret", new Vector3(0f, Walk - 0.3f, 0f), 90f, new Vector3(1.1f, 1.1f, 1.1f));
            foreach (var (px, pz) in new[] { (-2.0f, -2.0f), (2.0f, -2.0f), (2.0f, 2.0f), (-2.0f, 2.0f) })
                Put(t, C_PillarRound, "Leg", new Vector3(px, 0f, pz), 0f, new Vector3(0.6f, Walk / 5f, 0.6f));
            Banner(t, "PlayerColor_BannerA", new Vector3(0f, Walk + 1.4f, -LogDepth * 0.5f - 0.3f), 180f, 1.8f, _ferCloth);
            Banner(t, "PlayerColor_BannerB", new Vector3(0f, Walk + 1.4f, LogDepth * 0.5f + 0.3f), 0f, 1.8f, _ferCloth);
            return root;
        }

        /// <summary>A log bastion the engine stands on, "Deck" marker on top; the wall runs through it along Z.</summary>
        static GameObject BuildLogMount()
        {
            var root = new GameObject("Mount_Feraldis");
            var t = root.transform;
            float depth = 6.5f;
            Put(t, C_Block, "Curtain_Bastion", Vector3.zero, 0f, new Vector3(depth / 5.5f, (Walk - 0.2f) / 5f, 1f));
            Put(t, C_FloorWood, "Deck", new Vector3(depth * 0.5f, Walk, 2.75f), 0f, new Vector3(depth / 5f, 1f, 1.1f));
            Put(t, B_SpikeWall2, "ParapetA", new Vector3(depth * 0.5f - 0.3f, Walk - 0.4f, -2.75f), 90f, new Vector3(1.1f, 0.7f, 1.4f));
            Put(t, B_SpikeWall2, "ParapetB", new Vector3(-depth * 0.5f + 0.3f, Walk - 0.4f, 2.75f), -90f, new Vector3(1.1f, 0.7f, 1.4f));
            var deck = new GameObject("DeckMarker"); // the sim reads the "Deck" child's height
            deck.name = "Deck";
            deck.transform.SetParent(t, false);
            deck.transform.localPosition = new Vector3(0f, Walk, 0f);
            return root;
        }

        static void MakeWallMeshesReadable()
        {
            var models = new HashSet<string>();
            foreach (var piece in _wallPieces)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KitRoot + piece + ".prefab");
                if (prefab == null) continue;
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null) models.Add(AssetDatabase.GetAssetPath(mf.sharedMesh));
            }
            foreach (var path in models)
            {
                if (AssetImporter.GetAtPath(path) is ModelImporter mi && !mi.isReadable)
                {
                    mi.isReadable = true;
                    mi.SaveAndReimport();
                    Report.Add("OK Read/Write on " + path);
                }
            }
        }

        static void BindWallConfig(GameObject curtain, GameObject hub, GameObject gate, GameObject tower, GameObject mount)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<TheWaningBorder.Rendering.WallModuleArtConfig>(WallArtConfigPath);
            if (cfg == null) throw new Exception("WallModuleArt.asset missing at " + WallArtConfigPath);
            var sets = new List<TheWaningBorder.Rendering.WallModuleArtConfig.CultureSet>(cfg.cultureSets ?? Array.Empty<TheWaningBorder.Rendering.WallModuleArtConfig.CultureSet>());
            var set = sets.Find(s => s != null && s.culture == Cultures.Feraldis);
            if (set == null) { set = new TheWaningBorder.Rendering.WallModuleArtConfig.CultureSet { culture = Cultures.Feraldis }; sets.Add(set); }
            set.curtain = curtain; set.hub = hub; set.gate = gate; set.tower = tower;
            set.ballistaMount = mount; set.trebuchetMount = mount;
            cfg.cultureSets = sets.ToArray();
            EditorUtility.SetDirty(cfg);
        }

        // ═════════════════════════════════════════════════════════════════
        // The other Feraldis buildings, re-skinned from the kit
        // ═════════════════════════════════════════════════════════════════

        static void OtherBuildings()
        {
            VariantInPlace(FER + "GreatForge/GreatForge.prefab", FER + "GreatForge/GreatForge.asset", "GreatForge", 9f, BuildGreatForge);
            VariantInPlace(FER + "HallOfAxes/HallOfAxes.prefab", FER + "HallOfAxes/HallOfAxes.asset", "HallOfAxes", 7f, BuildHallOfAxes);
            VariantInPlace(FER + "Pasture/RigYard.prefab", FER + "Pasture/Pasture.asset", "RigYard", 7f, BuildRigYard);
            VariantInPlace(FER + "SiegeYard/EngineWorks.prefab", FER + "SiegeYard/SiegeYard.asset", "EngineWorks", 7f, BuildEngineWorks);
            PlainInPlace(FER + "WarTotem/WarTotem.prefab", FER + "WarTotem/WarTotem.asset", "WarTotem", BuildWarTotem);
            PlainInPlace(FER + "HuntingLodge/HuntingLodge.prefab", FER + "HuntingLodge/HuntingLodge.asset", "HuntingLodge", BuildHuntingLodge);
            PlainInPlace(FER + "LoggingStation/LoggingStation.prefab", FER + "LoggingStation/LoggingStation.asset", "LoggingStation", BuildLoggingStation);
            LevelPrefabs(FER + "SlavePens/Art/", "SlavePens", FER + "SlavePens/SlavePens_Lvl{0}.asset", BuildSlavePens);
            BranchInPlace(TT + "Age0/Buildings/GatherersHut/GatherersHut.asset", "WorkCamp", 3, BuildWorkCamp);
            BranchInPlace(TT + "Age0/Buildings/TempleOfRidan/TempleOfRidan.asset", "BloodAltar", 1, BuildBloodAltar);
        }

        /// <summary>A culture-only levelled building's SO prefab rebuilt in place
        /// (same path, so the GUID survives): Lv0 = a construction frame of the
        /// given size + Feraldis/Lv1..3. The SO's prefab field is re-pointed at
        /// the saved asset.</summary>
        static void VariantInPlace(string prefabPath, string soPath, string stem, float frameSize, Action<Transform, int> build)
        {
            try
            {
                var root = new GameObject(stem);
                var lv0 = Group(root.transform, "Lv0");
                Frame(lv0, frameSize);
                var fer = Group(root.transform, "Feraldis");
                for (int l = 1; l <= 3; l++) { var lv = Group(fer, "Lv" + l); build(lv, l); CheckReach(stem, l, lv); }
                var prefab = Save(root, prefabPath);
                WireBuildingSo(soPath, prefab);
                Report.Add($"OK {stem}: variant prefab rebuilt from the kit -> {prefabPath}");
            }
            catch (Exception e) { Fail(stem, e.ToString()); }
        }

        static void PlainInPlace(string prefabPath, string soPath, string stem, Action<Transform> build)
        {
            try
            {
                var root = new GameObject(stem);
                build(root.transform);
                var prefab = Save(root, prefabPath);
                WireBuildingSo(soPath, prefab);
                Report.Add($"OK {stem}: prefab rebuilt from the kit -> {prefabPath}");
            }
            catch (Exception e) { Fail(stem, e.ToString()); }
        }

        /// <summary>One prefab per level beside the old Blender ones, referenced by the level SOs (the legacy ladder BuildingPrefabSwapSystem reads).</summary>
        static void LevelPrefabs(string dir, string stem, string soFormat, Action<Transform, int> build)
        {
            for (int l = 1; l <= 3; l++)
            {
                try
                {
                    var root = new GameObject($"{stem}_{l}");
                    build(root.transform, l);
                    CheckReach(stem, l, root.transform);
                    var prefab = Save(root, $"{dir}{stem}_{l}.prefab");
                    WireBuildingSo(string.Format(soFormat, l), prefab);
                    Report.Add($"OK {stem} L{l} -> {dir}{stem}_{l}.prefab");
                }
                catch (Exception e) { Fail($"{stem} L{l}", e.ToString()); }
            }
        }

        /// <summary>Rebuild the Feraldis branch (Lv1..levels) of an Age 0 building's multi-variant SO prefab.</summary>
        static void BranchInPlace(string soPath, string stem, int levels, Action<Transform, int> build)
        {
            try
            {
                string prefabPath = SoPrefabPath(soPath);
                if (string.IsNullOrEmpty(prefabPath)) throw new Exception("SO has no prefab: " + soPath);
                var contents = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    Transform container = FindContainerWithLv0(contents.transform, 2);
                    if (container == null) throw new Exception("no Lv0 container in " + prefabPath);
                    Transform fer = FindChild(container, "Feraldis") ?? Group(container, "Feraldis");
                    Clear(fer);
                    for (int l = 1; l <= levels; l++)
                    {
                        var lv = Group(fer, "Lv" + l);
                        ResetToPrefabFrame(lv, contents.transform);
                        build(lv, l);
                        CheckReach(stem, l, lv);
                    }
                    fer.gameObject.SetActive(true);
                    PrefabUtility.SaveAsPrefabAsset(contents, prefabPath, out bool ok);
                    if (!ok) throw new Exception("SaveAsPrefabAsset failed for " + prefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                Report.Add($"OK {stem}: Feraldis/Lv1..{levels} rebuilt from the kit in {prefabPath}");
            }
            catch (Exception e) { Fail(stem, e.ToString()); }
        }

        static void WireBuildingSo(string soPath, GameObject prefab)
        {
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(soPath);
            if (so == null) throw new Exception("SO missing " + soPath);
            var s = new SerializedObject(so);
            var p = s.FindProperty("prefab");
            if (p == null) throw new Exception("no prefab field on " + soPath);
            p.objectReferenceValue = prefab;
            s.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(so);
        }

        /// <summary>The Lv0 construction frame: plinth, four posts, two rafters — numbered for the rise.</summary>
        static void Frame(Transform lv0, float size)
        {
            float h = size * 0.5f - 0.6f;
            Put(Group(lv0, "1_Plinth"), C_Block, "Plinth", Vector3.zero, 0f, new Vector3(size / 5.5f, 0.4f / 5f, size / 5.5f));
            var posts = Group(lv0, "2_Posts");
            foreach (var (px, pz) in new[] { (-h, -h), (h, -h), (h, h), (-h, h) })
                Put(posts, C_PillarSq, "Post", new Vector3(px, 0.4f, pz), 0f, new Vector3(0.55f, 0.8f, 0.55f));
            var rafters = Group(lv0, "3_Rafters");
            Put(rafters, C_PanelWood, "RafterA", new Vector3(h, 4.2f, -h - 0.15f), 0f, new Vector3(2f * h / 5f, 0.5f, 1f));
            Put(rafters, C_PanelWood, "RafterB", new Vector3(-h, 4.2f, h + 0.15f), 180f, new Vector3(2f * h / 5f, 0.5f, 1f));
        }

        /// <summary>A dark timber hall (hx x hz half sizes, one or two storeys
        /// of 2.5 m) with a stone footing, gables on ±X, a steep shingle roof
        /// and antler finials. The door faces -Z. Returns the wall top.</summary>
        static float DarkHall(Transform lv, float cx, float cz, float hx, float hz, int storeys, float roofRise, bool door = true,
                              bool loft = false)
        {
            const float footing = 0.35f;
            int nx = Mathf.Max(1, Mathf.RoundToInt(hx * 2f / W)), nz = Mathf.Max(1, Mathf.RoundToInt(hz * 2f / W));
            Put(lv, C_Block, "Footing", new Vector3(cx, 0f, cz), 0f, new Vector3((2f * hx + 0.6f) / 5.5f, footing / 5f, (2f * hz + 0.6f) / 5.5f));
            string[] Row(int n, string mid, string end, string centre = null)
            {
                var r = new string[n];
                for (int i = 0; i < n; i++) r[i] = (i == 0 || i == n - 1) ? end : mid;
                if (centre != null) r[n / 2] = centre;
                return r;
            }
            float y = footing;
            for (int s = 0; s < storeys; s++)
            {
                bool ground = s == 0;
                Ring(lv, $"S{s}", cx, cz, nx * W * 0.5f, nz * W * 0.5f, y,
                     Row(nx, ground ? H_Wall : H_WallWin, H_WallBeams, ground && door ? H_WallDoor : null),
                     Row(nz, ground ? H_Wall : H_WallWin, H_WallBeams),
                     Row(nx, ground ? H_WallWin : H_WallWin, H_WallBeams),
                     Row(nz, ground ? H_Wall : H_WallWin, H_WallBeams));
                y += W;
            }
            float ex = nx * W * 0.5f, ez = nz * W * 0.5f;
            if (loft)
            {
                // A half-height clerestory course: the "one and a half storey"
                // hall of a mid level, visibly taller than Lv1 without being Lv3.
                Ring(lv, "Loft", cx, cz, ex, ez, y, Row(nx, H_WallWin, H_WallBeams), Row(nz, H_WallWin, H_WallBeams),
                     Row(nx, H_WallWin, H_WallBeams), Row(nz, H_WallWin, H_WallBeams), Style.Feraldis, 0.5f);
                y += W * 0.5f;
            }
            foreach (var (px, pz) in new[] { (-ex, -ez), (ex, -ez), (ex, ez), (-ex, ez) })
                Put(lv, C_PillarSq, "Post", new Vector3(cx + px, footing, cz + pz), 0f, new Vector3(0.55f, (y - footing) / 5.05f, 0.55f));
            float ridge = y + roofRise;
            Gable(lv, "GableW", new Vector3(cx - ex, y, cz), ez, roofRise, 90f);
            Gable(lv, "GableE", new Vector3(cx + ex, y, cz), ez, roofRise, -90f);
            SteepRoof(lv, "Hall", cx - ex - 0.3f, cx + ex + 0.3f, cz, ez + 0.3f, y - 0.1f, ridge);
            // The antler mesh projects 0.67 m FORWARD of its pivot (x2 at this
            // scale): mounted 1.2 m inside the gable so the tips end at the eave
            // line instead of past the yard.
            AntlerFinial(lv, "AntlerW", new Vector3(cx - ex + 1.2f, ridge - 0.2f, cz), -90f, 2.0f);
            AntlerFinial(lv, "AntlerE", new Vector3(cx + ex - 1.2f, ridge - 0.2f, cz), 90f, 2.0f);
            return y;
        }

        /// <summary>An open-sided shed: posts and a steep shingle roof, no walls.</summary>
        static void Shed(Transform lv, float cx, float cz, float hx, float hz, float eave, float rise)
        {
            foreach (var (px, pz) in new[] { (-hx, -hz), (hx, -hz), (hx, hz), (-hx, hz), (0f, -hz), (0f, hz) })
                Put(lv, C_PillarSq, "ShedPost", new Vector3(cx + px, 0f, cz + pz), 0f, new Vector3(0.5f, eave / 5.05f, 0.5f));
            SteepRoof(lv, "Shed", cx - hx - 0.4f, cx + hx + 0.4f, cz, hz + 0.4f, eave - 0.1f, eave + rise);
        }

        static void SkullPost(Transform lv, Vector3 pos, float h = 2.5f)
        {
            Put(lv, C_PillarSq, "SkullPost", pos, 0f, new Vector3(0.3f, h / 5.05f, 0.3f));
            Put(lv, P_Skull, "Skull", pos + new Vector3(0f, h, 0f), 0f, Vector3.one * 2.0f);
        }
        // ═════════════════════════════════════════════════════════════════
        // Previews
        // ═════════════════════════════════════════════════════════════════

        [MenuItem("Waning Border/Art/Render Feraldis Kit Previews")]
        public static void RenderPreviews()
        {
            string outDir = Path.GetFullPath(PreviewDir);
            Directory.CreateDirectory(outDir);
            string barracks = SoPrefabPath(BarracksSoPath);
            var jobs = new List<(string label, string prefab, int level, byte culture)>
            {
                ("Shelter", FortressPrefabPath, 0, Cultures.None),
                ("FortressFeraldis", FortressPrefabPath, 1, Cultures.Feraldis),
                ("FortressFeraldis", FortressPrefabPath, 2, Cultures.Feraldis),
                ("FortressFeraldis", FortressPrefabPath, 3, Cultures.Feraldis),
                ("Longhouse", barracks, 1, Cultures.Feraldis),
                ("Longhouse", barracks, 2, Cultures.Feraldis),
                ("Longhouse", barracks, 3, Cultures.Feraldis),
                ("WallCurtain", WallDir + "Curtain_Feraldis.prefab", -1, Cultures.Feraldis),
                ("WallHub", WallDir + "Hub_Feraldis.prefab", -1, Cultures.Feraldis),
                ("WallGate", WallDir + "Gate_Feraldis.prefab", -1, Cultures.Feraldis),
                ("WallTower", WallDir + "Tower_Feraldis.prefab", -1, Cultures.Feraldis),
                ("WallMount", WallDir + "Mount_Feraldis.prefab", -1, Cultures.Feraldis),
                ("GreatForge", FER + "GreatForge/GreatForge.prefab", 0, Cultures.Feraldis),
                ("GreatForge", FER + "GreatForge/GreatForge.prefab", 1, Cultures.Feraldis),
                ("GreatForge", FER + "GreatForge/GreatForge.prefab", 3, Cultures.Feraldis),
                ("HallOfAxes", FER + "HallOfAxes/HallOfAxes.prefab", 1, Cultures.Feraldis),
                ("HallOfAxes", FER + "HallOfAxes/HallOfAxes.prefab", 3, Cultures.Feraldis),
                ("RigYard", FER + "Pasture/RigYard.prefab", 1, Cultures.Feraldis),
                ("RigYard", FER + "Pasture/RigYard.prefab", 3, Cultures.Feraldis),
                ("EngineWorks", FER + "SiegeYard/EngineWorks.prefab", 1, Cultures.Feraldis),
                ("EngineWorks", FER + "SiegeYard/EngineWorks.prefab", 3, Cultures.Feraldis),
                ("WarTotem", FER + "WarTotem/WarTotem.prefab", -1, Cultures.Feraldis),
                ("HuntingLodge", FER + "HuntingLodge/HuntingLodge.prefab", -1, Cultures.Feraldis),
                ("LoggingStation", FER + "LoggingStation/LoggingStation.prefab", -1, Cultures.Feraldis),
                ("SlavePens", FER + "SlavePens/Art/SlavePens_1.prefab", -1, Cultures.Feraldis),
                ("SlavePens", FER + "SlavePens/Art/SlavePens_3.prefab", -1, Cultures.Feraldis),
                ("WorkCamp", SoPrefabPath(TT + "Age0/Buildings/GatherersHut/GatherersHut.asset"), 1, Cultures.Feraldis),
                ("WorkCamp", SoPrefabPath(TT + "Age0/Buildings/GatherersHut/GatherersHut.asset"), 3, Cultures.Feraldis),
                ("BloodAltar", SoPrefabPath(TT + "Age0/Buildings/TempleOfRidan/TempleOfRidan.asset"), 1, Cultures.Feraldis),
            };
            var report = new List<string>();
            foreach (var j in jobs)
            {
                try
                {
                    report.Add(RenderOne(j.label, j.prefab, j.level, j.culture, outDir, new Vector3(1f, 0.85f, -1f), ""));
                    report.Add(RenderOne(j.label, j.prefab, j.level, j.culture, outDir, new Vector3(-1f, 0.7f, 1f), "_back"));
                }
                catch (Exception e) { report.Add($"FAIL {j.label} L{j.level}: {e.Message}"); }
            }
            try { report.Add(RenderWallAssembly(outDir)); } catch (Exception e) { report.Add("FAIL assembly: " + e.Message); }
            string text = "[FeraldisKit] previews\n" + string.Join("\n", report);
            Debug.Log(text);
            try { File.WriteAllText(Path.Combine(outDir, "report.txt"), text); } catch (Exception) { }
        }

        // ─────────────────────────────────────────────────────────────────
        // Level comparisons: Lv1 | Lv2 | Lv3 side by side, each fitted to the
        // building's real footprint, from the in-game camera (CameraController
        // .asset: FOV 40, default distance 37.5 m, tilt ~47 deg at that zoom).
        // ─────────────────────────────────────────────────────────────────

        [MenuItem("Waning Border/Art/Render Feraldis Level Comparisons")]
        public static void RenderLevelComparisons()
        {
            string outDir = Path.GetFullPath(PreviewDir);
            Directory.CreateDirectory(outDir);
            var report = new List<string>();
            void Variant(string name, string prefabPath, string soPath)
            {
                try { report.Add(RenderComparison(name, outDir, FootprintOf(soPath), l => ShownVariant(prefabPath, l))); }
                catch (Exception e) { report.Add($"FAIL {name}: {e.Message}"); }
            }
            void Ladder(string name, string dir, string stem, string soPath)
            {
                try { report.Add(RenderComparison(name, outDir, FootprintOf(soPath), l => Instance($"{dir}{stem}_{l}.prefab"))); }
                catch (Exception e) { report.Add($"FAIL {name}: {e.Message}"); }
            }
            Variant("Fortress", FortressPrefabPath, TT + "Age0/Buildings/Fortress/Fortress.asset");
            Variant("Longhouse", SoPrefabPath(BarracksSoPath), BarracksSoPath);
            Variant("GreatForge", FER + "GreatForge/GreatForge.prefab", FER + "GreatForge/GreatForge.asset");
            Variant("HallOfAxes", FER + "HallOfAxes/HallOfAxes.prefab", FER + "HallOfAxes/HallOfAxes.asset");
            Variant("RigYard", FER + "Pasture/RigYard.prefab", FER + "Pasture/Pasture.asset");
            Variant("EngineWorks", FER + "SiegeYard/EngineWorks.prefab", FER + "SiegeYard/SiegeYard.asset");
            Variant("WorkCamp", SoPrefabPath(TT + "Age0/Buildings/GatherersHut/GatherersHut.asset"), TT + "Age0/Buildings/GatherersHut/GatherersHut.asset");
            Ladder("SlavePens", FER + "SlavePens/Art/", "SlavePens", TT + "Age0/Buildings/Hut/Hut.asset");
            string text = "[FeraldisKit] level comparisons\n" + string.Join("\n", report);
            Debug.Log(text);
            try { File.WriteAllText(Path.Combine(outDir, "compare_report.txt"), text); } catch (Exception) { }
        }

        /// <summary>The footprint in metres from the SO's footprintCells (2 m cells).</summary>
        static Vector2 FootprintOf(string soPath)
        {
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(soPath);
            if (so == null) throw new Exception("SO missing " + soPath);
            var p = new SerializedObject(so).FindProperty("footprintCells");
            if (p == null) throw new Exception("no footprintCells on " + soPath);
            return new Vector2(Mathf.Max(1, p.vector2IntValue.x) * 2f, Mathf.Max(1, p.vector2IntValue.y) * 2f);
        }

        static GameObject Instance(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new Exception("prefab missing " + prefabPath);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.hideFlags = HideFlags.HideAndDontSave;
            return go;
        }

        static GameObject ShownVariant(string prefabPath, int level)
        {
            var go = Instance(prefabPath);
            var v = TheWaningBorder.Rendering.BuildingVariantVisual.TrySetup(go);
            if (v == null) throw new Exception("no variant layout in " + prefabPath);
            v.ShowVariant(Cultures.Feraldis, level);
            return go;
        }

        static string RenderComparison(string name, string outDir, Vector2 footprint, Func<int, GameObject> make)
        {
            var created = new List<GameObject>();
            var stage = new Vector3(0f, -4000f, 0f);
            try
            {
                float spacing = Mathf.Max(footprint.x, footprint.y) + 4f;
                var sizes = new List<string>();
                for (int l = 1; l <= 3; l++)
                {
                    var go = make(l);
                    created.Add(go);
                    float fit = PresentationSpawnSystem.ComputeFootprintFitForSize(go, footprint.x, footprint.y, out Vector3 offset);
                    go.transform.localScale = Vector3.one * fit;
                    go.transform.position = stage + new Vector3((l - 2) * spacing, 0f, 0f) - new Vector3(offset.x, 0f, offset.z) * fit;
                    TheWaningBorder.Rendering.BuildingFactionColorMarker.Apply(go, new Color(0.85f, 0.15f, 0.15f));
                    if (MeshBounds(go, out var b)) sizes.Add($"L{l} fit {fit:F2} -> {b.size.x:F1}x{b.size.y:F1}x{b.size.z:F1}");
                }
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.hideFlags = HideFlags.HideAndDontSave;
                created.Add(ground);
                ground.transform.position = stage + new Vector3(0f, -0.02f, 0f);
                ground.transform.localScale = new Vector3(12f, 1f, 12f);
                var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                gm.SetColor("_BaseColor", new Color(0.30f, 0.40f, 0.28f));
                gm.SetFloat("_Smoothness", 0.05f);
                ground.GetComponent<Renderer>().sharedMaterial = gm;
                UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());

                // The in-game camera: tilt between 35 (zoom 15) and 70 (zoom 80)
                // at the default distance 37.5 -> ~47 deg, FOV 40.
                const float dist = 37.5f, tilt = 47f, fov = 40f;
                var camGo = new GameObject("FerCompareCam") { hideFlags = HideFlags.HideAndDontSave };
                created.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.3f; cam.farClipPlane = 400f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.10f, 0.12f, 0.18f, 1f);
                var focus = stage + new Vector3(0f, 2f, 0f);
                var back = new Vector3(0f, Mathf.Sin(tilt * Mathf.Deg2Rad), -Mathf.Cos(tilt * Mathf.Deg2Rad));
                camGo.transform.position = focus + back * dist;
                camGo.transform.rotation = Quaternion.LookRotation(-back, Vector3.up);
                var sunGo = new GameObject("FerCompareSun") { hideFlags = HideFlags.HideAndDontSave };
                created.Add(sunGo);
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.7f; sun.color = new Color(1f, 0.94f, 0.86f);
                sun.shadows = LightShadows.Soft;
                sunGo.transform.rotation = Quaternion.Euler(50f, -40f, 0f);
                var fillGo = new GameObject("FerCompareFill") { hideFlags = HideFlags.HideAndDontSave };
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

        static string SoPrefabPath(string soPath)
        {
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(soPath);
            if (so == null) return null;
            var p = new SerializedObject(so).FindProperty("prefab");
            var go = p != null ? p.objectReferenceValue as GameObject : null;
            return go != null ? AssetDatabase.GetAssetPath(go) : null;
        }

        /// <summary>hub | wall | wall | tower | wall | gate | wall | hub, as the game fits them (3 m module).</summary>
        static string RenderWallAssembly(string outDir)
        {
            var root = new GameObject("FerWallAssembly") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.position = new Vector3(0f, -4000f, 0f);
            var created = new List<GameObject> { root };
            try
            {
                var curtainPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallDir + "Curtain_Feraldis.prefab");
                var curtain = TheWaningBorder.Rendering.WallModuleArt.ForPrefab(curtainPrefab, TheWaningBorder.Entities.AlanthorWall.InstanceSpacing, true);
                if (curtain == null) return "FAIL assembly: curtain art did not prepare (meshes readable?)";
                float s = curtain.Scale;
                float z = 0f;
                void Add(string file, float span, bool alongZ, float fit)
                {
                    var p = AssetDatabase.LoadAssetAtPath<GameObject>(WallDir + file);
                    var art = TheWaningBorder.Rendering.WallModuleArt.ForPrefab(p, fit, alongZ, s);
                    if (art == null) return;
                    var holder = new GameObject(file).transform;
                    holder.SetParent(root.transform, false);
                    holder.localPosition = new Vector3(0f, 0f, z + span * 0.5f);
                    var go = TheWaningBorder.Rendering.WallModuleArt.Instantiate(art, holder, file);
                    TheWaningBorder.Rendering.WallModuleArt.ApplyOwnerColor(go, new Color(0.85f, 0.15f, 0.15f));
                    z += span;
                }
                float hubW = TheWaningBorder.Entities.AlanthorWall.HubWidth, mod = TheWaningBorder.Entities.AlanthorWall.InstanceSpacing;
                Add("Hub_Feraldis.prefab", hubW, false, hubW);
                Add("Curtain_Feraldis.prefab", mod, true, mod);
                Add("Curtain_Feraldis.prefab", mod, true, mod);
                Add("Tower_Feraldis.prefab", mod, true, mod);
                Add("Curtain_Feraldis.prefab", mod, true, mod);
                Add("Gate_Feraldis.prefab", TheWaningBorder.Entities.AlanthorWall.GateSpanMetres, true, TheWaningBorder.Entities.AlanthorWall.GateSpanMetres);
                Add("Curtain_Feraldis.prefab", mod, true, mod);
                Add("Hub_Feraldis.prefab", hubW, false, hubW);
                Shoot(root, Path.Combine(outDir, "WallAssembly.png"), new Vector3(-1f, 0.75f, -0.7f), created);
                Shoot(root, Path.Combine(outDir, "WallAssembly_inner.png"), new Vector3(1f, 0.6f, 0.6f), created);
                return "OK assembly (curtain scale " + s.ToString("F2") + ")";
            }
            finally { foreach (var c in created) if (c != null) UnityEngine.Object.DestroyImmediate(c); }
        }

        /// <summary>level: -1 = plain prefab, 0 = Lv0 branch, N = Feraldis/LvN.</summary>
        static string RenderOne(string label, string prefabPath, int level, byte culture, string outDir, Vector3 viewDir, string suffix)
        {
            if (string.IsNullOrEmpty(prefabPath)) return $"SKIP {label}: no prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) return $"SKIP {label}: not loadable {prefabPath}";
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.position = new Vector3(0f, -4000f, 0f);
            var created = new List<GameObject> { go };
            try
            {
                if (level >= 0)
                {
                    var v = TheWaningBorder.Rendering.BuildingVariantVisual.TrySetup(go);
                    if (v == null) return $"SKIP {label}: no variant layout in {prefabPath}";
                    if (level > 0) v.ShowVariant(culture, level);
                }
                TheWaningBorder.Rendering.BuildingFactionColorMarker.Apply(go, new Color(0.85f, 0.15f, 0.15f));
                if (level < 0) TheWaningBorder.Rendering.WallModuleArt.ApplyOwnerColor(go, new Color(0.85f, 0.15f, 0.15f));
                if (!MeshBounds(go, out var b)) return $"FAIL {label} L{level}: no renderers";
                string file = Path.Combine(outDir, $"{label}_{(level < 0 ? "model" : "Lv" + level)}{suffix}.png");
                Shoot(go, file, viewDir, created);
                return $"OK {label} L{level}{suffix}: bounds {b.size.x:F1} x {b.size.y:F1} x {b.size.z:F1} m -> {file}";
            }
            finally { foreach (var c in created) if (c != null) UnityEngine.Object.DestroyImmediate(c); }
        }

        /// <summary>Bounds of the MESH renderers (particle systems report
        /// bounds at the world origin until simulated, 4 km from the stage).</summary>
        static bool MeshBounds(GameObject go, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        /// <summary>Orthographic RTS-angle shot with a dark moss ground, key sun,
        /// cool fill and a flat dusk ambient (the open scene's own ambient is not
        /// what the game grades with), framed on the projected bounds.</summary>
        static void Shoot(GameObject go, string file, Vector3 viewDir, List<GameObject> created)
        {
            if (!MeshBounds(go, out var b)) return;
            var local = new List<GameObject>();

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.hideFlags = HideFlags.HideAndDontSave;
            local.Add(ground);
            ground.transform.position = new Vector3(b.center.x, b.min.y - 0.02f, b.center.z);
            float gs = Mathf.Max(b.size.x, b.size.z) * 0.32f;
            ground.transform.localScale = new Vector3(gs, 1f, gs);
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gm.SetColor("_BaseColor", new Color(0.30f, 0.40f, 0.28f));
            gm.SetFloat("_Smoothness", 0.05f);
            ground.GetComponent<Renderer>().sharedMaterial = gm;
            UnityEngine.Object.DestroyImmediate(ground.GetComponent<Collider>());

            var camGo = new GameObject("FerKitCam") { hideFlags = HideFlags.HideAndDontSave };
            local.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            float diag = Mathf.Max(b.size.x, b.size.y, b.size.z);
            cam.nearClipPlane = 0.1f; cam.farClipPlane = diag * 12f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.12f, 0.18f, 1f);
            var dir = viewDir.normalized;
            camGo.transform.position = b.center + dir * diag * 4f;
            camGo.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
            // Frame the projected corners, not the longest axis.
            float ex = 0f, ey = 0f;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var v = camGo.transform.InverseTransformPoint(c);
                ex = Mathf.Max(ex, Mathf.Abs(v.x)); ey = Mathf.Max(ey, Mathf.Abs(v.y));
            }
            cam.orthographicSize = Mathf.Max(ey, ex * 800f / 1000f) * 1.12f;
            var sunGo = new GameObject("FerKitSun") { hideFlags = HideFlags.HideAndDontSave };
            local.Add(sunGo);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.7f; sun.color = new Color(1f, 0.94f, 0.86f);
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(50f, -40f, 0f);
            var fillGo = new GameObject("FerKitFill") { hideFlags = HideFlags.HideAndDontSave };
            local.Add(fillGo);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = 0.45f; fill.color = new Color(0.75f, 0.8f, 1f);
            fillGo.transform.rotation = Quaternion.Euler(35f, 140f, 0f);

            bool fog = RenderSettings.fog;
            var ambMode = RenderSettings.ambientMode;
            var ambCol = RenderSettings.ambientLight;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.48f, 0.58f);
            var rt = new RenderTexture(1000, 800, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderSettings.fog = fog;
            RenderSettings.ambientMode = ambMode;
            RenderSettings.ambientLight = ambCol;
            cam.targetTexture = null;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            foreach (var g in local) UnityEngine.Object.DestroyImmediate(g);
            UnityEngine.Object.DestroyImmediate(gm);
        }

        // ═════════════════════════════════════════════════════════════════
        // Helpers
        // ═════════════════════════════════════════════════════════════════

        /// <summary>Report any level whose mesh bounds reach past its "Yard" /
        /// "Plinth" slab — the footprint fit would then shrink that level and
        /// undo the size progression (see BuildFeraldisFortress).</summary>
        static void CheckReach(string stem, int level, Transform lv)
        {
            Transform slab = null;
            foreach (var t in lv.GetComponentsInChildren<Transform>(true))
                if (t.name == "Yard" || t.name == "Plinth") { slab = t; break; }
            var sr = slab != null ? slab.GetComponentInChildren<Renderer>(true) : null;
            if (sr == null) return;
            var sb = sr.bounds;
            if (!MeshBounds(lv.gameObject, out var b)) return;
            float over = Mathf.Max(sb.min.x - b.min.x, b.max.x - sb.max.x, sb.min.z - b.min.z, b.max.z - sb.max.z);
            if (over > 0.05f)
            {
                var worst = new List<string>();
                foreach (var r in lv.GetComponentsInChildren<Renderer>(false))
                {
                    if (r is ParticleSystemRenderer) continue;
                    var rb = r.bounds;
                    float o = Mathf.Max(sb.min.x - rb.min.x, rb.max.x - sb.max.x, sb.min.z - rb.min.z, rb.max.z - sb.max.z);
                    if (o > 0.05f) worst.Add((r.transform.parent != null ? r.transform.parent.name + "/" : "") + r.name + " +" + o.ToString("F2"));
                }
                Report.Add($"WARN {stem} L{level}: reaches {over:F2} m past its slab ({string.Join(", ", worst)})");
            }
        }

        static Transform FindContainerWithLv0(Transform t, int maxDepth)
        {
            for (int i = 0; i < t.childCount; i++)
                if (string.Equals(t.GetChild(i).name, "Lv0", StringComparison.OrdinalIgnoreCase)) return t;
            if (maxDepth <= 1) return null;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = FindContainerWithLv0(t.GetChild(i), maxDepth - 1);
                if (f != null) return f;
            }
            return null;
        }

        static Transform FindChild(Transform t, string name)
        {
            for (int i = 0; i < t.childCount; i++)
                if (string.Equals(t.GetChild(i).name, name, StringComparison.OrdinalIgnoreCase)) return t.GetChild(i);
            return null;
        }

        static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(t.GetChild(i).gameObject);
        }

        static GameObject Save(GameObject root, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var asset = PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            UnityEngine.Object.DestroyImmediate(root);
            if (!ok || asset == null) throw new Exception("SaveAsPrefabAsset failed for " + path);
            return asset;
        }

        static void ClearLevelPrefab(string soPath)
        {
            try
            {
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(soPath);
                if (so == null) throw new Exception("SO missing " + soPath);
                var s = new SerializedObject(so);
                var p = s.FindProperty("prefab");
                if (p == null) throw new Exception("no prefab field on " + soPath);
                if (p.objectReferenceValue == null) return;
                p.objectReferenceValue = null;
                s.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(so);
                Report.Add("OK cleared prefab on " + soPath);
            }
            catch (Exception e) { Fail(soPath, e.ToString()); }
        }

        static void Fail(string id, string why)
        {
            _failures++;
            Report.Add("FAIL " + id + ": " + why);
            Debug.LogError("[FeraldisKit] " + id + ": " + why);
        }
    }
}
