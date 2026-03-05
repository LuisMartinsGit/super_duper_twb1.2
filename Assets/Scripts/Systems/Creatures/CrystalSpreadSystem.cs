// File: Assets/Scripts/Systems/Creatures/CrystalSpreadSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Creatures
{
    /// <summary>
    /// Spreads cursed ground around Crystal Main Nodes over time.
    /// Each tick (CrystalNode.TickInterval), spawns CursedGround marker entities
    /// within the node's SpreadRadius. Spread rate scales with crystal level.
    /// CursedGround entities serve as visual indicators and gameplay markers
    /// for the expanding crystal corruption zone.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CrystalSpreadSystem : ISystem
    {
        /// <summary>Presentation ID for cursed ground visual.</summary>
        private const int CursedGroundPresentationId = 311;

        /// <summary>Maximum cursed ground entities per node to prevent entity bloat.</summary>
        private const int MaxCursedGroundPerNode = 30;

        /// <summary>Minimum spacing between cursed ground markers.</summary>
        private const float MinGroundSpacing = 4f;

        private uint _randomSeed;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CrystalMainNodeTag>();
            _randomSeed = 54321;
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            _randomSeed += 1;
            var random = new Random(_randomSeed);

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Count existing cursed ground entities
            int existingGroundCount = 0;
            foreach (var _ in SystemAPI.Query<RefRO<CursedGroundTag>>())
            {
                existingGroundCount++;
            }

            foreach (var (crystalNode, levelState, transform, entity) in SystemAPI
                .Query<RefRW<CrystalNode>, RefRO<CrystalLevelState>, RefRO<LocalTransform>>()
                .WithAll<CrystalMainNodeTag>()
                .WithEntityAccess())
            {
                ref var node = ref crystalNode.ValueRW;
                if (node.Enabled == 0) continue;

                // Tick timer
                node.TickTimer += dt;
                if (node.TickTimer < node.TickInterval) continue;
                node.TickTimer = 0f;

                // Number of ground patches to spawn scales with level
                int level = levelState.ValueRO.Level;
                int patchesToSpawn = (int)node.SpreadPerTick + (level - 1);

                // Cap total cursed ground
                int remaining = MaxCursedGroundPerNode - (existingGroundCount / math.max(1, CountMainNodes(ref state)));
                patchesToSpawn = math.min(patchesToSpawn, math.max(0, remaining));

                float3 nodePos = transform.ValueRO.Position;

                for (int i = 0; i < patchesToSpawn; i++)
                {
                    // Random position within spread radius
                    float angle = random.NextFloat(0f, math.PI * 2f);
                    float dist = random.NextFloat(3f, node.SpreadRadius);
                    float3 groundPos = nodePos + new float3(
                        math.cos(angle) * dist,
                        0f,
                        math.sin(angle) * dist
                    );
                    groundPos.y = nodePos.y; // Approximate height

                    // Create cursed ground entity
                    var groundEntity = ecb.CreateEntity();
                    ecb.AddComponent<CursedGroundTag>(groundEntity);
                    ecb.AddComponent(groundEntity, LocalTransform.FromPosition(groundPos));
                    ecb.AddComponent(groundEntity, new PresentationId { Id = CursedGroundPresentationId });
                    ecb.AddComponent(groundEntity, new Radius { Value = 2f });
                }

                existingGroundCount += patchesToSpawn;
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        /// <summary>
        /// Count active crystal main nodes for per-node ground budget.
        /// </summary>
        private int CountMainNodes(ref SystemState state)
        {
            int count = 0;
            foreach (var _ in SystemAPI.Query<RefRO<CrystalMainNodeTag>>())
            {
                count++;
            }
            return count;
        }
    }
}
