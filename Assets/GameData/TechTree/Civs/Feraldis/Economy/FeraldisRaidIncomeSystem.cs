// FeraldisRaidIncomeSystem.cs
// Raiding is the Feraldis late-game supply (docs/Design/Age_1_Feraldis.md
// §3.4-3.5, 2026-10-12):
//
//   RAZE BOUNTY  an enemy building destroyed by a Feraldis faction pays it a
//                share of what the building cost — its build price plus every
//                level its owner bought — in the same resource mix.
//   PILLAGE      an enemy unit killed by a Feraldis faction pays a share of
//                its WHOLE cost, as supplies (Alanthor soldiers cost iron and
//                veilstone, not supplies, so a share of the supply cost alone
//                would pay nothing against them).
//   BLOOD        the same kill pays religion pts (killPtsPerCost x its whole
//                cost, at least killPtsMin), 2026-10-13 — Religion.md §7.
//
// Supersedes the flat-reward PillageSystem (2026-10-12). Runs before
// DeathSystem and skips corpses already in their death animation / collapse,
// so every death pays exactly once (the task-056 F-1 bug). A building its
// owner deleted pays nothing: DeleteDirect clears the last damager.

using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace TheWaningBorder.Feraldis
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSystem))]
    [UpdateAfter(typeof(MeleeCombatSystem))]
    [UpdateAfter(typeof(RangedCombatSystem))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class FeraldisRaidIncomeSystem : SystemBase
    {
        static readonly ComponentType[] QT_Dead =
        {
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LastDamagedByFaction>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.Exclude<DeathAnimationState>(),
            ComponentType.Exclude<BuildingCollapseState>(),
        };
        static CachedEntityQuery QC_Dead;

        protected override void OnUpdate()
        {
            // Feraldis is not in every edition (docs/Design/Alpha_Build.md §4);
            // without it this system has nothing to do and no config to read.
            if (!GameEdition.Allows(Cultures.Feraldis)) return;
            var em = EntityManager;
            var q = QC_Dead.Get(em, QT_Dead);
            if (q.IsEmpty) return;
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                if (hps[i].Value > 0) continue;
                var e = ents[i];
                if (!TransientState.Active<LastDamagedByFaction>(em, e)) continue;
                var killer = em.GetComponentData<LastDamagedByFaction>(e).Value;
                var victim = facs[i].Value;
                if (killer == victim || victim == Faction.Border || killer == Faction.Border) continue;
                if (!Alliances.AreHostile(killer, victim)) continue;
                if (!FeraldisEconomy.IsFeraldis(em, killer)) continue;

                var cfg = FeraldisEconomy.Cfg;
                if (em.HasComponent<BuildingTag>(e))
                {
                    var worth = BuildingWorth(em, e, victim);
                    var pay = Scale(worth, cfg.razeBountyShare);
                    if (!pay.IsZero)
                    {
                        FactionEconomy.Add(em, killer, pay, IncomeSource.Loot);
                        SimSignals.Gain(At(em, e), killer, pay);
                    }
                }
                else if (em.HasComponent<UnitTag>(e))
                {
                    var c = FeraldisSacrifice.UnitCost(em, e);
                    int total = c.Supplies + c.Iron + c.Veilstone + c.Veilsteel;
                    int s = Mathf.FloorToInt(total * cfg.pillageShare);
                    if (s > 0)
                    {
                        FactionEconomy.Add(em, killer, Cost.Of(supplies: s), IncomeSource.Loot);
                        // Over the fallen enemy, where the raiders stand.
                        SimSignals.Gain(At(em, e), killer, Cost.Of(supplies: s));
                    }

                    // DRAWING BLOOD PAYS RELIGION (Religion.md §7, 2026-10-13):
                    // every enemy unit a Feraldis faction kills pays pts scaled
                    // by its cost, through the same escalating ladder as curse
                    // kills, with or without a Blood Altar. Curse units are
                    // paid by CurseKillReligionSystem (victim == Border is
                    // skipped above), so nothing is paid twice.
                    int pts = Mathf.Max(cfg.killPtsMin, Mathf.RoundToInt(total * cfg.killPtsPerCost));
                    FeraldisSacrifice.PayBloodPoints(em, killer, pts);
                }
            }
        }

        private static Unity.Mathematics.float3 At(EntityManager em, Entity e)
            => em.HasComponent<Unity.Transforms.LocalTransform>(e)
                ? em.GetComponentData<Unity.Transforms.LocalTransform>(e).Position
                : default;

        /// <summary>What a building cost its owner: the price paid plus every bought level (L2+; L1 is free at age-up).</summary>
        public static Cost BuildingWorth(EntityManager em, Entity building, Faction owner)
        {
            var total = default(Cost);
            string id = BuildCosts.IdFromEntity(em, building);
            if (!string.IsNullOrEmpty(id) && BuildCosts.TryGetPaid(em, building, id, out var paid)) total += paid;

            if (em.HasComponent<BuildingUpgradeState>(building))
            {
                int level = em.GetComponentData<BuildingUpgradeState>(building).Level;
                string ladder = UpgradeBuildingCommandHelper.ResolveBuildingId(em, building);
                if (!string.IsNullOrEmpty(ladder))
                    for (int l = 2; l <= level; l++)
                        if (BuildingUpgradeConfig.TryGetCost(em, owner, ladder, (byte)l, out var lc)) total += lc;
            }
            return total;
        }

        public static Cost Scale(Cost c, float share) => Cost.Of(
            supplies: Mathf.FloorToInt(c.Supplies * share),
            iron: Mathf.FloorToInt(c.Iron * share),
            veilstone: Mathf.FloorToInt(c.Veilstone * share),
            veilsteel: Mathf.FloorToInt(c.Veilsteel * share));
    }
}
