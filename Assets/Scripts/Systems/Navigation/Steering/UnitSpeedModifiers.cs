// UnitSpeedModifiers.cs
// The multiplier UnitIntegratorSystem applies on top of a unit's base (or
// formation-override) speed: spell slows, veil / Suppression drag, Fortify,
// timed haste. Lifted out of the integrator so FormationGroupSystem can size
// the group speed from what its members can ACTUALLY do — a formation whose
// speed ignores a 40 % slow on one member leaves that member behind for the
// whole march (docs/Design/Navigation_And_Formations.md §2.10).
//
// Two entry points, ONE rule. The EntityManager overload is the original
// and stays for one-off callers. The Lookups overload is for the per-unit
// loops, and is BURST-SAFE (2026-10-15): component reads go through
// ComponentLookups, and the managed inputs (the FeraldisCombat config SO and
// the three Feraldis research checks) are resolved once per Update on the
// main thread into plain fields and a NativeArray table, so the overload runs
// inside the integrator's and the formation pass's Burst jobs.
// The two MUST stay result-identical — change one, change both.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Navigation
{
    public static class UnitSpeedModifiers
    {
        /// <summary>Product of every speed effect on <paramref name="e"/>.
        /// 0 when it cannot move at all (Fortified).</summary>
        public static float Multiplier(EntityManager em, Entity e)
        {
            float m = TheWaningBorder.Feraldis.FeraldisCombat.SpeedMult(em, e);
            // No Retreat (Feraldis): a frenzied unit cannot be slowed.
            if (TransientState.Active<SpellDebuff>(em, e)
                && !TheWaningBorder.Feraldis.FeraldisCombat.IgnoresSlows(em, e))
                m *= 1f - em.GetComponentData<SpellDebuff>(e).SpeedReduction;
            // The Veil / Suppression auras: BorderDebuff.SpeedPenalty was
            // authored but never consumed — units wading through veil crust
            // (or a Suppression field) now actually slow down.
            if (em.HasComponent<BorderDebuff>(e))
            {
                var bd = em.GetComponentData<BorderDebuff>(e);
                if (bd.SpeedPenalty > 0f)
                    m *= 1f - math.min(0.9f, bd.SpeedPenalty);
            }
            if (TransientState.Active<Fortified>(em, e)) return 0f;
            if (TransientState.Active<SpellBuff>(em, e))
            {
                var buff = em.GetComponentData<SpellBuff>(e);
                if (buff.SpeedMultiplier > 0f && buff.SpeedMultiplier != 1f)
                    m *= buff.SpeedMultiplier;
            }
            return m;
        }

        /// <summary>
        /// Lookup set for the per-unit overload. Create once in OnCreate,
        /// <see cref="Update"/> at the top of every OnUpdate (and again after
        /// any main-thread structural change), <see cref="Dispose"/> in
        /// OnDestroy. A plain job field: every container in it is [ReadOnly].
        /// Research completes on the main thread between systems, never inside
        /// one, so the per-Update table answers exactly what HasResearched would.
        /// </summary>
        public struct Lookups
        {
            [ReadOnly] internal ComponentLookup<FeraldisUnitTag> Feraldis;
            [ReadOnly] internal ComponentLookup<BerserkBuff> Berserk;
            [ReadOnly] internal ComponentLookup<SprintClock> Sprint;
            [ReadOnly] internal ComponentLookup<BloodFrenzy> Frenzy;
            [ReadOnly] internal ComponentLookup<SiegeTag> Siege;
            [ReadOnly] internal ComponentLookup<SpellDebuff> Debuff;
            [ReadOnly] internal ComponentLookup<BorderDebuff> Border;
            [ReadOnly] internal ComponentLookup<Fortified> Fort;
            [ReadOnly] internal ComponentLookup<SpellBuff> Buff;
            [ReadOnly] internal ComponentLookup<FactionTag> Faction;

            /// <summary>[tech * ResearchFactions + faction] = 1 when researched.</summary>
            [ReadOnly] internal NativeArray<byte> Research;

            // FeraldisCombat.Cfg, copied in Update — only while a Feraldis unit
            // exists, which is exactly when the old path touched the config,
            // so an edition stripped of the Feraldis data never asks for it.
            internal float BerserkSpeed;
            internal float SprintSeconds;
            internal float SprintSpeed;
            internal float BloodBoilersSpeed;

            public static Lookups Create(ref SystemState state)
            {
                return new Lookups
                {
                    Feraldis = state.GetComponentLookup<FeraldisUnitTag>(true),
                    Berserk = state.GetComponentLookup<BerserkBuff>(true),
                    Sprint = state.GetComponentLookup<SprintClock>(true),
                    Frenzy = state.GetComponentLookup<BloodFrenzy>(true),
                    Siege = state.GetComponentLookup<SiegeTag>(true),
                    Debuff = state.GetComponentLookup<SpellDebuff>(true),
                    Border = state.GetComponentLookup<BorderDebuff>(true),
                    Fort = state.GetComponentLookup<Fortified>(true),
                    Buff = state.GetComponentLookup<SpellBuff>(true),
                    Faction = state.GetComponentLookup<FactionTag>(true),
                    Research = new NativeArray<byte>(TechCount * ResearchFactions, Allocator.Persistent),
                };
            }

            public void Dispose()
            {
                if (Research.IsCreated) Research.Dispose();
            }

            public void Update(ref SystemState state)
            {
                Feraldis.Update(ref state);
                Berserk.Update(ref state);
                Sprint.Update(ref state);
                Frenzy.Update(ref state);
                Siege.Update(ref state);
                Debuff.Update(ref state);
                Border.Update(ref state);
                Fort.Update(ref state);
                Buff.Update(ref state);
                Faction.Update(ref state);

                // The table and config are only ever read for a FeraldisUnitTag
                // unit (SpeedMult and IgnoresSlows both test it first), so with
                // none alive they are never consulted and need no refresh.
                var feraldisQ = state.GetEntityQuery(ComponentType.ReadOnly<FeraldisUnitTag>());
                if (feraldisQ.IsEmptyIgnoreFilter) return;

                for (int t = 0; t < TechCount; t++)
                    for (int f = 0; f < ResearchFactions; f++)
                        Research[t * ResearchFactions + f] =
                            TheWaningBorder.Feraldis.FeraldisCombat.Researched((Faction)f, TechIds.Ids[t])
                                ? (byte)1 : (byte)0;

                var c = TheWaningBorder.Feraldis.FeraldisCombat.Cfg;
                BerserkSpeed = c.berserkSpeedMultiplier;
                SprintSeconds = c.warpathSprintOutOfCombatSeconds;
                SprintSpeed = c.warpathSprintSpeed;
                BloodBoilersSpeed = c.bloodBoilersSpeedMultiplier;
            }
        }

        // Research table: [tech * ResearchFactions + faction]. Faction is a
        // byte enum, but only the player slots (0..7) and the Border (8) ever
        // carry a FactionTag; 16 covers them with room to spare, and anything
        // past it reads "not researched".
        private const int TechNoRetreat = 0, TechWarpathSprint = 1, TechBloodBoilers = 2;
        private const int TechCount = 3;
        private const int ResearchFactions = 16;

        /// <summary>FeraldisCombat.Researched(em, e, tech), from the per-Update table.</summary>
        private static bool Researched(ref Lookups lk, Entity e, int tech)
        {
            if (!lk.Faction.HasComponent(e)) return false;
            int f = (int)lk.Faction[e].Value;
            if (f < 0 || f >= ResearchFactions) return false;
            return lk.Research[tech * ResearchFactions + f] != 0;
        }

        /// <summary>Lookup twin of <see cref="Multiplier(EntityManager, Entity)"/>
        /// — same reads in the same order, same short-circuits. Burst-safe.</summary>
        public static float Multiplier(ref Lookups lk, Entity e)
        {
            float m = SpeedMult(ref lk, e);
            if (lk.Debuff.HasComponent(e) && lk.Debuff.IsComponentEnabled(e) && !IgnoresSlows(ref lk, e))
                m *= 1f - lk.Debuff[e].SpeedReduction;
            if (lk.Border.HasComponent(e))
            {
                var bd = lk.Border[e];
                if (bd.SpeedPenalty > 0f)
                    m *= 1f - math.min(0.9f, bd.SpeedPenalty);
            }
            if (lk.Fort.HasComponent(e) && lk.Fort.IsComponentEnabled(e)) return 0f;
            if (lk.Buff.HasComponent(e) && lk.Buff.IsComponentEnabled(e))
            {
                var buff = lk.Buff[e];
                if (buff.SpeedMultiplier > 0f && buff.SpeedMultiplier != 1f)
                    m *= buff.SpeedMultiplier;
            }
            return m;
        }

        // Mirrors FeraldisCombat.IgnoresSlows.
        private static bool IgnoresSlows(ref Lookups lk, Entity e)
            => lk.Frenzy.HasComponent(e) && lk.Feraldis.HasComponent(e)
               && Researched(ref lk, e, TechNoRetreat);

        // Mirrors FeraldisCombat.SpeedMult (config only read for Feraldis
        // units, exactly as there).
        private static float SpeedMult(ref Lookups lk, Entity e)
        {
            if (!lk.Feraldis.HasComponent(e)) return 1f;
            float m = 1f;
            if (lk.Berserk.HasComponent(e)) m *= lk.BerserkSpeed;
            if (lk.Sprint.HasComponent(e)
                && lk.Sprint[e].OutOfCombat >= lk.SprintSeconds
                && Researched(ref lk, e, TechWarpathSprint))
                m *= lk.SprintSpeed;
            if (lk.Frenzy.HasComponent(e) && lk.Siege.HasComponent(e)
                && Researched(ref lk, e, TechBloodBoilers))
                m *= lk.BloodBoilersSpeed;
            return m;
        }

        // Managed tech ids, kept in their own class so no Burst-compiled
        // method's declaring type carries a managed static initializer.
        private static class TechIds
        {
            internal static readonly string[] Ids =
                { "Feraldis_NoRetreat", "Feraldis_WarpathSprint", "Feraldis_BloodBoilers" };
        }
    }
}
