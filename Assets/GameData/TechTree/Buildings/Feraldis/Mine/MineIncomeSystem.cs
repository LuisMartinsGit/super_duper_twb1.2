// The Mine's yield. Canon: docs/Design/Age_0.md § Mine.
//
// A Mine works EVERY iron and veilstone node inside MineConstants.PatchRadius
// with no workers, and — the whole point — WITHOUT DEPLETING THEM. Nothing
// here touches IronDepositState.RemainingIron or the veilstone node state;
// the yield is conjured per standing node per second.
//
// That is the trade the design makes explicit: hand-mining is fast and
// finite, a Mine is slow and permanent. Per-node rates are deliberately well
// under a worker's, so a Mine only wins over a long game or where you cannot
// spare the workers at all (which for Feraldis is always — its Workers
// cannot gather).
//
// Node counts are rescanned on a slow timer rather than every tick: nodes
// get destroyed, and veilstone outcroppings precipitate in and out over a
// match, so the set genuinely changes — just not fast enough to care about
// between rescans.

using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Economy
{
    public static class MineConstants
    {
        /// <summary>How far from the Mine a node counts as "in the patch".</summary>
        public const float PatchRadius = 18f;

        /// <summary>Iron per second per worked node.</summary>
        public const float IronPerNodePerSecond = 0.25f;

        /// <summary>Veilstone per second per worked node.</summary>
        public const float VeilstonePerNodePerSecond = 0.15f;

        /// <summary>Payout cadence.</summary>
        public const float TickInterval = 1f;

        /// <summary>Node-set rescan cadence.</summary>
        public const float RescanInterval = 5f;

        /// <summary>Nodes a single Mine can work. Stops one Mine dropped in
        /// the middle of a huge field from paying out unboundedly.</summary>
        public const int MaxWorkedNodes = 8;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class MineIncomeSystem : SystemBase
    {
        private static readonly ComponentType[] IronTypes =
        {
            ComponentType.ReadOnly<IronMineTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static readonly ComponentType[] VeilTypes =
        {
            ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };

        private CachedEntityQuery _ironQuery;
        private CachedEntityQuery _veilQuery;

        protected override void OnCreate()
        {
            RequireForUpdate<MineTag>();
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = EntityManager;

            using var ironXfs = _ironQuery.Get(em, IronTypes)
                .ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var veilXfs = _veilQuery.Get(em, VeilTypes)
                .ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float r2 = MineConstants.PatchRadius * MineConstants.PatchRadius;

            foreach (var (mine, transform, faction) in SystemAPI
                .Query<RefRW<MineState>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<MineTag>()
                .WithNone<UnderConstruction>())
            {
                ref var ms = ref mine.ValueRW;
                var p = transform.ValueRO.Position;

                ms.RescanTimer -= dt;
                if (ms.RescanTimer <= 0f)
                {
                    ms.RescanTimer = MineConstants.RescanInterval;
                    ms.IronNodes = CountInRange(ironXfs, p.x, p.z, r2);
                    ms.VeilstoneNodes = CountInRange(veilXfs, p.x, p.z, r2);

                    // Say exactly what this Mine can see, once. A Mine that
                    // reaches here is BUILT (the query excludes
                    // UnderConstruction), so this line appearing at all rules
                    // out "never finished"; the counts then say whether it is
                    // standing on ore or on nothing.
                    // AILogger, NOT TWBLog: TWBLog.Log is
                    // [Conditional("TWB_VERBOSE")] and compiles out of normal
                    // builds entirely, so a diagnostic written with it would
                    // never have appeared. This lands in the per-faction
                    // Logs/AI_<Faction>.log that the match post-mortems read.
                    if (ms.Reported == 0)
                    {
                        ms.Reported = 1;
                        TheWaningBorder.AI.AILogger.Log(faction.ValueRO.Value, "ECONOMY",
                            $"Mine ONLINE at ({p.x:0},{p.z:0}): {ms.IronNodes} iron + " +
                            $"{ms.VeilstoneNodes} veilstone node(s) within {MineConstants.PatchRadius:0}m " +
                            $"(world has {ironXfs.Length} iron / {veilXfs.Length} veilstone nodes)" +
                            (ms.IronNodes == 0 && ms.VeilstoneNodes == 0
                                ? "  <-- NO NODES IN RANGE, THIS MINE WILL NEVER PRODUCE"
                                : ""));
                    }

                    // Cap the TOTAL worked nodes, iron first.
                    if (ms.IronNodes > MineConstants.MaxWorkedNodes)
                        ms.IronNodes = MineConstants.MaxWorkedNodes;
                    int room = MineConstants.MaxWorkedNodes - ms.IronNodes;
                    if (ms.VeilstoneNodes > room) ms.VeilstoneNodes = room;
                }

                ms.TickTimer -= dt;
                if (ms.TickTimer > 0f) continue;
                ms.TickTimer = MineConstants.TickInterval;

                if (ms.IronNodes <= 0 && ms.VeilstoneNodes <= 0) continue;

                ms.IronPurse += ms.IronNodes
                    * MineConstants.IronPerNodePerSecond * MineConstants.TickInterval;
                ms.VeilstonePurse += ms.VeilstoneNodes
                    * MineConstants.VeilstonePerNodePerSecond * MineConstants.TickInterval;

                int iron = (int)ms.IronPurse;
                int veilstone = (int)ms.VeilstonePurse;
                if (iron <= 0 && veilstone <= 0) continue;

                ms.IronPurse -= iron;
                ms.VeilstonePurse -= veilstone;

                // NOTE: nothing is deducted from the nodes. That is the
                // feature, not an oversight.
                FactionEconomy.Add(em, faction.ValueRO.Value, new Cost
                {
                    Iron = iron,
                    Veilstone = veilstone,
                });
            }
        }

        private static int CountInRange(NativeArray<LocalTransform> xfs, float x, float z, float r2)
        {
            int n = 0;
            for (int i = 0; i < xfs.Length; i++)
            {
                float dx = xfs[i].Position.x - x;
                float dz = xfs[i].Position.z - z;
                if (dx * dx + dz * dz <= r2) n++;
            }
            return n;
        }
    }
}
