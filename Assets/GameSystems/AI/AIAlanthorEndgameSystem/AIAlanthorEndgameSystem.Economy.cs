// AIAlanthorEndgameSystem.Economy.cs
// Age-2 ladder and expansion.
// Partial of AIAlanthorEndgameSystem.cs -- split 2026-08-12 for readability.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Sect;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    public partial struct AIAlanthorEndgameSystem : ISystem
    {
        // ──────────────────────────────────────────────────────────────────
        // 4. AGE-2 BUILDING LADDER
        // ──────────────────────────────────────────────────────────────────

        // Age-2 build ladder, priority-ordered. Temple leads: sect adoption
        // (chapel plots), Litharch training and the whole religious layer
        // hang off it. (The Practice Range is the LEVELED Archery Range now,
        // not a placeable building.)
        // The Royal Stable and Siege Yard LEFT the ladder (2026-10-04,
        // Game_AI.md 5g): production buildings come from SimpleAISystem only
        // — one per province (the line the army plan needs most), more only
        // while the existing production is saturated (ProductionGate). The
        // ladder placed them at home past that rule.
        private static readonly (string id, float rMin, float rMax)[] Age2Ladder =
        {
            ("TempleOfRidan",          16f, 26f),
        };

        /// <summary>Returns true while a ladder entry is still missing (an
        /// attempt was made this tick or is pending) — the expansion passes
        /// key off this so the core always outranks them.</summary>
        private static bool TryBuildAge2Ladder(Faction faction, EntityManager em, float3 hallPos)
        {
            for (int i = 0; i < Age2Ladder.Length; i++)
            {
                var (id, rMin, rMax) = Age2Ladder[i];
                if (CountFactionBuildings(em, faction, id) > 0) continue;
                // No Religion Point, no Temple (Religion.md §2) — skip it
                // rather than hold the Stable and Siege Yard behind an entry
                // that cannot be placed.
                if (id == "TempleOfRidan"
                    && !TheWaningBorder.Economy.FactionReligionPointsHelper.CanAfford(em, faction,
                           TheWaningBorder.Economy.FactionReligionPointsHelper.Cfg.templeRp))
                    continue;
                TryBuildOnce(faction, em, hallPos, id, rMin, rMax);
                return true; // one ladder attempt per think tick, in order
            }
            return false; // ladder complete — expansion passes may run
        }

        // ──────────────────────────────────────────────────────────────────
        // 4c/4d. EXPANSION TARGETS (endgame completeness)
        // ──────────────────────────────────────────────────────────────────

        /// <summary>Build Huts toward the housing target, one at a time.
        /// Returns true when a foundation was placed or queued this tick.</summary>
        private static bool TryBuildHouses(Faction faction, EntityManager em, float3 hallPos)
        {
            if (CountFactionBuildingsByTag<HutTag>(em, faction) >= Cfg.houseTarget) return false;
            if (AnyFactionBuildingUnderConstruction<HutTag>(em, faction)) return false;
            if (BuildingFactory.AtFactionCap(em, faction, "Hut")) return false;
            // The house quarter: pack new Houses around the ones standing
            // (AICommon.TryHouseQuarterAnchor), the base ring only for the first.
            if (AICommon.TryHouseQuarterAnchor(em, faction, out float3 quarter, hallPos))
                return TryBuildOnce(faction, em, quarter, "Hut", 0f, 12f, flush: true, holdable: true);
            return TryBuildOnce(faction, em, hallPos, "Hut", 12f, 28f, holdable: true);
        }

        /// <summary>Returns true when the foundation was placed (or queued
        /// for lockstep) this tick — false on any pre-flight or placement
        /// failure (the cost is refunded on the rollback paths).</summary>
        private static bool TryBuildOnce(Faction faction, EntityManager em, float3 hallPos,
            string buildingId, float ringMin, float ringMax, bool flush = false,
            bool holdable = false)
        {
            if (!BuildCosts.Exists(buildingId)) return false;
            var cost = BuildCosts.For(em, faction, buildingId);
            if (!FactionEconomy.CanAfford(em, faction, cost)) return false;
            // Discretionary callers (houses, sect buildings) yield to the
            // pivotal savings hold when this building spends a resource the
            // save is short on. The age-2 ladder is never held.
            if (holdable && AIPivotalReserve.ShouldHold(em, faction, cost)) return false;

            // The Temple costs a Religion Point (docs/Design/Religion.md §2);
            // without one the executor refuses it, so do not try every think.
            if (buildingId == "TempleOfRidan"
                && !TheWaningBorder.Economy.FactionReligionPointsHelper.CanAfford(em, faction,
                       TheWaningBorder.Economy.FactionReligionPointsHelper.Cfg.templeRp))
                return false;

            // Pre-flight: need an idle worker. Don't spend cost on a foundation
            // nobody will work on.
            if (AICommon.CountIdleWorkers(em, faction) == 0) return false;

            int2 size = BuildingSizeConfig.GetSize(buildingId);
            // The base rings clog up over a long match (gatherer huts tile the
            // ground around the hall). If the authored ring has no slot, retry
            // once at 1.6x the radius rather than silently stalling the ladder
            // forever — an outlying stable beats no stable.
            // A flush search (the House quarter) lets footprints touch.
            float3 pos;
            if (flush)
            {
                if (!AIEndgameCommon.TryFindBuildSpotRingGap(em, hallPos, size, ringMin, ringMax * 1.6f,
                        angleSamples: 24, radiusStep: 4f, seededStart: true, gap: 0f, out pos, buildingId))
                    return false;
            }
            else if (!TryFindBuildPositionRing(em, hallPos, size, ringMin, ringMax, out pos)
                && !TryFindBuildPositionRing(em, hallPos, size, ringMax, ringMax * 1.6f, out pos))
                return false;

            // No AI-side Spend: PlaceBuildingDirect charges the cost on
            // every peer (docs/Multiplayer_LAN_Readiness.md).

            // Replicating entry point (audit F4) — PlaceBuildingDirect was
            // host-only. Queued case: dispatch at the position, null target;
            // workers auto-find the foundation on arrival.
            bool queuedPlacement = CommandRouter.IssuePlaceBuilding(em, buildingId, pos, faction,
                out Entity building, CommandSource.AI);
            if (queuedPlacement)
            {
                AICommon.DispatchWorkersTo(em, faction, Entity.Null, buildingId, pos, maxWorkers: 2);
                AILogger.Log(faction, "BUILDING", $"Alanthor age-2 ladder: queued {buildingId}");
                return true;
            }
            // Null = the executor rejected (cap or bank short) — nothing was
            // spent, so there is nothing to refund.
            if (building == Entity.Null) return false;

            int dispatched = AICommon.DispatchWorkersTo(em, faction, building, buildingId, pos, maxWorkers: 2);
            if (dispatched == 0)
            {
                FactionEconomy.Add(em, faction, cost);
                em.DestroyEntity(building);
                return false;
            }
            AILogger.Log(faction, "BUILDING", $"Alanthor age-2 ladder: queued {buildingId}");
            return true;
        }
        /// <summary>Count this faction's buildings by marker tag (completed
        /// AND under construction — expansion targets are totals).</summary>
        private static int CountFactionBuildingsByTag<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            var query = AIQueryCache.TagFaction<T>(em);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            // Plans count too (docs/Design/Planned_Buildings.md): an ordered
            // building whose worker is still walking is already decided.
            int count = TheWaningBorder.Entities.PlannedBuildings.CountOf(em, faction, TheWaningBorder.Entities.PlannedBuildings.IdsFor<T>());
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) count++;
            return count;
        }

        /// <summary>True while any of this faction's buildings with the given
        /// marker tag is still under construction — the one-foundation-at-a-
        /// time gate for the expansion passes.</summary>
        private static bool AnyFactionBuildingUnderConstruction<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            // A plan is a site that has not broken ground yet.
            if (TheWaningBorder.Entities.PlannedBuildings.CountOf(em, faction, TheWaningBorder.Entities.PlannedBuildings.IdsFor<T>()) > 0) return true;
            var query = AIQueryCache.TagFactionUnderConstruction<T>(em);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) return true;
            return false;
        }
    }
}
