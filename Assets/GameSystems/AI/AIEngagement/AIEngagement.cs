// AIEngagement.cs
// The AI's fight-or-retreat brain, and its focus-fire picker.
//
// Three questions, one place:
//   1. WHAT is around my army right now?        -> Assess
//   2. Do I WIN this fight?                     -> Assessment.ShouldFight
//   3. WHICH of them do I kill first?           -> PickPriorityTarget
//
// Why this exists (2026-08-18, witnessed): an Expert AI chased a scout into
// the enemy base, died to the garrison plus the Hall, and the survivors towed
// the counter-attack home. The chase itself is leashed in TargetingSystem, but
// the deeper fault was that the AI could not SEE the fight it was walking
// into. TacticalQuery.StrengthInRadius counts UnitTag entities and nothing
// else, so a Hall — 2400 HP and a multi-target gun — contributed exactly ZERO
// to "how dangerous is it here". Attacking into a defended base therefore
// looked identical to attacking into an empty field, which is precisely the
// "fighting next to the enemy Hall we are always outnumbered" report.

using Unity.Collections;
using TheWaningBorder.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>The verdict for one place at one moment.</summary>
    public struct EngagementAssessment
    {
        /// <summary>Allied power in the band (own faction + allies).</summary>
        public int MyPower;
        /// <summary>Hostile MOBILE power — the army that can chase you.</summary>
        public int EnemyMobilePower;
        /// <summary>Hostile STATIC power — buildings that shoot back.</summary>
        public int EnemyStaticPower;
        /// <summary>Everything hostile, mobile and static together.</summary>
        public int EnemyPower => EnemyMobilePower + EnemyStaticPower;
        /// <summary>Enemy power over mine. 1.0 = even, &gt;1 = losing.</summary>
        public float Ratio;
        /// <summary>True when committing here is worth it.</summary>
        public bool ShouldFight;
    }

    public static class AIEngagement
    {

        #region Tuning

        /// <summary>Every number below used to be a const in this file;
        /// they live in AIEngagement.asset now.</summary>
        static AIEngagementConfig Cfg => AIEngagementConfig.I;

        /// <summary>
        /// Resolves an omitted radius / commit ratio to the configured default.
        ///
        /// A C# default parameter must be a compile-time constant, so the value
        /// cannot be the asset's — callers pass NaN (the literal default) and
        /// the real number is read here, once the config is loadable.
        /// </summary>
        static float Radius(float v) => float.IsNaN(v) ? Cfg.defaultAssessRadius : v;

        static float CommitRatio(float v) => float.IsNaN(v) ? Cfg.defaultCommitRatio : v;

        /// <summary>defaultAssessRadius, for callers outside this class.</summary>
        public static float DefaultAssessRadius => Cfg.defaultAssessRadius;

        /// <summary>defaultCommitRatio, for callers outside this class.</summary>
        public static float DefaultCommitRatio => Cfg.defaultCommitRatio;

        #endregion


        // ──────────────────────────────────────────────────────────────
        // 1. WHAT IS HERE
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Threat from hostile BUILDINGS in the band — the term
        /// TacticalQuery has always been missing.
        ///
        /// Scored as sustained damage output, NOT as a unit would be: a
        /// building's huge HP pool is durability, not danger, so it carries a
        /// quarter of a unit's HP weight. What actually kills an army is the
        /// gun, and a multi-target gun (the Hall's chain) is worth its target
        /// count. Walls are excluded — only siege engages the fortification
        /// line (docs/Design/Combat_Pacing.md) — and Border structures are
        /// verb objectives rather than combatants.
        /// </summary>
        public static int StaticDefencePower(EntityManager em, Faction faction,
            float3 pos, float radius)
        {
            // Served from AIStrengthMap: the per-building rules above are
            // evaluated once per refresh, not once per call.
            return AIStrengthMap.StaticPowerInRadius(em, faction, pos, radius);
        }

        /// <summary>
        /// Full read of one location: who is here, and do we win.
        /// </summary>
        public static EngagementAssessment Assess(EntityManager em, Faction faction,
            float3 pos, float radius = float.NaN, float commitRatio = float.NaN)
        {
            radius = Radius(radius); commitRatio = CommitRatio(commitRatio);

            var a = new EngagementAssessment
            {
                MyPower = TacticalQuery.FactionStrengthInRadius(em, faction, pos, radius),
                EnemyMobilePower = TacticalQuery.EnemyStrengthInRadius(em, faction, pos, radius),
                EnemyStaticPower = StaticDefencePower(em, faction, pos, radius),
            };

            // Nothing hostile here at all: always worth walking in.
            if (a.EnemyPower <= 0) { a.Ratio = 0f; a.ShouldFight = true; return a; }
            // No army of our own in the band — never "fight" with nobody.
            if (a.MyPower <= 0) { a.Ratio = float.MaxValue; a.ShouldFight = false; return a; }

            a.Ratio = a.EnemyPower / (float)a.MyPower;
            a.ShouldFight = a.Ratio <= commitRatio;
            return a;
        }

        /// <summary>
        /// Power of a specific set of bodies, on the same scale as
        /// TacticalQuery (damage x2 + hp/10). Used to judge a wave BEFORE it
        /// marches: the army is still at home, so measuring "my power at the
        /// target" would read zero and refuse every attack ever.
        /// </summary>
        public static int PowerOf(EntityManager em,
            System.Collections.Generic.List<Entity> units)
        {
            int sum = 0;
            for (int i = 0; i < units.Count; i++)
            {
                var e = units[i];
                if (!em.Exists(e) || !em.HasComponent<Health>(e)) continue;
                var hp = em.GetComponentData<Health>(e);
                if (hp.Value <= 0) continue;
                int dmg = em.HasComponent<Damage>(e) ? em.GetComponentData<Damage>(e).Value : 0;
                sum += math.max(0, dmg * 2 + hp.Value / 10);
            }
            return sum;
        }

        /// <summary>
        /// Would this army win at that place? Enemy side counts BUILDINGS,
        /// which is what the old count-only wave gate never did.
        /// </summary>
        public static EngagementAssessment AssessAssault(EntityManager em, Faction faction,
            System.Collections.Generic.List<Entity> army, float3 targetPos,
            float radius = float.NaN, float commitRatio = float.NaN)
        {
            radius = Radius(radius); commitRatio = CommitRatio(commitRatio);

            var a = new EngagementAssessment
            {
                MyPower = PowerOf(em, army),
                EnemyMobilePower = TacticalQuery.EnemyStrengthInRadius(em, faction, targetPos, radius),
                EnemyStaticPower = StaticDefencePower(em, faction, targetPos, radius),
            };
            if (a.EnemyPower <= 0) { a.Ratio = 0f; a.ShouldFight = true; return a; }
            if (a.MyPower <= 0) { a.Ratio = float.MaxValue; a.ShouldFight = false; return a; }
            a.Ratio = a.EnemyPower / (float)a.MyPower;
            a.ShouldFight = a.Ratio <= commitRatio;
            return a;
        }

        // ──────────────────────────────────────────────────────────────
        // 2. WHO DIES FIRST
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Focus-fire pick: the hostile unit near <paramref name="fromPos"/>
        /// worth killing first.
        ///
        /// Priority, in order of weight:
        ///   * DANGER — its damage output. Killing the thing that hurts most
        ///     reduces incoming damage fastest; this is the whole point of
        ///     focusing rather than everyone hitting whatever is nearest.
        ///   * NEARLY DEAD — a wounded body dies sooner, so it stops shooting
        ///     sooner. Finishing beats spreading.
        ///   * FRAGILE — low max HP dies quickly for the same reason.
        ///   * CLOSE — a mild pull so the army does not run past three enemies
        ///     to reach a marginally better fourth.
        ///
        /// Ties broken by entity index. Host-only (the AI brains are), so the
        /// candidate list may come from the host's strength map; the scoring
        /// itself is pure arithmetic over live component data.
        /// </summary>
        // Host-only scratch (the AI runs on one thread, on the host).
        private static readonly System.Collections.Generic.List<Entity> _candidates =
            new System.Collections.Generic.List<Entity>(64);

        public static Entity PickPriorityTarget(EntityManager em, Faction faction,
            float3 fromPos, float radius = float.NaN)
        {
            radius = Radius(radius);

            // CANDIDATES from the strength map (up to one refresh stale),
            // SCORED on live state: a candidate that died or walked out of
            // the radius since the snapshot is skipped, so the order never
            // names a stale body.
            AIStrengthMap.HostileUnitCandidates(em, faction, fromPos, radius, _candidates);

            float r2 = radius * radius;
            Entity best = Entity.Null;
            float bestScore = float.MinValue;

            for (int i = 0; i < _candidates.Count; i++)
            {
                var e = _candidates[i];
                if (!em.Exists(e) || !em.HasComponent<Health>(e)
                    || !em.HasComponent<LocalTransform>(e) || !em.HasComponent<FactionTag>(e)) continue;
                if (!Alliances.AreHostile(faction, em.GetComponentData<FactionTag>(e).Value)) continue;
                var hp = em.GetComponentData<Health>(e);
                if (hp.Value <= 0) continue;

                var p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - fromPos.x;
                float dz = p.z - fromPos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > r2) continue;

                int dmg = em.HasComponent<Damage>(e)
                    ? em.GetComponentData<Damage>(e).Value : 0;

                float score = dmg * 3f;                              // danger
                score += (hp.Max - hp.Value) * 0.10f;                // nearly dead
                score -= hp.Max * 0.02f;                             // fragile first
                score -= math.sqrt(d2) * 0.5f;                       // mild proximity pull
                // Workers are high-value targets (Game_AI.md § 3b) — the same
                // bonus the tactics layer's army scoring gives them.
                if (em.HasComponent<WorkerTag>(e)) score += AITacticsConfig.I.workerTargetBonus;

                if (score > bestScore
                    || (score == bestScore && best != Entity.Null && e.Index < best.Index))
                {
                    bestScore = score;
                    best = e;
                }
            }
            return best;
        }
    }
}
