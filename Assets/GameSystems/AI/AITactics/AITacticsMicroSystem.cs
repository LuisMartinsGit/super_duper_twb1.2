// AITacticsMicroSystem.cs
// Per-UNIT micro for AI factions (docs/Design/Game_AI.md § 6e): kiting and
// ability value. The army-level half (counter targeting, flanking, fall-back,
// ranged behind melee) is SimpleAISystem.Tactics.cs; both read the tier's
// AITacticsSkill through AITactics.
//
// Why a system of its own: kiting is a sub-second decision ("melee is on me
// and I am reloading"), and the brain thinks on the tier's thinkInterval —
// 5 s on Easy. Folding micro into the think would have made difficulty a
// decision-rate knob again, which is exactly what the 2026-10-04 diagnosis
// said it is not. Instead every tier runs micro on the same cadence
// (microInterval / abilityInterval) and the SKILL decides whether it kites at
// all and how patient its abilities are.
//
// Budget: one pass per faction per interval, phase-staggered by faction, over
// two cached queries (shooters, casters). Hostile/ally reads come from the
// AIStrengthMap spatial hash and are re-read live; nothing here scans the
// world per unit. Host-only (GameSettings.ShouldRunAIBrains), every action a
// CommandRouter order with CommandSource.AI, so lockstep replicates it.
//
// Players are never touched: a faction without an AI brain has no skill and
// is skipped before anything is read.

