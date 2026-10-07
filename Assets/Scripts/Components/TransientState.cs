// TransientState.cs
// The one way to flip a unit's transient combat/movement state on and off.
//
// WHY (2026-09-03): ~16 components — Target, the command components,
// AttackMoveTag, ChaseAnchor, GuardSuppressed, DeathAnimationState, SpellBuff,
// FirstStrike, FormationSlotMemory, the damage ledgers, PresentationViewSpawned
// — were ADDED and REMOVED as units fought and moved. Every distinct
// combination of them is a permanent archetype, and a 76-minute match built
// 7,782 of them (97% empty): every structural change then walked that registry,
// and the EntityQueryManager's 16MB BlockAllocator finally overflowed — the
// "Cannot exceed budget" endgame crash, with the whole late-game slowdown as
// its run-up.
//
// The cure: those components are IEnableableComponent now, pre-added DISABLED
// by the factories, and toggled through these helpers. Flipping an enabled bit
// is not a structural change — a unit keeps ONE archetype for its whole life.
// Query semantics survive unchanged: WithAll<T> matches only enabled,
// WithNone<T> treats disabled as absent.
//
// Rules:
//   * "does the unit have X" is Active<T>() — presence alone now means nothing.
//   * Never RemoveComponent one of these from a unit; Clear<T>() it.
//   * The helpers tolerate entities that never got the pre-add (buildings,
//     projectiles, scenario husks): Set adds on demand (a one-time structural
//     change that converges), Clear/Active treat absence as inactive.

using Unity.Entities;

/// <summary>Enable-bit lifecycle for the pre-added transient components.
/// See the file header for why these must never be added/removed per fight.</summary>
public static class TransientState
{
    // ── EntityManager ────────────────────────────────────────────────

    /// <summary>Activate with data. Adds the component only if the entity
    /// never carried it (non-unit callers); units just flip the bit.</summary>
    public static void Set<T>(EntityManager em, Entity e, T value)
        where T : unmanaged, IComponentData, IEnableableComponent
    {
        if (!em.HasComponent<T>(e)) em.AddComponent<T>(e);
        em.SetComponentData(e, value);
        em.SetComponentEnabled<T>(e, true);
    }

    /// <summary>Activate a data-less flag.</summary>
    public static void SetFlag<T>(EntityManager em, Entity e)
        where T : unmanaged, IComponentData, IEnableableComponent
    {
        if (!em.HasComponent<T>(e)) em.AddComponent<T>(e);
        em.SetComponentEnabled<T>(e, true);
    }

    /// <summary>Deactivate. Safe when absent.</summary>
    public static void Clear<T>(EntityManager em, Entity e)
        where T : unmanaged, IComponentData, IEnableableComponent
    {
        if (em.HasComponent<T>(e))
            em.SetComponentEnabled<T>(e, false);
    }

    /// <summary>Present AND enabled — the old HasComponent meaning.</summary>
    public static bool Active<T>(EntityManager em, Entity e)
        where T : unmanaged, IComponentData, IEnableableComponent
        => em.HasComponent<T>(e) && em.IsComponentEnabled<T>(e);

    // ── EntityCommandBuffer ──────────────────────────────────────────
    // ECB has no read access, so the add-if-absent guard cannot be recorded.
    // AddComponent on an entity that already has T is set-value at playback,
    // and the pre-add guarantees units always have T — so Add+Enable is
    // correct for units AND for entities seeing T the first time.

    public static void Set<T>(EntityCommandBuffer ecb, Entity e, T value)
        where T : unmanaged, IComponentData, IEnableableComponent
    {
        ecb.AddComponent(e, value);
        ecb.SetComponentEnabled<T>(e, true);
    }

    public static void SetFlag<T>(EntityCommandBuffer ecb, Entity e)
        where T : unmanaged, IComponentData, IEnableableComponent
    {
        ecb.AddComponent<T>(e);
        ecb.SetComponentEnabled<T>(e, true);
    }

