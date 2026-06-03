// TechTreeParser.cs
// Static, side-effect-free parser for TechTree.json.
// Part of: Data/TechTree/
//
// Extracted from TechTreeDB so the SAME parsing path is shared by:
//   * TechTreeDB runtime JSON fallback (when no catalog is assigned), and
//   * TechTreeSOGenerator (editor tool that turns the JSON into SO assets).
//
// Keeps the ID-indexed slice-and-deserialize approach (find the object whose
// "id" matches a target, brace-balance its slice, hand it to JsonUtility via the
// *Json DTOs in TechTreeJsonDtos.cs). Building-default injection (Shrine/Temple)
// and BuildCosts syncing remain in TechTreeDB — those are DB post-processing
// concerns, not parsing.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Data
{
    /// <summary>Raw result of parsing TechTree.json. No defaults injected.</summary>
    public sealed class TechTreeParseResult
    {
        public readonly Dictionary<string, UnitDef> Units = new();
        public readonly Dictionary<string, BuildingDef> Buildings = new();
        public readonly Dictionary<string, TechnologyDef> Technologies = new();
        public readonly Dictionary<string, SectDef> Sects = new();
        public string Faction = "unknown";
        public List<string> Resources = new();
    }

    public static class TechTreeParser
    {
        // ═══════════════════════════════════════════════════════════════════
        // ENTRY POINT
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Parse the full tech tree from JSON text. Returns an empty result on
        /// null/blank input or on any parse error (never throws).
        /// </summary>
        public static TechTreeParseResult ParseAll(string json)
        {
            var result = new TechTreeParseResult();
            if (string.IsNullOrWhiteSpace(json)) return result;

            try
            {
                var root = JsonUtility.FromJson<TechTreeRootJson>(json);
                result.Faction = string.IsNullOrEmpty(root?.faction) ? "unknown" : root.faction;
                result.Resources = root?.resources != null
                    ? new List<string>(root.resources)
                    : new List<string>();

                // Era 1 — Human Core
                ParseBuilding(json, "Hall", result);
                ParseBuilding(json, "Hut", result);
                ParseBuilding(json, "GatherersHut", result);
                ParseBuilding(json, "Barracks", result);
                ParseBuilding(json, "ArcheryRange", result);
                ParseBuilding(json, "ShrineOfAhridan", result);
                ParseBuilding(json, "TempleOfRidan", result);
                ParseBuilding(json, "VaultOfAlmierra", result);

                ParseUnit(json, "Builder", result);
                ParseUnit(json, "Miner", result);
                ParseUnit(json, "Scout", result);
                ParseUnit(json, "Swordsman", result);
                ParseUnit(json, "Archer", result);
                ParseUnit(json, "Crossbowman", result);
                ParseUnit(json, "Longbowman", result);
                ParseUnit(json, "Litharch", result);

                // Era 1 — Feraldis
                ParseBuilding(json, "FiendstoneKeep", result);
                ParseBuilding(json, "Feraldis_BeastPen", result);
                ParseBuilding(json, "Feraldis_HuntingLodge", result);
                ParseBuilding(json, "Feraldis_LoggingStation", result);
                ParseBuilding(json, "Feraldis_Foundry", result);
                ParseBuilding(json, "Feraldis_Tower", result);
                ParseBuilding(json, "Feraldis_Longhouse", result);
                ParseBuilding(json, "Feraldis_SiegeYard", result);

                ParseUnit(json, "Feraldis_Berserker", result);
                ParseUnit(json, "Feraldis_Hunter", result);
                ParseUnit(json, "Feraldis_WarboarRider", result);
                ParseUnit(json, "Feraldis_SiegeRam", result);

                // Era 2 — Alanthor
                ParseBuilding(json, "KingsCourt", result);
                ParseBuilding(json, "Alanthor_Wall", result);
                ParseBuilding(json, "Alanthor_Tower", result);
                ParseBuilding(json, "Alanthor_PracticeRange", result);
                ParseBuilding(json, "Alanthor_SiegeYard", result);
                ParseBuilding(json, "Alanthor_Smelter", result);
                ParseBuilding(json, "Alanthor_Crucible", result);

                ParseUnit(json, "Alanthor_Sentinel", result);
                ParseUnit(json, "Alanthor_Crossbowman", result);
                ParseUnit(json, "Alanthor_Cataphract", result);
                ParseUnit(json, "Alanthor_Ballista", result);

                // Era 2 — Runai
                ParseBuilding(json, "ThessarasBazaar", result);
                ParseBuilding(json, "Runai_Outpost", result);
                ParseBuilding(json, "Runai_TradeHub", result);
                ParseBuilding(json, "Runai_Vault", result);
                ParseBuilding(json, "Runai_VeilsteelFoundry", result);
                ParseBuilding(json, "Runai_SiegeWorkshop", result);

                ParseUnit(json, "Runai_Spearman", result);
                ParseUnit(json, "Runai_Skirmisher", result);
                ParseUnit(json, "Runai_Raider", result);
                ParseUnit(json, "Runai_Catapult", result);
                ParseUnit(json, "Runai_Caravan", result);
                ParseUnit(json, "Runai_Escort", result);

                // Runai technologies
                ParseTechnology(json, "Runai_LongHaulTariffs", result);
                ParseTechnology(json, "Runai_PackBazaar", result);
                ParseTechnology(json, "Runai_EscortedCaravans", result);

                // Era 1 technologies
                ParseTechnology(json, "Research_Era2", result);
                ParseTechnology(json, "ImprovedTools", result);
                ParseTechnology(json, "StorageCarts", result);
                ParseTechnology(json, "BasicDrills", result);
                ParseTechnology(json, "WoodenArmor", result);

                // Sects (+ embedded sect units/techs)
                ParseAllSects(json, result);
            }
            catch (Exception)
            {
                // Mirror the original behaviour: swallow parse errors, return what we have.
            }

            return result;
        }

        // ═══════════════════════════════════════════════════════════════════
        // PER-ID PARSERS (delegate field-level parsing to JsonUtility via DTOs)
        // ═══════════════════════════════════════════════════════════════════

        static void ParseBuilding(string json, string buildingId, TechTreeParseResult result)
        {
            if (!TrySliceObjectById(json, buildingId, out string slice)) return;
            var dto = JsonUtility.FromJson<BuildingJson>(slice);
            if (dto == null) return;
            result.Buildings[buildingId] = dto.ToDef(buildingId);
        }

        static void ParseUnit(string json, string unitId, TechTreeParseResult result)
        {
            if (!TrySliceObjectById(json, unitId, out string slice)) return;
            slice = PreprocessClassKeyword(slice);
            var dto = JsonUtility.FromJson<UnitJson>(slice);
            if (dto == null) return;
            result.Units[unitId] = dto.ToDef(unitId);
        }

        static void ParseTechnology(string json, string techId, TechTreeParseResult result)
        {
            if (!TrySliceObjectById(json, techId, out string slice)) return;
            var dto = JsonUtility.FromJson<TechnologyJson>(slice);
            if (dto == null) return;
            result.Technologies[techId] = dto.ToDef(techId);
        }

        // ═══════════════════════════════════════════════════════════════════
        // SECTS
        // ═══════════════════════════════════════════════════════════════════

        static void ParseAllSects(string json, TechTreeParseResult result)
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
                slice = PreprocessClassKeyword(slice);

                var dto = JsonUtility.FromJson<SectJson>(slice);
                if (dto != null && !string.IsNullOrEmpty(dto.id))
                {
                    result.Sects[dto.id] = dto.ToDef();
                    RegisterSectEmbeddedUnit(dto, result);
                    RegisterSectEmbeddedTech(dto, result);
                }

                searchPos = sectEnd + 1;
            }
        }

        static void RegisterSectEmbeddedUnit(SectJson sect, TechTreeParseResult result)
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

            result.Units[normalizedId] = unit;
        }

        static void RegisterSectEmbeddedTech(SectJson sect, TechTreeParseResult result)
        {
            if (sect.tech == null || string.IsNullOrEmpty(sect.tech.id)) return;

            string rawId = sect.tech.id;
            string normalizedId = "Tech_" + rawId;

            var tech = sect.tech.ToDef(overrideId: normalizedId, defaultResearchTime: 45);
            tech.name = rawId.Replace("_", " ");
            if (string.IsNullOrEmpty(tech.desc)) tech.desc = tech.effect;

            if (tech.cost == null || (tech.cost.Supplies == 0 && tech.cost.Iron == 0 && tech.cost.Crystal == 0))
                tech.cost = new CostBlock { Supplies = 150, Iron = 75, Crystal = 50 };

            result.Technologies[normalizedId] = tech;
        }

        // ═══════════════════════════════════════════════════════════════════
        // SLICE / PRE-PROCESS / BRACE-MATCH HELPERS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Locate the object whose "id" field matches <paramref name="targetId"/>
        /// and return its full brace-balanced slice. False if not found.
        /// </summary>
        static bool TrySliceObjectById(string json, string targetId, out string slice)
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

        /// <summary>Rename JSON field "class" to "unitClass" ('class' is a C# keyword).</summary>
        static string PreprocessClassKeyword(string slice)
        {
            return slice
                .Replace("\"class\":", "\"unitClass\":")
                .Replace("\"class\" :", "\"unitClass\" :");
        }

        static int FindMatchingBracket(string json, int openIndex)
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

        static int FindMatchingBrace(string json, int openIndex)
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
    }
}
