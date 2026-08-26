// PerClassTierAbilitySystem.cs
// Per-class equipment-tier abilities (spec §4.3-§4.4). Layers on top of the
// universal Veilstone+ shield bar (ShieldBarSystem) by reading the unit's
// UnitClass + effective tier and stamping class-specific components:
//
//   Siege  + Veilstone/Veilsteel/Glow  →  SiegeShieldAura (passive aura, see below)
//   Magic  + Veilstone/Veilsteel/Glow  →  HeroPhaseShield (per-hit damage absorb)
//   Support + Veilstone/Veilsteel/Glow →  HeroPhaseShield  (treated as hero archetype)
//
// Aura resolution: SiegeShieldAura entities scan for friendly units in
// radius and stamp AuraShieldBoost on them. ShieldBarSystem reads the
// boost to bump ShieldBar.Max temporarily.
//
// Hero phase shield: detects Health drops via LastObservedHealth (same
// pattern as ShieldBarSystem). On an absorbed hit, refunds part of the
// damage and resets the cooldown.
//
// Not in this slice (active abilities, need UI binding):
//   Spearman Veilsteel: 1HP duplicate squad
//   Spearman Glow:      revive battalion members on cooldown
//   Siege Veilsteel:    temporal echo shots
//   Siege Glow:         self-repair from destruction (on-death revive subsumed
//                       by GlowReviveSystem)
//   Hero Veilsteel:     summon a temporal echo of the hero
//   Hero Glow:          revive nearby fallen units (one-shot revive subsumed
//                       by GlowReviveSystem; nearby radius extension is future)
//
// Location: Assets/Scripts/Systems/Combat/

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EquipmentTierSystem))]
    [UpdateBefore(typeof(ShieldBarSystem))]
    public partial class PerClassTierAbilitySystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = (float)SystemAPI.Time.DeltaTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // ── Phase 1: stamp/unstamp class-specific components ──
            foreach (var (unitTag, applied, entity) in SystemAPI
                .Query<RefRO<UnitTag>, RefRO<UnitEquipmentApplied>>()
                .WithEntityAccess())
            {
                var tier = applied.ValueRO.Value;
                var cls = unitTag.ValueRO.Class;
                bool atLeastVeilstone = (int)tier >= (int)EquipmentTier.Veilstone;

                // Siege Veilstone+ aura
                if (cls == UnitClass.Siege)
                {
                    if (atLeastVeilstone)
                    {
                        int bonus = tier switch
                        {
                            EquipmentTier.Veilsteel => EquipmentTierConfig.SiegeShieldAuraVeilsteelBonus,
                            EquipmentTier.Glow      => EquipmentTierConfig.SiegeShieldAuraGlowBonus,
                            _                       => EquipmentTierConfig.SiegeShieldAuraVeilstoneBonus,
                        };
                        var aura = new SiegeShieldAura
                        {
                            Radius = EquipmentTierConfig.SiegeShieldAuraRadius,
                            BonusShield = bonus,
                        };
                        if (em.HasComponent<SiegeShieldAura>(entity))
                            em.SetComponentData(entity, aura);
                        else
                            ecb.AddComponent(entity, aura);
                    }
                    else if (em.HasComponent<SiegeShieldAura>(entity))
                    {
                        ecb.RemoveComponent<SiegeShieldAura>(entity);
                    }
                }

                // Hero (Magic/Support) Veilstone+ phase shield
                if (cls == UnitClass.Magic || cls == UnitClass.Support)
                {
                    if (atLeastVeilstone)
                    {
                        float reduction = tier switch
                        {
                            EquipmentTier.Veilsteel => EquipmentTierConfig.HeroPhaseShieldReductionVeilsteel,
                            EquipmentTier.Glow      => EquipmentTierConfig.HeroPhaseShieldReductionGlow,
                            _                       => EquipmentTierConfig.HeroPhaseShieldReductionVeilstone,
                        };

                        if (em.HasComponent<HeroPhaseShield>(entity))
                        {
                            var ps = em.GetComponentData<HeroPhaseShield>(entity);
                            ps.BaseCooldown = EquipmentTierConfig.HeroPhaseShieldCooldown;
                            ps.ReductionPercent = reduction;
                            em.SetComponentData(entity, ps);
                        }
                        else
                        {
                            int hp = em.HasComponent<Health>(entity)
                                ? em.GetComponentData<Health>(entity).Value : 0;
                            ecb.AddComponent(entity, new HeroPhaseShield
                            {
                                ChargeReadyTimer = 0f,
                                BaseCooldown = EquipmentTierConfig.HeroPhaseShieldCooldown,
                                ReductionPercent = reduction,
                                LastObservedHealth = hp,
                            });
                        }
                    }
                    else if (em.HasComponent<HeroPhaseShield>(entity))
                    {
                        ecb.RemoveComponent<HeroPhaseShield>(entity);
                    }
                }
            }

            // Apply structural changes from Phase 1 before iterating again.
            ecb.Playback(em);
            ecb.Dispose();

            // ── Phase 2: resolve siege auras ──
            // Snapshot allied targets (units) once.
            var allyQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            using var allyEnts = allyQuery.ToEntityArray(Allocator.Temp);
            using var allyFactions = allyQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var allyTransforms = allyQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var allyHealths = allyQuery.ToComponentDataArray<Health>(Allocator.Temp);

            var boostByEntity = new NativeHashMap<Entity, int>(allyEnts.Length, Allocator.Temp);

            foreach (var (aura, transform, faction) in SystemAPI
                .Query<RefRO<SiegeShieldAura>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<UnitTag>())
            {
                var center = transform.ValueRO.Position;
                var myFac = faction.ValueRO.Value;
                float r = aura.ValueRO.Radius;
                int bonus = aura.ValueRO.BonusShield;

                for (int i = 0; i < allyEnts.Length; i++)
                {
                    // Team allies count as allies. The best-wins map below is
                    // what keeps two sources from stacking. docs/Design/Teams.md
                    if (!Alliances.AreAllied(myFac, allyFactions[i].Value)) continue;
                    if (allyHealths[i].Value <= 0) continue;

                    float dxz = math.distance(
                        new float2(center.x, center.z),
                        new float2(allyTransforms[i].Position.x, allyTransforms[i].Position.z));
                    if (dxz > r) continue;

                    // Max bonus across multiple auras (don't stack).
                    if (boostByEntity.TryGetValue(allyEnts[i], out int prior))
                    {
                        if (bonus > prior) boostByEntity[allyEnts[i]] = bonus;
                    }
                    else
                    {
                        boostByEntity.Add(allyEnts[i], bonus);
                    }
                }
            }

            // Apply boost: stamp AuraShieldBoost on covered units, strip from others.
            var ecb2 = new EntityCommandBuffer(Allocator.Temp);
            for (int i = 0; i < allyEnts.Length; i++)
            {
                var e = allyEnts[i];
                if (boostByEntity.TryGetValue(e, out int amount))
                {
                    if (em.HasComponent<AuraShieldBoost>(e))
                        em.SetComponentData(e, new AuraShieldBoost { Amount = amount });
                    else
                        ecb2.AddComponent(e, new AuraShieldBoost { Amount = amount });
                }
                else if (em.HasComponent<AuraShieldBoost>(e))
                {
                    ecb2.RemoveComponent<AuraShieldBoost>(e);
                }
            }
            ecb2.Playback(em);
            ecb2.Dispose();
            boostByEntity.Dispose();

            // ── Phase 3: tick hero phase shield + absorb damage ──
            foreach (var (psRW, healthRW) in SystemAPI
                .Query<RefRW<HeroPhaseShield>, RefRW<Health>>())
            {
                ref var ps = ref psRW.ValueRW;
                ref var hp = ref healthRW.ValueRW;

                if (ps.ChargeReadyTimer > 0f)
                    ps.ChargeReadyTimer = math.max(0f, ps.ChargeReadyTimer - dt);

                int delta = ps.LastObservedHealth - hp.Value;
                if (delta > 0 && ps.ChargeReadyTimer <= 0f)
                {
                    // Absorb a fraction of the damage and reset cooldown.
                    int refunded = (int)math.ceil(delta * ps.ReductionPercent);
                    hp.Value = math.min(hp.Max, hp.Value + refunded);
                    ps.ChargeReadyTimer = ps.BaseCooldown;
                }
                ps.LastObservedHealth = hp.Value;
            }
        }
    }
}
