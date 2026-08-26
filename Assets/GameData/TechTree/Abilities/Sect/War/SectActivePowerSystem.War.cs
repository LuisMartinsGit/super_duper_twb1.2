// File: Assets/GameData/TechTree/Abilities/Sect/SectActivePowerSystem.War.cs
// Effect bodies for the Sect of War's canon kinds (docs/Design/Sects.md
// section 6). The dispatch switch lives in SectActivePowerSystem.cs; this file
// is only the bodies, matching the Alanthor split.
//
// Blood Rain is the one power in the game whose effect is not bounded by its
// cast circle. Only the blood POOL it leaves is local; the haste and the
// silence are map-wide and side-blind, so the queries below deliberately skip
// the Alliances filter every other power applies. That is the design, not an
// oversight: Blood Rain converts the whole match into a weapons fight, and the
// caster pays for it by being silenced along with the enemy.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    public static partial class SectActivePowerHelper
    {
        // -- War --------------------------------------------------------------

        // Blood Rain's silence is polled from UI and per-cast code paths, so the
        // query is hoisted rather than rebuilt (managed queries created per call
        // leak native memory - see the sweep of 2026-07-16).
        private static readonly ComponentType[] SilenceTypes =
            { ComponentType.ReadWrite<SectGlobalSilence>() };
        private static TheWaningBorder.Core.CachedEntityQuery _silenceQuery;

        /// <summary>
        /// Blood Rain. Three effects from one cast:
        ///   1. a blood pool at the target, sized by the power's reach - real
        ///      Feraldis terrain that feeds Frenzy and can host a War Totem;
        ///   2. <paramref name="haste"/> attack speed for EVERY unit on the
        ///      map, allied and hostile alike;
        ///   3. a map-wide caster lockout for the same duration.
        /// </summary>
        private static void ApplyBloodRain(EntityManager em, Faction faction,
            float3 center, float radius, float haste, float duration)
        {
            // 1. The local half - a pool of real blood on real ground.
            var pool = em.CreateEntity(typeof(BloodPool), typeof(LocalTransform));
            em.SetComponentData(pool, new BloodPool
            {
                Radius        = radius,
                TimeRemaining = duration,
            });
            em.SetComponentData(pool, LocalTransform.FromPositionRotationScale(
                center, quaternion.identity, 1f));

            // 2. Map-wide haste. No radius test and no faction test - every
            //    unit that can swing a weapon swings faster.
            if (haste > 1f)
            {
                var query = em.CreateEntityQuery(
                    ComponentType.ReadOnly<UnitTag>(),
                    ComponentType.ReadOnly<FactionTag>());
                using var entities = query.ToEntityArray(Allocator.Temp);
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                for (int i = 0; i < entities.Length; i++)
                {
                    var e = entities[i];
                    var stamp = new SectHaste { Multiplier = haste, TimeRemaining = duration };
                    // A stronger or longer Blood Rain wins on both axes; a
                    // weaker one landing mid-effect must not cut the first short.
                    if (em.HasComponent<SectHaste>(e))
                    {
                        var cur = em.GetComponentData<SectHaste>(e);
                        stamp.Multiplier    = math.max(cur.Multiplier, haste);
                        stamp.TimeRemaining = math.max(cur.TimeRemaining, duration);
                        em.SetComponentData(e, stamp);
                    }
                    else ecb.AddComponent(e, stamp);
                }
                ecb.Playback(em);
                ecb.Dispose();
            }

            // 3. Map-wide silence. One singleton for the whole match: a second
            //    Blood Rain landing mid-lockout extends it rather than adding a
            //    second entity nobody would ever find to tick down.
            ApplyGlobalSilence(em, faction, duration);
        }

        /// <summary>
        /// Raise (or extend) Blood Rain's map-wide caster lockout. Public so the
        /// AI and any future silence effect share one code path.
        /// </summary>
        public static void ApplyGlobalSilence(EntityManager em, Faction source, float duration)
        {
            var q = _silenceQuery.Get(em, SilenceTypes);
            using var existing = q.ToEntityArray(Allocator.Temp);
            if (existing.Length > 0)
            {
                var s = em.GetComponentData<SectGlobalSilence>(existing[0]);
                // Extend, never shorten - a level-I Blood Rain must not cut a
                // level-III lockout short.
                if (duration > s.TimeRemaining)
                {
                    s.TimeRemaining = duration;
                    s.Source        = source;
                    em.SetComponentData(existing[0], s);
                }
                return;
            }

            var e = em.CreateEntity(typeof(SectGlobalSilence));
            em.SetComponentData(e, new SectGlobalSilence
            {
                TimeRemaining = duration,
                Source        = source,
            });
        }

        /// <summary>
        /// True while Blood Rain's lockout stands. Both cast gates
        /// (SectActivePowerHelper.Fire and AbilityLifecycleSystem) consult it.
        /// </summary>
        public static bool IsGloballySilenced(EntityManager em)
        {
            var q = _silenceQuery.Get(em, SilenceTypes);
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<SectGlobalSilence>(ents[i]).TimeRemaining > 0f)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Call to Arms. Stamps SectTrainingBoon on the caster's own military
        /// buildings in the circle - anything with a TrainingState, which is
        /// exactly "a building that trains units".
        /// </summary>
        private static void ApplyCallToArms(EntityManager em, Faction faction,
            float3 center, float radius, float costMultiplier, float duration, byte level)
        {
            bool single = IsSingleTarget(radius);
            float r2 = radius * radius;
            float speed = level >= 3 ? SectLeverEffects.CallToArmsSpeedLv3 : 1f;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<TrainingState>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                // Own buildings only. Allies get their own Call to Arms - a
                // shared cost cut would double-dip on team resources.
                if (em.GetComponentData<FactionTag>(e).Value != faction) continue;

                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz > r2) continue;

                var boon = new SectTrainingBoon
                {
                    TimeRemaining   = duration,
                    CostMultiplier  = costMultiplier,
                    SpeedMultiplier = speed,
                };
                if (em.HasComponent<SectTrainingBoon>(e)) em.SetComponentData(e, boon);
                else ecb.AddComponent(e, boon);

                if (single) break;
            }

            ecb.Playback(em);
            ecb.Dispose();
        }

        /// <summary>
        /// Bloodfury III - damage and flat armor in one buff. Slots I and II
        /// use the plain DamageCircle path; only the level-III spec routes here.
        /// </summary>
        private static void ApplyDamageArmorCircle(EntityManager em, Faction faction,
            float3 center, float radius, float damageMultiplier, float duration)
        {
            ApplyCircleBuff(em, faction, center, radius, new SpellBuff
            {
                DamageMultiplier = damageMultiplier,
                ArmorBonus       = SectLeverEffects.BloodfuryArmorLv3,
                TimeRemaining    = duration,
            });
        }
    }
}
