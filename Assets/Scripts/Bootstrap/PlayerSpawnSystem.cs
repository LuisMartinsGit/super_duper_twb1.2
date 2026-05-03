// PlayerSpawnSystem.cs
// Spawns initial units and buildings for each faction at game start
// Location: Assets/Scripts/Bootstrap/PlayerSpawnSystem.cs

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Entities;
using TheWaningBorder.Economy;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    public static class PlayerSpawnSystem
    {
        /// <summary>
        /// Spawn starting bases and units for all active factions.
        /// Call from GameBootstrap after world initialization.
        /// </summary>
        public static void SpawnAllFactions()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return;
            }

            var em = world.EntityManager;

            // Reset network ID generator so all clients assign IDs in the same deterministic order
            NetworkIdGenerator.Reset();

            // Fix #200: clear the FactionEconomy static bank cache so any stale
            // Entity handles from a previous world (e.g., returning to the main
            // menu and starting a new game) don't leak into the fresh world.
            FactionEconomy.ClearCache();

            // Fix #206: also clear the per-helper query caches so stale
            // EntityQuery handles from the previous world are not reused.
            FactionResourcesHelper.ClearCache();
            PopulationHelper.ClearCache();

            int playerCount = GameSettings.TotalPlayers;
            
            // Calculate spawn positions based on layout
            var positions = CalculateSpawnPositions(playerCount);

            for (int i = 0; i < playerCount; i++)
            {
                var slot = LobbyConfig.Slots[i];
                if (slot == null || slot.Type == SlotType.Empty) continue;

                // In observer mode the watcher's slot is SlotType.Observer; we
                // still spawn it so the AI brain (created by AIBootstrap because
                // IsFactionHumanControlled returns false for everyone in
                // observer mode) has a Hall + builders + miners to play with.
                // Skip Observer only when we're NOT in observer mode (that
                // means a real spectator with no faction to play, an edge case
                // we don't currently use but kept here for safety).
                if (slot.Type == SlotType.Observer && !GameSettings.IsObserver) continue;

                var faction = slot.Faction;
                var spawnPos = positions[i];

                SpawnFactionBase(em, faction, spawnPos);
            }
        }

        private static void SpawnFactionBase(EntityManager em, Faction faction, float3 position)
        {
            // Ensure position is on land and at correct height
            float3 spawnPos = EnsureValidSpawnPosition(position);

            // Spawn Hall (main base) — use BuildingFactory for NetworkedEntity assignment
            BuildingFactory.Create(em, "Hall", spawnPos, faction);

            // Spawn starting Builders just outside the Hall's inflated footprint
            // (Hall is 4x4 cells + 1 cell padding = blocked at +/-3 m, so 6 m of clearance).
            float offset = 6f;
            float3 builderPos1 = EnsureValidSpawnPosition(spawnPos + new float3(offset, 0, 0));
            float3 builderPos2 = EnsureValidSpawnPosition(spawnPos + new float3(-offset, 0, 0));
            float3 builderPos3 = EnsureValidSpawnPosition(spawnPos + new float3(0, 0, offset));

            UnitFactory.Create(em, "Builder", builderPos1, faction);
            UnitFactory.Create(em, "Builder", builderPos2, faction);
            UnitFactory.Create(em, "Builder", builderPos3, faction);
        }

        /// <summary>
        /// Ensure spawn position is on land and at correct terrain height.
        /// </summary>
        private static float3 EnsureValidSpawnPosition(float3 position)
        {
            // Get terrain height
            float y = TerrainUtility.GetHeight(position.x, position.z);
            
            // Check if position is on land (using ProceduralTerrain if available)
            var terrain = ProceduralTerrain.Instance;
            if (terrain != null && terrain.IsInWater(new Vector3(position.x, y, position.z)))
            {
                // Try to find nearby land
                var nearestIsland = terrain.GetNearestIsland(new Vector3(position.x, y, position.z));
                if (nearestIsland.HasValue)
                {
                    var island = nearestIsland.Value;
                    Vector2 dir = new Vector2(position.x, position.z) - island.Center;
                    if (dir.magnitude > 0.1f)
                    {
                        dir = dir.normalized;
                        // Move toward island center
                        float safeDist = island.Radius * 0.5f;
                        position.x = island.Center.x + dir.x * safeDist;
                        position.z = island.Center.y + dir.y * safeDist;
                        y = TerrainUtility.GetHeight(position.x, position.z);
                    }
                }
            }

            return new float3(position.x, y, position.z);
        }

        private static float3[] CalculateSpawnPositions(int playerCount)
        {
            // Try to use island-aware spawning from ProceduralTerrain
            var terrain = ProceduralTerrain.Instance;
            if (terrain != null && terrain.Islands.Count > 0)
            {
                var positions3D = terrain.GetMultiplayerSpawnPositions(playerCount);
                var result = new float3[playerCount];
                for (int i = 0; i < playerCount; i++)
                {
                    result[i] = new float3(positions3D[i].x, positions3D[i].y, positions3D[i].z);
                }
                return result;
            }

            // Fallback to layout-based spawning
            return CalculateLayoutSpawnPositions(playerCount);
        }

        private static float3[] CalculateLayoutSpawnPositions(int playerCount)
        {
            var positions = new float3[playerCount];
            int half = GameSettings.MapHalfSize;
            float spawnRadius = half * 0.7f;

            switch (GameSettings.SpawnLayout)
            {
                case SpawnLayout.TwoSides:
                    positions = CalculateTwoSidesPositions(playerCount, half);
                    break;

                case SpawnLayout.Circle:
                default:
                    for (int i = 0; i < playerCount; i++)
                    {
                        float angle = (i / (float)playerCount) * math.PI * 2f;
                        float x = math.cos(angle) * spawnRadius;
                        float z = math.sin(angle) * spawnRadius;
                        float y = TerrainUtility.GetHeight(x, z);
                        positions[i] = new float3(x, y, z);
                    }
                    break;
            }

            return positions;
        }

        private static float3[] CalculateTwoSidesPositions(int playerCount, int mapHalf)
        {
            var positions = new float3[playerCount];
            float spawnDist = mapHalf * 0.7f;
            
            int side1Count = (playerCount + 1) / 2;
            int side2Count = playerCount - side1Count;

            bool leftRight = GameSettings.TwoSides == TwoSidesPreset.LeftRight;

            // Side 1
            for (int i = 0; i < side1Count; i++)
            {
                float offset = (i - (side1Count - 1) * 0.5f) * 20f;
                float x, z;
                
                if (leftRight)
                {
                    x = -spawnDist;
                    z = offset;
                }
                else
                {
                    x = offset;
                    z = -spawnDist;
                }
                
                float y = TerrainUtility.GetHeight(x, z);
                positions[i] = new float3(x, y, z);
            }

            // Side 2
            for (int i = 0; i < side2Count; i++)
            {
                float offset = (i - (side2Count - 1) * 0.5f) * 20f;
                float x, z;
                
                if (leftRight)
                {
                    x = spawnDist;
                    z = offset;
                }
                else
                {
                    x = offset;
                    z = spawnDist;
                }
                
                float y = TerrainUtility.GetHeight(x, z);
                positions[side1Count + i] = new float3(x, y, z);
            }

            return positions;
        }
    }
}