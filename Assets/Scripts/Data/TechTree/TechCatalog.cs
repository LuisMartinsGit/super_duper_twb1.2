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

    private static void Build()
    {
        _unitsById.Clear(); _buildingsById.Clear(); _technologiesById.Clear(); _sectsById.Clear();
        _unitSOsById.Clear(); _buildingSOsById.Clear(); _prefabByPid.Clear(); _controllerByPid.Clear();

        // Fully-qualified: the `Resources` property below would shadow UnityEngine.Resources.
        var catalog = UnityEngine.Resources.Load<TechTreeCatalog>(CatalogResourceName);
        var jsonAsset = TryLoadJson();
        string json = jsonAsset != null ? jsonAsset.text : null;

        // 1. Techs/sects/faction/resources always come from JSON (not yet SO-ified).
        var parsed = TechTreeParser.ParseAll(json);
        _faction = parsed.Faction;
        _resources = parsed.Resources;
        _combatProfile = new CombatProfile { defenseFormulaHint = "" };
        foreach (var kv in parsed.Technologies) _technologiesById[kv.Key] = kv.Value;
        foreach (var kv in parsed.Sects) _sectsById[kv.Key] = kv.Value;

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
        }
        return _buildingsById.TryGetValue(id, out def);
    }

    public static bool TryGetTechnology(string id, out TechnologyDef def) { EnsureLoaded(); return _technologiesById.TryGetValue(id, out def); }
    public static bool TryGetSect(string id, out SectDef def) { EnsureLoaded(); return _sectsById.TryGetValue(id, out def); }

    public static UnitDef GetUnit(string id) => TryGetUnit(id, out var def) ? def : null;
    public static BuildingDef GetBuilding(string id) => TryGetBuilding(id, out var def) ? def : null;
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
        EnsureBuildingDefault("ShrineOfRidan", "Shrine of Ridan", "Trains Litharchs, +1 RP", 800, 16, 1.8f, 1, new[] { "Litharch" }, ShrineResearch);
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
    }

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
}
