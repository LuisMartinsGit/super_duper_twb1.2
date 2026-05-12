// ShieldBarSystem.cs
// Crystal+ tier units have a second HP layer (spec §4.2). This system
// owns the lifecycle:
//   1. Sync: add/remove/resize ShieldBar based on UnitEquipmentApplied.
//   2. Absorb: detect HP drops vs LastObservedHealth and route the delta
//      through the shield first. Damage that overflows the shield falls
//      through to Health as normal.
//   3. Regen: out-of-combat regen with a 3s delay gate after each hit.
//
// The absorb pass is decoupled from combat systems — we observe damage
// post-hoc by stamping LastObservedHealth, which lets every damage path
// (melee, ranged, projectiles, cursed ground DoT, blast AOE, etc.) flow
// through the shield without per-call wiring.
//
// Location: Assets/Scripts/Systems/Combat/

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EquipmentTierSystem))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class ShieldBarSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = (float)SystemAPI.Time.DeltaTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // ── Phase 1: sync ShieldBar presence + Max to current tier ──
            foreach (var (appliedRO, healthRO, entity) in SystemAPI
                .Query<RefRO<UnitEquipmentApplied>, RefRO<Health>>()
                .WithEntityAccess())
            {
                int target = EquipmentTierConfig.ShieldBarMax(appliedRO.ValueRO.Value);
                bool hasShield = em.HasComponent<ShieldBar>(entity);

                if (target <= 0)
                {
                    if (hasShield) ecb.RemoveComponent<ShieldBar>(entity);
                    continue;
                }

                if (!hasShield)
                {
                    ecb.AddComponent(entity, new ShieldBar
                    {
                        Current = target,
                        Max = target,
                        LastObservedHealth = healthRO.ValueRO.Value,
                        RegenDelayTimer = 0f,
                    });
                }
                else
                {
                    var sb = em.GetComponentData<ShieldBar>(entity);
                    if (sb.Max != target)
                    {
                        sb.Max = target;
                        if (sb.Current > sb.Max) sb.Current = sb.Max;
                        em.SetComponentData(entity, sb);
                    }
                }
            }

            // ── Phase 2: absorb damage + regen ──
            // Iterate ShieldBar holders; compare Health to LastObservedHealth.
            // Any drop goes through the shield first; remaining damage stays
            // on Health. Then tick regen.
            foreach (var (shieldRW, healthRW) in SystemAPI
                .Query<RefRW<ShieldBar>, RefRW<Health>>())
            {
                ref var sb = ref shieldRW.ValueRW;
                ref var hp = ref healthRW.ValueRW;

                int delta = sb.LastObservedHealth - hp.Value;
                if (delta > 0 && sb.Current > 0)
                {
                    int absorbed = math.min(delta, sb.Current);
                    sb.Current -= absorbed;
                    hp.Value = math.min(hp.Max, hp.Value + absorbed);  // refund absorbed damage to Health
                    sb.RegenDelayTimer = 0f;
                }
                else if (delta > 0)
                {
                    // Damage with no shield to absorb — still counts as a hit
                    // for the regen-delay gate.
                    sb.RegenDelayTimer = 0f;
                }

                sb.LastObservedHealth = hp.Value;

                // Regen (gated by RegenDelay since last damage).
                if (sb.Current < sb.Max)
                {
                    sb.RegenDelayTimer += dt;
                    if (sb.RegenDelayTimer >= EquipmentTierConfig.ShieldBarRegenDelay)
                    {
                        float gained = EquipmentTierConfig.ShieldBarRegenPerSecond * dt;
                        int add = (int)math.ceil(gained);
                        if (add > 0)
                            sb.Current = math.min(sb.Max, sb.Current + add);
                    }
                }
            }

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
