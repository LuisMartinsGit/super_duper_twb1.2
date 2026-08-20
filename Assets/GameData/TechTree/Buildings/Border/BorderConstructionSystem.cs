// File: Assets/GameData/TechTree/Buildings/Border/BorderConstructionSystem.cs
//
// Border buildings (the Large node) have no builders —
// they self-construct over time. This system advances UnderConstruction.Progress
// on any entity with BorderTag at 1 second per real second, then strips the
// component so PresentationSpawnSystem.SyncTransforms fires the completion
// flourish and pieces snap to rest.

using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct BorderConstructionSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f) return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (uc, entity) in SystemAPI
                .Query<RefRW<UnderConstruction>>()
                .WithAll<BorderTag>()
                .WithEntityAccess())
            {
                ref var u = ref uc.ValueRW;
                u.Progress += dt;
                if (u.Total <= 0f || u.Progress >= u.Total)
                    ecb.RemoveComponent<UnderConstruction>(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