    /// <summary>Deactivate via ECB, guarded at record time — a
    /// SetComponentEnabled played back on an entity without T is a hard
    /// crash in the player (no safety checks), and units spawned outside
    /// the factory dispatcher (the Border creatures were the first) do not
    /// carry the pre-add. Absent means inactive; nothing to record.</summary>
    public static void Clear<T>(EntityManager em, EntityCommandBuffer ecb, Entity e)
        where T : unmanaged, IComponentData, IEnableableComponent
    {
        if (em.HasComponent<T>(e))
            ecb.SetComponentEnabled<T>(e, false);
    }

    // The full pre-add set, ONE structural change per spawn. The sect lever
    // stamp buffer rides along: "already processed" is read from the buffer's
    // CONTENT, so an empty pre-add is behaviour-neutral and stops the lever
    // pass from splitting every unit archetype in two. FirstStrike joined the
    // pre-add in UnitSet3 (2026-10-07): it is granted with TransientState.Set,
    // which on a pre-added unit only flips the bit.
    static readonly ComponentType[] UnitSet =
    {
        // Target is NOT here: factories already add it always-present with
        // Value = Entity.Null, and "no target" is the Null value, not absence.
        ComponentType.ReadWrite<TheWaningBorder.Core.Commands.Types.AttackCommand>(),
        ComponentType.ReadWrite<TheWaningBorder.Core.Commands.Types.AttackMoveCommand>(),
        ComponentType.ReadWrite<TheWaningBorder.Core.Commands.Types.MoveCommand>(),
        ComponentType.ReadWrite<AttackMoveTag>(),
        ComponentType.ReadWrite<UserMoveOrder>(),
        ComponentType.ReadWrite<ChaseAnchor>(),
        ComponentType.ReadWrite<GuardSuppressed>(),
        ComponentType.ReadWrite<DeathAnimationState>(),
        ComponentType.ReadWrite<SpellBuff>(),
        ComponentType.ReadWrite<FormationSlotMemory>(),
        ComponentType.ReadWrite<PresentationViewSpawned>(),
        ComponentType.ReadWrite<DamageDealtTotal>(),
        ComponentType.ReadWrite<LastAttackerEntity>(),
        ComponentType.ReadWrite<LastDamagedByFaction>(),
        ComponentType.ReadWrite<SectUnitLeverApplied>(),
    };

    // Second half of the pre-add set. A ComponentTypeSet holds at most 15
    // types (Unity.Entities throws past that), so the set is added in two
    // calls. Two structural changes at spawn, then one archetype for life.
    static readonly ComponentType[] UnitSet2 =
    {
        // Veil exposure + hero XP (2026-09-27). ExposureState and BorderDebuff
        // are PLAIN components pre-added at zero: a zero exposure clock and a
        // zero debuff read exactly as absent to every reader (the debuff is
        // folded in as 1 + AttPenalty / gated on SpeedPenalty > 0), so the
        // veil systems zero them instead of removing. VeilDebuffTag and
        // HeroXpAwarded are enableable flags, pre-added disabled below.
        ComponentType.ReadWrite<ExposureState>(),
        ComponentType.ReadWrite<BorderDebuff>(),
        ComponentType.ReadWrite<VeilDebuffTag>(),
        ComponentType.ReadWrite<HeroXpAwarded>(),
    };

