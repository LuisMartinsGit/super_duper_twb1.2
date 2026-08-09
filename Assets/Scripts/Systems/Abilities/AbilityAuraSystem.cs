// AbilityAuraSystem.cs
// Continuous passives for the data-driven ability system:
//   - Passive AURA abilities (King's Call): refresh a SpellBuff on same-faction
//     units in radius; grant ChargeDamageBonus to allied cavalry.
//   - Scout Sight: line-of-sight ramps up while the owner stands still.
//   - Ledger auto-cast: an idle Ledger fires Automate Facility on a nearby
//     eligible economy building (routes through AbilityActivated so the normal
//     cast pipeline runs).
//   - Cavalry charge detection: fast-moving cavalry closing on a target get the
//     Charging marker (read on-hit for the King's Call / King Lexor charge bonus).
//
// Managed SystemBase; self-throttled to ~0.4 s (auras/AI don't need per-frame).
// Buffs use a short refresh window so they fade automatically when a unit leaves
// the aura (SpellBuffSystem expires them).

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Abilities;
using TheWaningBorder.Economy; // SuppliesIncome

namespace TheWaningBorder.Systems.Abilities
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class AbilityAuraSystem : SystemBase
    {
        private const float Interval = 0.4f;
        private const float BuffRefresh = Interval + 0.6f; // buff outlives one tick so it only fades when truly out of range
        // Scout Sight: three vision levels.
        //   moving   → X = BaseLos * ScoutMovingFraction (small, never lower);
        //   settling → LOS ramps linearly X→Y over ScoutRampSeconds while the
        //              scout neither moves nor takes damage;
        //   settled  → Y = BaseLos * ScoutMaxFraction, held steady.
        // Moving or taking damage resets the ramp to X (never below it).
        private const float ScoutRampSeconds   = 25f;
        private const float ScoutMovingFraction = 0.25f;
        private const float ScoutMaxFraction    = 1.0f;
        // Pre-Celestarii handicap (design 2026-08-02): until the faction
        // researches ScoutingCelestarii, the settled max is capped at 80 %
        // of BaseLos and the ramp fills half as fast. The research restores
        // both to full — read live, so it applies the moment it completes.
        private const float PreCelestariiMaxScale  = 0.8f;
        private const float PreCelestariiRampScale = 0.5f;
        // Stillness detection is a real speed threshold (u/s), not a raw
        // per-tick displacement: collision-separation and arrival-settling
        // nudges stay below it, so a perched scout at full vision no longer
        // gets spuriously reset (which read as LOS "pulsating").
        private const float ScoutStillSpeed = 1.0f;
        private double _last;

        protected override void OnUpdate()
        {
            double now = SystemAPI.Time.ElapsedTime;
            if (now - _last < Interval) return;
            float elapsed = (float)(now - _last);
            _last = now;
            var em = EntityManager;

            ApplyPassiveAuras(em);
            TickScoutSight(em, elapsed);
            TickLedgerAutoCast(em);
            TickChargeDetection(em, elapsed);
        }

        // ---- King's Call & any Passive Aura ability ----
        private void ApplyPassiveAuras(EntityManager em)
        {
            // Snapshot all units (potential aura targets) once.
            var unitQ = em.CreateEntityQuery(ComponentType.ReadOnly<UnitTag>(),
                                             ComponentType.ReadOnly<FactionTag>(),
                                             ComponentType.ReadOnly<LocalTransform>());
            using var units = unitQ.ToEntityArray(Allocator.Temp);
            using var unitFac = unitQ.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var unitXf = unitQ.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var auraQ = em.CreateEntityQuery(ComponentType.ReadOnly<UnitAbilities>(),
                                             ComponentType.ReadOnly<FactionTag>(),
                                             ComponentType.ReadOnly<LocalTransform>());
            using var auras = auraQ.ToEntityArray(Allocator.Temp);

            foreach (var src in auras)
            {
                var slots = em.GetComponentData<UnitAbilities>(src);
                AbilityCard aura = null;
                for (int s = 0; s < 4; s++)
                {
                    var c = AbilityCatalog.Get(slots.Get(s));
                    if (c != null && c.Activation == AbilityActivation.Passive && c.Targeting == AbilityTargeting.Aura)
                    { aura = c; break; }
                }
                if (aura == null) continue;

                Faction srcFac = em.GetComponentData<FactionTag>(src).Value;
                float3 srcPos = em.GetComponentData<LocalTransform>(src).Position;
                float radSq = aura.Radius * aura.Radius;

                float atkMult = 1f + aura.EffectValue(AbilityEffectKind.AttackPct) / 100f;
                float armor = aura.EffectValue(AbilityEffectKind.ArmorPct); // flat placeholder
                int chargeBonus = (int)aura.EffectValue(AbilityEffectKind.ChargeBonusFlat);

                for (int i = 0; i < units.Length; i++)
                {
                    if (unitFac[i].Value != srcFac) continue; // same-faction (== same culture) allies
                    float2 d = new float2(unitXf[i].Position.x - srcPos.x, unitXf[i].Position.z - srcPos.z);
                    if (math.dot(d, d) > radSq) continue;

                    var buff = em.HasComponent<SpellBuff>(units[i]) ? em.GetComponentData<SpellBuff>(units[i]) : default;
                    buff.DamageMultiplier = math.max(buff.DamageMultiplier, atkMult);
                    buff.ArmorBonus = math.max(buff.ArmorBonus, armor);
                    buff.TimeRemaining = math.max(buff.TimeRemaining, BuffRefresh);
                    AddOrSet(em, units[i], buff);

                    // Charge bonus to allied cavalry only.
                    if (chargeBonus > 0 && em.HasComponent<ArmorTypeData>(units[i]) &&
                        em.GetComponentData<ArmorTypeData>(units[i]).Value == ArmorType.Cavalry)
                    {
                        AddOrSet(em, units[i], new ChargeDamageBonus { Bonus = chargeBonus, TimeRemaining = BuffRefresh });
                    }
                }
            }
        }

        // ---- Scout Sight: small LOS on the move, ramps X→Y while still & unharmed ----
        private void TickScoutSight(EntityManager em, float elapsed)
        {
            foreach (var (state, los, xf, fac, e) in
                     SystemAPI.Query<RefRW<ScoutSightState>, RefRW<LineOfSight>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                         .WithEntityAccess())
            {
                var st = state.ValueRO;

                bool celestarii = FactionResearchState.Instance != null &&
                    FactionResearchState.Instance.HasResearched(fac.ValueRO.Value, "ScoutingCelestarii");
                float maxFraction = ScoutMaxFraction * (celestarii ? 1f : PreCelestariiMaxScale);
                float rampSeconds = ScoutRampSeconds / (celestarii ? 1f : PreCelestariiRampScale);
                float3 p = xf.ValueRO.Position;
                float moved = math.distance(new float2(p.x, p.z), new float2(st.LastX, st.LastZ));
                bool moving = elapsed > 0f && moved / elapsed > ScoutStillSpeed;
                st.LastX = p.x; st.LastZ = p.z;

                // Taking damage counts as disturbed — the ramp restarts.
                bool damaged = false;
                if (em.HasComponent<Health>(e))
                {
                    int hp = em.GetComponentData<Health>(e).Value;
                    damaged = hp < st.LastHealth;
                    st.LastHealth = hp;
                }

                // BaseLos is the scout's authored LOS (seeded at spawn). Guard for
                // older saves that never captured it.
                if (st.BaseLos <= 0f) st.BaseLos = math.max(1f, los.ValueRO.Radius);
                float movingLos = st.BaseLos * ScoutMovingFraction; // X
                float maxLos = st.BaseLos * maxFraction;            // Y

                // CurrentBonus is the ramp fraction [0..1]: 0 = moving LOS,
                // 1 = fully settled. It only ever resets to 0 (the X floor —
                // vision never drops below the moving level) or grows; at 1
                // the radius holds perfectly steady at Y.
                if (moving || damaged)
                    st.CurrentBonus = 0f;
                else
                    st.CurrentBonus = math.min(1f, st.CurrentBonus + elapsed / rampSeconds);

                los.ValueRW = new LineOfSight { Radius = math.lerp(movingLos, maxLos, st.CurrentBonus) };
                state.ValueRW = st;
            }
        }

        // ---- Ledger: fire Automate Facility on a nearby eligible eco building ----
        private void TickLedgerAutoCast(EntityManager em)
        {
            var ledgerQ = em.CreateEntityQuery(ComponentType.ReadOnly<LedgerTag>(),
                                               ComponentType.ReadOnly<FactionTag>(),
                                               ComponentType.ReadOnly<LocalTransform>());
            using var ledgers = ledgerQ.ToEntityArray(Allocator.Temp);
            if (ledgers.Length == 0) return;

            int automateIdx = AbilityCatalog.IndexOf("Automate Facility");
            var card = AbilityCatalog.Get(automateIdx);
            if (card == null) return;

            var bldgQ = em.CreateEntityQuery(ComponentType.ReadOnly<BuildingTag>(),
                                             ComponentType.ReadOnly<FactionTag>(),
                                             ComponentType.ReadOnly<LocalTransform>());
            using var bldgs = bldgQ.ToEntityArray(Allocator.Temp);
            using var bldgFac = bldgQ.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var bldgXf = bldgQ.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float castRange = card.Range + 2f;               // must be ~adjacent to automate
            float castRangeSq = castRange * castRange;

            foreach (var led in ledgers)
            {
                bool casting = em.HasComponent<AbilityCastState>(led) || em.HasComponent<AbilityActivated>(led);
                var cds = em.HasComponent<AbilityCooldowns>(led) ? em.GetComponentData<AbilityCooldowns>(led) : default;

                Faction fac = em.GetComponentData<FactionTag>(led).Value;
                float3 pos = em.GetComponentData<LocalTransform>(led).Position;

                // Nearest eligible economy building at ANY distance (not just in range).
                Entity best = Entity.Null; float bestSq = float.MaxValue; float3 bestPos = default;
                for (int i = 0; i < bldgs.Length; i++)
                {
                    if (bldgFac[i].Value != fac) continue;
                    if (em.HasComponent<UnderAutomation>(bldgs[i]) || em.HasComponent<AutoYieldBoost>(bldgs[i])) continue;
                    if (!IsEconomyBuilding(em, bldgs[i])) continue;
                    float2 d = new float2(bldgXf[i].Position.x - pos.x, bldgXf[i].Position.z - pos.z);
                    float sq = math.dot(d, d);
                    if (sq < bestSq) { bestSq = sq; best = bldgs[i]; bestPos = bldgXf[i].Position; }
                }

                if (best == Entity.Null)
                {
                    // Nothing left to automate — stop roaming.
                    StopLedger(em, led, pos);
                    continue;
                }

                if (bestSq <= castRangeSq)
                {
                    // In range: hold position and fire (unless already casting / cooling).
                    StopLedger(em, led, pos);
                    if (!casting && cds.C0 <= 0f)
                        AddOrSet(em, led, new AbilityActivated { Target = best });
                }
                else if (!casting)
                {
                    // Walk toward the target building via the AI move path. Only
                    // (re)issue when the goal actually changed, so we don't spam
                    // nav-path requests every throttle tick.
                    bool needMove = true;
                    if (em.HasComponent<DesiredDestination>(led))
                    {
                        var dd = em.GetComponentData<DesiredDestination>(led);
                        if (dd.Has == 1 && math.distancesq(dd.Position, bestPos) < 1f) needMove = false;
                    }
                    if (needMove)
                        TheWaningBorder.Core.Commands.CommandRouter.IssueMove(
                            em, led, bestPos, TheWaningBorder.Core.Commands.CommandSource.AI);
                }
            }
        }

        // Clear a Ledger's movement goal so it stops (used when it arrives at a
        // building or has nothing to automate). Leaves any in-flight cast alone.
        private static void StopLedger(EntityManager em, Entity led, float3 pos)
        {
            if (em.HasComponent<DesiredDestination>(led))
            {
                var dd = em.GetComponentData<DesiredDestination>(led);
                if (dd.Has != 0)
                    em.SetComponentData(led, new DesiredDestination { Position = pos, Has = 0 });
            }
        }

        private static bool IsEconomyBuilding(EntityManager em, Entity b)
        {
            // Buildings that produce/hold resources are eligible.
            return em.HasComponent<SuppliesIncome>(b) || em.HasComponent<GathererHutTag>(b) || em.HasComponent<HallTag>(b);
        }

        // ---- Cavalry charge: mark cavalry closing fast on a target ----
        private void TickChargeDetection(EntityManager em, float elapsed)
        {
            // Collect first, then apply — structural adds can't happen inside the query.
            var chargers = new NativeList<Entity>(Allocator.Temp);
            foreach (var (xf, tgt, e) in
                     SystemAPI.Query<RefRO<LocalTransform>, RefRO<Target>>().WithEntityAccess())
            {
                if (!em.HasComponent<ArmorTypeData>(e) || em.GetComponentData<ArmorTypeData>(e).Value != ArmorType.Cavalry)
                    continue;
                var t = tgt.ValueRO.Value;
                if (t == Entity.Null || !em.Exists(t) || !em.HasComponent<LocalTransform>(t)) continue;

                float dist = math.distance(xf.ValueRO.Position, em.GetComponentData<LocalTransform>(t).Position);
                if (dist > 2.5f) chargers.Add(e); // still closing (outside melee reach)
            }
            for (int i = 0; i < chargers.Length; i++)
                AddOrSet(em, chargers[i], new Charging { TimeRemaining = 1.5f });
            chargers.Dispose();
        }

        private static void AddOrSet<T>(EntityManager em, Entity e, T value) where T : unmanaged, IComponentData
        {
            if (em.HasComponent<T>(e)) em.SetComponentData(e, value);
            else em.AddComponentData(e, value);
        }
    }
}
