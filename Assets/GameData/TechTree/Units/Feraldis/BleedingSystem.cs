// File: Assets/GameData/TechTree/Units/Feraldis/BleedingSystem.cs
// Ticks Bleeding. Canon: docs/Design/Age_1_Feraldis.md.
//
// BLEEDING IS DOT *AND* BLOOD (design rule, 2026-08-05 rev.2). A bleeding
// unit takes damage over time AND drips blood onto the ground beneath it as
// it moves. That is the whole point of how much of the Feraldis roster
// inflicts bleed: a bleeding enemy is a brush painting the ground that
// Feraldis units frenzy on, that War Totems drink, and that Firethrowers
// can ignite — whether the victim ever dies or not.
//
// Unit-death contract (project rule): a bleed-out NEVER destroys the entity.
// It drives Health to 0 and DeathSystem does the rest — which is also what
// makes the kill splat blood normally.
//
// The DPS is fractional but Health is an int, so each victim carries a
// sub-second accumulator; whole points are subtracted as they accrue.
// BloodMap is managed main-thread state, so this is a SystemBase.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Influence;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class BleedingSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<Bleeding>();
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = EntityManager;
            var expired = new NativeList<Entity>(Allocator.Temp);
            bool bloodReady = BloodMap.Ready;

            foreach (var (bleed, health, transform, entity) in SystemAPI
                .Query<RefRW<Bleeding>, RefRW<Health>, RefRO<LocalTransform>>()
                .WithNone<DeathAnimationState>()
                .WithEntityAccess())
            {
                ref var b = ref bleed.ValueRW;

                // Already dying from another source — let it go.
                if (health.ValueRO.Value <= 0)
                {
                    expired.Add(entity);
                    continue;
                }

                b.Remaining -= dt;
                b.Accumulator += b.DamagePerSecond * dt;

                int whole = (int)b.Accumulator;
                if (whole > 0)
                {
                    b.Accumulator -= whole;
                    var h = health.ValueRO;
                    h.Value = math.max(0, h.Value - whole);
                    health.ValueRW = h;

                    // Credit the bleed's owner so pillage / last-damager
                    // bookkeeping attributes a bleed-out correctly.
                    if (em.HasComponent<LastDamagedByFaction>(entity))
                        em.SetComponentData(entity, new LastDamagedByFaction
                        {
                            Value = b.Source
                        });
                }

                // The blood half of the rule: drip under the victim wherever
                // it currently is, so a fleeing bleeder draws a line.
                if (bloodReady)
                {
                    var p = transform.ValueRO.Position;
                    BloodMap.AddBlood(new UnityEngine.Vector3(p.x, p.y, p.z),
                        BleedBloodPerSecond * dt);
                }

                if (b.Remaining <= 0f) expired.Add(entity);
            }

            for (int i = 0; i < expired.Length; i++)
                em.RemoveComponent<Bleeding>(expired[i]);
            expired.Dispose();
        }
    }
}
