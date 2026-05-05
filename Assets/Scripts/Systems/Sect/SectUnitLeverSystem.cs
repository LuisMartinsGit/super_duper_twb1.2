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

            // Per-faction × per-sect, walk the matching units once.
            // Inner loop is small (at most 8 sects × number-of-factions),
            // so the cost per tick is dominated by the unit query.
            for (int sectIdx = 0; sectIdx < SectConfig.SectCount; sectIdx++)
            {
                string sectId = SectConfig.IdAt(sectIdx);
                var spec = SectLeverEffects.UnitOf(sectId);
                // Skip sects whose Unit-lever spec is empty / default.
                if (spec.DamageMultiplier == 0f && spec.ArmorBonus == 0 && spec.HpMultiplier == 0f) continue;

                ApplyForSect(ref state, em, sectId, spec);
            }
        }

        private static void ApplyForSect(ref SystemState state, EntityManager em,
            string sectId, in SectUnitLeverSpec spec)
        {
            // Stamp encodes (sectIndex << 4) | level so re-application on
            // upgrade is a simple inequality check.
            int sectIdx = SectConfig.IndexOf(sectId);
            if (sectIdx < 0) return;

            // Pass 1: never-stamped units of this sect.
            var pendingNew = new NativeList<Entity>(Allocator.Temp);
            var pendingNewLevels = new NativeList<byte>(Allocator.Temp);

            foreach (var (unit, faction, entity) in SystemAPI
                .Query<RefRO<UnitTag>, RefRO<FactionTag>>()
                .WithEntityAccess())
            {
                if (spec.AppliesToClass >= 0 && (int)unit.ValueRO.Class != spec.AppliesToClass)
                    continue;

                byte level = SectQuery.LevelOf(em, faction.ValueRO.Value,
                    sectId, SectLeverKind.Unit);
                if (level == 0) continue;

                // Existing stamp: skip if this sect already at the same or higher level.
                if (HasStampForSect(em, entity, sectIdx, out byte appliedLevel)
                    && appliedLevel >= level) continue;

                pendingNew.Add(entity);
                pendingNewLevels.Add(level);
            }

            for (int i = 0; i < pendingNew.Length; i++)
            {
                var e = pendingNew[i];
                if (!em.Exists(e)) continue;

                byte applied = HasStampForSect(em, e, sectIdx, out byte prev) ? prev : (byte)0;
                byte newLevel = pendingNewLevels[i];

                float scalar = SectLeverEffects.LevelScalar(newLevel)
                              / (applied > 0 ? SectLeverEffects.LevelScalar(applied) : 1f);

                ApplyDelta(em, e, spec, scalar);
                SetStampForSect(em, e, sectIdx, newLevel);
            }

            pendingNew.Dispose();
            pendingNewLevels.Dispose();
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
