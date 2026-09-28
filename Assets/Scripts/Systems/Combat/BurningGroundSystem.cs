// BurningGroundSystem.cs
// Applies damage-over-time from BurningGround entities to EVERYTHING standing
// in them. Canon: docs/Design/Fire.md §3 ("Fire has no owner").

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Systems.Combat;

/// <summary>
/// An entity is immune to fire (burning ground, a burning building) while it
/// carries this. Fire is otherwise ownerless — it burns its own lighter, allies
/// and enemies alike (docs/Design/Fire.md §3, docs/Design/Teams.md) — so this
/// marker is the ONLY way out, and only a spell, trait or tech that says so in
/// the Design folder may grant it.
/// Timed when a spell grants it (<see cref="Permanent"/> false, removed by
/// <see cref="BurningGroundSystem"/> when <see cref="TimeRemaining"/> runs
/// out); permanent when a trait or tech does.
/// </summary>
public struct FireImmune : IComponentData
{
    public float TimeRemaining;
    public bool Permanent;
}

/// <summary>
/// Queries all BurningGround entities. Every 1 second (throttled):
/// - Finds every unit and building whose BODY overlaps a tile (unit radius /
///   building footprint box, not the centre point) — owner, allies and
///   enemies alike
/// - Deals the strongest overlapping tile's DPS through DamageOverTime
///   (Invulnerable / FireImmune / Liquid Courage / Life Cling honoured)
/// - Decrements TimeRemaining. When &lt;= 0, destroys tile via ECB.
/// Also runs down timed FireImmune grants.
///
/// A tile's FactionTag is OPTIONAL and is attribution only (kill credit when
/// the victim is hostile to it). It never decides who burns — the Firethrower's
/// blood fire carried none and so dealt no damage at all, which is the bug
/// this rule replaced.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct BurningGroundSystem : ISystem
{
    /// <summary>Interval between damage ticks in seconds.</summary>
    private const float DamageTickInterval = 1f;

    // Match-phased (SimCadence.cs), not a bare accumulator: the system
    // object outlives matches, so a bare timer starts the next match at
    // whatever phase the last one ended on -- different on every peer.
    private SimCadence.Periodic _tickTimer;

    public void OnCreate(ref SystemState state)
    {
        // Not gated on BurningGround: timed FireImmune grants must run down
        // even while nothing is burning.
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    public void OnUpdate(ref SystemState state)
    {
        float dt = SystemAPI.Time.DeltaTime;
        bool damageDue = _tickTimer.Due(dt, DamageTickInterval);
        var em = state.EntityManager;

        // Fix #225: use the frame-scoped Singleton ECB so structural changes
        // (DestroyEntity below) play back at EndSimulation in a predictable
        // order, instead of immediately via a local ECB mid-frame.
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // Timed fire immunity runs down; permanent grants never expire.
        foreach (var (immune, entity) in SystemAPI
            .Query<RefRW<FireImmune>>()
            .WithEntityAccess())
        {
            if (immune.ValueRO.Permanent) continue;
            immune.ValueRW.TimeRemaining -= dt;
            if (immune.ValueRO.TimeRemaining <= 0f)
                ecb.RemoveComponent<FireImmune>(entity);
        }

        // Always tick down BurningGround timers
        foreach (var (burning, entity) in SystemAPI
            .Query<RefRW<BurningGround>>()
            .WithEntityAccess())
        {
            burning.ValueRW.TimeRemaining -= dt;

            if (burning.ValueRO.TimeRemaining <= 0f)
            {
                ecb.DestroyEntity(entity);
            }
        }

        // Only apply damage on tick intervals
        if (!damageDue) return;

        // Collect all active burning ground positions and data
        var groundPositions = new NativeList<float3>(Allocator.Temp);
        var groundDPS = new NativeList<float>(Allocator.Temp);
        var groundRadii = new NativeList<float>(Allocator.Temp);
        var groundHasFaction = new NativeList<bool>(Allocator.Temp);
        var groundFactions = new NativeList<Faction>(Allocator.Temp);

        foreach (var (burning, transform, tile) in SystemAPI
            .Query<RefRO<BurningGround>, RefRO<LocalTransform>>()
            .WithEntityAccess())
        {
            if (burning.ValueRO.TimeRemaining <= 0f) continue; // Skip expired tiles

            bool owned = em.HasComponent<FactionTag>(tile);
            groundPositions.Add(transform.ValueRO.Position);
            groundDPS.Add(burning.ValueRO.DPS);
            groundRadii.Add(burning.ValueRO.Radius);
            groundHasFaction.Add(owned);
            groundFactions.Add(owned ? em.GetComponentData<FactionTag>(tile).Value : default);
        }

        if (groundPositions.Length > 0)
        {
            // Everything standing in fire burns — no faction test (Fire.md §3).
            foreach (var (health, xf, entity) in SystemAPI
                .Query<RefRW<Health>, RefRO<LocalTransform>>()
                .WithAny<UnitTag, BuildingTag>()
                .WithNone<Invulnerable>() // (task-062 C-4) — DoT honors LockdownVault
                .WithEntityAccess())
            {
                if (health.ValueRO.Value <= 0) continue; // DeathSystem owns it
                if (TransientState.Active<DeathAnimationState>(em, entity)) continue;
                if (em.HasComponent<BuildingCollapseState>(entity)) continue;
                // The curse's own nodes are crust, not fuel (Fire.md §3/§7).
                if (em.HasComponent<BorderMainNodeTag>(entity)
                    || em.HasComponent<SmallNodeTag>(entity)) continue;

                float2 p = xf.ValueRO.Position.xz;
                bool isBuilding = em.HasComponent<BuildingTag>(entity);
                bool hasBox = isBuilding && em.HasComponent<BuildingSize>(entity);
                float2 half = float2.zero;
                if (hasBox)
                {
                    var size = em.GetComponentData<BuildingSize>(entity);
                    half = new float2(size.Width, size.Height) * 0.5f;
                }
                float bodyRadius = !hasBox && em.HasComponent<Radius>(entity)
                    ? em.GetComponentData<Radius>(entity).Value : 0f;

                // The strongest overlapping tile burns this entity; one tile
                // per tick, never the sum.
                int best = -1;
                for (int i = 0; i < groundPositions.Length; i++)
                {
                    float2 c = groundPositions[i].xz;
                    float r = groundRadii[i];
                    bool touches;
                    if (hasBox)
                    {
                        // Circle vs the footprint's axis-aligned box.
                        float2 nearest = math.clamp(c, p - half, p + half);
                        touches = math.distancesq(nearest, c) <= r * r;
                    }
                    else
                    {
                        float reach = r + bodyRadius;
                        touches = math.distancesq(p, c) <= reach * reach;
                    }
                    if (touches && (best < 0 || groundDPS[i] > groundDPS[best])) best = i;
                }
                if (best < 0) continue;

                int damage = DamageOverTime.ScaleTick(em, entity, true,
                    groundDPS[best] * DamageTickInterval);
                ref var hp = ref health.ValueRW;
                DamageOverTime.Commit(em, entity, ref hp, damage,
                    groundHasFaction[best], groundFactions[best]);
            }
        }

        groundPositions.Dispose();
        groundDPS.Dispose();
        groundRadii.Dispose();
        groundHasFaction.Dispose();
        groundFactions.Dispose();
        // ECB is played back automatically by EndSimulationEntityCommandBufferSystem.
    }
}
