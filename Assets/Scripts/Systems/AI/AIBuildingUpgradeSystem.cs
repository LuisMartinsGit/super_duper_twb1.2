// AIBuildingUpgradeSystem.cs
// Culture-agnostic AI driver for the building upgrade system.
//
// Each AI brain that's Era >= 2 with a non-None culture picks ONE
// upgradeable building per tick (slowest cadence so it doesn't dominate
// the build queue) and tries UpgradeBuildingCommandHelper.Execute on it.
// Priority: Hall first (multi-target unlock + train speed), then Barracks
// (training + arrow attack at L3), then Hut (pop scaling).
//
// Reserves a small buffer of resources before upgrading so upgrades
// don't bankrupt the AI mid-rush. Reserves are loose — if the AI
// genuinely can't afford it, the command helper rejects gracefully.
//
// Location: Assets/Scripts/AI/Managers/AIBuildingUpgradeSystem.cs

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;

namespace TheWaningBorder.AI
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SimpleAISystem))]
    public partial struct AIBuildingUpgradeSystem : ISystem
    {
        // Slow strategic loop. Upgrades take 20-45s to complete; checking
        // every 6 s is plenty of cadence and keeps query churn low.
        private const float ThinkInterval = 6f;

        // Reserve buffer the AI keeps untouched before queueing an upgrade —
        // upgrades are expensive and we don't want them to starve military /
        // research lines.
        private const int ReserveSupplies = 200;
        private const int ReserveIron     = 50;
        private const int ReserveCrystal  = 20;

        // Priority order — cheapest, highest-impact first. Hall gives the
        // largest combat + train benefits per click; Barracks gains an
        // attack at L3; Huts are pop scaling.
        private static readonly string[] PriorityOrder = { "Hall", "Barracks", "Hut" };

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AIBrain>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (GameSettings.IsMultiplayer && !GameSettings.IsHost()) return;
            float time = (float)SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;

            // Snapshot brains — we make structural changes (BuildingUpgrading
            // gets added) so we can't iterate via SystemAPI.Query.
            var brainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<AIBrain>());
            using var brainEntities = brainQuery.ToEntityArray(Allocator.Temp);

            for (int b = 0; b < brainEntities.Length; b++)
            {
                var brainEntity = brainEntities[b];
                if (!em.Exists(brainEntity)) continue;
                var brain = em.GetComponentData<AIBrain>(brainEntity);
                if (brain.IsActive == 0) continue;

                Faction faction = brain.Owner;

                // Per-brain throttle.
                if (em.HasComponent<AIBuildingUpgradeTickState>(brainEntity))
                {
                    var tick = em.GetComponentData<AIBuildingUpgradeTickState>(brainEntity);
                    if (time < tick.NextThinkTime) continue;
                    tick.NextThinkTime = time + ThinkInterval;
                    em.SetComponentData(brainEntity, tick);
                }
                else
                {
                    em.AddComponentData(brainEntity, new AIBuildingUpgradeTickState
                    {
                        NextThinkTime = time + ThinkInterval,
                    });
                    continue; // skip first tick
                }

                // Era 2+ + culture picked? UpgradeBuildingCommandHelper does
                // the same gate but rejecting at this layer cuts query cost.
                if (!FactionEconomy.TryGetBank(em, faction, out var bank)) continue;
                if (!em.HasComponent<FactionEra>(bank)) continue;
                if (em.GetComponentData<FactionEra>(bank).Value < 2) continue;
                if (!HasCulture(em, faction)) continue;

                // Reserve check — keep some resources for non-upgrade use.
                if (!FactionEconomy.TryGetResources(em, faction, out var res)) continue;
                if (res.Supplies < ReserveSupplies) continue;
                if (res.Iron     < ReserveIron)     continue;
                if (res.Crystal  < ReserveCrystal)  continue;

                // Walk the priority order; first eligible building gets the upgrade.
                TryUpgradeOne(em, faction);
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // HELPERS
        // ──────────────────────────────────────────────────────────────────

        private static bool HasCulture(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>());
            using var ents = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (em.GetComponentData<FactionProgress>(ents[i]).Culture != Cultures.None) return true;
            }
            return false;
        }

        private static void TryUpgradeOne(EntityManager em, Faction faction)
        {
            for (int p = 0; p < PriorityOrder.Length; p++)
            {
                if (TryUpgradeBuildingType(em, faction, PriorityOrder[p])) return;
            }
        }

        /// <summary>
        /// Find the LOWEST-LEVEL building of the given type owned by the
        /// faction. Lowest level = highest marginal benefit per upgrade
        /// click (uncultured Hall → L1 unlocks the multi-target chain).
        /// Returns true if the upgrade was queued.
        /// </summary>
        private static bool TryUpgradeBuildingType(EntityManager em, Faction faction, string buildingId)
        {
            EntityQuery query;
            switch (buildingId)
            {
                case "Hall":
                    query = em.CreateEntityQuery(
                        ComponentType.ReadOnly<HallTag>(),
                        ComponentType.ReadOnly<BuildingUpgradeable>(),
                        ComponentType.ReadOnly<FactionTag>());
                    break;
                case "Barracks":
                    query = em.CreateEntityQuery(
                        ComponentType.ReadOnly<BarracksTag>(),
                        ComponentType.ReadOnly<BuildingUpgradeable>(),
                        ComponentType.ReadOnly<FactionTag>());
                    break;
                case "Hut":
                    query = em.CreateEntityQuery(
                        ComponentType.ReadOnly<HutTag>(),
                        ComponentType.ReadOnly<BuildingUpgradeable>(),
                        ComponentType.ReadOnly<FactionTag>());
                    break;
                default:
                    return false;
            }

            using var ents = query.ToEntityArray(Allocator.Temp);

            Entity best = Entity.Null;
            int bestLevel = int.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                if (em.HasComponent<BuildingUpgrading>(ents[i])) continue;

                byte lvl = em.HasComponent<BuildingUpgradeState>(ents[i])
                    ? em.GetComponentData<BuildingUpgradeState>(ents[i]).Level : (byte)0;
                if (lvl >= BuildingUpgradeConfig.MaxLevel) continue;

                if (lvl < bestLevel) { bestLevel = lvl; best = ents[i]; }
            }
            if (best == Entity.Null) return false;

            var result = UpgradeBuildingCommandHelper.Execute(em, best);
            if (result == UpgradeBuildingResult.Ok)
            {
                AILogger.Log(faction, "BUILDING",
                    $"Upgrading {buildingId} to L{bestLevel + 1}");
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Per-brain tick throttle for the building upgrade loop.
    /// </summary>
    public struct AIBuildingUpgradeTickState : IComponentData
    {
        public float NextThinkTime;
    }
}
