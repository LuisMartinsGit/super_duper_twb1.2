// AIFeraldisEndgameSystem.Helpers.cs
// Queue guards, budgeted placement, spot search and builder dispatch.
// Partial of AIFeraldisEndgameSystem.cs -- split 2026-08-12 for readability.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    public partial struct AIFeraldisEndgameSystem : ISystem
    {
        private static bool IsUnitQueued(EntityManager em, Faction faction, string unitId)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (!em.HasBuffer<TrainQueueItem>(ents[i])) continue;
                var buf = em.GetBuffer<TrainQueueItem>(ents[i]);
                for (int j = 0; j < buf.Length; j++)
                    if (buf[j].UnitId.ToString() == unitId) return true;
            }
            return false;
        }

        private static void TryQueueAtTemple(EntityManager em, Faction faction, string unitId)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<TempleOfRidanTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (!CommandRouter.CanTrainAtBuilding(em, ents[i], unitId, out _, out _)) return;
                CommandRouter.IssueTrain(em, ents[i], unitId, CommandSource.AI);
                AILogger.Log(faction, "MILITARY", $"{unitId} queued at Temple");
                return;
            }
        }

        /// <summary>
        /// Place a building and get builders onto it.
        ///
        /// CommandRouter.IssuePlaceBuilding has an inverted-looking contract
        /// and getting it wrong is silent: it returns TRUE when the placement
        /// was QUEUED FOR LOCKSTEP (nothing exists locally yet, `building` is
        /// Null) and FALSE when it was CREATED IMMEDIATELY in single player
        /// (`building` is the real entity). An earlier version of this method
        /// treated false as failure and returned — so in single player every
        /// Mine, Totem, Thrower Camp and Pasture WAS created and then
        /// instantly abandoned with no builders, sitting at 1 HP under
        /// construction forever. That is why three matches in a row showed a
        /// Feraldis AI with zero iron and no military buildings.
        ///
        /// The cost is charged inside PlaceBuildingDirect on every peer
        /// (docs/Multiplayer_LAN_Readiness.md) — the caller only CHECKS
        /// affordability, then REFUNDS if no builder is available (the
        /// single-player branch), or the AI silently leaks its bank into
        /// foundations nobody will ever finish.
        /// </summary>
        private static void TryPlace(EntityManager em, Faction faction, string buildingId,
            float3 anchor, float rmin, float rmax, AIBudgetCategory cat)
        {
            if (!BuildCosts.TryGet(buildingId, out var cost)) return;
            if (!AIBudget.CanSpend(faction, cat, cost)) return;
            if (!FactionEconomy.CanAfford(em, faction, cost)) return;

            var size = BuildingSizeConfig.GetSize(buildingId);
            if (!TryFindSpot(em, anchor, size, rmin, rmax, out float3 pos))
            {
                AILogger.Log(faction, "BUILDING", $"{buildingId}: no valid spot {rmin:0}-{rmax:0}m from anchor");
                return;
            }

            // No AI-side Spend: PlaceBuildingDirect charges the cost on
            // every peer (docs/Multiplayer_LAN_Readiness.md). The budget
            // record stays — AIBudget is advisory bookkeeping, not the bank.
            AIBudget.RecordSpend(faction, cat, cost);

            bool queued = CommandRouter.IssuePlaceBuilding(em, buildingId, pos, faction,
                out Entity building, CommandSource.AI);
            if (queued)
            {
                // Lockstep will build it; send builders at the position.
                DispatchBuilders(em, faction, Entity.Null, buildingId, pos);
                AILogger.Log(faction, "BUILDING", $"{buildingId} queued at ({pos.x:0},{pos.z:0})");
                return;
            }

            if (building == Entity.Null)
            {
                // The executor rejected — nothing spent, nothing to refund.
                return;
            }

            int dispatched = DispatchBuilders(em, faction, building, buildingId, pos);
            if (dispatched == 0)
            {
                // Nobody to build it — undo rather than leave a permanent
                // 1 HP foundation blocking the count check forever.
                FactionEconomy.Add(em, faction, cost);
                em.DestroyEntity(building);
                AILogger.Log(faction, "BUILDING", $"{buildingId}: no idle builder, cancelled");
                return;
            }
            AILogger.Log(faction, "BUILDING", $"{buildingId} placed at ({pos.x:0},{pos.z:0})");
        }

        /// <summary>Ring scan for a legal build spot. Tuning (12 samples,
        /// 6 m steps, fixed start angle) is Feraldis's; the algorithm is
        /// shared with Alanthor in AIEndgameCommon.</summary>
        private static bool TryFindSpot(EntityManager em, float3 anchor, int2 size,
            float rmin, float rmax, out float3 pos)
            => AIEndgameCommon.TryFindBuildSpotRing(em, anchor, size, rmin, rmax,
                angleSamples: 12, radiusStep: 6f, seededStart: false, out pos);

        /// <summary>Send up to two builders. Returns how many were sent so
        /// the caller can refund a placement nobody can finish.</summary>
        private static int DispatchBuilders(EntityManager em, Faction faction,
            Entity site, string buildingId, float3 sitePos)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            int sent = 0;
            for (int i = 0; i < ents.Length && sent < 2; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                CommandRouter.IssueBuild(em, ents[i], site, buildingId, sitePos, CommandSource.AI);
                sent++;
            }
            return sent;
        }
    }
}
