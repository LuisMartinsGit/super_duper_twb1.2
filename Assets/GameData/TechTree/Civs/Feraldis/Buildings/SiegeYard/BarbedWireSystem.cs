// BarbedWireSystem.cs
// The Harpoon Rig's barbed-wire line: for its short life it hurts and bleeds
// EVERY unit crossing it — friend or foe (the deliberate exception in
// docs/Design/Teams.md §2). Bleeding paints blood, so a wire also lays a strip
// of frenzy ground. docs/Design/Age_1_Feraldis.md §7.5.

using TheWaningBorder.Core;
using TheWaningBorder.Systems.Combat;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Feraldis
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class BarbedWireSystem : SystemBase
    {
        private const float Tick = 0.25f;
        private SimCadence.Periodic _cadence;

        static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Units;

        protected override void OnUpdate()
        {
            // Feraldis is not in every edition (docs/Design/Alpha_Build.md §4);
            // without it this system has nothing to do and no config to read.
            if (!GameEdition.Allows(Cultures.Feraldis)) return;
            if (!_cadence.Due(SystemAPI.Time.DeltaTime, Tick)) return;
            var em = EntityManager;
            var c = FeraldisCombat.Cfg;

            var wires = new NativeList<Entity>(Allocator.Temp);
            foreach (var (w, e) in SystemAPI.Query<RefRO<BarbedWire>>().WithEntityAccess()) wires.Add(e);
            if (wires.Length == 0) { wires.Dispose(); return; }

            var q = QC_Units.Get(em, QT_Units);
            using var units = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int w = 0; w < wires.Length; w++)
            {
                var wire = em.GetComponentData<BarbedWire>(wires[w]);
                wire.Remaining -= Tick;
                if (wire.Remaining <= 0f) { em.DestroyEntity(wires[w]); continue; }

                wire.Carry += c.wireDamagePerSecond * Tick;
                int dmg = Mathf.FloorToInt(wire.Carry);
                wire.Carry -= dmg;
                float hw2 = c.wireHalfWidth * c.wireHalfWidth;
                for (int i = 0; i < units.Length; i++)
                {
                    if (DistSqToSegmentXZ(xfs[i].Position, wire.From, wire.To) > hw2) continue;
                    if (dmg > 0) SpellDamage.Apply(em, units[i], dmg, DamageType.Melee, wire.Source, friendlyFire: true);
                    FeraldisBleed.Apply(em, ecb, units[i], c.wireBleedDps, c.wireBleedSeconds, wire.Source);
                }
                em.SetComponentData(wires[w], wire);
            }
            ecb.Playback(em);
            ecb.Dispose();
            wires.Dispose();
        }

        public static float DistSqToSegmentXZ(float3 p, float3 a, float3 b)
        {
            float2 pa = p.xz - a.xz, ba = b.xz - a.xz;
            float len2 = math.dot(ba, ba);
            float t = len2 > 1e-4f ? math.saturate(math.dot(pa, ba) / len2) : 0f;
            float2 d = pa - ba * t;
            return math.dot(d, d);
        }
    }
}
