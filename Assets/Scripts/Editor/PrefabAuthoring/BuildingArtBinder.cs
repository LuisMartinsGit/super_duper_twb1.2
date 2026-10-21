// BuildingArtBinder.cs
// ONE SO, ONE MODEL (docs/Design/Art_Direction.md § 13, 2026-10-20).
// Developer: "presentation layer should ONLY accept the prefabs through the SO,
// no exception."
//
// Waning Border > Art > Bind Building Art (also -executeMethod
// TheWaningBorder.EditorTools.BuildingArtBinder.Run) — idempotent:
//
//   1. BIND THE MODELS. Every FBX in a TechTree Art/ folder named by the
//      convention — <culture>_<Name>_lv<N> (a level) or <BuildingId>_Age_0 (a
//      base form) — is imported with the House's materials remapped by name
//      (Civs/Alanthor/Buildings/House/Materials) and set as that SO's prefab:
//      the level SO in the folder above Art/ for that culture and level, or
//      the BuildingDefSO with that id.
//   2. SPLIT THE MULTI-VARIANT PREFABS. A building whose SO prefab still holds
//      Lv0 + <Culture>/LvN branches (the Synty kit compositions) gets one
//      prefab per branch, unpacked and self-contained: <Id>_Age_0.prefab for
//      the base SO, <culture>_<LevelName>_lv<N>.prefab for every level SO left
//      without a model, and the culture looks of a building with no ladder
//      in its SO's culturePrefabs. The Hut keeps HutAge0.prefab (developer).
//      Tech-visual "Upgrades" groups are switched off in the split prefabs.
//   3. Sect building SOs get their real presentation ids (they read 0).
//   4. BIND THE WALL SET. One folder per wall piece per wall level, holding
//      only its .blend, .fbx and WallPieceArtSO (StoneWall/Lvl<N>/<Piece>/,
//      Palisade/Lvl0/<Piece>/): each piece SO takes the FBX beside it, and
//      WallModuleArt.asset lists them all — the only place wall art comes
//      from. The part SOs and the Stone Wall's level SOs get the first /
//      level-N models; the audit lists every piece a level must have and
//      lacks.
//      Replaces Waning Border > Walls > Bind Wall Art and Author Castle
//      Prefabs (retired 2026-10-20: Synty kit compositions bound by id).
//
// Report: Temp/building-art-binder.txt.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Data;

namespace TheWaningBorder.EditorTools
{
    public static class BuildingArtBinder
    {
        const string Root = "Assets/GameData/TechTree";
        const string HouseMats = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/House/Materials/";
        static readonly string[] HouseMatNames = {
            "Base_texture", "Buissons_texture", "Chambranle_fenetres_et_lumiere_texture",
            "Lanterne_texture", "Muret_texture", "Murs_texture", "Piliers_texture",
            "Porte_et_lumiere_porte_texture", "Toit_texture" };
        static readonly Regex LevelName = new Regex(@"^(alanthor|feraldis|runai)_(\w+?)_lv(\d)$");
        static readonly Regex BaseName = new Regex(@"^(\w+)_Age_0$");
        static readonly Dictionary<string, int> SectPids = new Dictionary<string, int> {
            { "Sect_Reliquary", 561 }, { "Sect_MendingHall", 562 }, { "Sect_Stonehold", 563 },
            { "Sect_Treasury", 564 }, { "Sect_MusterYard", 565 } };
        static readonly string[] KeepPrefab = { "Hut" };

        static StringBuilder _log;

