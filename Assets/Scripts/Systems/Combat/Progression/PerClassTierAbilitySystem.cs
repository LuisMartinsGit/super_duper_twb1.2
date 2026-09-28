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
//                       with the removed Glow tier)
//   Hero Veilsteel:     summon a temporal echo of the hero
//   Hero Glow:          revive nearby fallen units (one-shot revive subsumed
//                       with the removed Glow tier)

using Unity.Collections;
using TheWaningBorder.Core;
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
        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every
        // call and these were never disposed, so this hot path leaked one
        // per invocation. A bloated registry slows every later query AND
        // every structural change. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_UnitTagFactionTagLocalTransformHealth =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_UnitTagFactionTagLocalTransformHealth;

        #endregion

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
                            _                       => EquipmentTierConfig.SiegeShieldAuraVeilstoneBonus,
                        };
                        var aura = new SiegeShieldAura
                        {
                            Radius = EquipmentTierConfig.SiegeShieldAuraRadius,
                            BonusShield = bonus,
                        };
                        if (em.HasComponent<SiegeShieldAura>(entity))
                        {
                            // Write only on change: an unconditional write
                            // re-dirtied every siege chunk every frame.
                            var cur = em.GetComponentData<SiegeShieldAura>(entity);
                            if (cur.Radius != aura.Radius || cur.BonusShield != aura.BonusShield)
                                em.SetComponentData(entity, aura);
                        }
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
                            _                       => EquipmentTierConfig.HeroPhaseShieldReductionVeilstone,
                        };

                        if (em.HasComponent<HeroPhaseShield>(entity))
                        {
                            var ps = em.GetComponentData<HeroPhaseShield>(entity);
                            if (ps.BaseCooldown != EquipmentTierConfig.HeroPhaseShieldCooldown
                                || ps.ReductionPercent != reduction)
                            {
                                ps.BaseCooldown = EquipmentTierConfig.HeroPhaseShieldCooldown;
                                ps.ReductionPercent = reduction;
                                em.SetComponentData(entity, ps);
                            }
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
            // Nothing to do unless an aura exists or a stale boost must be
            // stripped (2026-09-25). This used to snapshot four arrays over
            // every unit and walk them every frame in matches with no
            // Veilstone siege at all — which is most of every match.
            var auraSourceQuery = SystemAPI.QueryBuilder()
                .WithAll<SiegeShieldAura, LocalTransform, FactionTag, UnitTag>().Build();
            var boostedQuery = SystemAPI.QueryBuilder().WithAll<AuraShieldBoost>().Build();
            if (!auraSourceQuery.IsEmpty || !boostedQuery.IsEmpty)
                ResolveSiegeAuras(em);

            TickPhaseShields(dt);
        }

        private void ResolveSiegeAuras(EntityManager em)
        {
            // Snapshot allied targets (units) once.
            var allyQuery = QC_UnitTagFactionTagLocalTransformHealth.Get(em, QT_UnitTagFactionTagLocalTransformHealth);
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

                    // Padded squared reject first; the exact test below
                    // still decides every candidate it lets through.
                    float ddx = center.x - allyTransforms[i].Position.x;
                    float ddz = center.z - allyTransforms[i].Position.z;
                    float rPad = r * 1.001f + 0.01f;
                    if (ddx * ddx + ddz * ddz > rPad * rPad) continue;

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
                    {
                        if (em.GetComponentData<AuraShieldBoost>(e).Amount != amount)
                            em.SetComponentData(e, new AuraShieldBoost { Amount = amount });
                    }
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
        }

        // ── Phase 3: tick hero phase shield + absorb damage ──
        // RefRO + a write only when the shield actually refunds (2026-09-25):
        // RefRW<Health> marked every hero chunk's Health as changed every
        // frame, defeating every Health change filter downstream.
        private void TickPhaseShields(float dt)
        {
            var em = EntityManager;
            var healEnts = new NativeList<Entity>(4, Allocator.Temp);
            var healVals = new NativeList<int>(4, Allocator.Temp);
            foreach (var (psRW, healthRO, entity) in SystemAPI
                .Query<RefRW<HeroPhaseShield>, RefRO<Health>>()
                .WithEntityAccess())
            {
                ref var ps = ref psRW.ValueRW;
                var hp = healthRO.ValueRO;

                if (ps.ChargeReadyTimer > 0f)
                    ps.ChargeReadyTimer = math.max(0f, ps.ChargeReadyTimer - dt);

                int delta = ps.LastObservedHealth - hp.Value;
                if (delta > 0 && ps.ChargeReadyTimer <= 0f)
                {
                    // Absorb a fraction of the damage and reset cooldown.
                    int refunded = (int)math.ceil(delta * ps.ReductionPercent);
                    int healed = math.min(hp.Max, hp.Value + refunded);
                    if (healed != hp.Value)
                    {
                        hp.Value = healed;
                        healEnts.Add(entity);
                        healVals.Add(healed);
                    }
                    ps.ChargeReadyTimer = ps.BaseCooldown;
                }
                ps.LastObservedHealth = hp.Value;
            }

            for (int i = 0; i < healEnts.Length; i++)
            {
                var h = em.GetComponentData<Health>(healEnts[i]);
                h.Value = healVals[i];
                em.SetComponentData(healEnts[i], h);
            }
            healEnts.Dispose();
            healVals.Dispose();
        }
    }
}
