// WallGatePassabilitySystem.cs
// Manages gate open/close based on friendly unit proximity.
// Gates auto-open for friendly units and close when no friendlies are nearby.
// Location: Assets/Scripts/Systems/Buildings/WallGatePassabilitySystem.cs

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Collections;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Systems.Movement;

namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TheWaningBorder.Systems.Work.PassabilityBuildingSync))]
    public partial struct WallGatePassabilitySystem : ISystem
    {
        private const float PollInterval = 0.3f;
        private const float FriendlyDetectRadius = 3.0f;

        private float _timer;

        public void OnCreate(ref SystemState state)
        {
            _timer = 0f;
        }

        public void OnUpdate(ref SystemState state)
        {
            var grid = PassabilityGrid.Instance;
            if (grid == null) return;

            _timer -= SystemAPI.Time.DeltaTime;
            if (_timer > 0f) return;
            _timer = PollInterval;

            var em = state.EntityManager;
            bool anyChanged = false;

            // Collect all unit positions + factions for proximity checks
            var unitQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>()
            );
            var unitTransforms = unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var unitFactions = unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            // Iterate all gates
            foreach (var (gateState, transform, factionTag, buildingSize, entity) in SystemAPI
                         .Query<RefRW<WallGateState>, RefRO<LocalTransform>, RefRO<FactionTag>, RefRO<BuildingSize>>()
                         .WithAll<WallGateTag>()
                         .WithEntityAccess())
            {
                float3 gatePos = transform.ValueRO.Position;
                Faction gateFaction = factionTag.ValueRO.Value;
                float detectRadiusSq = FriendlyDetectRadius * FriendlyDetectRadius;

                bool friendlyNearby = false;
                for (int i = 0; i < unitTransforms.Length; i++)
                {
                    if (unitFactions[i].Value != gateFaction) continue;

                    float distSq = math.distancesq(
                        new float2(gatePos.x, gatePos.z),
                        new float2(unitTransforms[i].Position.x, unitTransforms[i].Position.z));

                    if (distSq <= detectRadiusSq)
                    {
                        friendlyNearby = true;
                        break;
                    }
                }

                byte wasOpen = gateState.ValueRO.IsOpen;
                byte nowOpen = friendlyNearby ? (byte)1 : (byte)0;

                if (wasOpen != nowOpen)
                {
                    gateState.ValueRW.IsOpen = nowOpen;
                    var size = new int2(buildingSize.ValueRO.Width, buildingSize.ValueRO.Height);

                    if (nowOpen == 1)
                        grid.UnblockBuildingRect(gatePos, size);
                        grid.BlockBuildingRect(gatePos, size);

                    anyChanged = true;
                }
            }

            unitTransforms.Dispose();
            unitFactions.Dispose();

            if (anyChanged)
            {
                var ffm = FlowFieldManager.Instance;
                if (ffm != null) ffm.InvalidateAll();
            }
        }
    }
}
