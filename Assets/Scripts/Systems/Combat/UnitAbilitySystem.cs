// UnitAbilitySystem.cs
// Processes sect unit abilities: cooldowns, activation, and effect timers
// Location: Assets/Scripts/Systems/Combat/UnitAbilitySystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Handles all 12 sect unit abilities in three phases:
    ///
    /// Phase 1: Tick cooldowns on all UnitAbility components
    /// Phase 2: Process AbilityActivated tags - apply effects per AbilityId, start cooldown, remove tag
    /// Phase 3: Tick duration-based effect timers (HealOverTime, Fortified, Condemned) - remove on expiry
    ///
    /// NOTE: Not BurstCompiled because it uses structural changes and EntityManager reads.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(MeleeCombatSystem))]
    public partial struct UnitAbilitySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f) return;

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            var em = state.EntityManager;

            // ══════════════════════════════════════════════════════════
            // Phase 1: Tick cooldowns
            // ══════════════════════════════════════════════════════════
            foreach (var ability in SystemAPI.Query<RefRW<UnitAbility>>())
            {
                if (ability.ValueRO.CooldownRemaining > 0f)
                {
                    ability.ValueRW.CooldownRemaining -= dt;
                    if (ability.ValueRW.CooldownRemaining < 0f)
                        ability.ValueRW.CooldownRemaining = 0f;
                }
            }

            // ══════════════════════════════════════════════════════════
            // Phase 2: Process AbilityActivated tags
            // ══════════════════════════════════════════════════════════
            foreach (var (activated, ability, transform, faction, entity) in SystemAPI
                .Query<RefRO<AbilityActivated>, RefRW<UnitAbility>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithEntityAccess())
            {
                var target = activated.ValueRO.Target;
                var id = ability.ValueRO.Id;
                var myPos = transform.ValueRO.Position;
                var myFaction = faction.ValueRO.Value;

                switch (id)
                {
                    // ── 1. RapidMend (ScarGuard): self heal-over-time ──
                    case AbilityId.RapidMend:
                        ecb.AddComponent(entity, new HealOverTime
                        {
                            TotalHealing = 50f,
                            Duration = 3f,
                            Elapsed = 0f
                        });
                        break;

                    // ── 2. ArcanePulse (GolemAutark): AOE magic damage ──
                    case AbilityId.ArcanePulse:
                        ApplyAoeDamage(em, myPos, myFaction, 5f, 15, entity);
                        break;

                    // ── 3. Fortify (StoneWarden): self armor + immobile ──
                    case AbilityId.Fortify:
                        ecb.AddComponent(entity, new Fortified
                        {
                            ArmorBonus = 5f,
                            TimeRemaining = 8f
                        });
                        break;

                    // ── 4. Dispel (ArchivistAdept): strip buffs/debuffs from target ──
                    case AbilityId.Dispel:
                        if (target != Entity.Null && em.Exists(target))
                        {
                            if (em.HasComponent<SpellBuff>(target))
                                ecb.RemoveComponent<SpellBuff>(target);
                            if (em.HasComponent<SpellDebuff>(target))
                                ecb.RemoveComponent<SpellDebuff>(target);
                            if (em.HasComponent<Condemned>(target))
                                ecb.RemoveComponent<Condemned>(target);
                            if (em.HasComponent<IgniteBuff>(target))
                                ecb.RemoveComponent<IgniteBuff>(target);
                            if (em.HasComponent<VoidStrikeBuff>(target))
                                ecb.RemoveComponent<VoidStrikeBuff>(target);
                            if (em.HasComponent<Fortified>(target))
                                ecb.RemoveComponent<Fortified>(target);
                        }
                        break;

                    // ── 5. Sanction (FlameWarden): root target ──
                    case AbilityId.Sanction:
                        if (target != Entity.Null && em.Exists(target))
                        {
                            ecb.AddComponent(target, new SpellDebuff
                            {
                                SpeedReduction = 1.0f,
                                SuppliesDrainPerSecond = 0f,
                                TimeRemaining = 2f
                            });
                        }
                        break;

                    // ── 6. Safeguard (VaultKeeper): armor aura to nearby friendlies ──
                    case AbilityId.Safeguard:
                        ApplyAoeBuff(em, ecb, myPos, myFaction, 6f, entity);
                        break;

                    // ── 7. MirrorShield (GlassmarkArcanist): damage reflect on self ──
                    case AbilityId.MirrorShield:
                        ecb.AddComponent(entity, new SpellBuff
                        {
                            ArmorBonus = 0f,
                            DamageMultiplier = 0f,
                            SpeedMultiplier = 0f,
                            DamageReflect = 0.30f,
                            TimeRemaining = 6f
                        });
                        break;

                    // ── 8. Condemn (Judicator): mark target for bonus damage ──
                    case AbilityId.Condemn:
                        if (target != Entity.Null && em.Exists(target))
                        {
                            ecb.AddComponent(target, new Condemned
                            {
                                DamageMultiplier = 1.25f,
                                TimeRemaining = 6f
                            });
                        }
                        break;

                    // ── 9. Ignite (Ashblade): fire damage on next 3 attacks ──
                    case AbilityId.Ignite:
                        ecb.AddComponent(entity, new IgniteBuff
                        {
                            AttacksRemaining = 3,
                            BonusDamage = 8f
                        });
                        break;

                    // ── 10. WarCry (Brandbreaker): AOE slow to enemies ──
                    case AbilityId.WarCry:
                        ApplyAoeSlow(em, ecb, myPos, myFaction, 8f, entity);
                        break;

                    // ── 11. ChainBind (Chaincaster): root target ──
                    case AbilityId.ChainBind:
                        if (target != Entity.Null && em.Exists(target))
                        {
                            ecb.AddComponent(target, new SpellDebuff
                            {
                                SpeedReduction = 1.0f,
                                SuppliesDrainPerSecond = 0f,
                                TimeRemaining = 3f
                            });
                        }
                        break;

                    // ── 12. VoidStrike (Nullblade): next-attack bonus damage ──
                    case AbilityId.VoidStrike:
                        ecb.AddComponent(entity, new VoidStrikeBuff
                        {
                            BonusDamage = 40f,
                            BonusVsCrystal = 80f
                        });
                        break;
                }

                // Start cooldown
                ability.ValueRW.CooldownRemaining = ability.ValueRO.CooldownDuration;

                // Remove the activation tag
                ecb.RemoveComponent<AbilityActivated>(entity);
            }

            // ══════════════════════════════════════════════════════════
            // Phase 3: Tick effect timers
            // ══════════════════════════════════════════════════════════

            // ── HealOverTime ──
            foreach (var (hot, health, entity) in SystemAPI
                .Query<RefRW<HealOverTime>, RefRW<Health>>()
                .WithEntityAccess())
            {
                float prevElapsed = hot.ValueRO.Elapsed;
                hot.ValueRW.Elapsed += dt;

                // Calculate healing this frame based on proportion of duration elapsed
                float healRate = hot.ValueRO.TotalHealing / hot.ValueRO.Duration;
                int healAmount = (int)(healRate * dt);
                if (healAmount < 1 && hot.ValueRO.Elapsed > prevElapsed) healAmount = 1;

                ref var hp = ref health.ValueRW;
                hp.Value = math.min(hp.Value + healAmount, hp.Max);

                if (hot.ValueRO.Elapsed >= hot.ValueRO.Duration)
                {
                    ecb.RemoveComponent<HealOverTime>(entity);
                }
            }

            // ── Fortified ──
            foreach (var (fortified, entity) in SystemAPI
                .Query<RefRW<Fortified>>()
                .WithEntityAccess())
            {
                fortified.ValueRW.TimeRemaining -= dt;
                if (fortified.ValueRO.TimeRemaining <= 0f)
                {
                    ecb.RemoveComponent<Fortified>(entity);
                }
            }

            // ── Condemned ──
            foreach (var (condemned, entity) in SystemAPI
                .Query<RefRW<Condemned>>()
                .WithEntityAccess())
            {
                condemned.ValueRW.TimeRemaining -= dt;
                if (condemned.ValueRO.TimeRemaining <= 0f)
                {
                    ecb.RemoveComponent<Condemned>(entity);
                }
            }
        }

        // ──────────────────────────────────────────────────────────
        // AOE Helpers
        // ──────────────────────────────────────────────────────────

        /// <summary>
        /// ArcanePulse: Deal flat damage to all enemies within radius.
        /// </summary>
        private static void ApplyAoeDamage(EntityManager em, float3 center, Faction casterFaction,
            float radius, int damage, Entity caster)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadWrite<Health>()
            );

            var entities = query.ToEntityArray(Allocator.Temp);
            var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float radiusSq = radius * radius;

            for (int i = 0; i < entities.Length; i++)
            {
                // Skip self and friendlies
                if (entities[i] == caster) continue;
                if (factions[i].Value == casterFaction) continue;

                float3 pos = transforms[i].Position;
                float distSq = math.distancesq(
                    new float2(center.x, center.z),
                    new float2(pos.x, pos.z));

                if (distSq <= radiusSq)
                {
                    var health = em.GetComponentData<Health>(entities[i]);
                    health.Value -= damage;
                    if (health.Value < 0) health.Value = 0;
                    em.SetComponentData(entities[i], health);
                }
            }

            entities.Dispose();
            factions.Dispose();
            transforms.Dispose();
        }

        /// <summary>
        /// Safeguard: Apply SpellBuff (armor aura) to nearby friendly units.
        /// </summary>
        private static void ApplyAoeBuff(EntityManager em, EntityCommandBuffer ecb,
            float3 center, Faction casterFaction, float radius, Entity caster)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            var entities = query.ToEntityArray(Allocator.Temp);
            var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float radiusSq = radius * radius;

            for (int i = 0; i < entities.Length; i++)
            {
                // Only affect friendlies (including self)
                if (factions[i].Value != casterFaction) continue;

                float3 pos = transforms[i].Position;
                float distSq = math.distancesq(
                    new float2(center.x, center.z),
                    new float2(pos.x, pos.z));

                if (distSq <= radiusSq)
                {
                    ecb.AddComponent(entities[i], new SpellBuff
                    {
                        ArmorBonus = 3f,
                        DamageMultiplier = 0f,
                        SpeedMultiplier = 0f,
                        DamageReflect = 0f,
                        TimeRemaining = 5f
                    });
                }
            }

            entities.Dispose();
            factions.Dispose();
            transforms.Dispose();
        }

        /// <summary>
        /// WarCry: Apply SpellDebuff (slow) to nearby enemy units.
        /// </summary>
        private static void ApplyAoeSlow(EntityManager em, EntityCommandBuffer ecb,
            float3 center, Faction casterFaction, float radius, Entity caster)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            var entities = query.ToEntityArray(Allocator.Temp);
            var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float radiusSq = radius * radius;

            for (int i = 0; i < entities.Length; i++)
            {
                // Skip self and friendlies
                if (entities[i] == caster) continue;
                if (factions[i].Value == casterFaction) continue;

                float3 pos = transforms[i].Position;
                float distSq = math.distancesq(
                    new float2(center.x, center.z),
                    new float2(pos.x, pos.z));

                if (distSq <= radiusSq)
                {
                    ecb.AddComponent(entities[i], new SpellDebuff
                    {
                        SpeedReduction = 0.30f,
                        SuppliesDrainPerSecond = 0f,
                        TimeRemaining = 4f
                    });
                }
            }

            entities.Dispose();
            factions.Dispose();
            transforms.Dispose();
        }
    }
}
