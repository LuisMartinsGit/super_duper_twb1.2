using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="AITactics"/> and
    /// <see cref="AITacticsMicroSystem"/>. The asset is AITactics.asset,
    /// beside AITactics.cs. WHICH of these behaviours a tier uses, and its
    /// thresholds, are per-difficulty (<see cref="AITacticsSkill"/> on the
    /// profile assets); these are the shared geometry and weights.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/AITactics",
                     fileName = "AITactics")]
    public sealed class AITacticsConfig : ScriptableObject, IComponentConfig
    {
        static AITacticsConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static AITacticsConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AITacticsConfig>());

        // ── Cadence ──────────────────────────────────────────────────────

        /// <summary>Seconds between kiting passes for one AI faction.
        /// Factions are phase-staggered across this window.</summary>
        public float microInterval;

        /// <summary>Seconds between ability-value passes for one AI faction.</summary>
        public float abilityInterval;

        // ── Kiting ───────────────────────────────────────────────────────

        /// <summary>A hostile melee unit this close (edge to edge) to a
        /// reloading ranged unit makes it step back.</summary>
        public float kiteTriggerDistance;

        /// <summary>How far one kiting step backs off.</summary>
        public float kiteStepDistance;

        /// <summary>Kite only with at least this much reload left — a shot
        /// that is nearly ready is worth more than the step.</summary>
        public float kiteMinReloadSeconds;

        /// <summary>Kite only when my speed is at least this multiple of the
        /// melee unit's — a slower shooter cannot open distance.</summary>
        public float kiteSpeedRatio;

        /// <summary>Furthest a unit may kite from where it first kited in
        /// this fight. Past it, it stands and shoots (the stance leash
        /// still owns everything else).</summary>
        public float kiteMaxDrift;

        /// <summary>A unit whose last kiting step is older than this starts
        /// a fresh drift allowance (a new fight).</summary>
        public float kiteAnchorResetSeconds;

        /// <summary>Most kiting orders one pass may issue (per faction).</summary>
        public int kiteMaxPerTick;

        // ── Target priority (focus fire + counters) ─────────────────────

        /// <summary>Weight of a candidate's damage output.</summary>
        public float dangerScale;

        /// <summary>Weight per missing HP point ("finish it"), scaled by the
        /// tier's focusFireWeight.</summary>
        public float finishScale;

        /// <summary>Penalty per max-HP point (fragile first).</summary>
        public float fragileScale;

        /// <summary>Penalty per metre from the army centroid.</summary>
        public float distanceScale;

        /// <summary>Score per point of the candidate's bonus damage against
        /// MY units' tags (it counters me — kill it first), scaled by the
        /// tier's counterTargetWeight.</summary>
        public float counterThreatScale;

        /// <summary>Score per point of MY bonus damage against the
        /// candidate's tags (I counter it), scaled by counterTargetWeight.</summary>
        public float counterEdgeScale;

        /// <summary>Bonus for a candidate that prefers heroes (TargetPreference
        /// Hero) while my army has one, scaled by counterTargetWeight.</summary>
        public float heroThreatBonus;

        /// <summary>Bonus for a high-value candidate (hero, siege, support,
        /// caster), scaled by focusFireWeight.</summary>
        public float highValueBonus;

        /// <summary>How many of the best army-scored candidates each member
        /// chooses among — the concentration of the focus.</summary>
        public int focusTopK;

        /// <summary>Per-member penalty per metre from the member itself.</summary>
        public float memberDistanceScale;

        /// <summary>A member keeps its current target unless the new pick
        /// scores this much better (stops re-ordering every tick).</summary>
        public float switchMargin;

        // ── Ranged behind melee ─────────────────────────────────────────

        /// <summary>A ranged member takes a target only within its own range
        /// plus this slack; otherwise it holds the firing line.</summary>
        public float rangedReachSlack;

        /// <summary>How far behind the melee front the firing line stands.</summary>
        public float rangedStandoff;

        /// <summary>A ranged member already this close to its firing-line
        /// spot is left alone.</summary>
        public float rangedLineTolerance;

        /// <summary>Spacing between shooters along the firing line.</summary>
        public float rangedLineSpacing;

        /// <summary>Shooters per row of the firing line before the next one
        /// stands on the same spots (array of spots, not tuning per unit).</summary>
        public int rangedLineSlots;

        // ── Flanking ─────────────────────────────────────────────────────

        /// <summary>Smallest army that detaches a flanking group.</summary>
        public int flankMinArmy;

        /// <summary>Smallest flanking group worth sending.</summary>
        public int flankMinGroup;

        /// <summary>Fewest enemies in contact for a flank to be worth it.</summary>
        public int flankMinEnemies;

        /// <summary>A melee member is "fast" (eligible to flank) when it is
        /// cavalry or its speed is at least this multiple of the army's mean.</summary>
        public float flankSpeedRatio;

        /// <summary>Sideways offset of the flank point from the enemy centre.</summary>
        public float flankLateral;

        /// <summary>How far PAST the enemy centre (away from my army) the
        /// flank point sits — positive = behind them.</summary>
        public float flankDepth;

        /// <summary>The flanking group strikes once it is this close to the
        /// flank point.</summary>
        public float flankArriveRadius;

        /// <summary>Strike anyway after this long swinging round.</summary>
        public float flankTimeout;

        /// <summary>Earliest a mission may send another flanking group.</summary>
        public float flankRetrySeconds;

        // ── Fall back / regroup ─────────────────────────────────────────

        /// <summary>Radius of the live power read that decides a fall-back.</summary>
        public float powerRadius;

        /// <summary>Search radius for a friendly tower / Fortress to fall
        /// back on.</summary>
        public float fallbackAnchorSearch;

        /// <summary>With no anchor in reach, fall back this far toward the
        /// capital.</summary>
        public float fallbackDistance;

        /// <summary>The army counts as "back" once its centre is this close
        /// to the fall-back point.</summary>
        public float fallbackArriveRadius;

        /// <summary>Least time spent falling back before re-engaging.</summary>
        public float fallbackHoldSeconds;

        /// <summary>Longest fall-back; after it the army resumes its march
        /// if the enemy left, or falls back to the capital if not.</summary>
        public float fallbackTimeout;

        /// <summary>Armies smaller than this never fall back (nothing to
        /// save; the legacy retreat covers them).</summary>
        public int fallbackMinArmy;

        /// <summary>No fall-back this close to the capital — that is base
        /// defence, and there is nowhere better to go.</summary>
        public float fallbackSafeHomeRadius;

        // ── Ability value ────────────────────────────────────────────────

        /// <summary>Radius of the per-caster enemy / ally read.</summary>
        public float abilityScanRadius;

        /// <summary>An enemy this close to a caster puts it "in combat".</summary>
        public float contactDistance;

        /// <summary>Self-defensive abilities fire at or below this HP share
        /// (in combat).</summary>
        public float defensiveHpFraction;

        /// <summary>Fewest allies an ally buff must reach.</summary>
        public int buffMinAllies;

        /// <summary>A buff is "about to be needed" when an enemy is within
        /// its radius plus this margin.</summary>
        public float buffEnemyMargin;

        /// <summary>Radius the sect-unit actives (ArcanePulse, WarCry,
        /// Safeguard) are judged over — their effect circles are authored in
        /// UnitAbilitySystem, so this is the AI's read of "inside it".</summary>
        public float legacyAbilityRadius;

        /// <summary>Condemn marks a non-high-value target only while it has
        /// more than this share of its HP left (the bonus has work to do).</summary>
        public float condemnMinHpFraction;

        /// <summary>Seconds between "held" log lines for one ability per
        /// faction.</summary>
        public float abilityHeldLogSeconds;

        // ── Sect powers (AIAlanthorEndgameSystem) ───────────────────────

        /// <summary>A fight the tactics layer reported is a valid sect-power
        /// site for this long.</summary>
        public float fightSiteSeconds;

        /// <summary>Enemies this close to the capital make the base a fight
        /// site of its own.</summary>
        public float baseThreatRadius;

        // ── Logging ──────────────────────────────────────────────────────

        /// <summary>Seconds between aggregated TACTICS / TARGET lines per
        /// faction (kite counts, focus changes).</summary>
        public float logIntervalSeconds;
    }
}
