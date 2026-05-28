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
using TheWaningBorder.World.MapMarkers;

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

            // Hand-authored maps: PlayerStartMarker components in the scene
            // override the radial / two-sides layout for every faction that
            // has a marker. Factions without a marker fall back to their
            // calculated position so a partially-marked map still works.
            bool useMarkers = MapMarkerRegistry.HasPlayerMarkers;
            if (useMarkers)
            {
                var sb = new System.Text.StringBuilder("[PlayerSpawnSystem] markers found: ");
                for (int mi = 0; mi < MapMarkerRegistry.PlayerStarts.Count; mi++)
                {
                    var m = MapMarkerRegistry.PlayerStarts[mi];
                    if (m == null) continue;
                    var p = m.WorldPosition;
                    sb.Append($"{m.Faction}@({p.x:F0},{p.z:F0}) ");
                }
                Debug.Log(sb.ToString());
            }

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
                float3 spawnPos = positions[i];

                if (useMarkers)
                {
                    var marker = MapMarkerRegistry.FindPlayerMarker(faction);
                    if (marker != null)
                    {
                        var p = marker.WorldPosition;
                        spawnPos = new float3(p.x, p.y, p.z);
                        Debug.Log($"[PlayerSpawnSystem] {faction} → marker at " +
                                  $"({spawnPos.x:F0},{spawnPos.z:F0})");
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[PlayerSpawnSystem] no PlayerStartMarker for {faction} — " +
                            "falling back to procedural position. Add a marker for " +
                            "this faction or remove the slot.");
                    }
                }

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
            // Snap to terrain height.
            float y = TerrainUtility.GetHeight(position.x, position.z);
            return new float3(position.x, y, position.z);
        }

        private static float3[] CalculateSpawnPositions(int playerCount)
        {
            // Hand-authored maps use PlayerStartMarkers (applied by the caller);
            // this layout is only the fallback for unmarked factions.
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