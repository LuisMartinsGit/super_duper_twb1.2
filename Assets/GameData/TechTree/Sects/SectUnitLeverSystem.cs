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

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SectUnitLeverSystem : ISystem
    {
        // faction x sect Unit-lever level as of the LAST update, [faction*12 +
        // sect] (2026-09-25 perf pass). The old loop ran 12 sects x every unit
        // and resolved the level per (sect, unit) — a string IndexOf plus a
        // bank lookup each, i.e. ~12 x N lookups every tick for a number that
        // changes a handful of times per match.
        private const int MaxFactionSlots = 256;
        private NativeArray<byte> _lastLevel;
        private NativeArray<byte> _seen;
        private byte _primed;
        private EntityQuery _units;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UnitTag>();
            _lastLevel = new NativeArray<byte>(MaxFactionSlots * SectConfig.SectCount, Allocator.Persistent);
            _seen = new NativeArray<byte>(MaxFactionSlots, Allocator.Persistent);
            _units = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<UnitTag, FactionTag>()
                .Build(ref state);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_lastLevel.IsCreated) _lastLevel.Dispose();
            if (_seen.IsCreated) _seen.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // Which sects have a unit lever at all — resolved once per update.
            var specs = new NativeArray<SectUnitLeverSpec>(SectConfig.SectCount, Allocator.Temp);
            var hasSpec = new NativeArray<byte>(SectConfig.SectCount, Allocator.Temp);
            int specCount = 0;
            for (int s = 0; s < SectConfig.SectCount; s++)
            {
                var spec = SectLeverEffects.UnitOf(SectConfig.IdAt(s));
                specs[s] = spec;
                if (spec.DamageMultiplier == 0f && spec.ArmorBonus == 0 && spec.HpMultiplier == 0f) continue;
                hasSpec[s] = 1;
                specCount++;
            }

            // This update's level table, resolved lazily per faction (one bank
            // read gives all 12 sects). 255 = faction not resolved yet.
            var level = new NativeArray<byte>(MaxFactionSlots * SectConfig.SectCount, Allocator.Temp);
            var resolved = new NativeArray<byte>(MaxFactionSlots, Allocator.Temp);

            bool anyChanged = _primed == 0;
            for (int f = 0; f < MaxFactionSlots; f++)
            {
                if (_seen[f] == 0) continue;
                Resolve(em, level, resolved, (Faction)f);
                for (int s = 0; s < SectConfig.SectCount; s++)
                    if (level[f * SectConfig.SectCount + s] != _lastLevel[f * SectConfig.SectCount + s])
                        anyChanged = true;
            }

            if (specCount > 0)
            {
                // Only units in CHANGED chunks (new units, owner changes) need
                // a look — unless a level moved, which can newly qualify any
                // unit. Stamps make a revisit a no-op, so a spurious chunk
                // change costs time, never a double application.
                if (!anyChanged) _units.SetChangedVersionFilter(ComponentType.ReadOnly<FactionTag>());
                var unitEntities = _units.ToEntityArray(Allocator.Temp);
                var unitTags = _units.ToComponentDataArray<UnitTag>(Allocator.Temp);
                var unitFactions = _units.ToComponentDataArray<FactionTag>(Allocator.Temp);
                _units.ResetFilter();

                // Unit-major: each unit still receives its sects in 0..11
                // order, which is the only order that matters (per-unit HP /
                // damage truncation); units never affect each other.
                for (int i = 0; i < unitEntities.Length; i++)
                {
                    var entity = unitEntities[i];
                    var fac = unitFactions[i].Value;
                    int f = (int)fac;
                    Resolve(em, level, resolved, fac);
                    int cls = (int)unitTags[i].Class;

                    for (int s = 0; s < SectConfig.SectCount; s++)
                    {
                        if (hasSpec[s] == 0) continue;
                        var spec = specs[s];
                        if (spec.AppliesToClass >= 0 && cls != spec.AppliesToClass) continue;

                        byte lv = level[f * SectConfig.SectCount + s];
                        if (lv == 0) continue;
                        if (!em.Exists(entity)) break;

                        bool hasStamp = HasStampForSect(em, entity, s, out byte appliedLevel);
                        if (hasStamp && appliedLevel >= lv) continue;

                        float scalar = SectLeverEffects.LevelScalar(lv)
                                      / (appliedLevel > 0 ? SectLeverEffects.LevelScalar(appliedLevel) : 1f);

                        ApplyDelta(em, entity, spec, scalar);
                        SetStampForSect(em, entity, s, lv);
                    }
                }

                unitEntities.Dispose();
                unitTags.Dispose();
                unitFactions.Dispose();
            }

            for (int f = 0; f < MaxFactionSlots; f++)
            {
                if (resolved[f] == 0) continue;
                _seen[f] = 1;
                for (int s = 0; s < SectConfig.SectCount; s++)
                    _lastLevel[f * SectConfig.SectCount + s] = level[f * SectConfig.SectCount + s];
            }
            _primed = 1;

            specs.Dispose();
            hasSpec.Dispose();
            level.Dispose();
            resolved.Dispose();
        }

        /// <summary>Fill one faction's 12 Unit-lever levels, once per update.
        /// Same read SectQuery.LevelOf(..., Unit) makes, minus the per-call
        /// string IndexOf and bank lookup.</summary>
        private static void Resolve(EntityManager em, NativeArray<byte> level,
            NativeArray<byte> resolved, Faction faction)
        {
            int f = (int)faction;
            if (resolved[f] != 0) return;
            resolved[f] = 1;
            if (!SectQuery.TryGetAdoptionState(em, faction, out var adoption)) return;
            for (int s = 0; s < SectConfig.SectCount; s++)
                level[f * SectConfig.SectCount + s] = adoption.Get(s).LevelOf(SectLeverKind.Unit);
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
            // SectUnitLeverSpec uses the default(0) value for "unset" multipliers.
            // Skip both 0 (unset) and 1 (no-op) for HP / Damage so a sect that
            // only sets one axis doesn't accidentally zero the other on every
            // matching unit. (Previously: HpMultiplier=0 sect → unit HP × 0.)

            // HP — multiplicative on Max + Value.
            if (spec.HpMultiplier > 0.001f
                && math.abs(spec.HpMultiplier - 1f) > 0.001f
                && em.HasComponent<Health>(entity))
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
            if (spec.DamageMultiplier > 0.001f
                && math.abs(spec.DamageMultiplier - 1f) > 0.001f
                && em.HasComponent<Damage>(entity))
            {
                var dmg = em.GetComponentData<Damage>(entity);
                float diff = 1f + (spec.DamageMultiplier - 1f) * scalar;
                dmg.Value = (int)(dmg.Value * diff);
                em.SetComponentData(entity, dmg);
            }
        }
    }

}