using System.Collections.Generic;
using TheWaningBorder.Abilities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Entities;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.AI
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class AITacticsMicroSystem : SystemBase
    {
        #region Cached queries

        static readonly ComponentType[] QT_Shooters =
        {
            ComponentType.ReadOnly<ArcherTag>(),
            ComponentType.ReadOnly<ArcherState>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Shooters;

        static readonly ComponentType[] QT_Casters =
        {
            ComponentType.ReadOnly<UnitAbilities>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_Casters;

        static readonly ComponentType[] QT_SectCasters =
        {
            ComponentType.ReadOnly<UnitAbility>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_SectCasters;

        #endregion

        static AITacticsConfig Cfg => AITacticsConfig.I;

        private const int MaxF = AITactics.MaxFactions;

        private readonly double[] _nextKite = new double[MaxF];
        private readonly double[] _nextAbility = new double[MaxF];
        private readonly bool[] _kiteDue = new bool[MaxF];
        private readonly bool[] _abilityDue = new bool[MaxF];
        private readonly AITacticsSkill[] _skills = new AITacticsSkill[MaxF];
        private readonly int[] _issued = new int[MaxF];
        private int _epoch = -1;

        // Host scratch (main thread only).
        private readonly List<Entity> _cand = new List<Entity>(64);
        private readonly List<Entity> _allies = new List<Entity>(64);

        // A cast the router QUEUED (lockstep) has not landed yet: the unit
        // still reads "ready" for a tick or two. Lookup-only.
        private readonly Dictionary<Entity, double> _castPending = new Dictionary<Entity, double>();
        // The last area cast of each card per faction (xyz = where, w = until
        // when it covers that ground), so ten cavalry with War Horn ready do
        // not all blow it on the same charge. Lookup-only.
        private readonly Dictionary<(int, string), float4> _recentArea = new Dictionary<(int, string), float4>();
        private double _now;

        protected override void OnUpdate()
        {
            if (!GameSettings.ShouldRunAIBrains()) return;
            var em = EntityManager;
            double now = AITactics.Now(em);
            _now = now;

            // Match scope + PHASE stagger: faction k starts k/8 of the way
            // into each window, so eight AI factions never all pay on one
            // frame.
            if (_epoch != SimCadence.Epoch)
            {
                _epoch = SimCadence.Epoch;
                _castPending.Clear();
                _recentArea.Clear();
                for (int f = 0; f < MaxF; f++)
                {
                    _nextKite[f] = now + Cfg.microInterval * f / MaxF;
                    _nextAbility[f] = now + Cfg.abilityInterval * f / MaxF;
                }
            }

            bool anyKite = false, anyAbility = false;
            for (int f = 0; f < MaxF; f++)
            {
                _kiteDue[f] = false; _abilityDue[f] = false;
                bool kiteTime = now >= _nextKite[f];
                bool abilityTime = now >= _nextAbility[f];
                if (!kiteTime && !abilityTime) continue;
                if (kiteTime) _nextKite[f] = now + Cfg.microInterval;
                if (abilityTime) _nextAbility[f] = now + Cfg.abilityInterval;
                if (!AITactics.TryGetSkill(em, (Faction)f, out var skill)) continue;
                _skills[f] = skill;
                if (kiteTime && skill.kiting) { _kiteDue[f] = true; anyKite = true; }
                if (abilityTime && skill.castUnitAbilities) { _abilityDue[f] = true; anyAbility = true; }
            }

            if (anyKite) TickKiting(em, now);
            if (anyAbility)
            {
                TickUnitAbilities(em);
                TickSectUnitAbilities(em);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // KITING
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// A ranged unit that has just loosed (reloading) with hostile melee
        /// inside kiteTriggerDistance, and the legs to open the gap, takes ONE
        /// step straight away from it; once the step is walked it re-acquires
        /// (or the army tactics re-issue its focus) and fires again. Bounded
        /// by kiteMaxDrift from where it started kiting in this fight, so the
        /// stance leash and the army's cohesion still own the big picture.
        /// </summary>
        private void TickKiting(EntityManager em, double now)
        {
            var q = QC_Shooters.Get(em, QT_Shooters);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            System.Array.Clear(_issued, 0, MaxF);

            for (int i = 0; i < ents.Length; i++)
            {
                int fi = (int)facs[i].Value;
                if (fi < 0 || fi >= MaxF || !_kiteDue[fi]) continue;
                if (_issued[fi] >= Cfg.kiteMaxPerTick) continue;
                var e = ents[i];

                // Siege and emplaced engines do not dance; a channelling
                // caster would lose its cast; an airborne unit cannot step.
                if (em.HasComponent<SiegeTag>(e) || em.HasComponent<EmplacedEngineTag>(e)
                    || em.HasComponent<StationaryAutoFire>(e)) continue;
                if (em.HasComponent<AbilityCastState>(e) || em.HasComponent<Launched>(e)) continue;
                if (em.GetComponentData<Health>(e).Value <= 0) continue;
                var archer = em.GetComponentData<ArcherState>(e);
                if (archer.CooldownTimer < Cfg.kiteMinReloadSeconds) continue;
                if (AITactics.IsKiting(em, e)) continue;

                float3 myPos = em.GetComponentData<LocalTransform>(e).Position;
                float myRadius = AITactics.RadiusOf(em, e);
                var faction = facs[i].Value;

                // Nearest hostile MELEE body inside the trigger band.
                AIStrengthMap.HostileUnitCandidates(em, faction, myPos, Cfg.kiteTriggerDistance, _cand);
                Entity threat = Entity.Null;
                float best = float.MaxValue;
                float3 threatPos = default;
                for (int c = 0; c < _cand.Count; c++)
                {
                    var h = _cand[c];
                    if (!AITactics.IsLiveHostileUnit(em, faction, h) || !AITactics.IsMelee(em, h)) continue;
                    if (!em.HasComponent<Damage>(h) || em.GetComponentData<Damage>(h).Value <= 0) continue;
                    float3 hp = em.GetComponentData<LocalTransform>(h).Position;
                    float edge = math.distance(hp.xz, myPos.xz) - myRadius - AITactics.RadiusOf(em, h);
                    if (edge > Cfg.kiteTriggerDistance) continue;
                    if (edge < best || (edge == best && h.Index < threat.Index))
                    { best = edge; threat = h; threatPos = hp; }
                }
                if (threat == Entity.Null) continue;

                // Legs: a shooter slower than what is chasing it only turns
                // its back to it.
                float mySpeed = AITactics.SpeedOf(em, e);
                if (mySpeed <= 0f || mySpeed < AITactics.SpeedOf(em, threat) * Cfg.kiteSpeedRatio) continue;

                // Leash: no further than kiteMaxDrift from where this fight's
                // kiting began.
                float3 anchor = AITactics.KiteAnchor(em, e, myPos);
                if (math.distance(anchor.xz, myPos.xz) > Cfg.kiteMaxDrift) continue;

                float2 away = math.normalizesafe(myPos.xz - threatPos.xz);
                if (math.lengthsq(away) < 1e-4f) continue;
                float3 dest = new float3(myPos.x + away.x * Cfg.kiteStepDistance, myPos.y,
                                         myPos.z + away.y * Cfg.kiteStepDistance);

                CommandRouter.IssueMove(em, e, dest, CommandSource.AI);
                AITactics.MarkKiting(em, e, faction, now + Cfg.kiteStepDistance / mySpeed);
                _issued[fi]++;
            }

            for (int f = 0; f < MaxF; f++)
            {
                if (!_kiteDue[f]) continue;
                if (!AITactics.LogDue(em, (Faction)f, "kite", Cfg.logIntervalSeconds)) continue;
                int n = AITactics.TakeKiteCount((Faction)f);
                if (n > 0)
                    AILogger.Log((Faction)f, "TACTICS",
                        $"kite {n} units (last {Cfg.logIntervalSeconds:F0}s)");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // ABILITY VALUE — the per-caster read
        // ─────────────────────────────────────────────────────────────────

        /// <summary>What one caster can see around it, read once per pass.</summary>
        private struct Ctx
        {
            public float3 Pos;
            public float HpFrac;
            public bool InCombat;
            public int EnemyPower, AllyPower;
            public int EnemiesInScan;
            public Entity NearestEnemy;
            public float NearestEnemyDist;
            public float Ratio => AllyPower > 0 ? EnemyPower / (float)AllyPower : (EnemyPower > 0 ? float.MaxValue : 0f);
        }

        private Ctx ReadContext(EntityManager em, Entity e, Faction faction)
        {
            var c = new Ctx();
            c.Pos = em.GetComponentData<LocalTransform>(e).Position;
            var hp = em.GetComponentData<Health>(e);
            c.HpFrac = hp.Max > 0 ? hp.Value / (float)hp.Max : 1f;
            float scan = Cfg.abilityScanRadius;
            float scan2 = scan * scan;

            AIStrengthMap.HostileUnitCandidates(em, faction, c.Pos, scan, _cand);
            // Keep only live, in-radius hostiles so the per-card counts below
            // are plain distance checks.
            for (int i = _cand.Count - 1; i >= 0; i--)
            {
                var h = _cand[i];
                if (!AITactics.IsLiveHostileUnit(em, faction, h)) { _cand.RemoveAt(i); continue; }
                float d2 = math.distancesq(em.GetComponentData<LocalTransform>(h).Position.xz, c.Pos.xz);
                if (d2 > scan2) { _cand.RemoveAt(i); continue; }
            }
            c.NearestEnemyDist = float.MaxValue;
            for (int i = 0; i < _cand.Count; i++)
            {
                var h = _cand[i];
                c.EnemyPower += AITactics.LiveStrength(em, h);
                float d = math.distance(em.GetComponentData<LocalTransform>(h).Position.xz, c.Pos.xz);
                if (d < c.NearestEnemyDist || (d == c.NearestEnemyDist && h.Index < c.NearestEnemy.Index))
                { c.NearestEnemyDist = d; c.NearestEnemy = h; }
            }
            c.EnemiesInScan = _cand.Count;
            c.InCombat = c.NearestEnemyDist <= Cfg.contactDistance;

            AIStrengthMap.FriendlyUnitCandidates(em, faction, c.Pos, scan, _allies);
            for (int i = _allies.Count - 1; i >= 0; i--)
            {
                var a = _allies[i];
                if (!em.Exists(a) || !em.HasComponent<Health>(a) || em.GetComponentData<Health>(a).Value <= 0)
                { _allies.RemoveAt(i); continue; }
                float d2 = math.distancesq(em.GetComponentData<LocalTransform>(a).Position.xz, c.Pos.xz);
                if (d2 > scan2) { _allies.RemoveAt(i); continue; }
            }
            if (!_allies.Contains(e)) _allies.Add(e);
            for (int i = 0; i < _allies.Count; i++) c.AllyPower += AITactics.LiveStrength(em, _allies[i]);
            return c;
        }

        /// <summary>Hostiles from the last read within <paramref name="r"/>.</summary>
        private int EnemiesWithin(EntityManager em, float3 pos, float r)
        {
            float r2 = r * r; int n = 0;
            for (int i = 0; i < _cand.Count; i++)
                if (math.distancesq(em.GetComponentData<LocalTransform>(_cand[i]).Position.xz, pos.xz) <= r2) n++;
            return n;
        }

        /// <summary>Allies from the last read within <paramref name="r"/>,
        /// optionally only those carrying <paramref name="needTags"/>; also
        /// sums their HP pool and how much of it is missing.</summary>
        private int AlliesWithin(EntityManager em, float3 pos, float r, uint needTags, bool rangedOnly,
            out int hpMissing, out int hpMax, out int wounded)
        {
            float r2 = r * r; int n = 0;
            hpMissing = 0; hpMax = 0; wounded = 0;
            for (int i = 0; i < _allies.Count; i++)
            {
                var a = _allies[i];
                if (math.distancesq(em.GetComponentData<LocalTransform>(a).Position.xz, pos.xz) > r2) continue;
                if (needTags != 0 && (AITactics.TagsOf(em, a) & needTags) == 0
                    && !(needTags == (uint)UnitTagBits.Cavalry && em.HasComponent<CavalryTag>(a))) continue;
                if (rangedOnly && !em.HasComponent<ArcherTag>(a)) continue;
                if (!em.HasComponent<UnitTag>(a)) continue;
                var hp = em.GetComponentData<Health>(a);
                hpMax += hp.Max;
                hpMissing += math.max(0, hp.Max - hp.Value);
                if (hp.Value < hp.Max) wounded++;
                n++;
            }
            return n;
        }

        // ─────────────────────────────────────────────────────────────────
        // DATA-DRIVEN ACTIVES (UnitAbilities / AbilityCatalog cards)
        // ─────────────────────────────────────────────────────────────────

        private readonly int[] _slots = new int[4];

        private void TickUnitAbilities(EntityManager em)
        {
            var q = QC_Casters.Get(em, QT_Casters);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                int fi = (int)facs[i].Value;
                if (fi < 0 || fi >= MaxF || !_abilityDue[fi]) continue;
                var e = ents[i];
                if (!em.HasComponent<UnitTag>(e)) continue;   // buildings / Ledger targets
                if (em.GetComponentData<Health>(e).Value <= 0) continue;
                if (em.HasComponent<AbilityCastState>(e) || em.HasComponent<AbilityActivated>(e)
                    || em.HasComponent<Launched>(e)) continue;

                if (CastPending(e)) continue;
                int nSlots = AbilityQuery.ActiveSlots(em, e, _slots);
                if (nSlots == 0) continue;

                var faction = facs[i].Value;
                ref readonly var skill = ref _skills[fi];
                bool haveCtx = false;
                Ctx ctx = default;
                var abil = em.GetComponentData<UnitAbilities>(e);

                for (int k = 0; k < nSlots; k++)
                {
                    int slot = _slots[k];
                    if (AbilityQuery.CooldownRemaining(em, e, slot) > 0f) continue;
                    var card = AbilityCatalog.Get(abil.Get(slot));
                    if (card == null) continue;
                    if (!haveCtx) { ctx = ReadContext(em, e, faction); haveCtx = true; }
                    if (AreaCovered(fi, card, ctx.Pos))
                    {
                        LogHeld(em, faction, card.Name, "another cast already covers this ground");
                        continue;
                    }

                    var verdict = EvaluateCard(em, e, card, in ctx, in skill, out string why, out int caught);
                    if (verdict == Verdict.Ignore) continue;
                    if (verdict == Verdict.Hold)
                    {
                        LogHeld(em, faction, card.Name, why);
                        continue;
                    }
                    if (CommandRouter.IssueUnitAbilitySlot(em, e, slot, Entity.Null, null, CommandSource.AI))
                    {
                        MarkCast(e);
                        if (card.Targeting == AbilityTargeting.Area && card.Radius > 0f)
                            _recentArea[(fi, card.Name)] = new float4(ctx.Pos,
                                (float)(_now + math.max(card.Duration, Cfg.abilityInterval)));
                        AILogger.Log(faction, "ABILITY",
                            $"{card.Name} cast ({caught} enemies, {AITactics.IdOf(em, e)} at {ctx.HpFrac * 100f:F0}% hp)");
                        break;   // one cast per caster per pass
                    }
                }
            }
        }

        private enum Verdict : byte { Ignore, Hold, Cast }

        private bool CastPending(Entity e)
            => _castPending.TryGetValue(e, out var t) && t > _now;

        private void MarkCast(Entity e)
        {
            _castPending[e] = _now + Cfg.abilityInterval * 2f;
            if (_castPending.Count > 256)
            {
                var drop = new List<Entity>();
                foreach (var kv in _castPending) if (kv.Value <= _now) drop.Add(kv.Key);
                for (int i = 0; i < drop.Count; i++) _castPending.Remove(drop[i]);
            }
        }

        /// <summary>Another caster of this faction already put this card's
        /// area over this spot, and it is still running.</summary>
        private bool AreaCovered(int faction, AbilityCard card, float3 pos)
        {
            if (card.Targeting != AbilityTargeting.Area || card.Radius <= 0f) return false;
            if (!_recentArea.TryGetValue((faction, card.Name), out var r)) return false;
            return r.w > _now && math.distance(r.xz, pos.xz) <= card.Radius;
        }

        /// <summary>
        /// Is THIS the moment for this card? By effect kind, never by
        /// "it is off cooldown":
        ///   * area damage / control  — at least aoeMinEnemies inside its radius;
        ///   * ultimates (Honour thy Pledge) — only when the fight is pivotal;
        ///   * heals (Field Hospital) — enough of the allied pool is missing;
        ///   * ally buffs (War Horn, Volleys, attack / armour) — enough allies
        ///     affected AND an enemy inside the radius or about to be;
        ///   * self-defence (Liquid Courage) — in combat and hurt or outnumbered;
        ///   * escapes (Full Gallop) — in contact, hurt or out-powered.
        /// Cards the AI should not drive (reveals, economy, passives) are
        /// ignored without a log line.
        /// </summary>
        private Verdict EvaluateCard(EntityManager em, Entity e, AbilityCard card, in Ctx ctx,
            in AITacticsSkill skill, out string why, out int caught)
        {
            why = null;
            caught = ctx.EnemiesInScan;
            int aoeMin = math.max(1, skill.aoeMinEnemies);
            float radius = card.Radius > 0f ? card.Radius : Cfg.abilityScanRadius;

            if (card.IsAimed
                || card.HasEffect(AbilityEffectKind.RevealFog)
                || card.HasEffect(AbilityEffectKind.ResourceYieldPct)
                || card.HasEffect(AbilityEffectKind.NoAutomation)
                || card.HasEffect(AbilityEffectKind.LosRampWhileStill))
                return Verdict.Ignore;

            if (card.HasEffect(AbilityEffectKind.ShardboundFury) || card.Affects == AbilityAffects.Enemies)
            {
                caught = EnemiesWithin(em, ctx.Pos, radius);
                if (caught >= aoeMin) return Verdict.Cast;
                why = $"only {caught}/{aoeMin} enemies in {radius:F0} m";
                return Verdict.Hold;
            }

            if (card.HasEffect(AbilityEffectKind.SummonPledgeArmy))
            {
                if (ctx.InCombat && (ctx.Ratio >= skill.pivotalRatio || ctx.EnemiesInScan >= aoeMin * 2))
                    return Verdict.Cast;
                why = ctx.InCombat
                    ? $"fight not pivotal (power {ctx.EnemyPower} vs {ctx.AllyPower})"
                    : "not in combat";
                return Verdict.Hold;
            }

            if (card.HasEffect(AbilityEffectKind.DeployFieldHospital))
            {
                int n = AlliesWithin(em, ctx.Pos, radius, 0, false, out int miss, out int max, out int wounded);
                float frac = max > 0 ? miss / (float)max : 0f;
                if (frac >= skill.healMinMissingFraction && wounded >= Cfg.buffMinAllies) return Verdict.Cast;
                why = $"{frac * 100f:F0}% of {n} allies' hp missing ({wounded} wounded)";
                return Verdict.Hold;
            }

            bool escape = card.HasEffect(AbilityEffectKind.MoveSpeedPct)
                          && card.EffectValue(AbilityEffectKind.MoveSpeedPct) > 0f
                          && card.HasEffect(AbilityEffectKind.DisarmWhileBuffed);
            if (escape)
            {
                if (ctx.InCombat && (ctx.HpFrac <= Cfg.defensiveHpFraction || ctx.Ratio >= skill.pivotalRatio))
                    return Verdict.Cast;
                why = ctx.InCombat ? "holding ground (not hurt, not out-powered)" : "no one to escape from";
                return Verdict.Hold;
            }

            bool selfDefence = card.Targeting == AbilityTargeting.SelfCast
                && (card.HasEffect(AbilityEffectKind.DamageTakenPct) || card.HasEffect(AbilityEffectKind.HpFloor)
                    || card.HasEffect(AbilityEffectKind.ArmorPct) || card.HasEffect(AbilityEffectKind.ArmorFlat));
            if (selfDefence)
            {
                if (ctx.InCombat && (ctx.HpFrac <= Cfg.defensiveHpFraction
                                     || ctx.Ratio >= skill.pivotalRatio
                                     || ctx.EnemiesInScan >= aoeMin))
                    return Verdict.Cast;
                why = ctx.InCombat
                    ? $"not pressed ({ctx.HpFrac * 100f:F0}% hp, {ctx.EnemiesInScan} enemies)"
                    : "not in combat";
                return Verdict.Hold;
            }

            // Ally buffs: who is affected, and is a fight here or arriving?
            bool cavalryBuff = card.Affects == AbilityAffects.AlliedCavalry;
            bool rangedBuff = card.Affects == AbilityAffects.AlliedRanged
                              || card.HasEffect(AbilityEffectKind.FireRatePct);
            bool allyBuff = cavalryBuff || rangedBuff
                || card.Affects == AbilityAffects.AlliedAll || card.Affects == AbilityAffects.AlliedCulture
                || card.HasEffect(AbilityEffectKind.ChargeDamagePct)
                || card.HasEffect(AbilityEffectKind.AttackPct);
            if (allyBuff)
            {
                int affected = card.Targeting == AbilityTargeting.SelfCast ? 1
                    : AlliesWithin(em, ctx.Pos, radius, cavalryBuff ? (uint)UnitTagBits.Cavalry : 0u,
                                   rangedBuff, out _, out _, out _);
                int need = card.Targeting == AbilityTargeting.SelfCast ? 1 : Cfg.buffMinAllies;
                caught = EnemiesWithin(em, ctx.Pos, radius + Cfg.buffEnemyMargin);
                if (affected >= need && caught > 0) return Verdict.Cast;
                why = caught == 0 ? "no enemy in reach" : $"only {affected}/{need} allies affected";
                return Verdict.Hold;
            }

            return Verdict.Ignore;
        }

        private void LogHeld(EntityManager em, Faction faction, string ability, string why)
        {
            if (why == null) return;
            if (!AITactics.LogDue(em, faction, "held:" + ability, Cfg.abilityHeldLogSeconds)) return;
            AILogger.Log(faction, "ABILITY", $"{ability} held ({why})");
        }

        // ─────────────────────────────────────────────────────────────────
        // SECT-UNIT ACTIVES (legacy UnitAbility, UnitAbilitySystem)
        // ─────────────────────────────────────────────────────────────────

        private void TickSectUnitAbilities(EntityManager em)
        {
            var q = QC_SectCasters.Get(em, QT_SectCasters);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                int fi = (int)facs[i].Value;
                if (fi < 0 || fi >= MaxF || !_abilityDue[fi]) continue;
                var e = ents[i];
                if (em.GetComponentData<Health>(e).Value <= 0) continue;
                if (em.HasComponent<AbilityActivated>(e) || em.HasComponent<Launched>(e)) continue;
                var ab = em.GetComponentData<UnitAbility>(e);
                if (ab.Id == AbilityId.None || ab.CooldownRemaining > 0f) continue;
                if (CastPending(e)) continue;

                var faction = facs[i].Value;
                ref readonly var skill = ref _skills[fi];
                var ctx = ReadContext(em, e, faction);
                int aoeMin = math.max(1, skill.aoeMinEnemies);
                float aoe = Cfg.legacyAbilityRadius;

                Entity target = Entity.Null;
                string why = null;
                int caught = ctx.EnemiesInScan;
                bool cast = false;
                Entity current = em.HasComponent<Target>(e) ? em.GetComponentData<Target>(e).Value : Entity.Null;
                bool currentHostile = current != Entity.Null && AITactics.IsLiveHostileUnit(em, faction, current);

                switch (ab.Id)
                {
                    case AbilityId.ArcanePulse:
                    case AbilityId.WarCry:
                        caught = EnemiesWithin(em, ctx.Pos, aoe);
                        cast = caught >= aoeMin;
                        if (!cast) why = $"only {caught}/{aoeMin} enemies in {aoe:F0} m";
                        break;

                    case AbilityId.RapidMend:
                        cast = ctx.InCombat && ctx.HpFrac <= Cfg.defensiveHpFraction;
                        if (!cast) why = $"{ctx.HpFrac * 100f:F0}% hp";
                        break;

                    case AbilityId.Fortify:
                    case AbilityId.MirrorShield:
                        caught = EnemiesWithin(em, ctx.Pos, Cfg.contactDistance);
                        cast = ctx.InCombat && (ctx.HpFrac <= Cfg.defensiveHpFraction || caught >= 2);
                        if (!cast) why = ctx.InCombat ? "not pressed" : "not in combat";
                        break;

                    case AbilityId.Safeguard:
                    {
                        int allies = AlliesWithin(em, ctx.Pos, aoe, 0, false, out _, out _, out _);
                        cast = ctx.InCombat && allies >= Cfg.buffMinAllies;
                        if (!cast) why = ctx.InCombat ? $"only {allies} allies in {aoe:F0} m" : "not in combat";
                        break;
                    }

                    case AbilityId.Ignite:
                    case AbilityId.VoidStrike:
                        cast = currentHostile && ctx.InCombat;
                        if (!cast) why = "no target in reach";
                        break;

                    case AbilityId.Condemn:
                        // Mark what is worth the bonus: a high-value body, or
                        // one with most of its HP still to take.
                        if (currentHostile && InRange(em, e, current, ab.Range)
                            && (AITactics.IsHighValue(em, current) || HpFrac(em, current) > Cfg.condemnMinHpFraction))
                        { target = current; cast = true; }
                        else why = "no worthy target in range";
                        break;

                    case AbilityId.Sanction:
                    case AbilityId.ChainBind:
                        // Root what would otherwise reach us or get away:
                        // cavalry and heroes first.
                        target = PickRootTarget(em, e, ab.Range);
                        cast = target != Entity.Null;
                        if (!cast) why = "no cavalry or hero in range";
                        break;

                    case AbilityId.Dispel:
                        target = PickBuffedEnemy(em, e, ab.Range);
                        cast = target != Entity.Null;
                        if (!cast) why = "no buffed enemy in range";
                        break;
                }

                if (!cast) { LogHeld(em, faction, ab.Id.ToString(), why); continue; }
                CommandRouter.IssueAbility(em, e, target, CommandSource.AI);
                MarkCast(e);
                AILogger.Log(faction, "ABILITY",
                    $"{ab.Id} cast ({caught} enemies, {AITactics.IdOf(em, e)} at {ctx.HpFrac * 100f:F0}% hp)");
            }
        }

        private static float HpFrac(EntityManager em, Entity e)
        {
            var hp = em.GetComponentData<Health>(e);
            return hp.Max > 0 ? hp.Value / (float)hp.Max : 1f;
        }

        private static bool InRange(EntityManager em, Entity a, Entity b, float range)
        {
            if (range <= 0f) return true;
            float3 pa = em.GetComponentData<LocalTransform>(a).Position;
            float3 pb = em.GetComponentData<LocalTransform>(b).Position;
            return math.distance(pa.xz, pb.xz) <= range;
        }

        /// <summary>Nearest cavalry / hero hostile within range, from the last
        /// context read.</summary>
        private Entity PickRootTarget(EntityManager em, Entity caster, float range)
        {
            Entity best = Entity.Null; float bestD = float.MaxValue;
            float3 me = em.GetComponentData<LocalTransform>(caster).Position;
            for (int i = 0; i < _cand.Count; i++)
            {
                var h = _cand[i];
                bool fast = em.HasComponent<CavalryTag>(h) || AITactics.IsHero(em, h)
                            || (AITactics.TagsOf(em, h) & (uint)UnitTagBits.Cavalry) != 0;
                if (!fast) continue;
                float d = math.distance(em.GetComponentData<LocalTransform>(h).Position.xz, me.xz);
                if (range > 0f && d > range) continue;
                if (d < bestD || (d == bestD && h.Index < best.Index)) { bestD = d; best = h; }
            }
            return best;
        }

        /// <summary>Nearest hostile carrying a strippable buff, in range.</summary>
        private Entity PickBuffedEnemy(EntityManager em, Entity caster, float range)
        {
            Entity best = Entity.Null; float bestD = float.MaxValue;
            float3 me = em.GetComponentData<LocalTransform>(caster).Position;
            for (int i = 0; i < _cand.Count; i++)
            {
                var h = _cand[i];
                bool buffed = TransientState.Active<SpellBuff>(em, h) || em.HasComponent<IgniteBuff>(h)
                              || em.HasComponent<VoidStrikeBuff>(h) || em.HasComponent<Fortified>(h);
                if (!buffed) continue;
                float d = math.distance(em.GetComponentData<LocalTransform>(h).Position.xz, me.xz);
                if (range > 0f && d > range) continue;
                if (d < bestD || (d == bestD && h.Index < best.Index)) { bestD = d; best = h; }
            }
            return best;
        }
    }
}
