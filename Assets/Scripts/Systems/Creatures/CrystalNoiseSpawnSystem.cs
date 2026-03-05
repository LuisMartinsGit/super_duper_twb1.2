// File: Assets/Scripts/Systems/Creatures/CrystalNoiseSpawnSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;

namespace TheWaningBorder.Systems.Creatures
{
    /// <summary>
    /// Monitors mining noise on Crystal Main Nodes and spawns defensive
    /// creatures when the noise threshold is exceeded.
    ///
    /// Mining noise is accumulated by CrystalMiningSystem when players
    /// mine cadavers near a node. When noise crosses the threshold,
    /// 1-2 creatures spawn near the node as a defensive response.
    ///
    /// Cooldown prevents rapid spawning. Max creatures per node
    /// prevents runaway population growth.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CrystalNoiseSpawnSystem : ISystem
    {
        /// <summary>Noise required to trigger creature spawn.</summary>
        private const int NoiseThreshold = 10;

        /// <summary>Minimum seconds between spawns per node.</summary>
        private const float SpawnCooldown = 60f;

        /// <summary>Maximum creatures that can exist near a single node.</summary>
        private const int MaxCreaturesPerNode = 6;

        /// <summary>Range to count existing creatures near a node.</summary>
        private const float CreatureCountRange = 30f;

        /// <summary>Spread distance for spawned creatures around node.</summary>
        private const float SpawnSpread = 8f;

        private uint _randomSeed;
        private float _globalCooldown;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CrystalMainNodeTag>();
            _randomSeed = 99887;
            _globalCooldown = 0f;
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            _randomSeed += 1;
            var random = new Random(_randomSeed);

            // Global cooldown to avoid checking too frequently
            _globalCooldown -= dt;
            if (_globalCooldown > 0f) return;

            var em = state.EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Collect creature positions for proximity counting
            var creaturePositions = new NativeList<float3>(Allocator.Temp);
            foreach (var (creatureTransform, _) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<CreatureTag>>())
            {
                creaturePositions.Add(creatureTransform.ValueRO.Position);
            }

            foreach (var (noise, crystalNode, levelState, transform, entity) in SystemAPI
                .Query<RefRW<CrystalMiningNoise>, RefRO<CrystalNode>, RefRO<CrystalLevelState>, RefRO<LocalTransform>>()
                .WithAll<CrystalMainNodeTag>()
                .WithEntityAccess())
            {
                if (crystalNode.ValueRO.Enabled == 0) continue;

                ref var noiseRef = ref noise.ValueRW;
                if (noiseRef.LocalNoise < NoiseThreshold) continue;

                float3 nodePos = transform.ValueRO.Position;

                // Count existing creatures near this node
                int nearbyCreatures = 0;
                for (int i = 0; i < creaturePositions.Length; i++)
                {
                    if (math.distance(nodePos, creaturePositions[i]) <= CreatureCountRange)
                    {
                        nearbyCreatures++;
                    }
                }

                // Max creatures scales with level
                int maxForNode = MaxCreaturesPerNode + (levelState.ValueRO.Level - 1);
                if (nearbyCreatures >= maxForNode)
                {
                    // Cap reached, just reset noise
                    noiseRef.LocalNoise = 0;
                    continue;
                }

                // Spawn 1-2 creatures
                int toSpawn = random.NextInt(1, 3); // 1 or 2
                toSpawn = math.min(toSpawn, maxForNode - nearbyCreatures);

                for (int i = 0; i < toSpawn; i++)
                {
                    float angle = random.NextFloat(0f, math.PI * 2f);
                    float dist = random.NextFloat(3f, SpawnSpread);
                    float3 spawnPos = nodePos + new float3(
                        math.cos(angle) * dist,
                        0f,
                        math.sin(angle) * dist
                    );
                    spawnPos.y = nodePos.y; // Approximate to node height

                    Creature.Create(ecb, spawnPos);
                }

                // Reset noise and set cooldown
                noiseRef.LocalNoise = 0;
                _globalCooldown = SpawnCooldown;
            }

            creaturePositions.Dispose();
            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
