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
                TWBLog.Log(sb.ToString());
            }

            // Marker assignment is EXACT and exhaustive: pass 1 gives every
            // faction its faction-matched marker; pass 2 hands the remaining
            // (unused) markers to factions without a match, in the registry's
            // deterministic order. The procedural layout only ever applies to
            // factions left over after every marker is spent — so on a marked
            // map, starting positions are exactly the authored marker
            // positions, every run, even when the marker Faction fields don't
            // line up with the active lobby slots.
            var usedMarkers = new System.Collections.Generic.HashSet<PlayerStartMarker>();

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
                    // Pass 1: exact faction match (unused markers only, so a
                    // duplicated Faction field can't double-assign).
                    PlayerStartMarker marker = null;
                    var exact = MapMarkerRegistry.FindPlayerMarker(faction);
                    if (exact != null && !usedMarkers.Contains(exact))
                        marker = exact;

                    // Pass 2: first unused marker in deterministic registry
                    // order — an authored start position always beats the
                    // procedural layout, even with a mismatched Faction field.
                    if (marker == null)
                    {
                        for (int mi = 0; mi < MapMarkerRegistry.PlayerStarts.Count; mi++)
                        {
                            var candidate = MapMarkerRegistry.PlayerStarts[mi];
                            if (candidate == null || usedMarkers.Contains(candidate)) continue;
                            marker = candidate;
                            Debug.LogWarning(
                                $"[PlayerSpawnSystem] no PlayerStartMarker set to {faction} — " +
                                $"using the unclaimed marker '{candidate.gameObject.name}' " +
                                $"(Faction={candidate.Faction}) instead. Set the marker's " +
                                "Faction field to silence this warning.");
                            break;
                        }
                    }

                    if (marker != null)
                    {
                        usedMarkers.Add(marker);
                        var p = marker.WorldPosition;
                        spawnPos = new float3(p.x, p.y, p.z);
                        TWBLog.Log($"[PlayerSpawnSystem] {faction} → marker " +
                                  $"'{marker.gameObject.name}' at " +
                                  $"({spawnPos.x:F2},{spawnPos.z:F2}) — honored exactly");
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[PlayerSpawnSystem] more active factions than " +
                            $"PlayerStartMarkers — {faction} falls back to the " +
                            "procedural layout position. Add a marker for this " +
                            "faction or reduce the player count.");
                    }

                    SpawnFactionBase(em, faction, spawnPos, exactPosition: marker != null);
                    continue;
                }

                SpawnFactionBase(em, faction, spawnPos, exactPosition: false);
            }
        }

        private static void SpawnFactionBase(EntityManager em, Faction faction, float3 position,
            bool exactPosition)
        {
            // Marker-sourced positions are AUTHORED — honor them verbatim in
            // XZ (only the terrain-height snap applies). The silent clamp to
            // playable bounds stays for procedural layouts, but a marker that
            // would be moved gets a loud warning instead of a quiet shift:
            // "the Hall is not on my marker" must never be a mystery again.
            float3 spawnPos;
            if (exactPosition)
            {
                const float EdgeMargin = 4f;
                TerrainUtility.GetPlayableBounds(out var bMin, out var bMax);
                if (position.x < bMin.x + EdgeMargin || position.x > bMax.x - EdgeMargin ||
                    position.z < bMin.y + EdgeMargin || position.z > bMax.y - EdgeMargin)
                {
                    Debug.LogWarning(
                        $"[PlayerSpawnSystem] {faction}'s start marker at " +
                        $"({position.x:F2},{position.z:F2}) sits outside the playable " +
                        $"bounds ({bMin.x:F0}..{bMax.x:F0}, {bMin.y:F0}..{bMax.y:F0}). " +
                        "Honoring it anyway — move the marker if units spawn off-grid.");
                }
                spawnPos = new float3(position.x,
                    TerrainUtility.GetHeight(position.x, position.z), position.z);
            }
            else
            {
                spawnPos = EnsureValidSpawnPosition(position);
            }

            // Spawn Hall (main base) — use BuildingFactory for NetworkedEntity assignment
            BuildingFactory.Create(em, "Hall", spawnPos, faction);

            // Spawn starting Builders just outside the Hall's inflated footprint
            // (Hall is 4x4 cells + 1 cell padding = blocked at +/-3 m, so 6 m of clearance).
            float offset = 6f;
            float3 builderPos1 = EnsureValidSpawnPosition(spawnPos + new float3(offset, 0, 0));
            float3 builderPos2 = EnsureValidSpawnPosition(spawnPos + new float3(-offset, 0, 0));
            float3 builderPos3 = EnsureValidSpawnPosition(spawnPos + new float3(0, 0, offset));

            UnitFactory.Create(em, "Worker", builderPos1, faction);
            UnitFactory.Create(em, "Worker", builderPos2, faction);
            UnitFactory.Create(em, "Worker", builderPos3, faction);

            // Starting army south of the Hall (workers occupy E/W/N): a front
            // row of three Swordsmen, two Archers behind, and a Scout on the
            // eastern flank. No Catapult (§2.5b rev.3): siege in the opening
            // seconds trivialised every early curse anchor.
            const float spacing = 2.5f;
            float3 frontRow = spawnPos + new float3(0, 0, -offset);
            float3 backRow = spawnPos + new float3(0, 0, -offset - spacing);

            UnitFactory.Create(em, "Spearman", EnsureValidSpawnPosition(frontRow + new float3(-spacing, 0, 0)), faction);
            UnitFactory.Create(em, "Spearman", EnsureValidSpawnPosition(frontRow), faction);
            UnitFactory.Create(em, "Spearman", EnsureValidSpawnPosition(frontRow + new float3(spacing, 0, 0)), faction);

            UnitFactory.Create(em, "Archer", EnsureValidSpawnPosition(backRow + new float3(-spacing * 0.5f, 0, 0)), faction);
            UnitFactory.Create(em, "Archer", EnsureValidSpawnPosition(backRow + new float3(spacing * 0.5f, 0, 0)), faction);

            UnitFactory.Create(em, "Scout", EnsureValidSpawnPosition(backRow + new float3(spacing * 2f, 0, 0)), faction);
        }

        /// <summary>
        /// Ensure spawn position is on the terrain and at correct height.
        /// Clamps into the actual Terrain bounds (with a small margin) so no
        /// spawn — layout-derived or marker-derived — can land off the
        /// terrain, outside the nav grid.
        /// </summary>
        private static float3 EnsureValidSpawnPosition(float3 position)
        {
            const float EdgeMargin = 4f;
            TerrainUtility.GetPlayableBounds(out var bMin, out var bMax);
            position.x = math.clamp(position.x, bMin.x + EdgeMargin, bMax.x - EdgeMargin);
            position.z = math.clamp(position.z, bMin.y + EdgeMargin, bMax.y - EdgeMargin);

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

            // The playable rectangle comes from the ACTUAL Terrain object.
            // Unity terrains are corner-anchored (they span transform.position
            // .. position + size), so an origin-centred MapHalfSize box points
            // off the terrain on any hand-authored map — units spawned there
            // sit outside the nav grid and every move command fails.
            TerrainUtility.GetPlayableBounds(out var bMin, out var bMax);
            float2 center = new float2((bMin.x + bMax.x) * 0.5f, (bMin.y + bMax.y) * 0.5f);
            float halfExtent = math.min(bMax.x - bMin.x, bMax.y - bMin.y) * 0.5f;
            float spawnRadius = halfExtent * 0.7f;

            switch (GameSettings.SpawnLayout)
            {
                case SpawnLayout.TwoSides:
                    positions = CalculateTwoSidesPositions(playerCount, center, spawnRadius);
                    break;

                case SpawnLayout.Circle:
                default:
                    for (int i = 0; i < playerCount; i++)
                    {
                        float angle = (i / (float)playerCount) * math.PI * 2f;
                        float x = center.x + math.cos(angle) * spawnRadius;
                        float z = center.y + math.sin(angle) * spawnRadius;
                        float y = TerrainUtility.GetHeight(x, z);
                        positions[i] = new float3(x, y, z);
                    }
                    break;
            }

            return positions;
        }

        private static float3[] CalculateTwoSidesPositions(int playerCount, float2 center, float spawnDist)
        {
            var positions = new float3[playerCount];

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
                    x = center.x - spawnDist;
                    z = center.y + offset;
                }
                else
                {
                    x = center.x + offset;
                    z = center.y - spawnDist;
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
                    x = center.x + spawnDist;
                    z = center.y + offset;
                }
                else
                {
                    x = center.x + offset;
                    z = center.y + spawnDist;
                }

                float y = TerrainUtility.GetHeight(x, z);
                positions[side1Count + i] = new float3(x, y, z);
            }

            return positions;
        }
    }
}