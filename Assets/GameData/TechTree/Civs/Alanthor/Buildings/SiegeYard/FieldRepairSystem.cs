// FieldRepairSystem.cs — the Siege Yard's Field Repair research (decision 75,
// 2026-11-18, developer: "repairing over time"): once a faction has researched
// it, every one of its siege engines repairs itself, all the time, at the
// rate on FieldRepair.asset (effectsList: Target "passive",
// Stat RepairHpPerSecond). It had cost a level-3 research and done nothing.
//
// Lockstep: a SimCadence-paced 1 s tick, integer HP, the fraction of a point
// carried per faction-wide rate so a rate below 1 HP/s still adds up exactly.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Abilities
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FieldRepairSystem : SystemBase
    {
        public const string TechId = "FieldRepair";
        private const float TickInterval = 1f;   // cadence, not tuning

        private SimCadence.Periodic _cadence;
        private float _carry;   // the fraction of a point not yet repaired
        private EntityQuery _siege;

        protected override void OnCreate()
        {
            _siege = GetEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<Health>(),
                ComponentType.Exclude<DeathAnimationState>());
        }

        protected override void OnUpdate()
        {
            float tick = _cadence.DueStep(World.Time.DeltaTime, TickInterval);
            if (tick <= 0f) return;
            var research = FactionResearchState.Instance;
            if (research == null || _siege.IsEmptyIgnoreFilter) return;

            float rate = TechCatalog.TechEffect(TechId, "RepairHpPerSecond");
            if (rate <= 0f) return;
            float total = rate * tick + _carry;
            int heal = (int)total;
            _carry = total - heal;
            if (heal <= 0) return;

            var em = EntityManager;
            using var ents = _siege.ToEntityArray(Allocator.Temp);
            using var tags = _siege.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = _siege.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (tags[i].Class != UnitClass.Siege) continue;
                if (!research.HasResearched(facs[i].Value, TechId)) continue;
                var hp = em.GetComponentData<Health>(ents[i]);
                if (hp.Value <= 0 || hp.Value >= hp.Max) continue;
                hp.Value = math.min(hp.Max, hp.Value + heal);
                em.SetComponentData(ents[i], hp);
            }
        }
    }
}
