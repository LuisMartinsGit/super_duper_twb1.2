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
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Abilities;

namespace TheWaningBorder.Systems.Abilities
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TheWaningBorder.Systems.Combat.MeleeCombatSystem))]
    public partial class AbilityLifecycleSystem : SystemBase
    {
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
            var actQ = em.CreateEntityQuery(ComponentType.ReadOnly<AbilityActivated>(),
                                            ComponentType.ReadOnly<UnitAbilities>());
            using (var acts = actQ.ToEntityArray(Allocator.Temp))
            {
                // Blood Rain (War) silences every caster on the map, both
                // sides, for its duration - docs/Design/Sects.md section 6.
                // Read once per frame rather than per activation.
                bool silenced = TheWaningBorder.Systems.Sect.SectActivePowerHelper
                    .IsGloballySilenced(em);

                foreach (var e in acts)
                {
                    var target = em.GetComponentData<AbilityActivated>(e).Target;
                    var slots = em.GetComponentData<UnitAbilities>(e);
                    int slot = FirstActiveSlot(slots, em, e);
                    em.RemoveComponent<AbilityActivated>(e);
                    if (slot < 0) continue;
                    // Drop the activation BEFORE the cooldown is charged, so a
                    // silenced cast costs the player nothing but the click.
                    // Anything already winding up in AbilityCastState below
                    // still resolves: silence stops new casts, it does not
                    // un-cast what is already in flight.
                    if (silenced) continue;

                    int idx = slots.Get(slot);
                    var card = AbilityCatalog.Get(idx);
                    if (card == null) continue;

                    float cd = card.Cooldown > 0f ? card.Cooldown : (card.CastTime + card.Duration + 1f);
                    SetCooldown(em, e, slot, cd);

                    if (card.CastTime > 0f)
                        AddOrSet(em, e, new AbilityCastState { AbilityIndex = idx, CastRemaining = card.CastTime, Target = target });
                    else
                        AbilityEffectExecutor.Apply(em, e, card, target);
                }
            }

            // ---- 3. Cast timers -> apply on completion ----
            var castDone = new NativeList<Entity>(Allocator.Temp);
            foreach (var (cast, e) in SystemAPI.Query<RefRW<AbilityCastState>>().WithEntityAccess())
            {
                var c = cast.ValueRO;
                c.CastRemaining -= dt;
                cast.ValueRW = c;
                if (c.CastRemaining <= 0f) castDone.Add(e);
            }
            foreach (var e in castDone)
            {
                var c = em.GetComponentData<AbilityCastState>(e);
                em.RemoveComponent<AbilityCastState>(e);
                AbilityEffectExecutor.Apply(em, e, AbilityCatalog.Get(c.AbilityIndex), c.Target);
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

        /// <summary>First slot holding an Active ability that's off cooldown.</summary>
        private static int FirstActiveSlot(UnitAbilities slots, EntityManager em, Entity e)
        {
            var cds = em.HasComponent<AbilityCooldowns>(e) ? em.GetComponentData<AbilityCooldowns>(e) : default;
            for (int s = 0; s < 4; s++)
            {
                var card = AbilityCatalog.Get(slots.Get(s));
                if (card == null || card.Activation != AbilityActivation.Active) continue;
                float cd = s == 0 ? cds.C0 : s == 1 ? cds.C1 : s == 2 ? cds.C2 : cds.C3;
                if (cd <= 0f) return s;
            }
            return -1;
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
