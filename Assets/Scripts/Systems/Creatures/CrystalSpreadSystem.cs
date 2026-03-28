// File: Assets/Scripts/Systems/Creatures/CrystalSpreadSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static TheWaningBorder.Core.Config.CrystalConstants;

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

            // Count all spreading nodes (main + resource sub-nodes)
            int nodeCount = 0;
            foreach (var _ in SystemAPI.Query<RefRO<CrystalMainNodeTag>>())
            {
                nodeCount++;
            }
            foreach (var (subTag, subNode) in SystemAPI
                .Query<RefRO<CrystalSubNodeTag>, RefRO<CrystalNode>>())
            {
                if (subTag.ValueRO.Type == CrystalSubNodeType.Resource)
                    nodeCount++;
            }

            // === Main Node Spread ===
            foreach (var (crystalNode, spreadState, nodeLevel, transform, entity) in SystemAPI
                .Query<RefRO<CrystalNode>, RefRW<CrystalSpreadState>, RefRW<CrystalNodeLevel>, RefRO<LocalTransform>>()
                .WithAll<CrystalMainNodeTag>()
                .WithEntityAccess())
            {
                if (crystalNode.ValueRO.Enabled == 0) continue;

                ref var spread = ref spreadState.ValueRW;

                // Update node level from current spread radius
                nodeLevel.ValueRW.Value = CrystalNodeLevel.FromRadius(spread.CurrentRingRadius);

                // Tick timer (interval from CrystalConstants)
                spread.TickTimer += dt;
                if (spread.TickTimer < MainNodeTickInterval) continue;
                spread.TickTimer = 0f;

                // Ring already at max radius -- nothing to spread
                if (spread.CurrentRingRadius >= crystalNode.ValueRO.SpreadRadius) continue;

                // Level-based ring step: fast early, slow late
                int level = nodeLevel.ValueRW.Value;
                float ringStep = level == 1 ? 3.0f : level == 2 ? 2.0f : 1.0f;

                // Advance the ring frontier
                float prevRadius = spread.CurrentRingRadius;
                float newRadius = math.min(prevRadius + ringStep, crystalNode.ValueRO.SpreadRadius);
                spread.CurrentRingRadius = newRadius;

                // Per-node budget check
                int perNodeBudget = MaxTilesPerNode - (existingGroundTotal / math.max(1, nodeCount));
                if (perNodeBudget <= 0) continue;

                int tilesSpawned = SpawnRingTiles(ref ecb, transform.ValueRO.Position,
                    prevRadius, newRadius, perNodeBudget, entity);

                existingGroundTotal += tilesSpawned;
            }

            // === Resource Sub-Node Spread ===
            foreach (var (crystalNode, spreadState, transform, subTag, entity) in SystemAPI
                .Query<RefRO<CrystalNode>, RefRW<CrystalSpreadState>, RefRO<LocalTransform>, RefRO<CrystalSubNodeTag>>()
                .WithAll<CrystalSubNodeTag>()
                .WithNone<CrystalMainNodeTag>()
                .WithEntityAccess())
            {
                // Only Resource sub-nodes spread cursed ground
                if (subTag.ValueRO.Type != CrystalSubNodeType.Resource) continue;

                if (crystalNode.ValueRO.Enabled == 0) continue;

                ref var spread = ref spreadState.ValueRW;

                // Tick timer (interval from CrystalConstants)
                spread.TickTimer += dt;
                if (spread.TickTimer < ResourceNodeTickInterval) continue;
                spread.TickTimer = 0f;

                // Ring already at max radius -- nothing to spread
                if (spread.CurrentRingRadius >= crystalNode.ValueRO.SpreadRadius) continue;

                float ringStep = BaseRingStep;

                float prevRadius = spread.CurrentRingRadius;
                float newRadius = math.min(prevRadius + ringStep, crystalNode.ValueRO.SpreadRadius);
                spread.CurrentRingRadius = newRadius;

                int perNodeBudget = MaxTilesPerNode - (existingGroundTotal / math.max(1, nodeCount));
                if (perNodeBudget <= 0) continue;

                // Sub-node entity is the OwnerNode for its cursed ground tiles
                int tilesSpawned = SpawnRingTiles(ref ecb, transform.ValueRO.Position,
                    prevRadius, newRadius, perNodeBudget, entity);

                existingGroundTotal += tilesSpawned;
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        /// <summary>
        /// Spawns cursed ground tiles in an annular ring between prevRadius and newRadius.
        /// Returns the number of tiles spawned.
        /// </summary>
        private static int SpawnRingTiles(ref EntityCommandBuffer ecb, float3 nodePos,
            float prevRadius, float newRadius, int budget, Entity ownerEntity)
        {
            int tilesSpawned = 0;

            float radialStep = TileSpacing * 0.8f; // Slight overlap for coverage
            for (float r = math.max(prevRadius, TileSpacing * 0.5f); r <= newRadius; r += radialStep)
            {
                // Number of tiles at this radius based on circumference and spacing
                float circumference = 2f * math.PI * r;
                int tilesAtRadius = math.max(1, (int)(circumference / TileSpacing));
                float angleStep = (2f * math.PI) / tilesAtRadius;

                for (int i = 0; i < tilesAtRadius; i++)
                {
                    if (tilesSpawned >= budget) break;

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
                    ecb.AddComponent(groundEntity, new OwnerNode { Value = ownerEntity });

                    tilesSpawned++;
                }

                if (tilesSpawned >= budget) break;
            }

            return tilesSpawned;
        }
    }
}
