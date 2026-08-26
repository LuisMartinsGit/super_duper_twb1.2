// Monitors veilstone faction extinction. When all main nodes are destroyed,
// starts a 5-minute timer and respawns a new main node at a random location.

using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Terrain;
using Cost = TheWaningBorder.Core.Cost;

namespace TheWaningBorder.Systems.Border
{
    /// <summary>
    /// Monitors veilstone faction extinction. When all BorderMainNodeTag entities
    /// are destroyed, starts a 5-minute respawn timer. On expiry, spawns a new
    /// main node at a random valid location (same rules as initial bootstrap).
    ///
    /// Uses SystemBase because BorderMainNode.Create() performs structural changes.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class BorderExtinctionSystem : SystemBase
    {
        private const float RespawnDelay = 180f; // 3 minutes
        private const float MinDistFromPlayers = 60f;

        // Cached EntityQueries — initialized in OnCreate()
        private EntityQuery _extinctionQuery;
        private EntityQuery _nodeCountQuery;
        private EntityQuery _hallQuery;

        protected override void OnCreate()
        {
            RequireForUpdate<BorderExtinctionState>();

            _extinctionQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<BorderExtinctionState>()
            );

            _nodeCountQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<BorderMainNodeTag>()
            );

            _hallQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;

            // Get extinction state singleton
            if (_extinctionQuery.IsEmpty) return;

            using var extinctionEntities = _extinctionQuery.ToEntityArray(Allocator.Temp);
            var ext = em.GetComponentData<BorderExtinctionState>(extinctionEntities[0]);

            // Count remaining main nodes
            int nodeCount = _nodeCountQuery.CalculateEntityCount();

            if (nodeCount > 0)
            {
                // Border faction is alive
                ext.IsExtinct = 0;
                ext.HasEverExisted = 1;
                em.SetComponentData(extinctionEntities[0], ext);
                return;
            }

            // No nodes exist
            if (ext.HasEverExisted == 0) return; // Never spawned, nothing to respawn

            float dt = World.Time.DeltaTime;

            if (ext.IsExtinct == 0)
            {
                // Just went extinct — start timer
                ext.IsExtinct = 1;
                ext.RespawnTimer = RespawnDelay;
            }
            else
            {
                ext.RespawnTimer -= dt;
                if (ext.RespawnTimer <= 0f)
                {
                    // Respawn a new main node
                    TryRespawn(em);
                    ext.IsExtinct = 0;
                    ext.RespawnTimer = 0f;
                }
            }

            em.SetComponentData(extinctionEntities[0], ext);
        }

        private void TryRespawn(EntityManager em)
        {
            // Deterministic random seed for multiplayer. Earlier missing
            // braces meant the World.Time.ElapsedTime branch always ran
            // and clobbered the lockstep-tick seed, so host and client
            // computed different respawn positions and desynced state at
            // the moment Veilstone extinction triggered. (task-058 F-2 / MB-21)
            uint seed;
            if (GameSettings.IsMultiplayer && LockstepServiceLocator.IsActive)
                seed = (uint)(LockstepServiceLocator.Instance.CurrentTick * 4217 + GameSettings.SpawnSeed + 99);
            else
                seed = (uint)(World.Time.ElapsedTime * 1000 + GameSettings.SpawnSeed + 99);
            var random = new Unity.Mathematics.Random(math.max(1, seed));

            // Get player positions
            using var hallFactions = _hallQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hallTransforms = _hallQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var playerPositions = new NativeList<float3>(Allocator.Temp);
            for (int i = 0; i < hallFactions.Length; i++)
            {
                if (hallFactions[i].Value != Faction.Border)
                    playerPositions.Add(hallTransforms[i].Position);
            }

            // Ring around the ACTUAL terrain centre — Unity terrains are
            // corner-anchored, so an origin-centred ring lands off the
            // terrain on hand-authored maps.
            TerrainUtility.GetPlayableBounds(out var bMin, out var bMax);
            float2 mapCenter = new float2((bMin.x + bMax.x) * 0.5f, (bMin.y + bMax.y) * 0.5f);
            float halfExtent = math.min(bMax.x - bMin.x, bMax.y - bMin.y) * 0.5f;
            float spawnRadius = halfExtent * 0.7f;
            var grid = PassabilityGrid.Instance;

            for (int attempt = 0; attempt < 30; attempt++)
            {
                float angle = random.NextFloat(0, math.PI * 2f);
                float dist = random.NextFloat(math.min(20f, spawnRadius), spawnRadius);
                float3 candidate = new float3(
                    mapCenter.x + math.cos(angle) * dist, 0,
                    mapCenter.y + math.sin(angle) * dist);

                candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);

                if (grid != null && !grid.IsPassable(candidate))
                    continue;

                // Min distance from players
                bool tooCloseToPlayer = false;
                for (int p = 0; p < playerPositions.Length; p++)
                {
                    if (math.distance(candidate, playerPositions[p]) < MinDistFromPlayers)
                    {
                        tooCloseToPlayer = true;
                        break;
                    }
                }
                if (tooCloseToPlayer) continue;

                // Valid position — spawn
                BorderMainNode.Create(em, candidate);

                // Give small veilstone bank boost
                FactionEconomy.Add(em, Faction.Border, Cost.Of(veilstone: 100));

                playerPositions.Dispose();
                return;
            }

            // Failed to find valid position — OnUpdate will retry next frame
            // since RespawnTimer is still <= 0
            playerPositions.Dispose();
        }
    }
}
