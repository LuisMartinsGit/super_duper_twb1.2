// PatrolThreatDetectionSystem.cs
// Spec §7.4 (Sins of a Solar Empire 2 TEC borrow): when enemy units engage
// within range of a trade lane, nearby caravans become player-controllable.
// Returns to autonomous when combat ends.
//
// Implementation (refinement #3 — keyed on CaravanTag since the separate
// patrol entity type was removed):
//   - Every caravan carries PatrolAlertState (added lazily).
//   - Each tick we scan for hostile units within PatrolThreatRange of each
//     caravan. If any present: alert flag flips to 1 and NotControllableTag
//     is removed (RTSInputManager.IsBlockedByNotControllable then lets the
//     player issue commands).
//   - If no hostile in range, PeacefulSeconds counts up. After
//     PatrolAlertTimeout seconds, alert flag clears and NotControllableTag
//     is restored — the caravan resumes autonomous route behavior.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PatrolThreatDetectionSystem : SystemBase
    {
        private EntityQuery _hostileQuery;
        private EntityQuery _patrolWithoutStateQuery;

        protected override void OnCreate()
        {
            // Hostiles = any unit with FactionTag + LocalTransform + Health.
            // Filtering by faction happens per-patrol (own faction excluded).
            _hostileQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());

            _patrolWithoutStateQuery = EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<CaravanTag>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<FactionTag>(),
                },
                None = new[] { ComponentType.ReadOnly<PatrolAlertState>() },
            });
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = (float)SystemAPI.Time.DeltaTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // ── Step 0: lazy-add PatrolAlertState to any new patrols ──
            if (!_patrolWithoutStateQuery.IsEmpty)
            {
                using var ents = _patrolWithoutStateQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    ecb.AddComponent(ents[i], new PatrolAlertState
                    {
                        IsAlert = 0,
                        PeacefulSeconds = PatrolAlertTimeout, // start "fully peaceful"
                    });
                }
            }

            // ── Step 1: snapshot hostiles ──
            // Filtering by patrol's own faction is per-iteration; the snapshot
            // includes all factions so each patrol picks the foreign ones.
            using var hostileEnts = _hostileQuery.ToEntityArray(Allocator.Temp);
            using var hostileFactions = _hostileQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hostileTransforms = _hostileQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hostileHealths = _hostileQuery.ToComponentDataArray<Health>(Allocator.Temp);

            // ── Step 2: tick each caravan ──
            foreach (var (alertRW, transform, faction, entity) in SystemAPI
                .Query<RefRW<PatrolAlertState>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<CaravanTag>()
                .WithEntityAccess())
            {
                ref var alert = ref alertRW.ValueRW;
                Faction myFaction = faction.ValueRO.Value;
                float3 myPos = transform.ValueRO.Position;

                bool hostileNearby = false;
                for (int h = 0; h < hostileEnts.Length; h++)
                {
                    if (hostileFactions[h].Value == myFaction) continue;
                    if (hostileFactions[h].Value == Faction.White) continue;  // observer
                    if (hostileHealths[h].Value <= 0) continue;

                    var hp = hostileTransforms[h].Position;
                    float dxz = math.distance(
                        new float2(myPos.x, myPos.z),
                        new float2(hp.x, hp.z));
                    if (dxz <= PatrolThreatRange)
                    {
                        hostileNearby = true;
                        break;
                    }
                }

                if (hostileNearby)
                {
                    alert.PeacefulSeconds = 0f;
                    if (alert.IsAlert == 0)
                    {
                        alert.IsAlert = 1;
                        if (em.HasComponent<NotControllableTag>(entity))
                            ecb.RemoveComponent<NotControllableTag>(entity);
                    }
                }
                else
                {
                    alert.PeacefulSeconds += dt;
                    if (alert.IsAlert == 1 && alert.PeacefulSeconds >= PatrolAlertTimeout)
                    {
                        alert.IsAlert = 0;
                        if (!em.HasComponent<NotControllableTag>(entity))
                            ecb.AddComponent<NotControllableTag>(entity);
                    }
                }
            }

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
