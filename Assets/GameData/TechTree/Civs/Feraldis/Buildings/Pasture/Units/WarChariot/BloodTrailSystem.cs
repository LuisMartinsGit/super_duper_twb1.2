// The War Chariot's blood trail. Canon: docs/Design/Age_1_Feraldis.md.
//
// Paints BloodMap under a moving unit. Gated on DISTANCE TRAVELLED rather
// than time, for two reasons: a parked chariot must not pool blood under
// itself into a free totem site, and a trail laid per-metre reads as a line
// on the ground instead of a string of blobs at whatever the framerate was.
//
// BloodMap is managed main-thread state, so this is a SystemBase.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Influence;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class BloodTrailSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<BloodTrail>();
        }

        protected override void OnUpdate()
        {
            if (!BloodMap.Ready) return;

            foreach (var (trail, transform) in SystemAPI
                .Query<RefRW<BloodTrail>, RefRO<LocalTransform>>()
                .WithNone<DeathAnimationState>())
            {
                ref var t = ref trail.ValueRW;
                var p = transform.ValueRO.Position;

                if (t.HasLast == 0)
                {
                    t.LastPos = p;
                    t.HasLast = 1;
                    continue;
                }

                float dx = p.x - t.LastPos.x;
                float dz = p.z - t.LastPos.z;
                float dist = math.sqrt(dx * dx + dz * dz);
                if (dist < t.MinStep) continue;

                // Amount is per-second of MOVEMENT: a step of MinStep metres
                // at the unit's speed is roughly MinStep/speed seconds, but
                // tying it to distance keeps the trail density constant
                // regardless of speed buffs (blood frenzy, Death Frenzy).
                BloodMap.AddBlood(new UnityEngine.Vector3(p.x, p.y, p.z),
                    t.BloodPerSecond);
                t.LastPos = p;
            }
        }
    }
}
