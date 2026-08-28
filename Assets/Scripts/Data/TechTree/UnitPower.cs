// UnitPower.cs
// The Power number: one statistic per unit, for comparing units against each
// other and balancing them. docs/Design/Unit_Power.md.
//
// PURELY DERIVED. Every input is a stat the unit already carries in its SO, so
// there is nothing to author and nothing that can contradict the unit — retune
// a cost or a cooldown and the Power number moves with it the same frame. It is
// deliberately NOT a field on UnitDefSO: an authored power rating is a second
// opinion about a unit that immediately starts disagreeing with the first.
//
// WHAT IT MEASURES: combat output per resource invested. Cost has to be in it
// or the number cannot answer the question balance actually asks — a Trebuchet
// out-fights a Spearman and tells you nothing; a Trebuchet that out-fights a
// Spearman by less than it costs is the finding. Roughly 100 is par for the
// Age 0 + Alanthor roster, so 50 reads as "half the value for the money" at a
// glance and the eye does the work instead of arithmetic.
//
// WHAT IT IS NOT: a prediction of who wins a fight. It has no idea about
// counters (bonusVsTags), formations, terrain, micro or numbers, and a unit
// whose whole job is one of those will score badly while being essential.
// Treat an outlier as a question, not a verdict.

using System;

namespace TheWaningBorder.Data
{
    /// <summary>The Power number and the parts it is built from, so an odd
    /// result can be read rather than merely disbelieved.</summary>
    public struct UnitPowerBreakdown
    {
        /// <summary>Sustained damage per second, aim time included.</summary>
        public float Dps;
        /// <summary>HP after the unit's own damage reduction.</summary>
        public float EffectiveHp;
        /// <summary>Offence and durability combined, with reach and AoE.</summary>
        public float Combat;
        /// <summary>Cost and training time on one scale.</summary>
        public float Investment;
        /// <summary>Combat per investment, scaled so ~100 is par.</summary>
        public float Power;
        /// <summary>False when the unit has no combat or support output at all
        /// (a Scout, a Ledger). Those are not weak units, they are units this
        /// metric does not measure — reporting 0 for them would be a lie
        /// dressed as a number.</summary>
        public bool Measurable;
    }

    public static class UnitPower
    {
        // ── Resource weights ─────────────────────────────────────────────
        // Each tier is scarcer than the last, so a point of it is worth more.
        // Supplies are the unit, and the ladder doubles: a territory pays
        // supplies for free and iron / veilstone only where the map put a node
        // (docs/Design/Regions.md §4), and veilsteel is rarer still.
        private const float IronWeight = 2f;
        private const float VeilstoneWeight = 4f;
        private const float VeilsteelWeight = 8f;

        /// <summary>Supplies-equivalent of one second of training. A unit also
        /// costs the BUILDING that made it, for as long as it was in there —
        /// leaving that out makes a slow, cheap unit look free.</summary>
        private const float TimeWeight = 2f;

        /// <summary>Floor on the attack cycle. Guards the division, and a zero
        /// cooldown in a SO means "unset", not "infinite rate".</summary>
        private const float MinCycle = 0.5f;

        /// <summary>Metres of reach per +100% of the reach bonus. Range is
        /// survivability you do not pay for in HP: a longbow that never gets
        /// hit is worth more than its health bar says.</summary>
        private const float ReachDivisor = 40f;

        /// <summary>Metres of AoE radius per +100% of effective damage.</summary>
        private const float AoeDivisor = 4f;

        /// <summary>
        /// The hit armor is measured against. Armor is SUBTRACTED
        /// (docs/Design/Combat_Pacing.md § Armor), so "how much is this armor
        /// worth" has no answer without saying worth against WHAT — 4 armor
        /// halves an 8-damage arrow and is nothing against a trebuchet.
        ///
        /// 12 is the roster's median attack, so a unit's durability score is
        /// "how long it survives the average thing shooting at it". A single
        /// reference attack is a simplification, and it is the honest one: the
        /// alternative is a metric that silently assumes every unit is fighting
        /// the unit that most flatters it.
        /// </summary>
        private const float ReferenceAttack = 12f;

