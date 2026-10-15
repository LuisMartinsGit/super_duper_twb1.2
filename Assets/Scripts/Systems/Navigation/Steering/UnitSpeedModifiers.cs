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
// loops (the integrator and the formation pass ran ~10 EntityManager calls
// per unit per tick through here, late-game that was a large slice of the
// integrator's 88 ms): component reads go through ComponentLookups and the
// three Feraldis research checks are memoised per faction for the tick.
// The two MUST stay result-identical — change one, change both.

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
        /// any main-thread structural change). Update also clears the research
        /// memo, so a tech completed between two systems is seen by the next.
        /// </summary>
        public struct Lookups
        {
            internal ComponentLookup<FeraldisUnitTag> Feraldis;
            internal ComponentLookup<BerserkBuff> Berserk;
            internal ComponentLookup<SprintClock> Sprint;
            internal ComponentLookup<BloodFrenzy> Frenzy;
            internal ComponentLookup<SiegeTag> Siege;
            internal ComponentLookup<SpellDebuff> Debuff;
            internal ComponentLookup<BorderDebuff> Border;
            internal ComponentLookup<Fortified> Fort;
            internal ComponentLookup<SpellBuff> Buff;
            internal ComponentLookup<FactionTag> Faction;

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
                };
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
                System.Array.Clear(s_researchMemo, 0, s_researchMemo.Length);
            }
        }

        // Research memo: [tech * 256 + faction] = 0 unknown, 1 no, 2 yes.
        // Research completes on the main thread between systems, never inside
        // one, so a per-Update memo answers exactly what HasResearched would.
        private const int TechNoRetreat = 0, TechWarpathSprint = 1, TechBloodBoilers = 2;
        private static readonly string[] s_techIds =
            { "Feraldis_NoRetreat", "Feraldis_WarpathSprint", "Feraldis_BloodBoilers" };
        private static readonly sbyte[] s_researchMemo = new sbyte[3 * 256];

        /// <summary>FeraldisCombat.Researched(em, e, tech), memoised per faction.</summary>
        private static bool Researched(ref Lookups lk, Entity e, int tech)
        {
            if (!lk.Faction.HasComponent(e)) return false;
            int f = (int)lk.Faction[e].Value;
            int slot = tech * 256 + (f & 0xFF);
            sbyte v = s_researchMemo[slot];
            if (v == 0)
            {
                v = TheWaningBorder.Feraldis.FeraldisCombat.Researched(
                        (Faction)f, s_techIds[tech]) ? (sbyte)2 : (sbyte)1;
                s_researchMemo[slot] = v;
            }
            return v == 2;
        }

        /// <summary>Lookup twin of <see cref="Multiplier(EntityManager, Entity)"/>
        /// — same reads in the same order, same short-circuits.</summary>
        public static float Multiplier(ref Lookups lk, Entity e)
        {
            float m = SpeedMult(ref lk, e);
            if (TransientState.Active(ref lk.Debuff, e) && !IgnoresSlows(ref lk, e))
                m *= 1f - lk.Debuff[e].SpeedReduction;
            if (lk.Border.HasComponent(e))
            {
                var bd = lk.Border[e];
                if (bd.SpeedPenalty > 0f)
                    m *= 1f - math.min(0.9f, bd.SpeedPenalty);
            }
            if (TransientState.Active(ref lk.Fort, e)) return 0f;
            if (TransientState.Active(ref lk.Buff, e))
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

        // Mirrors FeraldisCombat.SpeedMult (Cfg only touched for Feraldis
        // units, exactly as there).
        private static float SpeedMult(ref Lookups lk, Entity e)
        {
            if (!lk.Feraldis.HasComponent(e)) return 1f;
            float m = 1f;
            var c = TheWaningBorder.Feraldis.FeraldisCombat.Cfg;
            if (lk.Berserk.HasComponent(e)) m *= c.berserkSpeedMultiplier;
            if (lk.Sprint.HasComponent(e)
                && lk.Sprint[e].OutOfCombat >= c.warpathSprintOutOfCombatSeconds
                && Researched(ref lk, e, TechWarpathSprint))
                m *= c.warpathSprintSpeed;
            if (lk.Frenzy.HasComponent(e) && lk.Siege.HasComponent(e)
                && Researched(ref lk, e, TechBloodBoilers))
                m *= c.bloodBoilersSpeedMultiplier;
            return m;
        }
    }
}
