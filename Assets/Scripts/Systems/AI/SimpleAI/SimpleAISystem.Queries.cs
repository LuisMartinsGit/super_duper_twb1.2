// SimpleAISystem.Queries.cs
// Shared entity-query helpers: counts, lookups, classification predicates.
// Partial of SimpleAISystem.cs -- split 2026-08-12 for readability.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Data.AI;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.World.FogOfWar;
using TheWaningBorder.World.Terrain;
using UnityEngine;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        /// <summary>Least-queued completed trainer of the given tag — this is
        /// what makes multiple Barracks/Ranges train in PARALLEL (the old
        /// first-found lookup funneled every order into one building's
        /// 5-slot queue no matter how many stood idle).</summary>
        private static Entity FindLeastBusyTrainer<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            Entity best = Entity.Null;
            int bestQueue = int.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                if (!em.HasBuffer<TrainQueueItem>(ents[i])) continue;
                int len = em.GetBuffer<TrainQueueItem>(ents[i]).Length;
                if (len < bestQueue) { bestQueue = len; best = ents[i]; }
            }
            return best;
        }

        /// <summary>The faction's completed culture (from its Hall's
        /// FactionProgress) — Cultures.None while still Age 0 or mid
        /// age-up research.</summary>
        private static byte FactionCultureOf(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var prog = q.ToComponentDataArray<FactionProgress>(Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) return prog[i].Culture;
            return Cultures.None;
        }

        /// <summary>Under-construction foundations of a tag — the "in
        /// flight" count for pipeline-style building (Gatherer's Huts).</summary>
        private static int CountFactionBuildingsUnderConstruction<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<UnderConstruction>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) n++;
            return n;
        }

        /// <summary>Faction buildings of a tag, INCLUDING under-construction
        /// foundations — growth targets must count them or the maintenance
        /// loop re-places the same building every tick until one finishes.</summary>
        private static int CountFactionBuildings<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<FactionTag>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) n++;
            return n;
        }

        private static bool TrainsUnit(EntityManager em, string buildingId, string unitId)
        {
            if (!TechCatalog.TryGetBuilding(buildingId, out var def) || def?.trains == null) return false;
            for (int i = 0; i < def.trains.Length; i++)
                if (def.trains[i] == unitId) return true;
            return false;
        }
        /// <summary>
        /// COMMAND FOLLOW-THROUGH: a worker already committed to construction
        /// or repair must not be re-tasked by any other AI routine. BuildCommand
        /// covers the en-route phase (BuildOrder only appears once construction
        /// starts at the site) — missing it was the "AI places foundations but
        /// never builds them" bug: AssignIdleMiners stole the walking builder
        /// back to mining every think tick.
        /// </summary>
        private static bool IsCommittedWorker(EntityManager em, Entity worker)
        {
            return em.HasComponent<BuildCommand>(worker)
                || em.HasComponent<BuildOrder>(worker)
                || em.HasComponent<RepairOrder>(worker);
        }
        /// <summary>First faction building of the tag type that can accept a
        /// research order right now: completed, carries a ResearchQueueItem
        /// buffer, and its combined production queue has room.</summary>
        private static Entity FindResearchHost<TTag>(EntityManager em, Faction faction)
            where TTag : unmanaged, IComponentData
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<TTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<ResearchQueueItem>());
            using var ents = query.ToEntityArray(Allocator.Temp);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                if (TheWaningBorder.Core.Commands.CommandRouter.IsProductionQueueFull(em, ents[i])) continue;
                return ents[i];
            }
            return Entity.Null;
        }
        /// <summary>
        /// Count military units of <paramref name="faction"/>: combat-class
        /// UnitTag, battalion leader OR loose unit (skip members so a 4-man
        /// battalion still counts as 1 toward DesiredMilitary, matching the
        /// "1 Train step = 1 entry" bookkeeping).
        /// </summary>
        private static int CountAliveMilitary(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            int n = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;

                // FREE BODIES DO NOT COUNT AS AN ARMY. Conscripted Feraldis
                // Workers and Raider-Camp Plunderers are both combat-class
                // UnitTags, so they satisfied this floor and the AI stopped
                // training real soldiers entirely: the 2026-08-06 match had a
                // Feraldis AI finish on 18,629 supplies, 5,499 veilstone and
                // military 0, having trained ZERO units in 32 minutes while
                // recycling conscripted workers 38 times.
                //
                // They are still real fighters on the map — they just must not
                // be mistaken for the standing army the floor is sizing.
                if (em.HasComponent<ConscriptedTag>(ents[i])) continue;
                if (em.HasComponent<PlundererTag>(ents[i])) continue;
                // THE THIRD FREE BODY, missed when the other two were fixed.
                // Feraldis Houses spawn Raiders on construction AND on every
                // upgrade (BuildingConstructionSystem / BuildingUpgradeSystem
                // → FeraldisRaider.CreateUncontrolled). They are combat-class
                // UnitTags that nobody trained and nobody can command
                // (NotControllableTag), and they were satisfying this floor.
                //
                // Measured 2026-08-07, 46-minute match: the Feraldis AI
                // trained 7 Workers, 1 Scout and 10 Iconoclasts — and ZERO
                // combat units — while logging "floor blocked" exactly ONCE.
                // It was never blocked; the House raiders kept telling it the
                // army was already big enough. Its military went 5 → 6 → 4 →
                // 4 → 0 while the Alanthor AI on the same map climbed to 25.
                //
                // A raider is a real fighter on the map. It is just not the
                // standing army this floor is sizing, and it cannot be sent
                // anywhere, so it must not suppress recruitment.
                if (em.HasComponent<FeraldisRaiderTag>(ents[i])) continue;
                // Nor is the culture ritualist a soldier: counting the
                // Scholar/Acolyte/Corruptor here would let a single 300-supply
                // caster satisfy part of the army floor.
                if (IsVerbUnit(em, ents[i])) continue;
                n++;
            }
            return n;
        }

        /// <summary>
        /// Living workers. Counts CanBuild, NOT MinerTag: Feraldis Workers
        /// have the mining half stripped at age-up, so a MinerTag count read
        /// zero for them forever and the worker floor retrained endlessly —
        /// the 2026-08-05 match ended with a yard full of idle Feraldis
        /// workers and no army.
        /// </summary>
        private static int CountAliveMiners(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                // A conscripted Feraldis Worker is a soldier now, not a
                // builder — counting it kept the floor "satisfied" by troops
                // out on the map, so a faction that sent everyone to war
                // never rebuilt its build crew. Excluding them makes the
                // floor maintain exactly WorkerFloorFor() real builders.
                if (em.HasComponent<ConscriptedTag>(ents[i])) continue;
                n++;
            }
            return n;
        }
        /// <summary>
        /// Count items in this faction's training queues that match either the
        /// combat-class predicate or the miner predicate. Either flag may be
        /// set; both unset returns 0. Avoids walking the queues twice for
        /// callers that need both counts.
        /// </summary>
        private static int CountQueuedByPredicate(
            EntityManager em, Faction faction, bool isCombat = false, bool isMiner = false)
        {
            if (!isCombat && !isMiner) return 0;

            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<TrainQueueItem>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            int n = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var buffer = em.GetBuffer<TrainQueueItem>(ents[i]);
                for (int j = 0; j < buffer.Length; j++)
                {
                    string id = buffer[j].UnitId.ToString();
                    UnitClass cls = UnitFactory.GetUnitClass(id);
                    if (isCombat && IsCombatClass(cls)) n++;
                    // Worker (formerly Builder + Miner) is UnitClass.Economy
                    // since the merge but still counts as a miner slot —
                    // every Worker carries MinerTag and can auto-find a
                    // deposit. Without this branch the AI would chase
                    // miners forever after training the unified unit.
                    else if (isMiner && (cls == UnitClass.Miner || cls == UnitClass.Economy)) n++;
                }
            }
            return n;
        }
        private static bool IsCombatClass(UnitClass c)
        {
            return c == UnitClass.Melee || c == UnitClass.Ranged
                || c == UnitClass.Siege || c == UnitClass.Magic;
        }

        /// <summary>
        /// RITUALISTS ARE NOT ARMY. The three culture verbs (Alanthor purify /
        /// Runai pacify / Feraldis destroy) are carried by units that are all
        /// combat-class UnitTags — Scholar and Acolyte are UnitClass.Magic,
        /// the Iconoclast/Corruptor is UnitClass.Melee — so every draft site in
        /// this file happily swept them into attack waves, and
        /// AttackMoveCommandHelper.Execute → CommandHelper.ClearAllCommands
        /// stripped the verb command off them.
        ///
        /// The channel times are 35 s (purify), 45 s (pacify) and 40 s
        /// (corrupt); ReinforceActiveWave re-commands every 10 s. That made
        /// well domination ARITHMETICALLY UNREACHABLE for an AI: the
        /// 2026-08-07 match logged 128 Corruptor dispatches at the same well
        /// over 22 minutes without a single one ever landing, because the wave
        /// sweep stole the unit before it could finish channelling.
        ///
        /// AIFeraldisEndgameSystem.CommitArmy already had this guard
        /// (`if (em.HasComponent&lt;CorruptorTag&gt;(u)) continue;`) — SimpleAISystem
        /// runs underneath it and never did.
        ///
        /// Covers both the unit identity (tags, so an idle ritualist walking
        /// home is still never drafted) and an in-flight order/channel on any
        /// unit, so future verb carriers are protected by the second half even
        /// if someone forgets to add their tag here.
        /// </summary>
        private static bool IsVerbUnit(EntityManager em, Entity e)
        {
            return em.HasComponent<ScholarTag>(e)
                || em.HasComponent<AcolyteTag>(e)
                || em.HasComponent<CorruptorTag>(e)
                || em.HasComponent<RitualState>(e)
                || em.HasComponent<PurifyCommand>(e)
                || em.HasComponent<ConvertNodeCommand>(e)
                || em.HasComponent<CorruptCommand>(e);
        }
        /// <summary>Living scouts of this faction (vision pipeline health check).</summary>
        private static int CountScouts(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var tags = q.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < tags.Length; i++)
                if (facs[i].Value == faction && tags[i].Class == UnitClass.Scout) n++;
            return n;
        }
        // ─────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────

        private static Entity FindFactionBuilding<TTag>(EntityManager em, Faction faction)
            where TTag : unmanaged, IComponentData
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<TTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                // Skip buildings still under construction unless caller checks itself.
                return entities[i];
            }
            return Entity.Null;
        }

        private static bool FactionHasChoiceBuilding(EntityManager em, Faction faction)
        {
            // Choice buildings carry ChoiceBuildingTag (set by BuildingFactory for
            // ShrineOfRidan / VaultOfAlmierra / FiendstoneKeep). The AI age-up
            // gate must require a COMPLETED choice building — the canonical
            // helper that excludes UnderConstruction is
            // GetCompletedFactionChoiceBuilding. (Player + AI gates were both
            // counting under-construction choice buildings before the fix.)
            var existing = BuildingFactory.GetCompletedFactionChoiceBuilding(em, faction);
            if (existing != null) return true;

            // Also accept a completed TempleOfRidan even though it isn't a
            // "choice" building per ChoiceBuildingIds.
            Entity temple = FindFactionBuilding<TempleTag>(em, faction);
            return temple != Entity.Null && !em.HasComponent<UnderConstruction>(temple);
        }

        private static Entity FindBrainEntity(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<AIBrain>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value == faction) return entities[i];
            }
            return Entity.Null;
        }

        private static Cost ToCost(CostBlock block)
        {
            if (block == null) return default;
            return new Cost
            {
                Supplies  = block.Supplies,
                Iron      = block.Iron,
                Veilstone   = block.Veilstone,
                Veilsteel = block.Veilsteel,
            };
        }
    }
}
