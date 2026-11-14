// FormationArriveFacingSystem.cs
// Turns a unit to its formation's facing once it has stopped on its slot
// (FormationArriveFacing, set by FormationMoveCommandHelper.IssuePlanOrders).
//
// Only a unit that is standing still, has no target and stands on the slot the
// order named is turned. One that stopped anywhere else (a new order, a fight
// that carried it off, a blocked slot) drops the request: it was about THAT
// slot. Main thread, plain maths: the same on every lockstep peer.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Navigation
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(UnitIntegratorSystem))]
    public partial struct FormationArriveFacingSystem : ISystem
    {
        /// <summary>The integrator's own turn rate (rad/s).</summary>
        private const float TurnSpeed = 6f;
        /// <summary>How far from the slot still counts as standing on it (m):
        /// the plain stop window, with a little slack.</summary>
        private const float OnSlotDistance = 0.35f;
        private const float DoneRadians = 0.01f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<FormationArriveFacing>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f) return;
            float maxStep = TurnSpeed * dt;
            var targets = SystemAPI.GetComponentLookup<Target>(true);

            foreach (var (face, xf, dest, enabled, entity) in SystemAPI
                .Query<RefRO<FormationArriveFacing>, RefRW<LocalTransform>, RefRO<DesiredDestination>,
                       EnabledRefRW<FormationArriveFacing>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                if (dest.ValueRO.Has != 0) continue;   // still walking

                float3 pos = xf.ValueRO.Position;
                float2 off = pos.xz - face.ValueRO.Slot.xz;
                if (math.lengthsq(off) > OnSlotDistance * OnSlotDistance
                    || math.lengthsq(face.ValueRO.Dir) < 1e-6f)
                {
                    enabled.ValueRW = false;           // stopped somewhere else
                    continue;
                }
                // Fighting from the slot: the fight owns the heading.
                if (targets.HasComponent(entity) && targets[entity].Value != Entity.Null)
                    continue;

                float3 fwd = math.mul(xf.ValueRO.Rotation, new float3(0f, 0f, 1f));
                float have = math.atan2(fwd.x, fwd.z);
                float want = math.atan2(face.ValueRO.Dir.x, face.ValueRO.Dir.y);
                float err = want - have;
                err -= math.round(err / (2f * math.PI)) * (2f * math.PI);   // shortest way round
                if (math.abs(err) <= DoneRadians)
                {
                    xf.ValueRW.Rotation = quaternion.RotateY(want);
                    enabled.ValueRW = false;
                    continue;
                }
                xf.ValueRW.Rotation = quaternion.RotateY(have + math.clamp(err, -maxStep, maxStep));
            }
        }
    }
}
