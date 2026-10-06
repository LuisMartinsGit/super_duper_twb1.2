// SimpleAISystem.Composition.cs
// The Alanthor army plan: every unit by ROLE against the army the faction has
// actually scouted, the dynamic basics share and its cap, the army's
// veilstone claim and the King Lexor priority. Partial of SimpleAISystem.cs.
// docs/Design/Game_AI.md § 5d is the design; SimpleAISystem.asset
// (compositionRoles) carries every number.
//
// ─────────────────────────────────────────────────────────────────────────
// WHY (2026-10-03, the 0.0.33 60-minute batch, then the developer's design)
//
// End-of-match rosters were Spearman + Archer and almost nothing else. The
// first fix planned the army as five "lines" and trained the best unlocked
// tier of each — but Swordsman, Nobleman and Sentinel are not tiers of one
// line, and neither are Archer, Crossbowman and Longbowman. Each one has a job
// and a counter (docs/Design/Combat_Pacing.md — the counter table and the
// weight tags; the numbers are the unit SOs' bonusVsTags / tags / defense),
// and the right army is the one that answers what the enemy is fielding.
//
// THE ROLES (what each row of the role table is FOR):
//
//   Spearman     anti-cavalry (+Cavalry bonus). Cheap, veilstone-free, and the
//                one thing cavalry loses to — kept ALL GAME, scaled by the
//                enemy's cavalry, with a baseline even without intel. A basic.
//   Archer       anti-infantry (+Infantry bonus). Cheap massed bows that clear
//                a foot line; the wrong answer to armour (8 damage into 4-7
//                points of it) and to cavalry (no melee armour). A basic.
//   Swordsman    mail line infantry with the anti-SIEGE bonus: the body of
//                the line, and how an enemy siege train dies.
//   Nobleman     elite shock infantry — the highest melee damage and the
//                fastest foot, no bonus: the gap-closer onto an enemy bow
//                line when cavalry is short, and a generalist brawler.
//   Sentinel     the heaviest foot armour with the anti-HEAVY bonus: the
//                answer to heavy infantry, barded cavalry and anything an
//                arrow cannot hurt. Slow, two population.
//   Crossbowman  ranged anti-cavalry (+Cavalry bonus that ignores barding):
//                the Cataphract answer.
//   Longbowman   long-range bow DPS, no bonus — outranges every other bow and
//                shreds slow foot and masses; glass against cavalry.
//   Outrider     light cavalry raider (+Ranged), the fastest unit: runs down
//                bows and unescorted siege, harasses.
//   Cataphract   heavy shock cavalry (+Ranged): breaks bow lines and siege
//                trains; walks into spears and crossbows.
//   Battering Ram  EARLY anti-building (buildings only). Its share moves to
//                the Trebuchet once the Siege Yard can train one.
//   Trebuchet    LATE anti-building / anti-fortification, the longest reach.
//   Ballista     ANTI-ARMY siege, single target: heroes, heavy cavalry and
//                enemy engines (its auto-acquire prefers them — unit SO
//                preferTargets, Combat_Pacing.md § Target preference).
//   Catapult     ANTI-ARMY siege, splash (+Infantry): clumped foot and
//                formations (prefers the densest knot of enemies in reach).
//
// HOW A SHARE IS MADE (all numbers in assets):
//   1. INTEL. share = baseShare + counterResponse x sum(perEnemyX x enemy X
//      fraction), the enemy read taken from the faction's fresh sightings
//      (EnemySightingRecord, strength-weighted): cavalry, heavy cavalry,
//      infantry, ranged, heavy, armoured, siege, high-value, massed.
//      counterResponse is the DIFFICULTY's (AIDifficultyProfileSO).
//   2. PERSONALITY. Each class (infantry / ranged / cavalry / siege) is scaled
//      by the personality's RoleBudget relative to Balanced; the basics by its
//      basicsAppetite (AISettingsSO.PersonalityBlock).
//   3. DIFFICULTY. The basics by basicsShareScale.
//   4. ECONOMY. The veilstone roles by the veilstone income the Trading
//      Outposts deliver (veilstoneIncomeForFullLadder) — a starved faction
//      leans on the basics, a rich one on the roles.
//   5. LOCKS. A role its trainer cannot train yet hands its share down its
//      fallback chain (Sentinel -> Swordsman -> Spearman ...); a superseded
//      role (Ram -> Trebuchet) hands it up.
//   6. The basics' total is clamped into [basicsMinShare, basicsMaxShare].
// The next unit is the role furthest below its share. The basics may number
// the larger of basicsFloor and their share of the army — a HARD cap (the
// developer's 2026-10-04 ruling: the share is fixed even with population
// free). At the cap the AI WAITS and SAVES for the role unit; nothing lets a
// basic past it (the overflow valve and the "basics fill" are gone). A bank
// piling up supplies and iron meanwhile spends on veilstone and upgrades
// (SimpleAISystem.Surplus.cs, Game_AI.md 5e).
// While a role is behind, AIBudget earmarks militaryVeilstoneShare of the
// veilstone income for the army; King Lexor outranks every other unit.
// ─────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Data.AI;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        // ── The enemy read ───────────────────────────────────────────────

        private const int ReadCavalry = 0, ReadHeavyCavalry = 1, ReadInfantry = 2, ReadRanged = 3,
                          ReadHeavy = 4, ReadArmoured = 5, ReadSiege = 6, ReadHighValue = 7,
                          ReadMassed = 8, ReadCount = 9;

        private static readonly string[] ReadNames =
            { "cav", "heavy cav", "infantry", "ranged", "heavy", "armoured", "siege", "high-value", "massed" };

        private static float Coef(SimpleAISystemConfig.CompositionRole r, int k) => k switch
        {
            ReadCavalry      => r.perEnemyCavalry,
            ReadHeavyCavalry => r.perEnemyHeavyCavalry,
            ReadInfantry     => r.perEnemyInfantry,
            ReadRanged       => r.perEnemyRanged,
            ReadHeavy        => r.perEnemyHeavy,
            ReadArmoured     => r.perEnemyArmoured,
            ReadSiege        => r.perEnemySiege,
            ReadHighValue    => r.perEnemyHighValue,
            _                => r.perEnemyMassed,
        };

        // ── Role classes (for the personality's RoleBudget) ─────────────

        private const int ClassInfantry = 0, ClassRanged = 1, ClassCavalry = 2, ClassSiege = 3, ClassCount = 4;

        /// <summary>A unit's class from its SO tags (Siege before Ranged:
        /// the engines carry both).</summary>
        private static int RoleClassOf(string unitId)
        {
            if (!TechCatalog.TryGetUnit(unitId, out var def) || def == null) return ClassInfantry;
            uint m = UnitTagParse.Mask(def.tags);
            if ((m & (uint)UnitTagBits.Siege) != 0) return ClassSiege;
            if ((m & (uint)UnitTagBits.Cavalry) != 0) return ClassCavalry;
            if ((m & (uint)UnitTagBits.Ranged) != 0) return ClassRanged;
            return ClassInfantry;
        }

        /// <summary>
        /// The think-stable plan: who is alive or queued in each role, which
        /// roles can train, the enemy read and every share. Counts are bumped
        /// as this think issues orders, so several picks inside one think
        /// converge instead of repeating.
        /// </summary>
        private sealed class ArmyPlan
        {
            public int Stamp = -1;
            public Faction F;
            /// <summary>Alanthor, aged up, at least one veilstone role trainable.</summary>
            public bool Active;

            public SimpleAISystemConfig.CompositionRole[] Rows;
            public int N;
            public FixedString64Bytes[] Keys = new FixedString64Bytes[0];
            public int[] FallbackIdx = new int[0], SupIdx = new int[0], Class = new int[0];
            public int[] Count = new int[0];          // alive + queued
            public bool[] Trainable = new bool[0];
            public float[] Raw = new float[0];        // after intel, personality, difficulty, economy
            public float[] Share = new float[0];      // resolved and normalised
            public int[] Reason = new int[0];         // dominant enemy term (-1 = baseline)
            public float[] ReasonFrac = new float[0];
            public int[] Via = new int[0];            // the row whose share this mostly is

            public int Army, Basics;
            public readonly float[] Enemy = new float[ReadCount];
            public int EnemySightings;
            public bool EnemyRead;

            public float BasicsShare, EconScale, VeilstoneIncome, CounterResponse, DiffBasics, PersBasics;
            /// <summary>The surplus-resource tilt is on this think (§ 5a glut rule).</summary>
            public bool GlutTilt;

            public bool LexorOwed;          // capital can train him, none alive or queued
            public Cost LexorCost;
            public int LexorPop;
            public string SavingFor;        // last role unit the picker could not buy

            public void EnsureSize(int n)
            {
                if (Keys.Length == n) return;
                Keys = new FixedString64Bytes[n];
                FallbackIdx = new int[n]; SupIdx = new int[n]; Class = new int[n];
                Count = new int[n]; Trainable = new bool[n];
                Raw = new float[n]; Share = new float[n];
                Reason = new int[n]; ReasonFrac = new float[n]; Via = new int[n];
            }
        }

        private static readonly ArmyPlan _armyPlan = new ArmyPlan();

        private static int RowIndex(ArmyPlan p, string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return -1;
            for (int r = 0; r < p.N; r++)
                if (p.Rows[r] != null && p.Rows[r].unitId == unitId) return r;
            return -1;
        }

        private static int RowIndex(ArmyPlan p, in FixedString64Bytes id)
        {
            for (int r = 0; r < p.N; r++)
                if (p.Keys[r] == id) return r;
            return -1;
        }

        private static bool IsBasicRow(ArmyPlan p, int r) => r >= 0 && p.Rows[r].basic;

        /// <summary>1 + tilt x (1 - 2 x the unit's supplies share of its
        /// cost), floored at 0.1: a unit paid only in iron/veilstone gets
        /// 1 + tilt, one paid only in supplies 1 - tilt.</summary>
        private static float GlutTiltFactor(string unitId, float tilt)
        {
            if (string.IsNullOrEmpty(unitId)) return 1f;
            if (!TechCatalog.TryGetUnit(unitId, out var def) || def?.cost == null) return 1f;
            float total = def.cost.Supplies + def.cost.Iron + def.cost.Veilstone + def.cost.Veilsteel;
            if (total <= 0f) return 1f;
            return math.max(0.1f, 1f + tilt * (1f - 2f * def.cost.Supplies / total));
        }

        private static string ShortName(string unitId)
            => unitId != null && unitId.StartsWith("Alanthor_") ? unitId.Substring(9) : unitId;

        // ── Building the plan ────────────────────────────────────────────

        /// <summary>The plan for this think (memoised per think and faction).</summary>
        private static ArmyPlan GetArmyPlan(EntityManager em, Faction faction)
        {
            var p = _armyPlan;
            if (p.Stamp == _thinkStamp && p.F == faction) return p;
            p.Stamp = _thinkStamp;
            p.F = faction;
            p.Active = false;
            p.Army = p.Basics = 0;
            p.LexorOwed = false;
            p.SavingFor = null;
            p.EnemySightings = 0;
            p.EnemyRead = false;
            for (int k = 0; k < ReadCount; k++) p.Enemy[k] = 0f;

            var rows = Cfg.compositionRoles;
            p.Rows = rows;
            p.N = rows != null ? rows.Length : 0;
            p.EnsureSize(p.N);
            for (int r = 0; r < p.N; r++)
            {
                p.Count[r] = 0; p.Trainable[r] = false; p.Raw[r] = 0f; p.Share[r] = 0f;
                p.Reason[r] = -1; p.ReasonFrac[r] = 0f; p.Via[r] = r;
            }

            if (p.N == 0 || !TechCatalog.IsReady) return p;
            if (FactionCultureOf(em, faction) != Cultures.Alanthor) return p;
            if (FactionEra(em, faction) < 2) return p;

            for (int r = 0; r < p.N; r++)
            {
                var row = rows[r];
                p.Keys[r] = new FixedString64Bytes(row?.unitId ?? "");
                p.FallbackIdx[r] = RowIndex(p, row?.fallbackUnitId);
                p.SupIdx[r] = RowIndex(p, row?.supersededByUnitId);
                p.Class[r] = RoleClassOf(row?.unitId);
                p.Trainable[r] = row != null && FirstTrainableOrNull(em, faction, row.unitId) != null;
            }

            // Alive...
            var q = QC_UnitTypeIdFactionTag.Get(em, QT_UnitTypeIdFactionTag);
            using (var uids = q.ToComponentDataArray<UnitTypeId>(Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < uids.Length; i++)
                    if (facs[i].Value == faction) CountIntoPlan(p, uids[i].Value);

            // ...and queued, so a burst of orders is not re-issued while the
            // units are still in the queues.
            var pq = QC_FactionTagResearchQueueItem.Get(em, QT_FactionTagResearchQueueItem);
            using (var ents = pq.ToEntityArray(Allocator.Temp))
            using (var facs = pq.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    if (facs[i].Value != faction) continue;
                    var buf = em.GetBuffer<ProductionQueueItem>(ents[i]);
                    for (int j = 0; j < buf.Length; j++)
                        if (buf[j].Kind == ProductionKind.Train) CountIntoPlan(p, buf[j].Id);
                }

            // Who is deciding: the faction's brain carries its difficulty and
            // personality (layer 1 and layer 2) and its sightings.
            Entity brain = BrainOf(em, faction);
            var brainData = brain != Entity.Null ? em.GetComponentData<AIBrain>(brain) : default;
            var profile = AISimpleDifficulty.GetProfile(brainData.Difficulty);
            var personality = AISettings.Get().For(brainData.Personality,
                AISimpleDifficulty.GetProfile(brainData.Difficulty).PersonalityWeight);

            ReadEnemy(em, brain, profile.IntelFreshnessSeconds, p);
            ComputeShares(em, faction, p, profile, personality);

            for (int r = 0; r < p.N; r++)
            {
                p.Army += p.Count[r];
                if (rows[r].basic) p.Basics += p.Count[r];
                else if (p.Trainable[r] && p.Share[r] > 0f) p.Active = true;
            }

            // King Lexor: owed once a capital can train him and none is alive
            // or queued (a dead king is owed again — the hero rules re-train).
            if (TechCatalog.TryGetUnit("King Lexor", out var lexor) && lexor != null
                && !TheWaningBorder.Abilities.HeroTrainLimit.HasLiveOrQueuedKingLexor(em, faction))
            {
                Entity seat = FindCapitalTrainer(em, faction, "King Lexor");
                if (seat != Entity.Null && !em.HasComponent<UnderConstruction>(seat)
                    && CommandRouter.CanTrainAtBuilding(em, seat, "King Lexor", out _, out _))
                {
                    p.LexorOwed = true;
                    p.LexorCost = AICommon.ToCost(lexor.cost);
                    p.LexorPop = UnitFactory.GetPopulationCost("King Lexor");
                }
            }
            return p;
        }

        private static void CountIntoPlan(ArmyPlan p, in FixedString64Bytes id)
        {
            int r = RowIndex(p, id);
            if (r >= 0) p.Count[r]++;
        }

        static readonly ComponentType[] QT_AIBrainOnly = { ComponentType.ReadOnly<AIBrain>() };
        static CachedEntityQuery QC_AIBrainOnly;

        /// <summary>The brain entity that owns <paramref name="faction"/>
        /// (Entity.Null when none — a human faction).</summary>
        private static Entity BrainOf(EntityManager em, Faction faction)
        {
            var q = QC_AIBrainOnly.Get(em, QT_AIBrainOnly);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var brains = q.ToComponentDataArray<AIBrain>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (brains[i].Owner == faction) return ents[i];
            return Entity.Null;
        }

        /// <summary>
        /// What the faction has SEEN of the enemy army, as strength-weighted
        /// fractions (fog-honest: only the brain's own sightings). "Fresh"
        /// is measured against the newest sighting in the buffer — the
        /// sightings are stamped on the intel system's clock, not the think's.
        /// </summary>
        private static void ReadEnemy(EntityManager em, Entity brain, float freshness, ArmyPlan p)
        {
            if (brain == Entity.Null || !em.HasBuffer<EnemySightingRecord>(brain)) return;
            var buffer = em.GetBuffer<EnemySightingRecord>(brain);
            float newest = float.MinValue;
            for (int i = 0; i < buffer.Length; i++)
                if (buffer[i].LastSeenTime > newest) newest = buffer[i].LastSeenTime;

            float cell = math.max(1f, Cfg.massedCellSize);
            float total = 0f;
            var sums = _readSums;
            for (int k = 0; k < ReadCount; k++) sums[k] = 0f;
            var cells = new NativeHashMap<int2, int>(64, Allocator.Temp);

            // Pass 1: classes, and how many foot stand in each cell.
            for (int i = 0; i < buffer.Length; i++)
            {
                var rec = buffer[i];
                if (rec.Category != IntelCategory.MilitaryUnit) continue;
                if (newest - rec.LastSeenTime > freshness) continue;
                if (!em.Exists(rec.Enemy) || !em.HasComponent<UnitTag>(rec.Enemy)) continue;
                float s = math.max(1, rec.EstStrength);
                total += s;
                p.EnemySightings++;
                uint m = em.HasComponent<UnitTagsData>(rec.Enemy) ? em.GetComponentData<UnitTagsData>(rec.Enemy).Mask : 0u;
                bool cav = (m & (uint)UnitTagBits.Cavalry) != 0 || em.HasComponent<CavalryTag>(rec.Enemy);
                bool heavy = (m & (uint)UnitTagBits.Heavy) != 0;
                bool siege = (m & (uint)UnitTagBits.Siege) != 0;
                bool ranged = !siege && (m & (uint)UnitTagBits.Ranged) != 0;
                bool inf = !cav && (m & (uint)UnitTagBits.Infantry) != 0;
                bool hero = em.HasComponent<HeroLevel>(rec.Enemy);
                bool armoured = em.HasComponent<Defense>(rec.Enemy)
                    && em.GetComponentData<Defense>(rec.Enemy).Ranged >= Cfg.armouredRangedDefense;
                if (cav) sums[ReadCavalry] += s;
                if (cav && heavy) sums[ReadHeavyCavalry] += s;
                if (inf) sums[ReadInfantry] += s;
                if (ranged) sums[ReadRanged] += s;
                if (heavy) sums[ReadHeavy] += s;
                if (armoured) sums[ReadArmoured] += s;
                if (siege) sums[ReadSiege] += s;
                if (hero || (cav && heavy) || siege) sums[ReadHighValue] += s;
                if (inf || ranged)
                {
                    var key = new int2((int)math.floor(rec.Position.x / cell), (int)math.floor(rec.Position.z / cell));
                    cells.TryGetValue(key, out int n);
                    cells[key] = n + 1;
                }
            }

            // Pass 2: the foot standing massed. A sum, so the hash map's
            // order never matters.
            for (int i = 0; i < buffer.Length; i++)
            {
                var rec = buffer[i];
                if (rec.Category != IntelCategory.MilitaryUnit) continue;
                if (newest - rec.LastSeenTime > freshness) continue;
                if (!em.Exists(rec.Enemy) || !em.HasComponent<UnitTag>(rec.Enemy)) continue;
                uint m = em.HasComponent<UnitTagsData>(rec.Enemy) ? em.GetComponentData<UnitTagsData>(rec.Enemy).Mask : 0u;
                bool cav = (m & (uint)UnitTagBits.Cavalry) != 0 || em.HasComponent<CavalryTag>(rec.Enemy);
                bool siege = (m & (uint)UnitTagBits.Siege) != 0;
                bool foot = !cav && (!siege && (m & (uint)(UnitTagBits.Ranged | UnitTagBits.Infantry)) != 0);
                if (!foot) continue;
                var key = new int2((int)math.floor(rec.Position.x / cell), (int)math.floor(rec.Position.z / cell));
                if (cells.TryGetValue(key, out int n) && n >= Cfg.massedMinUnits)
                    sums[ReadMassed] += math.max(1, rec.EstStrength);
            }

            cells.Dispose();

            p.EnemyRead = p.EnemySightings >= Cfg.enemyReadMinSightings && total > 0f;
            if (!p.EnemyRead) return;
            for (int k = 0; k < ReadCount; k++) p.Enemy[k] = math.saturate(sums[k] / total);
        }

        /// <summary>
        /// Every role's share: intel, personality, difficulty, economy, then
        /// locks, normalisation and the basics clamp. See the file header.
        /// </summary>
        private static void ComputeShares(EntityManager em, Faction faction, ArmyPlan p,
            in AIDifficultyProfile profile, AISettingsSO.PersonalityBlock personality)
        {
            var rows = p.Rows;
            p.CounterResponse = math.max(0f, profile.CounterResponse);
            p.DiffBasics = math.max(0f, profile.BasicsShareScale);
            p.PersBasics = personality != null ? math.max(0f, personality.basicsAppetite) : 1f;

            // PERSONALITY by class: its RoleBudget relative to Balanced's, so
            // a Balanced AI runs the table as authored.
            var budget = RoleBudget.For(personality != null ? personality.personality : AIPersonality.Balanced);
            var balanced = RoleBudget.For(AIPersonality.Balanced);
            var classFactor = _classFactor;
            classFactor[ClassRanged]  = Ratio(budget.RangedFrac, balanced.RangedFrac);
            classFactor[ClassCavalry] = Ratio(budget.CavalryFrac, balanced.CavalryFrac);
            classFactor[ClassSiege]   = Ratio(budget.SiegeFrac, balanced.SiegeFrac);
            classFactor[ClassInfantry] = Ratio(
                1f - budget.RangedFrac - budget.CavalryFrac - budget.SiegeFrac,
                1f - balanced.RangedFrac - balanced.CavalryFrac - balanced.SiegeFrac);

            // ECONOMY: the veilstone the Outposts deliver a minute (on Buy),
            // or a bank that already holds plenty.
            p.VeilstoneIncome = OutpostVeilstoneIncome(em, faction);
            float econ = Cfg.veilstoneIncomeForFullLadder > 0f
                ? math.saturate(p.VeilstoneIncome / Cfg.veilstoneIncomeForFullLadder) : 1f;
            if (FactionEconomy.TryGetResources(em, faction, out var bank)
                && Cfg.veilstoneBankForFullLadder > 0f && bank.Veilstone >= Cfg.veilstoneBankForFullLadder)
                econ = 1f;
            p.EconScale = math.lerp(math.saturate(Cfg.ladderScaleWhenStarved), 1f, econ);

            // THE BASICS SCALE IS NOT A WAY TO SHRINK THE ARMY (2026-10-04,
            // Game_AI.md § 5h). A tier below 1 leans on role units — but only
            // while veilstone flows. Starved (econ < 1), the role units cannot
            // be bought anyway, and the hard basics cap made Expert's 0.75 a
            // smaller army, not a better one: lift it toward 1 by the same
            // amount the ladder is starved.
            if (p.DiffBasics < 1f) p.DiffBasics = math.lerp(1f, p.DiffBasics, econ);

            // THE GLUT TILT (2026-10-05, Game_AI.md § 5a): veilstone or iron
            // piling up while the army is short of SUPPLIES — weigh the roles
            // paid mostly in the surplus up, the supply-heavy ones down. The
            // basics clamp and the locks below still apply.
            float tilt = math.max(0f, Cfg.glutCompositionTilt);
            p.GlutTilt = tilt > 0f && AIBudget.IsMilitaryShort(faction, AIBudget.ResSupplies)
                && (OutpostBuyOff(faction)
                    || (FactionEconomy.TryGetResources(em, faction, out var glutBank)
                        && glutBank.Iron >= Cfg.glutIronAbove));

            // 1-4: raw shares.
            for (int r = 0; r < p.N; r++)
            {
                var row = rows[r];
                if (row == null) continue;
                float v = row.baseShare;
                float bestTerm = 0f;
                if (p.EnemyRead)
                    for (int k = 0; k < ReadCount; k++)
                    {
                        float t = Coef(row, k) * p.Enemy[k] * p.CounterResponse;
                        v += t;
                        if (t > bestTerm) { bestTerm = t; p.Reason[r] = k; p.ReasonFrac[r] = p.Enemy[k]; }
                    }
                v = math.max(0f, v) * classFactor[p.Class[r]];
                v *= row.basic ? p.DiffBasics * p.PersBasics : p.EconScale;
                if (p.GlutTilt) v *= GlutTiltFactor(row.unitId, tilt);
                p.Raw[r] = v;
            }

            // 5: locks — each row's share flows to the unit that can do its
            // job now. Via records whose share a role mostly carries.
            if (_biggest.Length < p.N) _biggest = new float[p.N];
            var biggest = _biggest;
            for (int r = 0; r < p.N; r++) biggest[r] = 0f;
            for (int r = 0; r < p.N; r++)
            {
                if (p.Raw[r] <= 0f) continue;
                int t = ResolveRow(p, r);
                if (t < 0) continue;
                p.Share[t] += p.Raw[r];
                if (p.Raw[r] > biggest[t]) { biggest[t] = p.Raw[r]; p.Via[t] = r; }
            }

            float sum = 0f;
            for (int r = 0; r < p.N; r++) sum += p.Share[r];
            if (sum <= 0f) { p.BasicsShare = 0f; return; }
            for (int r = 0; r < p.N; r++) p.Share[r] /= sum;

            // 6: the basics clamp.
            float b = 0f;
            for (int r = 0; r < p.N; r++) if (rows[r].basic) b += p.Share[r];
            float lo = math.saturate(Cfg.basicsMinShare), hi = math.saturate(math.max(Cfg.basicsMaxShare, lo));
            float target = math.clamp(b, lo, hi);
            if (b > 0f && b < 1f && target != b)
            {
                float kb = target / b, kn = (1f - target) / (1f - b);
                for (int r = 0; r < p.N; r++) p.Share[r] *= rows[r].basic ? kb : kn;
                b = target;
            }
            p.BasicsShare = b;
        }

        // Host-only scratch for the plan build (no per-think garbage).
        private static readonly float[] _readSums = new float[ReadCount];
        private static readonly float[] _classFactor = new float[ClassCount];
        private static float[] _biggest = new float[0];

        private static float Ratio(float a, float b) => b > 0.0001f ? math.max(0f, a) / b : 1f;

        /// <summary>The row whose unit takes this row's share right now:
        /// its superseder once that can train, else itself, else down its
        /// fallback chain. -1 = nothing on the chain can train.</summary>
        private static int ResolveRow(ArmyPlan p, int r)
        {
            int idx = r;
            for (int d = 0; d <= p.N && idx >= 0; d++)
            {
                int sup = p.SupIdx[idx];
                if (sup >= 0 && p.Trainable[sup]) return sup;
                if (p.Trainable[idx]) return idx;
                idx = p.FallbackIdx[idx];
            }
            return -1;
        }

        /// <summary>Veilstone a minute this faction's Trading Outposts
        /// deliver at the Buy rate (the trade the AI runs while the army is
        /// short), speed research included.</summary>
        private static float OutpostVeilstoneIncome(EntityManager em, Faction faction)
        {
            var q = QC_Outposts.Get(em, QT_Outposts);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int mine = 0;
            for (int i = 0; i < facs.Length; i++) if (facs[i].Value == faction) mine++;
            if (mine == 0) return 0f;
            TheWaningBorder.Systems.Economy.TradingOutpostSystem.PerMinute(
                faction, TradeRecipe.BuyVeilstone, out _, out var earn);
            return mine * earn.Veilstone;
        }

        /// <summary>An order went out this think: count it, so the next pick
        /// in the same think sees it.</summary>
        private static void NoteArmyPlanTrained(Faction faction, string unitId)
        {
            var p = _armyPlan;
            if (p.Stamp != _thinkStamp || p.F != faction) return;
            if (TheWaningBorder.Abilities.HeroTrainLimit.IsKingLexorId(unitId)) p.LexorOwed = false;
            if (!p.Active) return;
            int r = RowIndex(p, unitId);
            if (r < 0) return;
            p.Count[r]++;
            p.Army++;
            if (p.Rows[r].basic) p.Basics++;
        }

        // ── The pick ─────────────────────────────────────────────────────

        /// <summary>
        /// THE ALANTHOR PICK (replaces the spread / step-down tail for an
        /// active plan). The role furthest below its share — lowest
        /// (count + 1) / share, strict &lt; over the table order, so the
        /// choice is deterministic. Returns the unit to train next — or, when
        /// the army is saving, the role unit it is saving for (the train
        /// attempt then fails on the bank and nothing cheaper is bought in
        /// its place).
        /// </summary>
        private static string PickPlannedUnit(EntityManager em, Faction faction, ArmyPlan p)
        {
            int bestRole = -1, bestBasic = -1;
            float roleRatio = float.MaxValue, basicRatio = float.MaxValue;
            for (int r = 0; r < p.N; r++)
            {
                if (!p.Trainable[r] || p.Share[r] <= 0.001f) continue;
                float ratio = (p.Count[r] + 1) / p.Share[r];
                if (p.Rows[r].basic) { if (ratio < basicRatio) { basicRatio = ratio; bestBasic = r; } }
                else if (ratio < roleRatio) { roleRatio = ratio; bestRole = r; }
            }

            // A basic is the role most behind (the spear wall against a
            // cavalry army, say): it goes, inside the cap.
            if (bestBasic >= 0 && basicRatio < roleRatio && BasicAllowed(p))
            {
                p.SavingFor = null;
                return p.Rows[bestBasic].unitId;
            }

            if (bestRole >= 0)
            {
                string unit = p.Rows[bestRole].unitId;
                if (CanBuyPlannedNow(em, faction, unit, p))
                {
                    p.SavingFor = null;
                    return unit;
                }
                p.SavingFor = unit;
            }

            // The role unit cannot be paid for right now: a basic, if the
            // cap lets one through. At the cap the army waits for the role
            // unit — the cap is never exceeded (Game_AI.md 5d).
            if (bestBasic >= 0 && BasicAllowed(p))
                return p.Rows[bestBasic].unitId;

            // Saving. Name the role unit; its train attempt fails on the
            // bank and records the shortage (AIBudget.NoteMilitaryShort),
            // which keeps the Trading Outposts on Buy.
            if (bestRole >= 0) return p.Rows[bestRole].unitId;
            if (p.LexorOwed) p.SavingFor = "King Lexor";
            return bestBasic >= 0 ? p.Rows[bestBasic].unitId : "Spearman";
        }

        /// <summary>The siege role most below its share (the siege program
        /// trains it), or null when no engine can train.</summary>
        private static string PlannedSiegeUnit(ArmyPlan p)
        {
            int best = -1;
            float bestRatio = float.MaxValue;
            for (int r = 0; r < p.N; r++)
            {
                if (p.Class[r] != ClassSiege || !p.Trainable[r] || p.Share[r] <= 0.001f) continue;
                float ratio = (p.Count[r] + 1) / p.Share[r];
                if (ratio < bestRatio) { bestRatio = ratio; best = r; }
            }
            return best >= 0 ? p.Rows[best].unitId : null;
        }

        /// <summary>Engines alive or queued.</summary>
        private static int PlannedSiegeCount(ArmyPlan p)
        {
            int n = 0;
            for (int r = 0; r < p.N; r++) if (p.Class[r] == ClassSiege) n += p.Count[r];
            return n;
        }

        // ── The basics cap ───────────────────────────────────────────────

        /// <summary>Basics allowed under the cap right now: the plan's
        /// dynamic basics share of the army, never less than basicsFloor so
        /// a small army can start. The floor is a minimum, NOT an allowance
        /// added on top of the share (that let a 100-strong army field 38%
        /// basics against a 22% share).</summary>
        private static int BasicsAllowed(ArmyPlan p)
            => math.max(Cfg.basicsFloor, (int)(p.BasicsShare * p.Army));

        /// <summary>Is another basic allowed by the cap? A HARD cap while the
        /// plan is active (Game_AI.md 5d, 2026-10-04): no valve, no fill —
        /// past it the army saves for the role unit.</summary>
        private static bool BasicAllowed(ArmyPlan p)
            => !p.Active || p.Basics < BasicsAllowed(p);

        /// <summary>True while an active Alanthor plan has its basics at the
        /// cap — any fallback that would name a basic must not fire.</summary>
        private static bool BasicsCapped(EntityManager em, Faction faction)
        {
            if (FactionCultureOf(em, faction) != Cultures.Alanthor) return false;
            var p = GetArmyPlan(em, faction);
            return p.Active && !BasicAllowed(p);
        }

        /// <summary>Can the army buy this role unit this instant: the bank
        /// covers it, no savings hold stops it, and it does not eat the
        /// veilstone King Lexor is waiting on.</summary>
        private static bool CanBuyPlannedNow(EntityManager em, Faction faction, string unitId, ArmyPlan p)
        {
            if (!TechCatalog.TryGetUnit(unitId, out var def) || def == null) return false;
            var cost = AICommon.ToCost(def.cost);
            if (!FactionEconomy.CanAfford(em, faction, cost)) return false;
            if (MilitaryHold(em, faction, unitId, cost)) return false;
            return LexorGate(em, faction, unitId, cost, p) == null;
        }

        /// <summary>
        /// The pivotal savings hold as it applies to a unit: the capital
        /// uniques are themselves pivotal purchases and are never held; a
        /// combat unit is judged only on the veilstone the army's earmark
        /// does not cover (the earmark is the army's, the save cannot have it).
        /// </summary>
        private static bool MilitaryHold(EntityManager em, Faction faction, string unitId, Cost cost)
        {
            if (TheWaningBorder.Abilities.HeroTrainLimit.IsKingLexorId(unitId)
                || TheWaningBorder.Abilities.HeroTrainLimit.IsLedgerId(unitId))
                return false;
            if (IsCombatClass(UnitFactory.GetUnitClass(unitId)))
                cost.Veilstone = math.max(0, cost.Veilstone - AIBudget.MilitaryVeilstoneCredit(faction));
            return AIPivotalReserve.ShouldHold(em, faction, cost);
        }

        /// <summary>
        /// KING LEXOR FIRST: while he is owed (a capital can train him and
        /// none is alive or queued), every other combat unit leaves his
        /// veilstone in the bank and his population free. Null when the
        /// unit may go; otherwise the refusal.
        /// </summary>
        private static string LexorGate(EntityManager em, Faction faction, string unitId, Cost cost, ArmyPlan p)
        {
            if (!p.LexorOwed) return null;
            if (TheWaningBorder.Abilities.HeroTrainLimit.IsKingLexorId(unitId)) return null;
            if (!IsCombatClass(UnitFactory.GetUnitClass(unitId))) return null;
            if (PopulationHelper.TryGetFactionPopulation(faction, out int pop, out int popMax)
                && pop + UnitFactory.GetPopulationCost(unitId) > popMax - p.LexorPop)
                return "population kept for King Lexor";
            if (cost.Veilstone > 0 && FactionEconomy.TryGetResources(em, faction, out var res)
                && res.Veilstone - cost.Veilstone < p.LexorCost.Veilstone)
                return "veilstone saved for King Lexor";
            return null;
        }

        /// <summary>
        /// The composition's say in TryTrainUnitWithReason — every training
        /// path funnels through it, so the build order, the floor, the
        /// growth burst, the goal list and the siege program all obey the
        /// same cap and the same King Lexor priority. Null = allowed.
        /// </summary>
        private static string CompositionGate(EntityManager em, Faction faction, string unitId, Cost cost)
        {
            if (FactionCultureOf(em, faction) != Cultures.Alanthor) return null;
            var p = GetArmyPlan(em, faction);
            if (!p.Active) return null;
            if (IsBasicRow(p, RowIndex(p, unitId)) && !BasicAllowed(p))
                return $"basics cap {p.Basics}/{BasicsAllowed(p)} (saving for {p.SavingFor ?? "a role unit"})";
            return LexorGate(em, faction, unitId, cost, p);
        }

        // ── The army's veilstone claim and the log ──────────────────────

        /// <summary>
        /// Every think, after the pick: is the army's veilstone claim on
        /// (AIBudget.SetMilitaryVeilstoneClaim)? On while a veilstone role is
        /// below its share of the army or King Lexor is owed, and the army
        /// has population to spawn into. Also writes the once-a-minute
        /// composition log.
        /// </summary>
        private static void UpdateArmyVeilstoneClaim(EntityManager em, Faction faction, float now)
        {
            var p = GetArmyPlan(em, faction);
            if (!p.Active)
            {
                AIBudget.SetMilitaryVeilstoneClaim(faction, false);
                return;
            }

            float armyBasis = math.max(p.Army, Cfg.basicsFloor);
            bool behind = p.LexorOwed;
            for (int r = 0; r < p.N && !behind; r++)
                if (!p.Rows[r].basic && p.Trainable[r] && p.Share[r] > 0f
                    && p.Count[r] < p.Share[r] * armyBasis)
                    behind = true;
            bool room = !PopulationHelper.TryGetFactionPopulation(faction, out int pop, out int popMax)
                        || pop < popMax;
            AIBudget.SetMilitaryVeilstoneClaim(faction, behind && (room || p.LexorOwed));

            // SAVING ON VEILSTONE IS A SHORTAGE (2026-10-04, Game_AI.md 5e):
            // the army waiting for a role unit the bank cannot cover in
            // veilstone records it here every think, not only when a train
            // attempt happened to reach the wallet — the surplus spenders
            // (Outposts, trade research, outcrop claims) key off it.
            if (p.SavingFor != null
                && TechCatalog.TryGetUnit(p.SavingFor, out var saving) && saving != null)
            {
                var cost = AICommon.ToCost(saving.cost);
                if (cost.Veilstone > 0 && FactionEconomy.TryGetResources(em, faction, out var bank)
                    && bank.Veilstone < cost.Veilstone)
                    AIBudget.NoteMilitaryShort(em, faction, cost);
            }

            LogComposition(em, faction, p, now);
        }

        private static readonly Dictionary<Faction, float> _nextCompositionLog = new Dictionary<Faction, float>();

        /// <summary>
        /// Once per compositionLogInterval:
        /// <c>composition: army N, basics b/cap (share 22%: difficulty x1.00,
        /// personality x1.15, economy x0.78 at 200 veilstone/min) | enemy
        /// (14 sighted): cav 40%, ... | Spearman 12/20 30% (enemy cav 40%) |
        /// Sentinel 0/3 9% (enemy armoured 25%, covering Ballista) | ... |
        /// veilstone v, army earmark e | King Lexor owed | saving for X</c>
        /// </summary>
        private static void LogComposition(EntityManager em, Faction faction, ArmyPlan p, float now)
        {
            if (!AILogger.Enabled) return;
            if (_nextCompositionLog.TryGetValue(faction, out float next) && now < next) return;
            _nextCompositionLog[faction] = now + Cfg.compositionLogInterval;

            var sb = new System.Text.StringBuilder(512);
            sb.Append("composition: army ").Append(p.Army)
              .Append(", basics ").Append(p.Basics).Append('/').Append(BasicsAllowed(p))
              .Append(" (share ").Append((int)math.round(p.BasicsShare * 100f)).Append("%: difficulty x")
              .Append(p.DiffBasics.ToString("0.00")).Append(", personality x")
              .Append(p.PersBasics.ToString("0.00")).Append(", economy x")
              .Append(p.EconScale.ToString("0.00")).Append(" at ")
              .Append((int)math.round(p.VeilstoneIncome)).Append(" veilstone/min)");

            sb.Append(" | enemy");
            if (!p.EnemyRead) sb.Append(" unread (").Append(p.EnemySightings).Append(" sighted) — baseline mix");
            else
            {
                sb.Append(" (").Append(p.EnemySightings).Append(" sighted, response x")
                  .Append(p.CounterResponse.ToString("0.00")).Append("):");
                for (int k = 0; k < ReadCount; k++)
                    if (p.Enemy[k] >= 0.005f)
                        sb.Append(' ').Append(ReadNames[k]).Append(' ')
                          .Append((int)math.round(p.Enemy[k] * 100f)).Append('%');
            }

            for (int r = 0; r < p.N; r++)
            {
                if (p.Share[r] <= 0f && p.Count[r] == 0) continue;
                int target = (int)math.round(p.Share[r] * p.Army);
                sb.Append(" | ").Append(ShortName(p.Rows[r].unitId)).Append(' ')
                  .Append(p.Count[r]).Append('/').Append(target).Append(' ')
                  .Append((int)math.round(p.Share[r] * 100f)).Append('%');
                if (!p.Trainable[r]) { sb.Append(" (locked)"); continue; }
                int src = p.Via[r];
                int why = src >= 0 ? p.Reason[src] : -1;
                sb.Append(" (");
                if (why >= 0)
                    sb.Append("enemy ").Append(ReadNames[why]).Append(' ')
                      .Append((int)math.round(p.ReasonFrac[src] * 100f)).Append('%');
                else sb.Append("base");
                if (src >= 0 && src != r) sb.Append(", covering ").Append(ShortName(p.Rows[src].unitId));
                sb.Append(')');
            }

            FactionEconomy.TryGetResources(em, faction, out var res);
            sb.Append(" | veilstone ").Append(res.Veilstone)
              .Append(", army earmark ").Append(AIBudget.MilitaryVeilstoneCredit(faction));
            if (p.LexorOwed) sb.Append(" | King Lexor owed (first in line)");
            sb.Append(p.SavingFor != null ? $" | saving for {p.SavingFor}" : " | not saving");
            if (p.GlutTilt) sb.Append(" | glut tilt (supplies short, iron/veilstone piling up)");
            if (AIBudget.VeilstoneHeldSurplus(em, faction))
                sb.Append(" | veilstone-held surplus (spending on veilstone and upgrades)");
            AILogger.Log(faction, "MILITARY", sb.ToString());
        }

        // ── Capital uniques ──────────────────────────────────────────────

        /// <summary>
        /// The capital that trains a capital unique (King Lexor, the Ledger).
        /// The HOME capital when it can — but a faction with several
        /// Fortresses must not be refused "needs Lv3" by its home seat while
        /// another of its Fortresses stands at L3 (or after the home seat
        /// fell): any completed capital whose level gate is open and whose
        /// queue has room serves, highest level first. Falls back to the
        /// home capital so a refusal still names the real gate.
        /// </summary>
        private static Entity FindCapitalTrainer(EntityManager em, Faction faction, string unitId)
        {
            Entity home = FindFactionBuilding<HallTag>(em, faction);
            if (CapitalCanTake(em, home, unitId)) return home;

            Entity best = Entity.Null;
            int bestLevel = -1;
            var q = AIQueryCache.TagFaction<HallTag>(em);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction || !CapitalCanTake(em, ents[i], unitId)) continue;
                int lv = em.HasComponent<BuildingUpgradeState>(ents[i])
                    ? em.GetComponentData<BuildingUpgradeState>(ents[i]).Level : 0;
                if (lv > bestLevel) { bestLevel = lv; best = ents[i]; }
            }
            return best != Entity.Null ? best : home;
        }

        private static bool CapitalCanTake(EntityManager em, Entity hall, string unitId)
            => hall != Entity.Null
               && em.Exists(hall)
               && !em.HasComponent<UnderConstruction>(hall)
               && em.HasBuffer<ProductionQueueItem>(hall)
               && !CommandRouter.IsProductionQueueFull(em, hall)
               && CommandRouter.CanTrainAtBuilding(em, hall, unitId, out _, out _);
    }
}
