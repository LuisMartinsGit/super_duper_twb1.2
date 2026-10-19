// AbilityLifecycleSystem.cs
// Per-frame engine for the data-driven ability system: fires active abilities
// (triggered via the existing AbilityActivated component), runs cast timers,
// resolves the aftermath chain, and ticks the ability effect timers
// (SelfDoT / LifeCling / AutoYieldBoost / UnderAutomation /
// cooldowns).
//
// Managed SystemBase (structural changes + managed AbilityCatalog lookups),
// mirroring UnitAbilitySystem / TempleHealSystem. Auto-registers via
// [UpdateInGroup]; ordered before combat so buffs apply the same frame.
//
// PERF (2026-10-15): the cooldown decay and the seven timed-marker countdowns
// walked every unit chunk in managed foreach loops, then cleared the expired
// markers one EntityManager call at a time. They are Burst IJobEntity passes
// now (run on the main thread, at the same point in the update), and an
// expired marker's enable bit is cleared in place — the same final state as
// TransientState.Clear. Only subtraction, max and a compare: IEEE-exact, so
// Burst and managed code give identical bits on every peer.

using Unity.Burst;
using Unity.Collections;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Abilities
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TheWaningBorder.Systems.Combat.MeleeCombatSystem))]
    public partial class AbilityLifecycleSystem : SystemBase
    {
        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every
        // call and these were never disposed, so this hot path leaked one
        // per invocation. A bloated registry slows every later query AND
        // every structural change. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_AbilityActivatedUnitAbilities =
        {
            ComponentType.ReadOnly<AbilityActivated>(),
            ComponentType.ReadOnly<UnitAbilities>(),
        };
        static CachedEntityQuery QC_AbilityActivatedUnitAbilities;

        #endregion

        // PERF (2026-10-16): emptiness guards for the seven timed-marker
        // countdowns (and the three managed passes over enableable state).
        // Each marker is pre-added DISABLED on every unit, so each Run() walked
        // every unit chunk (and stamped it changed) on every tick although a
        // live marker is rare. IsEmpty honours enable bits: a guard reading
        // "empty" means the job would have found every bit off and done
        // nothing. Result-identical.
        private EntityQuery _anyLifeCling, _anyAutoYield, _anyUnderAutomation, _anyCharging,
                            _anyChargeBonus, _anyNextCharge, _anyTempDisarm,
                            _anyCasting, _anyAftermath, _anySelfDoT;

        protected override void OnCreate()
        {
            _anyLifeCling = GetEntityQuery(ComponentType.ReadOnly<LifeCling>());
            _anyAutoYield = GetEntityQuery(ComponentType.ReadOnly<AutoYieldBoost>());
            _anyUnderAutomation = GetEntityQuery(ComponentType.ReadOnly<UnderAutomation>());
            _anyCharging = GetEntityQuery(ComponentType.ReadOnly<Charging>());
            _anyChargeBonus = GetEntityQuery(ComponentType.ReadOnly<ChargeDamageBonus>());
            _anyNextCharge = GetEntityQuery(ComponentType.ReadOnly<NextChargePct>());
            _anyTempDisarm = GetEntityQuery(ComponentType.ReadOnly<TempDisarm>());
            _anyCasting = GetEntityQuery(ComponentType.ReadOnly<AbilityCastState>());
            _anyAftermath = GetEntityQuery(ComponentType.ReadOnly<AbilityAftermath>());
            _anySelfDoT = GetEntityQuery(ComponentType.ReadOnly<SelfDoT>());
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f) return;
            var em = EntityManager;

            // ---- 1. Cooldown decay ----
            new CooldownDecayJob { Dt = dt }.Run();

            // ---- 2. Consume AbilityActivated -> begin the unit's active ability ----
            var actQ = QC_AbilityActivatedUnitAbilities.Get(em, QT_AbilityActivatedUnitAbilities);
            using (var acts = actQ.ToEntityArray(Allocator.Temp))
            {
                // Blood Rain (War) silences every caster on the map, both
                // sides, for its duration - docs/Design/Sects.md section 6.
                // Read once per frame rather than per activation.
                bool silenced = TheWaningBorder.Systems.Sect.SectActivePowerHelper
                    .IsGloballySilenced(em);

                foreach (var e in acts)
                {
                    var act = em.GetComponentData<AbilityActivated>(e);
                    var target = act.Target;
                    var slots = em.GetComponentData<UnitAbilities>(e);

                    // An explicit slot is the player naming WHICH ability;
                    // -1 keeps the old "first ready active" behaviour, which
                    // is still right for every unit that has only one.
                    int slot = act.Slot >= 0 && act.Slot < 4
                        ? (RequestedSlotUsable(slots, em, e, act.Slot) ? act.Slot : -1)
                        : FirstActiveSlot(slots, em, e);
                    TransientState.Clear<AbilityActivated>(em, e);
                    if (slot < 0) continue;
                    // One channel at a time. The cooldown is only charged when
                    // a channel COMPLETES, so without this a second click during
                    // the channel would find the slot "ready" and restart it.
                    if (TransientState.Active<AbilityCastState>(em, e)) continue;
                    // The dead and the airborne cast nothing.
                    if (IsInterrupted(em, e)) continue;
                    // Drop the activation BEFORE the cooldown is charged, so a
                    // silenced cast costs the player nothing but the click.
                    // Anything already winding up in AbilityCastState below
                    // still resolves: silence stops new casts, it does not
                    // un-cast what is already in flight.
                    if (silenced) continue;

                    // Blinding Glare III (Witness) locks the individual unit
                    // rather than the map. Same rule as the global silence: the
                    // activation is dropped before the cooldown is charged, and
                    // anything already winding up still resolves.
                    if (TransientState.Active<SectBlinded>(em, e)
                        && em.GetComponentData<SectBlinded>(e).LocksAbilities != 0) continue;

                    int idx = slots.Get(slot);
                    var card = AbilityCatalog.Get(idx);
                    if (card == null) continue;

                    // A single-target card is cast ON an entity: re-check the
                    // pick on every peer BEFORE the cooldown is charged. A
                    // ground click, the wrong kind, a target out of range or
                    // one that died since the click costs nothing (the client
                    // already explained). docs/Design/Spells.md §6.
                    if (card.IsEntityTargeted
                        && !AbilityTargetRules.Check(em, card, e, target, out _)) continue;
                    // Line of sight (Spells.md §6.1): an aimed point or an
                    // enemy target the caster's faction cannot see right now
                    // is refused here, before anything is spent. Reveals,
                    // passives and caster-centred cards are exempt. Fresh
                    // gather: the authoritative test must not depend on a
                    // cache warmed differently on this peer.
                    if (!AbilityTargetRules.CastInSight(em, card, e, target, fresh: true)) continue;

                    // Channelled: the cooldown waits for the channel to land
                    // (section 3). Instant: charged now, then applied.
                    // docs/Design/Spells.md, "Cast and interrupt".
                    if (card.CastTime > 0f)
                    {
                        var cast = new AbilityCastState
                        {
                            AbilityIndex = idx, CastRemaining = card.CastTime, Target = target,
                            Slot = slot, CastTotal = card.CastTime,
                        };
                        SnapshotOrders(em, e, ref cast);
                        TransientState.Set(em, e, cast);
                    }
                    else
                    {
                        SetCooldown(em, e, slot, card.EffectiveCooldown);
                        AbilityEffectExecutor.Apply(em, e, card, target);
                    }
                }
            }

            // ---- 3. Cast timers -> interrupt, or apply + charge the cooldown ----
            // Interrupt first: a channel broken by a new order, a stun or
            // death is simply lost -- no effect, and no cooldown charged, so the
            // player can recast at once. Silence and Blinding Glare do NOT
            // break a channel already in flight (they only stop new casts).
            var castDone = new NativeList<Entity>(Allocator.Temp);
            var castBroken = new NativeList<Entity>(Allocator.Temp);
            if (!_anyCasting.IsEmpty)
            foreach (var (cast, e) in SystemAPI.Query<RefRW<AbilityCastState>>().WithEntityAccess())
            {
                var c = cast.ValueRO;
                if (IsInterrupted(em, e) || (HasNewOrder(em, e, c) && !Unbreakable(c)))
                { castBroken.Add(e); continue; }
                c.CastRemaining -= dt;
                cast.ValueRW = c;
                if (c.CastRemaining <= 0f) castDone.Add(e);
            }
            foreach (var e in castBroken) TransientState.Clear<AbilityCastState>(em, e);
            castBroken.Dispose();
            foreach (var e in castDone)
            {
                var c = em.GetComponentData<AbilityCastState>(e);
                TransientState.Clear<AbilityCastState>(em, e);
                var card = AbilityCatalog.Get(c.AbilityIndex);
                if (card == null) continue;
                // The target died, vanished or stopped being legal during the
                // channel: the channel breaks like an interrupt -- no effect,
                // no cooldown (docs/Design/Spells.md §6).
                if (card.IsEntityTargeted
                    && !AbilityTargetRules.Check(em, card, e, c.Target, out _)) continue;
                SetCooldown(em, e, c.Slot, card.EffectiveCooldown);
                AbilityEffectExecutor.Apply(em, e, card, c.Target);
            }
            castDone.Dispose();

            // ---- 4. Aftermath chain ----
            var afterDone = new NativeList<Entity>(Allocator.Temp);
            if (!_anyAftermath.IsEmpty)
            foreach (var (aft, e) in SystemAPI.Query<RefRW<AbilityAftermath>>().WithEntityAccess())
            {
                var a = aft.ValueRO;
                a.Remaining -= dt;
                aft.ValueRW = a;
                if (a.Remaining <= 0f) afterDone.Add(e);
            }
            foreach (var e in afterDone)
            {
                var a = em.GetComponentData<AbilityAftermath>(e);
                TransientState.Clear<AbilityAftermath>(em, e);
                var parent = AbilityCatalog.Get(a.AbilityIndex);
                if (parent?.Aftermath == null) continue;
                foreach (var name in parent.Aftermath)
                {
                    var next = AbilityCatalog.Get(name);
                    if (next != null) AbilityEffectExecutor.Apply(em, e, next, a.Target);
                }
            }
            afterDone.Dispose();

            // ---- 5. Self DoT ----
            var dotDone = new NativeList<Entity>(Allocator.Temp);
            // (Guarded: its RefRW<Health> otherwise completed every job
            // writing Health on every tick, with no SelfDoT live.)
            if (!_anySelfDoT.IsEmpty)
            foreach (var (dot, hp, e) in SystemAPI.Query<RefRW<SelfDoT>, RefRW<Health>>().WithEntityAccess())
            {
                var d = dot.ValueRO;
                d.TimeRemaining -= dt;
                d.FractionalAccumulator += d.Dps * dt;
                int whole = (int)d.FractionalAccumulator;
                if (whole > 0)
                {
                    d.FractionalAccumulator -= whole;
                    var h = hp.ValueRO;
                    int floor = TransientState.Active<LifeCling>(em, e) ? em.GetComponentData<LifeCling>(e).Floor : 0;
                    h.Value = math.max(floor, h.Value - whole);
                    hp.ValueRW = h;
                }
                dot.ValueRW = d;
                if (d.TimeRemaining <= 0f) dotDone.Add(e);
            }
            foreach (var e in dotDone) TransientState.Clear<SelfDoT>(em, e);
            dotDone.Dispose();

            // ---- 6. Timed markers: decrement TimeRemaining, clear at 0 ----
            // (War Horn's NextChargePct window is normally consumed by the
            // charge that lands — this is the timeout for cavalry that never
            // connected; TempDisarm is Full Gallop's sprint lockout.)
            if (!_anyLifeCling.IsEmpty) new LifeClingTimerJob { Dt = dt }.Run();
            if (!_anyAutoYield.IsEmpty) new AutoYieldBoostTimerJob { Dt = dt }.Run();
            if (!_anyUnderAutomation.IsEmpty) new UnderAutomationTimerJob { Dt = dt }.Run();
            if (!_anyCharging.IsEmpty) new ChargingTimerJob { Dt = dt }.Run();
            if (!_anyChargeBonus.IsEmpty) new ChargeDamageBonusTimerJob { Dt = dt }.Run();
            if (!_anyNextCharge.IsEmpty) new NextChargePctTimerJob { Dt = dt }.Run();
            if (!_anyTempDisarm.IsEmpty) new TempDisarmTimerJob { Dt = dt }.Run();

            // (Use Celestar's fog reveal reuses the sect RevealCircle power — the
            // reveal entity is created + ticked by SectActivePowerSystem /
            // SectRevealTickSystem, so nothing to tick here.)
        }

        /// <summary>
        /// Death or a stun breaks a channel (and refuses a new one): Health at
        /// 0, the death animation running, or hurled into the air by
        /// Shardbound Fury (Launched -- the one hard stun in the game).
        /// </summary>
        private static bool IsInterrupted(EntityManager em, Entity e)
        {
            if (em.HasComponent<Health>(e) && em.GetComponentData<Health>(e).Value <= 0) return true;
            if (TransientState.Active<DeathAnimationState>(em, e)) return true;
            if (em.HasComponent<TheWaningBorder.Entities.Launched>(e)) return true;
            return false;
        }

        /// <summary>The curse's Veil Offering (Territory_Claims.md §6.9.5):
        /// only death or a stun breaks it, never an order.</summary>
        private static bool Unbreakable(in AbilityCastState cast)
        {
            var card = AbilityCatalog.Get(cast.AbilityIndex);
            return card != null && card.HasEffect(AbilityEffectKind.CurseVeilOffering);
        }

        /// <summary>Record the move / attack order live when the channel began,
        /// so a later DIFFERENT order can be told from the one the caster was
        /// already carrying out.</summary>
        private static void SnapshotOrders(EntityManager em, Entity e, ref AbilityCastState cast)
        {
            if (TransientState.Active<MoveCommand>(em, e))
            {
                cast.HadMove = 1;
                cast.MoveDestination = em.GetComponentData<MoveCommand>(e).Destination;
            }
            if (TransientState.Active<AttackCommand>(em, e))
            {
                cast.HadAttack = 1;
                cast.AttackTarget = em.GetComponentData<AttackCommand>(e).Target;
            }
        }

        /// <summary>
        /// True when the caster has been given a new move or attack order since
        /// the channel began -- the player (or AI) changed its mind, and the
        /// cast is abandoned. An order that merely ENDED (arrival, target dead)
        /// is not a new one.
        /// </summary>
        private static bool HasNewOrder(EntityManager em, Entity e, in AbilityCastState cast)
        {
            if (TransientState.Active<MoveCommand>(em, e))
            {
                if (cast.HadMove == 0) return true;
                var dest = em.GetComponentData<MoveCommand>(e).Destination;
                if (math.distancesq(dest, cast.MoveDestination) > OrderChangeEpsilonSq) return true;
            }
            if (TransientState.Active<AttackCommand>(em, e))
            {
                if (cast.HadAttack == 0) return true;
                if (em.GetComponentData<AttackCommand>(e).Target != cast.AttackTarget) return true;
            }
            return false;
        }

        /// <summary>Squared distance under which a re-issued move is the same
        /// order (float noise in a re-snapped destination), not a new one.</summary>
        private const float OrderChangeEpsilonSq = 0.01f;

        /// <summary>First slot holding an Active ability that's off cooldown.</summary>
        private static int FirstActiveSlot(UnitAbilities slots, EntityManager em, Entity e)
        {
            var cds = em.HasComponent<AbilityCooldowns>(e) ? em.GetComponentData<AbilityCooldowns>(e) : default;
            for (int s = 0; s < 4; s++)
            {
                var card = AbilityCatalog.Get(slots.Get(s));
                if (card == null || card.Activation != AbilityActivation.Active) continue;
                if (!AbilityQuery.IsUnlocked(em, e, card)) continue;
                float cd = s == 0 ? cds.C0 : s == 1 ? cds.C1 : s == 2 ? cds.C2 : cds.C3;
                if (cd <= 0f) return s;
            }
            return -1;
        }

        /// <summary>Is the slot the caller asked for actually an unlocked,
        /// ready Active? A stale button on a hero that has just died and been
        /// revived lower could otherwise fire an ability he no longer has.</summary>
        private static bool RequestedSlotUsable(UnitAbilities slots, EntityManager em, Entity e, int slot)
        {
            var card = AbilityCatalog.Get(slots.Get(slot));
            if (card == null || card.Activation != AbilityActivation.Active) return false;
            if (!AbilityQuery.IsUnlocked(em, e, card)) return false;
            var cds = em.HasComponent<AbilityCooldowns>(e) ? em.GetComponentData<AbilityCooldowns>(e) : default;
            float cd = slot == 0 ? cds.C0 : slot == 1 ? cds.C1 : slot == 2 ? cds.C2 : cds.C3;
            return cd <= 0f;
        }

        private static void SetCooldown(EntityManager em, Entity e, int slot, float cd)
        {
            var c = em.HasComponent<AbilityCooldowns>(e) ? em.GetComponentData<AbilityCooldowns>(e) : default;
            switch (slot) { case 0: c.C0 = cd; break; case 1: c.C1 = cd; break; case 2: c.C2 = cd; break; default: c.C3 = cd; break; }
            AddOrSet(em, e, c);
        }

        private static void AddOrSet<T>(EntityManager em, Entity e, T value) where T : unmanaged, IComponentData
        {
            if (em.HasComponent<T>(e)) em.SetComponentData(e, value);
            else em.AddComponentData(e, value);
        }
    }

    // ── Burst countdowns (see the PERF note in the header) ──────────────
    // One job per marker type: IJobEntity cannot be generic. Each skips a
    // disabled marker explicitly, so it does not matter whether the
    // EnabledRefRW parameter makes the query match disabled ones too.

    [BurstCompile]
    internal partial struct CooldownDecayJob : IJobEntity
    {
        public float Dt;
        void Execute(ref AbilityCooldowns c)
        {
            c.C0 = math.max(0f, c.C0 - Dt); c.C1 = math.max(0f, c.C1 - Dt);
            c.C2 = math.max(0f, c.C2 - Dt); c.C3 = math.max(0f, c.C3 - Dt);
        }
    }

    [BurstCompile]
    internal partial struct LifeClingTimerJob : IJobEntity
    {
        public float Dt;
        void Execute(ref LifeCling c, EnabledRefRW<LifeCling> on)
        {
            if (!on.ValueRO) return;
            c.TimeRemaining -= Dt;
            if (c.TimeRemaining <= 0f) on.ValueRW = false;
        }
    }

    [BurstCompile]
    internal partial struct AutoYieldBoostTimerJob : IJobEntity
    {
        public float Dt;
        void Execute(ref AutoYieldBoost c, EnabledRefRW<AutoYieldBoost> on)
        {
            if (!on.ValueRO) return;
            c.TimeRemaining -= Dt;
            if (c.TimeRemaining <= 0f) on.ValueRW = false;
        }
    }

    [BurstCompile]
    internal partial struct UnderAutomationTimerJob : IJobEntity
    {
        public float Dt;
        void Execute(ref UnderAutomation c, EnabledRefRW<UnderAutomation> on)
        {
            if (!on.ValueRO) return;
            c.TimeRemaining -= Dt;
            if (c.TimeRemaining <= 0f) on.ValueRW = false;
        }
    }

    [BurstCompile]
    internal partial struct ChargingTimerJob : IJobEntity
    {
        public float Dt;
        void Execute(ref Charging c, EnabledRefRW<Charging> on)
        {
            if (!on.ValueRO) return;
            c.TimeRemaining -= Dt;
            if (c.TimeRemaining <= 0f) on.ValueRW = false;
        }
    }

    [BurstCompile]
    internal partial struct ChargeDamageBonusTimerJob : IJobEntity
    {
        public float Dt;
        void Execute(ref ChargeDamageBonus c, EnabledRefRW<ChargeDamageBonus> on)
        {
            if (!on.ValueRO) return;
            c.TimeRemaining -= Dt;
            if (c.TimeRemaining <= 0f) on.ValueRW = false;
        }
    }

    [BurstCompile]
    internal partial struct NextChargePctTimerJob : IJobEntity
    {
        public float Dt;
        void Execute(ref NextChargePct c, EnabledRefRW<NextChargePct> on)
        {
            if (!on.ValueRO) return;
            c.TimeRemaining -= Dt;
            if (c.TimeRemaining <= 0f) on.ValueRW = false;
        }
    }

    [BurstCompile]
    internal partial struct TempDisarmTimerJob : IJobEntity
    {
        public float Dt;
        void Execute(ref TempDisarm c, EnabledRefRW<TempDisarm> on)
        {
            if (!on.ValueRO) return;
            c.TimeRemaining -= Dt;
            if (c.TimeRemaining <= 0f) on.ValueRW = false;
        }
    }
}
