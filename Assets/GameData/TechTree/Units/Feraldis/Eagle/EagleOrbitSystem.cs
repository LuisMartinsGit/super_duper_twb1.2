// File: Assets/GameData/TechTree/Units/Feraldis/Eagle/EagleOrbitSystem.cs
// Flies each eagle around its scout, and cleans it up when the scout dies.
// Canon: docs/Design/Age_1_Feraldis.md.
//
// The circling is a steady sweep with a breathing radius — enough variation
// that the bird reads as alive and its revealed area keeps changing shape,
// but fully deterministic (angle integrates from sim delta-time, wobble is a
// sine of elapsed time, phases seeded from the owner's entity index). No
// RNG, so every lockstep client flies the same bird.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class EagleOrbitSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<EagleCompanion>();
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            float t = (float)SystemAPI.Time.ElapsedTime;
            var em = EntityManager;

            var orphaned = new NativeList<Entity>(Allocator.Temp);

            foreach (var (eagle, transform, entity) in SystemAPI
                .Query<RefRW<EagleCompanion>, RefRW<LocalTransform>>()
                .WithEntityAccess())
            {
                ref var ec = ref eagle.ValueRW;
                var owner = ec.Owner;

                // The bird outlives nothing: no scout, no eagle.
                if (owner == Entity.Null || !em.Exists(owner)
                    || !em.HasComponent<LocalTransform>(owner)
                    || (em.HasComponent<Health>(owner)
                        && em.GetComponentData<Health>(owner).Value <= 0))
                {
                    orphaned.Add(entity);
                    continue;
                }

                ec.Angle += EagleOrbitSpeed * dt;
                if (ec.Angle > math.PI * 2f) ec.Angle -= math.PI * 2f;

                float radius = EagleOrbitRadius
                    + math.sin(t * EagleWobbleSpeed + ec.WobblePhase) * EagleOrbitWobble;

                float3 c = em.GetComponentData<LocalTransform>(owner).Position;
                float x = c.x + math.cos(ec.Angle) * radius;
                float z = c.z + math.sin(ec.Angle) * radius;
                float groundY = TerrainUtility.IsReady()
                    ? TerrainUtility.GetHeight(x, z)
                    : c.y;

                var xf = transform.ValueRO;
                xf.Position = new float3(x, groundY + EagleHeight, z);
                // Face along the tangent so it banks into the turn.
                xf.Rotation = quaternion.RotateY(-ec.Angle);
                transform.ValueRW = xf;
            }

            for (int i = 0; i < orphaned.Length; i++)
                em.DestroyEntity(orphaned[i]);
            orphaned.Dispose();
        }
    }
}
