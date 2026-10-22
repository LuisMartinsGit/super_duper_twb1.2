// MatchScoreSystem.cs
// Computes every faction's Score (docs/Design/Score.md) every
// sampleIntervalSeconds of sim time and publishes it to MatchScore. Derived
// only — nothing in the simulation reads a score back.
//
//   Score = Units + Buildings + Stockpile                  (2026-10-22)
//
//   Units      every living unit at its SO price x (combat now / combat on
//              the SO) — UnitPower.CombatOf on the live Damage /
//              AttackCooldown / Health.Max / Defense / range, so research,
//              ranks and hero levels show through the units they improved
//              and are worth nothing once those units are gone
//   Buildings  every standing building at what it was paid (wall modules at
//              the curtain price) + the faction-wide level-ups of every type
//              the faction still owns
//   Stockpile  the bank
//
// Every price is converted with the one resource ladder on the asset
// (supplies / iron / veilstone / veilsteel). A defeated faction scores 0.
// Kills, deaths, razed buildings and the tech count are still counted for
// the readers but feed no part of the Score.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Diagnostics;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Buildings;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.Systems.Core
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial class MatchScoreSystem : SystemBase
    {
        private EntityQuery _units;
        private EntityQuery _buildings;
        private EntityQuery _verdict;
        private float _next;
        private int _epoch = -1;

        protected override void OnCreate()
        {
            _units = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<FactionTag, UnitTypeId, Health>().Build(this);
            _buildings = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<BuildingTag, FactionTag, Health>()
                .WithNone<UnderConstruction, PlannedBuilding>().Build(this);
            _verdict = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<MatchVerdictState>().Build(this);
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
                // The ledger's lifetime totals are per match (the HUD's
                // lumpy-income readout reads them); this is where they reset.
                EconomyLedger.ResetLifetime();
                _next = 0f;
            }

            float now = SimClock.Now;
            if (now < _next) return;
            _next = now + math.max(1f, cfg.sampleIntervalSeconds);

            var em = EntityManager;

            // ── Who is out of the match ──
            var eliminated = new bool[MatchScore.Factions];
            if (!_verdict.IsEmptyIgnoreFilter)
            {
                using var verdicts = _verdict.ToEntityArray(Allocator.Temp);
                if (verdicts.Length > 0 && em.HasBuffer<EliminatedFactionRecord>(verdicts[0]))
                {
                    var buf = em.GetBuffer<EliminatedFactionRecord>(verdicts[0], true);
                    for (int i = 0; i < buf.Length; i++)
                    {
                        int f = (int)buf[i].Value;
                        if (f >= 0 && f < MatchScore.Factions) eliminated[f] = true;
                    }
                }
            }

            // ── Units: the living, at their SO price scaled by their stats ──
            var unitValue = new float[MatchScore.Factions];
            using (var ents = _units.ToEntityArray(Allocator.Temp))
            using (var facs = _units.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var ids = _units.ToComponentDataArray<UnitTypeId>(Allocator.Temp))
            using (var hps = _units.ToComponentDataArray<Health>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    int f = (int)facs[i].Value;
                    if (f < 0 || f >= MatchScore.Factions || hps[i].Value <= 0) continue;
                    if (!TechCatalog.TryGetUnit(ids[i].Value.ToString(), out var def) || def == null) continue;
                    unitValue[f] += Points(cfg, def.cost) * StrengthRatio(em, ents[i], def, hps[i]);
                }

            // ── Buildings: the standing, at what they were paid ──
            var buildingValue = new float[MatchScore.Factions];
            var fortCount = new int[MatchScore.Factions];
            using (var ents = _buildings.ToEntityArray(Allocator.Temp))
            using (var facs = _buildings.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var hps = _buildings.ToComponentDataArray<Health>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    int f = (int)facs[i].Value;
                    if (f < 0 || f >= MatchScore.Factions || hps[i].Value <= 0) continue;
                    var e = ents[i];
                    // A wall SEGMENT is the container of its modules; the
                    // modules (instances) carry the curtain price, the hub
                    // its own, so the segment itself is worth nothing twice.
                    if (em.HasComponent<WallSegmentTag>(e)) continue;
                    if (em.HasComponent<FortressTag>(e)) fortCount[f]++;
                    if (em.HasComponent<WallInstanceTag>(e))
                    {
                        buildingValue[f] += Points(cfg, TheWaningBorder.Core.Commands.CommandRouter
                            .WallModuleCost(em.HasComponent<PalisadeTag>(e)));
                        continue;
                    }
                    string id = TheWaningBorder.Entities.BuildingIds.Of(e, em)
                                ?? BuildCosts.IdFromEntity(em, e);
                    if (string.IsNullOrEmpty(id)) continue;
                    if (BuildCosts.TryGetPaid(em, e, id, out var paid))
                        buildingValue[f] += Points(cfg, paid);
                    else if (TechCatalog.TryGetBuilding(id, out var bdef) && bdef != null)
                        buildingValue[f] += Points(cfg, bdef.cost);
                }

            var research = FactionResearchState.Instance;

            for (int f = 0; f < MatchScore.Factions; f++)
            {
                var faction = (Faction)f;
                if (!FactionEconomy.TryGetBank(em, faction, out var bank)) continue;

                // ── Buildings, continued: the faction-wide level-ups of every
                // type it still owns (paid once per type, docs/Design/Overview.md
                // § Building levels) ──
                float levelValue = 0f;
                byte culture = CultureConfig.GetCompletedCulture(em, faction);
                if (culture != Cultures.None && em.HasBuffer<FactionBuildingLevel>(bank))
                {
                    var rows = em.GetBuffer<FactionBuildingLevel>(bank, true);
                    for (int i = 0; i < rows.Length; i++)
                    {
                        if (rows[i].Level < 2) continue;
                        string ladder = rows[i].BuildingId.ToString();
                        if (FactionBuildingLevels.CountOwned(em, faction, ladder) <= 0) continue;
                        for (int l = 2; l <= rows[i].Level; l++)
                            if (TechCatalog.TryGetBuildingLevel(culture, ladder, l, out var lvl) && lvl != null)
                                levelValue += Points(cfg, lvl.upgradeCost);
                    }
                }

                // ── Stockpile ──
                float stock = 0f;
                if (em.HasComponent<FactionResources>(bank))
                {
                    var r = em.GetComponentData<FactionResources>(bank);
                    stock = r.Supplies * cfg.weightSupplies + r.Iron * cfg.weightIron
                          + r.Veilstone * cfg.weightVeilstone + r.Veilsteel * cfg.weightVeilsteel;
                }

                // ── Shown, not scored ──
                int kills = MatchScore.KillsOf(faction), deaths = MatchScore.DeathsOf(faction);
                int razed = MatchScore.RazedOf(faction);
                float kd = kills / (float)math.max(1, deaths);
                int territories = TerritoryOwnership.Ready ? TerritoryOwnership.CountOf(faction) : 0;
                int techs = research != null ? research.GetCompletedCount(faction) : 0;

                float units = unitValue[f];
                float buildings = buildingValue[f] + levelValue;
                if (eliminated[f]) { units = 0f; buildings = 0f; stock = 0f; }
                float score = units + buildings + stock;

                // A faction with nothing on the map, nothing banked and
                // nothing fought is not in the match (an empty slot).
                bool live = score > 0f || eliminated[f] || kills + deaths > 0 || territories > 0;
                var row = new ScoreRow
                {
                    Live = live, Eliminated = eliminated[f],
                    Units = units, Buildings = buildings, Stockpile = stock,
                    Score = score,
                    Territories = territories, Fortresses = fortCount[f], Techs = techs,
                    Kills = kills, Deaths = deaths, Razed = razed, Kd = kd,
                };
                MatchScore.Publish(faction, row, now);
            }
        }

        /// <summary>
        /// How much stronger the unit is now than its SO says: the Power
        /// number's strength term on the live components the tech effects
        /// write (Damage, AttackCooldown, Health.Max, Defense, ArcherState
        /// range) over the same term on the SO. 1 when the SO has no
        /// measurable output (a Scout, a Ledger) or the entity lacks the
        /// components — the unit then counts at its plain price.
        /// </summary>
        private static float StrengthRatio(EntityManager em, Entity e, UnitDef def, in Health hp)
        {
            float baseDef = def.defense == null ? 0f
                : (def.defense.melee + def.defense.ranged + def.defense.siege + def.defense.magic) / 4f;
            float baseRange = math.max(def.attackRange, def.siegeRange);
            float baseCombat = UnitPower.CombatOf(def.damage, def.attackCooldown, def.aimTime, def.aoeRadius,
                def.healsPerSecond, def.buildSpeed, baseDef, def.hp, baseRange, out _, out _);
            if (baseCombat <= 0f) return 1f;

            float damage = em.HasComponent<Damage>(e) ? em.GetComponentData<Damage>(e).Value : def.damage;
            float cooldown = em.HasComponent<AttackCooldown>(e)
                ? em.GetComponentData<AttackCooldown>(e).Cooldown : def.attackCooldown;
            float avgDef = baseDef;
            if (em.HasComponent<Defense>(e))
            {
                var d = em.GetComponentData<Defense>(e);
                avgDef = (d.Melee + d.Ranged + d.Siege + d.Magic) / 4f;
            }
            float range = em.HasComponent<ArcherState>(e)
                ? math.max(em.GetComponentData<ArcherState>(e).MaxRange, def.siegeRange) : baseRange;
            float liveCombat = UnitPower.CombatOf(damage, cooldown, def.aimTime, def.aoeRadius,
                def.healsPerSecond, def.buildSpeed, avgDef, hp.Max, range, out _, out _);
            return liveCombat <= 0f ? 1f : liveCombat / baseCombat;
        }

        private static float Points(MatchScoreSystemConfig cfg, CostBlock c)
            => c == null ? 0f
             : c.Supplies * cfg.weightSupplies + c.Iron * cfg.weightIron
             + c.Veilstone * cfg.weightVeilstone + c.Veilsteel * cfg.weightVeilsteel;

        private static float Points(MatchScoreSystemConfig cfg, Cost c)
            => c.Supplies * cfg.weightSupplies + c.Iron * cfg.weightIron
             + c.Veilstone * cfg.weightVeilstone + c.Veilsteel * cfg.weightVeilsteel;
    }
}
