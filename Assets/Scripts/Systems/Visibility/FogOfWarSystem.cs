// File: Assets/Scripts/Systems/Visibility/FogOfWarSystem.cs
using Unity.Entities;
using Unity.Collections;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.World.FogOfWar;
using TheWaningBorder.Presentation;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Visibility
{
    /// <summary>
    /// ECS system that updates fog of war visibility each frame.
    /// 
    /// Works with FogOfWarManager to:
    /// 1. Clear current visibility each frame
    /// 2. Stamp visibility circles for all units with LineOfSight
    /// 3. Mark revealed cells as permanently explored
    /// 4. Update the human player's fog texture
    /// 
    /// Visibility states:
    /// - Hidden: Never seen (dark fog)
    /// - Revealed: Previously seen but not currently visible (lighter fog, buildings show as ghosts)
    /// - Visible: Currently within line of sight (clear, full visibility)
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FogOfWarSystem : SystemBase
    {
        private static bool s_logged;

        protected override void OnUpdate()
        {
            var mgr = FogOfWarManager.Instance;
            if (mgr == null) return;

            // Begin new frame - clears current visibility
            mgr.BeginFrame();

            // Query all entities with LineOfSight and position.
            // Exclude BorderTag: veilstone entities are enemy to all players
            // and should NOT reveal fog. GetEntityQuery caches per system —
            // CreateEntityQuery per frame leaks into the world's registry.
            var query = GetEntityQuery(
                ComponentType.ReadOnly<LineOfSight>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.Exclude<BorderTag>());

            var lineOfSights = query.ToComponentDataArray<LineOfSight>(Allocator.Temp);
            var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < lineOfSights.Length; i++)
            {
                // Ensure valid radius
                float radius = Mathf.Max(0.01f, lineOfSights[i].Radius);

                // task-063 phase 1: sect FogVisionBonus removed with the
                // FactionSectState bridge. Phase 2 reintroduces vision-related sect
                // levers (e.g. Witness — All-Seeing).

                // Stamp visibility circle for this unit's faction
                mgr.Stamp(factions[i].Value, (Vector3)transforms[i].Position, radius);
            }

            lineOfSights.Dispose();
            transforms.Dispose();
            factions.Dispose();

            // Finalize frame - rebuilds the overlay texture (throttled inside)
            mgr.EndFrameAndBuild();
        }

        // ==================== Static Query Methods ====================

        /// <summary>
        /// Check if a position is currently visible to a faction.
        /// Returns true if the position is within any of the faction's units' line of sight.
        /// </summary>
        public static bool IsVisibleToFaction(Faction faction, float3 position)
        {
            if (FogOfWarManager.Instance == null) return true; // Fallback: everything visible
            return FogOfWarManager.Instance.IsVisible(faction, new Vector3(position.x, 0, position.z));
        }

        /// <summary>
        /// Check if a position has been revealed (explored) by a faction.
        /// Returns true if the position was ever within the faction's line of sight.
        /// </summary>
        public static bool IsRevealedToFaction(Faction faction, float3 position)
        {
            if (FogOfWarManager.Instance == null) return true; // Fallback: everything revealed
            return FogOfWarManager.Instance.IsRevealed(faction, new Vector3(position.x, 0, position.z));
        }
    }

    /// <summary>
    /// Syncs GameObject visibility with fog of war state.
    /// 
    /// Visibility rules:
    /// - Player-owned entities: Always visible
    /// - Enemy units: Only visible when in current line of sight
    /// - Enemy buildings: Visible when in LoS, ghost when only revealed
    /// 
    /// Works with EntityViewManager to show/hide presentation GameObjects.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FogOfWarSystem))]
    public partial class FogVisibilitySyncSystem : SystemBase
    {
        // Show/hide runs at 10 Hz — SetActive flips don't need frame-exact
        // timing, and the full sweep over every presented entity is too
        // heavy to pay per frame.
        private const float SyncInterval = 0.1f;
        private float _nextSync;

        protected override void OnUpdate()
        {
            if (UnityEngine.Time.unscaledTime < _nextSync) return;
            _nextSync = UnityEngine.Time.unscaledTime + SyncInterval;

            var mgr = FogOfWarManager.Instance;
            var entityViewManager = EntityViewManager.Instance;
            if (entityViewManager == null) return;

            var em = EntityManager;

            // When fog of war is disabled — or the local player is an
            // OBSERVER — make all entities visible. In observer matches the
            // fog manager still exists so the AIs' intel stays fog-honest
            // (per-faction grids), but the observer's VIEW is unfogged.
            if (mgr == null || GameSettings.IsObserver)
            {
                var allQuery = GetEntityQuery(
                    ComponentType.ReadOnly<PresentationId>(),
                    ComponentType.ReadOnly<LocalTransform>());
                var allEntities = allQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < allEntities.Length; i++)
                {
                    if (entityViewManager.TryGetView(allEntities[i], out var go) && go != null)
                        go.SetActive(true);
                }
                allEntities.Dispose();
                return;
            }

            var humanFaction = mgr.HumanFaction;

            // Cache player unit positions + LOS for direct distance fallback
            var playerLosQuery = GetEntityQuery(
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LineOfSight>());
            var pAllTransforms = playerLosQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var pAllFactions = playerLosQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var pAllLOS = playerLosQuery.ToComponentDataArray<LineOfSight>(Allocator.Temp);

            // Build compact arrays of only player units
            int playerCount = 0;
            for (int pi = 0; pi < pAllFactions.Length; pi++)
                if (pAllFactions[pi].Value == humanFaction) playerCount++;

            var playerPositions = new NativeArray<float3>(playerCount, Allocator.Temp);
            var playerLOSRadii = new NativeArray<float>(playerCount, Allocator.Temp);
            int idx = 0;
            for (int pi = 0; pi < pAllFactions.Length; pi++)
            {
                if (pAllFactions[pi].Value == humanFaction)
                {
                    playerPositions[idx] = pAllTransforms[pi].Position;
                    playerLOSRadii[idx] = pAllLOS[pi].Radius;
                    idx++;
                }
            }
            pAllTransforms.Dispose();
            pAllFactions.Dispose();
            pAllLOS.Dispose();

            // Query entities with presentation
            var query = GetEntityQuery(
                ComponentType.ReadOnly<PresentationId>(),
                ComponentType.ReadOnly<LocalTransform>());

            var entities = query.ToEntityArray(Allocator.Temp);
            var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var position = transforms[i].Position;

                if (!entityViewManager.TryGetView(entity, out var gameObject) || gameObject == null) 
                    continue;

                bool isBuilding = em.HasComponent<BuildingTag>(entity);
                bool isUnit = em.HasComponent<UnitTag>(entity);
                bool isVisible = mgr.IsVisible(humanFaction, (Vector3)position);
                bool isRevealed = mgr.IsRevealed(humanFaction, (Vector3)position);
                bool isMine = em.HasComponent<FactionTag>(entity) && 
                              em.GetComponentData<FactionTag>(entity).Value == humanFaction;

                // Player-owned entities - always visible
                if (isMine)
                {
                    if (!gameObject.activeSelf) gameObject.SetActive(true);
                    continue;
                }

                // Enemy/neutral units - show when visible through fog OR
                // when any player unit is close enough to see them directly.
                // The direct distance check catches fog grid resolution issues.
                if (isUnit && !isBuilding)
                {
                    if (!isVisible)
                    {
                        for (int pi = 0; pi < playerCount; pi++)
                        {
                            float dx = position.x - playerPositions[pi].x;
                            float dz = position.z - playerPositions[pi].z;
                            float distSq = dx * dx + dz * dz;
                            float los = playerLOSRadii[pi];
                            if (distSq <= los * los)
                            {
                                isVisible = true;
                                break;
                            }
                        }
                    }

                    // Stealth: an enemy unit with StealthTag stays hidden inside
                    // our vision area unless one of our units is within proximity
                    // (mirrors TargetingSystem's 3u reveal — keeps "I can shoot it"
                    // and "I can see it" consistent).
                    if (isVisible && em.HasComponent<StealthTag>(entity))
                    {
                        const float StealthProximityRevealSq = 3f * 3f;
                        bool revealedByProximity = false;
                        for (int pi = 0; pi < playerCount; pi++)
                        {
                            float dx = position.x - playerPositions[pi].x;
                            float dz = position.z - playerPositions[pi].z;
                            if (dx * dx + dz * dz <= StealthProximityRevealSq)
                            {
                                revealedByProximity = true;
                                break;
                            }
                        }
                        if (!revealedByProximity) isVisible = false;
                    }

                    if (gameObject.activeSelf != isVisible) gameObject.SetActive(isVisible);
                    continue;
                }

                // Enemy/neutral static entities (buildings, deposits, border
                // structures — anything without UnitTag). Three-state visibility:
                //   currently visible              -> show normally
                //   previously revealed, not visible -> show as ghost (last-seen)
                //   never revealed                 -> hide entirely
                // Previously the ghost branch required isBuilding, which made
                // iron / veilstone deposits and any non-BuildingTag static entity
                // vanish for good once they left vision — fixed here.
                // (The old per-entity Renderer fetch + MaterialPropertyBlock
                // get/set pair was a no-op that allocated every frame — the
                // ghost shader hook can come back on a cached renderer when a
                // ghost material actually exists.)
                bool show = isVisible || isRevealed;   // revealed → last-seen ghost
                if (gameObject.activeSelf != show) gameObject.SetActive(show);
            }

            entities.Dispose();
            transforms.Dispose();
            playerPositions.Dispose();
            playerLOSRadii.Dispose();
        }
    }
}