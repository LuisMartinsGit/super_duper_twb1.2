// CaravanLootSystem.cs
// A Runai wagon killed by an enemy gives its cargo to the faction that killed
// it (docs/Design/Age_1_Runai.md §3.1, 2026-10-14) — the reward for raiding
// a trade road. Shown as a floating "+N" where the wagon fell.
//
// Same death hook as FeraldisRaidIncomeSystem: runs before DeathSystem and
// skips corpses already in their death animation, so each wagon pays once.
// A wagon its owner deleted pays nothing (DeleteDirect clears the last damager).

using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Runai
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProjectileSystem))]
    [UpdateAfter(typeof(MeleeCombatSystem))]
    [UpdateAfter(typeof(RangedCombatSystem))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class CaravanLootSystem : SystemBase
    {
        protected override void OnCreate()
        {
            base.OnCreate();
            // Runai ships only in edition C (docs/Design/Alpha_Build.md §4). Off
            // elsewhere: its configs are stripped from A/B builds, and running
            // it there threw every tick, in the menu and in every match.
            if (!GameEdition.Allows(Cultures.Runai)) { Enabled = false; return; }
        }

        static readonly ComponentType[] QT_Dead =
        {
            ComponentType.ReadOnly<RunaiCaravan>(), ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LastDamagedByFaction>(), ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<DeathAnimationState>(),
        };
        static CachedEntityQuery QC_Dead;

        protected override void OnUpdate()
        {
            var em = EntityManager;
            var q = QC_Dead.Get(em, QT_Dead);
            if (q.IsEmpty) return;
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (hps[i].Value > 0) continue;
                var e = ents[i];
                if (!TransientState.Active<LastDamagedByFaction>(em, e)) continue;
                var killer = em.GetComponentData<LastDamagedByFaction>(e).Value;
                var owner = em.GetComponentData<FactionTag>(e).Value;
                if (killer == owner || killer == Faction.Border || !Alliances.AreHostile(killer, owner)) continue;

                var c = em.GetComponentData<RunaiCaravan>(e);
                var loot = new Cost
                {
                    Supplies = (int)math.floor(c.Supplies), Iron = (int)math.floor(c.Iron),
                    Veilstone = (int)math.floor(c.Veilstone), Veilsteel = (int)math.floor(c.Veilsteel),
                };
                // Paid once: the cargo leaves the wagon with the loot.
                c.Supplies = c.Iron = c.Veilstone = c.Veilsteel = 0f;
                em.SetComponentData(e, c);
                if (loot.Supplies + loot.Iron + loot.Veilstone + loot.Veilsteel <= 0) continue;
                FactionEconomy.Add(em, killer, loot, IncomeSource.Loot);
                SimSignals.Gain(em.GetComponentData<LocalTransform>(e).Position, killer, loot);
            }
        }
    }
}
