// BuildingExitSystem.cs
// A TRAINED UNIT WALKS OUT OF ITS BUILDING (2026-11-12).
//
// A building whose prefab carries the Unit_spawn and Exit_point markers
// (Shared/Buildings/BuildingAnchors.cs) releases its units INSIDE itself:
// TrainingSystem.SpawnUnit puts the unit on Unit_spawn and stamps
// ExitingBuilding. Unit_spawn lies inside the building's blocked footprint,
// where no flow field leads and the integrator's stuck recovery would snap
// the unit out, so for the length of the walk THIS system owns the unit's
// transform — a straight line to Exit_point at the unit's own speed — and
// UnitIntegratorSystem leaves it alone (its query excludes ExitingBuilding,
// as it excludes Launched). At the door the component is removed and the
// unit takes its muster order (the rally point, or a few metres past the
// door) like any other.
//
// DesiredDestination is pointed at the door for the walk: it is what the
// walk animation reads (UnitAnimationSync IsMoving). An order given mid-walk
// overwrites it; the unit still finishes the few metres to the door — it
// cannot path from inside the footprint — and then follows that order
// instead of mustering.
//
// Lockstep: main thread, fixed step, math.* only; the removal goes through
// the end-of-simulation command buffer.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Systems.Navigation;

/// <summary>A freshly trained unit on its way from Unit_spawn to Exit_point.</summary>
public struct ExitingBuilding : IComponentData
{
    /// <summary>The building it is leaving (units released together queue up).</summary>
    public Entity Building;
    /// <summary>Exit_point, world.</summary>
    public float3 Exit;
    /// <summary>Where it goes once outside.</summary>
    public float3 Muster;
    /// <summary>Seconds it waits on Unit_spawn before setting off.</summary>
    public float Delay;
    /// <summary>1 once it has set off.</summary>
    public byte Started;
}

namespace TheWaningBorder.Systems.Training
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(UnitIntegratorSystem))]
    public partial struct BuildingExitSystem : ISystem
    {
        /// <summary>Speed of a unit with no MoveSpeed (the integrator's default).</summary>
        private const float FallbackSpeed = 3.5f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<ExitingBuilding>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (exitRW, xfRW, ddRW, entity) in SystemAPI
                         .Query<RefRW<ExitingBuilding>, RefRW<LocalTransform>, RefRW<DesiredDestination>>()
                         .WithNone<DeathAnimationState>()
                         .WithEntityAccess())
            {
                ref var exit = ref exitRW.ValueRW;
                if (exit.Delay > 0f)
                {
                    exit.Delay -= dt;
                    continue;
                }
                if (exit.Started == 0)
                {
                    exit.Started = 1;
                    ddRW.ValueRW = new DesiredDestination { Position = exit.Exit, Has = 1 };
                }

                float speed = FallbackSpeed;
                if (SystemAPI.HasComponent<MoveSpeed>(entity))
                {
                    float ms = SystemAPI.GetComponent<MoveSpeed>(entity).Value;
                    if (ms > 0f) speed = ms;
                }

                ref var xf = ref xfRW.ValueRW;
                float3 to = exit.Exit - xf.Position;
                to.y = 0f;
                float dist = math.length(to);
                float step = speed * dt;

                if (dist > step)
                {
                    float3 dir = to / dist;
                    xf.Position += dir * step;
                    xf.Rotation = quaternion.RotateY(math.atan2(dir.x, dir.z));
                    continue;
                }

                // At the door.
                xf.Position = new float3(exit.Exit.x, xf.Position.y, exit.Exit.z);
                ecb.RemoveComponent<ExitingBuilding>(entity);
                // Nobody re-ordered it on the way out: muster.
                var dd = ddRW.ValueRO;
                if (dd.Has != 0 && dd.Position.Equals(exit.Exit))
                    ddRW.ValueRW = new DesiredDestination { Position = exit.Muster, Has = 1 };
            }
        }
    }
}
