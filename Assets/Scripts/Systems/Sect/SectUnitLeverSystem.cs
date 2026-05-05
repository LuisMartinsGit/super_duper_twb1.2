// SectUnitLeverSystem.cs
// Generic stat-bump applier for the Unit lever (task-063 phase 5).
//
// For each faction × sect with the Unit lever at Lv 1+, scans units of
// that faction matching the spec's UnitClass and applies the per-sect
// bump (HP / armor / damage). Stamped via SectUnitLeverApplied so each
// unit is processed exactly once per (sect-id, level) pair. Phase 4
// scaling diff is applied when the lever level on the faction rises.
//
// task-063 phase 5.
//
// Location: Assets/Scripts/Systems/Sect/SectUnitLeverSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SectUnitLeverSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UnitTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // Materialize the unit set once per tick. SystemAPI.Query cannot
            // be called from a static helper (source-generator constraint),
            // so we keep the iteration in OnUpdate and pre-collect the inputs
            // per sect into temp lists.
            var unitEntities = new NativeList<Entity>(Allocator.Temp);
            var unitClasses  = new NativeList<int>(Allocator.Temp);
            var unitFactions = new NativeList<Faction>(Allocator.Temp);
            foreach (var (unit, faction, entity) in SystemAPI
                .Query<RefRO<UnitTag>, RefRO<FactionTag>>()
                .WithEntityAccess())
            {
                unitEntities.Add(entity);
                unitClasses.Add((int)unit.ValueRO.Class);
                unitFactions.Add(faction.ValueRO.Value);
            }

            for (int sectIdx = 0; sectIdx < SectConfig.SectCount; sectIdx++)
            {
                string sectId = SectConfig.IdAt(sectIdx);
                var spec = SectLeverEffects.UnitOf(sectId);
                if (spec.DamageMultiplier == 0f && spec.ArmorBonus == 0 && spec.HpMultiplier == 0f) continue;

                ApplyForSect(em, sectIdx, sectId, spec,
                    unitEntities, unitClasses, unitFactions);
            }

            unitEntities.Dispose();
            unitClasses.Dispose();
            unitFactions.Dispose();
        }

        private static void ApplyForSect(EntityManager em,
            int sectIdx, string sectId, in SectUnitLeverSpec spec,
            NativeList<Entity> unitEntities, NativeList<int> unitClasses,
            NativeList<Faction> unitFactions)
        {
            for (int i = 0; i < unitEntities.Length; i++)
            {
                var entity = unitEntities[i];
                if (!em.Exists(entity)) continue;

                if (spec.AppliesToClass >= 0 && unitClasses[i] != spec.AppliesToClass)
                    continue;

                byte level = SectQuery.LevelOf(em, unitFactions[i],
                    sectId, SectLeverKind.Unit);
                if (level == 0) continue;

                byte appliedLevel = 0;
                bool hasStamp = HasStampForSect(em, entity, sectIdx, out appliedLevel);
                if (hasStamp && appliedLevel >= level) continue;

                float scalar = SectLeverEffects.LevelScalar(level)
                              / (appliedLevel > 0 ? SectLeverEffects.LevelScalar(appliedLevel) : 1f);

                ApplyDelta(em, entity, spec, scalar);
                SetStampForSect(em, entity, sectIdx, level);
            }
        }

        // Stamp uses a DynamicBuffer<SectUnitLeverApplied> per unit so each
        // sect's level can be tracked independently (a unit's faction may
        // have multiple sects with the same UnitClass target).
        private static bool HasStampForSect(EntityManager em, Entity entity, int sectIdx, out byte level)
        {
            level = 0;
            if (!em.HasBuffer<SectUnitLeverApplied>(entity)) return false;
            var buf = em.GetBuffer<SectUnitLeverApplied>(entity);
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].SectIndex == sectIdx) { level = buf[i].Level; return true; }
            }
            return false;
        }

        private static void SetStampForSect(EntityManager em, Entity entity, int sectIdx, byte level)
        {
            DynamicBuffer<SectUnitLeverApplied> buf;
            if (!em.HasBuffer<SectUnitLeverApplied>(entity))
                buf = em.AddBuffer<SectUnitLeverApplied>(entity);
            else
                buf = em.GetBuffer<SectUnitLeverApplied>(entity);
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].SectIndex == sectIdx)
                {
                    buf[i] = new SectUnitLeverApplied { SectIndex = (byte)sectIdx, Level = level };
                    return;
                }
            }
            buf.Add(new SectUnitLeverApplied { SectIndex = (byte)sectIdx, Level = level });
        }

        private static void ApplyDelta(EntityManager em, Entity entity,
            in SectUnitLeverSpec spec, float scalar)
        {
            // HP — multiplicative on Max + Value.
            if (math.abs(spec.HpMultiplier - 1f) > 0.001f && em.HasComponent<Health>(entity))
            {
                var hp = em.GetComponentData<Health>(entity);
                float diff = 1f + (spec.HpMultiplier - 1f) * scalar;
                hp.Max   = (int)(hp.Max   * diff);
                hp.Value = (int)(hp.Value * diff);
                em.SetComponentData(entity, hp);
            }

            // Armor — flat add to all defense channels.
            if (spec.ArmorBonus > 0 && em.HasComponent<Defense>(entity))
            {
                var def = em.GetComponentData<Defense>(entity);
                int bump = (int)(spec.ArmorBonus * scalar);
                def.Melee  += bump;
                def.Ranged += bump;
                def.Siege  += bump;
                def.Magic  += bump;
                em.SetComponentData(entity, def);
            }

            // Damage — multiplicative on Damage component.
            if (math.abs(spec.DamageMultiplier - 1f) > 0.001f && em.HasComponent<Damage>(entity))
            {
                var dmg = em.GetComponentData<Damage>(entity);
                float diff = 1f + (spec.DamageMultiplier - 1f) * scalar;
                dmg.Value = (int)(dmg.Value * diff);
                em.SetComponentData(entity, dmg);
            }
        }
    }

}
