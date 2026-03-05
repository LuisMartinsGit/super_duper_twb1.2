// HealCommand.cs
// Heal command component and execution logic
// Location: Assets/Scripts/Core/Commands/CommandTypes/HealCommand.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// ECS Component representing a heal command for a healer unit.
    /// When attached to an entity, healing systems will process it.
    /// </summary>
    public struct HealCommand : IComponentData
    {
        /// <summary>The friendly unit to heal</summary>
        public Entity Target;
    }

    /// <summary>
    /// Helper class for executing heal commands
    /// </summary>
    public static class HealCommandHelper
    {
        /// <summary>
        /// Execute a heal command on a healer unit.
        /// Clears conflicting commands and sets up healing state.
        /// </summary>
        public static void Execute(EntityManager em, Entity healer, Entity target)
        {
            if (!em.Exists(healer) || !em.Exists(target)) return;

            // Verify target is friendly
            if (!IsFriendly(em, healer, target)) return;

            // Verify target needs healing
            if (!NeedsHealing(em, target)) return;

            // Clear conflicting commands
            CommandHelper.ClearAllCommands(em, healer);

            // Set up heal command
            SetupHeal(em, healer, target);
        }

        /// <summary>
        /// Check if a heal command can be executed
        /// </summary>
        public static bool CanExecute(EntityManager em, Entity healer, Entity target)
        {
            if (!em.Exists(healer) || !em.Exists(target)) return false;
            if (!em.HasComponent<CanHeal>(healer)) return false;
            if (!IsFriendly(em, healer, target)) return false;
            if (!NeedsHealing(em, target)) return false;
            return true;
        }

        /// <summary>
        /// Check if target is on the same faction as healer
        /// </summary>
        public static bool IsFriendly(EntityManager em, Entity healer, Entity target)
        {
            if (!em.HasComponent<FactionTag>(healer) || !em.HasComponent<FactionTag>(target))
                return false;

            var healerFaction = em.GetComponentData<FactionTag>(healer).Value;
            var targetFaction = em.GetComponentData<FactionTag>(target).Value;

            return healerFaction == targetFaction;
        }

        /// <summary>
        /// Check if target has less than max health
        /// </summary>
        public static bool NeedsHealing(EntityManager em, Entity target)
        {
            if (!em.HasComponent<Health>(target)) return false;
            if (em.HasComponent<UnhealableTag>(target)) return false;

            var health = em.GetComponentData<Health>(target);
            return health.Value < health.Max;
        }

        /// <summary>
        /// Find nearest friendly unit that needs healing
        /// </summary>
        public static Entity FindNearestHealTarget(EntityManager em, Entity healer, float maxRange)
        {
            if (!em.HasComponent<FactionTag>(healer) || !em.HasComponent<LocalTransform>(healer))
                return Entity.Null;

            var healerFaction = em.GetComponentData<FactionTag>(healer).Value;
            var healerPos = em.GetComponentData<LocalTransform>(healer).Position;

            // Query all units that could be heal candidates
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<Health>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            var entities = query.ToEntityArray(Allocator.Temp);
            var healths = query.ToComponentDataArray<Health>(Allocator.Temp);
            var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            Entity bestTarget = Entity.Null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < entities.Length; i++)
            {
                // Skip self
                if (entities[i] == healer) continue;

                // Same faction only
                if (factions[i].Value != healerFaction) continue;

                // Skip unhealable units
                if (em.HasComponent<UnhealableTag>(entities[i])) continue;

                // Skip dead units
                if (healths[i].Value <= 0) continue;

                // Skip full-health units
                if (healths[i].Value >= healths[i].Max) continue;

                // XZ distance check
                float dist = math.distance(
                    new float2(healerPos.x, healerPos.z),
                    new float2(transforms[i].Position.x, transforms[i].Position.z)
                );

                if (dist <= maxRange && dist < bestDist)
                {
                    bestDist = dist;
                    bestTarget = entities[i];
                }
            }

            entities.Dispose();
            healths.Dispose();
            factions.Dispose();
            transforms.Dispose();

            return bestTarget;
        }

        private static void SetupHeal(EntityManager em, Entity healer, Entity target)
        {
            var cmd = new HealCommand { Target = target };

            if (!em.HasComponent<HealCommand>(healer))
                em.AddComponentData(healer, cmd);
            else
                em.SetComponentData(healer, cmd);

            // Move toward target if needed
            if (em.HasComponent<LocalTransform>(target))
            {
                var targetPos = em.GetComponentData<LocalTransform>(target).Position;

                if (em.HasComponent<DesiredDestination>(healer))
                {
                    em.SetComponentData(healer, new DesiredDestination
                    {
                        Position = targetPos,
                        Has = 1
                    });
                }
            }
        }
    }
}