// TechCatalog.cs
// Static replacement for the former `TechTreeDB` MonoBehaviour singleton.
// Part of: Data/TechTree/
//
// Loads the ScriptableObject catalog (Resources/TechTreeCatalog) lazily on first access
// and serves the same unit/building/tech/sect lookups the old TechTreeDB.Instance did,
// plus a presentationId -> prefab registry used by the prefab-based spawn path.
// Technologies + sects are still parsed from TechTree.json (Resources) via TechTreeParser.
//
// No GameObject / scene wiring needed — it is a pure static service.

using System.Collections.Generic;
using UnityEngine;
using TheWaningBorder.Data;

public static class TechCatalog
{
    private const string CatalogResourceName = "TechTreeCatalog";

    // ─── storage ───────────────────────────────────────────────────────────
    private static bool _loaded;
    private static readonly Dictionary<string, UnitDef> _unitsById = new();
    private static readonly Dictionary<string, BuildingDef> _buildingsById = new();
    private static readonly Dictionary<string, TechnologyDef> _technologiesById = new();
    private static readonly Dictionary<string, SectDef> _sectsById = new();
    // Source SOs, so TryGet* can refresh the cached def from the asset each call —
    // that is what makes Inspector edits apply to the next-spawned entity ("on the fly").
    private static readonly Dictionary<string, UnitDefSO> _unitSOsById = new();
    private static readonly Dictionary<string, BuildingDefSO> _buildingSOsById = new();
    private static readonly Dictionary<string, TechDefSO> _techSOsById = new();
    // presentationId -> prefab (for the prefab-based spawn path). null prefab = primitive fallback.
    private static readonly Dictionary<int, GameObject> _prefabByPid = new();
    // presentationId -> animator controller (assigned at spawn when the prefab's
    // own Animator has no controller, e.g. variant-of-FBX prefabs).
    private static readonly Dictionary<int, RuntimeAnimatorController> _controllerByPid = new();

    private static CombatProfile _combatProfile;
    private static string _faction;
    private static List<string> _resources = new();

    /// <summary>
    /// Always true (mirrors the old `TechTreeDB.Instance != null`, which was true once the
    /// singleton existed regardless of data). Accessing it also lazy-loads the catalog, so
    /// rerouted null-guards both compile and trigger the load.
    /// </summary>
    public static bool IsReady { get { EnsureLoaded(); return true; } }