        /// <summary>
        /// Scale constant. Chosen so the Age 0 + Alanthor roster's MEDIAN sits
        /// at ~100 as of 2026-08-28 (re-tuned with the armor pass, which raised every
        /// armoured unit's durability). It is a readability constant and nothing
        /// else — it moves every unit together and so can never change a
        /// comparison. Re-tune it if the roster drifts far enough that the
        /// numbers stop reading as percentages of par.
        /// </summary>
        private const float Scale = 819f;

        /// <summary>The Power number, or 0 for a unit the metric cannot
        /// measure. Use <see cref="Breakdown"/> when you need to tell those
        /// two apart.</summary>
        public static float Of(UnitDef def) => Breakdown(def).Power;

        public static UnitPowerBreakdown Breakdown(UnitDef def)
        {
            var b = new UnitPowerBreakdown();
            if (def == null) return b;

            // ── Offence ──────────────────────────────────────────────────
            // Aim time is part of the cycle, not an alternative to cooldown:
            // an archer that winds up for 0.5 s and then waits 1.5 s fires
            // every 2 s, and treating those as competing floors flattered every
            // ranged unit in the roster.
            float cycle = Math.Max(def.attackCooldown + def.aimTime, MinCycle);
            b.Dps = def.damage > 0f ? def.damage / cycle : 0f;

            // Splash multiplies what each shot is worth.
            float aoe = 1f + Math.Max(0f, def.aoeRadius) / AoeDivisor;

            // Support output on the same scale as damage: a point of healing is
            // a point of damage undone, and a builder's throughput is what it
            // contributes to the fight it is not in.
            float utility = Math.Max(0f, def.healsPerSecond)
                          + Math.Max(0f, def.buildSpeed) * 0.5f;

            float offence = b.Dps * aoe + utility;

            // ── Durability ───────────────────────────────────────────────
            // Armor is SUBTRACTED from each hit, so N armor multiplies how long
            // you live by referenceAttack / (referenceAttack - armor) — not by
            // some percentage of your health bar. This used to read the
            // Defense component's stale comment and compute (d + 100) / 100,
            // which valued 5 armor at +5% when it is really +71% against the
            // median attack. Every durability number the metric produced before
            // 2026-08-28 was wrong by that much.
            //
            // Averaged across the four damage types because the metric does not
            // know what the unit will be shot by.
            float avgDef = def.defense == null ? 0f
                : (def.defense.melee + def.defense.ranged
                   + def.defense.siege + def.defense.magic) / 4f;
            // Clamped so armor at or above the reference attack does not divide
            // by zero or go negative. The floor is the game's own minimum-damage
            // rule: a hit always lands for at least 1.
            float perHit = Math.Max(1f, ReferenceAttack - Math.Max(0f, avgDef));
            b.EffectiveHp = Math.Max(1f, def.hp) * (ReferenceAttack / perHit);

            if (offence <= 0f)
            {
                // No output of any kind. Not measurable — see the field docs.
                b.Investment = InvestmentOf(def);
                return b;
            }

            // ── Combat ───────────────────────────────────────────────────
            // Geometric mean, not a product or a sum. A glass cannon and a
            // damage sponge should both score like the mid-range unit they beat
            // and lose to respectively; a product lets one dimension run away
            // with the number, and a sum lets a unit with no offence at all
            // still look like a fighter because it has health.
            float reach = 1f + Math.Max(def.attackRange, def.siegeRange) / ReachDivisor;
            b.Combat = (float)Math.Sqrt(offence * b.EffectiveHp) * reach;

            b.Investment = InvestmentOf(def);
            b.Power = Scale * b.Combat / b.Investment;
            b.Measurable = true;
            return b;
        }

        /// <summary>Cost and training time on one supplies-equivalent scale.</summary>
        public static float InvestmentOf(UnitDef def)
        {
            float c = 0f;
            if (def?.cost != null)
                c = def.cost.Supplies
                  + def.cost.Iron * IronWeight
                  + def.cost.Veilstone * VeilstoneWeight
                  + def.cost.Veilsteel * VeilsteelWeight;
            c += Math.Max(0f, def?.trainingTime ?? 0f) * TimeWeight;
            return Math.Max(1f, c);
        }
    }
}
