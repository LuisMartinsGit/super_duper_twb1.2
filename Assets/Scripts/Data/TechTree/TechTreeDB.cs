// TechTreeDB.cs
// Central database for all unit, building, and technology definitions
// Parses TechTree.json and provides lookup APIs
// Part of: Data/TechTree/

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

    // ═══════════════════════════════════════════════════════════════════════
    // DATA STORAGE
    // ═══════════════════════════════════════════════════════════════════════
    
    private readonly Dictionary<string, UnitDef> _unitsById = new();
    private readonly Dictionary<string, BuildingDef> _buildingsById = new();
    private readonly Dictionary<string, TechnologyDef> _technologiesById = new();
    private readonly Dictionary<string, SectDef> _sectsById = new();
    
    private CombatProfile _combatProfile;
    private string _faction;
    private List<string> _resources = new();

    // ═══════════════════════════════════════════════════════════════════════
    // PUBLIC API - LOOKUPS
    // ═══════════════════════════════════════════════════════════════════════
    
    public bool TryGetUnit(string id, out UnitDef def) => _unitsById.TryGetValue(id, out def);
    public bool TryGetBuilding(string id, out BuildingDef def) => _buildingsById.TryGetValue(id, out def);
    public bool TryGetTechnology(string id, out TechnologyDef def) => _technologiesById.TryGetValue(id, out def);
    public bool TryGetSect(string id, out SectDef def) => _sectsById.TryGetValue(id, out def);
    
    public UnitDef GetUnit(string id) => _unitsById.TryGetValue(id, out var def) ? def : null;
    public BuildingDef GetBuilding(string id) => _buildingsById.TryGetValue(id, out var def) ? def : null;
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
        // Auto-load from Resources if not assigned
        if (humanTechJson == null || string.IsNullOrWhiteSpace(humanTechJson.text))
        {
            humanTechJson = TryLoadFromResources();
        }

        if (humanTechJson == null || string.IsNullOrWhiteSpace(humanTechJson.text))
        {
            Debug.LogError("[TechTreeDB] No JSON provided! Assign TechTree.json or place in Resources folder.");
            return;
        }

        ParseJson(humanTechJson.text);
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
                Debug.Log($"[TechTreeDB] Auto-loaded from Resources/{path}");
                return asset;
            }
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // JSON PARSING
    // ═══════════════════════════════════════════════════════════════════════
    //
    // Fix #220: the previous implementation was 600+ lines of hand-rolled
    // substring parsing (ParseString / ParseFloat / ParseStringArray /
    // ParseDefenseBlock / ParseCostBlock / ParseTechEffects). Every per-field
    // read did IndexOf + Substring + TryParse, and every object boundary was
    // found via LastIndexOf('{', ...) + FindMatchingBrace.
    //
    // The rewrite keeps the ID-indexed lookup approach (find an object whose
    // "id" field matches a target ID, slice it, deserialize it) because it
    // sidesteps the nested eras[] -> variants.{culture}.buildings[] tree the
    // JSON is organized in. But field-level parsing now delegates to
    // UnityEngine.JsonUtility via the intermediate *Json DTOs in
    // TechTreeJsonDtos.cs, eliminating ~350 lines of per-field parser code.
    //
    // One pre-processing step remains: the JSON field "class" is a C#
    // reserved word, so PreprocessSlice() renames it to "unitClass" on the
    // sliced substring before deserialization.

    private void ParseJson(string json)
    {
        try
        {
            Debug.Log("[TechTreeDB] Parsing tech tree...");

            // Global fields via a minimal root DTO
            var root = JsonUtility.FromJson<TechTreeRootJson>(json);
            _faction = string.IsNullOrEmpty(root?.faction) ? "unknown" : root.faction;
            _resources = root?.resources != null
                ? new List<string>(root.resources)
                : new List<string>();

            _combatProfile = new CombatProfile
            {
                defenseFormulaHint = "",
            };

            // Parse Era 1 - Human Core
            Debug.Log("[TechTreeDB] Parsing Era 1 Human units and buildings...");
            ParseBuilding(json, "Hall");
            ParseBuilding(json, "Hut");
            ParseBuilding(json, "GatherersHut");
            ParseBuilding(json, "Barracks");
            ParseBuilding(json, "ShrineOfAhridan");
            ParseBuilding(json, "TempleOfRidan");
            ParseBuilding(json, "VaultOfAlmierra");
            
            ParseUnit(json, "Builder");
            ParseUnit(json, "Miner");
            ParseUnit(json, "Scout");
            ParseUnit(json, "Swordsman");
            ParseUnit(json, "Archer");
            ParseUnit(json, "Litharch");

            // Parse Era 1 - Feraldis (if present)
            Debug.Log("[TechTreeDB] Parsing Feraldis (Era 1 variant)...");
            ParseBuilding(json, "FiendstoneKeep");
            ParseBuilding(json, "Feraldis_BeastPen");
            ParseBuilding(json, "Feraldis_HuntingLodge");
            ParseBuilding(json, "Feraldis_LoggingStation");
            ParseBuilding(json, "Feraldis_Foundry");
            ParseBuilding(json, "Feraldis_Tower");
            ParseBuilding(json, "Feraldis_Longhouse");
            ParseBuilding(json, "Feraldis_SiegeYard");
            
            ParseUnit(json, "Feraldis_Berserker");
            ParseUnit(json, "Feraldis_Hunter");
            ParseUnit(json, "Feraldis_WarboarRider");
            ParseUnit(json, "Feraldis_SiegeRam");

            // Parse Era 2 - Alanthor
            Debug.Log("[TechTreeDB] Parsing Alanthor (Era 2)...");
            ParseBuilding(json, "KingsCourt");
            ParseBuilding(json, "Alanthor_Wall");
            ParseBuilding(json, "Alanthor_Tower");
            ParseBuilding(json, "Alanthor_Garrison");
            ParseBuilding(json, "Alanthor_Stable");
            ParseBuilding(json, "Alanthor_SiegeYard");
            ParseBuilding(json, "Alanthor_Smelter");
            ParseBuilding(json, "Alanthor_Crucible");
            
            ParseUnit(json, "Alanthor_Sentinel");
            ParseUnit(json, "Alanthor_Crossbowman");
            ParseUnit(json, "Alanthor_Cataphract");
            ParseUnit(json, "Alanthor_Ballista");

            // Parse Era 2 - Runai
            Debug.Log("[TechTreeDB] Parsing Runai (Era 2)...");
            ParseBuilding(json, "ThessarasBazaar");
            ParseBuilding(json, "Runai_Outpost");
            ParseBuilding(json, "Runai_TradeHub");
            ParseBuilding(json, "Runai_Vault");
            ParseBuilding(json, "Runai_VeilsteelFoundry");
            ParseBuilding(json, "Runai_SiegeWorkshop");

            ParseUnit(json, "Runai_Spearman");
            ParseUnit(json, "Runai_Skirmisher");
            ParseUnit(json, "Runai_Raider");
            ParseUnit(json, "Runai_Catapult");
            ParseUnit(json, "Runai_Caravan");
            ParseUnit(json, "Runai_Escort");

            // Parse Runai Technologies
            ParseTechnology(json, "Runai_LongHaulTariffs");
            ParseTechnology(json, "Runai_PackBazaar");
            ParseTechnology(json, "Runai_EscortedCaravans");

            // Parse Technologies (Era 1)
            Debug.Log("[TechTreeDB] Parsing Technologies...");
            ParseTechnology(json, "Research_Era2");
            ParseTechnology(json, "ImprovedTools");
            ParseTechnology(json, "StorageCarts");
            ParseTechnology(json, "BasicDrills");
            ParseTechnology(json, "WoodenArmor");

            // Parse Sects
            Debug.Log("[TechTreeDB] Parsing Sects...");
            ParseAllSects(json);

            // Ensure Shrine and Temple entries exist with defaults if not in JSON
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

            // Log summary
            Debug.Log($"[TechTreeDB] ✓ Loaded {_buildingsById.Count} buildings, {_unitsById.Count} units, " +
                     $"{_technologiesById.Count} technologies, {_sectsById.Count} sects");

            // Log sample units for verification
            LogSampleUnits();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[TechTreeDB] Parse error: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void LogSampleUnits()
    {
        string[] sampleUnits = { "Swordsman", "Archer", "Litharch", "Builder" };
        foreach (var unitId in sampleUnits)
        {
            if (_unitsById.TryGetValue(unitId, out var unit))
            {
                Debug.Log($"[TechTreeDB] {unit.id}: HP={unit.hp}, Speed={unit.speed}, " +
                         $"Dmg={unit.damage}, Range={unit.attackRange}, LOS={unit.lineOfSight}");
            }
        }
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

    // ═══════════════════════════════════════════════════════════════════════
    // PER-ID PARSERS (Fix #220 — now delegate to JsonUtility)
    // ═══════════════════════════════════════════════════════════════════════

    void ParseBuilding(string json, string buildingId)
    {
        if (!TrySliceObjectById(json, buildingId, out string slice)) return;
        var dto = JsonUtility.FromJson<BuildingJson>(slice);
        if (dto == null) return;
        _buildingsById[buildingId] = dto.ToDef(buildingId);
    }

    void ParseUnit(string json, string unitId)
    {
        if (!TrySliceObjectById(json, unitId, out string slice)) return;
        // JSON uses "class":; rename inside the slice because 'class' is a C# keyword
        // and JsonUtility matches field names verbatim.
        slice = PreprocessClassKeyword(slice);
        var dto = JsonUtility.FromJson<UnitJson>(slice);
        if (dto == null) return;
        _unitsById[unitId] = dto.ToDef(unitId);
    }

    void ParseTechnology(string json, string techId)
    {
        if (!TrySliceObjectById(json, techId, out string slice))
        {
            Debug.LogWarning($"[TechTreeDB] Technology not found: {techId}");
            return;
        }
        var dto = JsonUtility.FromJson<TechnologyJson>(slice);
        if (dto == null) return;
        _technologiesById[techId] = dto.ToDef(techId);
    }

    /// <summary>
    /// Locate the object whose "id" field matches <paramref name="targetId"/>
    /// and return its full brace-balanced slice.
    /// Returns false if no matching object is found.
    /// </summary>
    bool TrySliceObjectById(string json, string targetId, out string slice)
    {
        slice = null;
        string searchPattern = $"\"id\": \"{targetId}\"";
        int idx = json.IndexOf(searchPattern, StringComparison.Ordinal);
        if (idx == -1) return false;

        int objStart = json.LastIndexOf('{', idx);
        if (objStart == -1) return false;

        int objEnd = FindMatchingBrace(json, objStart);
        if (objEnd == -1) return false;

        slice = json.Substring(objStart, objEnd - objStart + 1);
        return true;
    }

    /// <summary>
    /// Rename JSON field "class" to "unitClass" in a slice so JsonUtility
    /// can map it to a legal C# identifier. Only touches the field-name
    /// position (`"class":`) which never appears inside string values in
    /// this JSON.
    /// </summary>
    static string PreprocessClassKeyword(string slice)
    {
        // Two common serializations: `"class":` and `"class" :`
        return slice
            .Replace("\"class\":", "\"unitClass\":")
            .Replace("\"class\" :", "\"unitClass\" :");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SECTS (Fix #220 — now delegate to JsonUtility per-sect)
    // ═══════════════════════════════════════════════════════════════════════

    void ParseAllSects(string json)
    {
        int sectsIndex = json.IndexOf("\"sects\":", StringComparison.Ordinal);
        if (sectsIndex == -1) return;

        int listIndex = json.IndexOf("\"list\":", sectsIndex, StringComparison.Ordinal);
        if (listIndex == -1) return;

        int arrayStart = json.IndexOf('[', listIndex);
        if (arrayStart == -1) return;

        int arrayEnd = FindMatchingBracket(json, arrayStart);
        if (arrayEnd == -1) return;

        int searchPos = arrayStart + 1;

        while (true)
        {
            int sectStart = json.IndexOf('{', searchPos);
            if (sectStart == -1 || sectStart > arrayEnd) break;

            int sectEnd = FindMatchingBrace(json, sectStart);
            if (sectEnd == -1) break;

            string slice = json.Substring(sectStart, sectEnd - sectStart + 1);
            // Sect units and techs can also use "class":; rewrite once at the
            // sect level so embedded unit blocks deserialize cleanly.
            slice = PreprocessClassKeyword(slice);

            var dto = JsonUtility.FromJson<SectJson>(slice);
            if (dto != null && !string.IsNullOrEmpty(dto.id))
            {
                _sectsById[dto.id] = dto.ToDef();
                RegisterSectEmbeddedUnit(dto);
                RegisterSectEmbeddedTech(dto);
            }

            searchPos = sectEnd + 1;
        }
    }

    /// <summary>
    /// Register the unit block embedded inside a sect entry.
    /// Normalizes the ID: "Golem_Autark" → "Sect_GolemAutark".
    /// </summary>
    void RegisterSectEmbeddedUnit(SectJson sect)
    {
        if (sect.unit == null || string.IsNullOrEmpty(sect.unit.id)) return;

        string rawId = sect.unit.id;
        string normalizedId = "Sect_" + rawId.Replace("_", "");
        string displayName  = rawId.Replace("_", " ");

        var unit = sect.unit.ToDef(
            overrideId: normalizedId,
            overrideName: displayName,
            defaultHp: 100, defaultSpeed: 5, defaultDamage: 10,
            defaultAttackRange: 1.5f, defaultMinRange: 0,
            defaultLoS: 14, defaultTrainingTime: 15,
            defaultArmorType: "infantry_heavy", defaultDamageType: "melee");

        if (unit.cost == null || (unit.cost.Supplies == 0 && unit.cost.Iron == 0 && unit.cost.Crystal == 0))
            unit.cost = new CostBlock { Supplies = 100, Iron = 50 };

        _unitsById[normalizedId] = unit;
        Debug.Log($"[TechTreeDB] Sect unit: {normalizedId} (from {sect.id}) HP={unit.hp} Dmg={unit.damage}");
    }

    /// <summary>
    /// Register the tech block embedded inside a sect entry.
    /// Normalizes the ID: "DietaryMandate" → "Tech_DietaryMandate".
    /// </summary>
    void RegisterSectEmbeddedTech(SectJson sect)
    {
        if (sect.tech == null || string.IsNullOrEmpty(sect.tech.id)) return;

        string rawId = sect.tech.id;
        string normalizedId = "Tech_" + rawId;

        var tech = sect.tech.ToDef(overrideId: normalizedId, defaultResearchTime: 45);
        tech.name = rawId.Replace("_", " ");
        // Sect tech stores the description in the same "effect" field
        if (string.IsNullOrEmpty(tech.desc)) tech.desc = tech.effect;

        if (tech.cost == null || (tech.cost.Supplies == 0 && tech.cost.Iron == 0 && tech.cost.Crystal == 0))
            tech.cost = new CostBlock { Supplies = 150, Iron = 75, Crystal = 50 };

        _technologiesById[normalizedId] = tech;
        Debug.Log($"[TechTreeDB] Sect tech: {normalizedId} (from {sect.id}) Time={tech.researchTime}s");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BRACE / BRACKET MATCHERS (only remaining hand-rolled helpers)
    // ═══════════════════════════════════════════════════════════════════════

    int FindMatchingBracket(string json, int openIndex)
    {
        if (openIndex < 0 || openIndex >= json.Length || json[openIndex] != '[') return -1;

        int depth = 1;
        for (int i = openIndex + 1; i < json.Length; i++)
        {
            if (json[i] == '[') depth++;
            else if (json[i] == ']')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    int FindMatchingBrace(string json, int openIndex)
    {
        if (openIndex < 0 || json[openIndex] != '{') return -1;
        
        int depth = 1;
        for (int i = openIndex + 1; i < json.Length; i++)
        {
            if (json[i] == '{') depth++;
            else if (json[i] == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    // Fix #220: ParseFloat / ParseString / ParseDefenseBlock / ParseCostBlock
    // / ParseTechEffects / ParseStringArray / ParseResourcesArray /
    // ParseCombatProfile have all been removed. Field-level parsing is now
    // handled by UnityEngine.JsonUtility via the *Json DTOs in
    // TechTreeJsonDtos.cs. Only FindMatchingBrace / FindMatchingBracket
    // remain, and they are used solely to locate object boundaries for the
    // ID-indexed slice-and-deserialize approach.
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
    /// Full damage pipeline:
    ///   1. Type modifier   (damage type vs armor type matrix)
    ///   2. Height modifier  (attacker elevation advantage/disadvantage)
    ///   3. Crystal modifier (buff/debuff multiplier)
    ///   4. Defense reduction (diminishing returns: def / (def + 100))
    ///
    /// Returns at least 1 damage.
    /// </summary>
    public static int CalculateFinalDamage(int baseDamage, DamageType dmgType,
        ArmorType armorType, int defenseValue, float heightMod, float crystalMod)
    {
        float typeModifier  = GetModifier(dmgType, armorType);
        float defReduction  = 1f - (defenseValue / (float)(defenseValue + 100));
        int   finalDmg      = (int)math.round(baseDamage * typeModifier * heightMod * crystalMod * defReduction);
        return math.max(1, finalDmg);
    }
}