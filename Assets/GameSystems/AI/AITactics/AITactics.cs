// AITactics.cs
// Shared tactical knowledge for the AI's armies (docs/Design/Game_AI.md § 6e):
// the per-faction tactics SKILL (from the difficulty profile), counter-aware
// target scoring, live power reads, the fights each faction is in (read by the
// sect-power and ability casters), the kiting registry, and rate-limited logs.
//
// WHY (2026-10-04, developer diagnosis): "an expert AI should be able to win
// fights when outnumbered through kiting / flanking / clever use of unit
// abilities. It does none of these. AI is using abilities in random places as
// soon as they are available. AI should prioritize targets that are good
// against its composition. The decision rate doesn't seem to be the
// bottleneck for difficulty." So difficulty now lives in WHAT the army does in
// a fight (AITacticsSkill), not in how often the brain thinks.
//
// HOST-ONLY STATE, like AIStrengthMap: everything decided here leaves as
// CommandRouter orders with CommandSource.AI, so none of it has to be
// identical on two machines. Dictionaries here are lookup-only — nothing
// iterates them to decide anything.

using System.Collections.Generic;
using TheWaningBorder.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.AI
{
    /// <summary>A fight a faction's army reported this window.</summary>
    public struct AIFightSite
    {
        public float3 Pos;
        /// <summary>Live own-side power at the site (units + own towers).</summary>
        public int Mine;
        /// <summary>Live hostile power at the site (units + hostile towers).</summary>
        public int Enemy;
        /// <summary>Enemy over mine; 0 when nothing hostile.</summary>
        public float Ratio => Mine > 0 ? Enemy / (float)Mine : (Enemy > 0 ? float.MaxValue : 0f);
    }

    /// <summary>
    /// What one army looks like to an enemy's bonus damage: its members
    /// grouped by (tags, own bonus), so "how hard does that unit counter my
    /// army" is a few multiplies rather than a loop over every member.
    /// </summary>
    public sealed class AIArmyProfile
    {
        /// <summary>Array dimension, not tuning: distinct (tags, bonus)
        /// groups tracked; a rarer extra group still counts in Count.</summary>
        public const int MaxGroups = 16;

        public readonly uint[] Mask = new uint[MaxGroups];
        public readonly BonusVsTags[] Bonus = new BonusVsTags[MaxGroups];
        public readonly int[] N = new int[MaxGroups];
        public int Groups;
        public int Count;
        public bool HasHero;
        public float MeanSpeed;

        public void Clear() { Groups = 0; Count = 0; HasHero = false; MeanSpeed = 0f; }

        /// <summary>Average bonus damage <paramref name="enemy"/> deals to one
        /// of my units (it counters me).</summary>
        public float ThreatFrom(in BonusVsTags enemy)
        {
            if (Count == 0 || enemy.IsEmpty) return 0f;
            float s = 0f;
            for (int g = 0; g < Groups; g++) s += N[g] * enemy.AmountAgainst(Mask[g]);
            return s / Count;
        }

        /// <summary>Average bonus damage one of my units deals to a target
        /// carrying <paramref name="enemyMask"/> (I counter it).</summary>
        public float EdgeOver(uint enemyMask)
        {
            if (Count == 0 || enemyMask == 0) return 0f;
            float s = 0f;
            for (int g = 0; g < Groups; g++) s += N[g] * Bonus[g].AmountAgainst(enemyMask);
            return s / Count;
        }
    }

    public static class AITactics
    {
        /// <summary>The shared tuning (AITactics.asset).</summary>
        public static AITacticsConfig Cfg => AITacticsConfig.I;

        /// <summary>Array dimension: player slots (Faction.Blue..White).
        /// The Border is never an AI brain.</summary>
        public const int MaxFactions = 8;

        /// <summary>Array dimension: fights remembered per faction.</summary>
        private const int MaxSites = 8;

        #region Cached queries

        static readonly ComponentType[] QT_Brains =
        {
            ComponentType.ReadOnly<AIBrain>(),
        };
        static CachedEntityQuery QC_Brains;

        #endregion

        // ─────────────────────────────────────────────────────────────────
        // MATCH SCOPE
        // ─────────────────────────────────────────────────────────────────

        private static int _epoch = -1;
        private static Unity.Entities.World _world;

        /// <summary>Sim clock every reader here agrees on.</summary>
        public static double Now(EntityManager em) => TheWaningBorder.Core.SimClock.Elapsed;

        private static void ResetIfStale(EntityManager em)
        {
            if (_epoch == SimCadence.Epoch && _world == em.World) return;
            _epoch = SimCadence.Epoch;
            _world = em.World;
            System.Array.Clear(_skillState, 0, _skillState.Length);
            _skillScanAt = double.MinValue;
            System.Array.Clear(_siteAt, 0, _siteAt.Length);
            _kiteUntil.Clear();
            _kiteAnchor.Clear();
            _logNext.Clear();
            System.Array.Clear(_kiteCount, 0, _kiteCount.Length);
        }

        // ─────────────────────────────────────────────────────────────────
        // SKILL
        // ─────────────────────────────────────────────────────────────────

        private static readonly AITacticsSkill[] _skill = new AITacticsSkill[MaxFactions];
        private static readonly byte[] _skillState = new byte[MaxFactions]; // 0 unknown, 1 AI
        private static double _skillScanAt = double.MinValue;

        /// <summary>
        /// The tactics skill of an AI faction, from its brain's difficulty
        /// profile. False for a human (no brain) — the caller does nothing.
        /// Re-scanned at most once a second, since brains appear at match
        /// start and never change tier.
        /// </summary>
        public static bool TryGetSkill(EntityManager em, Faction f, out AITacticsSkill skill)
        {
            ResetIfStale(em);
            skill = default;
            int i = (int)f;
            if (i < 0 || i >= MaxFactions) return false;
            if (_skillState[i] == 0)
            {
                double now = Now(em);
                if (now - _skillScanAt >= 1.0)
                {
                    _skillScanAt = now;
                    var q = QC_Brains.Get(em, QT_Brains);
                    using var brains = q.ToComponentDataArray<AIBrain>(Unity.Collections.Allocator.Temp);
                    for (int b = 0; b < brains.Length; b++)
                    {
                        int o = (int)brains[b].Owner;
                        if (o < 0 || o >= MaxFactions || brains[b].IsActive == 0) continue;
                        _skill[o] = AISimpleDifficulty.GetProfile(brains[b].Difficulty).Tactics;
                        _skillState[o] = 1;
                    }
                }
            }
            if (_skillState[i] != 1) return false;
            skill = _skill[i];
            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // FIGHT SITES
        // ─────────────────────────────────────────────────────────────────

        private static readonly AIFightSite[] _sites = new AIFightSite[MaxFactions * MaxSites];
        private static readonly double[] _siteAt = new double[MaxFactions * MaxSites];

        /// <summary>
        /// An army of <paramref name="f"/> is fighting at <paramref name="pos"/>.
        /// Updates the remembered site within a few metres, else the oldest.
        /// </summary>
        public static void ReportFightSite(EntityManager em, Faction f, float3 pos, int mine, int enemy)
        {
            ResetIfStale(em);
            int fi = (int)f;
            if (fi < 0 || fi >= MaxFactions) return;
            double now = Now(em);
            int slot = -1, oldest = 0;
            double oldestAt = double.MaxValue;
            float merge2 = Cfg.powerRadius * Cfg.powerRadius;
            for (int s = 0; s < MaxSites; s++)
            {
                int k = fi * MaxSites + s;
                if (_siteAt[k] > 0 && math.distancesq(_sites[k].Pos.xz, pos.xz) <= merge2) { slot = s; break; }
                if (_siteAt[k] < oldestAt) { oldestAt = _siteAt[k]; oldest = s; }
            }
            if (slot < 0) slot = oldest;
            int key = fi * MaxSites + slot;
            _sites[key] = new AIFightSite { Pos = pos, Mine = mine, Enemy = enemy };
            _siteAt[key] = now;
        }

        /// <summary>The faction's fights reported within fightSiteSeconds.</summary>
        public static void FightSites(EntityManager em, Faction f, List<AIFightSite> into)
        {
            ResetIfStale(em);
            into.Clear();
            int fi = (int)f;
            if (fi < 0 || fi >= MaxFactions) return;
            double now = Now(em);
            for (int s = 0; s < MaxSites; s++)
            {
                int k = fi * MaxSites + s;
                if (_siteAt[k] <= 0 || now - _siteAt[k] > Cfg.fightSiteSeconds) continue;
                into.Add(_sites[k]);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // KITING REGISTRY (read by the army tactics so a kiting step is not
        // overwritten by the next focus order)
        // ─────────────────────────────────────────────────────────────────

        private static readonly Dictionary<Entity, double> _kiteUntil = new Dictionary<Entity, double>();
        private static readonly Dictionary<Entity, float4> _kiteAnchor = new Dictionary<Entity, float4>();
        private static readonly List<Entity> _prune = new List<Entity>();
        private static readonly int[] _kiteCount = new int[MaxFactions];

        /// <summary>True while a kiting step this layer ordered is still
        /// being walked — leave the unit alone.</summary>
        public static bool IsKiting(EntityManager em, Entity e)
        {
            ResetIfStale(em);
            return _kiteUntil.TryGetValue(e, out var t) && t > Now(em);
        }

        /// <summary>Where this unit started kiting in the current fight
        /// (reset when its last step is older than kiteAnchorResetSeconds).</summary>
        internal static float3 KiteAnchor(EntityManager em, Entity e, float3 pos)
        {
            double now = Now(em);
            if (_kiteAnchor.TryGetValue(e, out var a)
                && now - a.w <= Cfg.kiteAnchorResetSeconds)
                return a.xyz;
            _kiteAnchor[e] = new float4(pos, (float)now);
            return pos;
        }

        internal static void MarkKiting(EntityManager em, Entity e, Faction f, double until)
        {
            double now = Now(em);
            _kiteUntil[e] = until;
            if (_kiteAnchor.TryGetValue(e, out var a)) { a.w = (float)now; _kiteAnchor[e] = a; }
            int fi = (int)f;
            if (fi >= 0 && fi < MaxFactions) _kiteCount[fi]++;

            // Bounded: drop finished entries once the table grows. Removal
            // order is irrelevant — nothing decides on iteration order.
            if (_kiteUntil.Count > 256)
            {
                _prune.Clear();
                foreach (var kv in _kiteUntil) if (kv.Value <= now) _prune.Add(kv.Key);
                for (int i = 0; i < _prune.Count; i++) { _kiteUntil.Remove(_prune[i]); _kiteAnchor.Remove(_prune[i]); }
            }
        }

        /// <summary>Kites issued for this faction since the last call.</summary>
        internal static int TakeKiteCount(Faction f)
        {
            int fi = (int)f;
            if (fi < 0 || fi >= MaxFactions) return 0;
            int n = _kiteCount[fi];
            _kiteCount[fi] = 0;
            return n;
        }

        // ─────────────────────────────────────────────────────────────────
        // LOGGING
        // ─────────────────────────────────────────────────────────────────

        private static readonly Dictionary<(int, string), double> _logNext = new Dictionary<(int, string), double>();

        /// <summary>True (and re-arms) when this faction may log
        /// <paramref name="key"/> again.</summary>
        public static bool LogDue(EntityManager em, Faction f, string key, float interval)
        {
            ResetIfStale(em);
            double now = Now(em);
            var k = ((int)f, key);
            if (_logNext.TryGetValue(k, out var next) && now < next) return false;
            _logNext[k] = now + interval;
            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // UNIT READS
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Live strength on TacticalQuery's scale (damage x2 + hp/10).</summary>
        public static int LiveStrength(EntityManager em, Entity e)
        {
            if (!em.Exists(e) || !em.HasComponent<Health>(e)) return 0;
            var hp = em.GetComponentData<Health>(e);
            if (hp.Value <= 0) return 0;
            int dmg = em.HasComponent<Damage>(e) ? em.GetComponentData<Damage>(e).Value : 0;
            return math.max(0, dmg * 2 + hp.Value / 10);
        }

        public static uint TagsOf(EntityManager em, Entity e)
            => em.HasComponent<UnitTagsData>(e) ? em.GetComponentData<UnitTagsData>(e).Mask : 0u;

        public static bool IsHero(EntityManager em, Entity e) => em.HasComponent<HeroLevel>(e);

        /// <summary>Heroes, siege, healers and casters: the bodies whose death
        /// changes a fight more than their HP says.</summary>
        public static bool IsHighValue(EntityManager em, Entity e)
        {
            if (em.HasComponent<HeroLevel>(e)) return true;
            if (!em.HasComponent<UnitTag>(e)) return false;
            var c = em.GetComponentData<UnitTag>(e).Class;
            return c == UnitClass.Siege || c == UnitClass.Support || c == UnitClass.Magic;
        }

        /// <summary>A unit that fights hand to hand.</summary>
        public static bool IsMelee(EntityManager em, Entity e)
            => em.HasComponent<UnitTag>(e)
               && em.GetComponentData<UnitTag>(e).Class == UnitClass.Melee
               && !em.HasComponent<ArcherTag>(e);

        /// <summary>The unit's authored max range, or 0 when it is not a
        /// shooter (or its range is not authored).</summary>
        public static float RangeOf(EntityManager em, Entity e)
            => em.HasComponent<ArcherTag>(e) && em.HasComponent<ArcherState>(e)
                ? math.max(0f, em.GetComponentData<ArcherState>(e).MaxRange) : 0f;

        public static float SpeedOf(EntityManager em, Entity e)
            => em.HasComponent<MoveSpeed>(e) ? em.GetComponentData<MoveSpeed>(e).Value : 0f;

        public static float RadiusOf(EntityManager em, Entity e)
            => em.HasComponent<Radius>(e) ? em.GetComponentData<Radius>(e).Value : 0f;

        /// <summary>Is this unit fast enough to flank: cavalry, or quicker
        /// than the army by flankSpeedRatio.</summary>
        public static bool IsFast(EntityManager em, Entity e, float armyMeanSpeed)
        {
            if (em.HasComponent<CavalryTag>(e)) return true;
            if ((TagsOf(em, e) & (uint)UnitTagBits.Cavalry) != 0) return true;
            return armyMeanSpeed > 0f && SpeedOf(em, e) >= armyMeanSpeed * Cfg.flankSpeedRatio;
        }

        // ─────────────────────────────────────────────────────────────────
        // ARMY PROFILE + SCORING
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Fill <paramref name="p"/> from the living members.</summary>
        public static void BuildProfile(EntityManager em, List<Entity> members, AIArmyProfile p)
        {
            p.Clear();
            float speed = 0f;
            for (int i = 0; i < members.Count; i++)
            {
                var u = members[i];
                if (!em.Exists(u)) continue;
                p.Count++;
                speed += SpeedOf(em, u);
                if (IsHero(em, u)) p.HasHero = true;
                uint mask = TagsOf(em, u);
                var bonus = em.HasComponent<BonusVsTags>(u) ? em.GetComponentData<BonusVsTags>(u) : default;
                int g = 0;
                for (; g < p.Groups; g++)
                    if (p.Mask[g] == mask && SameBonus(p.Bonus[g], bonus)) break;
                if (g == p.Groups)
                {
                    if (p.Groups == AIArmyProfile.MaxGroups) continue;
                    p.Mask[g] = mask; p.Bonus[g] = bonus; p.N[g] = 0; p.Groups++;
                }
                p.N[g]++;
            }
            p.MeanSpeed = p.Count > 0 ? speed / p.Count : 0f;
        }

        private static bool SameBonus(in BonusVsTags a, in BonusVsTags b)
            => a.Mask0 == b.Mask0 && a.Amount0 == b.Amount0 && a.Mask1 == b.Mask1 && a.Amount1 == b.Amount1
            && a.Mask2 == b.Mask2 && a.Amount2 == b.Amount2 && a.Mask3 == b.Mask3 && a.Amount3 == b.Amount3;

        /// <summary>Why a candidate topped the army's list (for the log).</summary>
        public enum FocusReason : byte { Danger, Finish, Value, Counter }

        /// <summary>
        /// The ARMY's view of one hostile unit: danger + nearly-dead +
        /// high-value + "it counters us / we counter it", minus fragility and
        /// distance. Pure arithmetic over live component data and SO-derived
        /// tags / bonuses.
        /// </summary>
        public static float ArmyScore(EntityManager em, Entity e, float3 from, AIArmyProfile army,
            in AITacticsSkill skill, out FocusReason reason)
        {
            var c = Cfg;
            var hp = em.GetComponentData<Health>(e);
            float3 p = em.GetComponentData<LocalTransform>(e).Position;
            int dmg = em.HasComponent<Damage>(e) ? em.GetComponentData<Damage>(e).Value : 0;
            float danger = dmg * c.dangerScale;
            float finish = (hp.Max - hp.Value) * c.finishScale * skill.focusFireWeight;
            float value = IsHighValue(em, e) ? c.highValueBonus * skill.focusFireWeight : 0f;
            // WORKERS ARE HIGH-VALUE TARGETS (2026-10-07, Game_AI.md § 3b): a
            // flat bonus at every tier, outside focusFireWeight — killing the
            // builders stops the rebuild, whatever the tier's focus skill.
            if (em.HasComponent<WorkerTag>(e)) value += c.workerTargetBonus;

            float counter = 0f;
            if (skill.counterTargetWeight > 0f)
            {
                var eb = em.HasComponent<BonusVsTags>(e) ? em.GetComponentData<BonusVsTags>(e) : default;
                counter += army.ThreatFrom(eb) * c.counterThreatScale;
                counter += army.EdgeOver(TagsOf(em, e)) * c.counterEdgeScale;
                if (army.HasHero && em.HasComponent<TargetPreference>(e)
                    && em.GetComponentData<TargetPreference>(e).Heroes != 0)
                    counter += c.heroThreatBonus;
                counter *= skill.counterTargetWeight;
            }

            reason = FocusReason.Danger;
            float top = danger;
            if (finish > top) { top = finish; reason = FocusReason.Finish; }
            if (value > top) { top = value; reason = FocusReason.Value; }
            if (counter > top) { reason = FocusReason.Counter; }

            return danger + finish + value + counter
                 - hp.Max * c.fragileScale
                 - math.distance(p.xz, from.xz) * c.distanceScale;
        }

        /// <summary>
        /// One member's view of a candidate the army already scored: its OWN
        /// counter relationship to it (a spearman wants the horse, the horse
        /// wants the archer) and its own walk to reach it.
        /// </summary>
        public static float MemberScore(EntityManager em, Entity member, uint memberMask,
            in BonusVsTags memberBonus, float3 memberPos, Entity e, float armyScore, in AITacticsSkill skill)
        {
            var c = Cfg;
            float s = armyScore;
            if (skill.counterTargetWeight > 0f)
            {
                var eb = em.HasComponent<BonusVsTags>(e) ? em.GetComponentData<BonusVsTags>(e) : default;
                s += skill.counterTargetWeight
                     * (eb.AmountAgainst(memberMask) * c.counterThreatScale
                        + memberBonus.AmountAgainst(TagsOf(em, e)) * c.counterEdgeScale);
            }
            float3 p = em.GetComponentData<LocalTransform>(e).Position;
            return s - math.distance(p.xz, memberPos.xz) * c.memberDistanceScale;
        }

        /// <summary>Is <paramref name="e"/> a live hostile unit we may order
        /// an attack on?</summary>
        public static bool IsLiveHostileUnit(EntityManager em, Faction faction, Entity e)
        {
            if (!em.Exists(e) || !em.HasComponent<Health>(e) || !em.HasComponent<LocalTransform>(e)
                || !em.HasComponent<FactionTag>(e)) return false;
            if (!Alliances.AreHostile(faction, em.GetComponentData<FactionTag>(e).Value)) return false;
            return em.GetComponentData<Health>(e).Value > 0;
        }

        /// <summary>
        /// Live power around a point: hostile units (from the strength map's
        /// candidates, re-read live) plus hostile towers, and the faction's
        /// own static defence. The caller adds its own army's live strength.
        /// </summary>
        public static void LivePowerAround(EntityManager em, Faction faction, float3 pos, float radius,
            List<Entity> scratch, out int enemy, out int friendlyStatic, out float3 enemyCentroid, out int enemyCount)
        {
            AIStrengthMap.HostileUnitCandidates(em, faction, pos, radius, scratch);
            enemy = 0; enemyCount = 0;
            float3 sum = float3.zero;
            float r2 = radius * radius;
            for (int i = 0; i < scratch.Count; i++)
            {
                var e = scratch[i];
                if (!IsLiveHostileUnit(em, faction, e)) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                if (math.distancesq(p.xz, pos.xz) > r2) continue;
                enemy += LiveStrength(em, e);
                sum += p; enemyCount++;
            }
            enemyCentroid = enemyCount > 0 ? sum / enemyCount : pos;
            enemy += AIStrengthMap.StaticPowerInRadius(em, faction, pos, radius);
            friendlyStatic = AIStrengthMap.FriendlyStaticPowerInRadius(em, faction, pos, radius);
        }

        /// <summary>A unit's display id for logs ("Spearman", "King Lexor").</summary>
        public static string IdOf(EntityManager em, Entity e)
            => em.HasComponent<UnitTypeId>(e) ? em.GetComponentData<UnitTypeId>(e).Value.ToString() : "unit";
    }
}
