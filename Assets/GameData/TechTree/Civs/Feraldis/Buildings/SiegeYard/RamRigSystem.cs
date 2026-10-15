// RamRigSystem.cs
// The Ram Rig: a moderately fast, fragile explosive on wheels. It drives to
// the structure it is attacking and BLOWS when it touches it — or wherever it
// dies (FeraldisDeathInterceptor calls Detonate). The blast is siege damage
// (it breaks walls) and hits EVERYTHING in its radius: enemies, allies and its
// own escort. Units cannot set it off by touching it, only by killing it.
// docs/Design/Age_1_Feraldis.md §7.5, Teams.md §2.

using TheWaningBorder.Core;
using TheWaningBorder.Systems.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Feraldis
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class RamRigSystem : SystemBase
    {
        static readonly ComponentType[] QT_Rams =
        {
            ComponentType.ReadOnly<RamRigTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Target>(),
            ComponentType.Exclude<RamRigSpent>(),
        };
        static CachedEntityQuery QC_Rams;

        static readonly ComponentType[] QT_Victims =
        {
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_Victims;

        protected override void OnUpdate()
        {
            // Feraldis is not in every edition (docs/Design/Alpha_Build.md §4);
            // without it this system has nothing to do and no config to read.
            if (!GameEdition.Allows(Cultures.Feraldis)) return;
            var em = EntityManager;
            var q = QC_Rams.Get(em, QT_Rams);
            if (q.IsEmpty) return;
            float trigger = FeraldisCombat.Cfg.ramTriggerDistance;

            using var rams = q.ToEntityArray(Allocator.Temp);
            var blow = new NativeList<Entity>(Allocator.Temp);
            for (int i = 0; i < rams.Length; i++)
            {
                var t = em.GetComponentData<Target>(rams[i]).Value;
                if (t == Entity.Null || !em.Exists(t) || !em.HasComponent<BuildingTag>(t)) continue;
                var p = em.GetComponentData<LocalTransform>(rams[i]).Position;
                if (TargetGeometry.SurfaceDistXZ(em, p, t) <= trigger) blow.Add(rams[i]);
            }
            for (int i = 0; i < blow.Length; i++)
            {
                Detonate(em, blow[i]);
                if (em.HasComponent<Health>(blow[i]))
                {
                    var h = em.GetComponentData<Health>(blow[i]);
                    h.Value = 0;
                    em.SetComponentData(blow[i], h);
                }
            }
            blow.Dispose();
        }

        /// <summary>The blast. Latched (RamRigSpent) so a rig blows exactly once.</summary>
        public static void Detonate(EntityManager em, Entity ram)
        {
            if (!em.Exists(ram) || em.HasComponent<RamRigSpent>(ram)) return;
            em.AddComponent<RamRigSpent>(ram);
            if (!em.HasComponent<LocalTransform>(ram)) return;
            var c = FeraldisCombat.Cfg;
            float3 at = em.GetComponentData<LocalTransform>(ram).Position;
            var source = em.HasComponent<FactionTag>(ram) ? em.GetComponentData<FactionTag>(ram).Value : Faction.Border;

            var q = QC_Victims.Get(em, QT_Victims);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (ents[i] == ram) continue;
                // Surface distance for buildings, so a big wall section is hit.
                float d = em.HasComponent<BuildingTag>(ents[i])
                    ? TargetGeometry.SurfaceDistXZ(em, at, ents[i])
                    : math.distance(at.xz, xfs[i].Position.xz);
                if (d > c.ramBlastRadius) continue;
                SpellDamage.Apply(em, ents[i], c.ramBlastDamage, DamageType.Siege, source, friendlyFire: true);
            }
        }
    }
}
