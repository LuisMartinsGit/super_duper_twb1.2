// MatchScoreSystem.cs
// Computes every faction's Score (docs/Design/Score.md) every
// sampleIntervalSeconds of sim time and publishes it to MatchScore. Derived
// only — nothing in the simulation reads a score back.
//
//   Economy  = earned/100 x pointsPerHundredEarned
//            + incomePerMin/100 x pointsPerHundredIncomePerMinute
//   Strategy = territories x pt + fortresses x pf + techs x ptech
//            + building levels x pl + age-up bonus
//   Military = (kills x pk + razed x pr) x clamp(K/D, kdMin, kdMax)
//
// "Earned" is the EconomyLedger's lifetime income (every source but refunds
// and the untracked residual), weighted per resource; the ledger keeps
// lifetime totals for exactly this, whether or not the match metrics run.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Diagnostics;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.Systems.Core
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial class MatchScoreSystem : SystemBase
    {
        private EntityQuery _fortresses;
        private EntityQuery _levelled;
        private float _next;
        private int _epoch = -1;
        private readonly float[] _prevEarned = new float[MatchScore.Factions];
        private float _prevSampleTime;

        protected override void OnCreate()
        {
            _fortresses = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<FortressTag, FactionTag>().WithNone<UnderConstruction>().Build(this);
            _levelled = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<BuildingTag, FactionTag, BuildingUpgradeState>().Build(this);
        }

        protected override void OnUpdate()
        {
            if (!MatchSimGate.Open) return;
            var cfg = MatchScoreSystemConfig.I;
            if (cfg == null) return;

            if (_epoch != SimCadence.Epoch)
            {
                _epoch = SimCadence.Epoch;
                MatchScore.ResetForMatch();
                EconomyLedger.ResetLifetime();
                System.Array.Clear(_prevEarned, 0, _prevEarned.Length);
                _next = 0f;
                _prevSampleTime = 0f;
            }

            float now = SimClock.Now;
            if (now < _next) return;
            _next = now + math.max(1f, cfg.sampleIntervalSeconds);
            float dtMin = math.max(1f / 60f, (now - _prevSampleTime) / 60f);
            _prevSampleTime = now;

            var em = EntityManager;
            var fortCount = new int[MatchScore.Factions];
            using (var facs = _fortresses.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < facs.Length; i++)
                {
                    int f = (int)facs[i].Value;
                    if (f >= 0 && f < MatchScore.Factions) fortCount[f]++;
                }
            var levelSum = new int[MatchScore.Factions];
            using (var facs = _levelled.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var lvls = _levelled.ToComponentDataArray<BuildingUpgradeState>(Allocator.Temp))
                for (int i = 0; i < facs.Length; i++)
                {
                    int f = (int)facs[i].Value;
                    if (f >= 0 && f < MatchScore.Factions) levelSum[f] += lvls[i].Level;
                }

            var research = FactionResearchState.Instance;
            float minute = now / 60f;

            for (int f = 0; f < MatchScore.Factions; f++)
            {
                var faction = (Faction)f;
                if (!FactionEconomy.TryGetBank(em, faction, out var bank)) continue;

                if (em.HasComponent<FactionEra>(bank) && em.GetComponentData<FactionEra>(bank).Value >= 2)
                    MatchScore.NoteAgeUp(faction, minute);

                // ── Economy ──
                float earned = 0f;
                for (int s = 0; s < (int)IncomeSource.Count; s++)
                {
                    var src = (IncomeSource)s;
                    if (src == IncomeSource.Refund || src == IncomeSource.Other) continue;
                    earned += EconomyLedger.LifetimeIncomeOf(f, src, 0) * cfg.weightSupplies
                            + EconomyLedger.LifetimeIncomeOf(f, src, 1) * cfg.weightIron
                            + EconomyLedger.LifetimeIncomeOf(f, src, 2) * cfg.weightVeilstone
                            + EconomyLedger.LifetimeIncomeOf(f, src, 3) * cfg.weightVeilsteel;
                }
                float incomePerMin = math.max(0f, earned - _prevEarned[f]) / dtMin;
                _prevEarned[f] = earned;
                float economy = earned / 100f * cfg.pointsPerHundredEarned
                              + incomePerMin / 100f * cfg.pointsPerHundredIncomePerMinute;

                // ── Strategy ──
                int territories = TerritoryOwnership.Ready ? TerritoryOwnership.CountOf(faction) : 0;
                int techs = research != null ? research.GetCompletedCount(faction) : 0;
                float ageUpMinute = MatchScore.AgeUpMinuteOf(faction);
                float ageBonus = ageUpMinute >= 0f
                    ? math.max(0f, cfg.ageUpBonusMax - cfg.ageUpBonusDecayPerMinute * ageUpMinute) : 0f;
                float strategy = territories * cfg.pointsPerTerritory
                               + fortCount[f] * cfg.pointsPerFortress
                               + techs * cfg.pointsPerTech
                               + levelSum[f] * cfg.pointsPerBuildingLevel
                               + ageBonus;

                // ── Military ──
                int kills = MatchScore.KillsOf(faction), deaths = MatchScore.DeathsOf(faction);
                int razed = MatchScore.RazedOf(faction);
                float kd = kills / (float)math.max(1, deaths);
                float kdFactor = math.clamp(kd, math.min(cfg.kdFactorMin, cfg.kdFactorMax),
                                                math.max(cfg.kdFactorMin, cfg.kdFactorMax));
                float military = (kills * cfg.pointsPerKill + razed * cfg.pointsPerRazedBuilding) * kdFactor;

                // A faction with nothing earned, nothing held and nothing
                // fought is not in the match (an empty slot).
                bool live = earned > 0f || territories > 0 || kills + deaths > 0 || fortCount[f] > 0;
                var row = new ScoreRow
                {
                    Live = live,
                    Economy = economy, Strategy = strategy, Military = military,
                    Score = economy + strategy + military,
                    Earned = earned, IncomePerMin = incomePerMin,
                    Territories = territories, Fortresses = fortCount[f], Techs = techs, Levels = levelSum[f],
                    Kills = kills, Deaths = deaths, Razed = razed, Kd = kd,
                };
                MatchScore.Publish(faction, row, now);
            }
        }
    }
}
