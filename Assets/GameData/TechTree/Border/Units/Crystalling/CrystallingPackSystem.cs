// CrystallingPackSystem.cs
// A Crystalling on its own is a nuisance; a pack is a threat. Every so often
// this counts, for each Crystalling, how many others stand within the pack
// radius and turns that into a damage bonus — so a swarm that keeps together
// hits like a swarm, and one that has been picked apart one at a time hits
// like the stragglers it is.
//
// The bonus is delivered through BorderBuff.AttBonus, which every combat
// path already multiplies in (MeleeCombatSystem, the Veilstinger and
// Godsplinter systems). The component existed for the old "Enforcement
// aura" and had no writer left; giving it one costs the combat code nothing.
//
// Cadence: a bucketed neighbour count every PackRefreshSeconds rather than every
// tick. A wave is a few dozen units, so the count is cheap, and a pack's size
// does not change meaningfully inside half a second. Deterministic across
// peers: it reads only replicated positions on a fixed sim cadence.
//
// Numbers live on BorderSettingsSO (crystallingPack*), not here.
// docs/Design/Curse_And_Shardroot.md — "Packs".

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CrystallingPackSystem : ISystem
    {
        static readonly ComponentType[] QT_Crystalling =
        {
            ComponentType.ReadOnly<CrystallingTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Crystalling;

        private const float PackRefreshSeconds = 0.5f;
        // Match-phased (SimCadence.cs), not a bare accumulator.
        private SimCadence.Periodic _timer;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CrystallingTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!_timer.Due(SystemAPI.Time.DeltaTime, PackRefreshSeconds)) return;

            var settings = TheWaningBorder.Data.Border.BorderSettings.Get();
            float radius = settings.crystallingPackRadius;
            float perMember = settings.crystallingPackBonusPerMember;
            float maxBonus = settings.crystallingPackMaxBonus;
            if (radius <= 0f || perMember <= 0f) return;
            float r2 = radius * radius;

            var em = state.EntityManager;
            var q = QC_Crystalling.Get(em, QT_Crystalling);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            // Spatial buckets (2026-09-27): cells one pack-radius wide, keyed
            // by (faction, cell) with no hash collisions, so each unit tests
            // only its own and the 8 neighbouring cells of its own faction
            // instead of the whole swarm — O(n) instead of O(n^2). The test
            // itself is the unchanged dx*dx+dz*dz <= r2 on the same floats,
            // and a count does not depend on visit order, so the sizes are
            // exactly what the pairwise loop produced.
            int n = ents.Length;
            var buckets = new NativeParallelMultiHashMap<long, int>(math.max(1, n), Allocator.Temp);
            var cellX = new NativeArray<int>(n, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var cellZ = new NativeArray<int>(n, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            // 1 % wider than the radius, so float rounding in x * inv can never
            // push a pair that is exactly `radius` apart two cells apart.
            float inv = 1f / (radius * 1.01f);
            for (int i = 0; i < n; i++)
            {
                cellX[i] = (int)math.floor(xfs[i].Position.x * inv);
                cellZ[i] = (int)math.floor(xfs[i].Position.z * inv);
                buckets.Add(PackKey((int)facs[i].Value, cellX[i], cellZ[i]), i);
            }

            for (int i = 0; i < n; i++)
            {
                int size = 1;
                var p = xfs[i].Position;
                int fac = (int)facs[i].Value;
                for (int oz = -1; oz <= 1; oz++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    if (!buckets.TryGetFirstValue(PackKey(fac, cellX[i] + ox, cellZ[i] + oz),
                            out int j, out var it))
                        continue;
                    do
                    {
                        if (j == i) continue;
                        float dx = xfs[j].Position.x - p.x;
                        float dz = xfs[j].Position.z - p.z;
                        if (dx * dx + dz * dz <= r2) size++;
                    } while (buckets.TryGetNextValue(out j, ref it));
                }

                // The first member is the unit itself and earns nothing; the
                // bonus is for company.
                float bonus = math.min(maxBonus, perMember * (size - 1));

                var e = ents[i];
                if (em.HasComponent<CrystallingPack>(e))
                {
                    if (em.GetComponentData<CrystallingPack>(e).Size != size)
                        em.SetComponentData(e, new CrystallingPack { Size = size });
                }
                else
                    em.AddComponentData(e, new CrystallingPack { Size = size });

                if (em.HasComponent<BorderBuff>(e))
                {
                    var buff = em.GetComponentData<BorderBuff>(e);
                    if (buff.AttBonus != bonus)
                    {
                        buff.AttBonus = bonus;
                        em.SetComponentData(e, buff);
                    }
                }
                else if (bonus > 0f)
                {
                    em.AddComponentData(e, new BorderBuff { AttBonus = bonus });
                }
            }

            buckets.Dispose();
            cellX.Dispose();
            cellZ.Dispose();
        }

        /// <summary>Collision-free bucket key: faction in the top bits, the
        /// two cell coordinates in 24 bits each (a +/-8M-cell range).</summary>
        private static long PackKey(int faction, int cx, int cz)
            => ((long)(faction & 0xFFFF) << 48)
             | ((long)(cx & 0xFFFFFF) << 24)
             | (long)(cz & 0xFFFFFF);
    }
}
