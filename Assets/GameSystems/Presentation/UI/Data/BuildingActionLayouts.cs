// BuildingActionLayouts.cs
// Fixed 4x4 ACTIONS-panel layouts for buildings that want an authored grid
// (Fortress, Hut/House, Gatherer's Hut/Guild, Barracks/Garrison — Alanthor).
// EVERY building's menu is the same 4 x 4 grid (2026-11-18, developer: "all
// buildings should preserve the 4x4 action grid"; ActionsPanelPrefabBinder),
// so each layout is 16 slots, row-major, 4 per row:
//   row 0      the training row (a building that trains no units leaves it
//              blank) - except where a layout authors its units in rows of
//              their own (the Garrison: AuthoredTraining)
//   rows 1-2   research
//   row 3      free; its LAST cell (slot 15) is the building's level-up,
//              drawn by the panel - a layout leaves slot 15 empty.
//
// THE TRAINING ROW IS NOT AUTHORED HERE (2026-10-03). It is the building
// SO's trains[], culture-gated by CultureGate and level-gated by each unit's
// minBuildingLevel — the SO is the one list of who trains where. The Ledger
// and King Lexor used to be injected into the capital's row by this file;
// they are on the Fortress SO now.
//
// THE GATES ARE THE TECH SO's (2026-10-03, unification item 21). This file
// only says WHERE a tech sits in the grid; whether it shows and whether it is
// clickable is read from the tech's own TechDefSO:
//   * CULTURE — a tech whose `culture` the faction does not have is BLANK
//     (absent, not greyed). Pre-culture that hides every Alanthor tech; the
//     culture pick reveals them (TechCatalog.CultureAllows, the same test the
//     AI uses).
//   * LEVEL — `minBuildingLevel` against the host's BuildingUpgradeState.Level.
//     Un-met, the slot stays in place, greyed, with a "Requires Lv N" note.
//   * PREREQUISITES — the tech's `prerequisites`; the first un-researched one
//     greys the slot with a "Requires <tech>" note.
// There used to be a SECOND gate here, a per-slot faction AGE 0-3 read as
// FactionEra - 1. Nothing ever raises a faction past Age 1, so every slot
// pinned to Age 2/3 (the upper tool tiers, the surveys, Veilstone Walls,
// Shard Pylons) was invisible to players while the AI — which reads
// researchAt and minBuildingLevel — researched them freely. It is gone.
//
// CHAINS pin a tech ladder to ONE slot and show the current un-consumed tier.
// STARTING research (queuing) — not just completing it — CONSUMES the tech: a
// single tech's slot goes blank, a chain advances to the next tier. Cancelling
// a queued tech un-consumes it, so the button returns.
//
// Layouts apply to Alanthor culture (and culture-None pre-culture, where only
// the Age-0 slots show). Other cultures fall back to the panel's generic
// layout (units in row 0, research from row 1) - on the same 4 x 4 grid.

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI.Data
{
    public enum ActionSlotKind : byte { Empty, Train, Tech, Chain }

    /// <summary>One authored grid cell. Placement only — every gate is the
    /// unit's or the tech's SO (see the header).</summary>
    public readonly struct ActionSlot
    {
        public readonly ActionSlotKind Kind;
        public readonly string Id;        // Train unit id / single Tech id
        public readonly int MinLevel;     // Train only: the unit SO's minBuildingLevel
        public readonly string[] Chain;   // Chain only: the tech ids, lowest tier first

        private ActionSlot(ActionSlotKind kind, string id, int minLevel, string[] chain)
        { Kind = kind; Id = id; MinLevel = minLevel; Chain = chain; }

        public static readonly ActionSlot Empty =
            new ActionSlot(ActionSlotKind.Empty, null, 0, null);
        public static ActionSlot Train(string id, int minLevel) =>
            new ActionSlot(ActionSlotKind.Train, id, minLevel, null);
        public static ActionSlot Tech(string id) =>
            new ActionSlot(ActionSlotKind.Tech, id, 0, null);
        public static ActionSlot ChainOf(params string[] tiers) =>
            new ActionSlot(ActionSlotKind.Chain, null, 0, tiers);
    }

    /// <summary>A slot resolved against live state, ready to render.</summary>
    public struct ResolvedSlot
    {
        public bool Empty;
        public bool IsTrain;   // true = train a unit; false = queue research
        public ActionButton Button;
        /// <summary>All tier ids collapsed into this slot (chain slots only —
        /// lets the renderer show the active tier's research progress on the
        /// slot even while it already displays the successor tier).</summary>
        public string[] ChainIds;
    }

    public static class BuildingActionLayouts
    {
        public const int Cols = 4;
        public const int Rows = 4;
        public const int SlotCount = Cols * Rows;

        // ── Authored layouts (keyed by GetBuildingId) ──────────────────────
        // Row-major 4x4: slots 0-3 = training row, 4-11 = research rows,
        // 12-14 free, 15 = the level-up (drawn by the panel; keep it Empty).
        private static readonly Dictionary<string, ActionSlot[]> _layouts = new()
        {
            // FORTRESS — the capital (the Shelter in Age 0).
            //  Age 0: Stone Tools, Armed Scouts
            //  Alanthor (culture-gated on the SOs): Scouting Celestarii,
            //  Mason Guild
            //  Every culture, Fortress L3: Field Intelligence (Territory_Claims
            //  §7.4) - it had no cell in the 3 x 5 layout.
            //  Training row: from the Fortress SO (see the header).
            ["Fortress"] = new[]
            {
                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,

                // Only Stone Tools is researched: the upper tool tiers are
                // granted by the Fortress's levels (decision 53, grantsTechs).
                ActionSlot.Tech("StoneTools"),
                ActionSlot.Tech("ArmedScouts"),
                ActionSlot.Tech("ScoutingCelestarii"),
                ActionSlot.Tech("MasonGuild"),
                // (No wall tech here: Battlements and Shielded Ramparts are
                // researched AT THE WALL HUB. Trade Charters is retired with
                // the Trading Outpost, decision 55.)

                ActionSlot.Tech("FieldIntelligence"),
                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,

                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,
                ActionSlot.Empty,   // slot 15: the level-up
            },

            // HUT -> House (Alanthor) — trains nothing and researches nothing
            // (population is its product); the grid stays blank but for the
            // level-up in slot 15.
            ["Hut"] = new[]
            {
                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,
                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,
                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,
                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,
                ActionSlot.Empty,   // slot 15: the level-up
            },

            // GATHERER'S HUT -> Guild (Alanthor) — one research chain,
            // Alanthor-gated on its SOs (the hut has no research in Age 0).
            ["GatherersHut"] = new[]
            {
                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,
                ActionSlot.ChainOf("IronReinforcements", "VeilstoneWalls", "VeilsteelPylons"),
                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,
                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,
                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,
                ActionSlot.Empty,   // slot 15: the level-up
            },

            // BARRACKS -> Garrison (Alanthor), 2026-11-13. One row per thing:
            //  row 0  the building: Conscription and the three decision-71
            //         ladders (weapons, melee armour - army-wide -, speed),
            //         each ladder in ONE cell
            //  row 1  the Spearman and his skills
            //  row 2  the Swordsman and his
            //  row 3  free; the level-up in its last cell (slot 15) like every
            //         building (it sat in slot 0 of the 3 x 5 grid)
            // The units are authored in their rows, so this grid does not take
            // its top row from trains[] (AuthoredTraining).
            ["Barracks"] = new[]
            {
                // row 0: Conscription, then the three ladders
                ActionSlot.Tech("Conscription"),
                ActionSlot.ChainOf("StoneWeapons", "IronWeapons", "VeilstoneWeapons"),
                ActionSlot.ChainOf("PaddedGambeson", "MailHauberk", "PlateHarness"),
                ActionSlot.ChainOf("Drill", "ForcedMarch", "VeteranStride"),

                // row 1: the Spearman (L1) and his three skills
                ActionSlot.Train("Spearman", 0),
                ActionSlot.Tech("ShieldWall"),
                ActionSlot.Tech("Charge"),
                ActionSlot.Tech("Brace"),

                // row 2: the Swordsman (L2) and his two
                ActionSlot.Train("Alanthor_Swordsman", 2),
                ActionSlot.Tech("Parry"),
                ActionSlot.Tech("RecklessAttack"),
                ActionSlot.Empty,

                // row 3: free, then the level-up (slot 15)
                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,
                ActionSlot.Empty,
            },
        };

        /// <summary>Layouts whose Train slots are authored in the grid: their
        /// top row is not filled from the building SO's trains[].</summary>
        private static readonly HashSet<string> AuthoredTraining = new HashSet<string> { "Barracks" };

        public static bool HasLayout(string buildingId) =>
            buildingId != null && _layouts.ContainsKey(buildingId);

        /// <summary>Faction age = FactionEra.Value - 1, clamped 0..3. NOT a
        /// research gate (see the header); the stats panel's Upgrade button
        /// reads it to know whether the faction has a culture yet.</summary>
        public static int FactionAge(EntityManager em, Faction faction) =>
            System.Math.Max(0, System.Math.Min(3, EntityInfoExtractor.GetFactionEra(em, faction) - 1));

        /// <summary>
        /// Resolve a building's authored layout into 16 render-ready slots.
        /// Returns false (caller falls back to the classic panel) when the
        /// building has no layout or its culture isn't Alanthor / None.
        /// </summary>
        public static bool TryResolve(Entity entity, EntityManager em, out ResolvedSlot[] resolved)
        {
            resolved = null;
            string buildingId = EntityActionExtractor.GetBuildingIdPublic(entity, em);
            if (buildingId == null || !_layouts.TryGetValue(buildingId, out var slots)) return false;
            if (!TechCatalog.IsReady) return false;

            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            byte culture = GetFactionCulture(em, faction);
            if (culture != Cultures.None && culture != Cultures.Alanthor) return false;

            // The Temple's research level is the highest adopted sect level
            // (decision 77); every other host's is its own level.
            int buildingLevel = TheWaningBorder.Entities.ResearchHostLevel.Of(em, entity);

            var research = FactionResearchState.Instance;
            Cost available = EntityActionExtractor.GetFactionResourcesAsCostPublic(em, faction);

            resolved = new ResolvedSlot[SlotCount];
            for (int i = 0; i < SlotCount; i++) resolved[i] = Blank;
            for (int i = 0; i < SlotCount && i < slots.Length; i++)
                resolved[i] = ResolveSlot(slots[i], entity, em, faction, culture, buildingLevel,
                    research, available);
            if (!AuthoredTraining.Contains(buildingId))
                ResolveTrainingRow(buildingId, entity, em, faction, culture, buildingLevel, available, resolved);
            return true;
        }

        /// <summary>
        /// Fill the top row from the building SO's trains[], in order: units
        /// this culture may not field are skipped (CultureGate), and a unit's
        /// minBuildingLevel locks its slot with a "Requires Lv N" note.
        /// </summary>
        private static void ResolveTrainingRow(string buildingId, Entity entity, EntityManager em,
            Faction faction, byte culture, int buildingLevel, Cost available, ResolvedSlot[] resolved)
        {
            for (int c = 0; c < Cols; c++) resolved[c] = Blank;
            if (!TechCatalog.TryGetBuilding(buildingId, out var def) || def.trains == null) return;

            int col = 0;
            foreach (var unitId in def.trains)
            {
                if (col >= Cols) break;
                if (string.IsNullOrEmpty(unitId)) continue;
                if (!CultureGate.CanFactionTrain(unitId, culture)) continue;
                if (CultureGate.IsSupersededByCulture(unitId, culture)) continue;
                int minLevel = TechCatalog.TryGetUnit(unitId, out var unit) ? unit.minBuildingLevel : 0;
                resolved[col++] = ResolveTrain(ActionSlot.Train(unitId, minLevel),
                    entity, em, faction, buildingLevel, available);
            }
        }

        // ── Slot resolution ────────────────────────────────────────────────

        private static readonly ResolvedSlot Blank = new ResolvedSlot { Empty = true };

        private static ResolvedSlot ResolveSlot(ActionSlot slot, Entity building, EntityManager em,
            Faction faction,
            byte culture, int buildingLevel, FactionResearchState research, Cost available)
        {
            switch (slot.Kind)
            {
                case ActionSlotKind.Train:
                    // An authored unit cell obeys the same gates as the
                    // training row: a unit this culture cannot field is absent.
                    if (!CultureGate.CanFactionTrain(slot.Id, culture)) return Blank;
                    if (CultureGate.IsSupersededByCulture(slot.Id, culture)) return Blank;
                    return ResolveTrain(slot, building, em, faction, buildingLevel, available);

                case ActionSlotKind.Tech:
                    if (!CultureShows(slot.Id, culture)) return Blank;
                    // Started (queued) or done -> the single slot goes blank.
                    if (Consumed(slot.Id, em, faction, research)) return Blank;
                    return ResolveTech(slot.Id, em, faction, buildingLevel, research, available);

                case ActionSlotKind.Chain:
                    return ResolveChain(slot, em, faction, culture, buildingLevel, research, available);

                default:
                    return Blank;
            }
        }

        private static ResolvedSlot ResolveTrain(ActionSlot slot, Entity building, EntityManager em,
            Faction faction,
            int buildingLevel, Cost available)
        {
            string name = slot.Id;
            Cost cost = default;
            string effect = "";
            float trainTime = 0f;
            if (TechCatalog.TryGetUnit(slot.Id, out var unit))
            {
                name = EntityInfoExtractor.UnitNameFor(slot.Id, GetFactionCulture(em, faction));
                effect = unit.unitClass;
                // The real duration this building will charge, not the SO
                // base — same fix as the roster tooltip in
                // EntityExtractors.Training. Conscription, the building's
                // level, culture and the sect ladders all live in
                // TrainDuration and none of them were visible here.
                trainTime = TheWaningBorder.Systems.Training.TrainingSystem
                    .TrainDuration(em, building, slot.Id, faction);
                if (unit.cost != null)
                    cost = new Cost
                    {
                        Supplies = unit.cost.Supplies, Iron = unit.cost.Iron,
                        Veilstone = unit.cost.Veilstone,
                    };
            }

            string req = buildingLevel < slot.MinLevel ? $"Requires Lv {slot.MinLevel}" : null;
            bool locked = req != null;
            bool afford = !locked && FactionEconomy.CanAfford(em, faction, cost);

            return new ResolvedSlot
            {
                IsTrain = true,
                Button = new ActionButton
                {
                    Id = slot.Id, Label = name,
                    Tooltip = Tip(name, effect, cost, trainTime, req),
                    Cost = cost, Enabled = !locked, CanAfford = afford,
                    // A picture of the unit as it looks at this level.
                    Icon = ActionIcons.Unit(slot.Id,
                        GetFactionCulture(em, faction) == Cultures.None ? 0 : buildingLevel),
                },
            };
        }

        private static ResolvedSlot ResolveTech(string techId, EntityManager em, Faction faction,
            int buildingLevel, FactionResearchState research, Cost available)
        {
            TechCatalog.TryGetTechnology(techId, out var tech);
            string name = tech != null ? tech.name : techId;
            string effect = tech != null ? (tech.desc ?? tech.effect) : "";
            float time = tech != null ? tech.researchTime : 0f;
            Cost cost = tech != null && tech.cost != null ? new Cost
            {
                Supplies = tech.cost.Supplies, Iron = tech.cost.Iron,
                Veilstone = tech.cost.Veilstone,
            } : default;

            // The SO's gates, in the order a player can act on them.
            string req = null;
            int minLevel = tech != null ? tech.minBuildingLevel : 0;
            if (buildingLevel < minLevel) req = $"Requires Lv {minLevel}";
            else if (tech?.prerequisites != null)
            {
                foreach (var prereq in tech.prerequisites)
                {
                    if (string.IsNullOrEmpty(prereq)) continue;
                    if (research != null && research.HasResearched(faction, prereq)) continue;
                    TechCatalog.TryGetTechnology(prereq, out var pre);
                    req = $"Requires {(pre != null ? pre.name : prereq)}";
                    break;
                }
            }

            bool locked = req != null;
            bool afford = !locked && FactionEconomy.CanAfford(em, faction, cost);

            return new ResolvedSlot
            {
                IsTrain = false,
                Button = new ActionButton
                {
                    Id = techId, Label = name,
                    Tooltip = Tip(name, effect, cost, time, req),
                    Cost = cost, Enabled = !locked, CanAfford = afford,
                    Icon = ActionIcons.Tech(techId),
                },
            };
        }

        private static ResolvedSlot ResolveChain(ActionSlot slot, EntityManager em, Faction faction,
            byte culture, int buildingLevel, FactionResearchState research, Cost available)
        {
            // Active tier = first not yet consumed (researched OR queued). Once
            // every tier is consumed the slot goes blank.
            int idx = -1;
            for (int i = 0; i < slot.Chain.Length; i++)
                if (!Consumed(slot.Chain[i], em, faction, research)) { idx = i; break; }
            if (idx < 0) return Blank;

            // A tier this culture cannot research ends the chain there (the
            // Alanthor tool tiers after Stone Tools, for anyone else).
            string tier = slot.Chain[idx];
            if (!CultureShows(tier, culture)) return Blank;

            var resolved = ResolveTech(tier, em, faction, buildingLevel, research, available);
            resolved.ChainIds = (string[])slot.Chain.Clone();
            return resolved;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        /// <summary>The tech SO's culture gate — the same test the AI uses.
        /// An unknown id shows (its button then reads as a bare id, which is
        /// louder than a silently missing slot).</summary>
        private static bool CultureShows(string techId, byte culture)
            => !TechCatalog.TryGetTechnology(techId, out var tech)
               || TechCatalog.CultureAllows(tech, culture);

        /// <summary>A tech is "consumed" once it is researched OR merely queued
        /// (started). Cancelling a queued tech un-consumes it.</summary>
        private static bool Consumed(string techId, EntityManager em, Faction faction,
            FactionResearchState research)
        {
            if (research != null && research.HasResearched(faction, techId)) return true;
            return EntityActionExtractor.IsTechQueued(em, faction, techId);
        }

        private static string Tip(string name, string effect, Cost cost, float time, string requirement)
        {
            var sb = new System.Text.StringBuilder(128);
            sb.Append(name);
            if (!string.IsNullOrEmpty(effect)) sb.Append('\n').Append(effect);
            if (time > 0f) sb.Append($"\nTime: {time:0}s");
            if (!cost.IsZero) sb.Append("\nCost: ");   // icon line rendered by the panel
            if (!string.IsNullOrEmpty(requirement)) sb.Append('\n').Append(requirement);
            return sb.ToString();
        }

        // Cached queries — CreateEntityQuery per frame leaks into the world's query registry.
        private static readonly ComponentType[] CultureQueryTypes =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<FactionProgress>(),
        };
        private static TheWaningBorder.Core.CachedEntityQuery _cultureQuery;

        private static byte GetFactionCulture(EntityManager em, Faction faction)
        {
            return CultureConfig.GetCompletedCulture(em, faction);
        }
    }
}
