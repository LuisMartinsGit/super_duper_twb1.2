// ReliquaryExplodeSystem.cs
// Glow reliquaries explode when destroyed while holding Glow (spec §6.3).
// The explosion damages every non-owner entity inside
// GlowReliquaryExplodeRadius, scaled by the stored Glow amount.
//
// Runs UpdateBefore(DeathSystem) so we can read GlowReliquaryStored
// before the entity is queued for destruction. Stored is zeroed out
// immediately so the explode fires exactly once even if the building
// somehow lingers a frame at Health <= 0.
//
// Empty reliquaries die quietly — no explode.
//
// Location: Assets/Scripts/Systems/Economy/

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Systems.Combat;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class ReliquaryExplodeSystem : SystemBase
    {
        private EntityQuery _victimQuery;

        protected override void OnCreate()
        {
            _victimQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>(),
                ComponentType.ReadOnly<FactionTag>()
            );
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;

            // Collect reliquaries at 0 HP with stored Glow.
            var explodes = new NativeList<float3>(2, Allocator.Temp);
            var explodeFactions = new NativeList<Faction>(2, Allocator.Temp);
            var explodeAmounts = new NativeList<int>(2, Allocator.Temp);
            var consumedEntities = new NativeList<Entity>(2, Allocator.Temp);

            foreach (var (stored, health, transform, faction, entity) in SystemAPI
                .Query<RefRW<GlowReliquaryStored>, RefRO<Health>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<GlowReliquaryTag>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;
                if (stored.ValueRO.Amount <= 0) continue;

                explodes.Add(transform.ValueRO.Position);
                explodeFactions.Add(faction.ValueRO.Value);
                explodeAmounts.Add(stored.ValueRO.Amount);
                consumedEntities.Add(entity);

                // Zero out so we don't double-trigger if the entity lingers.
                stored.ValueRW.Amount = 0;
            }

            if (explodes.Length == 0)
            {
                explodes.Dispose();
                explodeFactions.Dispose();
                explodeAmounts.Dispose();
                consumedEntities.Dispose();
                return;
            }

            // Build a quick-lookup set of the exploding reliquaries themselves
            // so the AOE loop can skip them as victims.
            var selfSet = new NativeHashSet<Entity>(consumedEntities.Length, Allocator.Temp);
            for (int i = 0; i < consumedEntities.Length; i++) selfSet.Add(consumedEntities[i]);

            // Snapshot victims.
            using var victimEnts = _victimQuery.ToEntityArray(Allocator.Temp);
            using var victimTransforms = _victimQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var victimHealth = _victimQuery.ToComponentDataArray<Health>(Allocator.Temp);
            using var victimFactions = _victimQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int e = 0; e < explodes.Length; e++)
            {
                float3 center = explodes[e];
                Faction owner = explodeFactions[e];
                int stored = explodeAmounts[e];
                int damage = (int)math.ceil(stored * GlowReliquaryExplodeDamagePerGlow);

                Debug.Log($"[GlowReliquary] EXPLODE — {owner}'s reliquary detonates with {stored} stored Glow ({damage} damage in {GlowReliquaryExplodeRadius:F0}u)");

                for (int v = 0; v < victimEnts.Length; v++)
                {
                    // Skip self (the reliquary being destroyed) and other
                    // entities owned by the same faction.
                    if (selfSet.Contains(victimEnts[v])) continue;
                    if (victimFactions[v].Value == owner) continue;
                    if (victimHealth[v].Value <= 0) continue;

                    var pos = victimTransforms[v].Position;
                    float dxz = math.distance(
                        new float2(pos.x, pos.z),
                        new float2(center.x, center.z));
                    if (dxz > GlowReliquaryExplodeRadius) continue;

                    // Linear falloff with distance.
                    float falloff = 1f - (dxz / GlowReliquaryExplodeRadius);
                    int dealt = (int)math.max(1, damage * falloff);

                    var h = em.GetComponentData<Health>(victimEnts[v]);
                    h.Value = math.max(0, h.Value - dealt);
                    em.SetComponentData(victimEnts[v], h);
                }
            }

            selfSet.Dispose();
            explodes.Dispose();
            explodeFactions.Dispose();
            explodeAmounts.Dispose();
            consumedEntities.Dispose();
        }
    }
}
