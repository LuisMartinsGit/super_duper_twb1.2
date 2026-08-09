// BuildingActionLayouts.cs
// Fixed 3x5 ACTIONS-panel layouts for buildings that want an authored grid
// (Hall/King's Court, Hut/House, Gatherer's Hut/Guild — Alanthor). Each
// building gets 15 slots (3 rows x 5 cols, row-major), matching the authored
// ActionsPanel prefab. The top row is EXCLUSIVELY the training row; rows 2-3
// hold research (a building that trains no units leaves the top row blank).
//
// TWO independent gates (spec 2026-07-12, revised):
//   * APPEAR AGE — the faction age at which the slot ENTERS the grid. Below it
//     the slot is BLANK (absent, not greyed). Base buttons appear at Age 0; the
//     culture-specific ones appear at Age 1 (the culture pick). "Age" is
//     FactionEra.Value - 1 (Era 1 = pre-culture = Age 0; culture -> Age 1;
//     Temple eras -> Age 2/3).
//   * AVAILABILITY — once shown, a slot is clickable only when its building
//     LEVEL requirement is met (BuildingUpgradeState.Level) and, for chain
//     tiers, its per-tier AGE is met. Building level tracks age (Lv N is
//     reachable at Age N via the stats-panel Upgrade button), so a "needs
//     Lv 2" button lights up exactly at Age 2. Un-met slots stay in place,
//     greyed, with a "Requires Lv N / Age N" note.
//
// CHAINS pin a tech ladder to ONE slot and show the current un-consumed tier;
// each tier is age-gated. STARTING research (queuing) — not just completing it
// — CONSUMES the tech: a single tech's slot goes blank, a chain advances to the
// next tier. Cancelling a queued tech un-consumes it, so the button returns.
//
// Layouts apply to Alanthor culture (and culture-None pre-culture, where only
// the Age-0 slots show). Other cultures fall back to the classic panel.
//
// Location: Assets/Scripts/UI/Panels/

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI.Panels
{
    public enum ActionSlotKind : byte { Empty, Train, Tech, Chain }

    /// <summary>One tier of a chain slot: a tech id plus the Age it unlocks at.</summary>
    public readonly struct ActionTier
    {
        public readonly string Id;
        public readonly int Age;
        public ActionTier(string id, int age) { Id = id; Age = age; }
    }

    /// <summary>One authored grid cell.</summary>
    public readonly struct ActionSlot
    {
        public readonly ActionSlotKind Kind;
        public readonly string Id;        // Train unit id / single Tech id
        public readonly int AppearAge;    // faction age at which the slot shows
        public readonly int MinLevel;     // required BuildingUpgradeState.Level (0 = none)
        public readonly string Prereq;    // tech id that must be researched first (null = none)
        public readonly ActionTier[] Chain;

        private ActionSlot(ActionSlotKind kind, string id, int appearAge, int minLevel,
            string prereq, ActionTier[] chain)
        { Kind = kind; Id = id; AppearAge = appearAge; MinLevel = minLevel; Prereq = prereq; Chain = chain; }

        public static readonly ActionSlot Empty =
            new ActionSlot(ActionSlotKind.Empty, null, 0, 0, null, null);
        public static ActionSlot Train(string id, int appearAge, int minLevel = 0) =>
            new ActionSlot(ActionSlotKind.Train, id, appearAge, minLevel, null, null);
        public static ActionSlot Tech(string id, int appearAge, int minLevel = 0, string prereq = null) =>
            new ActionSlot(ActionSlotKind.Tech, id, appearAge, minLevel, prereq, null);
        public static ActionSlot ChainOf(int minLevel, params ActionTier[] tiers) =>
            new ActionSlot(ActionSlotKind.Chain, null,
                tiers.Length > 0 ? tiers[0].Age : 0, minLevel, null, tiers);
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
        public const int Cols = 5;
        public const int Rows = 3;
        public const int SlotCount = Cols * Rows;

        // ── Authored layouts (keyed by GetBuildingId) ──────────────────────
        // Row-major 3x5: slots 0-4 = training row, 5-14 = research rows.
        private static readonly Dictionary<string, ActionSlot[]> _layouts = new()
        {
            // HALL -> King's Court (Alanthor)
            //  base (Age 0): Worker, Scout / Stone-tools chain
            //  King's Court (Age 1+): + Ledger(Lv2), King Lexor(Lv3),
            //                          Scouting Celestarii, Mason Guild(Lv2)
            ["Hall"] = new[]
            {
                ActionSlot.Train("Worker", appearAge: 0),
                ActionSlot.Train("Scout", appearAge: 0),
                ActionSlot.Train("Ledger", appearAge: 1, minLevel: 2),
                ActionSlot.Train("King Lexor", appearAge: 1, minLevel: 3),
                ActionSlot.Empty,

                ActionSlot.ChainOf(0,
                    new ActionTier("StoneTools", 0),
                    new ActionTier("IronTools", 1),
                    new ActionTier("VeilstoneTools", 2),
                    new ActionTier("VeilsteelTools", 3)),
                ActionSlot.Tech("ArmedScouts", appearAge: 0),
                ActionSlot.Tech("ScoutingCelestarii", appearAge: 1),
                ActionSlot.Tech("MasonGuild", appearAge: 1, minLevel: 2),
                ActionSlot.Empty,

                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,
            },

            // HUT -> House (Alanthor) — trains nothing, so the top row stays
            // blank and the single tech sits in the first research slot.
            ["Hut"] = new[]
            {
                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,
                ActionSlot.Tech("RetaliatoryMeasures", appearAge: 1, minLevel: 2),
                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,
                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,
            },

            // GATHERER'S HUT -> Guild (Alanthor) — three research chains + one
            // single. Each chain slot appears at its first tier's age.
            ["GatherersHut"] = new[]
            {
                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,
                ActionSlot.ChainOf(0,
                    new ActionTier("IronSurveying1", 1),
                    new ActionTier("IronSurveying2", 2),
                    new ActionTier("IronSurveying3", 3)),
                ActionSlot.ChainOf(2,
                    new ActionTier("VeilstoneSurvey1", 2),
                    new ActionTier("VeilstoneSurvey2", 3)),
                ActionSlot.Tech("VeilsteelSurvey", appearAge: 3, minLevel: 3, prereq: "VeilstoneSurvey2"),
                ActionSlot.ChainOf(0,
                    new ActionTier("IronReinforcements", 1),
                    new ActionTier("VeilstoneWalls", 2),
                    new ActionTier("VeilsteelPylons", 3)),
                ActionSlot.Empty,
                ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty, ActionSlot.Empty,
            },
        };

        public static bool HasLayout(string buildingId) =>
            buildingId != null && _layouts.ContainsKey(buildingId);

        /// <summary>Faction age = FactionEra.Value - 1, clamped 0..3.</summary>
        public static int FactionAge(EntityManager em, Faction faction) =>
            System.Math.Max(0, System.Math.Min(3, EntityInfoExtractor.GetFactionEra(em, faction) - 1));

        /// <summary>
        /// Resolve a building's authored layout into 15 render-ready slots.
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

            int factionAge = FactionAge(em, faction);
            int buildingLevel = 1;
            if (em.HasComponent<BuildingUpgradeState>(entity))
                buildingLevel = System.Math.Max(1, (int)em.GetComponentData<BuildingUpgradeState>(entity).Level);

            var research = FactionResearchState.Instance;
            Cost available = EntityActionExtractor.GetFactionResourcesAsCostPublic(em, faction);

            resolved = new ResolvedSlot[SlotCount];
            for (int i = 0; i < SlotCount && i < slots.Length; i++)
                resolved[i] = ResolveSlot(slots[i], em, faction, factionAge, buildingLevel,
                    research, available);
            return true;
        }

        // ── Slot resolution ────────────────────────────────────────────────

        private static readonly ResolvedSlot Blank = new ResolvedSlot { Empty = true };

        private static ResolvedSlot ResolveSlot(ActionSlot slot, EntityManager em, Faction faction,
            int factionAge, int buildingLevel, FactionResearchState research, Cost available)
        {
            switch (slot.Kind)
            {
                case ActionSlotKind.Train:
                    if (factionAge < slot.AppearAge) return Blank;
                    return ResolveTrain(slot, em, faction, buildingLevel, available);

                case ActionSlotKind.Tech:
                    if (factionAge < slot.AppearAge) return Blank;
                    // Started (queued) or done -> the single slot goes blank.
                    if (Consumed(slot.Id, em, faction, research)) return Blank;
                    return ResolveTech(slot.Id, slot.MinLevel, slot.Prereq, /*tierAge*/ 0,
                        em, faction, factionAge, buildingLevel, research, available);

                case ActionSlotKind.Chain:
                    return ResolveChain(slot, em, faction, factionAge, buildingLevel, research, available);

                default:
                    return Blank;
            }
        }

        private static ResolvedSlot ResolveTrain(ActionSlot slot, EntityManager em, Faction faction,
            int buildingLevel, Cost available)
        {
            string name = slot.Id;
            Cost cost = default;
            string effect = "";
            float trainTime = 0f;
            if (TechCatalog.TryGetUnit(slot.Id, out var unit))
            {
                name = unit.name ?? slot.Id;
                effect = unit.unitClass;
                trainTime = unit.trainingTime;
                if (unit.cost != null)
                    cost = new Cost
                    {
                        Supplies = unit.cost.Supplies, Iron = unit.cost.Iron,
                        Veilstone = unit.cost.Veilstone, Veilsteel = unit.cost.Veilsteel,
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
                },
            };
        }

        private static ResolvedSlot ResolveTech(string techId, int minLevel, string prereq,
            int tierAge, EntityManager em, Faction faction, int factionAge, int buildingLevel,
            FactionResearchState research, Cost available)
        {
            TechCatalog.TryGetTechnology(techId, out var tech);
            string name = tech != null ? tech.name : techId;
            string effect = tech != null ? (tech.desc ?? tech.effect) : "";
            float time = tech != null ? tech.researchTime : 0f;
            Cost cost = tech != null && tech.cost != null ? new Cost
            {
                Supplies = tech.cost.Supplies, Iron = tech.cost.Iron,
                Veilstone = tech.cost.Veilstone, Veilsteel = tech.cost.Veilsteel,
            } : default;

            string req = null;
            if (factionAge < tierAge) req = $"Requires Age {tierAge}";
            else if (buildingLevel < minLevel) req = $"Requires Lv {minLevel}";
            else if (prereq != null && !(research != null && research.HasResearched(faction, prereq)))
            {
                TechCatalog.TryGetTechnology(prereq, out var pre);
                req = $"Requires {(pre != null ? pre.name : prereq)}";
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
                },
            };
        }

        private static ResolvedSlot ResolveChain(ActionSlot slot, EntityManager em, Faction faction,
            int factionAge, int buildingLevel, FactionResearchState research, Cost available)
        {
            // The slot appears once the faction reaches the first tier's age.
            if (factionAge < slot.Chain[0].Age) return Blank;

            // Active tier = first not yet consumed (researched OR queued). Once
            // every tier is consumed the slot goes blank.
            int idx = -1;
            for (int i = 0; i < slot.Chain.Length; i++)
                if (!Consumed(slot.Chain[i].Id, em, faction, research)) { idx = i; break; }
            if (idx < 0) return Blank;

            var tier = slot.Chain[idx];
            var resolved = ResolveTech(tier.Id, slot.MinLevel, /*prereq*/ null, tier.Age,
                em, faction, factionAge, buildingLevel, research, available);
            resolved.ChainIds = new string[slot.Chain.Length];
            for (int i = 0; i < slot.Chain.Length; i++)
                resolved.ChainIds[i] = slot.Chain[i].Id;
            return resolved;
        }

        // ── Helpers ────────────────────────────────────────────────────────

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
            var q = _cultureQuery.Get(em, CultureQueryTypes);
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            using var prog = q.ToComponentDataArray<FactionProgress>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (facs[i].Value == faction) return prog[i].Culture;
            return Cultures.None;
        }
    }
}