    // Third and fourth halves (2026-10-07). The 8-player headless matches
    // crashed at 60-80 minutes with the SAME BlockAllocator overflow the first
    // pre-add cured: the world held ~3,300 entities but ~5,000 archetypes
    // (4,700+ empty), and the per-match census (WorldCensus NEWARCH lines)
    // named what was still being added and removed per fight — the ability
    // engine's timed buffs, the legacy sect-unit ability effects, every
    // Alanthor sect power's stamp, the formation membership pair and the
    // command-queue markers. They are IEnableableComponent now and ride the
    // same pre-add. Readers test TransientState.Active<T>, never HasComponent.
    static readonly ComponentType[] UnitSet3 =
    {
        ComponentType.ReadWrite<TheWaningBorder.Abilities.Charging>(),
        ComponentType.ReadWrite<TheWaningBorder.Abilities.ChargeDamageBonus>(),
        ComponentType.ReadWrite<TheWaningBorder.Abilities.NextChargePct>(),
        ComponentType.ReadWrite<TheWaningBorder.Abilities.TempDisarm>(),
        ComponentType.ReadWrite<TheWaningBorder.Abilities.SelfDoT>(),
        ComponentType.ReadWrite<TheWaningBorder.Abilities.LifeCling>(),
        ComponentType.ReadWrite<TheWaningBorder.Abilities.AbilityAftermath>(),
        ComponentType.ReadWrite<TheWaningBorder.Abilities.AbilityCastState>(),
        ComponentType.ReadWrite<AbilityActivated>(),
        ComponentType.ReadWrite<TheWaningBorder.Abilities.VolleyBuff>(),
        ComponentType.ReadWrite<TheWaningBorder.Abilities.NextShotBonus>(),
        ComponentType.ReadWrite<TheWaningBorder.Abilities.FirstStrike>(),
        ComponentType.ReadWrite<SpellDebuff>(),
        ComponentType.ReadWrite<Condemned>(),
        ComponentType.ReadWrite<Fortified>(),
    };

    static readonly ComponentType[] UnitSet4 =
    {
        ComponentType.ReadWrite<HealOverTime>(),
        ComponentType.ReadWrite<SectDisordered>(),
        ComponentType.ReadWrite<SectRegenTail>(),
        ComponentType.ReadWrite<SectDeathWard>(),
        ComponentType.ReadWrite<SectVeiled>(),
        ComponentType.ReadWrite<SectCurseWard>(),
        ComponentType.ReadWrite<StealthTag>(),
        ComponentType.ReadWrite<SectHaste>(),
        ComponentType.ReadWrite<SectBlinded>(),
        ComponentType.ReadWrite<StealthRevealed>(),
        ComponentType.ReadWrite<Invulnerable>(),
        ComponentType.ReadWrite<MarkedForSentence>(),
        ComponentType.ReadWrite<VenerationFervor>(),
        ComponentType.ReadWrite<FormationMemberState>(),
        ComponentType.ReadWrite<FormationSpeedOverride>(),
    };

    // CommandQueueActive / QueuedMoveStep are enableable flags. AntiquityKills
    // and AttainderLedger are PLAIN counters pre-added at zero: a zero tally
    // reads exactly as absent to every reader (the damage bonus needs n > 0,
    // the Writ bills Against(faction) == 0), so presence means nothing.
    // HoldPositionTag is deliberately NOT here: the emplacement factories add
    // it ENABLED before this runs, and a set-add cannot tell which types were
    // already there, so the disable below would wipe their hold. It is
    // enableable and toggled with Set/Clear, so a unit gains it at most once.
    static readonly ComponentType[] UnitSet5 =
    {
        ComponentType.ReadWrite<CommandQueueActive>(),
        ComponentType.ReadWrite<QueuedMoveStep>(),
        ComponentType.ReadWrite<AntiquityKills>(),
        ComponentType.ReadWrite<AttainderLedger>(),
    };