        [MenuItem("Waning Border/Art/Bind Building Art (SO only)")]
        public static void Run()
        {
            _log = new StringBuilder();
            AssetDatabase.Refresh();
            var buildings = Load<BuildingDefSO>();
            var levels = Load<BuildingLevelDefSO>();
            _log.AppendLine($"{buildings.Count} building SOs, {levels.Count} level SOs");

            EnsureChapelArt();
            BindModels(buildings, levels);
            SplitVariants(buildings, levels);
            BindWalls(buildings, levels);
            foreach (var b in buildings)
                if (SectPids.TryGetValue(b.id, out int pid) && b.presentationId != pid)
                {
                    b.presentationId = pid; EditorUtility.SetDirty(b);
                    _log.AppendLine($"pid {b.id} -> {pid}");
                }
            AssetDatabase.SaveAssets();
            ComponentConfigTools.Rebuild();
            Audit(buildings, levels);
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/building-art-binder.txt", _log.ToString());
            Debug.Log("[BuildingArtBinder]\n" + _log);
        }

        static List<T> Load<T>() where T : ScriptableObject =>
            AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { Root })
                .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(x => x != null).ToList();

        // ── 1. models ───────────────────────────────────────────────────

        static void BindModels(List<BuildingDefSO> buildings, List<BuildingLevelDefSO> levels)
        {
            foreach (var g in AssetDatabase.FindAssets("t:Model", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.EndsWith(".fbx") || Path.GetFileName(Path.GetDirectoryName(path)) != "Art") continue;
                string name = Path.GetFileNameWithoutExtension(path);
                var lm = LevelName.Match(name);
                var bm = BaseName.Match(name);
                if (!lm.Success && !bm.Success) continue;

                RemapHouseMaterials(path);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                string owner = Path.GetDirectoryName(Path.GetDirectoryName(path)).Replace('\\', '/');
                if (lm.Success)
                {
                    string culture = char.ToUpper(lm.Groups[1].Value[0]) + lm.Groups[1].Value.Substring(1);
                    int lvl = int.Parse(lm.Groups[3].Value);
                    var so = levels.FirstOrDefault(l => l.culture == culture && l.level == lvl
                        && Path.GetDirectoryName(AssetDatabase.GetAssetPath(l)).Replace('\\', '/') == owner);
                    if (so == null)
                    {
                        // No level SO: a building with no ladder in this
                        // culture — the model is its culture look.
                        var host = buildings.FirstOrDefault(b =>
                            Path.GetDirectoryName(AssetDatabase.GetAssetPath(b)).Replace('\\', '/') == owner);
                        if (host == null) { _log.AppendLine($"NO LEVEL SO for {path}"); continue; }
                        host.culturePrefabs.RemoveAll(c => c.culture == culture);
                        host.culturePrefabs.Add(new BuildingDefSO.CulturePrefab { culture = culture, prefab = model });
                        EditorUtility.SetDirty(host);
                        _log.AppendLine($"culture look {host.id}/{culture} <- {name}");
                        continue;
                    }
                    if (so.prefab != model) { so.prefab = model; EditorUtility.SetDirty(so); }
                    _log.AppendLine($"level {so.name} <- {name}");
                }
                else if (bm.Groups[1].Value.StartsWith("Chapel_"))
                {
                    // Chapels are not catalog buildings: their model goes on ChapelArt.asset.
                    string sect = bm.Groups[1].Value.Substring("Chapel_".Length);
                    SetChapel(sect, model);
                    _log.AppendLine($"chapel {sect} <- {name}");
                }
                else
                {
                    string id = bm.Groups[1].Value;
                    var so = buildings.FirstOrDefault(b => b.id == id);
                    if (so == null) { _log.AppendLine($"NO BUILDING SO '{id}' for {path}"); continue; }
                    if (so.prefab != model) { so.prefab = model; EditorUtility.SetDirty(so); }
                    _log.AppendLine($"base {so.id} <- {name}");
                }
            }
        }

