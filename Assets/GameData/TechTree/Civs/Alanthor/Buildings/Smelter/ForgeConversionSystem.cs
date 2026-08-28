// Passive veilsteel generation from the Forge (Smelter).

using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Economy
{
    /// <summary>
    /// Every 10 seconds, each completed Forge (Smelter) passively adds
    /// veilsteel to its faction bank — 1 / 2 / 3 per tick at building level
    /// 1 / 2 / 3 (calculator ladder 2026-08; the Smelter absorbed the
    /// deleted Crucible's veilsteel-engine role). No inputs required — the
    /// old iron+veilstone conversion (and the miner supply chain that fed
    /// it) was removed when veilsteel became a map resource; the Forge
    /// trickle is the slow, infinite complement to mining the Sharp
    /// Crystals node. The Forge is build-limited to 1 per faction, so this
    /// tops out at 18 veilsteel/min per player at Lv3.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct ForgeConversionSystem : ISystem
    {
        // Calculator 2026-08: canonical 1-per-10-s baseline. The old flat
        // +50% interval buff (6.6667 s) is retired — output growth now
        // comes from the Smelter's Lv1-3 upgrade ladder instead.
        public const float GenerationInterval = 10f;
        /// <summary>Veilsteel credited per tick PER BUILDING LEVEL (Lv1 = 1,
        /// Lv2 = 2, Lv3 = 3). Name kept for UI readers (EntityExtractors).</summary>
        public const int VeilsteelPerTick = 1;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ForgeStorage>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            foreach (var (storage, faction, entity) in SystemAPI
                .Query<RefRW<ForgeStorage>, RefRO<FactionTag>>()
                .WithAll<SmelterTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                ref var forge = ref storage.ValueRW;

                forge.ConversionTimer += dt;
                if (forge.ConversionTimer >= GenerationInterval)
                {
                    forge.ConversionTimer -= GenerationInterval;

                    // Output scales with the building's upgrade level
                    // (BuildingUpgradeState.Level, default 1 — L0 and the
                    // free culture-baseline L1 both produce 1).
                    int level = 1;
                    if (em.HasComponent<BuildingUpgradeState>(entity))
                    {
                        int lvl = em.GetComponentData<BuildingUpgradeState>(entity).Level;
                        if (lvl > 1) level = lvl;
                    }

                    FactionEconomy.Add(em, faction.ValueRO.Value,
                        Cost.Of(veilsteel: level * VeilsteelPerTick));
                }
            }
        }
    }
}
