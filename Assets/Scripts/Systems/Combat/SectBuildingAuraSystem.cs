// File: Assets/Scripts/Systems/Combat/SectBuildingAuraSystem.cs
// Handles runtime aura effects for sect unique buildings.
// Ticks once per second. Only processes completed (non-UnderConstruction) buildings.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.FogOfWar;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Processes aura effects for sect unique buildings every 1 second.
    ///
    /// Runtime auras handled:
    ///   410 Sanctuary       - Heal friendly units +3 HP within 15u
    ///   412 StoneheartBastion - Add SpellBuff(ArmorBonus=3) to friendly buildings within 12u
    ///   413 VeilSpire       - Stamp FogOfWar for owning faction within 30u
    ///   416 GlassSanctum    - Add SpellBuff(DamageReflect=0.15) to friendly buildings within 10u
    ///   417 Tribunal        - Add SpellDebuff(SpeedReduction=0) to enemy buildings within 20u (marker)
    ///   419 DreadTotem      - Add SpellDebuff(SpeedReduction=0.10) to enemy units within 15u
    ///   420 BindingPillar   - Deal 5 damage to CrystalTag entities within 12u
    ///
    /// Passive-only (no runtime aura): 411 ArchiveTower, 414 FlameBeacon,
    ///   415 Strongbox, 418 WarPyre, 421 PurgeAltar
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SectBuildingAuraSystem : ISystem
    {
        private const float TickInterval = 1f;
        private float _timer;

        private EntityQuery _sectBuildingQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SectUniqueBuildingTag>();

            _sectBuildingQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<SectUniqueBuildingTag>(),
                ComponentType.ReadOnly<PresentationId>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.Exclude<UnderConstruction>()
            );
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            _timer += dt;
            if (_timer < TickInterval) return;
            _timer = 0f;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var em = state.EntityManager;

            var buildingPositions = _sectBuildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var buildingPids = _sectBuildingQuery.ToComponentDataArray<PresentationId>(Allocator.Temp);
            var buildingFactions = _sectBuildingQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < buildingPositions.Length; i++)
            {
                float3 bPos = buildingPositions[i].Position;
                Faction bFaction = buildingFactions[i].Value;
                int pid = buildingPids[i].Id;

                switch (pid)
                {
                    case 410: // Sanctuary — heal friendly units +3 HP within 15u
                        ApplySanctuaryHeal(ref state, ecb, em, bPos, bFaction, 15f);
                        break;

                    case 412: // StoneheartBastion — armor buff to friendly buildings within 12u
                        ApplyStoneheartArmorBuff(ref state, ecb, em, bPos, bFaction, 12f);
                        break;

                    case 413: // VeilSpire — stamp fog of war within 30u
                        ApplyVeilSpireReveal(bPos, bFaction, 30f);
                        break;

                    case 416: // GlassSanctum — damage reflect buff to friendly buildings within 10u
                        ApplyGlassSanctumReflect(ref state, ecb, em, bPos, bFaction, 10f);
                        break;

                    case 417: // Tribunal — debuff marker on enemy buildings within 20u
                        ApplyTribunalDebuff(ref state, ecb, em, bPos, bFaction, 20f);
                        break;

                    case 419: // DreadTotem — speed debuff on enemy units within 15u
                        ApplyDreadTotemDebuff(ref state, ecb, em, bPos, bFaction, 15f);
                        break;

                    case 420: // BindingPillar — damage enemy crystal entities within 12u
                        ApplyBindingPillarDamage(ref state, ecb, em, bPos, bFaction, 12f);
                        break;

                    // 411, 414, 415, 418, 421 — passive only, no runtime aura
                    default:
                        break;
                }
            }

            buildingPositions.Dispose();
            buildingPids.Dispose();
            buildingFactions.Dispose();

            ecb.Playback(em);
            ecb.Dispose();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // AURA IMPLEMENTATIONS
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Sanctuary: heal friendly units within radius by 3 HP per tick.</summary>
        private void ApplySanctuaryHeal(ref SystemState state, EntityCommandBuffer ecb,
            EntityManager em, float3 center, Faction faction, float radius)
        {
            foreach (var (transform, health, factionTag, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRW<Health>, RefRO<FactionTag>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value != faction) continue;
                float dist = math.distance(center, transform.ValueRO.Position);
                if (dist > radius) continue;

                int newHp = math.min(health.ValueRO.Value + 3, health.ValueRO.Max);
                health.ValueRW.Value = newHp;
            }
        }

        /// <summary>StoneheartBastion: add ArmorBonus=3 SpellBuff to friendly buildings within radius.</summary>
        private void ApplyStoneheartArmorBuff(ref SystemState state, EntityCommandBuffer ecb,
            EntityManager em, float3 center, Faction faction, float radius)
        {
            foreach (var (transform, factionTag, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<BuildingTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value != faction) continue;
                float dist = math.distance(center, transform.ValueRO.Position);
                if (dist > radius) continue;

                var buff = new SpellBuff
                {
                    ArmorBonus = 3f,
                    DamageMultiplier = 0f,
                    SpeedMultiplier = 0f,
                    DamageReflect = 0f,
                    TimeRemaining = 2f // Refreshed every 1s tick, 2s buffer
                };

                if (em.HasComponent<SpellBuff>(entity))
                {
                    // Only overwrite if our armor bonus is higher
                    var existing = em.GetComponentData<SpellBuff>(entity);
                    if (existing.ArmorBonus < 3f)
                    {
                        existing.ArmorBonus = 3f;
                        if (existing.TimeRemaining < 2f) existing.TimeRemaining = 2f;
                        ecb.SetComponent(entity, existing);
                    }
                    else
                    {
                        // Just refresh timer
                        existing.TimeRemaining = math.max(existing.TimeRemaining, 2f);
                        ecb.SetComponent(entity, existing);
                    }
                }
                else
                {
                    ecb.AddComponent(entity, buff);
                }
            }
        }

        /// <summary>VeilSpire: stamp fog of war for owning faction.</summary>
        private void ApplyVeilSpireReveal(float3 center, Faction faction, float radius)
        {
            var fogMgr = FogOfWarManager.Instance;
            if (fogMgr == null) return;
            fogMgr.Stamp(faction, new UnityEngine.Vector3(center.x, center.y, center.z), radius);
        }

        /// <summary>GlassSanctum: add DamageReflect=0.15 SpellBuff to friendly buildings within radius.</summary>
        private void ApplyGlassSanctumReflect(ref SystemState state, EntityCommandBuffer ecb,
            EntityManager em, float3 center, Faction faction, float radius)
        {
            foreach (var (transform, factionTag, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<BuildingTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value != faction) continue;
                float dist = math.distance(center, transform.ValueRO.Position);
                if (dist > radius) continue;

                var buff = new SpellBuff
                {
                    ArmorBonus = 0f,
                    DamageMultiplier = 0f,
                    SpeedMultiplier = 0f,
                    DamageReflect = 0.15f,
                    TimeRemaining = 2f
                };

                if (em.HasComponent<SpellBuff>(entity))
                {
                    var existing = em.GetComponentData<SpellBuff>(entity);
                    if (existing.DamageReflect < 0.15f)
                    {
                        existing.DamageReflect = 0.15f;
                        if (existing.TimeRemaining < 2f) existing.TimeRemaining = 2f;
                        ecb.SetComponent(entity, existing);
                    }
                    else
                    {
                        existing.TimeRemaining = math.max(existing.TimeRemaining, 2f);
                        ecb.SetComponent(entity, existing);
                    }
                }
                else
                {
                    ecb.AddComponent(entity, buff);
                }
            }
        }

        /// <summary>Tribunal: debuff marker on enemy buildings within radius (SpeedReduction=0 as marker).</summary>
        private void ApplyTribunalDebuff(ref SystemState state, EntityCommandBuffer ecb,
            EntityManager em, float3 center, Faction faction, float radius)
        {
            foreach (var (transform, factionTag, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<BuildingTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value == faction) continue; // Skip friendly
                float dist = math.distance(center, transform.ValueRO.Position);
                if (dist > radius) continue;

                var debuff = new SpellDebuff
                {
                    SpeedReduction = 0f, // Marker only — build speed penalty handled elsewhere
                    SuppliesDrainPerSecond = 0f,
                    TimeRemaining = 2f
                };

                if (em.HasComponent<SpellDebuff>(entity))
                {
                    var existing = em.GetComponentData<SpellDebuff>(entity);
                    existing.TimeRemaining = math.max(existing.TimeRemaining, 2f);
                    ecb.SetComponent(entity, existing);
                }
                else
                {
                    ecb.AddComponent(entity, debuff);
                }
            }
        }

        /// <summary>DreadTotem: apply SpeedReduction=0.10 debuff to enemy units within radius.</summary>
        private void ApplyDreadTotemDebuff(ref SystemState state, EntityCommandBuffer ecb,
            EntityManager em, float3 center, Faction faction, float radius)
        {
            foreach (var (transform, factionTag, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value == faction) continue; // Skip friendly
                float dist = math.distance(center, transform.ValueRO.Position);
                if (dist > radius) continue;

                var debuff = new SpellDebuff
                {
                    SpeedReduction = 0.10f,
                    SuppliesDrainPerSecond = 0f,
                    TimeRemaining = 2f
                };

                if (em.HasComponent<SpellDebuff>(entity))
                {
                    var existing = em.GetComponentData<SpellDebuff>(entity);
                    if (existing.SpeedReduction < 0.10f) existing.SpeedReduction = 0.10f;
                    existing.TimeRemaining = math.max(existing.TimeRemaining, 2f);
                    ecb.SetComponent(entity, existing);
                }
                else
                {
                    ecb.AddComponent(entity, debuff);
                }
            }
        }

        /// <summary>
        /// BindingPillar: deal 5 damage to ENEMY CrystalTag entities within radius.
        /// Fix #227: the previous version damaged every crystal entity regardless
        /// of faction, which meant a pillar would slowly destroy the owner's own
        /// crystal buildings or units if any were nearby.
        /// </summary>
        private void ApplyBindingPillarDamage(ref SystemState state, EntityCommandBuffer ecb,
            EntityManager em, float3 center, Faction ownerFaction, float radius)
        {
            foreach (var (transform, health, factionTag, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRW<Health>, RefRO<FactionTag>>()
                .WithAll<CrystalTag>()
                .WithEntityAccess())
            {
                if (factionTag.ValueRO.Value == ownerFaction) continue; // skip own crystals

                float dist = math.distance(center, transform.ValueRO.Position);
                if (dist > radius) continue;

                health.ValueRW.Value = math.max(0, health.ValueRO.Value - 5);
            }
        }
    }
}
