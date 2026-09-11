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
// Cadence: a full pairwise count every PackRefreshSeconds rather than every
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

            for (int i = 0; i < ents.Length; i++)
            {
                int size = 1;
                var p = xfs[i].Position;
                for (int j = 0; j < ents.Length; j++)
                {
                    if (j == i || facs[j].Value != facs[i].Value) continue;
                    float dx = xfs[j].Position.x - p.x;
                    float dz = xfs[j].Position.z - p.z;
                    if (dx * dx + dz * dz <= r2) size++;
                }

                // The first member is the unit itself and earns nothing; the
                // bonus is for company.
                float bonus = math.min(maxBonus, perMember * (size - 1));

                var e = ents[i];
                if (em.HasComponent<CrystallingPack>(e))
                    em.SetComponentData(e, new CrystallingPack { Size = size });
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
        }
    }
}
