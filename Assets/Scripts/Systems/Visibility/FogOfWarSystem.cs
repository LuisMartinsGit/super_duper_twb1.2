// File: Assets/Scripts/Systems/Visibility/FogOfWarSystem.cs
using Unity.Entities;
using Unity.Collections;
using Unity.Transforms;
using Unity.Mathematics;
using UnityEngine;
using EntityWorld = Unity.Entities.World;
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

            var em = EntityWorld.DefaultGameObjectInjectionWorld.EntityManager;

            // Begin new frame - clears current visibility
            mgr.BeginFrame();

            // Query all entities with LineOfSight and position
            // Exclude CrystalTag: crystal entities are enemy to all players and should NOT reveal fog
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<LineOfSight>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.Exclude<CrystalTag>());

            var entities = query.ToEntityArray(Allocator.Temp);
            var lineOfSights = query.ToComponentDataArray<LineOfSight>(Allocator.Temp);
            var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            int stamped = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (!em.Exists(entities[i])) continue;

                // Ensure valid radius
                float radius = Mathf.Max(0.01f, lineOfSights[i].Radius);

                // task-063 phase 1: sect FogVisionBonus removed with the
                // FactionSectState bridge. Phase 2 reintroduces vision-related sect
                // levers (e.g. Witness — All-Seeing).

                // Stamp visibility circle for this unit's faction
                mgr.Stamp(factions[i].Value, (Vector3)transforms[i].Position, radius);
                stamped++;
            }

            entities.Dispose();
            lineOfSights.Dispose();
            transforms.Dispose();
            factions.Dispose();

            // Finalize frame - builds fog texture
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
        protected override void OnUpdate()
        {
            var mgr = FogOfWarManager.Instance;
            var entityViewManager = Object.FindFirstObjectByType<EntityViewManager>();
            if (entityViewManager == null) return;

            var em = EntityWorld.DefaultGameObjectInjectionWorld.EntityManager;

            // When fog of war is disabled, make all entities visible
            if (mgr == null)
            {
                var allQuery = em.CreateEntityQuery(
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
            var playerLosQuery = em.CreateEntityQuery(
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
            var query = em.CreateEntityQuery(
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

                var renderer = gameObject.GetComponentInChildren<Renderer>();

                // Player-owned entities - always visible
                if (isMine)
                {
                    gameObject.SetActive(true);
                    if (renderer != null)
                    {
                        var mpb = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(mpb);
                        renderer.SetPropertyBlock(mpb);
                    }
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
                    gameObject.SetActive(isVisible);
                    continue;
                }

                // Enemy/neutral buildings
                if (isVisible)
                {
                    // Currently visible - show normally
                    gameObject.SetActive(true);
                    if (renderer != null)
                    {
                        var mpb = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(mpb);
                        // Clear any ghost effects
                        renderer.SetPropertyBlock(mpb);
                    }
                }
                else if (isBuilding && isRevealed)
                {
                    // Previously seen building - show as ghost
                    gameObject.SetActive(true);
                    if (renderer != null)
                    {
                        var mpb = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(mpb);
                        // Optional: Apply ghost shader properties
                        // mpb.SetFloat("_Desaturate", 1f);
                        // mpb.SetFloat("_Alpha", 0.5f);
                        renderer.SetPropertyBlock(mpb);
                    }
                }
                else
                {
                    // Never seen or unit out of sight - hide
                    gameObject.SetActive(false);
                }
            }

            entities.Dispose();
            transforms.Dispose();
            playerPositions.Dispose();
            playerLOSRadii.Dispose();
        }
    }
}