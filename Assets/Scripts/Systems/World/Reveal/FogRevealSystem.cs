// FogRevealSystem.cs
// Turns FogReveal's pending bits into per-faction timers and counts them
// down on the lockstep step; a reveal whose every timer has run out is
// disabled. See FogRevealComponents.cs for the two rules that grant one.
//
// Deterministic: each entity's timers depend only on its own component and
// the fixed step, so the order entities are visited in changes nothing.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.World
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TheWaningBorder.Systems.Combat.ProjectileSystem))]
    public partial class FogRevealSystem : SystemBase
    {
        private EntityQuery _active;
        private FogRevealSystemConfig _cfg;

        protected override void OnCreate()
        {
            _active = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<FogReveal>()
                .Build(this);
            RequireForUpdate(_active);
        }

        protected override void OnUpdate()
        {
            if (_cfg == null) _cfg = ComponentConfig.Require<FogRevealSystemConfig>();
            if (_cfg == null) return;   // logged loudly by Require

            float dt = SystemAPI.Time.DeltaTime;
            float hitFor = _cfg.rangedHitDuration;
            float lockFor = _cfg.lockRevealDuration;
            var em = EntityManager;

            using var ents = _active.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var r = em.GetComponentData<FogReveal>(e);
                r.HitLo = math.max(r.HitLo - dt, 0f);
                r.HitHi = math.max(r.HitHi - dt, 0f);
                r.LockLo = math.max(r.LockLo - dt, 0f);
                r.LockHi = math.max(r.LockHi - dt, 0f);
                for (int f = 0; f < FogReveal.Players; f++)
                {
                    int bit = 1 << f;
                    if ((r.PendingHit & bit) != 0)
                    {
                        if (f < 4) r.HitLo[f] = math.max(r.HitLo[f], hitFor);
                        else r.HitHi[f - 4] = math.max(r.HitHi[f - 4], hitFor);
                    }
                    if ((r.PendingLock & bit) != 0)
                    {
                        if (f < 4) r.LockLo[f] = math.max(r.LockLo[f], lockFor);
                        else r.LockHi[f - 4] = math.max(r.LockHi[f - 4], lockFor);
                    }
                }
                r.PendingHit = 0;
                r.PendingLock = 0;
                em.SetComponentData(e, r);

                bool any = math.any(r.HitLo > 0f) || math.any(r.HitHi > 0f)
                           || math.any(r.LockLo > 0f) || math.any(r.LockHi > 0f);
                if (!any) em.SetComponentEnabled<FogReveal>(e, false);
            }
        }
    }
}