    /// <summary>Pre-add the full transient set, disabled, on a freshly
    /// created unit. Called once by UnitFactory's dispatcher; the entity
    /// then keeps one archetype for life.</summary>
    public static void PreAddUnitSet(EntityManager em, Entity e)
    {
        em.AddComponent(e, new ComponentTypeSet(UnitSet));
        em.AddComponent(e, new ComponentTypeSet(UnitSet2));
        em.AddComponent(e, new ComponentTypeSet(UnitSet3));
        em.AddComponent(e, new ComponentTypeSet(UnitSet4));
        em.AddComponent(e, new ComponentTypeSet(UnitSet5));

        em.SetComponentEnabled<TheWaningBorder.Core.Commands.Types.AttackCommand>(e, false);
        em.SetComponentEnabled<TheWaningBorder.Core.Commands.Types.AttackMoveCommand>(e, false);
        em.SetComponentEnabled<TheWaningBorder.Core.Commands.Types.MoveCommand>(e, false);
        em.SetComponentEnabled<AttackMoveTag>(e, false);
        em.SetComponentEnabled<UserMoveOrder>(e, false);
        em.SetComponentEnabled<ChaseAnchor>(e, false);
        em.SetComponentEnabled<GuardSuppressed>(e, false);
        em.SetComponentEnabled<DeathAnimationState>(e, false);
        em.SetComponentEnabled<SpellBuff>(e, false);
        em.SetComponentEnabled<FormationSlotMemory>(e, false);
        em.SetComponentEnabled<PresentationViewSpawned>(e, false);
        em.SetComponentEnabled<DamageDealtTotal>(e, false);
        em.SetComponentEnabled<LastAttackerEntity>(e, false);
        em.SetComponentEnabled<LastDamagedByFaction>(e, false);
        em.SetComponentEnabled<VeilDebuffTag>(e, false);
        em.SetComponentEnabled<HeroXpAwarded>(e, false);
        em.SetComponentEnabled<TheWaningBorder.Abilities.Charging>(e, false);
        em.SetComponentEnabled<TheWaningBorder.Abilities.ChargeDamageBonus>(e, false);
        em.SetComponentEnabled<TheWaningBorder.Abilities.NextChargePct>(e, false);
        em.SetComponentEnabled<TheWaningBorder.Abilities.TempDisarm>(e, false);
        em.SetComponentEnabled<TheWaningBorder.Abilities.SelfDoT>(e, false);
        em.SetComponentEnabled<TheWaningBorder.Abilities.LifeCling>(e, false);
        em.SetComponentEnabled<TheWaningBorder.Abilities.AbilityAftermath>(e, false);
        em.SetComponentEnabled<TheWaningBorder.Abilities.AbilityCastState>(e, false);
        em.SetComponentEnabled<AbilityActivated>(e, false);
        em.SetComponentEnabled<TheWaningBorder.Abilities.VolleyBuff>(e, false);
        em.SetComponentEnabled<TheWaningBorder.Abilities.NextShotBonus>(e, false);
        em.SetComponentEnabled<TheWaningBorder.Abilities.FirstStrike>(e, false);
        em.SetComponentEnabled<SpellDebuff>(e, false);
        em.SetComponentEnabled<Condemned>(e, false);
        em.SetComponentEnabled<Fortified>(e, false);
        em.SetComponentEnabled<HealOverTime>(e, false);
        em.SetComponentEnabled<SectDisordered>(e, false);
        em.SetComponentEnabled<SectRegenTail>(e, false);
        em.SetComponentEnabled<SectDeathWard>(e, false);
        em.SetComponentEnabled<SectVeiled>(e, false);
        em.SetComponentEnabled<SectCurseWard>(e, false);
        em.SetComponentEnabled<StealthTag>(e, false);
        em.SetComponentEnabled<SectHaste>(e, false);
        em.SetComponentEnabled<SectBlinded>(e, false);
        em.SetComponentEnabled<StealthRevealed>(e, false);
        em.SetComponentEnabled<Invulnerable>(e, false);
        em.SetComponentEnabled<MarkedForSentence>(e, false);
        em.SetComponentEnabled<VenerationFervor>(e, false);
        em.SetComponentEnabled<FormationMemberState>(e, false);
        em.SetComponentEnabled<FormationSpeedOverride>(e, false);
        em.SetComponentEnabled<CommandQueueActive>(e, false);
        em.SetComponentEnabled<QueuedMoveStep>(e, false);
    }

