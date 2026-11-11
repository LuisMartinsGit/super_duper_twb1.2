// SimpleAISystem.Surplus.cs
// Where an idle bank goes once the basics cap is hard: veilstone first, then
// upgrades. Partial of SimpleAISystem.cs. docs/Design/Game_AI.md 5e is the
// design; the thresholds are AIBudget.asset (surplusSupplies / surplusIron),
// the rest SimpleAISystem.asset (surplus*) and AIBuildingUpgradeSystem.asset.
//
// ─────────────────────────────────────────────────────────────────────────
// WHY (2026-10-04, the developer's ruling)
//
// The "basics fill" let an Alanthor army train veilstone-free units past the
// basics share whenever only veilstone was short and supplies and iron piled
// up. A 60-minute headless batch turned every army into 84% Spearman/Archer
// and starved the role units. The ruling, verbatim: "The initial share on
// basic units is fixed, even if there is population for them. The plan must
// always be to grab more veilstone and, do upgrades".
//
// So the cap is hard (SimpleAISystem.Composition.cs), and the surplus it
// leaves is spent by the existing spenders, each extended where it lives
// (the Trading Outpost spenders are gone with the building, decision 55):
//   * CollectClaimCandidates /  outcrop ground weighs more, claim rounds come
//     EnsureTerritoryClaim      round faster
//   * EnsureFortressExpansion   outcrop ground weighs more; a Fortress there
//                               may use the army's veilstone earmark
//   * TickEndgameResearchSweep  faster, ladder-free, the plan's trainers
//                               first, no veilstone while held
//   * AIBuildingUpgradeSystem   several level-ups per think
// Every action logs one SURPLUS line, so a headless batch can count them.
// ─────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        /// <summary>
        /// VEILSTONE SURPLUS: an aged-up Alanthor faction whose army is short
        /// of veilstone (AIBudget.IsMilitaryShort — the composition records
        /// it every think while it saves for a role unit) while the bank
        /// overflows with supplies and iron (AIBudget.BankOverflowing).
        /// </summary>
        private static bool IsVeilstoneSurplus(EntityManager em, Faction faction)
            => FactionCultureOf(em, faction) == Cultures.Alanthor
               && FactionEra(em, faction) >= 2
               && AIBudget.VeilstoneHeldSurplus(em, faction);

        /// <summary>One SURPLUS line per action, unthrottled — each is a real
        /// purchase, so it cannot spam faster than the bank allows.</summary>
        private static void LogSurplus(Faction faction, string message)
            => AILogger.Log(faction, "SURPLUS", message);

        /// <summary>Host tiers of the research sweep (Game_AI.md 5e).</summary>
        private const int SweepHostTiers = 3;

        /// <summary>1 = trains a unit the composition plan has a share for;
        /// 2 = everything else. (Tier 0, the Trading Outpost, is retired with
        /// the building — decision 55.)</summary>
        private static int SweepHostTier(EntityManager em, Entity building, ArmyPlan plan)
        {
            if (plan == null || !plan.Active) return 2;
            string buildingId = TheWaningBorder.Entities.BuildingIds.Of(building, em);
            if (string.IsNullOrEmpty(buildingId)
                || !TechCatalog.TryGetBuilding(buildingId, out var def) || def?.trains == null)
                return 2;
            for (int i = 0; i < def.trains.Length; i++)
            {
                int r = RowIndex(plan, def.trains[i]);
                if (r >= 0 && plan.Share[r] > 0f) return 1;
            }
            return 2;
        }
    }
}