    // ─── lifecycle ─────────────────────────────────────────────────────────
    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;           // set first — prevents re-entrancy from BuildCosts.SyncFromTechTree()
        Build();
    }

    /// <summary>Force a reload (e.g. after editing the catalog at runtime).</summary>
    public static void ReloadFromCatalog()
    {
        _loaded = false;
        EnsureLoaded();
    }

    // ─── Post-load derivations the live SO refresh must not undo ─────────
    //
    // TryGetBuilding re-stamps the authored SO over the cached def on every
    // call ("live refresh", so tuning an asset in the editor takes effect
    // without a reload). That silently reverted everything Build() derived
    // AFTER the SOs were loaded, because ApplyTo copies the whole def:
    //
    //   * the era overrides below — TempleOfRidan.asset shipped minEra 0, so
    //     the Temple was placeable in Age 0 despite the code forcing 2. The
    //     comment on that forcing promised "a stale SO minEra can never
    //     quietly re-open the rush"; it could, and did.
    //   * RebuildResearchLists — CLAUDE.md makes TechDefSO.researchAt the one
    //     source of truth for the research host and derives every building's
    //     research[] from it. A refresh put the hand-authored array back.
    //
    // Recording the derived values and re-applying them after ApplyTo keeps the
    // refresh doing what it is for (stat tuning) without letting it reach the
    // fields Build() owns.
    private static readonly Dictionary<string, int> _eraOverrides = new();
    private static readonly Dictionary<string, string[]> _derivedResearch = new();

    private static void ReapplyDerived(string id, BuildingDef def)
    {
        if (def == null) return;
        if (_eraOverrides.TryGetValue(id, out int era)) def.minEra = era;
        if (_derivedResearch.TryGetValue(id, out var research)) def.research = research;
    }

    private static void Build()
    {
        _unitsById.Clear(); _buildingsById.Clear(); _technologiesById.Clear(); _sectsById.Clear();
        _unitSOsById.Clear(); _buildingSOsById.Clear(); _techSOsById.Clear();
        _prefabByPid.Clear(); _controllerByPid.Clear();
        _eraOverrides.Clear(); _derivedResearch.Clear();

        // Fully-qualified: the `Resources` property below would shadow UnityEngine.Resources.
        var catalog = UnityEngine.Resources.Load<TechTreeCatalog>(CatalogResourceName);
        var jsonAsset = TryLoadJson();
        string json = jsonAsset != null ? jsonAsset.text : null;

        // 1. Sects/faction/resources come from JSON. Technologies prefer their SOs.
        var parsed = TechTreeParser.ParseAll(json);
        _faction = parsed.Faction;
        _resources = parsed.Resources;
        _combatProfile = new CombatProfile { defenseFormulaHint = "" };
        foreach (var kv in parsed.Sects) _sectsById[kv.Key] = kv.Value;

        // Technologies: the SO assets win, JSON is the deprecated fallback -- the
        // same arrangement units and buildings already use. A tech that has no SO
        // yet still loads from JSON, and says so once, rather than vanishing from
        // the tree.
        if (catalog != null && catalog.HasTechnologies)
        {
            foreach (var so in catalog.technologies)
            {
                if (so == null || string.IsNullOrEmpty(so.id)) continue;
                _technologiesById[so.id] = so.ToDef();
                _techSOsById[so.id] = so;
            }
            int missing = 0;
            foreach (var kv in parsed.Technologies)
            {
                if (_technologiesById.ContainsKey(kv.Key)) continue;
                _technologiesById[kv.Key] = kv.Value;
                missing++;
            }
            if (missing > 0)
                Debug.LogWarning($"[TechCatalog] {missing} technolog{(missing == 1 ? "y" : "ies")} " +
                    "had no SO asset and fell back to TechTree.json. Re-run " +
                    "Waning Border > Tech Tree > Generate Tech SOs.");
        }
        else
        {
            foreach (var kv in parsed.Technologies) _technologiesById[kv.Key] = kv.Value;
        }

        // 2. Units/buildings come from the authoritative SO catalog (JSON = deprecated fallback).
        if (catalog != null && catalog.HasEntries)
        {
            LoadFromCatalog(catalog);
        }
        else
        {
            Debug.LogWarning(
                "[TechCatalog] No TechTreeCatalog found at Resources/TechTreeCatalog — falling back to " +
                "DEPRECATED TechTree.json for unit/building stats. Generate the catalog (Waning Border ▸ " +
                "Tech Tree ▸ Generate Stat SOs) so the game reads the SO stats.");
            foreach (var kv in parsed.Units) _unitsById[kv.Key] = kv.Value;
            foreach (var kv in parsed.Buildings) _buildingsById[kv.Key] = kv.Value;
        }

        // 3. Ensure required buildings exist + Temple fixup (only adds/repairs missing).
        ApplyBuildingDefaults();

        // 3b. Seed the Alanthor King's Court units (Ledger + King Lexor) so they
        //     resolve before their UnitDefSO assets exist in the catalog. Same
        //     fallback pattern as ApplyBuildingDefaults — SO wins if authored.
        ApplyUnitDefaults();

        // 4. Sync the static BuildCosts lookup with the now-loaded data.
        BuildCosts.SyncFromTechTree();

        // 4b. Derive every building's research list from the technologies' own
        //     researchAt. This is the whole point of making researchAt the source
        //     of truth: the player grid reads BuildingDef.research[] and the AI
        //     reads tech.researchAt, and while those were authored separately they
        //     disagreed on 69 of 91 technologies -- techs that no building listed,
        //     so no player could ever research them. Now one field feeds both.
        RebuildResearchLists();

        // 5. Cross-reference audit (2026-08-11, "make the tech tree fool
        //    proof") — every dangling reference surfaces LOUDLY at load
        //    instead of as a silent runtime stall.
        ValidateCrossReferences();
    }

    /// <summary>
    /// One-shot cross-reference audit run at the end of Build(). Every
    /// dangling reference in the tree is a SILENT runtime stall: a tech
    /// whose prerequisite does not exist can never be researched, a
    /// building that trains an unknown unit shows a dead button, a
    /// researchAt host the AI cannot resolve starves its research ladder
    /// (2026-08-11 match: the Survey line stalled 57 minutes and the map's
    /// iron ran out — this audit exists so that CLASS of failure is a
    /// console warning at boot, not a frozen match at minute 40).
    /// </summary>
    /// <summary>
    /// Rewrite each building's <c>research</c> array from the technologies that
    /// name it in <c>researchAt</c>. Buildings keep any tech they already listed
    /// that no longer names them, so a hand-authored entry is reported by the
    /// audit rather than silently dropped here.
    /// </summary>
    /// <summary>The authoring asset behind a technology, when it is SO-backed.</summary>
    public static bool TryGetTechnologySO(string id, out TechDefSO so)
    {
        EnsureLoaded();
        return _techSOsById.TryGetValue(id ?? "", out so);
    }

    private static void RebuildResearchLists()
    {
        var byHost = new Dictionary<string, List<string>>();
        foreach (var tech in _technologiesById.Values)
        {
            if (tech == null || string.IsNullOrEmpty(tech.researchAt)) continue;
            if (!byHost.TryGetValue(tech.researchAt, out var list))
                byHost[tech.researchAt] = list = new List<string>();
            if (!list.Contains(tech.id)) list.Add(tech.id);
        }

        foreach (var kv in _buildingsById)
        {
            var def = kv.Value;
            if (def == null) continue;
            byHost.TryGetValue(kv.Key, out var derived);

            // Anything the building listed that no tech claims: keep it so the
            // audit below can name it. Dropping it here would hide the drift.
            var merged = derived != null ? new List<string>(derived) : new List<string>();
            if (def.research != null)
                foreach (var id in def.research)
                    if (!string.IsNullOrEmpty(id) && !merged.Contains(id)) merged.Add(id);

            def.research = merged.Count > 0 ? merged.ToArray() : System.Array.Empty<string>();
            _derivedResearch[kv.Key] = def.research;
        }

        // A researchAt pointing at a building that does not exist would otherwise
        // be invisible: no building lists the tech, so nothing renders it.
        foreach (var host in byHost.Keys)
            if (!_buildingsById.ContainsKey(host))
                Debug.LogWarning("[TechTreeValidator] " +
                     $"technolog{(byHost[host].Count == 1 ? "y" : "ies")} " +
                     $"[{string.Join(", ", byHost[host])}] research at '{host}', " +
                     "which is not a known building -- they can never be researched");
    }

    private static void ValidateCrossReferences()
    {
        int issues = 0;
        void Warn(string msg)
        {
            issues++;
            Debug.LogWarning("[TechTreeValidator] " + msg);
        }

        // Hosts the AI research ladder can resolve — mirrors the
        // SimpleAISystem.TryResearchTechWithReason host switch.
        var aiHosts = new HashSet<string>
        {
            "Barracks", "Hall", "ArcheryRange", "GatherersHut", "Hut",
            "Alanthor_RoyalStable", "Alanthor_SiegeYard", "Alanthor_Smelter",
            "ShrineOfRidan",
        };

        // Techs reachable through building research lists (the sweep path).
        var sweepReachable = new HashSet<string>();
        foreach (var b in _buildingsById.Values)
        {
            if (b?.research == null) continue;
            for (int i = 0; i < b.research.Length; i++)
            {
                string t = b.research[i];
                if (string.IsNullOrEmpty(t)) continue;
                sweepReachable.Add(t);
                if (!_technologiesById.ContainsKey(t))
                    Warn($"building '{b.id}' lists research '{t}' which has no technology def");
            }
        }

        foreach (var tech in _technologiesById.Values)
        {
            if (tech == null) continue;
            if (tech.prerequisites != null)
                for (int i = 0; i < tech.prerequisites.Length; i++)
                {
                    string p = tech.prerequisites[i];
                    if (!string.IsNullOrEmpty(p) && !_technologiesById.ContainsKey(p))
                        Warn($"tech '{tech.id}' requires '{p}' which does not exist — it can NEVER be researched");
                }

            bool ladderReachable = string.IsNullOrEmpty(tech.researchAt)
                || aiHosts.Contains(tech.researchAt);
            if (!ladderReachable && !sweepReachable.Contains(tech.id))
                Warn($"tech '{tech.id}' is unreachable: researchAt '{tech.researchAt}' has no AI host and no building lists it");
        }

        // A FREE BUILDING is a silent balance hole, not a cheap building. The
        // cost the player pays comes from the SO (BuildCosts.SyncFromTechTree
        // copies it over the code table unconditionally), so an SO whose cost
        // block was never filled in ships that building at zero — which is
        // exactly what nine of them were doing until 2026-08-28, the Hall
        // included. It cannot be papered over with "keep the code value when
        // the SO reads zero": that is the `if (def.hp > 0)` pattern CLAUDE.md
        // forbids, and it makes the SO authoritative only when it happens to be
        // filled in. Fix the data; this is what makes the data loud.
        foreach (var b in _buildingsById.Values)
        {
            if (b == null || b.cost == null) continue;
            if (b.cost.Supplies == 0 && b.cost.Iron == 0
                && b.cost.Veilstone == 0 && b.cost.Veilsteel == 0)
                Warn($"building '{b.id}' costs NOTHING — its SO cost block is all zeros, " +
                     "so it is free to build. Author the cost on the asset.");
        }

        foreach (var b in _buildingsById.Values)
        {
            if (b?.trains == null) continue;
            for (int i = 0; i < b.trains.Length; i++)
            {
                string u = b.trains[i];
                if (string.IsNullOrEmpty(u)) continue;
                if (!_unitsById.ContainsKey(u))
                    Warn($"building '{b.id}' trains '{u}' which has no unit def");
                if (!TheWaningBorder.Entities.UnitFactory.HasRecipe(u))
                    Warn($"building '{b.id}' trains '{u}' which has no UnitFactory recipe — it would spawn the default husk");
            }
        }

        foreach (var u in _unitsById.Values)
        {
            if (u == null) continue;
            if (u.minBuildingLevel > 4)
                Warn($"unit '{u.id}' needs building level {u.minBuildingLevel} — nothing levels that high (military L3, Temple L4): impossible gate");
            if (!TheWaningBorder.Entities.UnitFactory.HasRecipe(u.id))
                Warn($"unit '{u.id}' has a def but no UnitFactory recipe — trainable in UI, broken at spawn");
        }

        // Sect chapel pipeline resolves end-to-end for all 12 sects.
        foreach (var sectId in TheWaningBorder.Economy.SectConfig.AllSectIds)
        {
            string unitId = TheWaningBorder.Economy.SectConfig.UnitIdFor(sectId);
            if (unitId == null)
            {
                Warn($"sect '{sectId}' has no chapel unit mapping");
                continue;
            }
            if (!_unitsById.ContainsKey(unitId))
                Warn($"sect '{sectId}' chapel unit '{unitId}' has no unit def");
            if (!TheWaningBorder.Entities.UnitFactory.HasRecipe(unitId))
                Warn($"sect '{sectId}' chapel unit '{unitId}' has no UnitFactory recipe");
        }

        // ── stat audit ────────────────────────────────────────────────────
        // Entity factories read def fields straight (TechCatalog.Unit /
        // .Building) with no `if (> 0)` guard, because a guard is just a magic
        // number wearing a disguise. That trade only holds if a hole in the SO
        // is LOUD here instead of silently spawning a 0-HP unit mid-match.
        foreach (var u in _unitsById.Values)
        {
            if (u == null) continue;
            if (u.hp <= 0f)          Warn($"unit '{u.id}' has hp {u.hp} — spawns unkillable-adjacent or instantly dead");
            if (u.lineOfSight <= 0f) Warn($"unit '{u.id}' has lineOfSight {u.lineOfSight} — it will never acquire a target");
            if (u.radius <= 0f)      Warn($"unit '{u.id}' has radius {u.radius} — no collision or selection footprint");
        }
        foreach (var b in _buildingsById.Values)
        {
            if (b == null) continue;
            if (b.hp <= 0f)          Warn($"building '{b.id}' has hp {b.hp}");
            if (b.lineOfSight <= 0f) Warn($"building '{b.id}' has lineOfSight {b.lineOfSight}");
        }

        if (issues == 0)
            Debug.Log("[TechTreeValidator] tech tree cross-references clean");
        else
            Debug.LogWarning($"[TechTreeValidator] {issues} issue(s) found — every one is a silent stall or dead button at runtime");
    }

    private static void LoadFromCatalog(TechTreeCatalog catalog)
    {
        if (catalog.units != null)
        {
            foreach (var so in catalog.units)
            {
                if (so == null || string.IsNullOrEmpty(so.id)) continue;
                _unitsById[so.id] = so.ToDef();
                _unitSOsById[so.id] = so;
                if (so.prefab != null && so.presentationId != 0) _prefabByPid[so.presentationId] = so.prefab;
                if (so.animatorController != null && so.presentationId != 0)
                    _controllerByPid[so.presentationId] = so.animatorController;
            }
        }
        if (catalog.buildings != null)
        {
            foreach (var so in catalog.buildings)
            {
                if (so == null || string.IsNullOrEmpty(so.id)) continue;
                _buildingsById[so.id] = so.ToDef();
                _buildingSOsById[so.id] = so;
                if (so.prefab != null && so.presentationId != 0) _prefabByPid[so.presentationId] = so.prefab;
            }
        }
    }

    // ─── lookups (mirror the old TechTreeDB instance API) ────────────────────
    public static bool TryGetUnit(string id, out UnitDef def)
    {
        EnsureLoaded();
        if (_unitSOsById.TryGetValue(id, out var so) && so != null &&
            _unitsById.TryGetValue(id, out var cached))
        {
            so.ApplyTo(cached);   // live refresh
        }
        return _unitsById.TryGetValue(id, out def);
    }

    public static bool TryGetBuilding(string id, out BuildingDef def)
    {
        EnsureLoaded();
        if (id == "ShrineOfAhridan") id = "ShrineOfRidan"; // legacy id alias (pre-rename callers / saves)
        if (_buildingSOsById.TryGetValue(id, out var so) && so != null &&
            _buildingsById.TryGetValue(id, out var cached))
        {
            so.ApplyTo(cached);
            ReapplyDerived(id, cached);
        }
        return _buildingsById.TryGetValue(id, out def);
    }

    public static bool TryGetTechnology(string id, out TechnologyDef def) { EnsureLoaded(); return _technologiesById.TryGetValue(id, out def); }
    public static bool TryGetSect(string id, out SectDef def) { EnsureLoaded(); return _sectsById.TryGetValue(id, out def); }

    public static UnitDef GetUnit(string id) => TryGetUnit(id, out var def) ? def : null;
    public static BuildingDef GetBuilding(string id) => TryGetBuilding(id, out var def) ? def : null;

    // ─── never-null stat accessors (the factory contract) ────────────────────
    // Entity factories used to carry a private `DefaultHP = 800f` ladder and
    // fold the def in with `if (def.hp > 0) hp = def.hp;`. That made the SO the
    // source of truth only when it happened to be filled in, and a magic number
    // the source of truth silently whenever it was not — which is how 50 stat
    // fields ended up living in C# where no designer could find them.
    //
    // These two return a def ALWAYS, so a factory can read `def.hp` straight.
    // A missing id is a data bug, not a runtime branch: it is reported once
    // here and again by the load-time stat audit in ValidateCrossReferences.
    private static readonly HashSet<string> _reportedMissingDefs = new();

    /// <summary>The unit's def. Never null — logs once and returns a stub if the id is unknown.</summary>
    public static UnitDef Unit(string id)
    {
        if (TryGetUnit(id, out var def) && def != null) return def;
        if (_reportedMissingDefs.Add("unit:" + id))
            Debug.LogError($"[TechCatalog] No UnitDefSO for '{id}'. Every stat this unit " +
                "spawns with is now a stub. Author the SO and add it to Resources/TechTreeCatalog.");
        return StubUnit(id);
    }

    /// <summary>The building's def. Never null — logs once and returns a stub if the id is unknown.</summary>
    public static BuildingDef Building(string id)
    {
        if (TryGetBuilding(id, out var def) && def != null) return def;
        if (_reportedMissingDefs.Add("building:" + id))
            Debug.LogError($"[TechCatalog] No BuildingDefSO for '{id}'. Every stat this building " +
                "spawns with is now a stub. Author the SO and add it to Resources/TechTreeCatalog.");
        return StubBuilding(id);
    }

    // hp 1 rather than 0: a stub entity should be visibly broken, not
    // instantly dead in a way that reads as a combat bug.
    private static UnitDef StubUnit(string id) => new UnitDef
    {
        id = id, name = id, hp = 1f, speed = 1f, lineOfSight = 1f, radius = 0.5f,
        defense = new DefenseBlock(), cost = new CostBlock(),
    };

    private static BuildingDef StubBuilding(string id) => new BuildingDef
    {
        id = id, name = id, hp = 1f, lineOfSight = 1f, radius = 1f,
        defense = new DefenseBlock(), cost = new CostBlock(),
    };
    public static TechnologyDef GetTechnology(string id) { EnsureLoaded(); return _technologiesById.TryGetValue(id, out var def) ? def : null; }

    /// <summary>presentationId -> prefab for the spawn path. False = no prefab assigned (caller uses a primitive).</summary>
    public static bool TryGetPrefab(int presentationId, out GameObject prefab)
    {
        EnsureLoaded();
        return _prefabByPid.TryGetValue(presentationId, out prefab);
    }

    /// <summary>
    /// presentationId -> animator controller authored on the unit's SO. False =
    /// none assigned (the spawn path leaves the prefab's own Animator untouched).
    /// </summary>
    public static bool TryGetController(int presentationId, out RuntimeAnimatorController controller)
    {
        EnsureLoaded();
        return _controllerByPid.TryGetValue(presentationId, out controller);
    }

    public static CombatProfile CombatProfile { get { EnsureLoaded(); return _combatProfile; } }
    public static string Faction { get { EnsureLoaded(); return _faction; } }
    public static List<string> Resources { get { EnsureLoaded(); return _resources; } }

    public static IReadOnlyDictionary<string, UnitDef> AllUnits { get { EnsureLoaded(); return _unitsById; } }
    public static IReadOnlyDictionary<string, BuildingDef> AllBuildings { get { EnsureLoaded(); return _buildingsById; } }
    public static IReadOnlyDictionary<string, TechnologyDef> AllTechnologies { get { EnsureLoaded(); return _technologiesById; } }
    public static IReadOnlyDictionary<string, SectDef> AllSects { get { EnsureLoaded(); return _sectsById; } }

    public static IEnumerable<BuildingDef> GetAllBuildings() { EnsureLoaded(); return _buildingsById.Values; }
    public static IEnumerable<UnitDef> GetAllUnits() { EnsureLoaded(); return _unitsById.Values; }
    public static IEnumerable<TechnologyDef> GetAllTechnologies() { EnsureLoaded(); return _technologiesById.Values; }

    // ─── helpers (ported from TechTreeDB) ────────────────────────────────────

    // Age 0 Shrine tech ladder — also carried by the Temple of Ridan (the
    // Shrine's age-up form) so the heal ladder stays researchable after
    // age-up.
    private static readonly string[] ShrineResearch =
        { "HeightenedMasses", "WarriorPriests", "PiousMasses", "FervoredMasses" };

    private static void ApplyBuildingDefaults()
    {
        // RANGED IS AN AGE-1 UNLOCK (design 2026-08-11, Combat_Pacing.md
        // beat 0): the Age-0 archer rush was uncounterable, so the Archery
        // Range era-gates to 2. Forced here as the code-side backstop so a
        // stale SO/JSON minEra can never quietly re-open the rush.
        if (_buildingsById.TryGetValue("ArcheryRange", out var range) && range != null)
            range.minEra = 2;
        _eraOverrides["ArcheryRange"] = 2;

        EnsureBuildingDefault("ShrineOfRidan", "Shrine of Ridan", "Trains Litharchs, +1 RP", 800, 16, 1.8f, 1, new[] { "Litharch" }, ShrineResearch);

        // The veilstone extractor. Seeded for the same reason the sect
        // buildings are: its BuildingDefSO is authored but a new asset is not
        // in Resources/TechTreeCatalog until Unity imports it and someone adds
        // the reference, and until then Building() returns a 1-HP stub and the
        // build menu shows no button. The authored SO wins the moment it
        // loads; this only closes the gap.
        EnsureBuildingDefault("VeilstoneMine", "Veilstone Mine",
            "Veilstone extraction - built on a veilstone outcropping",
            700, 12, 1.5f, 1, System.Array.Empty<string>(), System.Array.Empty<string>());

        // THE TEMPLE IS AN AGE-1 BUILDING. It is common to all three cultures,
        // but it unlocks only once the culture adoption has COMPLETED — era 2,
        // since era 1 is pre-culture Age 0 (FactionEra, EconomyBootstrap).
        // TempleOfRidan.asset now carries minEra 2 as well; this stays as the
        // backstop, and unlike before it actually holds (see _eraOverrides).
        _eraOverrides["TempleOfRidan"] = 2;
        if (!_buildingsById.ContainsKey("TempleOfRidan"))
        {
            EnsureBuildingDefault("TempleOfRidan", "Temple of Ridan", "Sect expansion, training, research", 1500, 18, 2.5f, 2, new[] { "Litharch" }, ShrineResearch);
        }
        else
        {
            var existing = _buildingsById["TempleOfRidan"];
            existing.minEra = 2;
            existing.name = "Temple of Ridan";
            existing.role = "Sect expansion, training, research";
            if (existing.trains == null || existing.trains.Length == 0)
                existing.trains = new[] { "Litharch" };
            if (existing.research == null || existing.research.Length == 0)
                existing.research = ShrineResearch;
            _buildingsById["TempleOfRidan"] = existing;
        }

        // Sect buildings (docs/Design/Sects.md section 1). Seeded in code for
        // the same reason the sect UNITS are: no BuildingDefSO exists yet, and
        // the build menu iterates GetAllBuildings — a building with no def
        // never gets a button, however well its factory is wired. An authored
        // SO loaded above wins; this only fills the gap.
        //
        // Alanthor cluster only so far. The other eight land with their pass.
        SeedSectBuilding("Sect_Reliquary",   "Reliquary",    "Antiquity sect building. Trains the Lorekeeper.",
                         900f,  16f, "Sect_Lorekeeper",  "RoyalIndex");
        SeedSectBuilding("Sect_MendingHall", "Mending Hall", "Renewal sect building. Trains the Scar Guard.",
                         750f,  14f, "Sect_ScarGuard",   "FieldHospital");
        SeedSectBuilding("Sect_Stonehold",   "Stonehold",    "Fortitude sect building. Trains the Stone Warden.",
                         1800f, 12f, "Sect_StoneWarden", "DeepFoundations");
        SeedSectBuilding("Sect_Veilworks",   "Veilworks",    "Reclamation sect building. Trains the Golem Autark.",
                         850f,  15f, "Sect_GolemAutark", "WardensLedger");
        SeedSectBuilding("Sect_MusterYard",  "Muster Yard",  "War sect building. Trains the Warbreaker.",
                         1100f, 14f, "Sect_Warbreaker",  "EndlessMuster");
    }

    /// <summary>
    /// Seed one sect building. Every sect building has the same shape — it
    /// trains exactly its sect's unit and sells exactly its sect's research —
    /// so only hp / line of sight / the two ids differ.
    ///
    /// minEra 1 deliberately: the real gate is adoption, checked in
    /// EntityExtractors.GetBuildingActions, and an era gate on top of it would
    /// only be able to hide a building the player has already paid RP for.
    /// </summary>
    private static void SeedSectBuilding(string id, string name, string role,
        float hp, float los, string unitId, string techId)
        => EnsureBuildingDefault(id, name, role, hp, los, radius: 1.6f, minEra: 1,
                                 trains: new[] { unitId }, research: new[] { techId });

    /// <summary>
    /// Seed the two Alanthor King's Court units in code so they train, show in
    /// the UI, and resolve their cost/training-time before a UnitDefSO exists in
    /// the catalog. Only fills gaps — an authored SO (loaded above) already sits
    /// in _unitsById and is left untouched. Stats mirror the entity factories
    /// (Ledger.cs / KingLexor.cs); numbers are placeholders (owner tunes later).
    /// </summary>
    private static void ApplyUnitDefaults()
    {
        // Values below match the canonical tech-tree calculator
        // (tools/calculator/techtree.json). Owner may still tune.
        EnsureUnitDefault(new UnitDef
        {
            id = "Ledger", name = "Ledger", unitClass = "support",
            hp = 140f, speed = 3.5f, lineOfSight = 10f, trainingTime = 30f,
            damage = 0f, damageType = "melee", armorType = "structure_light",
            cost = CostBlock.Of(150, 40, 20, 0),
            minBuildingLevel = 2,
            abilities = new[] { "Automate Facility" },
        });
        EnsureUnitDefault(new UnitDef
        {
            id = "King Lexor", name = "King Lexor", unitClass = "melee",
            hp = 650f, speed = 7.0f, lineOfSight = 26f, trainingTime = 90f,
            damage = 45f, damageType = "melee", armorType = "cavalry_heavy",
            attackCooldown = 1.4f,
            cost = CostBlock.Of(600, 250, 100, 0),
            minBuildingLevel = 1,
            abilities = new[] { "King's Call", "Liquid Courage" },
        });

        // Sect chapel units for the three LIVE sects (2026-08-11,
        // docs/Design/Sect_Units.md): each adopted sect's chapel trains its
        // unique unit. Stats mirror the entity factories (Judicator.cs /
        // ScarGuard.cs / Warbreaker.cs); costs are the design-doc numbers
        // (owner tunes; an authored SO overrides these).
        EnsureUnitDefault(new UnitDef
        {
            id = "Sect_ScarGuard", name = "Scar Guard", unitClass = "melee",
            hp = 170f, speed = 3.2f, lineOfSight = 10f, trainingTime = 30f,
            damage = 16f, damageType = "melee", armorType = "infantry_heavy",
            attackCooldown = 1.2f,
            cost = CostBlock.Of(140, 50, 20, 0),
            minBuildingLevel = 1,
            abilities = new[] { "Rapid Mend" },
        });
        EnsureUnitDefault(new UnitDef
        {
            id = "Sect_Judicator", name = "Judicator", unitClass = "melee",
            hp = 160f, speed = 3.4f, lineOfSight = 10f, trainingTime = 30f,
            damage = 16f, damageType = "melee", armorType = "infantry_heavy",
            attackCooldown = 1.2f,
            cost = CostBlock.Of(130, 40, 25, 0),
            minBuildingLevel = 1,
            abilities = new[] { "Condemn" },
        });
        EnsureUnitDefault(new UnitDef
        {
            id = "Sect_Warbreaker", name = "Warbreaker", unitClass = "melee",
            hp = 260f, speed = 4.4f, lineOfSight = 10f, trainingTime = 36f,
            damage = 18f, damageType = "melee", armorType = "infantry_heavy",
            attackCooldown = 1.4f,
            cost = CostBlock.Of(180, 70, 30, 0),
            minBuildingLevel = 1,
            abilities = new[] { "War Cry" },
        });

        // Full 12-sect roster (2026-08-11, docs/Design/Sect_Units.md): the
        // remaining chapel units, seeded so adoption + chapel training works
        // for every sect. Stats mirror each entity factory's defaults.
        SeedSectUnit("Sect_Lorekeeper",       "Lorekeeper",         "support",  90, 3.4f,  0, 120, 40,  0);
        SeedSectUnit("Sect_StoneWarden",      "Stone Warden",       "melee",   200, 2.8f, 10, 150, 60, 20);
        SeedSectUnit("Sect_GolemAutark",      "Golem Autark",       "magic",   320, 2.0f, 22, 200, 80, 40);
        SeedSectUnit("Sect_ArchivistAdept",   "Archivist Adept",    "magic",   110, 3.5f, 14, 130, 40, 25);
        SeedSectUnit("Sect_VaultKeeper",      "Vault Keeper",       "melee",   140, 3.5f, 12, 140, 50, 20);
        SeedSectUnit("Sect_GlassmarkArcanist","Glassmark Arcanist", "magic",   100, 3.5f, 18, 150, 40, 30);
        SeedSectUnit("Sect_Ashblade",         "Ashblade",           "melee",   155, 5.0f, 14, 150, 60, 20);
        SeedSectUnit("Sect_Nullblade",        "Nullblade",          "melee",   150, 4.2f, 14, 150, 60, 25);
        SeedSectUnit("Sect_Chaincaster",      "Chaincaster",        "magic",   105, 3.5f, 10, 130, 40, 25);
    }

    /// <summary>Compact seeding helper for the sect chapel units — shared
    /// shape (30 s train, L1 gate, melee/magic damage typing from class).</summary>
    private static void SeedSectUnit(string id, string name, string unitClass,
        float hp, float speed, float damage, int supplies, int iron, int veilstone)
    {
        EnsureUnitDefault(new UnitDef
        {
            id = id, name = name, unitClass = unitClass,
            hp = hp, speed = speed, lineOfSight = 10f, trainingTime = 30f,
            damage = damage,
            damageType = unitClass == "magic" ? "magic" : "melee",
            armorType = "infantry_heavy",
            attackCooldown = 1.2f,
            cost = CostBlock.Of(supplies, iron, veilstone, 0),
            minBuildingLevel = 1,
        });
    }

    private static void EnsureUnitDefault(UnitDef def)
    {
        if (def == null || string.IsNullOrEmpty(def.id)) return;
        if (_unitsById.ContainsKey(def.id)) return; // SO / JSON already provided it
        _unitsById[def.id] = def;
    }

    private static void EnsureBuildingDefault(string id, string name, string role, float hp, float los, float radius, int minEra, string[] trains, string[] research = null)
    {
        if (_buildingsById.ContainsKey(id)) return;
        var raw = BuildCosts.Get(id);
        var cost = CostBlock.Of(raw.Supplies, raw.Iron, raw.Veilstone, raw.Veilsteel);
        _buildingsById[id] = new BuildingDef
        {
            id = id, name = name, role = role, hp = hp,
            lineOfSight = los, radius = radius, minEra = minEra,
            trains = trains, cost = cost,
            research = research ?? System.Array.Empty<string>(),
            armorType = "structure_human"
        };
    }

    private static TextAsset TryLoadJson()
    {
        string[] possiblePaths = { "TechTree", "Data/TechTree", "Config/TechTree", "TechTree/Human" };
        foreach (var path in possiblePaths)
        {
            var asset = UnityEngine.Resources.Load<TextAsset>(path);
            if (asset != null) return asset;
        }
        return null;
    }

    // ─── culture gating ────────────────────────────────────────────────────

    /// <summary>
    /// Is this technology researchable by the given culture?
    ///
    /// GAME RULE, single-sourced (2026-08-12): this used to exist as two
    /// byte-identical copies — SimpleAISystem.TechCultureAllowed and
    /// EntityExtractors.TechAvailableToCulture. The AI decides what to research
    /// with it and the UI decides what to SHOW with it, so any drift between
    /// them meant the AI researching techs the player cannot see, or the panel
    /// offering techs the AI would never take. One copy, both callers.
    ///
    /// `tech.culture` wins when the JSON declares one. Otherwise the id switch
    /// handles the shared-building case: the Gatherer's Hut carries both the
    /// Alanthor Survey drips and the Feraldis Raiding ladder, and each line is
    /// inert for the other culture (a Feraldis hut is a Raider Camp that
    /// gathers nothing, and only Feraldis fields Plunderers).
    /// </summary>
    public static bool CultureAllows(TechnologyDef tech, byte culture)
    {
        if (tech == null) return true;

        if (!string.IsNullOrEmpty(tech.culture))
        {
            switch (tech.culture)
            {
                case "Runai":    return culture == Cultures.Runai;
                case "Alanthor": return culture == Cultures.Alanthor;
                case "Feraldis": return culture == Cultures.Feraldis;
                // Unknown culture name: fall through to the id switch.
            }
        }

        switch (tech.id)
        {
            // Feraldis Raider Camp ladder.
            case "Raiding1":
            case "Raiding2":
            case "Raiding3":
            case "IronPlunder":
            case "VeilstonePlunder":
            case "VeilsteelPlunder":
                return culture == Cultures.Feraldis;

            // Alanthor Guild gather drips — dead weight on a Raider Camp.
            case "IronSurveying1":
            case "IronSurveying2":
            case "IronSurveying3":
            case "VeilstoneSurvey1":
            case "VeilstoneSurvey2":
            case "VeilsteelSurvey":
                return culture != Cultures.Feraldis;

            default:
                return true;
        }
    }

}
