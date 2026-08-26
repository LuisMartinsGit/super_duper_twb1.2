// TempleExplodeSystem.cs
// Glow stored at a Temple of Ridan detonates when the Temple is destroyed
// (spec §6.3, refinement #2 moved storage from a standalone Reliquary onto
// the Temple itself). The explosion damages every non-owner entity inside
// GlowReliquaryExplodeRadius, scaled by the stored Glow amount.
//
// Runs UpdateBefore(DeathSystem) so we can read GlowStored before the
// entity is queued for destruction. Stored is zeroed out immediately so
// the explode fires exactly once even if the Temple lingers a frame at
// Health <= 0.
//
// Empty Temples die quietly — no explode.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Systems.Combat;
using TheWaningBorder.Core.Localization;
using static TheWaningBorder.Core.Config.BorderConstants;

using TheWaningBorder.Core;
namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class TempleExplodeSystem : SystemBase
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

            // Collect Temples at 0 HP with stored Glow.
            var explodes = new NativeList<float3>(2, Allocator.Temp);
            var explodeFactions = new NativeList<Faction>(2, Allocator.Temp);
            var explodeAmounts = new NativeList<int>(2, Allocator.Temp);
            var consumedEntities = new NativeList<Entity>(2, Allocator.Temp);

            foreach (var (stored, health, transform, faction, entity) in SystemAPI
                .Query<RefRW<GlowStored>, RefRO<Health>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<TempleOfRidanTag>()
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

            // Build a quick-lookup set of the exploding Temples themselves
            // so the AOE loop can skip them as victims.
            var selfSet = new NativeHashSet<Entity>(consumedEntities.Length, Allocator.Temp);
            for (int i = 0; i < consumedEntities.Length; i++) selfSet.Add(consumedEntities[i]);

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

                TWBLog.Log($"[TempleExplode] {owner}'s Temple detonates with {stored} stored Glow ({damage} damage in {GlowReliquaryExplodeRadius:F0}u)");

                for (int v = 0; v < victimEnts.Length; v++)
                {
                    if (selfSet.Contains(victimEnts[v])) continue;
                    if (victimFactions[v].Value == owner) continue;
                    if (victimHealth[v].Value <= 0) continue;

                    var pos = victimTransforms[v].Position;
                    float dxz = math.distance(
                        new float2(pos.x, pos.z),
                        new float2(center.x, center.z));
                    if (dxz > GlowReliquaryExplodeRadius) continue;

                    float falloff = 1f - (dxz / GlowReliquaryExplodeRadius);
                    int dealt = (int)math.max(1, damage * falloff);

                    var h = em.GetComponentData<Health>(victimEnts[v]);
                    h.Value = math.max(0, h.Value - dealt);
                    em.SetComponentData(victimEnts[v], h);
                }
            }

            // An enshrined SHARDROOT survives the blast: it drops as the
            // persistent artifact pickup in the crater, up for grabs
            // (Curse & Shardroot canon §3.1 — volatility).
            for (int e = 0; e < consumedEntities.Length; e++)
            {
                var temple = consumedEntities[e];
                if (!em.HasComponent<ShardrootTag>(temple)) continue;
                em.RemoveComponent<ShardrootTag>(temple);

                var dropped = TheWaningBorder.Entities.GlowPickup.Create(
                    em, explodes[e], RitualKind.Purification,
                    ShardrootState.ShardrootPower);
                em.AddComponent<ShardrootTag>(dropped);
                TheWaningBorder.Systems.Border.ShardrootSystem.MakePersistent(em, dropped);
                SimSignals.Notify(
                    Loc.T("The Temple falls — the SHARDROOT lies in the crater!"));
            }

            selfSet.Dispose();
            explodes.Dispose();
            explodeFactions.Dispose();
            explodeAmounts.Dispose();
            consumedEntities.Dispose();
        }
    }
}
