// Ticks every timed Alanthor sect effect and pays out its expiry.
//
// One system rather than ten: these effects share exactly one behaviour —
// count down, then either remove yourself or hand back what you borrowed —
// and ten near-identical systems would each pay the query and scheduling cost
// for a handful of entities. The per-effect logic that is NOT shared (a
// building shutdown gating training, a veiled unit refusing orders) lives at
// the consuming site, gated on the component's presence.
//
// SystemBase, not ISystem: Cleanse writes to PlayerInfluenceMap, a managed
// static, and Harvest the Veil credits the faction bank through the managed
// EntityManager path.

using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Influence;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class AlanthorSectEffectSystem : SystemBase
    {
        /// <summary>Allied-unit query for Cleanse III's heal. Built once —
        /// creating a query per frame leaks native memory (see the managed
        /// query-leak sweep of 2026-07-16).</summary>
        private EntityQuery _healTargets;

        protected override void OnCreate()
        {
            _healTargets = GetEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<Health>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<UnitTag>());
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f) return;

            var em = EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            TickShutdown(em, ecb, dt);
            TickDisorder(em, ecb, dt);
            TickRegenTail(em, ecb, dt);
            TickDeathWard(em, ecb, dt);
            TickConjuredTowers(em, ecb, dt);
            TickVeil(em, ecb, dt);
            TickBulwark(em, ecb, dt);
            TickOverYield(em, ecb, dt);
            TickInfluenceBurst(em, ecb, dt);
            TickCurseWard(em, ecb, dt);

            ecb.Playback(em);
            ecb.Dispose();
        }

        // ── Antiquity ───────────────────────────────────────────────────────

        private void TickShutdown(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (shutdown, e) in SystemAPI.Query<RefRW<SectShutdown>>().WithEntityAccess())
            {
                shutdown.ValueRW.TimeRemaining -= dt;
                if (shutdown.ValueRO.TimeRemaining <= 0f) ecb.RemoveComponent<SectShutdown>(e);
            }
        }

        private void TickDisorder(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (disorder, e) in SystemAPI.Query<RefRW<SectDisordered>>().WithEntityAccess())
            {
                // Lv III lasts until the unit is killed — never counted down.
                if (SectEffectDuration.IsPermanent(disorder.ValueRO.TimeRemaining)) continue;

                disorder.ValueRW.TimeRemaining -= dt;
                if (disorder.ValueRO.TimeRemaining > 0f) continue;

                // Hand the unit back to the faction it started in.
                if (em.HasComponent<FactionTag>(e))
                    ecb.SetComponent(e, new FactionTag { Value = disorder.ValueRO.OriginalFaction });
                ecb.RemoveComponent<SectDisordered>(e);
            }
        }

        // ── Renewal ─────────────────────────────────────────────────────────

        private void TickRegenTail(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (tail, health, e) in SystemAPI
                .Query<RefRW<SectRegenTail>, RefRW<Health>>().WithEntityAccess())
            {
                int heal = (int)(health.ValueRO.Max * tail.ValueRO.FractionPerSecond * dt);
                if (heal > 0)
                {
                    int v = health.ValueRO.Value + heal;
                    health.ValueRW.Value = v > health.ValueRO.Max ? health.ValueRO.Max : v;
                }

                tail.ValueRW.TimeRemaining -= dt;
                if (tail.ValueRO.TimeRemaining <= 0f) ecb.RemoveComponent<SectRegenTail>(e);
            }
        }

        private void TickDeathWard(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (ward, health, e) in SystemAPI
                .Query<RefRW<SectDeathWard>, RefRW<Health>>().WithEntityAccess())
            {
                // The ward itself: nothing may take this unit below 1 HP. Held
                // here rather than in the damage path so every damage source —
                // melee, ranged, burning ground, curse — is covered by one rule.
                if (health.ValueRO.Value < 1) health.ValueRW.Value = 1;

                ward.ValueRW.TimeRemaining -= dt;
                if (ward.ValueRO.TimeRemaining > 0f) continue;

                if (ward.ValueRO.HealOnExpiry > 0f)
                {
                    int heal = (int)(health.ValueRO.Max * ward.ValueRO.HealOnExpiry);
                    int v = health.ValueRO.Value + heal;
                    health.ValueRW.Value = v > health.ValueRO.Max ? health.ValueRO.Max : v;
                }
                ecb.RemoveComponent<SectDeathWard>(e);
            }
        }

        private void TickConjuredTowers(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (tower, health, e) in SystemAPI
                .Query<RefRW<SectConjuredTower>, RefRW<Health>>().WithEntityAccess())
            {
                // Lv III towers are permanent — they stay until destroyed.
                if (SectEffectDuration.IsPermanent(tower.ValueRO.TimeRemaining)) continue;

                tower.ValueRW.TimeRemaining -= dt;
                if (tower.ValueRO.TimeRemaining > 0f) continue;

                // Crumble. Zero the health and let DeathSystem do the destroy —
                // destroying a building here would race the EndSimulation ECB.
                health.ValueRW.Value = 0;
                ecb.RemoveComponent<SectConjuredTower>(e);
            }
        }

        // ── Fortitude ───────────────────────────────────────────────────────

        private void TickVeil(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (veil, e) in SystemAPI.Query<RefRW<SectVeiled>>().WithEntityAccess())
            {
                veil.ValueRW.TimeRemaining -= dt;
                if (veil.ValueRO.TimeRemaining > 0f) continue;

                // Lv III pays out a damage window on expiry.
                if (veil.ValueRO.DamageOnExpiry > 0f)
                {
                    var buff = new SpellBuff
                    {
                        DamageMultiplier = 1f + veil.ValueRO.DamageOnExpiry,
                        TimeRemaining    = 10f,
                    };
                    if (em.HasComponent<SpellBuff>(e)) ecb.SetComponent(e, buff);
                    else                               ecb.AddComponent(e, buff);
                }

                ecb.RemoveComponent<SectVeiled>(e);
                if (em.HasComponent<StealthTag>(e)) ecb.RemoveComponent<StealthTag>(e);
            }
        }

        private void TickBulwark(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (bulwark, health, e) in SystemAPI
                .Query<RefRW<SectBulwark>, RefRW<Health>>().WithEntityAccess())
            {
                bulwark.ValueRW.TimeRemaining -= dt;
                if (bulwark.ValueRO.TimeRemaining > 0f) continue;

                // Take back exactly what was granted, and never leave the
                // building on 0 HP — expiry must not be able to kill it.
                int max = health.ValueRO.Max - bulwark.ValueRO.GrantedHp;
                if (max < 1) max = 1;
                health.ValueRW.Max = max;
                if (health.ValueRO.Value > max) health.ValueRW.Value = max;
                if (health.ValueRO.Value < 1)   health.ValueRW.Value = 1;

                ecb.RemoveComponent<SectBulwark>(e);
            }
        }

        // ── Reclamation ─────────────────────────────────────────────────────

        private void TickOverYield(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (yield, e) in SystemAPI.Query<RefRW<SectNodeOverYield>>().WithEntityAccess())
            {
                yield.ValueRW.TimeRemaining -= dt;
                yield.ValueRW.TickTimer     -= dt;

                if (yield.ValueRO.TickTimer <= 0f)
                {
                    yield.ValueRW.TickTimer += SectLeverEffects.HarvestTickSeconds;
                    PayHarvest(em, yield.ValueRO.Beneficiary, yield.ValueRO.Level);
                }

                if (yield.ValueRO.TimeRemaining <= 0f) ecb.RemoveComponent<SectNodeOverYield>(e);
            }
        }

        private static void PayHarvest(EntityManager em, Faction faction, byte level)
        {
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return;

            SectLeverEffects.HarvestYield(level, out int supplies, out int iron,
                                          out int veilstone, out int veilsteel);

            var res = em.GetComponentData<FactionResources>(bank);
            res.Supplies  += supplies;
            res.Iron      += iron;
            res.Veilstone += veilstone;
            res.Veilsteel += veilsteel;
            res.Clamp();
            em.SetComponentData(bank, res);
        }

        private void TickInfluenceBurst(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (burst, xf, e) in SystemAPI
                .Query<RefRW<SectInfluenceBurst>, RefRO<LocalTransform>>().WithEntityAccess())
            {
                var pos = xf.ValueRO.Position;

                // The influence deposit is gone (Regions.md §3b, 2026-08-31 —
                // no influence maps; ownership is territory-shaped and
                // changes only through claims). The burst keeps its healing
                // half, which is what the player actually sees.

                if (burst.ValueRO.HealsAllies)
                    HealAllies(em, _healTargets, burst.ValueRO.Owner, pos, burst.ValueRO.Radius, dt);

                burst.ValueRW.TimeRemaining -= dt;
                if (burst.ValueRO.TimeRemaining <= 0f) ecb.DestroyEntity(e);
            }
        }

        private static void HealAllies(EntityManager em, EntityQuery q, Faction owner,
                                       Unity.Mathematics.float3 center, float radius, float dt)
        {
            // 2% of max HP per second — the same rate Renewal's building
            // auto-repair uses, so the two read as one healing language.
            const float FractionPerSecond = 0.02f;
            float r2 = radius * radius;

            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs  = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                if (tags[i].Value != owner) continue;
                float dx = xfs[i].Position.x - center.x;
                float dz = xfs[i].Position.z - center.z;
                if (dx * dx + dz * dz > r2) continue;

                var h = em.GetComponentData<Health>(ents[i]);
                if (h.Value <= 0 || h.Value >= h.Max) continue;
                int v = h.Value + (int)(h.Max * FractionPerSecond * dt);
                h.Value = v > h.Max ? h.Max : v;
                em.SetComponentData(ents[i], h);
            }
        }

        private void TickCurseWard(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (ward, e) in SystemAPI.Query<RefRW<SectCurseWard>>().WithEntityAccess())
            {
                ward.ValueRW.TimeRemaining -= dt;
                if (ward.ValueRO.TimeRemaining <= 0f) ecb.RemoveComponent<SectCurseWard>(e);
            }
        }
    }
}
