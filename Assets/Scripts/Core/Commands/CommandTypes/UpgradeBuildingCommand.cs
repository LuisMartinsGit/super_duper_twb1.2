// Issues a building upgrade. Validates the request, captures base stats the
// first time the building is upgraded, deducts cost, and ENQUEUES the level-up
// on the building's production queue — the same queue its research sits in
// (ProductionQueueComponents).
//
// It used to stamp BuildingUpgrading directly, which is why an upgrade could
// not be lined up behind a research and why the two were displayed apart. The
// component still exists and still gates training and shooting, but
// ProductionQueueSystem adds it when the item reaches the head, not this file.
//
// One level-up in the queue at a time. Two would have to be priced before the
// first had applied, so the second's cost would be guesswork; refusing the
// click keeps AlreadyUpgrading meaning exactly what it always meant.

using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Core.Commands.Types
{
    public static class UpgradeBuildingCommandHelper
    {

        #region Cached queries

        // CreateEntityQuery registers a new query with the world on every call
        // and this one was never disposed. See Core/CachedEntityQuery.cs.
        static readonly ComponentType[] HallProgressTypes =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<FactionProgress>(),
        };
        static CachedEntityQuery _hallProgressQuery;

        #endregion

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
            // Queued OR running: both are "already upgrading" to the player.
            if (IsUpgradeQueued(em, building)) return UpgradeBuildingResult.AlreadyUpgrading;
            // Research and level-ups share the 16-slot production cap.
            if (CommandRouter.IsProductionQueueFull(em, building))
                return UpgradeBuildingResult.QueueFull;

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

            // Cost lookup + affordability CHECK only — the SPEND lives in
            // ApplyDirect, the path every peer executes. Spending here (on
            // the issuing peer alone) forked the faction banks, which feed
            // the desync checksum (docs/Multiplayer_LAN_Readiness.md).
            if (!BuildingUpgradeConfig.TryGetCost(buildingId, targetLevel, out var cost))
                return UpgradeBuildingResult.NotUpgradeable;
            if (!FactionEconomy.CanAfford(em, faction, cost)) return UpgradeBuildingResult.CannotAfford;

            // Validation stays on the issuing peer; the spend + state
            // mutation route through CommandRouter so both land on every
            // peer in multiplayer. Without this the upgrade existed on one
            // peer and the remote silently dropped level-gated Train
            // commands.
            CommandRouter.IssueBuildingUpgrade(em, building, source);
            return UpgradeBuildingResult.Ok;
        }

        /// <summary>
        /// Apply the upgrade state mutation on THIS peer: capture base stats
        /// once, stamp the in-progress timer. The target level is recomputed
        /// from local state so every peer derives the same result from the
        /// same command stream. Re-entry safe: no-ops when already upgrading
        /// or at max level. Validates affordability again and SPENDS here —
        /// this is the executor every peer runs (single-player direct path
        /// and lockstep replay alike), so the debit stays identical on all
        /// banks and the desync checksum holds
        /// (docs/Multiplayer_LAN_Readiness.md). A short bank rejects the
        /// whole command everywhere: no stats captured, no timer stamped.
        /// </summary>
        public static void ApplyDirect(EntityManager em, Entity building)
        {
            if (!em.Exists(building)) return;

            // UPGRADEABLE DOES NOT IMPLY RESEARCH-CAPABLE. The Royal Stable,
            // the Watch Tower and the Siege Yard have no research list, so
            // their factories never gave them a queue — refusing the upgrade
            // for want of a buffer would have killed level-ups on exactly
            // those three. Give them one on demand instead: "upgradeable
            // implies queueable" is then true by construction, and a future
            // upgradeable building cannot regress it the same way. Every peer
            // runs this executor, so the structural change is identical
            // everywhere.
            if (!em.HasBuffer<ProductionQueueItem>(building))
                em.AddBuffer<ProductionQueueItem>(building);
            if (!em.HasComponent<ProductionState>(building))
                em.AddComponentData(building, default(ProductionState));

            if (IsUpgradeQueued(em, building)) return;
            if (CommandRouter.IsProductionQueueFull(em, building)) return;

            byte currentLevel = 0;
            if (em.HasComponent<BuildingUpgradeState>(building))
                currentLevel = em.GetComponentData<BuildingUpgradeState>(building).Level;
            if (currentLevel >= BuildingUpgradeConfig.MaxLevel) return;
            byte targetLevel = (byte)(currentLevel + 1);

            // Cost lookup + SPEND — after the re-entry guards above so a
            // duplicate command can never double-charge.
            string buildingId = ResolveBuildingId(em, building);
            if (string.IsNullOrEmpty(buildingId)) return;
            if (!BuildingUpgradeConfig.TryGetCost(buildingId, targetLevel, out var cost)) return;
            if (!em.HasComponent<FactionTag>(building)) return;
            var faction = em.GetComponentData<FactionTag>(building).Value;
            if (!FactionEconomy.Spend(em, faction, cost)) return;

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

            // Enqueue. The DURATION is worked out when the item reaches the
            // head (ProductionQueueSystem), not here — an item that waits
            // behind a research should still take whatever an upgrade takes at
            // the moment it starts. Level is recorded so the refund matches
            // this charge exactly.
            em.GetBuffer<ProductionQueueItem>(building).Add(new ProductionQueueItem
            {
                Kind  = ProductionKind.BuildingUpgrade,
                Id    = default,
                Level = targetLevel,
            });
        }

        /// <summary>
        /// Is a level-up already queued or running on this building? One at a
        /// time — see the file header.
        /// </summary>
        public static bool IsUpgradeQueued(EntityManager em, Entity building)
        {
            if (em.HasComponent<BuildingUpgrading>(building)) return true;
            if (!em.HasBuffer<ProductionQueueItem>(building)) return false;
            var q = em.GetBuffer<ProductionQueueItem>(building);
            for (int i = 0; i < q.Length; i++)
                if (q[i].Kind == ProductionKind.BuildingUpgrade) return true;
            return false;
        }

        /// <summary>
        /// Hand back what a queued level-up was charged. Used when the item is
        /// cancelled, and when it reaches the head to find the level already
        /// applied from elsewhere (an age-up auto-level, a scenario
        /// promotion) — <paramref name="pricedLevel"/> is the level the item
        /// recorded, so the refund is the exact figure that was taken.
        /// </summary>
        public static void RefundQueued(EntityManager em, Entity building, byte pricedLevel)
        {
            if (!em.Exists(building) || !em.HasComponent<FactionTag>(building)) return;
            string id = ResolveBuildingId(em, building);
            if (string.IsNullOrEmpty(id)) return;
            if (!BuildingUpgradeConfig.TryGetCost(id, pricedLevel, out var cost)) return;
            FactionEconomy.Add(em, em.GetComponentData<FactionTag>(building).Value, cost);
        }

        // ──────────────────────────────────────────────────────────────────
        // HELPERS
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Map building entity -> upgrade-system-known id ("Hall" / "Barracks"
        /// / "Hut"). Uses the marker tag components rather than presentation
        /// id so the lookup keeps working through any future re-skinning.
        /// </summary>
        public static string ResolveBuildingId(EntityManager em, Entity e)
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
            var query = _hallProgressQuery.Get(em, HallProgressTypes);
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
