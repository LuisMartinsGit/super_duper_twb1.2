// SectWitnessVisionSystem.cs
// Implements Witness's Lv I "All-Seeing" passive (scouts +25% vision range).
//
// Scans Scout-class units that don't yet carry WitnessVisionApplied and,
// for those whose faction has Witness adopted, multiplies LineOfSight.Radius
// by the Lv I factor and stamps WitnessVisionApplied to mark them as
// already-bonused. This handles BOTH paths uniformly:
//   - Scouts existing at adoption time: stamped on the next tick.
//   - Scouts trained AFTER adoption: stamped when they enter the query.
//
// Lv II / Lv III scaling lives in Phase 4 — when the lever level upgrades,
// a diff system will apply the (newFactor - oldFactor) delta to scouts
// already stamped at the prior level. The AppliedLevel byte on the stamp
// is what makes that diff math possible.
//
// task-063 phase 2c.
//
// Location: Assets/Scripts/Systems/Sect/SectWitnessVisionSystem.cs

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SectWitnessVisionSystem : ISystem
    {
        // Per-level vision multiplier. Phase 4 reads AppliedLevel and applies
        // the diff (factorAt(new) / factorAt(old)) on lever upgrade.
        public static float MultiplierFor(byte level) => level switch
        {
            2 => 1.50f,
            3 => 1.75f,
            _ => 1.25f,
        };

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UnitTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // Pass 1: brand-new scouts that haven't received the bonus yet.
            var pendingNewStamps = new NativeList<Entity>(8, Allocator.Temp);
            var pendingNewLevels = new NativeList<byte>(8, Allocator.Temp);

            foreach (var (unit, faction, los, entity) in SystemAPI
                .Query<RefRO<UnitTag>, RefRO<FactionTag>, RefRW<LineOfSight>>()
                .WithNone<WitnessVisionApplied>()
                .WithEntityAccess())
            {
                if (unit.ValueRO.Class != UnitClass.Scout) continue;
                byte level = SectQuery.LevelOf(em, faction.ValueRO.Value,
                    SectConfig.Witness, SectLeverKind.Passive);
                if (level == 0) continue;

                los.ValueRW.Radius *= MultiplierFor(level);
                pendingNewStamps.Add(entity);
                pendingNewLevels.Add(level);
            }

            for (int i = 0; i < pendingNewStamps.Length; i++)
            {
                if (em.Exists(pendingNewStamps[i]))
                    em.AddComponentData(pendingNewStamps[i],
                        new WitnessVisionApplied { AppliedLevel = pendingNewLevels[i] });
            }
            pendingNewStamps.Dispose();
            pendingNewLevels.Dispose();

            // Pass 2: already-stamped scouts whose faction's lever level rose
            // since stamping. Apply the diff factor and bump AppliedLevel.
            // (task-063 phase 4)
            foreach (var (faction, los, applied) in SystemAPI
                .Query<RefRO<FactionTag>, RefRW<LineOfSight>, RefRW<WitnessVisionApplied>>())
            {
                byte currentLevel = SectQuery.LevelOf(em, faction.ValueRO.Value,
                    SectConfig.Witness, SectLeverKind.Passive);
                byte appliedLevel = applied.ValueRO.AppliedLevel;
                if (currentLevel <= appliedLevel) continue;

                float diffMult = MultiplierFor(currentLevel) / MultiplierFor(appliedLevel);
                los.ValueRW.Radius *= diffMult;
                applied.ValueRW.AppliedLevel = currentLevel;
            }
        }
    }
}
