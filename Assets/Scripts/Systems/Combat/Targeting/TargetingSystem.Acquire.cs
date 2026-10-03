// TargetingSystem.Acquire.cs
// Auto target acquisition by STANCE (spatial-hash enemy scan), the
// spread/nearest pick, and the chase leash on auto-acquired engagements.
// docs/Design/Stances.md is canonical for the rules.
// Partial of TargetingSystem.cs -- split 2026-08-12 for readability.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Combat
{
    public partial struct TargetingSystem : ISystem
    {
        /// <summary>What a unit may auto-acquire right now (stance + order).</summary>
        private struct AcquireRules
        {
            /// <summary>0 = this unit acquires nothing on its own (Hold).</summary>
            public byte Acquire;
            /// <summary>Cell sweep radius and the hard distance cap.</summary>
            public float ScanRadius;
            /// <summary>1 = a candidate is accepted only if it is attacking
            /// THIS unit (it hit us within the retaliation window, or its
            /// Target is us) -- the Defensive "return fire" rule.</summary>
            public byte ThreatsOnly;
            /// <summary>1 = this unit was hit within the retaliation window,
            /// so its LastAttackerEntity counts as a threat.</summary>
            public byte HitRecently;
            /// <summary>Chase leash for an engagement acquired under these
            /// rules; 0 = the unit never moves for it (the combat systems
            /// drop the target the moment it leaves reach).</summary>
            public float Leash;
            public byte IsMelee;
            public byte BuildingsOnly;
            public byte NonSiege;
        }

        /// <summary>A unit's own attack reach, to the target's surface:
        /// melee MeleeRange, ranged its ArcherState.MaxRange.</summary>
        private static float AttackReach(EntityManager em, Entity e)
        {
            if (em.HasComponent<ArcherState>(e))
            {
                var a = em.GetComponentData<ArcherState>(e);
                return a.MaxRange > 0f ? a.MaxRange : RangedFallbackReach;
            }
            return MeleeCombatSystem.MeleeRange;
        }

        /// <summary>RangedCombatSystem.DefaultMaxRange, for an archer whose SO
        /// left MaxRange at 0 -- the two must agree or a unit acquires what it
        /// then refuses to shoot.</summary>
        private const float RangedFallbackReach = 25f;

        /// <summary>
        /// A fixed mount (Stances.md §1a): an emplaced engine, or anything a
        /// scenario planted with <see cref="StationaryAutoFire"/>. It fires at
        /// whatever is inside its reach whatever its stance, and never moves
        /// for a target -- ordered or not. Public: the melee and ranged
        /// systems ask the same question.
        /// </summary>
        public static bool IsFixedMount(EntityManager em, Entity e)
            => em.HasComponent<EmplacedEngineTag>(e) || em.HasComponent<StationaryAutoFire>(e);

        /// <summary>
        /// May this unit MOVE to engage <paramref name="target"/>? The one
        /// question the melee and ranged chase branches ask (Stances.md §1, §5).
        ///
        /// - A fixed mount never moves, and a channelling ritualist holds its
        ///   ground.
        /// - A target TargetingSystem picked on its own (UnitEngagement.AutoTarget)
        ///   may be chased only when its engagement carries a leash -- the
        ///   Aggressive stance, attack-move and patrol. Defensive, Hold and
        ///   support units get a 0 leash: they fight what is in reach and let
        ///   the rest go.
        /// - Anything else is an ORDER (player or AI) and is always chased, on
        ///   every stance, Hold included. Player orders win.
        /// </summary>
        public static bool MayChase(EntityManager em, Entity e, Entity target)
        {
            if (IsFixedMount(em, e)) return false;
            if (em.HasComponent<RitualState>(e)) return false;
            if (em.HasComponent<UnitEngagement>(e))
            {
                var eng = em.GetComponentData<UnitEngagement>(e);
                if (eng.AutoTarget != Entity.Null && eng.AutoTarget == target)
                    return eng.Leash > 0f;
            }
            return true;
        }

        /// <summary>
        /// The stance table (docs/Design/Stances.md §1), in precedence order:
        ///
        /// 1. Fixed mount -- anything in reach, never moves.
        /// 2. Hold (and not attack-moving / patrolling) -- nothing at all.
        /// 3. Support class -- only what is attacking it and inside its reach,
        ///    never moves (even on attack-move: a healer never runs into melee).
        /// 4. Aggressive, attack-move or patrol -- anything in line of sight,
        ///    chased up to the Aggressive leash.
        /// 5. Defensive -- only what is attacking it and inside its reach,
        ///    never moves.
        /// </summary>
        private static AcquireRules RulesFor(EntityManager em, Entity e, UnitClass cls,
            UnitStanceMode stance, bool scanner, float los, bool hitRecently, in StanceSettings s)
        {
            var r = new AcquireRules
            {
                Acquire = 1,
                IsMelee = (byte)(cls == UnitClass.Melee ? 1 : 0),
                // Buildings-only siege (Battering Ram): never auto-acquire a
                // non-building target -- it exists to crack walls, not to
                // swing at soldiers (the melee fire path refuses those anyway).
                BuildingsOnly = (byte)(em.HasComponent<BuildingsOnlyAttacker>(e) ? 1 : 0),
                // The Wall Rule (docs/Design/Combat_Pacing.md): only siege
                // damages wall pieces, so non-siege never auto-acquires one.
                NonSiege = (byte)((!em.HasComponent<DamageTypeData>(e)
                    || em.GetComponentData<DamageTypeData>(e).Value != DamageType.Siege) ? 1 : 0),
                HitRecently = (byte)(hitRecently ? 1 : 0),
            };

            float reach = math.min(los, AttackReach(em, e));

            if (IsFixedMount(em, e))
            {
                r.ScanRadius = reach;
                return r;
            }
            if (stance == UnitStanceMode.Hold && !scanner)
            {
                // Hold is passive: it neither returns fire nor pursues.
                r.Acquire = 0;
                return r;
            }
            if (cls == UnitClass.Support)
            {
                r.ScanRadius = reach;
                r.ThreatsOnly = 1;
                return r;
            }
            // The 1.5x line-of-sight acquire bonus is GONE: a unit no longer
            // notices enemies it cannot see.
            if (scanner || stance == UnitStanceMode.Aggressive)
            {
                r.ScanRadius = los;
                r.Leash = s.AggressiveLeash;
                return r;
            }
            // Defensive: return fire, do not pursue.
            r.ScanRadius = reach;
            r.ThreatsOnly = 1;
            return r;
        }

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private void AutoAcquireTargets(ref SystemState state, ref EntityCommandBuffer ecb,
            in EnemyScan scan, in StanceSettings settings,
            ref NativeHashMap<Entity, int> attackerCount)
        {
            var em = state.EntityManager;
            uint tick = _tick;
            float clock = _clock;

            // Single unified loop for all target-seeking units:
            // idle units, attack-move units, and patrol units.
            // Workers are excluded.
            foreach (var (transform, faction, lineOfSight, target, unitTag, damage, eng, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>, RefRO<LineOfSight>, RefRO<Target>,
                       RefRO<UnitTag>, RefRO<Damage>, RefRW<UnitEngagement>>()
                .WithNone<DeathAnimationState>()  // a corpse acquires nothing
                .WithNone<TheWaningBorder.Entities.Launched>()  // neither does a unit in the air
                .WithNone<AttackCommand>()
                .WithNone<PassiveWorkerTag>()   // Workers are passive...
                                                //   ...except Feraldis Workers, which are
                                                //   light infantry that also build. The tag
                                                //   (not CanBuild) is what marks a worker as
                                                //   non-combatant; FeraldisCultureRetrofitSystem
                                                //   strips it.
                .WithNone<BuildCommand>()       // A COMMITTED WORKER IS BUSY. Feraldis
                                                //   Workers fight, so dropping PassiveWorkerTag
                                                //   let this pass (and return-to-guard below)
                                                //   grab them mid-build and fight the build
                                                //   order for their DesiredDestination every
                                                //   frame — the visible worker "glitching"
                                                //   reported 2026-08-06. Command follow-through:
                                                //   a worker with a build order finishes it.
                .WithNone<SectVeiled>()         // Stoneveil (Fortitude): a veiled unit may
                                                //   move, and nothing else. It cannot attack,
                                                //   gather, build or capture while veiled.
                .WithNone<WorkerTag>()           // Workers are handled by MiningSystem
                .WithNone<RitualState>()        // A CHANNELLING RITUALIST IS BUSY — the same
                                                //   rule as the committed worker above, added
                                                //   for the same failure.
                                                //
                                                //   The killer is the return-to-guard branch
                                                //   below, not target acquisition. A ritualist
                                                //   clears DesiredDestination to channel, which
                                                //   makes it IDLE; its GuardPoint is still where
                                                //   it spawned, tens of metres away; so the very
                                                //   next tick walks it home at full speed. It
                                                //   `continue`s before the damage check, so even
                                                //   a 0-damage caster like the Iconoclast is
                                                //   dragged off.
                                                //
                                                //   Measured 2026-08-07: 6 m -> 14 m in ~3 s
                                                //   (2.7 m/s against a 3.2 move speed), breaking
                                                //   the 40 s channel every single time. With
                                                //   MaxGuardDistance at 10 m this hit EVERY
                                                //   ritual on every map — no well is within
                                                //   10 m of a spawn.
                .WithEntityAccess())
            {
                // Skip units that already have an active target
                if (target.ValueRO.Value != Entity.Null) continue;
                // An acquisition queued earlier this tick has not played back
                // yet (the ECB lands at end of frame).
                if (eng.ValueRO.AutoTarget != Entity.Null) continue;

                // Scouts are vision-only by design. Even if a future tech /
                // TechTreeDB entry ever bumps their Damage above 0, they
                // must NEVER auto-engage — they are the AI's and player's
                // sole map-vision tool and the AI patrol loop relies on
                // them staying alive. Explicit class check guarantees this
                // regardless of the damage value below.
                //
                // Economy units (workers) NEVER auto-engage either. A
                // worker standing on a deposit is mining, so its
                // DesiredDestination.Has is 0 — which means the "skip units
                // with a destination" gate below does NOT protect it, and it
                // would auto-acquire a nearby enemy and wander off to chase it
                // while still assigned to the deposit.
                var cls = unitTag.ValueRO.Class;
                if (cls == UnitClass.Scout || cls == UnitClass.Economy || cls == UnitClass.Worker)
                    continue;

                // Damage gate — only units that actually deal damage
                // engage enemies. Litharchs (and any future zero-damage
                // support tier) sit idle in their formation slot until a
                // tech upgrades their damage above 0.
                if (damage.ValueRO.Value <= 0) continue;

                // STAGGER: one tick in AcquireStagger per unit, phase from its
                // network id (UnitStanceSystem stamps it), so every peer scans
                // the same units on the same tick.
                if (((tick + eng.ValueRO.ScanPhase) & (AcquireStagger - 1)) != 0) continue;

                // Just broke a leash: do not let the same fleeing target tow
                // the unit straight back out.
                if (clock < eng.ValueRO.NextAcquireAt) continue;

                // Cache the lookups; enableable transients read as active only
                // while their enabled bit is set (TransientState).
                bool hasAttackMove = TransientState.Active<AttackMoveTag>(em, entity);
                bool hasPatrol = em.HasComponent<PatrolTag>(entity);
                bool hasUserMoveOrder = TransientState.Active<UserMoveOrder>(em, entity);
                bool isActiveScanner = hasAttackMove || hasPatrol;

                // ── MARCH IN FORMATION, FIGHT AT THE GATES (2026-09-03). ──
                // A formation attack-move used to auto-acquire the whole way,
                // so the army peeled off unit by unit at every farm and stray
                // scout it marched past and arrived as a strung-out queue —
                // the "armies attack as a trickle" report. While a formation
                // member is still farther than FormationHoldRadius from its
                // attack-move destination (the guard point IS that
                // destination), it holds rank and keeps walking. Retaliation
                // is exempt: a unit HIT within the under-fire window fights
                // back — marching silently through an ambush would be worse.
                // (It used to key on "my last attacker is still alive", which
                // only cleared when that attacker died: one graze from an
                // archer that lived on released the unit for good.)
                if (hasAttackMove
                    && em.HasComponent<FormationMemberState>(entity)
                    && em.HasComponent<GuardPoint>(entity))
                {
                    var fgp = em.GetComponentData<GuardPoint>(entity);
                    if (fgp.Has != 0
                        && DistXZ(transform.ValueRO.Position, fgp.Position) > FormationHoldRadius)
                    {
                        bool underFire = clock - eng.ValueRO.HitAt <= settings.UnderFireWindow;
                        if (!underFire) continue;
                    }
                }

                // Idle units (no AttackMove/Patrol) with UserMoveOrder skip targeting
                if (!isActiveScanner && hasUserMoveOrder) continue;

                // Walking a QUEUED plain-move route: waypoints outweigh the
                // stance until the queue is done (2026-09-29) — a route that
                // fights at every stop is an attack-move, queued as such.
                if (!isActiveScanner && em.HasComponent<QueuedMoveStep>(entity)) continue;

                // Idle units skip while moving to a destination — including the
                // walk home from a broken leash. Return-to-guard owns
                // re-engagement en route (ProcessReturnToGuard).
                if (!isActiveScanner && em.HasComponent<DesiredDestination>(entity)
                    && em.GetComponentData<DesiredDestination>(entity).Has != 0)
                    continue;

                var myPos = transform.ValueRO.Position;
                var myFaction = faction.ValueRO.Value;
                var stance = EffectiveStance(em, entity);

                // ── Idle-only guard distance constraint ──
                // An idle unit that has wandered beyond MaxGuardDistance from
                // its guard point is sent back instead of acquiring a target —
                // and an idle unit only auto-acquires INSIDE its own chase
                // leash, so a unit left past it walks home rather than
                // re-engaging on the way (Stances.md §3).
                // Attack-move / patrol scanners are exempt (they advance), and
                // so is Hold (it never moves on its own) and an emplaced
                // engine (it is on its post).
                bool hitRecently = clock - eng.ValueRO.HitAt <= settings.RetaliationWindow;
                var rules = RulesFor(em, entity, cls, stance, isActiveScanner,
                    lineOfSight.ValueRO.Radius, hitRecently, in settings);
                if (rules.Acquire == 0) continue;   // Hold: passive unless ordered

                float3 anchor = myPos;
                if (!isActiveScanner && stance != UnitStanceMode.Hold
                    && !IsFixedMount(em, entity)
                    && em.HasComponent<GuardPoint>(entity))
                {
                    var guardPoint = em.GetComponentData<GuardPoint>(entity);
                    if (guardPoint.Has != 0)
                    {
                        anchor = guardPoint.Position;
                        var distFromGuard = DistXZ(myPos, guardPoint.Position);
                        if (distFromGuard > MaxGuardDistance)
                        {
                            SetDestination(em, ref ecb, entity, guardPoint.Position);
                            continue;
                        }
                        // A unit that may chase only acquires inside its own
                        // leash; one that never moves (leash 0) fights where
                        // it stands.
                        if (rules.Leash > 0f && distFromGuard > rules.Leash) continue;
                    }
                }

                Entity bestTarget = FindAutoTarget(em, entity, myPos, myFaction, in rules,
                    in scan, ref attackerCount);

                if (bestTarget != Entity.Null && em.Exists(bestTarget))
                {
                    ecb.SetComponent(entity, new Target { Value = bestTarget });
                    ref var e = ref eng.ValueRW;
                    e.AutoTarget = bestTarget;
                    e.Anchor = isActiveScanner ? myPos : anchor;
                    e.Leash = rules.Leash;

                    // Attack-move and patrol units also issue an AttackCommand so combat systems chase
                    // Do NOT clear DesiredDestination - unit resumes movement after combat.
                    // Not for a unit that may not chase (support on attack-move):
                    // it returns fire where it stands and keeps marching.
                    if (isActiveScanner && rules.Leash > 0f)
                        TransientState.Set(ecb, entity, new AttackCommand { Target = bestTarget });
                }
            }
        }

        /// <summary>
        /// The candidate scan shared by acquisition and return-to-guard:
        /// hostile factions' buckets only, stance rules applied, then the
        /// value / spread / nearest pick. Records the pick in attackerCount.
        /// </summary>
        private static Entity FindAutoTarget(EntityManager em, Entity self, float3 myPos,
            Faction myFaction, in AcquireRules rules, in EnemyScan scan,
            ref NativeHashMap<Entity, int> attackerCount)
        {
            int myF = (int)myFaction;
            if (myF < 0 || myF >= FactionSlots) return Entity.Null;

            // Two-pass scan throughout: track both absolute-nearest enemy
            // ("anyBest") and nearest under-cap enemy ("underBest"). Pick
            // under-cap only when (a) the attacker is melee (ranged/siege
            // don't physically clump, so they always pick nearest), AND
            // (b) the under-cap candidate is within SpreadDistRatio of
            // anyBest (so we don't march 50m to attack a far-away target
            // when a saturated one is right in front of us). Otherwise
            // fall back to anyBest — overflow attackers will dogpile.
            bool isMelee = rules.IsMelee != 0;
            bool selfOnRampart = isMelee && IsOnRampart(em, self);

            Entity underBest = Entity.Null;
            float underBestDist = float.MaxValue;
            Entity anyBest = Entity.Null;
            float anyBestDist = float.MaxValue;
            byte anyBestPrio = 0;
            Entity prioBest = Entity.Null;
            float prioBestDist = float.MaxValue;
            byte prioBestPrio = 0;

            Entity lastAttacker = TransientState.Active<LastAttackerEntity>(em, self)
                ? em.GetComponentData<LastAttackerEntity>(self).Value : Entity.Null;

            int radius = (int)math.ceil(rules.ScanRadius / TargetingCellSize);
            var myCell = new int2(
                (int)math.floor(myPos.x / TargetingCellSize),
                (int)math.floor(myPos.z / TargetingCellSize));

            // Hostile factions only: allies' buckets are never visited
            // (docs/Design/Teams.md — allies are never auto-acquired).
            for (int f = 0; f < FactionSlots; f++)
            {
                if (scan.FactionPresent[f] == 0) continue;
                if (scan.Hostile[myF * FactionSlots + f] == 0) continue;

                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        var key = new int3(myCell.x + dx, myCell.y + dy, f);
                        if (!scan.Map.TryGetFirstValue(key, out int i, out var it)) continue;
                        do
                        {
                            if (scan.Health[i].Value <= 0) continue;
                            byte flags = scan.Flags[i];

                            // Buildings-only siege: units are invisible to
                            // the ram's target scan.
                            if (rules.BuildingsOnly != 0 && (flags & FlagBuilding) == 0) continue;

                            // The Wall Rule: wall pieces are invisible to
                            // non-siege target scans.
                            if (rules.NonSiege != 0 && (flags & FlagWall) != 0) continue;

                            var cand = scan.Entities[i];
                            var enemyPos = scan.Transforms[i].Position;
                            // SURFACE distance, so a big building is judged by
                            // where its walls are, not where its pivot is. A
                            // 7x7 temple's pivot sits 3.5 m inside itself; on
                            // centre distance it read as further away than a
                            // hut standing right beside it, and the "nearest
                            // enemy" pick skipped the thing the unit was
                            // literally touching.
                            var dist = TargetGeometry.SurfaceDistXZ(em, myPos, enemyPos, cand);
                            if (dist > rules.ScanRadius) continue;

                            // Curse & Shardroot canon §2.1: BORDER units
                            // GUARD their wells — they don't hunt
                            // harvesters. Worker-class targets (workers)
                            // are ignored unless they stray
                            // right into the horde; military targets are
                            // engaged normally. Makes sneak-mining the
                            // crystal fields survivable.
                            if (myFaction == Faction.Border && dist > 9f && (flags & FlagWorker) != 0)
                                continue;

                            // Melee can't reach a vertically separated
                            // enemy (bridge deck above / valley floor
                            // below) — don't acquire it, pick someone
                            // reachable instead. Ranged units keep the
                            // target (they shoot up/down with the
                            // high-ground rules). Buildings are measured
                            // by their body, not their pivot height — see
                            // MeleeHeightBlocks.
                            if (isMelee && MeleeHeightBlocks(myPos.y, enemyPos.y,
                                    (flags & FlagBuilding) != 0, selfOnRampart))
                                continue;

                            // Skip stealthed enemies unless within proximity
                            // reveal range (3u) or exposed by a Lorekeeper
                            // (Antiquity detection stamp). Read live, not
                            // cached: stealth comes and goes.
                            if (dist > 3f && em.HasComponent<StealthTag>(cand)
                                && !em.HasComponent<StealthRevealed>(cand))
                                continue;

                            // DEFENSIVE / SUPPORT: return fire only -- the
                            // candidate must be attacking ME (Stances.md §1).
                            if (rules.ThreatsOnly != 0
                                && !IsThreat(em, cand, self, lastAttacker, rules.HitRecently != 0))
                                continue;

                            byte prio = scan.Priority[i];
                            if (dist < anyBestDist) { anyBest = cand; anyBestDist = dist; anyBestPrio = prio; }
                            if (prio > prioBestPrio || (prio == prioBestPrio && dist < prioBestDist))
                            {
                                prioBest = cand;
                                prioBestDist = dist;
                                prioBestPrio = prio;
                            }
                            if (isMelee)
                            {
                                int curCount = attackerCount.TryGetValue(cand, out int cv) ? cv : 0;
                                if (curCount < MaxAttackersPerEnemy && dist < underBestDist)
                                {
                                    underBest = cand;
                                    underBestDist = dist;
                                }
                            }
                        } while (scan.Map.TryGetNextValue(out i, ref it));
                    }
                }
            }

            // M2 bounded value tie-break: a higher-priority candidate
            // (healer/siege/caster) replaces the nearest pick only when
            // it sits within ValuePickDistRatio of it.
            // NearDistFloor: distances are measured to the target's
            // SURFACE, so a unit pressed against a building reads 0.0 —
            // and a pure ratio against 0 is 0, which would let a wall
            // permanently out-rank the soldier standing next to it.
            // Floor the comparison basis so "within 25% of nearest" also
            // means "or within a metre or so, absolute".
            if (prioBest != Entity.Null && prioBestPrio > anyBestPrio
                && prioBestDist <= math.max(anyBestDist, NearDistFloor) * ValuePickDistRatio)
            {
                anyBest = prioBest;
                anyBestDist = prioBestDist;
            }

            Entity best = PickSpreadOrNearest(underBest, underBestDist, anyBest, anyBestDist);

            // Record the assignment so the next unit in this same pass sees
            // the updated count (prevents two simultaneously-assigned
            // attackers from both picking the same enemy because both saw
            // count=0).
            if (best != Entity.Null)
            {
                int prev = attackerCount.TryGetValue(best, out int pv) ? pv : 0;
                attackerCount[best] = prev + 1;
            }
            return best;
        }

        /// <summary>
        /// Is <paramref name="cand"/> attacking me? It hit me within the
        /// retaliation window (it is my LastAttackerEntity), or its own Target
        /// is me -- the shot may still be in the air. The Defensive stance and
        /// support units engage only these (docs/Design/Stances.md §1).
        /// </summary>
        private static bool IsThreat(EntityManager em, Entity cand, Entity self, Entity lastAttacker,
            bool hitRecently)
        {
            if (hitRecently && cand == lastAttacker) return true;
            if (!em.HasComponent<Target>(cand)) return false;
            return em.GetComponentData<Target>(cand).Value == self;
        }

        /// <summary>
        /// The chase leash (Stances.md §3). An AUTO-acquired target is dropped
        /// once the unit is farther than the engagement's leash from where the
        /// engagement started, and the unit walks back to its guard point (an
        /// attack-mover / patroller: back onto its march, whose guard point IS
        /// the destination / waypoint). Ordered targets are never touched.
        ///
        /// Writes Target DIRECTLY, not through the ECB: the melee and ranged
        /// systems run after this one in the same frame and read Target live,
        /// and an ECB clear would let them write one more chase destination
        /// over the walk home.
        /// </summary>
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private void EnforceLeash(ref SystemState state, ref EntityCommandBuffer ecb,
            in StanceSettings settings)
        {
            var em = state.EntityManager;
            float clock = _clock;

            foreach (var (transform, target, eng, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRW<Target>, RefRW<UnitEngagement>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                ref var e = ref eng.ValueRW;
                if (e.AutoTarget == Entity.Null) continue;

                var t = target.ValueRO.Value;
                // The engagement ended (target died, an order cleared it) or
                // was replaced by an order: it is no longer an auto one. (Last
                // tick's acquisition played back at the end of last frame, so
                // a mismatch here is real.)
                if (t == Entity.Null || t != e.AutoTarget)
                {
                    e.AutoTarget = Entity.Null;
                    continue;
                }

                // The curse's units are wave-driven and guard their wells on
                // their own terms (Stances.md §4).
                if (em.HasComponent<BorderUnitTag>(entity)) continue;
                if (e.Leash <= 0f) continue;   // Hold: never moves for it anyway

                if (DistXZ(transform.ValueRO.Position, e.Anchor) <= e.Leash) continue;

                // ── Break the leash. ──
                target.ValueRW = new Target { Value = Entity.Null };
                e.AutoTarget = Entity.Null;
                e.NextAcquireAt = clock + settings.LeashReacquireCooldown;
                if (TransientState.Active<AttackCommand>(em, entity))
                    TransientState.Clear<AttackCommand>(em, ecb, entity);

                // Walk home — for an attack-mover / patroller the guard point
                // is its destination / waypoint, so this resumes the march.
                if (em.HasComponent<GuardPoint>(entity))
                {
                    var gp = em.GetComponentData<GuardPoint>(entity);
                    if (gp.Has != 0) SetDestination(em, ref ecb, entity, gp.Position);
                }
            }
        }

        /// <summary>Health drop since last tick = hit (feeds the under-fire
        /// window and the retaliation window). Source-agnostic on purpose:
        /// melee, projectiles, spells and burns all count.</summary>
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private void TrackHits(ref SystemState state)
        {
            float clock = _clock;
            foreach (var (health, eng) in SystemAPI
                .Query<RefRO<Health>, RefRW<UnitEngagement>>()
                .WithAll<UnitTag>())
            {
                int hp = health.ValueRO.Value;
                ref var e = ref eng.ValueRW;
                if (hp < e.LastHealth) e.HitAt = clock;
                e.LastHealth = hp;
            }
        }

        /// <summary>The stance a unit is actually in (HoldPositionTag wins).</summary>
        private static UnitStanceMode EffectiveStance(EntityManager em, Entity e)
        {
            if (em.HasComponent<HoldPositionTag>(e)) return UnitStanceMode.Hold;
            if (em.HasComponent<UnitStance>(e)) return em.GetComponentData<UnitStance>(e).Value;
            return UnitStanceMode.Aggressive;   // the default for every unit (Stances.md §4)
        }

        private static void SetDestination(EntityManager em, ref EntityCommandBuffer ecb,
            Entity entity, float3 pos)
        {
            if (!em.HasComponent<DesiredDestination>(entity))
                ecb.AddComponent(entity, new DesiredDestination { Position = pos, Has = 1 });
            else
                ecb.SetComponent(entity, new DesiredDestination { Position = pos, Has = 1 });
        }

        /// <summary>
        /// Returns underBest if it's a valid candidate within SpreadDistRatio of
        /// anyBest's distance, otherwise falls back to anyBest. Keeps overflow
        /// attackers from trekking far across the map just to honour the cap —
        /// they'll dogpile a nearby capped enemy if no reasonable alternative
        /// is in range.
        /// </summary>
        private static Entity PickSpreadOrNearest(Entity underBest, float underBestDist,
            Entity anyBest, float anyBestDist)
        {
            if (underBest == Entity.Null) return anyBest;
            if (anyBest == Entity.Null) return underBest;
            // underBest is by definition >= anyBest. Only accept it if the
            // detour cost is within SpreadDistRatio of nearest.
            if (underBestDist <= math.max(anyBestDist, NearDistFloor) * SpreadDistRatio) return underBest;
            return anyBest;
        }
    }
}
