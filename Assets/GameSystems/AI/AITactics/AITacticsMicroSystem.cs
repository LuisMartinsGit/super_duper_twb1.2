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

        // ── Time-sliced passes (2026-10-16 perf) ─────────────────────────
        // The micro measured ~5 ms per frame late game: with eight factions
        // phase-staggered over a 0.3 s window SOME faction was due on every
        // sim tick (three a frame), and each pass walked every shooter and
        // read a full context (two spatial scans plus a live read per
        // candidate) for every caster with a card off cooldown — every Runai
        // Worker carries The Call. Now: at most one slice per rendered frame,
        // bounded by microFrameBudgetMs; a pass that runs out keeps its
        // cursor and its due factions and resumes next frame; and a caster
        // with nobody hostile in reach skips the context read entirely.
        private int _lastFrame = -1;
        private bool _kitePass, _abilityPass;
        private int _kiteCursor, _unitCursor, _sectCursor;
        private long _sliceStart;
        private int _sliceWork;

        protected override void OnUpdate()
        {
            if (!GameSettings.ShouldRunAIBrains()) return;
            // One slice per RENDERED frame: lockstep catch-up ticks inside a
            // frame would only repeat the same reads (host-only, so wall
            // clock and frame count are safe here).
            int frame = UnityEngine.Time.frameCount;
            if (frame == _lastFrame) return;
            _lastFrame = frame;
            AIFrameBudget.Enter();
            try { Tick(); }
            finally { AIFrameBudget.Exit(); }
        }

        private void Tick()
        {
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
                _heldNext.Clear();
                _sectHeldNext.Clear();
                _kitePass = _abilityPass = false;
                for (int f = 0; f < MaxF; f++)
                {
                    _nextKite[f] = now + Cfg.microInterval * f / MaxF;
                    _nextAbility[f] = now + Cfg.abilityInterval * f / MaxF;
                }
            }

            // A pass in progress keeps its due factions; timers are read
            // only between passes (a faction due meanwhile joins the next).
            if (!_kitePass)
            {
                bool anyKite = false;
                for (int f = 0; f < MaxF; f++)
                {
                    _kiteDue[f] = false;
                    if (now < _nextKite[f]) continue;
                    _nextKite[f] = now + Cfg.microInterval;
                    if (!AITactics.TryGetSkill(em, (Faction)f, out var skill)) continue;
                    _skills[f] = skill;
                    if (skill.kiting) { _kiteDue[f] = true; anyKite = true; }
                }
                if (anyKite)
                {
                    _kitePass = true;
                    _kiteCursor = 0;
                    System.Array.Clear(_issued, 0, MaxF);
                }
            }
            if (!_abilityPass)
            {
                bool anyAbility = false;
                for (int f = 0; f < MaxF; f++)
                {
                    _abilityDue[f] = false;
                    if (now < _nextAbility[f]) continue;
                    _nextAbility[f] = now + Cfg.abilityInterval;
                    if (!AITactics.TryGetSkill(em, (Faction)f, out var skill)) continue;
                    _skills[f] = skill;
                    if (skill.castUnitAbilities) { _abilityDue[f] = true; anyAbility = true; }
                }
                if (anyAbility)
                {
                    _abilityPass = true;
                    _unitCursor = 0;
                    _sectCursor = 0;
                    for (int f = 0; f < MaxF; f++) _callable[f] = -1;
                }
            }

            _sliceStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _sliceWork = 0;
            if (_kitePass && TickKiting(em, now)) _kitePass = false;
            if (_abilityPass && TickUnitAbilities(em) && TickSectUnitAbilities(em)) _abilityPass = false;
        }

        /// <summary>Count one unit's full evaluation; true once this frame's
        /// slice is spent (never before microMinUnitsPerFrame units).</summary>
        private bool SliceSpent()
        {
            _sliceWork++;
            if (_sliceWork <= Cfg.microMinUnitsPerFrame || (_sliceWork & 7) != 0) return false;
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - _sliceStart) * 1000.0
                        / System.Diagnostics.Stopwatch.Frequency;
            return ms >= Cfg.microFrameBudgetMs;
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
        /// <summary>One slice of the kiting pass; true when the pass is done
        /// (false = out of time, resumes at <see cref="_kiteCursor"/>).</summary>
        private bool TickKiting(EntityManager em, double now)
        {
            var q = QC_Shooters.Get(em, QT_Shooters);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            // Bulk columns (2026-10-15 perf): the two tests that reject almost
            // every shooter — dead, or not reloading — read from arrays BEFORE
            // the EntityManager tag tests. All of them are side-effect-free
            // filters joined by AND, so the order changes no answer.
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            using var archers = q.ToComponentDataArray<ArcherState>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Resumed by index: the query order can shift between frames, so
            // a resumed pass may skip or revisit a few shooters — harmless
            // for a host-side heuristic that runs again in microInterval.
            for (int i = _kiteCursor; i < ents.Length; i++)
            {
                int fi = (int)facs[i].Value;
                if (fi < 0 || fi >= MaxF || !_kiteDue[fi]) continue;
                if (_issued[fi] >= Cfg.kiteMaxPerTick) continue;
                if (hps[i].Value <= 0) continue;
                if (archers[i].CooldownTimer < Cfg.kiteMinReloadSeconds) continue;
                var e = ents[i];

                // Siege and emplaced engines do not dance; a channelling
                // caster would lose its cast; an airborne unit cannot step.
                if (em.HasComponent<SiegeTag>(e) || em.HasComponent<EmplacedEngineTag>(e)
                    || em.HasComponent<StationaryAutoFire>(e)) continue;
                if (TransientState.Active<AbilityCastState>(em, e) || em.HasComponent<Launched>(e)) continue;
                if (AITactics.IsKiting(em, e)) continue;

                if (SliceSpent()) { _kiteCursor = i; return false; }

                float3 myPos = xfs[i].Position;
                var faction0 = facs[i].Value;
                // Nobody hostile in the trigger band at all (snapshot): no
                // per-candidate live reads.
                if (!AIStrengthMap.AnyHostileUnitCandidate(em, faction0, myPos, Cfg.kiteTriggerDistance)) continue;
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
            return true;
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
            // are plain distance checks. Compacted in place, order kept (a
            // RemoveAt per reject shifted the whole tail each time).
            int kept = 0;
            for (int i = 0; i < _cand.Count; i++)
            {
                var h = _cand[i];
                if (!AITactics.IsLiveHostileUnit(em, faction, h)) continue;
                float d2 = math.distancesq(em.GetComponentData<LocalTransform>(h).Position.xz, c.Pos.xz);
                if (d2 > scan2) continue;
                _cand[kept++] = h;
            }
            _cand.RemoveRange(kept, _cand.Count - kept);
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
            kept = 0;
            bool self = false;   // the caster seen among the kept allies (was a Contains scan)
            for (int i = 0; i < _allies.Count; i++)
            {
                var a = _allies[i];
                if (!em.Exists(a) || !em.HasComponent<Health>(a) || em.GetComponentData<Health>(a).Value <= 0) continue;
                float d2 = math.distancesq(em.GetComponentData<LocalTransform>(a).Position.xz, c.Pos.xz);
                if (d2 > scan2) continue;
                _allies[kept++] = a;
                self |= a == e;
            }
            _allies.RemoveRange(kept, _allies.Count - kept);
            if (!self) _allies.Add(e);
            for (int i = 0; i < _allies.Count; i++) c.AllyPower += AITactics.LiveStrength(em, _allies[i]);
            return c;
        }

        /// <summary>Hostiles from the last read within <paramref name="r"/>.</summary>
        /// <summary>
        /// The enemy unit a single-target card should be cast on: from the
        /// last context read (live hostiles in the scan), legal for the card
        /// (AbilityTargetRules — kind, side, range), nearest first; a card that
        /// silences abilities prefers a unit that has some.
        /// </summary>
        private Entity PickEnemyTarget(EntityManager em, AbilityCard card, Entity caster, float3 pos)
        {
            bool wantsCaster = card.HasEffect(AbilityEffectKind.SectStampOfRecord);
            Entity best = Entity.Null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < _cand.Count; i++)
            {
                var h = _cand[i];
                if (!AbilityTargetRules.Check(em, card, caster, h, out _)) continue;
                float d = math.distancesq(em.GetComponentData<LocalTransform>(h).Position.xz, pos.xz);
                if (wantsCaster && !em.HasComponent<UnitAbilities>(h)) d += 1e6f;
                if (d < bestScore || (d == bestScore && h.Index < best.Index)) { bestScore = d; best = h; }
            }
            return best;
        }

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
        /// <summary>The Call's callable escorts per faction, counted at most
        /// once per pass (-1 = not counted yet): the count walks every escort
        /// of the map, and every Worker of a Runai AI carries the card.</summary>
        private readonly int[] _callable = new int[MaxF];

        private int CallableEscorts(EntityManager em, int fi, Faction faction)
        {
            if (_callable[fi] < 0) _callable[fi] = TheWaningBorder.Runai.RunaiTradeSystem.CallableEscorts(em, faction);
            return _callable[fi];
        }

        /// <summary>One slice of the ability pass; true when done (false =
        /// out of time, resumes at <see cref="_unitCursor"/>).</summary>
        private bool TickUnitAbilities(EntityManager em)
        {
            var q = QC_Casters.Get(em, QT_Casters);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            // Bulk Health, and the cheap host-side tests first (2026-10-15
            // perf) — AND-joined, side-effect-free filters: same answers.
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            // (_callable is reset when the pass starts, not per slice.)

            for (int i = _unitCursor; i < ents.Length; i++)
            {
                int fi = (int)facs[i].Value;
                if (fi < 0 || fi >= MaxF || !_abilityDue[fi]) continue;
                if (hps[i].Value <= 0) continue;
                var e = ents[i];
                if (CastPending(e)) continue;
                if (!em.HasComponent<UnitTag>(e)) continue;   // buildings / Ledger targets
                if (TransientState.Active<AbilityCastState>(em, e) || TransientState.Active<AbilityActivated>(em, e)
                    || em.HasComponent<Launched>(e)) continue;

                int nSlots = AbilityQuery.ActiveSlots(em, e, _slots);
                if (nSlots == 0) continue;
                if (SliceSpent()) { _unitCursor = i; return false; }

                var faction = facs[i].Value;
                ref readonly var skill = ref _skills[fi];
                bool haveCtx = false, havePos = false;
                int hostileNear = -1;   // -1 unknown, 0 none in reach, 1 some
                Ctx ctx = default;
                float3 pos = default;
                var abil = em.GetComponentData<UnitAbilities>(e);

                for (int k = 0; k < nSlots; k++)
                {
                    int slot = _slots[k];
                    if (AbilityQuery.CooldownRemaining(em, e, slot) > 0f) continue;
                    var card = AbilityCatalog.Get(abil.Get(slot));
                    if (card == null) continue;
                    // A card the AI never drives costs no context read (a
                    // Worker's The Return, a Scout's reveal...).
                    var kind = KindOf(card);
                    if (kind == CardKind.Ignore) continue;
                    if (!havePos) { pos = em.GetComponentData<LocalTransform>(e).Position; havePos = true; }
                    if (AreaCovered(fi, card, pos))
                    {
                        LogHeld(fi, faction, card.Name, "another cast already covers this ground");
                        continue;
                    }
                    // Every rule but the heal needs an enemy in the scan
                    // (in combat, or caught inside the card's reach), and the
                    // scan's candidates come from the same snapshot this
                    // asks: none there = Hold, without the context read.
                    if (kind != CardKind.Hospital)
                    {
                        if (hostileNear < 0)
                            hostileNear = AIStrengthMap.AnyHostileUnitCandidate(em, faction, pos,
                                Cfg.abilityScanRadius) ? 1 : 0;
                        if (hostileNear == 0)
                        {
                            if (HeldLogDue(fi, card.Name)) LogHeld(fi, faction, card.Name, "no enemy in reach");
                            continue;
                        }
                    }
                    if (!haveCtx) { ctx = ReadContext(em, e, faction); haveCtx = true; }

                    // The reason is only spelled out when its log line is due.
                    bool wantWhy = HeldLogDue(fi, card.Name);
                    // A single-target card is cast ON an enemy, never at a
                    // spot (docs/Design/Spells.md §6): the AI names the entity
                    // and the engine re-checks it before the cooldown.
                    Entity pick = Entity.Null;
                    Verdict verdict;
                    string why = null;
                    int caught;
                    if (kind == CardKind.TargetEnemy)
                    {
                        pick = PickEnemyTarget(em, card, e, ctx.Pos);
                        verdict = pick != Entity.Null ? Verdict.Cast : Verdict.Hold;
                        caught = pick != Entity.Null ? 1 : 0;
                        if (pick == Entity.Null && wantWhy) why = "no legal target in reach";
                    }
                    else
                        verdict = EvaluateCard(em, fi, faction, card, kind, in ctx, in skill, wantWhy,
                                               out why, out caught);
                    if (verdict == Verdict.Ignore) continue;
                    if (verdict == Verdict.Hold)
                    {
                        LogHeld(fi, faction, card.Name, why);
                        continue;
                    }
                    if (CommandRouter.IssueUnitAbilitySlot(em, e, slot, pick, null, CommandSource.AI))
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
            return true;
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
        /// <summary>Which rule a card falls under — a pure function of the
        /// card, so it is worked out once per card, not per caster per pass.</summary>
        private enum CardKind : byte { Ignore, Enemies, Pledge, Call, Hospital, Escape, SelfDefence, AllyBuff, TargetEnemy }

        private readonly Dictionary<AbilityCard, CardKind> _cardKind = new Dictionary<AbilityCard, CardKind>();

        private CardKind KindOf(AbilityCard card)
        {
            if (_cardKind.TryGetValue(card, out var k)) return k;
            k = Classify(card);
            _cardKind[card] = k;
            return k;
        }

        /// <summary>The order of the tests is the precedence (the effect-kind
        /// list in <see cref="EvaluateCard"/>'s summary).</summary>
        private static CardKind Classify(AbilityCard card)
        {
            // Cast ON an entity: an enemy UNIT card (Stamp of Record, Lancet)
            // is driven here; own-building and enemy-building cards (Liquidate,
            // Raze) are economy / siege calls this system does not make.
            if (card.IsEntityTargeted)
                return card.TargetKind == CastTargetKind.EnemyUnit
                       || card.TargetKind == CastTargetKind.EnemyUnitOrBuilding
                    ? CardKind.TargetEnemy : CardKind.Ignore;
            if (card.IsAimed
                || card.HasEffect(AbilityEffectKind.RevealFog)
                || card.HasEffect(AbilityEffectKind.ResourceYieldPct)
                || card.HasEffect(AbilityEffectKind.NoAutomation)
                || card.HasEffect(AbilityEffectKind.LosRampWhileStill))
                return CardKind.Ignore;
            if (card.HasEffect(AbilityEffectKind.ShardboundFury) || card.Affects == AbilityAffects.Enemies)
                return CardKind.Enemies;
            if (card.HasEffect(AbilityEffectKind.SummonPledgeArmy)) return CardKind.Pledge;
            // Returning the escorts is left to the Call running out.
            if (card.HasEffect(AbilityEffectKind.RunaiReturnToWagons)) return CardKind.Ignore;
            if (card.HasEffect(AbilityEffectKind.RunaiTheCall)) return CardKind.Call;
            if (card.HasEffect(AbilityEffectKind.DeployFieldHospital)) return CardKind.Hospital;
            if (card.HasEffect(AbilityEffectKind.MoveSpeedPct)
                && card.EffectValue(AbilityEffectKind.MoveSpeedPct) > 0f
                && card.HasEffect(AbilityEffectKind.DisarmWhileBuffed))
                return CardKind.Escape;
            if (card.Targeting == AbilityTargeting.SelfCast
                && (card.HasEffect(AbilityEffectKind.DamageTakenPct) || card.HasEffect(AbilityEffectKind.HpFloor)
                    || card.HasEffect(AbilityEffectKind.ArmorPct) || card.HasEffect(AbilityEffectKind.ArmorFlat)))
                return CardKind.SelfDefence;
            if (card.Affects == AbilityAffects.AlliedCavalry || IsRangedBuff(card)
                || card.Affects == AbilityAffects.AlliedAll || card.Affects == AbilityAffects.AlliedCulture
                || card.HasEffect(AbilityEffectKind.ChargeDamagePct)
                || card.HasEffect(AbilityEffectKind.AttackPct))
                return CardKind.AllyBuff;
            return CardKind.Ignore;
        }

        private static bool IsRangedBuff(AbilityCard card)
            => card.Affects == AbilityAffects.AlliedRanged || card.HasEffect(AbilityEffectKind.FireRatePct);

        /// <summary>
        /// Is THIS the moment for this card? By effect kind, never by
        /// "it is off cooldown" (<paramref name="kind"/> from <see cref="KindOf"/>):
        ///   * area damage / control  — at least aoeMinEnemies inside its radius;
        ///   * ultimates (Honour thy Pledge) — only when the fight is pivotal;
        ///   * The Call — a pivotal fight AND enough escorts to call;
        ///   * heals (Field Hospital) — enough of the allied pool is missing;
        ///   * ally buffs (War Horn, Volleys, attack / armour) — enough allies
        ///     affected AND an enemy inside the radius or about to be;
        ///   * self-defence (Liquid Courage) — in combat and hurt or outnumbered;
        ///   * escapes (Full Gallop) — in contact, hurt or out-powered.
        /// Cards the AI should not drive (reveals, economy, passives) are
        /// ignored without a log line. <paramref name="why"/> is only built
        /// when <paramref name="wantWhy"/> (its log line is due).
        /// </summary>
        private Verdict EvaluateCard(EntityManager em, int fi, Faction faction, AbilityCard card, CardKind kind,
            in Ctx ctx, in AITacticsSkill skill, bool wantWhy, out string why, out int caught)
        {
            why = null;
            caught = ctx.EnemiesInScan;
            int aoeMin = math.max(1, skill.aoeMinEnemies);
            float radius = card.Radius > 0f ? card.Radius : Cfg.abilityScanRadius;

            switch (kind)
            {
                case CardKind.Enemies:
                    caught = EnemiesWithin(em, ctx.Pos, radius);
                    if (caught >= aoeMin) return Verdict.Cast;
                    if (wantWhy) why = $"only {caught}/{aoeMin} enemies in {radius:F0} m";
                    return Verdict.Hold;

                case CardKind.Pledge:
                    if (ctx.InCombat && (ctx.Ratio >= skill.pivotalRatio || ctx.EnemiesInScan >= aoeMin * 2))
                        return Verdict.Cast;
                    if (wantWhy)
                        why = ctx.InCombat
                            ? $"fight not pivotal (power {ctx.EnemyPower} vs {ctx.AllyPower})"
                            : "not in combat";
                    return Verdict.Hold;

                // THE CALL (docs/Design/Age_1_Runai.md §8.1, §9): pull the
                // escorts in when a fight here is pivotal — never a convenience
                // cast, the caravans ride unescorted meanwhile. The fight is
                // judged first: it is free, the escort count walks the map.
                case CardKind.Call:
                {
                    if (!ctx.InCombat) { why = "not in combat"; return Verdict.Hold; }
                    if (!(ctx.Ratio >= skill.pivotalRatio || ctx.EnemiesInScan >= aoeMin * 2))
                    {
                        if (wantWhy) why = $"fight not pivotal (power {ctx.EnemyPower} vs {ctx.AllyPower})";
                        return Verdict.Hold;
                    }
                    int callable = CallableEscorts(em, fi, faction);
                    if (callable < 3) { if (wantWhy) why = $"only {callable} escorts to call"; return Verdict.Hold; }
                    return Verdict.Cast;
                }

                case CardKind.Hospital:
                {
                    int n = AlliesWithin(em, ctx.Pos, radius, 0, false, out int miss, out int max, out int wounded);
                    float frac = max > 0 ? miss / (float)max : 0f;
                    if (frac >= skill.healMinMissingFraction && wounded >= Cfg.buffMinAllies) return Verdict.Cast;
                    if (wantWhy) why = $"{frac * 100f:F0}% of {n} allies' hp missing ({wounded} wounded)";
                    return Verdict.Hold;
                }

                case CardKind.Escape:
                    if (ctx.InCombat && (ctx.HpFrac <= Cfg.defensiveHpFraction || ctx.Ratio >= skill.pivotalRatio))
                        return Verdict.Cast;
                    why = ctx.InCombat ? "holding ground (not hurt, not out-powered)" : "no one to escape from";
                    return Verdict.Hold;

                case CardKind.SelfDefence:
                    if (ctx.InCombat && (ctx.HpFrac <= Cfg.defensiveHpFraction
                                         || ctx.Ratio >= skill.pivotalRatio
                                         || ctx.EnemiesInScan >= aoeMin))
                        return Verdict.Cast;
                    if (wantWhy)
                        why = ctx.InCombat
                            ? $"not pressed ({ctx.HpFrac * 100f:F0}% hp, {ctx.EnemiesInScan} enemies)"
                            : "not in combat";
                    return Verdict.Hold;

                case CardKind.AllyBuff:
                {
                    // Who is affected, and is a fight here or arriving?
                    bool cavalryBuff = card.Affects == AbilityAffects.AlliedCavalry;
                    bool rangedBuff = IsRangedBuff(card);
                    int affected = card.Targeting == AbilityTargeting.SelfCast ? 1
                        : AlliesWithin(em, ctx.Pos, radius, cavalryBuff ? (uint)UnitTagBits.Cavalry : 0u,
                                       rangedBuff, out _, out _, out _);
                    int need = card.Targeting == AbilityTargeting.SelfCast ? 1 : Cfg.buffMinAllies;
                    caught = EnemiesWithin(em, ctx.Pos, radius + Cfg.buffEnemyMargin);
                    if (affected >= need && caught > 0) return Verdict.Cast;
                    if (wantWhy) why = caught == 0 ? "no enemy in reach" : $"only {affected}/{need} allies affected";
                    return Verdict.Hold;
                }
            }
            return Verdict.Ignore;
        }

        // Held-cast log throttle, kept here so the check allocates nothing:
        // the shared AITactics.LogDue keys on a string, and "held:" + name
        // was concatenated per card per caster per pass to ask it.
        private readonly Dictionary<(int, string), double> _heldNext = new Dictionary<(int, string), double>();
        private readonly Dictionary<(int, AbilityId), double> _sectHeldNext = new Dictionary<(int, AbilityId), double>();

        private bool HeldLogDue(int fi, string ability)
            => !_heldNext.TryGetValue((fi, ability), out var next) || _now >= next;

        private void LogHeld(int fi, Faction faction, string ability, string why)
        {
            if (why == null || !HeldLogDue(fi, ability)) return;
            _heldNext[(fi, ability)] = _now + Cfg.abilityHeldLogSeconds;
            AILogger.Log(faction, "ABILITY", $"{ability} held ({why})");
        }

        private bool SectHeldLogDue(int fi, AbilityId id)
            => !_sectHeldNext.TryGetValue((fi, id), out var next) || _now >= next;

        private void LogSectHeld(int fi, Faction faction, AbilityId id, string why)
        {
            if (why == null || !SectHeldLogDue(fi, id)) return;
            _sectHeldNext[(fi, id)] = _now + Cfg.abilityHeldLogSeconds;
            AILogger.Log(faction, "ABILITY", $"{id} held ({why})");
        }

        // ─────────────────────────────────────────────────────────────────
        // SECT-UNIT ACTIVES (legacy UnitAbility, UnitAbilitySystem)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>One slice of the sect-unit pass; true when done (false =
        /// out of time, resumes at <see cref="_sectCursor"/>).</summary>
        private bool TickSectUnitAbilities(EntityManager em)
        {
            var q = QC_SectCasters.Get(em, QT_SectCasters);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = _sectCursor; i < ents.Length; i++)
            {
                int fi = (int)facs[i].Value;
                if (fi < 0 || fi >= MaxF || !_abilityDue[fi]) continue;
                var e = ents[i];
                if (em.GetComponentData<Health>(e).Value <= 0) continue;
                if (TransientState.Active<AbilityActivated>(em, e) || em.HasComponent<Launched>(e)) continue;
                var ab = em.GetComponentData<UnitAbility>(e);
                if (ab.Id == AbilityId.None || ab.CooldownRemaining > 0f) continue;
                if (CastPending(e)) continue;
                if (SliceSpent()) { _sectCursor = i; return false; }

                var faction = facs[i].Value;
                // Every rule but Condemn (which reads only the unit's own
                // target) needs an enemy among the scan's candidates — none
                // in the snapshot = hold, without the context read.
                if (ab.Id != AbilityId.Condemn
                    && !AIStrengthMap.AnyHostileUnitCandidate(em, faction,
                        em.GetComponentData<LocalTransform>(e).Position, Cfg.abilityScanRadius))
                {
                    if (SectHeldLogDue(fi, ab.Id)) LogSectHeld(fi, faction, ab.Id, "not in combat");
                    continue;
                }
                ref readonly var skill = ref _skills[fi];
                var ctx = ReadContext(em, e, faction);
                int aoeMin = math.max(1, skill.aoeMinEnemies);
                float aoe = Cfg.legacyAbilityRadius;
                bool wantWhy = SectHeldLogDue(fi, ab.Id);

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
                        if (!cast && wantWhy) why = $"only {caught}/{aoeMin} enemies in {aoe:F0} m";
                        break;

                    case AbilityId.RapidMend:
                        cast = ctx.InCombat && ctx.HpFrac <= Cfg.defensiveHpFraction;
                        if (!cast && wantWhy) why = $"{ctx.HpFrac * 100f:F0}% hp";
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
                        if (!cast && wantWhy) why = ctx.InCombat ? $"only {allies} allies in {aoe:F0} m" : "not in combat";
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

                if (!cast) { LogSectHeld(fi, faction, ab.Id, why); continue; }
                CommandRouter.IssueAbility(em, e, target, CommandSource.AI);
                MarkCast(e);
                AILogger.Log(faction, "ABILITY",
                    $"{ab.Id} cast ({caught} enemies, {AITactics.IdOf(em, e)} at {ctx.HpFrac * 100f:F0}% hp)");
            }
            return true;
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
                              || em.HasComponent<VoidStrikeBuff>(h) || TransientState.Active<Fortified>(em, h);
                if (!buffed) continue;
                float d = math.distance(em.GetComponentData<LocalTransform>(h).Position.xz, me.xz);
                if (range > 0f && d > range) continue;
                if (d < bestD || (d == bestD && h.Index < best.Index)) { bestD = d; best = h; }
            }
            return best;
        }
    }
}
