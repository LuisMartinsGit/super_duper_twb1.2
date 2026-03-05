// File: Assets/Scripts/Systems/Creatures/CrystalSpreadSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Creatures
{
    /// <summary>
    /// Spreads cursed ground around Crystal Main Nodes in expanding rings.
    /// Each tick, the ring frontier (CurrentRingRadius) advances outward,
    /// spawning visible cursed ground tiles at regular angular intervals.
    /// Uses a fixed ring step, creating a visible wavefront
    /// that players can see approaching their base.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CrystalSpreadSystem : ISystem
    {
        /// <summary>Presentation ID for cursed ground visual.</summary>
        private const int CursedGroundPresentationId = 311;

        /// <summary>Maximum cursed ground entities per node to prevent entity bloat.</summary>
        private const int MaxTilesPerNode = 200;

        /// <summary>Base ring expansion step per tick (world units).</summary>
        private const float BaseRingStep = 2f;

        /// <summary>Minimum arc distance between tiles on a ring (world units).</summary>
        private const float TileSpacing = 3.5f;

        /// <summary>Radius of each cursed ground tile's effect area.</summary>
        private const float TileRadius = 2f;

        /// <summary>Base DPS applied by cursed ground to non-crystal units.</summary>
        private const float BaseDPS = 2f;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CrystalMainNodeTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Count existing cursed ground per-node by counting all CursedGround entities
            // and dividing by node count for a rough per-node budget
            int existingGroundTotal = 0;
            foreach (var _ in SystemAPI.Query<RefRO<CursedGroundTag>>())
            {
                existingGroundTotal++;
            }

            int nodeCount = 0;
            foreach (var _ in SystemAPI.Query<RefRO<CrystalMainNodeTag>>())
            {
                nodeCount++;
            }

            foreach (var (crystalNode, transform, entity) in SystemAPI
                .Query<RefRW<CrystalNode>, RefRO<LocalTransform>>()
                .WithAll<CrystalMainNodeTag>()
                .WithEntityAccess())
            {
                ref var node = ref crystalNode.ValueRW;
                if (node.Enabled == 0) continue;

                // Tick timer
                node.TickTimer += dt;
                if (node.TickTimer < node.TickInterval) continue;
                node.TickTimer = 0f;

                // Ring already at max radius -- nothing to spread
                if (node.CurrentRingRadius >= node.SpreadRadius) continue;

                // Fixed ring expansion step
                float ringStep = BaseRingStep;

                // Advance the ring frontier
                float prevRadius = node.CurrentRingRadius;
                float newRadius = math.min(prevRadius + ringStep, node.SpreadRadius);
                node.CurrentRingRadius = newRadius;

                // Per-node budget check
                int perNodeBudget = MaxTilesPerNode - (existingGroundTotal / math.max(1, nodeCount));
                if (perNodeBudget <= 0) continue;

                float3 nodePos = transform.ValueRO.Position;
                int tilesSpawned = 0;

                // Spawn tiles in the annular ring between prevRadius and newRadius
                // Walk from inner to outer edge in radial steps
                float radialStep = TileSpacing * 0.8f; // Slight overlap for coverage
                for (float r = math.max(prevRadius, TileSpacing * 0.5f); r <= newRadius; r += radialStep)
                {
                    // Number of tiles at this radius based on circumference and spacing
                    float circumference = 2f * math.PI * r;
                    int tilesAtRadius = math.max(1, (int)(circumference / TileSpacing));
                    float angleStep = (2f * math.PI) / tilesAtRadius;

                    for (int i = 0; i < tilesAtRadius; i++)
                    {
                        if (tilesSpawned >= perNodeBudget) break;

                        float angle = i * angleStep;
                        float3 groundPos = nodePos + new float3(
                            math.cos(angle) * r,
                            0f,
                            math.sin(angle) * r
                        );
                        groundPos.y = nodePos.y;

                        // Create cursed ground entity with full component set
                        var groundEntity = ecb.CreateEntity();
                        ecb.AddComponent<CursedGroundTag>(groundEntity);
                        ecb.AddComponent(groundEntity, LocalTransform.FromPosition(groundPos));
                        ecb.AddComponent(groundEntity, new PresentationId { Id = CursedGroundPresentationId });
                        ecb.AddComponent(groundEntity, new Radius { Value = TileRadius });
                        ecb.AddComponent(groundEntity, new FactionTag { Value = Faction.White });
                        ecb.AddComponent(groundEntity, new CursedGroundDPS
                        {
                            DamagePerSecond = BaseDPS,
                            EffectRadius = TileRadius
                        });
                        ecb.AddComponent(groundEntity, new OwnerNode { Value = entity });

                        tilesSpawned++;
                    }

                    if (tilesSpawned >= perNodeBudget) break;
                }

                existingGroundTotal += tilesSpawned;
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
