// File: Assets/Scripts/Core/Commands/CommandTypes/UpgradeBuildingCommand.cs
// Issues a building upgrade. Validates the request, captures base stats
// the first time the building is upgraded, deducts cost, and stamps
// BuildingUpgrading. The actual stat application happens later, when
// BuildingUpgradeSystem ticks the timer down to zero — same pattern as
// UnderConstruction → BuildingConstructionSystem.

using Unity.Entities;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Core.Commands.Types
{
    public static class UpgradeBuildingCommandHelper
    {
        /// <summary>
        /// Try to start an upgrade on <paramref name="building"/>. Returns a
        /// result code so callers (UI, AI) can show the appropriate
        /// feedback. Captures base stats on first call so the upgrade
        /// system can recompute idempotently.
        /// </summary>
        public static UpgradeBuildingResult Execute(EntityManager em, Entity building,
            CommandSource source = CommandSource.LocalPlayer)
        {
            if (!em.Exists(building)) return UpgradeBuildingResult.NotUpgradeable;
            if (!em.HasComponent<BuildingUpgradeable>(building)) return UpgradeBuildingResult.NotUpgradeable;
            if (em.HasComponent<UnderConstruction>(building)) return UpgradeBuildingResult.UnderConstruction;
            if (em.HasComponent<BuildingUpgrading>(building)) return UpgradeBuildingResult.AlreadyUpgrading;

            // Identify the building type via PresentationId — same lookup the
            // factory uses, so we don't need a parallel string registry.
            string buildingId = ResolveBuildingId(em, building);
            if (string.IsNullOrEmpty(buildingId)) return UpgradeBuildingResult.NotUpgradeable;

            // Determine current level (default 0 if no state component yet).
            byte currentLevel = 0;
            if (em.HasComponent<BuildingUpgradeState>(building))
                currentLevel = em.GetComponentData<BuildingUpgradeState>(building).Level;

            if (currentLevel >= BuildingUpgradeConfig.MaxLevel)
                return UpgradeBuildingResult.AlreadyMaxLevel;

            // Owner culture: read from the Hall's FactionProgress (any culture
            // unlocks upgrades for that faction's buildings — even non-Hall ones).
            if (!em.HasComponent<FactionTag>(building)) return UpgradeBuildingResult.NoCulture;
            var faction = em.GetComponentData<FactionTag>(building).Value;
            if (!FactionHasCulture(em, faction)) return UpgradeBuildingResult.NoCulture;

            byte targetLevel = (byte)(currentLevel + 1);

            // Cost lookup + spend.
            if (!BuildingUpgradeConfig.TryGetCost(buildingId, targetLevel, out var cost))
                return UpgradeBuildingResult.NotUpgradeable;
            if (!FactionEconomy.CanAfford(em, faction, cost)) return UpgradeBuildingResult.CannotAfford;
            if (!FactionEconomy.Spend(em, faction, cost)) return UpgradeBuildingResult.CannotAfford;

            // Validation + spend stay on the issuing peer; the state
            // mutation routes through CommandRouter so it replicates in
            // multiplayer. Without this the upgrade existed on one peer and
            // the remote silently dropped level-gated Train commands.
            CommandRouter.IssueBuildingUpgrade(em, building, source);
            return UpgradeBuildingResult.Ok;
        }

        /// <summary>
        /// Apply the upgrade state mutation on THIS peer: capture base stats
        /// once, stamp the in-progress timer. The target level is recomputed
        /// from local state so every peer derives the same result from the
        /// same command stream. Re-entry safe: no-ops when already upgrading
        /// or at max level.
        /// </summary>
        public static void ApplyDirect(EntityManager em, Entity building)
        {
            if (!em.Exists(building)) return;
            if (em.HasComponent<BuildingUpgrading>(building)) return;

            byte currentLevel = 0;
            if (em.HasComponent<BuildingUpgradeState>(building))
                currentLevel = em.GetComponentData<BuildingUpgradeState>(building).Level;
            if (currentLevel >= BuildingUpgradeConfig.MaxLevel) return;
            byte targetLevel = (byte)(currentLevel + 1);

            // Capture base stats once. After this they NEVER change — the
            // upgrade system always recomputes scaled values from base, so
            // re-applying a level (save/load, frame race) can't double-bump.
            if (!em.HasComponent<BuildingUpgradeState>(building))
            {
                int baseHp = em.HasComponent<Health>(building)
                    ? em.GetComponentData<Health>(building).Max : 0;
                float baseAtkCd = em.HasComponent<BuildingRangedAttack>(building)
                    ? em.GetComponentData<BuildingRangedAttack>(building).Cooldown : 0f;
                int basePop = em.HasComponent<PopulationProvider>(building)
                    ? em.GetComponentData<PopulationProvider>(building).Amount : 0;

                em.AddComponentData(building, new BuildingUpgradeState
                {
                    Level                  = 0,
                    BaseHpMax              = baseHp,
                    BaseAttackCooldown     = baseAtkCd,
                    BasePopulationProvider = basePop,
                });
            }

            // Stamp the in-progress timer. Duration is per-building where
            // the calculator specifies one (falls back to the global curve).
            em.AddComponentData(building, new BuildingUpgrading
            {
                Progress    = 0f,
                Total       = BuildingUpgradeConfig.GetUpgradeDuration(
                    ResolveBuildingId(em, building), targetLevel),
                TargetLevel = targetLevel,
            });
        }

        // ──────────────────────────────────────────────────────────────────
        // HELPERS
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Map building entity -> upgrade-system-known id ("Hall" / "Barracks"
        /// / "Hut"). Uses the marker tag components rather than presentation
        /// id so the lookup keeps working through any future re-skinning.
        /// </summary>
        private static string ResolveBuildingId(EntityManager em, Entity e)
        {
            if (em.HasComponent<HallTag>(e))         return "Hall";
            if (em.HasComponent<BarracksTag>(e))     return "Barracks";
            if (em.HasComponent<ArcheryRangeTag>(e)) return "ArcheryRange";
            if (em.HasComponent<HutTag>(e))          return "Hut";
            if (em.HasComponent<GathererHutTag>(e))  return "GatherersHut";
            // Choice-building simple upgrades (design 2026-07-04). The Keep is
            // NOT here — it levels via wings (KeepWingSystem), not this ladder.
            if (em.HasComponent<VaultTag>(e))        return "VaultOfAlmierra";
            if (em.HasComponent<ShrineTag>(e))       return "ShrineOfRidan";
            // Alanthor culture ladders (calculator 2026-08).
            if (em.HasComponent<RoyalStableTag>(e))  return "Alanthor_RoyalStable";
            if (em.HasComponent<WatchTowerTag>(e))   return "Alanthor_Tower";
            if (em.HasComponent<SiegeYardTag>(e))    return "Alanthor_SiegeYard";
            if (em.HasComponent<SmelterTag>(e))      return "Alanthor_Smelter";
            return string.Empty;
        }

        /// <summary>
        /// Faction has picked a culture iff its Hall carries FactionProgress.Culture
        /// other than Cultures.None. Reading from the Hall avoids a separate
        /// per-faction lookup table.
        /// </summary>
        private static bool FactionHasCulture(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>());
            using var ents = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (em.GetComponentData<FactionProgress>(ents[i]).Culture != Cultures.None) return true;
            }
            return false;
        }

        /// <summary>
        /// Return the next level's cost (or default if no further levels).
        /// UI uses this to render "Upgrade — 200s 50i 15c" labels.
        /// </summary>
        public static bool TryGetNextCost(EntityManager em, Entity building, out Cost cost, out byte nextLevel)
        {
            cost = default;
            nextLevel = 0;
            if (!em.Exists(building) || !em.HasComponent<BuildingUpgradeable>(building)) return false;

            byte current = em.HasComponent<BuildingUpgradeState>(building)
                ? em.GetComponentData<BuildingUpgradeState>(building).Level : (byte)0;
            if (current >= BuildingUpgradeConfig.MaxLevel) return false;

            nextLevel = (byte)(current + 1);
            string id = ResolveBuildingId(em, building);
            return BuildingUpgradeConfig.TryGetCost(id, nextLevel, out cost);
        }
    }
}
