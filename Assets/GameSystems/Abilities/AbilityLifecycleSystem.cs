// AbilityLifecycleSystem.cs
// Per-frame engine for the data-driven ability system: fires active abilities
// (triggered via the existing AbilityActivated component), runs cast timers,
// resolves the aftermath chain, and ticks the ability effect timers
// (SelfDoT / LifeCling / AutoYieldBoost / UnderAutomation /
// cooldowns).
//
// Managed SystemBase (structural changes + managed AbilityCatalog lookups),
// mirroring UnitAbilitySystem / ShrineHealSystem. Auto-registers via
// [UpdateInGroup]; ordered before combat so buffs apply the same frame.

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

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f) return;
            var em = EntityManager;

            // ---- 1. Cooldown decay ----
            foreach (var cd in SystemAPI.Query<RefRW<AbilityCooldowns>>())
            {
                var c = cd.ValueRO;
                c.C0 = math.max(0f, c.C0 - dt); c.C1 = math.max(0f, c.C1 - dt);
                c.C2 = math.max(0f, c.C2 - dt); c.C3 = math.max(0f, c.C3 - dt);
                cd.ValueRW = c;
            }

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
                    em.RemoveComponent<AbilityActivated>(e);
                    if (slot < 0) continue;
                    // One channel at a time. The cooldown is only charged when
                    // a channel COMPLETES, so without this a second click during
                    // the channel would find the slot "ready" and restart it.
                    if (em.HasComponent<AbilityCastState>(e)) continue;
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
                    if (em.HasComponent<SectBlinded>(e)
                        && em.GetComponentData<SectBlinded>(e).LocksAbilities != 0) continue;

                    int idx = slots.Get(slot);
                    var card = AbilityCatalog.Get(idx);
                    if (card == null) continue;

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
                        AddOrSet(em, e, cast);
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
            foreach (var (cast, e) in SystemAPI.Query<RefRW<AbilityCastState>>().WithEntityAccess())
            {
                var c = cast.ValueRO;
                if (IsInterrupted(em, e) || HasNewOrder(em, e, c)) { castBroken.Add(e); continue; }
                c.CastRemaining -= dt;
                cast.ValueRW = c;
                if (c.CastRemaining <= 0f) castDone.Add(e);
            }
            foreach (var e in castBroken) em.RemoveComponent<AbilityCastState>(e);
            castBroken.Dispose();
            foreach (var e in castDone)
            {
                var c = em.GetComponentData<AbilityCastState>(e);
                em.RemoveComponent<AbilityCastState>(e);
                var card = AbilityCatalog.Get(c.AbilityIndex);
                if (card == null) continue;
                SetCooldown(em, e, c.Slot, card.EffectiveCooldown);
                AbilityEffectExecutor.Apply(em, e, card, c.Target);
            }
            castDone.Dispose();

            // ---- 4. Aftermath chain ----
            var afterDone = new NativeList<Entity>(Allocator.Temp);
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
                em.RemoveComponent<AbilityAftermath>(e);
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
                    int floor = em.HasComponent<LifeCling>(e) ? em.GetComponentData<LifeCling>(e).Floor : 0;
                    h.Value = math.max(floor, h.Value - whole);
                    hp.ValueRW = h;
                }
                dot.ValueRW = d;
                if (d.TimeRemaining <= 0f) dotDone.Add(e);
            }
            foreach (var e in dotDone) em.RemoveComponent<SelfDoT>(e);
            dotDone.Dispose();

            // ---- 6. Timed markers: decrement TimeRemaining, remove at 0 ----
            {
                var done = new NativeList<Entity>(Allocator.Temp);
                foreach (var (c, e) in SystemAPI.Query<RefRW<LifeCling>>().WithEntityAccess())
                { var v = c.ValueRO; v.TimeRemaining -= dt; c.ValueRW = v; if (v.TimeRemaining <= 0f) done.Add(e); }
                foreach (var e in done) em.RemoveComponent<LifeCling>(e);
                done.Dispose();
            }
            {
                var done = new NativeList<Entity>(Allocator.Temp);
                foreach (var (c, e) in SystemAPI.Query<RefRW<AutoYieldBoost>>().WithEntityAccess())
                { var v = c.ValueRO; v.TimeRemaining -= dt; c.ValueRW = v; if (v.TimeRemaining <= 0f) done.Add(e); }
                foreach (var e in done) em.RemoveComponent<AutoYieldBoost>(e);
                done.Dispose();
            }
            {
                var done = new NativeList<Entity>(Allocator.Temp);
                foreach (var (c, e) in SystemAPI.Query<RefRW<UnderAutomation>>().WithEntityAccess())
                { var v = c.ValueRO; v.TimeRemaining -= dt; c.ValueRW = v; if (v.TimeRemaining <= 0f) done.Add(e); }
                foreach (var e in done) em.RemoveComponent<UnderAutomation>(e);
                done.Dispose();
            }
            {
                var done = new NativeList<Entity>(Allocator.Temp);
                foreach (var (c, e) in SystemAPI.Query<RefRW<Charging>>().WithEntityAccess())
                { var v = c.ValueRO; v.TimeRemaining -= dt; c.ValueRW = v; if (v.TimeRemaining <= 0f) done.Add(e); }
                foreach (var e in done) em.RemoveComponent<Charging>(e);
                done.Dispose();
            }
            {
                var done = new NativeList<Entity>(Allocator.Temp);
                foreach (var (c, e) in SystemAPI.Query<RefRW<ChargeDamageBonus>>().WithEntityAccess())
                { var v = c.ValueRO; v.TimeRemaining -= dt; c.ValueRW = v; if (v.TimeRemaining <= 0f) done.Add(e); }
                foreach (var e in done) em.RemoveComponent<ChargeDamageBonus>(e);
                done.Dispose();
            }
            {
                // War Horn window — normally consumed by the charge that lands, this
                // is the timeout for cavalry that never connected.
                var done = new NativeList<Entity>(Allocator.Temp);
                foreach (var (c, e) in SystemAPI.Query<RefRW<NextChargePct>>().WithEntityAccess())
                { var v = c.ValueRO; v.TimeRemaining -= dt; c.ValueRW = v; if (v.TimeRemaining <= 0f) done.Add(e); }
                foreach (var e in done) em.RemoveComponent<NextChargePct>(e);
                done.Dispose();
            }
            {
                // Full Gallop's sprint lockout.
                var done = new NativeList<Entity>(Allocator.Temp);
                foreach (var (c, e) in SystemAPI.Query<RefRW<TempDisarm>>().WithEntityAccess())
                { var v = c.ValueRO; v.TimeRemaining -= dt; c.ValueRW = v; if (v.TimeRemaining <= 0f) done.Add(e); }
                foreach (var e in done) em.RemoveComponent<TempDisarm>(e);
                done.Dispose();
            }

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
}
