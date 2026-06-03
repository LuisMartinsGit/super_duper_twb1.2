// TechTreeDB.cs
// Central database for all unit, building, and technology definitions.
// Part of: Data/TechTree/
//
// STAT SOURCE (units + buildings): the editable ScriptableObject catalog
// (Assets/Resources/TechTreeCatalog -> UnitDefSO/BuildingDefSO under
// Assets/GameData/TechTree/**). This is now the AUTHORITATIVE source — tune stats
// by editing the .asset files in the Inspector. The catalog is auto-loaded from
// Resources when not wired in the scene, so it works without manual setup.
//
// TechTree.json is DEPRECATED as a stat source (it drifted from the design docs and
// caused balance bugs). It is retained ONLY for technologies + sects, which have not
// been migrated to SOs yet. If no catalog is found, the DB falls back to JSON for
// unit/building stats and logs a warning — that path is deprecated and should not be
// relied on.

using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using TheWaningBorder.Data;

[DefaultExecutionOrder(-10000)]
public sealed class TechTreeDB : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════════════
    // SINGLETON
    // ═══════════════════════════════════════════════════════════════════════
    
    public static TechTreeDB Instance { get; private set; }

    [Header("Tech Tree JSON")]
    [Tooltip("Assign the TechTree.json TextAsset here, or leave null for auto-load from Resources")]
    public TextAsset humanTechJson;

    [Header("Stat Source (ScriptableObjects) — AUTHORITATIVE")]
    [Tooltip("TechTreeCatalog of editable Unit/Building SO assets. This is the authoritative " +
             "unit/building stat source. If left null it is auto-loaded from " +
             "Resources/TechTreeCatalog. TechTree.json is only a deprecated fallback.")]
    public TechTreeCatalog catalog;

    // Resources path (without extension) used to auto-load the catalog when the field
    // above is not wired in the scene. Keep the asset at Assets/Resources/TechTreeCatalog.asset.
    private const string CatalogResourceName = "TechTreeCatalog";

    // ═══════════════════════════════════════════════════════════════════════
    // DATA STORAGE
    // ═══════════════════════════════════════════════════════════════════════
    
    private readonly Dictionary<string, UnitDef> _unitsById = new();
    private readonly Dictionary<string, BuildingDef> _buildingsById = new();
    private readonly Dictionary<string, TechnologyDef> _technologiesById = new();
    private readonly Dictionary<string, SectDef> _sectsById = new();

    // When catalog mode is active these hold the source SOs, so TryGet* can refresh
    // the cached def from the asset each call — that is what makes Inspector edits
    // take effect on the next-spawned entity ("on the fly").
    private readonly Dictionary<string, UnitDefSO> _unitSOsById = new();
    private readonly Dictionary<string, BuildingDefSO> _buildingSOsById = new();

    private CombatProfile _combatProfile;
    private string _faction;
    private List<string> _resources = new();

    // ═══════════════════════════════════════════════════════════════════════
    // PUBLIC API - LOOKUPS
    // ═══════════════════════════════════════════════════════════════════════
    
    public bool TryGetUnit(string id, out UnitDef def)
    {
        // In catalog mode, refresh the cached def from the editable asset so live
        // Inspector tweaks apply to the next spawn. Mutates in place (no alloc).
        if (_unitSOsById.TryGetValue(id, out var so) && so != null &&
            _unitsById.TryGetValue(id, out var cached))
        {
            so.ApplyTo(cached);
        }
        return _unitsById.TryGetValue(id, out def);
    }

    public bool TryGetBuilding(string id, out BuildingDef def)
    {
        if (_buildingSOsById.TryGetValue(id, out var so) && so != null &&
            _buildingsById.TryGetValue(id, out var cached))
        {
            so.ApplyTo(cached);
        }
        return _buildingsById.TryGetValue(id, out def);
    }

    public bool TryGetTechnology(string id, out TechnologyDef def) => _technologiesById.TryGetValue(id, out def);
    public bool TryGetSect(string id, out SectDef def) => _sectsById.TryGetValue(id, out def);

    public UnitDef GetUnit(string id) => TryGetUnit(id, out var def) ? def : null;
    public BuildingDef GetBuilding(string id) => TryGetBuilding(id, out var def) ? def : null;
    public TechnologyDef GetTechnology(string id) => _technologiesById.TryGetValue(id, out var def) ? def : null;
    
    public CombatProfile CombatProfile => _combatProfile;
    public string Faction => _faction;
    public List<string> Resources => _resources;
    
    public IReadOnlyDictionary<string, UnitDef> AllUnits => _unitsById;
    public IReadOnlyDictionary<string, BuildingDef> AllBuildings => _buildingsById;
    public IReadOnlyDictionary<string, TechnologyDef> AllTechnologies => _technologiesById;
    public IReadOnlyDictionary<string, SectDef> AllSects => _sectsById;
    /// <summary>
    /// Get all building definitions.
    /// </summary>
    public IEnumerable<BuildingDef> GetAllBuildings() => _buildingsById.Values;

    /// <summary>
    /// Get all unit definitions.
    /// </summary>
    public IEnumerable<UnitDef> GetAllUnits() => _unitsById.Values;

    /// <summary>
    /// Get all technology definitions.
    /// </summary>
    public IEnumerable<TechnologyDef> GetAllTechnologies() => _technologiesById.Values;

    // ═══════════════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ═══════════════════════════════════════════════════════════════════════
    
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        LoadTechTree();

        // Sync BuildCosts static lookup with authoritative TechTree data
        TheWaningBorder.Data.BuildCosts.SyncFromTechTree();
    }

    private void LoadTechTree()
    {
        // Authoritative stat source: the SO catalog. Auto-load from Resources when the
        // scene field is not wired so unit/building stats work without manual setup.
        if (catalog == null)
        {
            // Fully-qualified: the `Resources` property on this class (List<string>) would
            // otherwise shadow UnityEngine.Resources.
            catalog = UnityEngine.Resources.Load<TechTreeCatalog>(CatalogResourceName);
        }

        // TechTree.json is DEPRECATED for stats; still parsed for technologies + sects
        // (and as the unit/building fallback only when no catalog is found).
        if (humanTechJson == null || string.IsNullOrWhiteSpace(humanTechJson.text))
        {
            humanTechJson = TryLoadFromResources();
        }

        string json = humanTechJson != null ? humanTechJson.text : null;
        BuildFromSources(json);
    }

    /// <summary>
    /// Re-run loading from the current sources. Safe to call at runtime (e.g. from an
    /// editor "reload" button) to pick up bulk catalog changes.
    /// </summary>
    public void ReloadFromCatalog() => BuildFromSources(humanTechJson != null ? humanTechJson.text : null);

    /// <summary>
    /// Populate every lookup. Technologies/sects/faction/resources always come from
    /// JSON. Units/buildings come from the catalog SOs when one is assigned with
    /// entries; otherwise from JSON.
    /// </summary>
    private void BuildFromSources(string json)
    {
        _unitsById.Clear();
        _buildingsById.Clear();
        _technologiesById.Clear();
        _sectsById.Clear();
        _unitSOsById.Clear();
        _buildingSOsById.Clear();

        // 1. Parse JSON via the shared parser (never throws; empty if json is blank).
        var parsed = TechTreeParser.ParseAll(json);
        _faction = parsed.Faction;
        _resources = parsed.Resources;
        _combatProfile = new CombatProfile { defenseFormulaHint = "" };

        foreach (var kv in parsed.Technologies) _technologiesById[kv.Key] = kv.Value;
        foreach (var kv in parsed.Sects) _sectsById[kv.Key] = kv.Value;

        // 2. Units/buildings come from the authoritative SO catalog. JSON is only a
        //    deprecated fallback when no catalog is present (logs a warning).
        if (catalog != null && catalog.HasEntries)
        {
            LoadUnitsBuildingsFromCatalog();
        }
        else
        {
            Debug.LogWarning(
                "[TechTreeDB] No TechTreeCatalog found — falling back to DEPRECATED TechTree.json " +
                "for unit/building stats. Place the catalog at Assets/Resources/TechTreeCatalog.asset " +
                "(or assign it on the TechTreeDB component) so the game reads the SO stats.");
            foreach (var kv in parsed.Units) _unitsById[kv.Key] = kv.Value;
            foreach (var kv in parsed.Buildings) _buildingsById[kv.Key] = kv.Value;
        }

        // 3. Ensure required buildings exist + Temple fixup (only adds/repairs missing).
        ApplyBuildingDefaults();
    }

    private void LoadUnitsBuildingsFromCatalog()
    {
        if (catalog.units != null)
        {
            foreach (var so in catalog.units)
            {
                if (so == null || string.IsNullOrEmpty(so.id)) continue;
                _unitsById[so.id] = so.ToDef();
                _unitSOsById[so.id] = so;
            }
        }
        if (catalog.buildings != null)
        {
            foreach (var so in catalog.buildings)
            {
                if (so == null || string.IsNullOrEmpty(so.id)) continue;
                _buildingsById[so.id] = so.ToDef();
                _buildingSOsById[so.id] = so;
            }
        }
    }

    /// <summary>
    /// Inject the always-required Shrine/Temple building entries and apply the
    /// Temple-of-Ridan era/training fixup. Runs in both JSON and catalog modes;
    /// only adds or repairs entries that are missing.
    /// </summary>
    private void ApplyBuildingDefaults()
    {
        EnsureBuildingDefault("ShrineOfAhridan", "Shrine of Ahridan", "Trains Litharchs, +1 RP", 800, 16, 1.8f, 1, new[] { "Litharch" });
        if (!_buildingsById.ContainsKey("TempleOfRidan"))
        {
            EnsureBuildingDefault("TempleOfRidan", "Temple of Ridan", "Sect expansion, training, research", 1500, 18, 2.5f, 2, new[] { "Litharch" });
        }
        else
        {
            // Update existing entry to set minEra=2 and trains Litharch
            var existing = _buildingsById["TempleOfRidan"];
            existing.minEra = 2;
            existing.name = "Temple of Ridan";
            existing.role = "Sect expansion, training, research";
            if (existing.trains == null || existing.trains.Length == 0)
                existing.trains = new[] { "Litharch" };
            _buildingsById["TempleOfRidan"] = existing;
        }
    }

    private TextAsset TryLoadFromResources()
    {
        string[] possiblePaths = {
            "TechTree",
            "Data/TechTree",
            "Config/TechTree",
            "TechTree/Human"
        };

        foreach (var path in possiblePaths)
        {
            var asset = UnityEngine.Resources.Load<TextAsset>(path);
            if (asset != null)
            {
                return asset;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BUILDING DEFAULT (used when a required building is missing from JSON)
    // ═══════════════════════════════════════════════════════════════════════

    void EnsureBuildingDefault(string id, string name, string role, float hp, float los, float radius, int minEra, string[] trains)
    {
        if (_buildingsById.ContainsKey(id)) return;
        var raw = BuildCosts.Get(id);
        var cost = CostBlock.Of(raw.Supplies, raw.Iron, raw.Crystal, raw.Veilsteel, raw.Glow);
        _buildingsById[id] = new BuildingDef
        {
            id = id, name = name, role = role, hp = hp,
            lineOfSight = los, radius = radius, minEra = minEra,
            trains = trains, cost = cost,
            armorType = "structure_human"
        };
    }

    // JSON parsing (slice-and-deserialize, sect handling, brace matchers) now lives
    // in the shared static TechTreeParser, used by both this DB and the editor
    // TechTreeSOGenerator. See TechTreeParser.cs.
}

// ═══════════════════════════════════════════════════════════════════════════════
// COMBAT MODIFIER MATRIX
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Static lookup for damage-type x armor-type modifier matrix and final damage calculation.
/// Lazy-initialized on first access. Thread-safe via static initializer.
///
/// Matrix layout:
///   Rows = DamageType (Melee, Ranged, Siege, Magic, True)
///   Cols = ArmorType  (InfantryLight, InfantryHeavy, Ranged, Cavalry, Structure, StructureHuman)
/// </summary>
public static class CombatModifiers
{
    // 5 damage types x 6 armor types
    private static readonly float[,] _modifiers;

    static CombatModifiers()
    {
        _modifiers = new float[5, 6];

        // Melee vs: Light=1.0, Heavy=1.0, Ranged=1.1, Cavalry=0.9, Structure=0.2, StructHuman=0.2
        _modifiers[0, 0] = 1.0f;  _modifiers[0, 1] = 1.0f;  _modifiers[0, 2] = 1.1f;
        _modifiers[0, 3] = 0.9f;  _modifiers[0, 4] = 0.2f;  _modifiers[0, 5] = 0.2f;

        // Ranged vs: 1.1, 0.9, 1.0, 0.8, 0.15, 0.15
        _modifiers[1, 0] = 1.1f;  _modifiers[1, 1] = 0.9f;  _modifiers[1, 2] = 1.0f;
        _modifiers[1, 3] = 0.8f;  _modifiers[1, 4] = 0.15f; _modifiers[1, 5] = 0.15f;

        // Siege vs: 0.6, 0.8, 0.8, 0.7, 3.0, 2.4
        _modifiers[2, 0] = 0.6f;  _modifiers[2, 1] = 0.8f;  _modifiers[2, 2] = 0.8f;
        _modifiers[2, 3] = 0.7f;  _modifiers[2, 4] = 3.0f;  _modifiers[2, 5] = 2.4f;

        // Magic vs: 1.1, 0.9, 1.1, 1.0, 0.5, 0.45
        _modifiers[3, 0] = 1.1f;  _modifiers[3, 1] = 0.9f;  _modifiers[3, 2] = 1.1f;
        _modifiers[3, 3] = 1.0f;  _modifiers[3, 4] = 0.5f;  _modifiers[3, 5] = 0.45f;

        // True vs: all 1.0 (ignores armor type)
        _modifiers[4, 0] = 1.0f;  _modifiers[4, 1] = 1.0f;  _modifiers[4, 2] = 1.0f;
        _modifiers[4, 3] = 1.0f;  _modifiers[4, 4] = 1.0f;  _modifiers[4, 5] = 1.0f;
    }

    /// <summary>
    /// Look up the damage modifier for a given damage-type attacking a given armor-type.
    /// </summary>
    public static float GetModifier(DamageType dmg, ArmorType armor)
    {
        return _modifiers[(int)dmg, (int)armor];
    }

    /// <summary>
    /// Extract the defense value relevant to the incoming damage type.
    /// True damage always returns 0 (bypasses defense).
    /// </summary>
    public static int GetDefenseValue(Defense def, DamageType dmgType)
    {
        return dmgType switch
        {
            DamageType.Melee  => def.Melee,
            DamageType.Ranged => def.Ranged,
            DamageType.Siege  => def.Siege,
            DamageType.Magic  => def.Magic,
            DamageType.True   => 0, // True damage ignores defense
            _ => 0
        };
    }

    /// <summary>
    /// Global damage scalar applied at the END of the damage pipeline. 0.5
    /// halves all outgoing damage and gives the player more reaction time
    /// (combat-pacing knob — adjust here to tune TTK across the whole game).
    /// </summary>
    public const float GlobalDamageMultiplier = 0.5f;

    /// <summary>
    /// Full damage pipeline:
    ///   1. Type modifier   (damage type vs armor type matrix)
    ///   2. Height modifier  (attacker elevation advantage/disadvantage)
    ///   3. Crystal modifier (buff/debuff multiplier)
    ///   4. Defense reduction (diminishing returns: def / (def + 100))
    ///   5. Global damage scalar (combat pacing — see GlobalDamageMultiplier)
    ///
    /// Returns at least 1 damage.
    /// </summary>
    public static int CalculateFinalDamage(int baseDamage, DamageType dmgType,
        ArmorType armorType, int defenseValue, float heightMod, float crystalMod)
    {
        float typeModifier  = GetModifier(dmgType, armorType);
        float defReduction  = 1f - (defenseValue / (float)(defenseValue + 100));
        int   finalDmg      = (int)math.round(
            baseDamage * typeModifier * heightMod * crystalMod * defReduction
            * GlobalDamageMultiplier);
        return math.max(1, finalDmg);
    }
}