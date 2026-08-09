// File: Assets/Scripts/Systems/Creatures/BorderSpreadSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using static TheWaningBorder.Core.Config.BorderConstants;
using static TheWaningBorder.Core.Config.VeilCrustConstants;

namespace TheWaningBorder.Systems.Creatures
{
    /// <summary>
    /// Spreads border ground around Veilstone Main Nodes in expanding rings.
    /// Each tick, the ring frontier (CurrentRingRadius) advances outward,
    /// spawning visible border ground tiles at regular angular intervals.
    /// Uses a fixed ring step, creating a visible wavefront
    /// that players can see approaching their base.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct BorderSpreadSystem : ISystem
    {
        /// <summary>Presentation ID for border ground visual.</summary>
        private const int BorderGroundPresentationId = 311;

        /// <summary>Maximum border ground entities per node to prevent entity bloat.</summary>
        private const int MaxTilesPerNode = 200;

        /// <summary>Base ring expansion step per tick (world units).</summary>
        private const float BaseRingStep = 2f;

        /// <summary>Minimum arc distance between tiles on a ring (world units).</summary>
        private const float TileSpacing = 3.5f;

        /// <summary>Radius of each border ground tile's effect area.</summary>
        private const float TileRadius = 2f;

        /// <summary>Base DPS applied by border ground to non-veilstone units.</summary>
        private const float BaseDPS = 2f;

        // static readonly, NOT const: a const guard makes the rest of OnUpdate
        // provably unreachable, and the resulting CS0162 cannot be
        // pragma-suppressed — Entities source-gen re-emits this method body
        // into a generated file without pragmas.
        private static readonly bool LegacyRingTiles = false;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BorderMainNodeTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // Legacy ring-spawned border-ground tiles are retired in favour of
            // the VeilField crust (terrain-layer overlay). Flip LegacyRingTiles
            // to revive this old model.
            if (!LegacyRingTiles) return;

            float dt = SystemAPI.Time.DeltaTime;

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Count existing border ground per-node by counting all BorderGround entities
            // and dividing by node count for a rough per-node budget
            int existingGroundTotal = 0;
            foreach (var _ in SystemAPI.Query<RefRO<BorderGroundTag>>())
            {
                existingGroundTotal++;
            }

            // Count all spreading nodes (main + resource sub-nodes)
            int nodeCount = 0;
            foreach (var _ in SystemAPI.Query<RefRO<BorderMainNodeTag>>())
            {
                nodeCount++;
            }
            foreach (var (subTag, subNode) in SystemAPI
                .Query<RefRO<BorderSubNodeTag>, RefRO<BorderNode>>())
            {
                if (subTag.ValueRO.Type == BorderSubNodeType.Resource)
                    nodeCount++;
            }

            // === Main Node Spread ===
            // Skip nodes that are still under construction — wait until the
            // staggered rise animation finishes before they begin cursing the
            // ground around them.
            foreach (var (crystalNode, spreadState, nodeLevel, transform, entity) in SystemAPI
                .Query<RefRO<BorderNode>, RefRW<BorderSpreadState>, RefRW<BorderNodeLevel>, RefRO<LocalTransform>>()
                .WithAll<BorderMainNodeTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                if (crystalNode.ValueRO.Enabled == 0) continue;

                ref var spread = ref spreadState.ValueRW;

                // Update node level from current spread radius
                nodeLevel.ValueRW.Value = BorderNodeLevel.FromRadius(spread.CurrentRingRadius);

                // Tick timer (interval from BorderConstants)
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

                // Paint one organic blob at the node center sized to newRadius.
                // Non-uniform domain-warped noise makes the edge irregular (no circle).
                if (TheWaningBorder.World.Terrain.ProceduralTerrain.Instance != null)
                {
                    var p = transform.ValueRO.Position;
                    TheWaningBorder.World.Terrain.ProceduralTerrain.Instance
                        .PaintBorderGround(p.x, p.z, newRadius);
                }

                // Per-node budget check
                int perNodeBudget = MaxTilesPerNode - (existingGroundTotal / math.max(1, nodeCount));
                if (perNodeBudget <= 0) continue;

                int tilesSpawned = SpawnRingTiles(ref ecb, transform.ValueRO.Position,
                    prevRadius, newRadius, perNodeBudget, entity);

                existingGroundTotal += tilesSpawned;
            }

            // === Resource Sub-Node Spread ===
            // Same UnderConstruction skip as the main-node loop above.
            foreach (var (crystalNode, spreadState, transform, subTag, entity) in SystemAPI
                .Query<RefRO<BorderNode>, RefRW<BorderSpreadState>, RefRO<LocalTransform>, RefRO<BorderSubNodeTag>>()
                .WithAll<BorderSubNodeTag>()
                .WithNone<BorderMainNodeTag, UnderConstruction>()
                .WithEntityAccess())
            {
                // Only Resource sub-nodes spread border ground
                if (subTag.ValueRO.Type != BorderSubNodeType.Resource) continue;

                if (crystalNode.ValueRO.Enabled == 0) continue;

                ref var spread = ref spreadState.ValueRW;

                // Tick timer (interval from BorderConstants)
                spread.TickTimer += dt;
                if (spread.TickTimer < ResourceNodeTickInterval) continue;
                spread.TickTimer = 0f;

                // Ring already at max radius -- nothing to spread
                if (spread.CurrentRingRadius >= crystalNode.ValueRO.SpreadRadius) continue;

                float ringStep = BaseRingStep;

                float prevRadius = spread.CurrentRingRadius;
                float newRadius = math.min(prevRadius + ringStep, crystalNode.ValueRO.SpreadRadius);
                spread.CurrentRingRadius = newRadius;

                // Paint sub-node's organic border blob at its current radius
                if (TheWaningBorder.World.Terrain.ProceduralTerrain.Instance != null)
                {
                    var p = transform.ValueRO.Position;
                    TheWaningBorder.World.Terrain.ProceduralTerrain.Instance
                        .PaintBorderGround(p.x, p.z, newRadius);
                }

                int perNodeBudget = MaxTilesPerNode - (existingGroundTotal / math.max(1, nodeCount));
                if (perNodeBudget <= 0) continue;

                // Sub-node entity is the OwnerNode for its border ground tiles
                int tilesSpawned = SpawnRingTiles(ref ecb, transform.ValueRO.Position,
                    prevRadius, newRadius, perNodeBudget, entity);

                existingGroundTotal += tilesSpawned;
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        /// <summary>
        /// Spawns border ground tiles in an annular ring between prevRadius and newRadius.
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

                    // Create border ground entity with full component set
                    var groundEntity = ecb.CreateEntity();
                    ecb.AddComponent<BorderGroundTag>(groundEntity);
                    ecb.AddComponent(groundEntity, LocalTransform.FromPosition(groundPos));
                    ecb.AddComponent(groundEntity, new PresentationId { Id = BorderGroundPresentationId });
                    ecb.AddComponent(groundEntity, new Radius { Value = TileRadius });
                    ecb.AddComponent(groundEntity, new FactionTag { Value = Faction.Border });
                    ecb.AddComponent(groundEntity, new BorderGroundDPS
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
