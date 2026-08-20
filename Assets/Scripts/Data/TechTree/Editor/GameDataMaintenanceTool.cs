// GameDataMaintenanceTool.cs
// EDITOR-ONLY tool: one-shot GameData maintenance pass.
// Part of: Data/TechTree/Editor/
//
//  1. NormalizeGameData  — moves/renames every UnitDefSO / BuildingDefSO into the
//     canonical <Culture>/<CleanName>/<CleanName>.asset layout (the scheme commit
//     3e8c1d9 applied by hand to Age 0 + Alanthor). Targets the parked
//     Runai(TBD) / Feraldis(TBD) folders and the flat Veilstone unit folder.
//     All moves go through AssetDatabase so GUIDs (and catalog references)
//     are preserved.
//
//  2. WireUnitVisuals — builds an AnimatorController per unit using the
//     standardized UnitAnimationSync parameters (IsMoving / IsAttacking /
//     IsDead / AttackSpeed [+ IsHealing for healers]) from the humanoid clips
//     shipped in the Kevin Iglesias "Human Animations" and ExplosiveLLC
//     "Warrior Pack Bundle 2" packs, saves a prefab variant of the matching
//     pack character into the unit's GameData folder, and assigns both onto
//     the unit's UnitDefSO (prefab + animatorController fields, which
//     TechCatalog registers by presentationId at startup).
//
// Run via menu:  Waning Border > Game Data > ...
// Or batch:      Unity -batchmode -quit -executeMethod
//                TheWaningBorder.Data.EditorTools.GameDataMaintenanceTool.RunAll
//
// Wrapped in #if UNITY_EDITOR because this project ships a single runtime
// asmdef (TheWaningBorder.Runtime) with no separate editor assembly.

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace TheWaningBorder.Data.EditorTools
{
    public static class GameDataMaintenanceTool
    {
        const string UnitsFolder     = "Assets/GameData/TechTree/Units";
        const string BuildingsFolder = "Assets/GameData/TechTree/Buildings";

        // Pack roots.
        const string KIClips   = "Assets/Kevin Iglesias/Human Animations/Animations/Male";
        const string KIDummies = "Assets/Kevin Iglesias/Human Animations/Unity Demo Scenes/Human Melee Animations/Prefabs/Characters";
        const string EX        = "Assets/ExplosiveLLC/Warrior Pack Bundle 2 FREE";

        /// <summary>Batch-mode entry point: normalize layout, then wire visuals.</summary>
        public static void RunAll()
        {
            NormalizeGameData();
            WireUnitVisuals();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GameDataMaintenance] RunAll complete.");
        }

        /// <summary>
        /// Batch-mode entry point for the realistic-visuals pass: URP materials on
        /// every unit prefab (the packs ship built-in-pipeline shaders that render
        /// blank under URP), real character meshes instead of the KI mannequins,
        /// a proper Worker prefab, and realistic forest trees.
        /// </summary>
        public static void RunVisualPass()
        {
            ApplyRealisticUnitVisuals();
            BuildRealisticTrees();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[GameDataMaintenance] RunVisualPass complete.");
        }

        // ────────────────────────────────────────────────────────────────────
        //  1. Layout normalization
        // ────────────────────────────────────────────────────────────────────

        [MenuItem("Waning Border/Game Data/Normalize GameData Layout")]
        public static void NormalizeGameData()
        {
            int moved = 0;
            moved += NormalizeFolder<UnitDefSO>(UnitsFolder, isBuilding: false);
            moved += NormalizeFolder<BuildingDefSO>(BuildingsFolder, isBuilding: true);

            // Drop the now-empty parking folders.
            foreach (var leftover in new[]
            {
                $"{UnitsFolder}/Runai(TBD)", $"{UnitsFolder}/Feraldis(TBD)",
                $"{BuildingsFolder}/Runai(TBD)", $"{BuildingsFolder}/Feraldis(TBD)",
            })
            {
                if (AssetDatabase.IsValidFolder(leftover) &&
                    AssetDatabase.FindAssets("", new[] { leftover }).Length == 0)
                {
                    AssetDatabase.DeleteAsset(leftover);
                    Debug.Log($"[GameDataMaintenance] Removed empty folder {leftover}");
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[GameDataMaintenance] NormalizeGameData done — {moved} asset(s) relocated.");
        }

        static int NormalizeFolder<T>(string root, bool isBuilding) where T : ScriptableObject
        {
            int moved = 0;
            foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath<T>(path);
                if (so == null) continue;

                string id = so is UnitDefSO u ? u.id : (so as BuildingDefSO)?.id;
                if (string.IsNullOrEmpty(id)) { Debug.LogWarning($"[GameDataMaintenance] {path} has empty id — skipped."); continue; }

                string clean  = CleanName(id);
                string folder = $"{root}/{CultureDiskFolder(id, isBuilding)}/{clean}";
                string target = $"{folder}/{clean}.asset";
                if (path == target) continue;

                if (AssetDatabase.LoadAssetAtPath<T>(target) != null)
                {
                    Debug.LogWarning($"[GameDataMaintenance] Target {target} already exists — left {path} in place.");
                    continue;
                }

                EnsureFolder(folder);

                // Rename first (updates the object's m_Name), then move.
                string dir = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
                if (System.IO.Path.GetFileNameWithoutExtension(path) != clean)
                {
                    string err = AssetDatabase.RenameAsset(path, clean);
                    if (!string.IsNullOrEmpty(err)) { Debug.LogError($"[GameDataMaintenance] Rename failed for {path}: {err}"); continue; }
                    path = $"{dir}/{clean}.asset";
                }
                if (path != target)
                {
                    string err = AssetDatabase.MoveAsset(path, target);
                    if (!string.IsNullOrEmpty(err)) { Debug.LogError($"[GameDataMaintenance] Move failed for {path}: {err}"); continue; }
                }
                Debug.Log($"[GameDataMaintenance] {id} -> {target}");
                moved++;
            }
            return moved;
        }

        /// <summary>Strip the culture prefix off an id: Runai_Acolyte -> Acolyte.</summary>
        internal static string CleanName(string id)
        {
            foreach (var prefix in new[] { "Runai_", "Alanthor_", "Feraldis_", "Sect_" })
                if (id.StartsWith(prefix)) { id = id.Substring(prefix.Length); break; }
            var chars = id.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-') chars[i] = '_';
            return new string(chars);
        }

        static readonly HashSet<string> BorderUnitIds = new HashSet<string>
        {
            "Crystalling", "Godsplinter", "Veilstinger"
        };

        // Unprefixed culture buildings (FiendstoneKeep intentionally stays in
        // Age 0 — it is one of the three Age 0 age-up choice buildings).
        static readonly Dictionary<string, string> BuildingCultureExceptions = new Dictionary<string, string>
        {
            { "KingsCourt",      "Alanthor" },
            { "ThessarasBazaar", "Runai"    },
        };

        /// <summary>On-disk culture folder for an id (matches the hand-applied scheme: "Age 0" has a space).</summary>
        internal static string CultureDiskFolder(string id, bool isBuilding)
        {
            if (id.StartsWith("Runai_"))    return "Runai";
            if (id.StartsWith("Alanthor_")) return "Alanthor";
            if (id.StartsWith("Feraldis_")) return "Feraldis";
            if (id.StartsWith("Sect_"))     return "Sect";
            if (!isBuilding && BorderUnitIds.Contains(id)) return "Veilstone";
            if (isBuilding && BuildingCultureExceptions.TryGetValue(id, out var culture)) return culture;
            return "Age 0";
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int lastSlash = path.LastIndexOf('/');
            string parent = path.Substring(0, lastSlash);
            string leaf = path.Substring(lastSlash + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ────────────────────────────────────────────────────────────────────
        //  2. Visual wiring
        // ────────────────────────────────────────────────────────────────────

        class WireEntry
        {
            public string UnitId;
            public int    ForcePid;          // set on the SO when its presentationId is 0
            public string SourcePrefab;      // null = keep the SO's existing prefab
            public string IdleClip;
            public string MoveClip;
            public string AttackClip;
            public string DeathClip;         // null = no death state (corpse pops as before)
            public bool   Healer;            // adds IsHealing and routes it to the Attack state
            public float  VisualYaw;         // non-zero = model authored facing the wrong way;
                                             // the prefab wraps it in a root rotated by this yaw
            public float  AttackCycleOffset; // 0..1 phase shift of the Attack state's loop, to
                                             // line the clip's contact frame up with the damage
                                             // tick (0.5 = start half a swing in)
            public string TexturePath;       // non-null = build {folder}/Materials/{Unit}.mat
                                             // (URP/Lit, _BaseMap = this texture) and put it on
                                             // every renderer (for FBXes whose material references
                                             // an external texture that never shipped)
        }

        static List<WireEntry> BuildWireTable()
        {
            const string ArcherAnims = UnitsFolder + "/Age 0/Archer/Animations";
            const string ScoutFbx    = UnitsFolder + "/Age 0/Scout/scout.fbx";
            string kiIdle    = $"{KIClips}/Idles/HumanM@Idle01.fbx";
            string kiRun     = $"{KIClips}/Movement/Run/HumanM@Run01_Forward.fbx";
            string kiDeath   = $"{KIClips}/Combat/HumanM@Death01.fbx";
            string ki1HIdle  = $"{KIClips}/Combat/1H/HumanM@CombatIdle1H01.fbx";
            string ki1HAtkR  = $"{KIClips}/Combat/1H/HumanM@Attack1H01_R.fbx";
            string ki1HAtkL  = $"{KIClips}/Combat/1H/HumanM@Attack1H01_L.fbx";
            string ki2HIdle  = $"{KIClips}/Combat/2H/HumanM@CombatIdle2H01.fbx";
            string ki2HAtk   = $"{KIClips}/Combat/2H/HumanM@Attack2H01.fbx";
            string kiPolIdle = $"{KIClips}/Combat/Polearm/HumanM@CombatIdlePolearm01.fbx";
            string kiPolAtk  = $"{KIClips}/Combat/Polearm/HumanM@AttackPolearm01.fbx";

            string exKnight  = $"{EX}/Knight Warrior Mecanim Animation Pack";
            string exArcher  = $"{EX}/Archer Warrior Mecanim Animation Pack";
            string exMage    = $"{EX}/Mage Warrior Mecanim Animation Pack";
            string ex2Handed = $"{EX}/2 Handed Warrior Mecanim Animation Pack";

            WireEntry KIMelee(string id, string dummy, string idle, string attack) => new WireEntry
            {
                UnitId = id, SourcePrefab = $"{KIDummies}/{dummy}.prefab",
                IdleClip = idle, MoveClip = kiRun, AttackClip = attack, DeathClip = kiDeath,
            };
            WireEntry EXPack(string id, string packRoot, string prefabName, string clipPrefix, string idleName) => new WireEntry
            {
                UnitId = id, SourcePrefab = $"{packRoot}/Prefabs/{prefabName}.prefab",
                IdleClip = $"{packRoot}/Animations/{clipPrefix}@{idleName}.FBX",
                MoveClip = $"{packRoot}/Animations/{clipPrefix}@Run.FBX",
                AttackClip = $"{packRoot}/Animations/{clipPrefix}@Attack1.FBX",
            };

            var table = new List<WireEntry>
            {
                // Age 0 — Archer already has its prefab + Mixamo clips in-folder;
                // it was only missing the controller.
                new WireEntry
                {
                    UnitId = "Archer",
                    IdleClip   = $"{ArcherAnims}/unarmed idle 01.fbx",
                    MoveClip   = $"{ArcherAnims}/standing run forward.fbx",
                    AttackClip = $"{ArcherAnims}/standing aim recoil.fbx",
                    DeathClip  = $"{ArcherAnims}/standing death backward 01.fbx",
                },
                // Scout ships its own multi-take model (scout.fbx) — the fbx is
                // both the visual source and the clip source. Sprint (not walk)
                // for Move: the scout is the fast recon unit.
                new WireEntry
                {
                    UnitId = "Scout",
                    SourcePrefab = ScoutFbx,
                    // Torch idle is the scout's signature default stance.
                    IdleClip   = ScoutFbx + "#Idle_Torch_Loop",
                    MoveClip   = ScoutFbx + "#Sprint_Loop",
                    AttackClip = ScoutFbx + "#Sword_Attack",
                    DeathClip  = ScoutFbx + "#Death01",
                    // Model is authored facing -Z (walked backwards in-game).
                    VisualYaw  = 180f,
                    // The swing's contact frame sits half a cycle away from the
                    // damage tick — start the loop half a swing in.
                    AttackCycleOffset = 0.5f,
                    // The fbx material points at Desktop/scout.fbm — use the
                    // texture imported into the unit's Textures folder instead.
                    TexturePath = UnitsFolder + "/Age 0/Scout/Textures/Characters_Texture_Black.png",
                },

                // Alanthor (defense culture: armored knights)
                KIMelee("Alanthor_Swordsman", "HumanM_Dummy_Red - Sword and Shield", ki1HIdle, ki1HAtkR),
                EXPack("Alanthor_Sentinel", exKnight, "Knight Warrior", "Knight", "Idle"),
                EXPack("Alanthor_Crossbowman", exArcher, "Archer Warrior", "Archer", "Idle1"),
                EXPack("Alanthor_Scholar", exMage, "Mage Warrior", "Mage", "Idle"),

                // Runai
                KIMelee("Runai_Spearman", "HumanM_Dummy_Red - Polearm", kiPolIdle, kiPolAtk),
                EXPack("Runai_Skirmisher", exArcher, "Archer Warrior", "Archer", "Idle1"),
                KIMelee("Runai_Raider", "HumanM_Dummy_Red - Dual Wield", $"{KIClips}/Combat/HumanM@CombatIdle01.fbx", ki1HAtkL),
                EXPack("Runai_Acolyte", exMage, "Mage Warrior", "Mage", "Idle"),

                // Feraldis
                EXPack("Feraldis_Berserker", ex2Handed, "2Handed Warrior", "2Handed", "Idle"),
                EXPack("Feraldis_Hunter", exArcher, "Archer Warrior", "Archer", "Idle1"),
                KIMelee("Feraldis_Raider", "HumanM_Dummy_Red - Greatsword", ki2HIdle, ki2HAtk),
                EXPack("Feraldis_Iconoclast", exMage, "Mage Warrior", "Mage", "Idle"),
            };

            // Litharch is the Age 0 healer: mage visuals + IsHealing parameter.
            var litharch = EXPack("Litharch", exMage, "Mage Warrior", "Mage", "Idle");
            litharch.Healer = true;
            table.Add(litharch);

            // Alanthor_Swordsman falls through UnitFactory.GetPresentationId's
            // default arm (201) but its SO still has presentationId 0, so the
            // catalog never registers its visuals. Align the SO with the factory.
            table.First(e => e.UnitId == "Alanthor_Swordsman").ForcePid = 201;

            return table;
        }

        /// <summary>Resolve SOs by their id field, not by path (normalization may have just moved them).</summary>
        static Dictionary<string, UnitDefSO> ResolveUnitSOs()
        {
            var byId = new Dictionary<string, UnitDefSO>();
            foreach (var guid in AssetDatabase.FindAssets("t:UnitDefSO", new[] { UnitsFolder }))
            {
                var so = AssetDatabase.LoadAssetAtPath<UnitDefSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (so != null && !string.IsNullOrEmpty(so.id) && !byId.ContainsKey(so.id)) byId[so.id] = so;
            }
            return byId;
        }

        /// <summary>
        /// Wire ONLY the Scout from the current table — used after dropping in a
        /// new scout model so the other units' controllers aren't rebuilt.
        /// </summary>
        [MenuItem("Waning Border/Game Data/Wire Scout Visuals (new model)")]
        public static void WireScoutVisuals()
        {
            if (!ResolveUnitSOs().TryGetValue("Scout", out var so))
            {
                Debug.LogWarning("[GameDataMaintenance] No UnitDefSO with id 'Scout' found.");
                return;
            }
            var entry = BuildWireTable().First(e => e.UnitId == "Scout");
            bool ok = false;
            try { ok = WireOne(so, entry); }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GameDataMaintenance] WireScoutVisuals threw: {ex}");
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameDataMaintenance] WireScoutVisuals {(ok ? "done" : "FAILED — see messages above")}.");
        }

        // ── Alanthor Catapult (replaced the Ballista 2026-08-02) ─────────────
        const string CatapultFxSource = "Assets/Synty/PolygonFantasyKingdom/Prefabs/FX/FX_Catapult_Single_01.prefab";
        const string CatapultFxDest   = "Assets/Resources/Prefabs/Effects/FX_CatapultShot.prefab";
        const string CatapultSource   = "Assets/Synty/PolygonFantasyKingdom/Prefabs/SiegeEngines/SM_Wep_Catapult_01.prefab";

        /// <summary>
        /// Wire the Alanthor Catapult: copy the Synty shot FX into Resources
        /// (ProjectileVisualSystem loads "Prefabs/Effects/FX_CatapultShot"),
        /// build the unit prefab from the Synty siege engine with the
        /// CatapultVisual arm driver, and assign it on the UnitDefSO.
        /// </summary>
        [MenuItem("Waning Border/Game Data/Wire Catapult Visuals (Alanthor)")]
        public static void WireCatapultVisuals()
        {
            try
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(CatapultFxDest) == null)
                {
                    EnsureFolder("Assets/Resources/Prefabs/Effects");
                    if (!AssetDatabase.CopyAsset(CatapultFxSource, CatapultFxDest))
                    {
                        Debug.LogError($"[GameDataMaintenance] Could not copy {CatapultFxSource} -> {CatapultFxDest}");
                        return;
                    }
                }

                if (!ResolveUnitSOs().TryGetValue("Alanthor_Catapult", out var so))
                {
                    Debug.LogWarning("[GameDataMaintenance] No UnitDefSO with id 'Alanthor_Catapult' found.");
                    return;
                }

                var source = AssetDatabase.LoadAssetAtPath<GameObject>(CatapultSource);
                if (source == null)
                {
                    Debug.LogError($"[GameDataMaintenance] Synty catapult prefab missing: {CatapultSource}");
                    return;
                }

                string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(so)).Replace('\\', '/');
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                var vis = instance.GetComponent<TheWaningBorder.Presentation.CatapultVisual>();
                if (vis == null)
                    vis = instance.AddComponent<TheWaningBorder.Presentation.CatapultVisual>();

                // Nest the shot FX as an inactive template child so its
                // particle settings are tweakable as overrides on this prefab.
                // localScale stays 1 (spawned shots clone the template's local
                // scale as their world scale, so the 0.7 root must not leak in).
                var fxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CatapultFxDest);
                if (fxPrefab != null && vis.ShotFxTemplate == null)
                {
                    var fxChild = (GameObject)PrefabUtility.InstantiatePrefab(fxPrefab);
                    fxChild.name = "ShotFxTemplate";
                    fxChild.transform.SetParent(instance.transform, false);
                    fxChild.transform.localPosition = new Vector3(0f, 2.4f, 1.2f);
                    fxChild.SetActive(false);
                    vis.ShotFxTemplate = fxChild;
                }

                // The Synty engine reads oversized in-game — author it at 70 %.
                // PresentationSpawnSystem preserves the prefab's root scale via
                // ProceduralScaleTag.
                instance.transform.localScale = Vector3.one * 0.7f;
                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, $"{folder}/Catapult.prefab");
                Object.DestroyImmediate(instance);

                so.prefab = prefab;
                EditorUtility.SetDirty(so);
                AssetDatabase.SaveAssets();
                Debug.Log($"[GameDataMaintenance] WireCatapultVisuals done — prefab at {folder}/Catapult.prefab, FX at {CatapultFxDest}.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GameDataMaintenance] WireCatapultVisuals threw: {ex}");
            }
        }

        // ── Siege visual split (2026-08-16) ──────────────────────────────────
        // The Alanthor Ballista shipped wearing the Synty CATAPULT model as a
        // placeholder, and the Runai Catapult had no visual at all (its SO
        // carried presentationId 0 and no prefab — capsule fallback in game).
        const string BallistaSource = "Assets/Synty/PolygonFantasyKingdom/Prefabs/SiegeEngines/SM_Wep_Ballista_Mobile_01.prefab";

        /// <summary>
        /// Give the Ballista its own art (SM_Wep_Ballista_Mobile_01, saved
        /// over Ballista.prefab so the SO/guid references hold; no arm driver
        /// — the bolt is entity-rendered and impact-synced), and build the
        /// Runai Catapult's engine prefab (Synty catapult + CatapultVisual +
        /// nested shot FX) with presentationId 333 wired on its SO.
        /// </summary>
        [MenuItem("Waning Border/Game Data/Wire Siege Visuals (Ballista + Runai Catapult)")]
        public static void WireSiegeVisuals()
        {
            try
            {
                var sos = ResolveUnitSOs();

                // 1) Alanthor Ballista — real ballista art.
                if (!sos.TryGetValue("Alanthor_Ballista", out var ballistaSo))
                {
                    Debug.LogWarning("[GameDataMaintenance] No UnitDefSO with id 'Alanthor_Ballista' found.");
                }
                else
                {
                    var src = AssetDatabase.LoadAssetAtPath<GameObject>(BallistaSource);
                    if (src == null)
                    {
                        Debug.LogError($"[GameDataMaintenance] Synty ballista prefab missing: {BallistaSource}");
                    }
                    else
                    {
                        string folder = System.IO.Path.GetDirectoryName(
                            AssetDatabase.GetAssetPath(ballistaSo)).Replace('\\', '/');
                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
                        // Match the siege authoring scale convention (the Synty
                        // engines read oversized in-game at scale 1).
                        inst.transform.localScale = Vector3.one * 0.7f;
                        var prefab = PrefabUtility.SaveAsPrefabAsset(inst, $"{folder}/Ballista.prefab");
                        Object.DestroyImmediate(inst);
                        ballistaSo.prefab = prefab;
                        EditorUtility.SetDirty(ballistaSo);
                        Debug.Log($"[GameDataMaintenance] Ballista now wears its own art — {folder}/Ballista.prefab");
                    }
                }

                // 2) Runai Catapult — the engine the Alanthor wire builds, on
                //    the Runai SO, with the presentation id the recipe table
                //    registers for Runai_Catapult (333).
                if (!sos.TryGetValue("Runai_Catapult", out var catSo))
                {
                    Debug.LogWarning("[GameDataMaintenance] No UnitDefSO with id 'Runai_Catapult' found.");
                }
                else
                {
                    if (AssetDatabase.LoadAssetAtPath<GameObject>(CatapultFxDest) == null)
                    {
                        EnsureFolder("Assets/Resources/Prefabs/Effects");
                        if (!AssetDatabase.CopyAsset(CatapultFxSource, CatapultFxDest))
                            Debug.LogError($"[GameDataMaintenance] Could not copy {CatapultFxSource} -> {CatapultFxDest}");
                    }

                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(CatapultSource);
                    if (source == null)
                    {
                        Debug.LogError($"[GameDataMaintenance] Synty catapult prefab missing: {CatapultSource}");
                    }
                    else
                    {
                        string folder = System.IO.Path.GetDirectoryName(
                            AssetDatabase.GetAssetPath(catSo)).Replace('\\', '/');
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                        var vis = instance.GetComponent<TheWaningBorder.Presentation.CatapultVisual>();
                        if (vis == null)
                            vis = instance.AddComponent<TheWaningBorder.Presentation.CatapultVisual>();

                        var fxPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CatapultFxDest);
                        if (fxPrefab != null && vis.ShotFxTemplate == null)
                        {
                            var fxChild = (GameObject)PrefabUtility.InstantiatePrefab(fxPrefab);
                            fxChild.name = "ShotFxTemplate";
                            fxChild.transform.SetParent(instance.transform, false);
                            fxChild.transform.localPosition = new Vector3(0f, 2.4f, 1.2f);
                            fxChild.SetActive(false);
                            vis.ShotFxTemplate = fxChild;
                        }

                        instance.transform.localScale = Vector3.one * 0.7f;
                        var prefab = PrefabUtility.SaveAsPrefabAsset(instance, $"{folder}/Catapult.prefab");
                        Object.DestroyImmediate(instance);

                        catSo.prefab = prefab;
                        catSo.presentationId = 333;
                        EditorUtility.SetDirty(catSo);
                        Debug.Log($"[GameDataMaintenance] Runai Catapult wired — {folder}/Catapult.prefab, presentationId 333");
                    }
                }

                AssetDatabase.SaveAssets();
                Debug.Log("[GameDataMaintenance] WireSiegeVisuals done.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GameDataMaintenance] WireSiegeVisuals threw: {ex}");
            }
        }

        // ── Ledger automaton (visual identity 2026-08-02) ────────────────────
        // Floating legless automaton: open-frame torso exposing a central
        // crystal (tinted the player's color at runtime by LedgerVisual),
        // four arms, cogwheels, and a forcefield disc underneath. Built from
        // primitives — the project ships no gear meshes.

        [MenuItem("Waning Border/Game Data/Wire Ledger Visuals (automaton)")]
        public static void WireLedgerVisuals()
        {
            try
            {
                string folder = "Assets/GameData/TechTree/Units/Alanthor/Ledger";
                string matFolder = $"{folder}/Materials";
                if (!AssetDatabase.IsValidFolder(matFolder)) AssetDatabase.CreateFolder(folder, "Materials");

                Material MakeMat(string name, Color c, float metallic, float smooth)
                {
                    string path = $"{matFolder}/{name}.mat";
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m == null)
                    {
                        m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        AssetDatabase.CreateAsset(m, path);
                    }
                    m.SetColor("_BaseColor", c);
                    m.SetFloat("_Metallic", metallic);
                    m.SetFloat("_Smoothness", smooth);
                    EditorUtility.SetDirty(m);
                    return m;
                }

                var steel   = MakeMat("LedgerSteel", new Color(0.52f, 0.54f, 0.58f), 0.85f, 0.6f);
                var brass   = MakeMat("LedgerBrass", new Color(0.62f, 0.47f, 0.22f), 0.9f, 0.55f);
                var crystal = MakeMat("LedgerCrystal", new Color(0.85f, 0.9f, 1f), 0.1f, 0.9f);
                crystal.EnableKeyword("_EMISSION");
                crystal.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

                var field = MakeMat("LedgerForcefield", new Color(0.4f, 0.8f, 1f, 0.3f), 0f, 0.8f);
                field.SetFloat("_Surface", 1f);
                field.SetOverrideTag("RenderType", "Transparent");
                field.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                field.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                field.SetInt("_ZWrite", 0);
                field.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                field.renderQueue = 3000;
                EditorUtility.SetDirty(field);

                GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 pos,
                                Vector3 scale, Material mat, Vector3? euler = null)
                {
                    var go = GameObject.CreatePrimitive(type);
                    go.name = name;
                    Object.DestroyImmediate(go.GetComponent<Collider>());
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = pos;
                    go.transform.localScale = scale;
                    if (euler.HasValue) go.transform.localEulerAngles = euler.Value;
                    go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                    return go;
                }

                // Cogwheel: flat cylinder plus tooth boxes around the rim.
                // Spins around local Z (LedgerVisual rotates Space.Self Z).
                GameObject Cog(string name, Transform parent, Vector3 pos, float radius,
                               Vector3 euler)
                {
                    var pivot = new GameObject(name);
                    pivot.transform.SetParent(parent, false);
                    pivot.transform.localPosition = pos;
                    pivot.transform.localEulerAngles = euler;
                    float thick = radius * 0.35f;
                    Part(PrimitiveType.Cylinder, "Disc", pivot.transform, Vector3.zero,
                         new Vector3(radius * 2f, thick * 0.5f, radius * 2f), brass,
                         new Vector3(90f, 0f, 0f)); // cylinder Y-axis -> local Z
                    const int teeth = 8;
                    for (int i = 0; i < teeth; i++)
                    {
                        float a = i * (360f / teeth);
                        var dir = Quaternion.Euler(0f, 0f, a) * Vector3.up;
                        Part(PrimitiveType.Cube, "Tooth", pivot.transform,
                             dir * radius, new Vector3(radius * 0.28f, radius * 0.28f, thick),
                             brass, new Vector3(0f, 0f, a));
                    }
                    return pivot;
                }

                var root = new GameObject("Ledger");
                var body = new GameObject("Body");
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 1.0f, 0f);

                // Open-frame torso: top/bottom discs joined by four corner
                // struts, the crystal floating exposed in the middle.
                Part(PrimitiveType.Cylinder, "TorsoTop", body.transform,
                     new Vector3(0f, 0.32f, 0f), new Vector3(1.0f, 0.09f, 1.0f), steel);
                Part(PrimitiveType.Cylinder, "TorsoBottom", body.transform,
                     new Vector3(0f, -0.32f, 0f), new Vector3(1.0f, 0.09f, 1.0f), steel);
                Part(PrimitiveType.Sphere, "Dome", body.transform,
                     new Vector3(0f, 0.52f, 0f), new Vector3(0.62f, 0.4f, 0.62f), steel);
                for (int i = 0; i < 4; i++)
                {
                    float a = 45f + i * 90f;
                    var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                    Part(PrimitiveType.Cube, $"Strut{i}", body.transform,
                         dir * 0.42f, new Vector3(0.07f, 0.58f, 0.07f), brass);
                }

                Part(PrimitiveType.Cube, "Crystal", body.transform, Vector3.zero,
                     new Vector3(0.2f, 0.34f, 0.2f), crystal, new Vector3(45f, 30f, 45f));

                // Four arms at 90-degree stations: shoulder, upper arm angled
                // out-down, elbow, forearm angled in-down, hand.
                for (int i = 0; i < 4; i++)
                {
                    float a = i * 90f;
                    var station = new GameObject($"Arm{i}");
                    station.transform.SetParent(body.transform, false);
                    station.transform.localEulerAngles = new Vector3(0f, a, 0f);
                    float droop = 20f + (i % 2) * 14f; // pose variety per pair

                    Part(PrimitiveType.Sphere, "Shoulder", station.transform,
                         new Vector3(0.56f, 0.25f, 0f), Vector3.one * 0.18f, steel);
                    Part(PrimitiveType.Cylinder, "UpperArm", station.transform,
                         new Vector3(0.74f, 0.08f, 0f), new Vector3(0.09f, 0.21f, 0.09f),
                         brass, new Vector3(0f, 0f, 55f + droop));
                    Part(PrimitiveType.Sphere, "Elbow", station.transform,
                         new Vector3(0.9f, -0.08f, 0f), Vector3.one * 0.13f, steel);
                    Part(PrimitiveType.Cylinder, "Forearm", station.transform,
                         new Vector3(0.95f, -0.31f, 0f), new Vector3(0.075f, 0.2f, 0.075f),
                         brass, new Vector3(0f, 0f, 12f + droop * 0.5f));
                    Part(PrimitiveType.Sphere, "Hand", station.transform,
                         new Vector3(0.99f, -0.52f, 0f), Vector3.one * 0.15f, steel);
                }

                // Cogwheels — LedgerVisual spins everything named "Cog*".
                Cog("Cog_Back", body.transform, new Vector3(0f, 0.05f, -0.52f), 0.26f, Vector3.zero);
                Cog("Cog_Left", body.transform, new Vector3(-0.5f, 0.32f, 0f), 0.15f, new Vector3(0f, 90f, 0f));
                Cog("Cog_Right", body.transform, new Vector3(0.5f, 0.32f, 0f), 0.15f, new Vector3(0f, 90f, 0f));
                Cog("Cog_Dome", body.transform, new Vector3(0.18f, 0.62f, 0.18f), 0.1f, new Vector3(90f, 45f, 0f));
                Cog("Cog_Belly", body.transform, new Vector3(0f, -0.45f, 0.3f), 0.12f, new Vector3(60f, 0f, 0f));

                // Forcefield disc under the body — LedgerVisual spins it,
                // breathes its alpha, tints it, and gives it the hum.
                Part(PrimitiveType.Cylinder, "Forcefield", root.transform,
                     new Vector3(0f, 0.16f, 0f), new Vector3(1.5f, 0.015f, 1.5f), field);

                root.AddComponent<TheWaningBorder.Presentation.LedgerVisual>();

                EnsureFolder("Assets/Resources/Prefabs/Units");
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/Prefabs/Units/Ledger.prefab");
                Object.DestroyImmediate(root);
                AssetDatabase.SaveAssets();
                Debug.Log($"[GameDataMaintenance] WireLedgerVisuals done — {(prefab != null ? "prefab saved" : "SAVE FAILED")} at Resources/Prefabs/Units/Ledger.prefab.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GameDataMaintenance] WireLedgerVisuals threw: {ex}");
            }
        }

        // ── Temple of Ridan + sect chapels (visual rework 2026-08-02) ────────
        // Seven-sided cathedral mixing all three culture styles (Alanthor
        // slate walls, Runai sandstone bands/trim, Feraldis iron pylons and
        // roof), a LARGE door on face 0 and a tall central pinnacle. Six
        // chapels (two per affinity culture) with unique identifiable roofs:
        //   Renewal   (Alanthor) — steep gabled slate roof, copper ridge
        //   Antiquity (Alanthor) — stepped ziggurat roof
        //   Justice   (Runai)    — golden dome on a sandstone drum
        //   Silence   (Runai)    — tall slender tent-spire, teal accent
        //   War       (Feraldis) — jagged horned iron roof
        //   Ash       (Feraldis) — flat roof with ember-lit smokestack

        [MenuItem("Waning Border/Game Data/Wire Temple + Chapel Visuals")]
        public static void WireTempleAndChapelVisuals()
        {
            try
            {
                string matFolder = "Assets/GameData/TechTree/Buildings/Age 0/ShrineOfRidan/Materials";
                if (!AssetDatabase.IsValidFolder(matFolder))
                    AssetDatabase.CreateFolder("Assets/GameData/TechTree/Buildings/Age 0/ShrineOfRidan", "Materials");

                Material Mat(string name, Color c, float metallic, float smooth, Color? emission = null)
                {
                    string path = $"{matFolder}/{name}.mat";
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (m == null)
                    {
                        m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        AssetDatabase.CreateAsset(m, path);
                    }
                    m.SetColor("_BaseColor", c);
                    m.SetFloat("_Metallic", metallic);
                    m.SetFloat("_Smoothness", smooth);
                    if (emission.HasValue)
                    {
                        m.EnableKeyword("_EMISSION");
                        m.SetColor("_EmissionColor", emission.Value);
                    }
                    EditorUtility.SetDirty(m);
                    return m;
                }

                // Culture palette.
                var slate  = Mat("TempleSlate", new Color(0.87f, 0.88f, 0.92f), 0.05f, 0.4f);   // Alanthor
                var copper = Mat("TempleCopper", new Color(0.36f, 0.6f, 0.5f), 0.7f, 0.55f);
                var sand   = Mat("TempleSand", new Color(0.82f, 0.67f, 0.41f), 0.05f, 0.35f);   // Runai
                var gold   = Mat("TempleGold", new Color(0.85f, 0.66f, 0.24f), 0.9f, 0.7f);
                var teal   = Mat("TempleTeal", new Color(0.22f, 0.58f, 0.58f), 0.3f, 0.55f);
                var timber = Mat("TempleTimber", new Color(0.21f, 0.16f, 0.12f), 0.05f, 0.25f); // Feraldis
                var iron   = Mat("TempleIron", new Color(0.26f, 0.27f, 0.3f), 0.85f, 0.5f);
                var ember  = Mat("TempleEmber", new Color(0.4f, 0.12f, 0.05f), 0.1f, 0.3f,
                                 new Color(1f, 0.35f, 0.1f) * 1.8f);
                var dark   = Mat("TempleDoorDark", new Color(0.07f, 0.06f, 0.08f), 0.1f, 0.2f);
                var lumen  = Mat("TempleLumen", new Color(0.95f, 0.92f, 0.8f), 0.2f, 0.9f,
                                 new Color(1f, 0.9f, 0.6f) * 2f);

                // Statue stones (chapels are sect statues, 2026-08-02):
                // white marble for Alanthor, red sandstone for Runaii,
                // obsidian for Feraldis; blue crystal for held relics.
                var marble    = Mat("StatueMarble", new Color(0.93f, 0.93f, 0.95f), 0.05f, 0.55f);
                var redsand   = Mat("StatueRedSandstone", new Color(0.63f, 0.27f, 0.18f), 0.05f, 0.3f);
                var obsidian  = Mat("StatueObsidian", new Color(0.09f, 0.08f, 0.11f), 0.2f, 0.85f);
                var blueCrys  = Mat("StatueBlueCrystal", new Color(0.55f, 0.75f, 1f), 0.1f, 0.9f,
                                    new Color(0.35f, 0.6f, 1f) * 2.2f);

                GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 pos,
                                Vector3 scale, Material mat, Vector3? euler = null)
                {
                    var go = GameObject.CreatePrimitive(type);
                    go.name = name;
                    Object.DestroyImmediate(go.GetComponent<Collider>());
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = pos;
                    go.transform.localScale = scale;
                    if (euler.HasValue) go.transform.localEulerAngles = euler.Value;
                    go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                    return go;
                }

                // ═══ TEMPLE — 7-sided cathedral, LEVELED (_al_1.._al_4) ═══
                // The temple's live visual pipeline is the leveled swap set
                // (Prefabs/Buildings/TempleOfRidan_al_<level>, consumed by
                // BuildingPrefabSwapSystem on upgrade). The cathedral is the
                // TEMPLE's look — the pre-upgrade Shrine keeps its own visual,
                // so the old base override is deleted below.
                const int Sides = 7;
                const float Radius = 3.4f;                                  // fits the 7x7 footprint
                float apothem = Radius * Mathf.Cos(Mathf.PI / Sides);       // ~3.06
                float side = 2f * Radius * Mathf.Sin(Mathf.PI / Sides);     // ~2.95
                const float WallH = 4.2f;

                GameObject BuildCathedral(int level)
                {
                    var t = new GameObject($"TempleOfRidan_al_{level}");
                    float pinnacleShaftH = 1.3f + level * 0.55f;            // grows with level

                    // Base plinth ring.
                    Part(PrimitiveType.Cylinder, "Plinth", t.transform,
                         new Vector3(0f, 0.13f, 0f), new Vector3(7.6f, 0.13f, 7.6f), slate);

                    for (int i = 0; i < Sides; i++)
                    {
                        float a = i * (360f / Sides);                       // face 0 → +Z
                        var rot = Quaternion.Euler(0f, a, 0f);
                        var dir = rot * Vector3.forward;

                        // Alanthor slate wall + cornice + Runai base band.
                        Part(PrimitiveType.Cube, $"Wall{i}", t.transform,
                             dir * apothem + Vector3.up * (WallH * 0.5f + 0.25f),
                             new Vector3(side, WallH, 0.3f), slate, new Vector3(0f, a, 0f));
                        Part(PrimitiveType.Cube, $"Cornice{i}", t.transform,
                             dir * (apothem + 0.07f) + Vector3.up * (WallH + 0.32f),
                             new Vector3(side + 0.25f, 0.22f, 0.44f), sand, new Vector3(0f, a, 0f));
                        Part(PrimitiveType.Cube, $"BaseBand{i}", t.transform,
                             dir * (apothem + 0.05f) + Vector3.up * 0.62f,
                             new Vector3(side, 0.35f, 0.4f), sand, new Vector3(0f, a, 0f));
                        if (level >= 3)
                            Part(PrimitiveType.Cube, $"GildBand{i}", t.transform,
                                 dir * (apothem + 0.08f) + Vector3.up * 3.5f,
                                 new Vector3(side, 0.14f, 0.42f), gold, new Vector3(0f, a, 0f));

                        // Two tall arched lumen windows per non-door face.
                        if (i != 0)
                        {
                            for (int w = -1; w <= 1; w += 2)
                            {
                                var wPos = rot * new Vector3(w * 0.75f, 0f, apothem + 0.13f);
                                Part(PrimitiveType.Cube, $"WinFrame{i}_{w}", t.transform,
                                     wPos + Vector3.up * 2.25f, new Vector3(0.6f, 1.9f, 0.14f),
                                     sand, new Vector3(0f, a, 0f));
                                Part(PrimitiveType.Cube, $"Win{i}_{w}", t.transform,
                                     wPos + Vector3.up * 2.25f + rot * new Vector3(0f, 0f, 0.04f),
                                     new Vector3(0.42f, 1.7f, 0.12f), lumen, new Vector3(0f, a, 0f));
                                Part(PrimitiveType.Cube, $"WinArch{i}_{w}", t.transform,
                                     wPos + Vector3.up * 3.35f, new Vector3(0.42f, 0.42f, 0.13f),
                                     sand, new Vector3(0f, a, 45f));
                            }
                        }

                        // Feraldis buttress at each vertex: pylon + angled brace + cap.
                        float va = a + 180f / Sides;
                        var vrot = Quaternion.Euler(0f, va, 0f);
                        var vdir = vrot * Vector3.forward;
                        Part(PrimitiveType.Cube, $"Pylon{i}", t.transform,
                             vdir * Radius + Vector3.up * (WallH * 0.5f + 0.3f),
                             new Vector3(0.44f, WallH + 0.5f, 0.44f), iron, new Vector3(0f, va, 0f));
                        Part(PrimitiveType.Cube, $"Brace{i}", t.transform,
                             vdir * (Radius + 0.28f) + Vector3.up * 1.35f,
                             new Vector3(0.3f, 2.3f, 0.3f), timber,
                             vrot.eulerAngles + new Vector3(-16f, 0f, 0f));
                        Part(PrimitiveType.Cube, $"PylonCap{i}", t.transform,
                             vdir * Radius + Vector3.up * (WallH + 0.72f),
                             new Vector3(0.56f, 0.22f, 0.56f), sand, new Vector3(0f, va, 0f));

                        // Corner spirelets from level 2.
                        if (level >= 2)
                        {
                            Part(PrimitiveType.Cube, $"Spirelet{i}", t.transform,
                                 vdir * Radius + Vector3.up * (WallH + 1.25f),
                                 new Vector3(0.24f, 0.9f, 0.24f), iron, new Vector3(0f, va, 0f));
                            Part(PrimitiveType.Cube, $"SpireletTip{i}", t.transform,
                                 vdir * Radius + Vector3.up * (WallH + 1.8f),
                                 new Vector3(0.16f, 0.24f, 0.16f), lumen, new Vector3(45f, va, 45f));
                        }

                        // Lower roof ring (iron), sloping DOWN toward the walls
                        // (+X pitch drops the outward edge; -X made it read
                        // upside-down).
                        Part(PrimitiveType.Cube, $"RoofLow{i}", t.transform,
                             dir * (apothem * 0.63f) + Vector3.up * (WallH + 0.86f),
                             new Vector3(side + 0.55f, 0.12f, 2.5f), iron, new Vector3(33f, a, 0f));

                        // 7-sided clerestory drum with lumen slits.
                        float side2 = 2f * 1.75f * Mathf.Sin(Mathf.PI / Sides);
                        float apo2 = 1.75f * Mathf.Cos(Mathf.PI / Sides);
                        Part(PrimitiveType.Cube, $"Drum{i}", t.transform,
                             dir * apo2 + Vector3.up * (WallH + 1.85f),
                             new Vector3(side2, 1.1f, 0.2f), slate, new Vector3(0f, a, 0f));
                        Part(PrimitiveType.Cube, $"DrumWin{i}", t.transform,
                             dir * (apo2 + 0.08f) + Vector3.up * (WallH + 1.85f),
                             new Vector3(0.28f, 0.7f, 0.1f), lumen, new Vector3(0f, a, 0f));

                        // Upper roof ring to the apex (same +X downward pitch).
                        Part(PrimitiveType.Cube, $"RoofHigh{i}", t.transform,
                             dir * (apo2 * 0.55f) + Vector3.up * (WallH + 2.85f),
                             new Vector3(side2 + 0.4f, 0.1f, 1.7f), iron, new Vector3(40f, a, 0f));
                    }

                    // ── Grand portal on face 0 (+Z): steps, columns, arch. ──
                    for (int s = 0; s < 3; s++)
                        Part(PrimitiveType.Cube, $"Step{s}", t.transform,
                             new Vector3(0f, 0.09f + s * 0.12f, apothem + 0.85f - s * 0.3f),
                             new Vector3(3.4f - s * 0.5f, 0.14f, 0.75f), slate);
                    Part(PrimitiveType.Cube, "Door", t.transform,
                         new Vector3(0f, 1.75f, apothem + 0.1f), new Vector3(1.8f, 3.0f, 0.14f), dark);
                    Part(PrimitiveType.Cube, "DoorBandTop", t.transform,
                         new Vector3(0f, 2.55f, apothem + 0.18f), new Vector3(1.8f, 0.12f, 0.1f), iron);
                    Part(PrimitiveType.Cube, "DoorBandBot", t.transform,
                         new Vector3(0f, 1.0f, apothem + 0.18f), new Vector3(1.8f, 0.12f, 0.1f), iron);
                    for (int cSide = -1; cSide <= 1; cSide += 2)
                    {
                        Part(PrimitiveType.Cylinder, $"Column{cSide}", t.transform,
                             new Vector3(cSide * 1.25f, 1.75f, apothem + 0.4f),
                             new Vector3(0.26f, 1.6f, 0.26f), sand);
                        Part(PrimitiveType.Cube, $"Capital{cSide}", t.transform,
                             new Vector3(cSide * 1.25f, 3.45f, apothem + 0.4f),
                             new Vector3(0.42f, 0.2f, 0.42f), gold);
                    }
                    Part(PrimitiveType.Cube, "PortalLintel", t.transform,
                         new Vector3(0f, 3.7f, apothem + 0.35f), new Vector3(3.0f, 0.35f, 0.4f), sand);
                    Part(PrimitiveType.Cube, "PortalArchL", t.transform,
                         new Vector3(-0.85f, 4.05f, apothem + 0.35f), new Vector3(0.5f, 0.5f, 0.35f), sand,
                         new Vector3(0f, 0f, 45f));
                    Part(PrimitiveType.Cube, "PortalArchR", t.transform,
                         new Vector3(0.85f, 4.05f, apothem + 0.35f), new Vector3(0.5f, 0.5f, 0.35f), sand,
                         new Vector3(0f, 0f, 45f));
                    Part(PrimitiveType.Cube, "PortalGem", t.transform,
                         new Vector3(0f, 4.15f, apothem + 0.38f), new Vector3(0.3f, 0.3f, 0.2f), lumen,
                         new Vector3(0f, 0f, 45f));

                    // ── TALL central pinnacle (tri-culture stack, grows per level). ──
                    float py = WallH + 3.35f;
                    Part(PrimitiveType.Cylinder, "PinnacleCollar", t.transform,
                         new Vector3(0f, py, 0f), new Vector3(1.5f, 0.14f, 1.5f), iron);
                    Part(PrimitiveType.Cylinder, "PinnacleDrum", t.transform,
                         new Vector3(0f, py + 0.35f, 0f), new Vector3(1.2f, 0.25f, 1.2f), sand);
                    Part(PrimitiveType.Cylinder, "PinnacleShaft", t.transform,
                         new Vector3(0f, py + 0.6f + pinnacleShaftH * 0.5f, 0f),
                         new Vector3(0.68f, pinnacleShaftH * 0.5f, 0.68f), slate);
                    float topY = py + 0.6f + pinnacleShaftH;
                    if (level >= 3)
                        Part(PrimitiveType.Cylinder, "PinnacleGildRing", t.transform,
                             new Vector3(0f, topY + 0.06f, 0f), new Vector3(0.9f, 0.06f, 0.9f), gold);
                    Part(PrimitiveType.Cube, "PinnacleTaper", t.transform,
                         new Vector3(0f, topY + 0.55f, 0f), new Vector3(0.46f, 0.9f, 0.46f), iron);
                    Part(PrimitiveType.Cube, "PinnacleSpire", t.transform,
                         new Vector3(0f, topY + 1.5f, 0f), new Vector3(0.22f, 1.2f, 0.22f), iron);
                    Part(PrimitiveType.Cube, "PinnacleLight", t.transform,
                         new Vector3(0f, topY + 2.3f, 0f), new Vector3(0.34f, 0.55f, 0.34f), lumen,
                         new Vector3(45f, 30f, 45f));
                    if (level >= 4)
                        for (int g = 0; g < Sides; g++)
                        {
                            float ga = g * (360f / Sides);
                            var gdir = Quaternion.Euler(0f, ga, 0f) * Vector3.forward;
                            Part(PrimitiveType.Cube, $"CrownGem{g}", t.transform,
                                 gdir * 0.55f + Vector3.up * (topY + 1.15f),
                                 new Vector3(0.14f, 0.2f, 0.14f), lumen, new Vector3(45f, ga, 45f));
                        }

                    return t;
                }

                EnsureFolder("Assets/Resources/Prefabs/Buildings");
                // Base prefab (PrefabPaths pid 521 — the temple's SPAWN look)
                // is the level-1 cathedral; the leveled _al_1..4 set drives
                // upgrades via BuildingPrefabSwapSystem's TempleLevel scan.
                var baseCathedral = BuildCathedral(1);
                PrefabUtility.SaveAsPrefabAsset(baseCathedral,
                    "Assets/Resources/Prefabs/Buildings/TempleOfRidan.prefab");
                Object.DestroyImmediate(baseCathedral);
                for (int lvl = 1; lvl <= 4; lvl++)
                {
                    var cathedral = BuildCathedral(lvl);
                    PrefabUtility.SaveAsPrefabAsset(cathedral,
                        $"Assets/Resources/Prefabs/Buildings/TempleOfRidan_al_{lvl}.prefab");
                    Object.DestroyImmediate(cathedral);
                }

                // ═══ CHAPELS ARE SECT STATUES (2026-08-02) ═══
                // Each chapel is a monument on the temple's face: a figure in
                // the sect's affinity-culture stone holding the sect's tool.
                // White marble = Alanthor, red sandstone = Runaii, obsidian =
                // Feraldis. Statues face +Z (outward once docked).
                EnsureFolder("Assets/Resources/Prefabs/Buildings/Chapels");

                // Shared figure: plinth, robed body, torso, head, and two
                // upper-arm stubs; the per-sect builder adds forearms + tool.
                // Chapel statues stand on a COLUMN as tall as the temple walls
                // (WallH 4.2) so the figures sit at roof height, framing the
                // cathedral; the shrine keeps a low, humble pedestal.
                GameObject Statue(string name, Material stone, float scale = 1f,
                                  bool bearded = false, bool armored = false,
                                  bool tallPlinth = true)
                {
                    var root = new GameObject(name);
                    var fig = new GameObject("Figure");
                    fig.transform.SetParent(root.transform, false);
                    fig.transform.localScale = Vector3.one * scale;

                    Part(PrimitiveType.Cube, "Pedestal1", root.transform,
                         new Vector3(0f, 0.1f, 0f), new Vector3(1.7f, 0.2f, 1.7f), stone);
                    Part(PrimitiveType.Cube, "Pedestal2", root.transform,
                         new Vector3(0f, 0.36f, 0f), new Vector3(1.25f, 0.32f, 1.25f), stone);
                    if (tallPlinth)
                    {
                        Part(PrimitiveType.Cube, "Column", root.transform,
                             new Vector3(0f, 2.3f, 0f), new Vector3(0.95f, 3.6f, 0.95f), stone);
                        Part(PrimitiveType.Cube, "Capital", root.transform,
                             new Vector3(0f, 4.18f, 0f), new Vector3(1.3f, 0.22f, 1.3f), stone);
                        Part(PrimitiveType.Cube, "Platform", root.transform,
                             new Vector3(0f, 4.35f, 0f), new Vector3(1.5f, 0.12f, 1.5f), stone);
                        // Figure body coordinates assume ground at 0.52 (the
                        // low pedestal top) — lift to the platform surface.
                        fig.transform.localPosition = new Vector3(0f, 3.89f, 0f);
                    }

                    Part(PrimitiveType.Cylinder, "RobeSkirt", fig.transform,
                         new Vector3(0f, 1.02f, 0f), new Vector3(0.85f, 0.5f, 0.85f), stone);
                    Part(PrimitiveType.Cylinder, "RobeUpper", fig.transform,
                         new Vector3(0f, 1.62f, 0f), new Vector3(0.62f, 0.32f, 0.62f), stone);
                    Part(PrimitiveType.Cube, "Torso", fig.transform,
                         new Vector3(0f, 2.12f, 0f), new Vector3(0.66f, 0.6f, 0.44f), stone);
                    Part(PrimitiveType.Sphere, "Head", fig.transform,
                         new Vector3(0f, 2.62f, 0f), Vector3.one * 0.36f, stone);
                    if (bearded)
                        Part(PrimitiveType.Cube, "Beard", fig.transform,
                             new Vector3(0f, 2.48f, 0.14f), new Vector3(0.2f, 0.24f, 0.12f), stone);
                    if (armored)
                    {
                        Part(PrimitiveType.Sphere, "PauldronL", fig.transform,
                             new Vector3(-0.42f, 2.34f, 0f), Vector3.one * 0.26f, stone);
                        Part(PrimitiveType.Sphere, "PauldronR", fig.transform,
                             new Vector3(0.42f, 2.34f, 0f), Vector3.one * 0.26f, stone);
                        Part(PrimitiveType.Cube, "HelmCrest", fig.transform,
                             new Vector3(0f, 2.84f, 0f), new Vector3(0.08f, 0.2f, 0.34f), stone);
                    }
                    return root;
                }

                Transform Fig(GameObject statue) => statue.transform.Find("Figure");

                void Save(GameObject go)
                {
                    PrefabUtility.SaveAsPrefabAsset(go,
                        $"Assets/Resources/Prefabs/Buildings/Chapels/{go.name}.prefab");
                    Object.DestroyImmediate(go);
                }

                // ── SHRINE — smaller, humbler cousin of the temple: a bearded
                // man holding a large glowing crystal (pid 520 base visual). ──
                var shrine = Statue("ShrineOfAhridan", slate, scale: 1.05f, bearded: true,
                                    tallPlinth: false);
                var shrineFig = Fig(shrine);
                Part(PrimitiveType.Cylinder, "ArmL", shrineFig,
                     new Vector3(-0.34f, 1.98f, 0.3f), new Vector3(0.16f, 0.3f, 0.16f), slate,
                     new Vector3(55f, 20f, 0f));
                Part(PrimitiveType.Cylinder, "ArmR", shrineFig,
                     new Vector3(0.34f, 1.98f, 0.3f), new Vector3(0.16f, 0.3f, 0.16f), slate,
                     new Vector3(55f, -20f, 0f));
                Part(PrimitiveType.Cube, "GreatCrystal", shrineFig,
                     new Vector3(0f, 2.25f, 0.52f), new Vector3(0.42f, 0.8f, 0.42f), lumen,
                     new Vector3(45f, 30f, 45f));
                PrefabUtility.SaveAsPrefabAsset(shrine, "Assets/Resources/Prefabs/Buildings/ShrineOfAhridan.prefab");
                Object.DestroyImmediate(shrine);

                // Renewal (Alanthor, white marble) — a man in robes raising a
                // crystal-blue cup.
                var renewal = Statue("Chapel_Renewal", marble);
                var renFig = Fig(renewal);
                Part(PrimitiveType.Cylinder, "ArmL", renFig,
                     new Vector3(-0.3f, 2.05f, 0.28f), new Vector3(0.15f, 0.3f, 0.15f), marble,
                     new Vector3(50f, 18f, 0f));
                Part(PrimitiveType.Cylinder, "ArmR", renFig,
                     new Vector3(0.3f, 2.05f, 0.28f), new Vector3(0.15f, 0.3f, 0.15f), marble,
                     new Vector3(50f, -18f, 0f));
                Part(PrimitiveType.Cylinder, "CupStem", renFig,
                     new Vector3(0f, 2.38f, 0.46f), new Vector3(0.07f, 0.09f, 0.07f), marble);
                Part(PrimitiveType.Cylinder, "Cup", renFig,
                     new Vector3(0f, 2.55f, 0.46f), new Vector3(0.3f, 0.11f, 0.3f), blueCrys);
                Save(renewal);

                // Antiquity (Alanthor) — stepped ziggurat roof.
                // Antiquity (Alanthor, white marble) — a bearded scholar
                // reading a stone tablet.
                var antiquity = Statue("Chapel_Antiquity", marble, bearded: true);
                var antFig = Fig(antiquity);
                Part(PrimitiveType.Cylinder, "ArmL", antFig,
                     new Vector3(-0.28f, 1.95f, 0.26f), new Vector3(0.15f, 0.28f, 0.15f), marble,
                     new Vector3(65f, 12f, 0f));
                Part(PrimitiveType.Cylinder, "ArmR", antFig,
                     new Vector3(0.28f, 1.95f, 0.26f), new Vector3(0.15f, 0.28f, 0.15f), marble,
                     new Vector3(65f, -12f, 0f));
                Part(PrimitiveType.Cube, "Tablet", antFig,
                     new Vector3(0f, 2.1f, 0.46f), new Vector3(0.55f, 0.7f, 0.08f), marble,
                     new Vector3(-20f, 0f, 0f));
                Save(antiquity);

                // Justice (Runai) — golden dome on a sandstone drum.
                // Justice (Runaii, red sandstone) — a soldier holding out a
                // golden noose.
                var justice = Statue("Chapel_Justice", redsand, armored: true);
                var jusFig = Fig(justice);
                Part(PrimitiveType.Cylinder, "ArmR", jusFig,
                     new Vector3(0.34f, 2.1f, 0.3f), new Vector3(0.15f, 0.34f, 0.15f), redsand,
                     new Vector3(70f, -20f, 0f));
                Part(PrimitiveType.Cylinder, "Rope", jusFig,
                     new Vector3(0.4f, 1.95f, 0.52f), new Vector3(0.05f, 0.24f, 0.05f), gold);
                Part(PrimitiveType.Cylinder, "NooseLoop", jusFig,
                     new Vector3(0.4f, 1.55f, 0.52f), new Vector3(0.4f, 0.035f, 0.4f), gold,
                     new Vector3(90f, 0f, 0f));
                Save(justice);

                // Silence (Runai) — tall slender tent-spire with a teal accent.
                // Silence (Runaii, red sandstone) — a veiled figure cradling a
                // dim orb; the veil is a smooth faceless head-drape.
                var silence = Statue("Chapel_Silence", redsand);
                var silFig = Fig(silence);
                Part(PrimitiveType.Cube, "Veil", silFig,
                     new Vector3(0f, 2.62f, -0.04f), new Vector3(0.42f, 0.5f, 0.4f), redsand);
                Part(PrimitiveType.Cylinder, "ArmL", silFig,
                     new Vector3(-0.26f, 1.85f, 0.24f), new Vector3(0.15f, 0.26f, 0.15f), redsand,
                     new Vector3(72f, 10f, 0f));
                Part(PrimitiveType.Cylinder, "ArmR", silFig,
                     new Vector3(0.26f, 1.85f, 0.24f), new Vector3(0.15f, 0.26f, 0.15f), redsand,
                     new Vector3(72f, -10f, 0f));
                Part(PrimitiveType.Sphere, "Orb", silFig,
                     new Vector3(0f, 1.92f, 0.44f), Vector3.one * 0.3f, teal);
                Save(silence);

                // War (Feraldis) — jagged horned iron roof.
                // War (Feraldis, obsidian) — an armored soldier holding up the
                // severed head of an enemy.
                var war = Statue("Chapel_War", obsidian, armored: true);
                var warFig = Fig(war);
                Part(PrimitiveType.Cylinder, "ArmR", warFig,
                     new Vector3(0.36f, 2.35f, 0.22f), new Vector3(0.15f, 0.36f, 0.15f), obsidian,
                     new Vector3(35f, -25f, 0f));
                Part(PrimitiveType.Sphere, "EnemyHead", warFig,
                     new Vector3(0.5f, 2.75f, 0.42f), Vector3.one * 0.28f, obsidian);
                Part(PrimitiveType.Cube, "EnemyHeadHair", warFig,
                     new Vector3(0.5f, 2.88f, 0.4f), new Vector3(0.24f, 0.1f, 0.24f), ember);
                Part(PrimitiveType.Cylinder, "ArmL", warFig,
                     new Vector3(-0.34f, 1.9f, 0.1f), new Vector3(0.15f, 0.3f, 0.15f), obsidian,
                     new Vector3(15f, 10f, 0f));
                Part(PrimitiveType.Cube, "Blade", warFig,
                     new Vector3(-0.42f, 1.35f, 0.18f), new Vector3(0.1f, 0.9f, 0.05f), iron);
                Save(war);

                // Ash (Feraldis) — flat roof with an ember-lit smokestack.
                // Ash (Feraldis, obsidian) — a hooded mourner bearing an urn
                // with embers still glowing at its mouth.
                var ash = Statue("Chapel_Ash", obsidian);
                var ashFig = Fig(ash);
                Part(PrimitiveType.Cube, "Hood", ashFig,
                     new Vector3(0f, 2.7f, -0.06f), new Vector3(0.44f, 0.4f, 0.42f), obsidian);
                Part(PrimitiveType.Cylinder, "ArmL", ashFig,
                     new Vector3(-0.26f, 1.9f, 0.26f), new Vector3(0.15f, 0.28f, 0.15f), obsidian,
                     new Vector3(68f, 10f, 0f));
                Part(PrimitiveType.Cylinder, "ArmR", ashFig,
                     new Vector3(0.26f, 1.9f, 0.26f), new Vector3(0.15f, 0.28f, 0.15f), obsidian,
                     new Vector3(68f, -10f, 0f));
                Part(PrimitiveType.Cylinder, "Urn", ashFig,
                     new Vector3(0f, 2.02f, 0.46f), new Vector3(0.3f, 0.22f, 0.3f), obsidian);
                Part(PrimitiveType.Cylinder, "UrnEmbers", ashFig,
                     new Vector3(0f, 2.26f, 0.46f), new Vector3(0.24f, 0.03f, 0.24f), ember);
                Save(ash);

                AssetDatabase.SaveAssets();
                Debug.Log("[GameDataMaintenance] WireTempleAndChapelVisuals done — temple + 6 chapel prefabs saved.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GameDataMaintenance] WireTempleAndChapelVisuals threw: {ex}");
            }
        }

        // ── Influence ground layers (2026-08-03) ─────────────────────────────
        // Copies the Blood + AlanthorInfluence terrain layers into Resources
        // (the re-enabled InfluenceTerrainPainter appends them to any terrain
        // missing them) and builds the CurseInfluence layer from the
        // RockSubstance003 substance output, remapped to a purple-blue tint.

        [MenuItem("Waning Border/Game Data/Wire Influence Terrain Layers")]
        public static void WireInfluenceLayers()
        {
            try
            {
                EnsureFolder("Assets/Resources/TerrainLayers");

                void CopyLayer(string src, string name)
                {
                    string dst = $"Assets/Resources/TerrainLayers/{name}.terrainlayer";
                    if (AssetDatabase.LoadAssetAtPath<TerrainLayer>(dst) != null) return;
                    if (!AssetDatabase.CopyAsset(src, dst))
                        Debug.LogError($"[GameDataMaintenance] Could not copy {src} -> {dst}");
                }

                CopyLayer("Assets/GameData/Scenes/Maps/TestScene/PavingStones070_2K-PNG/AlanthorInfluence.terrainlayer",
                          "AlanthorInfluence");

                // Blood: NOT the guts texture — dark red SLEEK blotches
                // (user direction 2026-08-03): smooth plaster substance
                // remapped deep red, glossy, no normal detail.
                string bloodPath = "Assets/Resources/TerrainLayers/Blood.terrainlayer";
                var blood = AssetDatabase.LoadAssetAtPath<TerrainLayer>(bloodPath);
                if (blood == null)
                {
                    blood = new TerrainLayer();
                    AssetDatabase.CreateAsset(blood, bloodPath);
                }
                blood.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Assets/GameData/Scenes/Maps/TestScene/PaintedPlasterSubstance002_COMPILED_graph_0/PaintedPlasterSubstance002_COMPILED_basecolor.tga");
                blood.normalMapTexture = null;
                blood.tileSize = new Vector2(5f, 5f);
                blood.diffuseRemapMin = new Color(0.10f, 0.008f, 0.008f, 1f);
                blood.diffuseRemapMax = new Color(0.45f, 0.04f, 0.05f, 1f);
                blood.smoothness = 0.85f;
                EditorUtility.SetDirty(blood);

                // Curse layer: substance rock texture tinted purple-blue via
                // the layer's diffuse remap (no elevation — texture only).
                string cursePath = "Assets/Resources/TerrainLayers/CurseInfluence.terrainlayer";
                var curse = AssetDatabase.LoadAssetAtPath<TerrainLayer>(cursePath);
                if (curse == null)
                {
                    curse = new TerrainLayer();
                    AssetDatabase.CreateAsset(curse, cursePath);
                }
                curse.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Assets/GameData/Scenes/Maps/TestScene/RockSubstance003_COMPILED_graph_0/RockSubstance003_COMPILED_basecolor.tga");
                curse.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Assets/GameData/Scenes/Maps/TestScene/RockSubstance003_COMPILED_graph_0/RockSubstance003_COMPILED_normal.tga");
                curse.tileSize = new Vector2(6f, 6f);
                curse.diffuseRemapMin = new Color(0.10f, 0.04f, 0.22f, 1f);
                curse.diffuseRemapMax = new Color(0.55f, 0.38f, 1.0f, 1f);
                EditorUtility.SetDirty(curse);

                // Veilstone patch layer: ore-bearing GROUND under a veilstone
                // patch. Deliberately a DIFFERENT source texture from the curse
                // (gravel, not the curse's smooth rock) and a cold mineral grey
                // rather than the curse purple — a resource patch must never
                // read as cursed ground. Painted by InfluenceTerrainPainter
                // from VeilstonePatchGround.
                string patchPath = "Assets/Resources/TerrainLayers/VeilstonePatch.terrainlayer";
                var patch = AssetDatabase.LoadAssetAtPath<TerrainLayer>(patchPath);
                if (patch == null)
                {
                    patch = new TerrainLayer();
                    AssetDatabase.CreateAsset(patch, patchPath);
                }
                patch.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Assets/GameData/Scenes/Maps/Shared/GravelSubstance002_COMPILED_graph_0/GravelSubstance002_COMPILED_basecolor.tga");
                patch.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Assets/GameData/Scenes/Maps/Shared/GravelSubstance002_COMPILED_graph_0/GravelSubstance002_COMPILED_normal.tga");
                patch.tileSize = new Vector2(4f, 4f);
                patch.diffuseRemapMin = new Color(0.16f, 0.17f, 0.20f, 1f);
                patch.diffuseRemapMax = new Color(0.62f, 0.66f, 0.74f, 1f);
                patch.smoothness = 0.15f;
                EditorUtility.SetDirty(patch);

                AssetDatabase.SaveAssets();
                Debug.Log("[GameDataMaintenance] WireInfluenceLayers done — Blood, AlanthorInfluence, "
                          + "CurseInfluence, VeilstonePatch in Resources/TerrainLayers.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GameDataMaintenance] WireInfluenceLayers threw: {ex}");
            }
        }

        [MenuItem("Waning Border/Game Data/Wire Realistic Unit Visuals")]
        public static void WireUnitVisuals()
        {
            var byId = ResolveUnitSOs();

            int wired = 0;
            foreach (var entry in BuildWireTable())
            {
                if (!byId.TryGetValue(entry.UnitId, out var so))
                {
                    Debug.LogWarning($"[GameDataMaintenance] No UnitDefSO with id '{entry.UnitId}' — skipped.");
                    continue;
                }
                try
                {
                    if (WireOne(so, entry)) wired++;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[GameDataMaintenance] Wiring '{entry.UnitId}' failed: {ex}");
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[GameDataMaintenance] WireUnitVisuals done — {wired} unit(s) wired.");
        }

        static bool WireOne(UnitDefSO so, WireEntry entry)
        {
            string soPath = AssetDatabase.GetAssetPath(so);
            string folder = System.IO.Path.GetDirectoryName(soPath).Replace('\\', '/');
            string clean  = System.IO.Path.GetFileNameWithoutExtension(soPath);

            var idle   = LoadClip(entry.IdleClip);
            var move   = LoadClip(entry.MoveClip);
            var attack = LoadClip(entry.AttackClip);
            var death  = entry.DeathClip != null ? LoadClip(entry.DeathClip) : null;
            if (idle == null || move == null || attack == null)
            {
                Debug.LogWarning($"[GameDataMaintenance] Missing clip(s) for '{entry.UnitId}' " +
                                 $"(idle={idle != null}, move={move != null}, attack={attack != null}) — skipped.");
                return false;
            }

            var controller = BuildController($"{folder}/{clean}.controller", idle, move, attack, death,
                                             entry.Healer, entry.AttackCycleOffset);

            if (entry.SourcePrefab != null)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(entry.SourcePrefab);
                if (source == null)
                {
                    Debug.LogWarning($"[GameDataMaintenance] Source prefab missing for '{entry.UnitId}': {entry.SourcePrefab}");
                }
                else
                {
                    string prefabPath = $"{folder}/{clean}.prefab";
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);

                    // The Animator must sit on the MODEL root, never a wrapper
                    // above it: generic clip curve paths are relative to the
                    // Animator's node, so one level up they bind to nothing and
                    // the unit freezes in its default pose. Resolve it before
                    // any wrapping.
                    var animator = instance.GetComponentInChildren<Animator>();
                    if (animator == null) animator = instance.AddComponent<Animator>();

                    // Facing fix: SyncTransforms stomps the ROOT rotation every
                    // frame, so a model authored facing the wrong way must carry
                    // its yaw on a child below a neutral root.
                    if (entry.VisualYaw != 0f)
                    {
                        var wrapper = new GameObject(clean);
                        instance.transform.SetParent(wrapper.transform, false);
                        instance.transform.localRotation = Quaternion.Euler(0f, entry.VisualYaw, 0f);
                        instance = wrapper;
                    }

                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;

                    if (!string.IsNullOrEmpty(entry.TexturePath))
                        ApplyUnitMaterial(instance, folder, clean, entry.TexturePath);

                    // SaveAsPrefabAsset overwrites in place, preserving the GUID on re-runs.
                    var prefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                    Object.DestroyImmediate(instance);
                    so.prefab = prefab;
                }
            }

            if (so.presentationId == 0 && entry.ForcePid != 0) so.presentationId = entry.ForcePid;
            so.animatorController = controller;
            EditorUtility.SetDirty(so);
            Debug.Log($"[GameDataMaintenance] Wired '{entry.UnitId}' (pid {so.presentationId}) in {folder}");
            return true;
        }

        /// <summary>
        /// Create-or-update {folder}/Materials/{unit}.mat as URP/Lit with the
        /// given texture as _BaseMap, and assign it to every renderer slot of
        /// the instance. Used when the FBX's own material references a texture
        /// outside the project (e.g. a Desktop .fbm folder) and imports blank.
        /// </summary>
        static void ApplyUnitMaterial(GameObject instance, string folder, string clean, string texturePath)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (tex == null)
            {
                Debug.LogWarning($"[GameDataMaintenance] Texture not found at {texturePath} — renderers left as imported.");
                return;
            }

            string matFolder = $"{folder}/Materials";
            if (!AssetDatabase.IsValidFolder(matFolder)) AssetDatabase.CreateFolder(folder, "Materials");

            string matPath = $"{matFolder}/{clean}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(mat);

            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
        }

        // "path/to/model.fbx#ClipName" selects a named take inside a
        // multi-take FBX (matched by exact clip name, then by suffix so
        // "Armature|Armature|"-style take prefixes don't matter). A bare
        // path keeps the old first-clip behavior for single-clip files.
        static AnimationClip LoadClip(string spec)
        {
            string path = spec;
            string clipName = null;
            int hash = spec.IndexOf('#');
            if (hash >= 0)
            {
                path = spec.Substring(0, hash);
                clipName = spec.Substring(hash + 1);
                EnsureClipImportSettings(path);
            }

            var clips = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview"))
                .ToList();
            if (clipName == null) return clips.FirstOrDefault();
            return clips.FirstOrDefault(c => c.name == clipName)
                ?? clips.FirstOrDefault(c => c.name.EndsWith(clipName));
        }

        /// <summary>
        /// Multi-take FBXes imported with no clip splits get default clips
        /// with loopTime OFF — idle/run would play once and freeze. Author
        /// the clip list from the importer's default takes and loop every
        /// take whose name ends in "_Loop". Idempotent: reimports only when
        /// a flag actually changes.
        /// </summary>
        static void EnsureClipImportSettings(string fbxPath)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;

            bool changed = false;

            // "No Avatar" imports ship the model WITHOUT an Animator component,
            // so the wire step's fallback used to add one on the prefab wrapper
            // root — where generic clip paths (relative to the fbx root) bind
            // to nothing and the unit freezes in its default pose. Create the
            // avatar from the model so the Animator lives on the fbx root.
            if (importer.animationType != ModelImporterAnimationType.None
                && importer.avatarSetup == ModelImporterAvatarSetup.NoAvatar)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                changed = true;
            }

            var clips = importer.clipAnimations != null && importer.clipAnimations.Length > 0
                ? importer.clipAnimations
                : importer.defaultClipAnimations;
            if (clips != null && clips.Length > 0)
            {
                bool clipsChanged = importer.clipAnimations == null || importer.clipAnimations.Length == 0;
                foreach (var c in clips)
                {
                    // Loops: idles/movement (by *_Loop suffix or Idle in the
                    // name) and attacks — UnitAnimationSync scales the Attack
                    // state to one cycle per cooldown tick, so consecutive
                    // swings must chain instead of clamping on the last frame.
                    bool loop = c.name.EndsWith("_Loop")
                             || c.name.Contains("Idle")
                             || c.name.Contains("Attack");
                    if (c.loopTime != loop) { c.loopTime = loop; clipsChanged = true; }
                }
                if (clipsChanged)
                {
                    importer.clipAnimations = clips;
                    changed = true;
                }
            }

            if (changed) importer.SaveAndReimport();
        }

        /// <summary>
        /// Build the standard unit controller. States: Idle (default) / Move /
        /// Attack / [Death]. Parameters match UnitAnimationSync's probe set.
        /// </summary>
        static AnimatorController BuildController(string path, AnimationClip idle, AnimationClip move,
                                                  AnimationClip attack, AnimationClip death, bool healer,
                                                  float attackCycleOffset = 0f)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null)
                AssetDatabase.DeleteAsset(path);

            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("IsAttacking", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("IsDead", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter(new AnimatorControllerParameter
            {
                name = "AttackSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f
            });
            if (healer) ctrl.AddParameter("IsHealing", AnimatorControllerParameterType.Bool);

            var sm = ctrl.layers[0].stateMachine;
            var idleState   = sm.AddState("Idle");
            var moveState   = sm.AddState("Move");
            var attackState = sm.AddState("Attack");
            idleState.motion = idle;
            moveState.motion = move;
            attackState.motion = attack;
            attackState.speedParameterActive = true;
            attackState.speedParameter = "AttackSpeed";
            attackState.cycleOffset = attackCycleOffset;
            sm.defaultState = idleState;

            void Instant(AnimatorStateTransition t)
            {
                t.hasExitTime = false;
                t.duration = 0.15f;
            }

            var toMove = idleState.AddTransition(moveState);
            Instant(toMove); toMove.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
            var toIdle = moveState.AddTransition(idleState);
            Instant(toIdle); toIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");

            foreach (var from in new[] { idleState, moveState })
            {
                var toAttack = from.AddTransition(attackState);
                Instant(toAttack); toAttack.AddCondition(AnimatorConditionMode.If, 0, "IsAttacking");
                if (healer)
                {
                    var toCast = from.AddTransition(attackState);
                    Instant(toCast); toCast.AddCondition(AnimatorConditionMode.If, 0, "IsHealing");
                }
            }

            // Retrigger the (non-looping) attack clip while still attacking.
            var retrigger = attackState.AddTransition(attackState);
            retrigger.hasExitTime = true;
            retrigger.exitTime = 1f;
            retrigger.duration = 0f;
            retrigger.AddCondition(AnimatorConditionMode.If, 0, "IsAttacking");

            var attackExit = attackState.AddTransition(idleState);
            Instant(attackExit);
            attackExit.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAttacking");
            if (healer) attackExit.AddCondition(AnimatorConditionMode.IfNot, 0, "IsHealing");

            if (death != null)
            {
                var deathState = sm.AddState("Death");
                deathState.motion = death;
                var anyToDeath = sm.AddAnyStateTransition(deathState);
                anyToDeath.hasExitTime = false;
                anyToDeath.duration = 0.05f;
                anyToDeath.AddCondition(AnimatorConditionMode.If, 0, "IsDead");
            }

            return ctrl;
        }

        // ────────────────────────────────────────────────────────────────────
        //  3. Realistic unit materials + character meshes
        //
        //  The project renders with URP; the KI / ExplosiveLLC packs (and the
        //  Characters.fbx embedded materials) use built-in-pipeline shaders, so
        //  every unit rendered blank once the faction tint stopped masking it.
        //  This pass gives each unit prefab URP/Lit materials with the proper
        //  albedo textures, and swaps the KI mannequin bodies for the real
        //  character meshes inside Characters.fbx (Knight / Peasant / Warrior /
        //  Viking — same Humanoid rig family, so the generated controllers and
        //  KI clips retarget unchanged).
        // ────────────────────────────────────────────────────────────────────

        const string CharactersFbx = UnitsFolder + "/Age 0/Characters.fbx";
        const string AtlasBlueSrc  = UnitsFolder + "/Alanthor/Longbowman/Characters_Texture_Blue.png";
        const string AtlasDarkSrc  = UnitsFolder + "/Alanthor/Longbowman/Texture_Alt_04_Dark.png";
        const string AtlasBlue     = UnitsFolder + "/Age 0/Characters_Texture_Blue.png";
        const string AtlasDark     = UnitsFolder + "/Age 0/Characters_Texture_Dark.png";
        const string PeasantFbx    = "Assets/Resources/Prefabs/Units/SK_Character_Human_Peasant.fbx";
        const string PeasantTex    = "Assets/Resources/Prefabs/Units/Characters_Black.png";

        [MenuItem("Waning Border/Game Data/Apply Realistic Unit Visuals")]
        public static void ApplyRealisticUnitVisuals()
        {
            var byId = LoadUnitSOsById();

            // Shared character atlas lives next to the shared Characters.fbx.
            CopyIfMissing(AtlasBlueSrc, AtlasBlue);
            CopyIfMissing(AtlasDarkSrc, AtlasDark);

            // (a) KI mannequin units -> real Characters.fbx meshes.
            BuildCharacterVariant(byId, "Alanthor_Swordsman", "Character_Knight",  AtlasBlue);
            BuildCharacterVariant(byId, "Scout",              "Character_Peasant", AtlasBlue);
            BuildCharacterVariant(byId, "Runai_Spearman",     "Character_Warrior", AtlasBlue);
            BuildCharacterVariant(byId, "Runai_Raider",       "Character_Viking",  AtlasBlue);
            BuildCharacterVariant(byId, "Feraldis_Raider",    "Character_Viking",  AtlasDark);

            // (b) ExplosiveLLC warrior prefabs -> URP materials with the pack albedos.
            RemapPackUnit(byId, "Litharch",             $"{EX}/Mage Warrior Mecanim Animation Pack/Textures/Mage.psd");
            RemapPackUnit(byId, "Alanthor_Scholar",     $"{EX}/Mage Warrior Mecanim Animation Pack/Textures/Mage.psd");
            RemapPackUnit(byId, "Runai_Acolyte",        $"{EX}/Mage Warrior Mecanim Animation Pack/Textures/Mage.psd");
            RemapPackUnit(byId, "Feraldis_Iconoclast",  $"{EX}/Mage Warrior Mecanim Animation Pack/Textures/Mage.psd");
            RemapPackUnit(byId, "Alanthor_Sentinel",    $"{EX}/Knight Warrior Mecanim Animation Pack/Textures/Knight.psd");
            RemapPackUnit(byId, "Alanthor_Crossbowman", $"{EX}/Archer Warrior Mecanim Animation Pack/Textures/Archer.psd");
            RemapPackUnit(byId, "Runai_Skirmisher",     $"{EX}/Archer Warrior Mecanim Animation Pack/Textures/Archer.psd");
            RemapPackUnit(byId, "Feraldis_Hunter",      $"{EX}/Archer Warrior Mecanim Animation Pack/Textures/Archer.psd");
            RemapPackUnit(byId, "Feraldis_Berserker",   $"{EX}/2 Handed Warrior Mecanim Animation Pack/Textures/2Handed.psd");

            // (c) Archer is a Characters.fbx variant on embedded (built-in) materials.
            if (byId.TryGetValue("Archer", out var archer))
            {
                string folder = SoFolder(archer);
                var mat = EnsureUrpMat($"{folder}/Materials", "Archer", AtlasBlue);
                HealPrefabMaterials($"{folder}/Archer.prefab", $"{folder}/Materials", mat);
            }

            // (d) Longbowman: its Materials/ folder already holds URP versions of the
            // embedded materials — heal any slot still pointing at a built-in one.
            if (byId.TryGetValue("Alanthor_Longbowman", out var longbow))
            {
                string folder = SoFolder(longbow);
                var fallback = AssetDatabase.LoadAssetAtPath<Material>($"{folder}/Materials/Characters_Texture_Blue.mat");
                HealPrefabMaterials($"{folder}/Longbowman.prefab", $"{folder}/Materials", fallback);
            }

            // (e) Worker: was a raw FBX load from Resources (embedded built-in
            // materials). Build a proper prefab in its GameData folder.
            BuildWorkerPrefab(byId);

            AssetDatabase.SaveAssets();
            Debug.Log("[GameDataMaintenance] ApplyRealisticUnitVisuals done.");
        }

        static Dictionary<string, UnitDefSO> LoadUnitSOsById()
        {
            var byId = new Dictionary<string, UnitDefSO>();
            foreach (var guid in AssetDatabase.FindAssets("t:UnitDefSO", new[] { UnitsFolder }))
            {
                var so = AssetDatabase.LoadAssetAtPath<UnitDefSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (so != null && !string.IsNullOrEmpty(so.id) && !byId.ContainsKey(so.id)) byId[so.id] = so;
            }
            return byId;
        }

        static string SoFolder(UnitDefSO so) =>
            System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(so)).Replace('\\', '/');

        static void CopyIfMissing(string src, string dst)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(dst) == null &&
                AssetDatabase.LoadAssetAtPath<Object>(src) != null)
                AssetDatabase.CopyAsset(src, dst);
        }

        /// <summary>Create (or recreate) a URP/Lit material asset with the given albedo.</summary>
        static Material EnsureUrpMat(string folder, string name, string texPath,
                                     string normalPath = null, bool foliage = false)
        {
            EnsureFolder(folder);
            string path = $"{folder}/{name}.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var tex = AssetDatabase.LoadAssetAtPath<Texture>(texPath);
            if (tex != null) mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.15f);
            if (normalPath != null)
            {
                var n = AssetDatabase.LoadAssetAtPath<Texture>(normalPath);
                if (n != null) { mat.SetTexture("_BumpMap", n); mat.EnableKeyword("_NORMALMAP"); }
            }
            if (foliage)
            {
                // Alpha-clipped, double-sided — leaf cards read from both sides.
                mat.SetFloat("_AlphaClip", 1f);
                mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cutoff", 0.4f);
                mat.SetFloat("_Cull", 0f);
                mat.renderQueue = 2450;
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Replace every material slot on every renderer of a prefab asset.</summary>
        static void RemapAllMaterials(string prefabPath, Material mat)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            PrefabUtility.UnloadPrefabContents(root);
        }

        /// <summary>
        /// Fix only the broken (non-URP shader) slots: prefer a same-named .mat in
        /// the unit's Materials folder, else the fallback.
        /// </summary>
        static void HealPrefabMaterials(string prefabPath, string materialsFolder, Material fallback)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null) return;
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m != null && m.shader != null && m.shader.name.StartsWith("Universal")) continue;
                    Material repl = m != null
                        ? AssetDatabase.LoadAssetAtPath<Material>($"{materialsFolder}/{m.name}.mat")
                        : null;
                    mats[i] = repl != null ? repl : fallback;
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            PrefabUtility.UnloadPrefabContents(root);
        }

        static void RemapPackUnit(Dictionary<string, UnitDefSO> byId, string unitId, string texPath)
        {
            if (!byId.TryGetValue(unitId, out var so))
            {
                Debug.LogWarning($"[GameDataMaintenance] RemapPackUnit: no SO for '{unitId}'.");
                return;
            }
            string folder = SoFolder(so);
            string clean = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(so));
            string prefabPath = $"{folder}/{clean}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                Debug.LogWarning($"[GameDataMaintenance] RemapPackUnit: no prefab at {prefabPath}.");
                return;
            }
            var mat = EnsureUrpMat($"{folder}/Materials", clean, texPath);
            RemapAllMaterials(prefabPath, mat);
            Debug.Log($"[GameDataMaintenance] URP material applied to '{unitId}'.");
        }

        /// <summary>
        /// Replace a unit's prefab with one of the character meshes inside the
        /// shared Characters.fbx (keeps the unit's generated controller — both
        /// rigs are Humanoid so the clips retarget).
        /// </summary>
        static void BuildCharacterVariant(Dictionary<string, UnitDefSO> byId, string unitId,
                                          string meshName, string texPath)
        {
            if (!byId.TryGetValue(unitId, out var so))
            {
                Debug.LogWarning($"[GameDataMaintenance] BuildCharacterVariant: no SO for '{unitId}'.");
                return;
            }
            string folder = SoFolder(so);
            string clean = System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(so));
            var controller = AssetDatabase.LoadAssetAtPath<UnityEngine.RuntimeAnimatorController>($"{folder}/{clean}.controller");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(CharactersFbx);
            if (model == null) { Debug.LogError($"[GameDataMaintenance] Characters.fbx missing at {CharactersFbx}"); return; }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = clean;

            // Keep only the requested body; the other character meshes share the
            // same skeleton and would render stacked on top of each other.
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.gameObject.name != meshName) Object.DestroyImmediate(smr.gameObject);

            var mat = EnsureUrpMat($"{folder}/Materials", clean, texPath);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }

            var animator = inst.GetComponent<Animator>();
            if (animator == null) animator = inst.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            string prefabPath = $"{folder}/{clean}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
            Object.DestroyImmediate(inst);
            so.prefab = prefab;
            EditorUtility.SetDirty(so);
            Debug.Log($"[GameDataMaintenance] '{unitId}' rebuilt from Characters.fbx/{meshName}.");
        }

        static void BuildWorkerPrefab(Dictionary<string, UnitDefSO> byId)
        {
            if (!byId.TryGetValue("Worker", out var so)) return;
            string folder = SoFolder(so);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(PeasantFbx);
            if (model == null) { Debug.LogWarning("[GameDataMaintenance] Peasant FBX missing — Worker skipped."); return; }

            var controller = AssetDatabase.LoadAssetAtPath<UnityEngine.RuntimeAnimatorController>($"{folder}/Worker.controller");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = "Worker";

            var mat = EnsureUrpMat($"{folder}/Materials", "Worker", PeasantTex);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }

            var animator = inst.GetComponent<Animator>();
            if (animator == null) animator = inst.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            var prefab = PrefabUtility.SaveAsPrefabAsset(inst, $"{folder}/Worker.prefab");
            Object.DestroyImmediate(inst);
            so.prefab = prefab;
            if (controller != null) so.animatorController = controller;
            EditorUtility.SetDirty(so);
            Debug.Log("[GameDataMaintenance] Worker prefab built.");
        }

        // ────────────────────────────────────────────────────────────────────
        //  4. Realistic forest trees
        //
        //  The old tree path (ProceduralTerrain.PlaceTerrainTrees / Spruce_008)
        //  was deleted with the procedural map generation code, leaving forests
        //  invisible, and the stylized SM_Env_Tree prefabs in Resources are
        //  referenced by nothing. This builds URP variants of the realistic
        //  MapMagic demo Pine/Birch trees into Resources so the runtime forest
        //  spawner (PresentationSpawnSystem.CreateProceduralForest) can scatter
        //  them, and deletes the orphaned stylized trees.
        // ────────────────────────────────────────────────────────────────────

        const string MMTrees = "Assets/MapMagic/Demo/Trees";
        const string RealTreeFolder = "Assets/Resources/Prefabs/Nature/RealisticTrees";
        const string StylizedTreeFolder = "Assets/Resources/Prefabs/Nature/Trees";

        [MenuItem("Waning Border/Game Data/Build Realistic Trees")]
        public static void BuildRealisticTrees()
        {
            EnsureFolder(RealTreeFolder);

            // The MapMagic demo tree materials use a built-in CG shader (magenta
            // under URP) — bake URP/Lit foliage materials from the same textures.
            var pineMat = EnsureUrpMat($"{RealTreeFolder}/Materials", "Pine_URP",
                $"{MMTrees}/Pine/Textures/Pine.tif", $"{MMTrees}/Pine/Textures/Pine_n.tif", foliage: true);
            var birchMat = EnsureUrpMat($"{RealTreeFolder}/Materials", "Birch_URP",
                $"{MMTrees}/Birch/Textures/Birch.tif", $"{MMTrees}/Birch/Textures/Birch_n.tif", foliage: true);

            int built = 0;
            built += BuildTreeVariants("Pine", pineMat,
                new[] { "PineBig1", "PineBig2", "PineMed1", "PineMed2", "PineMed3", "PineSmall1", "PineSmall2" },
                name => $"{MMTrees}/Pine/Prefabs/{name}.prefab");
            built += BuildTreeVariants("Birch", birchMat,
                new[] { "TallSingleA", "TallSingleB", "TallDouble", "TallSplit", "ShortSingle", "ShortDouble" },
                name => $"{MMTrees}/Birch/Prefabs/{name}.prefab");

            // Remove the orphaned stylized low-poly trees (nothing references them;
            // living in Resources they were still shipped in builds).
            if (AssetDatabase.IsValidFolder(StylizedTreeFolder))
            {
                AssetDatabase.DeleteAsset(StylizedTreeFolder);
                Debug.Log($"[GameDataMaintenance] Deleted stylized tree folder {StylizedTreeFolder}");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[GameDataMaintenance] BuildRealisticTrees done — {built} tree prefab(s).");
        }

        static int BuildTreeVariants(string family, Material mat, string[] names,
                                     System.Func<string, string> sourcePath)
        {
            int built = 0;
            foreach (var name in names)
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath(name));
                if (src == null)
                {
                    Debug.LogWarning($"[GameDataMaintenance] Tree source missing: {sourcePath(name)}");
                    continue;
                }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = $"{family}_{name}";
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    r.sharedMaterials = mats;
                }
                PrefabUtility.SaveAsPrefabAsset(inst, $"{RealTreeFolder}/{family}_{name}.prefab");
                Object.DestroyImmediate(inst);
                built++;
            }
            return built;
        }
    }
}
#endif
