// DamageOverTime.cs
// The one set of rules every damage-over-time path obeys. Canon:
// docs/Design/Fire.md, "The damage-over-time contract".
//
// Before this, each DOT carried its own private copy of "subtract from
// Health", and they had drifted: bleeding, a burning building and curse
// exposure ignored Invulnerable and never ran the Liquid Courage scaling;
// burning ground scaled but floored first, so a reduced tick became 0. Now:
//
//   * Invulnerable            -> no damage (and a fractional DOT stops accruing)
//   * FireImmune (fire only)  -> no damage from fire
//   * SpellBuff reductions    -> scale the RATE, so a fraction carries over
//   * shield                  -> ShieldBar points are spent before Health
//                                (Combat_Pacing.md, shield points are hit points)
//   * LifeCling / Second Wind -> HP floor held, like SelfDoT in
//                                AbilityLifecycleSystem (DeathSystem is still
//                                the backstop for every other source)
//   * kill credit             -> LastDamagedByFaction, only when the source is
//                                hostile to the victim (your own fire killing
//                                your own unit credits nobody)
//
// Every call is a plain EntityManager read/write on non-structural data, so
// it is safe inside a SystemAPI.Query iteration. Deterministic: no clocks, no
// randomness, identical float math on every peer.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Abilities;

namespace TheWaningBorder.Systems.Combat
{
    public static class DamageOverTime
    {
        /// <summary>Fraction of an incoming DOT the victim actually takes:
        /// 0 when Invulnerable (or FireImmune, for fire), the SpellBuff
        /// reduction when one is active, otherwise 1.</summary>
        public static float IncomingScale(EntityManager em, Entity victim, bool isFire)
        {
            if (victim == Entity.Null || !em.Exists(victim)) return 0f;
            if (TransientState.Active<Invulnerable>(em, victim)) return 0f;
            if (isFire && em.HasComponent<FireImmune>(victim)) return 0f;
            return AbilityDamageHooks.IncomingMultiplier(em, victim);
        }

        /// <summary>Accrue a fractional amount (rate x dt) into the carrier's
        /// accumulator after scaling, and return the whole points now due.
        /// The fraction survives between calls, so a 90%-reduced bleed still
        /// lands one point every few seconds instead of rounding to nothing.</summary>
        public static int Accrue(EntityManager em, Entity victim, bool isFire,
            float amount, ref float accumulator)
        {
            float s = IncomingScale(em, victim, isFire);
            if (s <= 0f || amount <= 0f) return 0;
            accumulator += amount * s;
            int whole = (int)accumulator;
            if (whole > 0) accumulator -= whole;
            return whole;
        }

        /// <summary>Scale one whole tick (for DOTs with no per-victim
        /// accumulator, e.g. a 1 s burning-ground pulse). Floors AFTER
        /// scaling and keeps a positive tick at least 1 — 0 only when the
        /// victim is immune.</summary>
        public static int ScaleTick(EntityManager em, Entity victim, bool isFire, float amount)
        {
            float s = IncomingScale(em, victim, isFire);
            if (s <= 0f || amount <= 0f) return 0;
            return math.max(1, (int)(amount * s));
        }

        /// <summary>Subtract already-scaled whole damage from the victim's
        /// shield first (ShieldDamage.Absorb) and the overflow from
        /// <paramref name="hp"/>, holding LifeCling / Second Wind floors, and
        /// record kill credit for a hostile source. Returns the hit points
        /// actually removed, shield points included.</summary>
        public static int Commit(EntityManager em, Entity victim, ref Health hp,
            int damage, bool hasSource, Faction source)
        {
            if (damage <= 0 || hp.Value <= 0) return 0;

            // Shield points are hit points (Combat_Pacing.md): the shield
            // pays first, only the overflow reaches Health and its floors.
            // A fully absorbed hit still counts as landed (kill credit).
            int toHealth = ShieldDamage.Absorb(em, victim, damage);
            int shielded = damage - toHealth;
            damage = toHealth;

            int floor = 0;
            if (TransientState.Active<LifeCling>(em, victim))
                floor = math.max(floor, em.GetComponentData<LifeCling>(victim).Floor);
            if (TransientState.Active<SectDeathWard>(em, victim))
                floor = math.max(floor, 1);
            // A floor never HEALS: an entity already under it stays where it is.
            floor = math.min(floor, hp.Value);

            int before = hp.Value;
            hp.Value = math.max(floor, hp.Value - damage);
            int applied = shielded + (before - hp.Value);

            if (applied > 0 && hasSource && em.HasComponent<LastDamagedByFaction>(victim))
            {
                bool hostile = !em.HasComponent<FactionTag>(victim)
                    || Alliances.AreHostile(source, em.GetComponentData<FactionTag>(victim).Value);
                if (hostile)
                {
                    em.SetComponentData(victim, new LastDamagedByFaction { Value = source });
                    em.SetComponentEnabled<LastDamagedByFaction>(victim, true);
                }
            }
            return applied;
        }
    }
}
