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
        #region Cached queries

        // CreateEntityQuery registers a new query with the world on EVERY
        // call; these run per think tick. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_BuildingTagFactionTagLocalTransformHealth =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_BuildingTagFactionTagLocalTransformHealth;

        static readonly ComponentType[] QT_UnitTagFactionTagLocalTransformHealth =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_UnitTagFactionTagLocalTransformHealth;

        #endregion

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
            var q = QC_BuildingTagFactionTagLocalTransformHealth.Get(em, QT_BuildingTagFactionTagLocalTransformHealth);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);

            float r2 = radius * radius;
            int sum = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (!Alliances.AreHostile(faction, facs[i].Value)) continue;
                if (facs[i].Value == Faction.Border) continue;
                if (hps[i].Value <= 0) continue;
                if (em.HasComponent<WallTag>(ents[i])) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;

                float dx = xfs[i].Position.x - pos.x;
                float dz = xfs[i].Position.z - pos.z;
                if (dx * dx + dz * dz > r2) continue;

                // An unarmed building is an objective, not a threat: it still
                // costs time to chew through, hence the small durability term.
                int power = hps[i].Value / 40;

                if (em.HasComponent<BuildingRangedAttack>(ents[i]))
                {
                    var atk = em.GetComponentData<BuildingRangedAttack>(ents[i]);
                    int targets = math.max(1, atk.MaxTargets);
                    power += atk.Damage * 2 * targets;
                }
                sum += math.max(0, power);
            }
            return sum;
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
        /// Deterministic: pure arithmetic over replicated component data, ties
        /// broken by entity index, so every lockstep peer picks the same
        /// target from the same world.
        /// </summary>
        public static Entity PickPriorityTarget(EntityManager em, Faction faction,
            float3 fromPos, float radius = float.NaN)
        {
            radius = Radius(radius);

            var q = QC_UnitTagFactionTagLocalTransformHealth.Get(em, QT_UnitTagFactionTagLocalTransformHealth);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);

            float r2 = radius * radius;
            Entity best = Entity.Null;
            float bestScore = float.MinValue;

            for (int i = 0; i < ents.Length; i++)
            {
                if (!Alliances.AreHostile(faction, facs[i].Value)) continue;
                if (hps[i].Value <= 0) continue;

                float dx = xfs[i].Position.x - fromPos.x;
                float dz = xfs[i].Position.z - fromPos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > r2) continue;

                int dmg = em.HasComponent<Damage>(ents[i])
                    ? em.GetComponentData<Damage>(ents[i]).Value : 0;

                float score = dmg * 3f;                              // danger
                score += (hps[i].Max - hps[i].Value) * 0.10f;        // nearly dead
                score -= hps[i].Max * 0.02f;                         // fragile first
                score -= math.sqrt(d2) * 0.5f;                       // mild proximity pull

                if (score > bestScore
                    || (score == bestScore && best != Entity.Null && ents[i].Index < best.Index))
                {
                    bestScore = score;
                    best = ents[i];
                }
            }
            return best;
        }
    }
}
