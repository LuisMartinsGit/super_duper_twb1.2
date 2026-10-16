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
// PERF (2026-10-16): pass 1 walked every unit every tick through a
// RefRW<LineOfSight> query — a managed per-unit loop that also stamped
// EVERY unit chunk's LineOfSight as changed each tick (so nothing downstream
// could ever change-filter on vision). It is now a Burst scan that only
// collects the unstamped scouts, in the same chunk order, and the radius is
// written through the EntityManager for the few that qualify. LevelOf (a
// string lookup + bank read + a sect-building walk) is asked once per
// faction per tick, not once per scout. Same arithmetic, same order:
// result-identical.

using Unity.Burst;
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

            // LevelOf per faction, asked at most once per tick. Faction is a
            // byte; 255 = not asked yet.
            var levels = new NativeArray<byte>(256, Allocator.Temp);
            for (int i = 0; i < levels.Length; i++) levels[i] = 255;

            // Pass 1: brand-new scouts that haven't received the bonus yet.
            var scouts = new NativeList<Entity>(8, Allocator.Temp);
            var scoutFactions = new NativeList<Faction>(8, Allocator.Temp);
            new UnstampedScoutScanJob { Scouts = scouts, Factions = scoutFactions }.Run();

            for (int i = 0; i < scouts.Length; i++)
            {
                byte level = LevelFor(em, ref levels, scoutFactions[i]);
                if (level == 0) continue;

                var e = scouts[i];
                var los = em.GetComponentData<LineOfSight>(e);
                los.Radius *= MultiplierFor(level);
                em.SetComponentData(e, los);
                em.AddComponentData(e, new WitnessVisionApplied { AppliedLevel = level });
            }
            scouts.Dispose();
            scoutFactions.Dispose();

            // Pass 2: already-stamped scouts whose faction's lever level rose
            // since stamping. Apply the diff factor and bump AppliedLevel.
            // (task-063 phase 4)
            var raised = new NativeList<Entity>(4, Allocator.Temp);
            var raisedTo = new NativeList<byte>(4, Allocator.Temp);
            foreach (var (faction, applied, entity) in SystemAPI
                .Query<RefRO<FactionTag>, RefRO<WitnessVisionApplied>>()
                .WithAll<LineOfSight>()
                .WithEntityAccess())
            {
                byte currentLevel = LevelFor(em, ref levels, faction.ValueRO.Value);
                if (currentLevel <= applied.ValueRO.AppliedLevel) continue;
                raised.Add(entity);
                raisedTo.Add(currentLevel);
            }
            for (int i = 0; i < raised.Length; i++)
            {
                var e = raised[i];
                var applied = em.GetComponentData<WitnessVisionApplied>(e);
                byte currentLevel = raisedTo[i];
                float diffMult = MultiplierFor(currentLevel) / MultiplierFor(applied.AppliedLevel);
                var los = em.GetComponentData<LineOfSight>(e);
                los.Radius *= diffMult;
                em.SetComponentData(e, los);
                applied.AppliedLevel = currentLevel;
                em.SetComponentData(e, applied);
            }
            raised.Dispose();
            raisedTo.Dispose();
            levels.Dispose();
        }

        private static byte LevelFor(EntityManager em, ref NativeArray<byte> cache, Faction faction)
        {
            int f = (byte)faction;
            if (cache[f] == 255)
                cache[f] = SectQuery.LevelOf(em, faction, SectConfig.Witness, SectLeverKind.Passive);
            return cache[f];
        }

        /// <summary>Every Scout-class unit not yet stamped, in chunk order
        /// (the order the old foreach visited them).</summary>
        [BurstCompile]
        [WithNone(typeof(WitnessVisionApplied))]
        [WithAll(typeof(LineOfSight))]
        private partial struct UnstampedScoutScanJob : IJobEntity
        {
            public NativeList<Entity> Scouts;
            public NativeList<Faction> Factions;

            void Execute(Entity e, in UnitTag unit, in FactionTag faction)
            {
                if (unit.Class != UnitClass.Scout) return;
                Scouts.Add(e);
                Factions.Add(faction.Value);
            }
        }
    }
}
