// SimpleAISystem.CurseIntel.cs
// How strong is the curse at a node — never just "what stands there now"
// (docs/Design/Game_AI.md § 5i rule 2). Partial of SimpleAISystem.cs.
//
// ─────────────────────────────────────────────────────────────────────────
// WHY (2026-10-05, SunderedCrown headless match)
//
// Red (Easy/Rush) lost 124 units to the curse at two nodes 60-90 m from its
// base although the curse sent it no wave at all. Every attack passed the
// margin check against what the strength map read inside 40 m of the node:
//   "5 units attack the curse node at (49,-15) — power 160 vs 78"  (all dead)
//   "6 units attack ... — power 216 vs 87", then "losing (72+14 vs 243)"
//   "7 units attack ... — power 252 vs 99"
// while the same node had read 249 / 240 / 288 a minute earlier. Three
// things the single reading missed:
//   * the garrison moves: guards that are off chasing, or that belong to the
//     next node, come back the moment the fight starts, and an attacked node
//     is topped up first at the next garrison spawn;
//   * the garrison GROWS on a timer (garrisonCap x armyGrowth^n per node,
//     BorderSettingsSO), whether anyone looked or not;
//   * a Crystalling pack hits harder the bigger it is (crystallingPack*), and
//     cursed ground slows and burns the attacker — the strength scale
//     (damage x2 + hp/10) sees neither.
//
// So a curse node is judged by the MAX of three readings: what stands there
// now (pack-adjusted), the highest reading this faction has SEEN there
// (decaying slowly), and the garrison the curse's own config says the node
// has by now. Unlooked-at ground counts as reinforced. Logged as INTEL.
// ─────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.Data.Border;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        /// <summary>(faction, 10 m cell of a curse node) -> the highest power
        /// seen there (pack-adjusted), when it was seen, and when the faction
        /// last had the node in sight. Host-only; reset per match.</summary>
        private readonly Dictionary<(int, int, int), (float Power, float At, float LookAt)> _curseNodeSeen
            = new Dictionary<(int, int, int), (float, float, float)>();
        private readonly Dictionary<(int, int, int), float> _nextCurseIntelLog
            = new Dictionary<(int, int, int), float>();

        private static (int, int, int) CurseIntelKey(Faction faction, float3 node)
            => ((int)faction, (int)math.floor(node.x / 10f), (int)math.floor(node.z / 10f));

        /// <summary>
        /// The power this faction should expect to meet at the curse node at
        /// <paramref name="node"/>: max(visible now, last seen decaying,
        /// config baseline), x curseIntelStaleFactor when the node has not been
        /// in sight for curseIntelLookSeconds, x curseIntelGroundFactor for the
        /// cursed ground. <paramref name="visibleMobile"/> is the mobile
        /// hostile power AIEngagement read in the band (any hostile, so a
        /// rival standing there still counts).
        /// </summary>
        private int CurseNodePowerEstimate(EntityManager em, Faction faction, float3 node,
            float radius, float now, int visibleMobile)
        {
            var s = BorderSettings.Get();

            // VISIBLE, with the pack bonus the strength scale cannot see.
            int count = AIStrengthMap.UnitsOfFactionInRadius(em, node, radius, Faction.Border, out int cursePower);
            float pack = CursePackMultiplier(s, now, count);
            float visible = math.max(0, visibleMobile - cursePower) + cursePower * pack;

            // LAST SEEN, decaying (half-life), refreshed only while in sight.
            var fog = TheWaningBorder.World.FogOfWar.FogOfWarManager.Instance;
            bool inSight = fog == null
                || fog.IsVisible(faction, new UnityEngine.Vector3(node.x, 0f, node.z));
            var key = CurseIntelKey(faction, node);
            _curseNodeSeen.TryGetValue(key, out var mem);
            float halfLife = math.max(1f, Cfg.curseIntelLastSeenHalfLifeSeconds);
            float seen = mem.At > 0f || mem.Power > 0f
                ? mem.Power * math.pow(0.5f, math.max(0f, now - mem.At) / halfLife) : 0f;
            if (inSight)
            {
                if (visible >= seen) { mem.Power = visible; mem.At = now; seen = visible; }
                mem.LookAt = now;
                _curseNodeSeen[key] = mem;
            }

            // BASELINE: the garrison the curse's own rules give a node by now.
            float baseline = CurseGarrisonBaseline(em, s, now) * math.max(0f, Cfg.curseIntelBaselineFraction);

            float est = math.max(visible, math.max(seen, baseline));
            bool stale = !inSight && now - mem.LookAt > math.max(0f, Cfg.curseIntelLookSeconds);
            if (stale) est *= math.max(1f, Cfg.curseIntelStaleFactor);
            est *= math.max(1f, Cfg.curseIntelGroundFactor);
            int result = (int)math.ceil(est);

            if (!_nextCurseIntelLog.TryGetValue(key, out float nextLog) || now >= nextLog)
            {
                _nextCurseIntelLog[key] = now + math.max(1f, Cfg.curseIntelLogInterval);
                AILogger.Log(faction, "INTEL",
                    $"curse node ({node.x:0},{node.z:0}) power est {result} (visible {visible:0} " +
                    $"[{count} curse unit(s), pack x{pack:0.00}], last-seen {seen:0}, baseline {baseline:0}" +
                    (stale ? $", not looked at for {now - mem.LookAt:0}s" : "") + ")");
            }
            return result;
        }

        /// <summary>
        /// AIEngagement.AssessAssault against a CURSE NODE: the enemy side is
        /// the curse estimate above, the verdict re-derived with the default
        /// commit ratio.
        /// </summary>
        private EngagementAssessment AssessCurseNode(EntityManager em, Faction faction,
            List<Entity> army, float3 node, float radius, float now)
        {
            var a = AIEngagement.AssessAssault(em, faction, army, node, radius);
            int est = CurseNodePowerEstimate(em, faction, node, radius, now, a.EnemyMobilePower);
            a.EnemyMobilePower = math.max(a.EnemyMobilePower, est);
            if (a.EnemyPower <= 0) { a.Ratio = 0f; a.ShouldFight = true; return a; }
            if (a.MyPower <= 0) { a.Ratio = float.MaxValue; a.ShouldFight = false; return a; }
            a.Ratio = a.EnemyPower / (float)a.MyPower;
            a.ShouldFight = a.Ratio <= AIEngagement.DefaultCommitRatio;
            return a;
        }

        /// <summary>
        /// Garrison power the curse's rules give one node at match second
        /// <paramref name="now"/>: garrisonCap x armyGrowth^n (n = garrison
        /// spawns so far, one per armySpawnSeconds), capped by the curse unit cap
        /// shared among the live nodes, at the army tier the match minute has
        /// reached (minutesPerTier), pack bonus included. Every number is
        /// BorderSettingsSO's or the curse units' SOs.
        /// </summary>
        private static float CurseGarrisonBaseline(EntityManager em, BorderSettingsSO s, float now)
        {
            if (s == null || s.garrisonCap <= 0) return 0f;
            int n = (int)math.floor(now / math.max(10f, s.armySpawnSeconds));
            float size = s.garrisonCap * math.pow(math.max(1f, s.armyGrowth), n);
            // The cap scales with the territories the curse holds
            // (Territory_Claims.md §6.8, CurseUnitCap.Max), never above
            // maxCurseUnits.
            int cap = global::TheWaningBorder.Systems.Border.CurseUnitCap.Max;
            if (cap > 0)
            {
                var nq = QC_SmallNodeTagLocalTransformHealth.Get(em, QT_SmallNodeTagLocalTransformHealth);
                int nodes = math.max(1, nq.CalculateEntityCount());
                size = math.min(size, cap / (float)nodes);
            }
            return size * CurseUnitPower(s, now, size);
        }

        /// <summary>Average strength of one curse unit of the tier at
        /// <paramref name="now"/>, in a pack of <paramref name="packSize"/>
        /// (the Crystallings' share of it gets the pack damage bonus).</summary>
        private static float CurseUnitPower(BorderSettingsSO s, float now, float packSize)
        {
            var tier = CurseTierAt(s, now);
            float pc = UnitStrengthOf("Crystalling", 1f);
            if (tier == null || tier.TotalUnits <= 0) return pc;
            float total = tier.TotalUnits;
            float crystallings = packSize * tier.crystallings / total;
            float bonus = CursePackBonus(s, crystallings);
            return (tier.crystallings * UnitStrengthOf("Crystalling", 1f + bonus)
                    + tier.veilstingers * UnitStrengthOf("Veilstinger", 1f)
                    + tier.godsplinters * UnitStrengthOf("Godsplinter", 1f)) / total;
        }

        /// <summary>How much harder a curse group of <paramref name="count"/>
        /// hits than the strength scale says, from its pack bonus.</summary>
        private static float CursePackMultiplier(BorderSettingsSO s, float now, int count)
        {
            if (count <= 1) return 1f;
            float plain = CurseUnitPowerNoPack(s, now);
            return plain > 0f ? math.max(1f, CurseUnitPower(s, now, count) / plain) : 1f;
        }

        private static float CurseUnitPowerNoPack(BorderSettingsSO s, float now)
        {
            var tier = CurseTierAt(s, now);
            if (tier == null || tier.TotalUnits <= 0) return UnitStrengthOf("Crystalling", 1f);
            return (tier.crystallings * UnitStrengthOf("Crystalling", 1f)
                    + tier.veilstingers * UnitStrengthOf("Veilstinger", 1f)
                    + tier.godsplinters * UnitStrengthOf("Godsplinter", 1f)) / tier.TotalUnits;
        }

        /// <summary>Crystalling pack damage bonus for a pack of n (each OTHER
        /// member within crystallingPackRadius adds the per-member bonus, up to
        /// the max) — the defenders stand together at their node.</summary>
        private static float CursePackBonus(BorderSettingsSO s, float crystallings)
        {
            if (s.crystallingPackRadius <= 0f || s.crystallingPackBonusPerMember <= 0f) return 0f;
            return math.min(math.max(0f, s.crystallingPackMaxBonus),
                s.crystallingPackBonusPerMember * math.max(0f, crystallings - 1f));
        }

        private static BorderSettingsSO.ArmyTier CurseTierAt(BorderSettingsSO s, float now)
        {
            if (s == null || s.TierCount == 0) return null;
            int i = (int)math.floor(now / 60f / math.max(1f, s.minutesPerTier));
            return s.Tier(math.clamp(i, 0, s.TierCount - 1));
        }

        /// <summary>A unit's strength on the TacticalQuery scale (damage x2 +
        /// hp/10), from its SO, damage scaled by <paramref name="damageMult"/>.
        /// 0 when the SO is missing (the catalog audit reports that).</summary>
        private static float UnitStrengthOf(string unitId, float damageMult)
        {
            if (!TechCatalog.TryGetUnit(unitId, out var def) || def == null) return 0f;
            return def.damage * damageMult * 2f + def.hp / 10f;
        }
    }
}