    /// <summary>ECB twin of <see cref="PreAddUnitSet(EntityManager,Entity)"/>
    /// for units created deferred.</summary>
    public static void PreAddUnitSet(EntityCommandBuffer ecb, Entity e)
    {
        ecb.AddComponent(e, new ComponentTypeSet(UnitSet));
        ecb.AddComponent(e, new ComponentTypeSet(UnitSet2));
        ecb.AddComponent(e, new ComponentTypeSet(UnitSet3));
        ecb.AddComponent(e, new ComponentTypeSet(UnitSet4));
        ecb.AddComponent(e, new ComponentTypeSet(UnitSet5));

        ecb.SetComponentEnabled<TheWaningBorder.Core.Commands.Types.AttackCommand>(e, false);
        ecb.SetComponentEnabled<TheWaningBorder.Core.Commands.Types.AttackMoveCommand>(e, false);
        ecb.SetComponentEnabled<TheWaningBorder.Core.Commands.Types.MoveCommand>(e, false);
        ecb.SetComponentEnabled<AttackMoveTag>(e, false);
        ecb.SetComponentEnabled<UserMoveOrder>(e, false);
        ecb.SetComponentEnabled<ChaseAnchor>(e, false);
        ecb.SetComponentEnabled<GuardSuppressed>(e, false);
        ecb.SetComponentEnabled<DeathAnimationState>(e, false);
        ecb.SetComponentEnabled<SpellBuff>(e, false);
        ecb.SetComponentEnabled<FormationSlotMemory>(e, false);
        ecb.SetComponentEnabled<PresentationViewSpawned>(e, false);
        ecb.SetComponentEnabled<DamageDealtTotal>(e, false);
        ecb.SetComponentEnabled<LastAttackerEntity>(e, false);
        ecb.SetComponentEnabled<LastDamagedByFaction>(e, false);
        ecb.SetComponentEnabled<VeilDebuffTag>(e, false);
        ecb.SetComponentEnabled<HeroXpAwarded>(e, false);
        ecb.SetComponentEnabled<TheWaningBorder.Abilities.Charging>(e, false);
        ecb.SetComponentEnabled<TheWaningBorder.Abilities.ChargeDamageBonus>(e, false);
        ecb.SetComponentEnabled<TheWaningBorder.Abilities.NextChargePct>(e, false);
        ecb.SetComponentEnabled<TheWaningBorder.Abilities.TempDisarm>(e, false);
        ecb.SetComponentEnabled<TheWaningBorder.Abilities.SelfDoT>(e, false);
        ecb.SetComponentEnabled<TheWaningBorder.Abilities.LifeCling>(e, false);
        ecb.SetComponentEnabled<TheWaningBorder.Abilities.AbilityAftermath>(e, false);
        ecb.SetComponentEnabled<TheWaningBorder.Abilities.AbilityCastState>(e, false);
        ecb.SetComponentEnabled<AbilityActivated>(e, false);
        ecb.SetComponentEnabled<TheWaningBorder.Abilities.VolleyBuff>(e, false);
        ecb.SetComponentEnabled<TheWaningBorder.Abilities.NextShotBonus>(e, false);
        ecb.SetComponentEnabled<TheWaningBorder.Abilities.FirstStrike>(e, false);
        ecb.SetComponentEnabled<SpellDebuff>(e, false);
        ecb.SetComponentEnabled<Condemned>(e, false);
        ecb.SetComponentEnabled<Fortified>(e, false);
        ecb.SetComponentEnabled<HealOverTime>(e, false);
        ecb.SetComponentEnabled<SectDisordered>(e, false);
        ecb.SetComponentEnabled<SectRegenTail>(e, false);
        ecb.SetComponentEnabled<SectDeathWard>(e, false);
        ecb.SetComponentEnabled<SectVeiled>(e, false);
        ecb.SetComponentEnabled<SectCurseWard>(e, false);
        ecb.SetComponentEnabled<StealthTag>(e, false);
        ecb.SetComponentEnabled<SectHaste>(e, false);
        ecb.SetComponentEnabled<SectBlinded>(e, false);
        ecb.SetComponentEnabled<StealthRevealed>(e, false);
        ecb.SetComponentEnabled<Invulnerable>(e, false);
        ecb.SetComponentEnabled<MarkedForSentence>(e, false);
        ecb.SetComponentEnabled<VenerationFervor>(e, false);
        ecb.SetComponentEnabled<FormationMemberState>(e, false);
        ecb.SetComponentEnabled<FormationSpeedOverride>(e, false);
        ecb.SetComponentEnabled<CommandQueueActive>(e, false);
        ecb.SetComponentEnabled<QueuedMoveStep>(e, false);
    }
}
