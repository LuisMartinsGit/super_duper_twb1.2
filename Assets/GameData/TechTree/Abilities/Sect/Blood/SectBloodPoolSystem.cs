// SectBloodPoolSystem.cs
// Phase 3 of task-063: Feraldis blood-pool layer. Splits into three
// concerns inside a single ISystem to keep ordering trivial:
//
//   1. Spawn   — every dead unit position becomes a small BloodPool entity,
//                gated on at least one Feraldis Age II+ faction being live
//                in the world. Runs BEFORE DeathSystem so the WithNone
//                marker pattern lets each death fire exactly once.
//   2. Decay   — tick TimeRemaining on each pool; destroy at zero.
//   3. Sweep   — for each Feraldis Age II+ unit, stamp/clear InBloodPool
//                each frame based on radius proximity to any active pool.
//
// CombatDamageHelper reads InBloodPool and layers the in-pool damage
// bonus on top of Wrath's Lv I HP-missing bonus (+10% Lv I, +15% Lv II,
// +20% Lv III). The age-gate is enforced at sweep time (units of non-
// Feraldis or Age I factions never get the marker), and the global "is
// Phase 3 active at all" gate is computed once per OnUpdate to skip the
// per-unit work when no Feraldis Age II+ faction exists.
//
// Lv I tuning (per-pool):
//   - Radius:    2.5m
//   - Duration:  10s
//
// task-063 phase 3.
//
// Location: Assets/Scripts/Systems/Sect/SectBloodPoolSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct SectBloodPoolSystem : ISystem
    {
        private const float PoolRadius   = 2.5f;
        private const float PoolDuration = 10f;
        // Faction enum is byte (0..7). We size cache arrays accordingly.
        private const int   FactionCount = 8;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UnitTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            // Per-faction "is Feraldis Age II+" cache — built once per tick.
            var feraldisAge2Plus = new NativeArray<bool>(FactionCount, Allocator.Temp);
            bool anyFeraldisAge2Plus = false;
            for (byte f = 0; f < FactionCount; f++)
            {
                var faction = (Faction)f;
                if (FactionColors.GetFactionCulture(faction) != Cultures.Feraldis) continue;
                if (!FactionEconomy.TryGetBank(em, faction, out var bank)) continue;
                if (!em.HasComponent<FactionEra>(bank)) continue;
                if (em.GetComponentData<FactionEra>(bank).Value < 2) continue;
                feraldisAge2Plus[f] = true;
                anyFeraldisAge2Plus = true;
            }

            // ───── 1. SPAWN ─────────────────────────────────────────────────
            // Skip the death scan entirely if no Feraldis faction is Age 2+ —
            // pools are useless and would just leak entities for 10s each.
            if (anyFeraldisAge2Plus)
            {
                var spawnPositions = new NativeList<float3>(Allocator.Temp);
                foreach (var (health, transform) in SystemAPI
                    .Query<RefRO<Health>, RefRO<LocalTransform>>()
                    .WithAll<UnitTag>()
                    .WithNone<DeathAnimationState>())
                {
                    if (health.ValueRO.Value > 0) continue;
                    spawnPositions.Add(transform.ValueRO.Position);
                }
                for (int i = 0; i < spawnPositions.Length; i++)
                {
                    var pool = em.CreateEntity(
                        typeof(BloodPool), typeof(LocalTransform));
                    em.SetComponentData(pool, new BloodPool
                    {
                        Radius = PoolRadius,
                        TimeRemaining = PoolDuration,
                    });
                    em.SetComponentData(pool, LocalTransform.FromPositionRotationScale(
                        spawnPositions[i], quaternion.identity, 1f));
                }
                spawnPositions.Dispose();
            }

            // ───── 2. DECAY ─────────────────────────────────────────────────
            var expiredPools = new NativeList<Entity>(Allocator.Temp);
            foreach (var (pool, entity) in SystemAPI
                .Query<RefRW<BloodPool>>()
                .WithEntityAccess())
            {
                pool.ValueRW.TimeRemaining -= dt;
                if (pool.ValueRO.TimeRemaining <= 0f)
                    expiredPools.Add(entity);
            }
            for (int i = 0; i < expiredPools.Length; i++)
            {
                if (em.Exists(expiredPools[i]))
                    em.DestroyEntity(expiredPools[i]);
            }
            expiredPools.Dispose();

            // ───── 3. SWEEP ─────────────────────────────────────────────────
            // Collect surviving pool positions/radii for the proximity test.
            var poolPositions = new NativeList<float3>(Allocator.Temp);
            var poolRadii = new NativeList<float>(Allocator.Temp);
            foreach (var (pool, transform) in SystemAPI
                .Query<RefRO<BloodPool>, RefRO<LocalTransform>>())
            {
                if (pool.ValueRO.TimeRemaining <= 0f) continue;
                poolPositions.Add(transform.ValueRO.Position);
                poolRadii.Add(pool.ValueRO.Radius);
            }

            // Stamp / clear InBloodPool on Feraldis Age II+ units.
            var toStamp = new NativeList<Entity>(Allocator.Temp);
            var toClear = new NativeList<Entity>(Allocator.Temp);

            foreach (var (faction, transform, entity) in SystemAPI
                .Query<RefRO<FactionTag>, RefRO<LocalTransform>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                bool gated = (byte)faction.ValueRO.Value < FactionCount
                          && feraldisAge2Plus[(byte)faction.ValueRO.Value];
                bool inAnyPool = false;
                if (gated && poolPositions.Length > 0)
                {
                    var p = transform.ValueRO.Position;
                    for (int i = 0; i < poolPositions.Length; i++)
                    {
                        float dx = p.x - poolPositions[i].x;
                        float dz = p.z - poolPositions[i].z;
                        if (dx * dx + dz * dz <= poolRadii[i] * poolRadii[i])
                        {
                            inAnyPool = true;
                            break;
                        }
                    }
                }

                bool stamped = em.HasComponent<InBloodPool>(entity);
                if (inAnyPool && !stamped) toStamp.Add(entity);
                else if (!inAnyPool && stamped) toClear.Add(entity);
            }

            for (int i = 0; i < toStamp.Length; i++)
                if (em.Exists(toStamp[i])) em.AddComponent<InBloodPool>(toStamp[i]);
            for (int i = 0; i < toClear.Length; i++)
                if (em.Exists(toClear[i])) em.RemoveComponent<InBloodPool>(toClear[i]);

            toStamp.Dispose();
            toClear.Dispose();
            poolPositions.Dispose();
            poolRadii.Dispose();
            feraldisAge2Plus.Dispose();
        }
    }
}
