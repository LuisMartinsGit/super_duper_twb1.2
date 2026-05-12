// EquipmentTierSystem.cs
// Applies the per-faction equipment tier multiplier to unit stats. Mirrors
// UnitRankSystem's stamp-and-apply pattern so the two layers stack cleanly:
// UnitRank covers per-unit veterancy, EquipmentTier covers faction-wide
// research. A Lv3 (Crystal-rank) unit with Veilsteel equipment gets BOTH
// multipliers applied to Damage / Defense.
//
// Stamp pattern: UnitEquipmentApplied tracks the last-applied tier per
// unit. Each tick, units whose faction's tier for their class > the stamp
// get the (new/old) diff applied and the stamp bumped.
//
// Spec §4.1.
// Location: Assets/Scripts/Systems/Combat/

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(UnitRankSystem))]
    public partial struct EquipmentTierSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UnitTag>();
            state.RequireForUpdate<FactionEquipmentTier>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // Snapshot per-faction tiers into a fixed-size lookup so the
            // per-unit loop doesn't query the EM for each unit.
            // Faction enum ranges 0..7 (player) and 8 (Curse) — size 9.
            const int FactionCount = 9;
            var byFaction = new NativeArray<FactionEquipmentTier>(FactionCount, Allocator.Temp);
            // All zero-initialised → all Base by default (struct default).

            foreach (var (tag, tiers) in SystemAPI
                .Query<RefRO<FactionTag>, RefRO<FactionEquipmentTier>>())
            {
                int idx = (int)tag.ValueRO.Value;
                if (idx >= 0 && idx < FactionCount)
                    byFaction[idx] = tiers.ValueRO;
            }

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (unitTag, faction, entity) in SystemAPI
                .Query<RefRO<UnitTag>, RefRO<FactionTag>>()
                .WithEntityAccess())
            {
                int facIdx = (int)faction.ValueRO.Value;
                if (facIdx < 0 || facIdx >= FactionCount) continue;

                EquipmentTier target = byFaction[facIdx].Get(unitTag.ValueRO.Class);

                EquipmentTier applied = EquipmentTier.Base;
                if (em.HasComponent<UnitEquipmentApplied>(entity))
                    applied = em.GetComponentData<UnitEquipmentApplied>(entity).Value;

                if (target == applied) continue;

                ApplyTierDiff(em, entity, applied, target);

                if (em.HasComponent<UnitEquipmentApplied>(entity))
                    em.SetComponentData(entity, new UnitEquipmentApplied { Value = target });
                else
                    ecb.AddComponent(entity, new UnitEquipmentApplied { Value = target });
            }

            ecb.Playback(em);
            ecb.Dispose();
            byFaction.Dispose();
        }

        private static void ApplyTierDiff(EntityManager em, Entity entity,
            EquipmentTier oldTier, EquipmentTier newTier)
        {
            float diff = EquipmentTierConfig.StatMultiplier(newTier)
                       / EquipmentTierConfig.StatMultiplier(oldTier);
            if (math.abs(diff - 1f) < 0.001f) return;

            if (em.HasComponent<Damage>(entity))
            {
                var d = em.GetComponentData<Damage>(entity);
                d.Value = (int)(d.Value * diff);
                em.SetComponentData(entity, d);
            }
            if (em.HasComponent<Defense>(entity))
            {
                var def = em.GetComponentData<Defense>(entity);
                def.Melee  = (int)(def.Melee  * diff);
                def.Ranged = (int)(def.Ranged * diff);
                def.Siege  = (int)(def.Siege  * diff);
                def.Magic  = (int)(def.Magic  * diff);
                em.SetComponentData(entity, def);
            }
        }
    }
}
