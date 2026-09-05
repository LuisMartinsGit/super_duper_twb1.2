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
    // pass from splitting every unit archetype in two. FirstStrike stays
    // lazy: it is an Alanthor tech grant and Set() converges each granted
    // unit to a stable archetype anyway.
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

    /// <summary>Pre-add the full transient set, disabled, on a freshly
    /// created unit. Called once by UnitFactory's dispatcher; the entity
    /// then keeps one archetype for life.</summary>
    public static void PreAddUnitSet(EntityManager em, Entity e)
    {
        em.AddComponent(e, new ComponentTypeSet(UnitSet));

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
    }

    /// <summary>ECB twin of <see cref="PreAddUnitSet(EntityManager,Entity)"/>
    /// for units created deferred.</summary>
    public static void PreAddUnitSet(EntityCommandBuffer ecb, Entity e)
    {
        ecb.AddComponent(e, new ComponentTypeSet(UnitSet));

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
    }
}