        static void RemapHouseMaterials(string fbx)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (imp == null) return;
            bool changed = false;
            var existing = imp.GetExternalObjectMap();
            foreach (var n in HouseMatNames)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(HouseMats + n + ".mat");
                if (m == null) continue;
                var key = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
                if (existing.TryGetValue(key, out var cur) && cur == m) continue;
                imp.AddRemap(key, m); changed = true;
            }
            if (imp.animationType != ModelImporterAnimationType.None) { imp.animationType = ModelImporterAnimationType.None; changed = true; }
            if (imp.importAnimation) { imp.importAnimation = false; changed = true; }
            if (changed) imp.SaveAndReimport();
        }

        // ── 4. walls ───────────────────────────────────────────────────

        const string WallArtPath = "Assets/GameData/TechTree/Age0/Buildings/Wall/WallModuleArt.asset";

        /// <summary>The pieces each wall level must have (Age_1_Alanthor.md §
        /// The stone wall): the palisade is curtain, hub and gate; every stone
        /// level adds the wall tower, Battlemented (2) the Ballista mount,
        /// Shielded (3) the Trebuchet mount.</summary>
        static IEnumerable<(TheWaningBorder.Rendering.WallModuleArt.WallPart, int)> RequiredWallPieces()
        {
            var W = typeof(TheWaningBorder.Rendering.WallModuleArt.WallPart);
            foreach (var p in new[] { "Curtain", "Hub", "Gate" })
                yield return ((TheWaningBorder.Rendering.WallModuleArt.WallPart)System.Enum.Parse(W, p), 0);
            for (int lv = 1; lv <= 3; lv++)
            {
                foreach (var p in new[] { "Curtain", "Hub", "Gate", "Tower" })
                    yield return ((TheWaningBorder.Rendering.WallModuleArt.WallPart)System.Enum.Parse(W, p), lv);
                if (lv >= 2) yield return (TheWaningBorder.Rendering.WallModuleArt.WallPart.BallistaMount, lv);
                if (lv >= 3) yield return (TheWaningBorder.Rendering.WallModuleArt.WallPart.TrebuchetMount, lv);
            }
        }

        static void BindWalls(List<BuildingDefSO> buildings, List<BuildingLevelDefSO> levels)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<TheWaningBorder.Rendering.WallModuleArtConfig>(WallArtPath);
            if (cfg == null) { _log.AppendLine("NO WallModuleArt.asset at " + WallArtPath); return; }

            // Every piece SO takes the FBX in its own folder.
            var pieces = Load<TheWaningBorder.Rendering.WallPieceArtSO>();
            foreach (var piece in pieces)
            {
                string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(piece)).Replace('\\', '/');
                string fbx = Directory.GetFiles(folder, "*.fbx").Select(f => f.Replace('\\', '/')).FirstOrDefault();
                if (fbx == null) { _log.AppendLine($"NO FBX beside {AssetDatabase.GetAssetPath(piece)}"); continue; }
                if (piece.level >= 1) RemapHouseMaterials(fbx);   // the stone set is House-kit art
                // WallArtMesh bakes the curtain along the curve at runtime.
                var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
                if (imp != null && (!imp.isReadable || imp.importCameras || imp.importLights))
                {
                    imp.isReadable = true; imp.importCameras = false; imp.importLights = false;
                    imp.SaveAndReimport();
                }
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                if (piece.prefab != model) { piece.prefab = model; EditorUtility.SetDirty(piece); }
                _log.AppendLine($"wall {piece.part} L{piece.level} <- {Path.GetFileName(fbx)}");
            }
            cfg.pieces = pieces.OrderBy(p => p.level).ThenBy(p => (int)p.part).ToArray();
            EditorUtility.SetDirty(cfg);
            TheWaningBorder.Rendering.WallModuleArt.Invalidate();

            GameObject Model(TheWaningBorder.Rendering.WallModuleArt.WallPart part, int lv)
                => pieces.FirstOrDefault(p => p.part == part && p.level == lv)?.prefab;
            void SetBase(string id, GameObject prefab)
            {
                var so = buildings.FirstOrDefault(b => b.id == id);
                if (so == null || prefab == null || so.prefab == prefab) return;
                so.prefab = prefab; EditorUtility.SetDirty(so);
                _log.AppendLine($"base {id} <- {prefab.name}");
            }
            // The part SOs carry their first level's model.
            SetBase("Palisade", Model(TheWaningBorder.Rendering.WallModuleArt.WallPart.Hub, 0));
            SetBase("PalisadeSegment", Model(TheWaningBorder.Rendering.WallModuleArt.WallPart.Curtain, 0));
            SetBase("Alanthor_WallGate", Model(TheWaningBorder.Rendering.WallModuleArt.WallPart.Gate, 0));
            SetBase("Alanthor_Wall", Model(TheWaningBorder.Rendering.WallModuleArt.WallPart.Hub, 1));
            SetBase("Alanthor_WallSegment", Model(TheWaningBorder.Rendering.WallModuleArt.WallPart.Curtain, 1));
            SetBase("Alanthor_WallTower", Model(TheWaningBorder.Rendering.WallModuleArt.WallPart.Tower, 1));
            SetBase("Alanthor_BallistaEmplacement", Model(TheWaningBorder.Rendering.WallModuleArt.WallPart.BallistaMount, 2));
            SetBase("Alanthor_TrebuchetEmplacement", Model(TheWaningBorder.Rendering.WallModuleArt.WallPart.TrebuchetMount, 3));
            foreach (var l in levels)
            {
                if (l.buildingId != "Alanthor_Wall" || l.level < 1 || l.level > 3) continue;
                var m = Model(TheWaningBorder.Rendering.WallModuleArt.WallPart.Curtain, l.level);
                if (m == null || l.prefab == m) continue;
                l.prefab = m; EditorUtility.SetDirty(l);
                _log.AppendLine($"level {l.name} <- {m.name}");
            }

            _log.AppendLine("── audit: wall pieces without art ──");
            foreach (var (part, lv) in RequiredWallPieces())
                if (Model(part, lv) == null) _log.AppendLine($"  WALL  {part} level {lv}");
        }

        // ── chapels ────────────────────────────────────────────────────

        const string ChapelArtPath = "Assets/GameData/TechTree/Sects/Shared/Chapel/ChapelArt.asset";
        const string LegacyChapels = "Assets/Resources/Prefabs/Buildings/Chapels/";
        /// <summary>The chapel a sect showed before ChapelArt (the old presentation-id
        /// table) — kept for the sects whose own model is not authored yet.</summary>
        static readonly Dictionary<string, string> LegacyChapel = new Dictionary<string, string> {
            { "Sect_Antiquity", "Chapel_Antiquity" }, { "Sect_Renewal", "Chapel_Renewal" },
            { "Sect_Fortitude", "Chapel_Antiquity" }, { "Sect_Prosperity", "Chapel_Renewal" },
            { "Sect_Silence", "Chapel_Silence" }, { "Sect_Justice", "Chapel_Justice" },
            { "Sect_Veneration", "Chapel_Silence" }, { "Sect_Witness", "Chapel_Justice" },
            { "Sect_War", "Chapel_War" }, { "Sect_Ash", "Chapel_Ash" },
            { "Sect_Ruin", "Chapel_War" }, { "Sect_Wrath", "Chapel_Ash" } };

        static TheWaningBorder.Rendering.ChapelArtConfig ChapelCfg()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<TheWaningBorder.Rendering.ChapelArtConfig>(ChapelArtPath);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<TheWaningBorder.Rendering.ChapelArtConfig>();
                cfg.chapels = new List<TheWaningBorder.Rendering.ChapelArtConfig.Entry>();
                AssetDatabase.CreateAsset(cfg, ChapelArtPath);
            }
            if (cfg.chapels == null) cfg.chapels = new List<TheWaningBorder.Rendering.ChapelArtConfig.Entry>();
            return cfg;
        }

        static void EnsureChapelArt()
        {
            var cfg = ChapelCfg();
            foreach (var kv in LegacyChapel)
                if (!cfg.chapels.Any(e => e.sectId == kv.Key && e.prefab != null))
                    SetChapel(kv.Key, AssetDatabase.LoadAssetAtPath<GameObject>(LegacyChapels + kv.Value + ".prefab"));
        }

        static void SetChapel(string sect, GameObject prefab)
        {
            var cfg = ChapelCfg();
            cfg.chapels.RemoveAll(e => e.sectId == sect);
            cfg.chapels.Add(new TheWaningBorder.Rendering.ChapelArtConfig.Entry { sectId = sect, prefab = prefab });
            EditorUtility.SetDirty(cfg);
        }

        // ── 2. split ────────────────────────────────────────────────────

        static void SplitVariants(List<BuildingDefSO> buildings, List<BuildingLevelDefSO> levels)
        {
            foreach (var b in buildings)
            {
                var src = b.prefab;
                if (src == null || !TheWaningBorder.Rendering.BuildingVariantVisual.HasVariantLayout(src)) continue;
                string soDir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(b)).Replace('\\', '/');
                var branches = Branches(src);
                // A prefab already split keeps its lone Lv0 node: nothing to cut.
                if (branches.Count <= 1) continue;

                // Levels left without a model take their branch.
                var cultured = new HashSet<string>();
                foreach (var l in levels.Where(l => l.buildingId == b.id))
                {
                    cultured.Add(l.culture);
                    if (l.prefab != null) continue;
                    var node = Pick(branches, l.culture, l.level);
                    if (node == null) { _log.AppendLine($"  {l.name}: no {l.culture} branch — no model"); continue; }
                    string dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(l)).Replace('\\', '/') + "/Art";
                    string file = $"{dir}/{l.culture.ToLowerInvariant()}_{Clean(l.displayName)}_lv{l.level}.prefab";
                    l.prefab = SaveBranch(src, l.culture, node, file);
                    EditorUtility.SetDirty(l);
                    _log.AppendLine($"split {l.name} <- {file}");
                }

                // A culture branch with no ladder is the building's culture look.
                foreach (var kv in branches)
                {
                    if (kv.Key == "Lv0" || cultured.Contains(kv.Key)) continue;
                    var node = Pick(branches, kv.Key, 1);
                    if (node == null) continue;
                    if (b.culturePrefabs.Any(c => c.culture == kv.Key && c.prefab != null)) continue;
                    string file = $"{soDir}/Art/{kv.Key.ToLowerInvariant()}_{Clean(b.displayName)}_lv1.prefab";
                    var p = SaveBranch(src, kv.Key, node, file);
                    b.culturePrefabs.RemoveAll(c => c.culture == kv.Key);
                    b.culturePrefabs.Add(new BuildingDefSO.CulturePrefab { culture = kv.Key, prefab = p });
                    EditorUtility.SetDirty(b);
                    _log.AppendLine($"culture look {b.id}/{kv.Key} <- {file}");
                }

                // The base form, last (it replaces the prefab the others were cut from).
                if (KeepPrefab.Contains(b.id)) continue;
                string baseFile = $"{soDir}/Art/{b.id}_Age_0.prefab";
                b.prefab = SaveBranch(src, null, "Lv0", baseFile);
                EditorUtility.SetDirty(b);
                _log.AppendLine($"split base {b.id} <- {baseFile}");
            }
        }

        static string Clean(string s) => string.IsNullOrEmpty(s) ? "Level" : Regex.Replace(s, @"[^A-Za-z]", "");

        /// <summary>Branch map: "Lv0" -> "Lv0", culture -> its level node names.</summary>
        static Dictionary<string, List<string>> Branches(GameObject src)
        {
            var map = new Dictionary<string, List<string>>();
            var c = Container(src.transform, 2);
            if (c == null) return map;
            for (int i = 0; i < c.childCount; i++)
            {
                var ch = c.GetChild(i);
                if (ch.name == "Lv0") { map["Lv0"] = new List<string> { "Lv0" }; continue; }
                string culture = ch.name == "Runaii" ? "Runai" : ch.name;
                if (culture != "Alanthor" && culture != "Feraldis" && culture != "Runai") continue;
                var lv = new List<string>();
                for (int k = 0; k < ch.childCount; k++)
                    if (Regex.IsMatch(ch.GetChild(k).name, @"^[Ll][Vv]\d+$") && HasRenderers(ch.GetChild(k)))
                        lv.Add(ch.GetChild(k).name);
                if (lv.Count > 0) map[culture] = lv;
            }
            return map;
        }

        static bool HasRenderers(Transform t) => t.GetComponentsInChildren<Renderer>(true).Length > 0;

        /// <summary>The level node for culture/level: exact, else the highest
        /// authored below it (BuildingVariantVisual.ResolveTarget's rule).</summary>
        static string Pick(Dictionary<string, List<string>> map, string culture, int level)
        {
            if (!map.TryGetValue(culture, out var lv)) return null;
            string best = null; int bestN = 0;
            foreach (var n in lv)
            {
                int k = int.Parse(n.Substring(2));
                if (k <= level && k > bestN) { best = n; bestN = k; }
            }
            return best;
        }

        static Transform Container(Transform t, int depth)
        {
            for (int i = 0; i < t.childCount; i++) if (t.GetChild(i).name == "Lv0") return t;
            if (depth <= 1) return null;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = Container(t.GetChild(i), depth - 1);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>Unpacked copy of <paramref name="src"/> holding ONE branch:
        /// Lv0 (culture null) or culture/levelNode. Everything above the
        /// container is kept, so the authored frame (an FBX import's x100 /
        /// -90° node) is the same one the old prefab drew through.</summary>
        static GameObject SaveBranch(GameObject src, string culture, string levelNode, string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            var c = Container(inst.transform, 2);
            for (int i = c.childCount - 1; i >= 0; i--)
            {
                var ch = c.GetChild(i);
                string chCulture = ch.name == "Runaii" ? "Runai" : ch.name;
                bool keep = culture == null ? ch.name == "Lv0" : chCulture == culture;
                bool branch = ch.name == "Lv0" || chCulture == "Alanthor" || chCulture == "Feraldis" || chCulture == "Runai";
                if (branch && !keep) Object.DestroyImmediate(ch.gameObject);
            }
            if (culture != null)
            {
                var root = c.Cast<Transform>().First(t => (t.name == "Runaii" ? "Runai" : t.name) == culture);
                for (int k = root.childCount - 1; k >= 0; k--)
                    if (root.GetChild(k).name != levelNode) Object.DestroyImmediate(root.GetChild(k).gameObject);
                root.gameObject.SetActive(true);
                var lvT = root.Find(levelNode);
                if (lvT != null)
                {
                    lvT.gameObject.SetActive(true);
                    for (int k = 0; k < lvT.childCount; k++)
                        if (lvT.GetChild(k).name.StartsWith("Upgrades"))
                            foreach (Transform u in lvT.GetChild(k)) u.gameObject.SetActive(false);
                }
            }
            else c.Find("Lv0").gameObject.SetActive(true);
            // No variant layout is left: drop a stale controller if one was baked in.
            var vv = inst.GetComponent<TheWaningBorder.Rendering.BuildingVariantVisual>();
            if (vv != null) Object.DestroyImmediate(vv);
            inst.name = Path.GetFileNameWithoutExtension(file);
            var saved = PrefabUtility.SaveAsPrefabAsset(inst, file);
            Object.DestroyImmediate(inst);
            return saved;
        }

        // ── audit ───────────────────────────────────────────────────────

        static void Audit(List<BuildingDefSO> buildings, List<BuildingLevelDefSO> levels)
        {
            _log.AppendLine("── audit: SOs without a model ──");
            foreach (var b in buildings)
            {
                if (b.prefab == null) _log.AppendLine($"  BASE  {b.id}  ({AssetDatabase.GetAssetPath(b)})");
                else if (Branches(b.prefab).Count > 1)
                    _log.AppendLine($"  BASE  {b.id} still a multi-variant prefab");
            }
            foreach (var l in levels)
                if (l.prefab == null) _log.AppendLine($"  LEVEL {l.name}  ({l.culture} {l.buildingId} L{l.level})");
        }
    }
}
