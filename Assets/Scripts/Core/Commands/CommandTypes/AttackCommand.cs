// AttackCommand.cs
// Attack command component and execution logic
// Location: Assets/Scripts/Core/Commands/CommandTypes/AttackCommand.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// ECS Component representing an attack command for a unit.
    /// When attached to an entity, combat systems will process it.
    /// </summary>
    public struct AttackCommand : IComponentData
    {
        /// <summary>The entity to attack</summary>
        public Entity Target;
    }

    /// <summary>
    /// Helper class for executing attack commands
    /// </summary>
    public static class AttackCommandHelper
    {
        /// <summary>
        /// Execute an attack command on a unit.
        /// Clears conflicting commands and sets up combat state.
        /// </summary>
        public static void Execute(EntityManager em, Entity unit, Entity target)
        {
            if (!em.Exists(unit) || !em.Exists(target)) return;
            if (!em.HasComponent<LocalTransform>(target)) return;

            // Battalion leader: move leader toward target, propagate attack to members
            if (em.HasComponent<BattalionLeader>(unit) && em.HasBuffer<BattalionMember>(unit))
            {
                var targetPos = em.GetComponentData<LocalTransform>(target).Position;
                var leaderPos = em.GetComponentData<LocalTransform>(unit).Position;

                // Copy member entities to a local array BEFORE any structural changes
                // (structural changes like RemoveComponent invalidate DynamicBuffer handles)
                var membersBuffer = em.GetBuffer<BattalionMember>(unit);
                int memberCount = membersBuffer.Length;
                var memberEntities = new NativeArray<Entity>(memberCount, Allocator.Temp);
                for (int i = 0; i < memberCount; i++)
                    memberEntities[i] = membersBuffer[i].Value;

                // Check if battalion contains ranged units — stop at firing range
                bool hasRanged = false;
                float maxRange = 25f;
                for (int i = 0; i < memberCount; i++)
                {
                    var m = memberEntities[i];
                    if (m != Entity.Null && em.Exists(m) && em.HasComponent<ArcherTag>(m))
                    {
                        hasRanged = true;
                        if (em.HasComponent<ArcherState>(m))
                            maxRange = em.GetComponentData<ArcherState>(m).MaxRange;
                        break;
                    }
                }

                if (hasRanged)
                {
                    // Move leader to a position just inside max range
                    float3 dir = math.normalizesafe(targetPos - leaderPos);
                    float dist = math.distance(leaderPos, targetPos);
                    float stopAt = maxRange - 3f; // Stop 3 units inside max range
                    if (dist > stopAt)
                    {
                        float3 movePos = targetPos - dir * stopAt;
                        MoveCommandHelper.Execute(em, unit, movePos);
                    }
                }
                else
                {
                    // Melee battalion: move to target position
                    MoveCommandHelper.Execute(em, unit, targetPos);
                }

                // Set target on the leader itself so BattalionSyncSystem can detect
                // combat mode and MovementLineDisplay shows red line.
                // Do NOT set targets on members — they march in formation until
                // BattalionSyncSystem detects encirclement range and assigns per-member targets.
                SetupAttack(em, unit, target);

                // Remove UserMoveOrder that MoveCommandHelper added — leader needs to
                // remain eligible for targeting systems during march
                if (em.HasComponent<UserMoveOrder>(unit))
                    em.RemoveComponent<UserMoveOrder>(unit);

                // Track enemy battalion so members can chain targets from the same group
                Entity enemyLeader = Entity.Null;
                if (em.HasComponent<BattalionMemberData>(target))
                    enemyLeader = em.GetComponentData<BattalionMemberData>(target).Leader;
                else if (em.HasComponent<BattalionLeader>(target))
                    enemyLeader = target;

                if (enemyLeader != Entity.Null)
                {
                    if (!em.HasComponent<BattalionAttackTarget>(unit))
                        em.AddComponentData(unit, new BattalionAttackTarget { EnemyLeader = enemyLeader });
                        else
                            em.SetComponentData(unit, new BattalionAttackTarget { EnemyLeader = enemyLeader });
                }

                memberEntities.Dispose();
                return;
            }

            // Clear conflicting commands (but NOT MoveCommand - combat system handles chasing)
            ClearConflictingCommands(em, unit);

            // Set up attack
            SetupAttack(em, unit, target);

            // Set guard point to current position (unit will return here after combat)
            SetGuardPointToCurrent(em, unit);
        }

        /// <summary>
        /// Check if an attack command can be executed
        /// </summary>
        public static bool CanExecute(EntityManager em, Entity unit, Entity target)
        {
            if (!em.Exists(unit) || !em.Exists(target)) return false;
            // Battalion leaders have no Damage — their members do the fighting
            if (!em.HasComponent<Damage>(unit) && !em.HasComponent<BattalionLeader>(unit)) return false;

            // Verify not attacking friendly unit
            if (em.HasComponent<FactionTag>(unit) && em.HasComponent<FactionTag>(target))
            {
                var unitFaction = em.GetComponentData<FactionTag>(unit).Value;
                var targetFaction = em.GetComponentData<FactionTag>(target).Value;
                if (unitFaction == targetFaction) return false;
            }

            return true;
        }

        private static void ClearConflictingCommands(EntityManager em, Entity unit)
        {
            // Clear build
            if (em.HasComponent<BuildCommand>(unit))
                em.RemoveComponent<BuildCommand>(unit);

            // Clear gather
            if (em.HasComponent<GatherCommand>(unit))
                em.RemoveComponent<GatherCommand>(unit);

            // Clear heal
            if (em.HasComponent<HealCommand>(unit))
                em.RemoveComponent<HealCommand>(unit);
            // Clear Litharch healing state
            if (em.HasComponent<LitharchState>(unit))
            {
                var ls = em.GetComponentData<LitharchState>(unit);
                if (ls.IsHealing != 0) { ls.HealTarget = Entity.Null; ls.IsHealing = 0; em.SetComponentData(unit, ls); }
            }

            // Clear UserMoveOrder to allow combat system to take over
            if (em.HasComponent<UserMoveOrder>(unit))
                em.RemoveComponent<UserMoveOrder>(unit);
        }

        private static void SetupAttack(EntityManager em, Entity unit, Entity target)
        {
            // Add or update AttackCommand component
            if (!em.HasComponent<AttackCommand>(unit))
                em.AddComponentData(unit, new AttackCommand { Target = target });
                else
                    em.SetComponentData(unit, new AttackCommand { Target = target });

            // Also set Target component for combat system
            if (em.HasComponent<Target>(unit))
                em.SetComponentData(unit, new Target { Value = target });
                else
                    em.AddComponentData(unit, new Target { Value = target });
        }

        private static void SetGuardPointToCurrent(EntityManager em, Entity unit)
        {
            if (!em.HasComponent<LocalTransform>(unit)) return;

            // If the unit already has a guard point (e.g., from a move command),
            // keep it — the unit should return to its intended destination after combat,
            // not to where it happened to be when attacked.
            if (em.HasComponent<GuardPoint>(unit))
            {
                var existing = em.GetComponentData<GuardPoint>(unit);
                if (existing.Has != 0) return; // Preserve existing guard point
            }

            var pos = em.GetComponentData<LocalTransform>(unit).Position;

            if (em.HasComponent<GuardPoint>(unit))
            {
                em.SetComponentData(unit, new GuardPoint
                {
                    Position = pos,
                    Has = 1
                });
            }
            else
            {
                em.AddComponentData(unit, new GuardPoint
                {
                    Position = pos,
                    Has = 1
                });
            }
        }
    }
}