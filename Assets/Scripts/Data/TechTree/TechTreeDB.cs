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
    
    private void ParseJson(string json)
    {
        try
        {
            Debug.Log("[TechTreeDB] Parsing tech tree...");

            // Parse global data
            _faction = ParseString(json, "faction", "unknown");
            ParseResourcesArray(json);
            ParseCombatProfile(json);

            // Parse Era 1 - Human Core
            Debug.Log("[TechTreeDB] Parsing Era 1 Human units and buildings...");
            ParseBuilding(json, "Hall");
            ParseBuilding(json, "Hut");
            ParseBuilding(json, "GatherersHut");
            ParseBuilding(json, "Barracks");
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
            ParseUnit(json, "Runai_SandBallista");
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
    // PARSE RESOURCES ARRAY
    // ═══════════════════════════════════════════════════════════════════════
    
    void ParseResourcesArray(string json)
    {
        int resourcesIndex = json.IndexOf("\"resources\":");
        if (resourcesIndex == -1) return;
        
        int arrayStart = json.IndexOf("[", resourcesIndex);
        int arrayEnd = json.IndexOf("]", arrayStart);
        
        if (arrayStart == -1 || arrayEnd == -1) return;
        
        string arrayContent = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
        string[] parts = arrayContent.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var part in parts)
        {
            string cleaned = part.Trim().Trim('"');
            if (!string.IsNullOrEmpty(cleaned))
                _resources.Add(cleaned);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PARSE COMBAT PROFILE
    // ═══════════════════════════════════════════════════════════════════════
    
    void ParseCombatProfile(string json)
    {
        _combatProfile = new CombatProfile();
        
        int profileIndex = json.IndexOf("\"combatProfile\":");
        if (profileIndex == -1) return;
        
        _combatProfile.defenseFormulaHint = ParseString(
            json.Substring(profileIndex), 
            "defenseFormulaHint", 
            ""
        );
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PARSE BUILDING
    // ═══════════════════════════════════════════════════════════════════════
    
    void ParseBuilding(string json, string buildingId)
    {
        string searchPattern = $"\"id\": \"{buildingId}\"";
        int buildingIndex = json.IndexOf(searchPattern);
        
        if (buildingIndex == -1)
        {
            // Silently skip - not all buildings may be in every JSON
            return;
        }

        int objStart = json.LastIndexOf('{', buildingIndex);
        int objEnd = FindMatchingBrace(json, objStart);
        
        if (objStart == -1 || objEnd == -1) return;
        
        string buildingJson = json.Substring(objStart, objEnd - objStart + 1);

        var building = new BuildingDef
        {
            id = buildingId,
            name = ParseString(buildingJson, "name", buildingId),
            role = ParseString(buildingJson, "role", ""),
            hp = ParseFloat(buildingJson, "hp", 0, 1000),
            armorType = ParseString(buildingJson, "armorType", "structure_human"),
            lineOfSight = ParseFloat(buildingJson, "lineOfSight", 0, 20),
            radius = ParseFloat(buildingJson, "radius", 0, 1.6f),
            defense = ParseDefenseBlock(buildingJson),
            trains = ParseStringArray(buildingJson, "trains"),
            research = ParseStringArray(buildingJson, "research"),
            cost = ParseCostBlock(buildingJson),
            minEra = (int)ParseFloat(buildingJson, "minEra", 0, 0)
        };

        _buildingsById[buildingId] = building;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PARSE UNIT
    // ═══════════════════════════════════════════════════════════════════════
    
    void ParseUnit(string json, string unitId)
    {
        string searchPattern = $"\"id\": \"{unitId}\"";
        int unitIndex = json.IndexOf(searchPattern);
        
        if (unitIndex == -1)
        {
            // Silently skip - not all units may be in every JSON
            return;
        }

        int objStart = json.LastIndexOf('{', unitIndex);
        int objEnd = FindMatchingBrace(json, objStart);
        
        if (objStart == -1 || objEnd == -1) return;
        
        string unitJson = json.Substring(objStart, objEnd - objStart + 1);

        var unit = new UnitDef
        {
            id = unitId,
            unitClass = ParseString(unitJson, "class", ""),
            name = ParseString(unitJson, "name", unitId),
            hp = ParseFloat(unitJson, "hp", 0, 100),
            speed = ParseFloat(unitJson, "speed", 0, 5),
            trainingTime = ParseFloat(unitJson, "trainingTime", 0, 5),
            damage = ParseFloat(unitJson, "damage", 0, 10),
            attackRange = ParseFloat(unitJson, "attackRange", 0, 1.5f),
            minAttackRange = ParseFloat(unitJson, "minAttackRange", 0, 0f),
            lineOfSight = ParseFloat(unitJson, "lineOfSight", 0, 20),
            armorType = ParseString(unitJson, "armorType", "infantry"),
            damageType = ParseString(unitJson, "damageType", "melee"),
            defense = ParseDefenseBlock(unitJson),
            cost = ParseCostBlock(unitJson),
            buildSpeed = ParseFloat(unitJson, "buildSpeed", 0, 0f),
            gatheringSpeed = ParseFloat(unitJson, "gatheringSpeed", 0, 0f),
            carryCapacity = (int)ParseFloat(unitJson, "carryCapacity", 0, 0f),
            healsPerSecond = ParseFloat(unitJson, "healsPerSecond", 0, 0f)
        };

        _unitsById[unitId] = unit;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PARSE TECHNOLOGY
    // ═══════════════════════════════════════════════════════════════════════
    
    void ParseTechnology(string json, string techId)
    {
        string searchPattern = $"\"id\": \"{techId}\"";
        int techIndex = json.IndexOf(searchPattern);
        
        if (techIndex == -1)
        {
            Debug.LogWarning($"[TechTreeDB] Technology not found: {techId}");
            return;
        }

        int objStart = json.LastIndexOf('{', techIndex);
        int objEnd = FindMatchingBrace(json, objStart);
        
        if (objStart == -1 || objEnd == -1) return;
        
        string techJson = json.Substring(objStart, objEnd - objStart + 1);

        var tech = new TechnologyDef
        {
            id = techId,
            name = ParseString(techJson, "name", techId),
            effect = ParseString(techJson, "effect", ""),
            desc = ParseString(techJson, "desc", ""),
            role = ParseString(techJson, "role", ""),
            researchTime = ParseFloat(techJson, "researchTime", 0, 30),
            researchAt = ParseString(techJson, "researchAt", ""),
            cost = ParseCostBlock(techJson),
            prerequisites = ParseStringArray(techJson, "requires"),
            effects = ParseTechEffects(techJson)
        };

        _technologiesById[techId] = tech;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // PARSE SECTS
    // ═══════════════════════════════════════════════════════════════════════
    
    void ParseAllSects(string json)
    {
        int sectsIndex = json.IndexOf("\"sects\":");
        if (sectsIndex == -1) return;
        
        int listIndex = json.IndexOf("\"list\":", sectsIndex);
        if (listIndex == -1) return;
        
        int arrayStart = json.IndexOf("[", listIndex);
        if (arrayStart == -1) return;
        
        int searchPos = arrayStart;
        int arrayEnd = json.IndexOf("]", arrayStart);
        
        while (true)
        {
            int sectStart = json.IndexOf("{", searchPos);
            if (sectStart == -1 || sectStart > arrayEnd) break;
            
            int sectEnd = FindMatchingBrace(json, sectStart);
            if (sectEnd == -1) break;
            
            string sectJson = json.Substring(sectStart, sectEnd - sectStart + 1);
            
            var sect = new SectDef
            {
                id = ParseString(sectJson, "id", ""),
                order = ParseString(sectJson, "order", ""),
                affinity = ParseString(sectJson, "affinity", "")
            };
            
            if (!string.IsNullOrEmpty(sect.id))
                _sectsById[sect.id] = sect;
            
            searchPos = sectEnd + 1;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HELPER PARSERS
    // ═══════════════════════════════════════════════════════════════════════
    
    DefenseBlock ParseDefenseBlock(string json)
    {
        var defense = new DefenseBlock();
        
        int defIndex = json.IndexOf("\"defense\":");
        if (defIndex == -1) return defense;
        
        int blockStart = json.IndexOf("{", defIndex);
        int blockEnd = json.IndexOf("}", blockStart);
        
        if (blockStart == -1 || blockEnd == -1) return defense;
        
        string defBlock = json.Substring(blockStart, blockEnd - blockStart + 1);
        
        defense.melee = (int)ParseFloat(defBlock, "melee", 0, 0);
        defense.ranged = (int)ParseFloat(defBlock, "ranged", 0, 0);
        defense.siege = (int)ParseFloat(defBlock, "siege", 0, 0);
        defense.magic = (int)ParseFloat(defBlock, "magic", 0, 0);
        
        return defense;
    }

    CostBlock ParseCostBlock(string json)
    {
        var cost = new CostBlock();
        
        int costIndex = json.IndexOf("\"cost\":");
        if (costIndex == -1) return cost;
        
        int blockStart = json.IndexOf("{", costIndex);
        int blockEnd = json.IndexOf("}", blockStart);
        
        if (blockStart == -1 || blockEnd == -1) return cost;
        
        string costBlock = json.Substring(blockStart, blockEnd - blockStart + 1);
        
        cost.Supplies = (int)ParseFloat(costBlock, "Supplies", 0, 0);
        cost.Iron = (int)ParseFloat(costBlock, "Iron", 0, 0);
        cost.Crystal = (int)ParseFloat(costBlock, "Crystal", 0, 0);
        cost.Veilsteel = (int)ParseFloat(costBlock, "Veilsteel", 0, 0);
        cost.Glow = (int)ParseFloat(costBlock, "Glow", 0, 0);
        
        return cost;
    }

    TechEffects ParseTechEffects(string json)
    {
        int effectsIndex = json.IndexOf("\"effects\":");
        if (effectsIndex == -1) return null;

        int blockStart = json.IndexOf("{", effectsIndex);
        int blockEnd = json.IndexOf("}", blockStart);

        if (blockStart == -1 || blockEnd == -1) return null;

        string effectsBlock = json.Substring(blockStart, blockEnd - blockStart + 1);

        var effects = new TechEffects
        {
            gatherSpeedMult = ParseFloat(effectsBlock, "gatherSpeedMult", 0, 0),
            carryCapacityBonus = (int)ParseFloat(effectsBlock, "carryCapacityBonus", 0, 0),
            meleeAttackSpeedMult = ParseFloat(effectsBlock, "meleeAttackSpeedMult", 0, 0),
            meleeDefenseAdd = (int)ParseFloat(effectsBlock, "meleeDefenseAdd", 0, 0)
        };

        return effects.HasAnyEffect ? effects : null;
    }

    string[] ParseStringArray(string json, string fieldName)
    {
        var result = new List<string>();
        
        string pattern = $"\"{fieldName}\":";
        int index = json.IndexOf(pattern);
        if (index == -1) return result.ToArray();
        
        int arrayStart = json.IndexOf("[", index);
        int arrayEnd = json.IndexOf("]", arrayStart);
        
        if (arrayStart == -1 || arrayEnd == -1) return result.ToArray();
        
        string arrayContent = json.Substring(arrayStart + 1, arrayEnd - arrayStart - 1);
        string[] parts = arrayContent.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var part in parts)
        {
            string cleaned = part.Trim().Trim('"');
            if (!string.IsNullOrEmpty(cleaned))
                result.Add(cleaned);
        }
        
        return result.ToArray();
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

    float ParseFloat(string json, string fieldName, float notFoundValue, float defaultValue)
    {
        string pattern = $"\"{fieldName}\":";
        int index = json.IndexOf(pattern);
        
        if (index == -1) return notFoundValue;
        
        int start = index + pattern.Length;
        
        // Skip whitespace
        while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
        
        // Find end of number
        int end = start;
        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.' || json[end] == '-'))
            end++;
        
        if (end == start) return defaultValue;
        
        string numStr = json.Substring(start, end - start);
        if (float.TryParse(numStr, System.Globalization.NumberStyles.Float, 
                          System.Globalization.CultureInfo.InvariantCulture, out float result))
            return result;
        
        return defaultValue;
    }

    string ParseString(string json, string fieldName, string defaultValue)
    {
        string pattern = $"\"{fieldName}\":";
        int index = json.IndexOf(pattern);
        
        if (index == -1) return defaultValue;
        
        int start = json.IndexOf('"', index + pattern.Length);
        if (start == -1) return defaultValue;
        
        int end = json.IndexOf('"', start + 1);
        if (end == -1) return defaultValue;
        
        return json.Substring(start + 1, end - start - 1);
    }
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