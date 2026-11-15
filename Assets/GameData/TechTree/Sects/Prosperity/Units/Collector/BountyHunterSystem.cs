// BountyHunterSystem.cs -- the Bounty Hunter's Bounty (Sects.md § Sect of
// Prosperity, decision 47): every Interval seconds the hunter casts Bounty on
// ONE hostile unit within Range whose HP is below its own -- the target dies
// at once and its owner is paid its cost (ProsperityBounty). With no target in
// reach it looks again a second later. Deterministic: the hunters are
// snapshotted first (Bounty kills, which may change structure), then each
// picks the nearest eligible target, ties to the earlier entity.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Systems.Sect;

/// <summary>Bounty Hunter (the Collector, Sect of Prosperity): auto-casts
/// Bounty on one weaker hostile unit every Interval seconds.</summary>
public struct BountyHunterTrait : IComponentData
{
    public float Interval;
    public float Range;
    /// <summary>Seconds until the next cast.</summary>
    public float Timer;
}

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class BountyHunterSystem : SystemBase
    {
        /// <summary>Retry delay when no target is in reach. Loop pacing, not tuning.</summary>
        private const float RetrySeconds = 1f;

        static readonly ComponentType[] QT_Hunters =
        {
            ComponentType.ReadWrite<BountyHunterTrait>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Hunters;

        static readonly ComponentType[] QT_Units =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_Units;

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = SystemAPI.Time.DeltaTime;
            var hq = QC_Hunters.Get(em, QT_Hunters);
            if (hq.IsEmpty) return;

            using var hunters = hq.ToEntityArray(Allocator.Temp);
            var traits = hq.ToComponentDataArray<BountyHunterTrait>(Allocator.Temp);
            try { Tick(em, dt, hunters, traits); }
            finally { traits.Dispose(); }
        }

        private void Tick(EntityManager em, float dt, NativeArray<Entity> hunters, NativeArray<BountyHunterTrait> traits)
        {
            bool anyDue = false;
            for (int i = 0; i < traits.Length; i++)
            {
                var t = traits[i];
                t.Timer -= dt;
                traits[i] = t;
                if (t.Timer <= 0f) anyDue = true;
            }
            if (!anyDue)
            {
                for (int i = 0; i < hunters.Length; i++) em.SetComponentData(hunters[i], traits[i]);
                return;
            }

            var uq = QC_Units.Get(em, QT_Units);
            using var units = uq.ToEntityArray(Allocator.Temp);
            using var uxf = uq.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < hunters.Length; i++)
            {
                var h = hunters[i];
                var t = traits[i];
                if (t.Timer <= 0f && em.Exists(h))
                {
                    var hp = em.GetComponentData<Health>(h);
                    Entity best = Entity.Null;
                    if (hp.Value > 0 && !TransientState.Active<DeathAnimationState>(em, h))
                    {
                        var me = em.GetComponentData<FactionTag>(h).Value;
                        float3 at = em.GetComponentData<LocalTransform>(h).Position;
                        float bestD = t.Range * t.Range;
                        for (int u = 0; u < units.Length; u++)
                        {
                            var v = units[u];
                            if (!em.Exists(v)) continue;
                            float d = math.distancesq(uxf[u].Position.xz, at.xz);
                            if (d > bestD) continue;
                            if (!ProsperityBounty.CanTake(em, me, v)) continue;
                            if (em.GetComponentData<Health>(v).Value >= hp.Value) continue;   // weaker than the hunter only
                            bestD = d;
                            best = v;
                        }
                        if (best != Entity.Null) ProsperityBounty.Strike(em, me, best, ProsperityBounty.HunterShare);
                    }
                    t.Timer = best != Entity.Null ? math.max(RetrySeconds, t.Interval) : RetrySeconds;
                }
                if (em.Exists(h)) em.SetComponentData(h, t);
            }
        }
    }
}
