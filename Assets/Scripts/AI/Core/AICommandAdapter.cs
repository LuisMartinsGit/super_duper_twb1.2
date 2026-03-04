// AICommandAdapter.cs
// Adapter for AI systems to issue commands through the unified CommandRouter
// Location: Assets/Scripts/AI/Core/AICommandAdapter.cs

using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;  // ← Required for CommandRouter

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Adapter for AI systems to issue commands through the unified CommandRouter.
    /// All AI commands go through here to ensure proper multiplayer synchronization.
    /// </summary>
    public static class AICommandAdapter
    {
        /// <summary>
        /// Check if AI should issue commands (for multiplayer sync).
        /// In multiplayer, only host AI processes commands.
        /// </summary>
        private static bool ShouldAIIssueCommands()
        {
            if (!GameSettings.IsMultiplayer) return true;
            return GameSettings.IsHost();
        }

        // ==================== Movement ====================

        /// <summary>
        /// Issue a move command from AI.
        /// </summary>
        public static void IssueMove(EntityManager em, Entity unit, float3 destination)
        {
            if (!ShouldAIIssueCommands()) return;
            if (unit == Entity.Null || !em.Exists(unit)) return;

            CommandRouter.IssueMove(em, unit, destination, CommandRouter.CommandSource.AI);
        }

        /// <summary>
        /// Issue move to multiple units in formation.
        /// </summary>
        public static void IssueMoveFormation(EntityManager em, NativeArray<Entity> units,
            float3 destination, float spacing = 2.5f)
        {
            if (!ShouldAIIssueCommands()) return;

            int count = 0;
            for (int i = 0; i < units.Length; i++)
            {
                if (units[i] != Entity.Null && em.Exists(units[i]))
                    count++;
            }

            if (count == 0) return;

            int cols = (int)math.ceil(math.sqrt(count));
            int row = 0, col = 0;

            for (int i = 0; i < units.Length; i++)
            {
                if (units[i] == Entity.Null || !em.Exists(units[i])) continue;

                float3 offset = new float3(
                    (col - cols / 2f) * spacing,
                    0,
                    (row - cols / 2f) * spacing
                );

                float3 targetPos = destination + offset;
                CommandRouter.IssueMove(em, units[i], targetPos, CommandRouter.CommandSource.AI);

                col++;
                if (col >= cols)
                {
                    col = 0;
                    row++;
                }
            }
        }

        /// <summary>
        /// Move all units in an army buffer to formation positions.
        /// </summary>
        public static void MoveArmy(EntityManager em, DynamicBuffer<ArmyUnit> armyUnits,
            float3 destination, float spacing = 2.5f)
        {
            if (!ShouldAIIssueCommands()) return;

            int count = 0;
            for (int i = 0; i < armyUnits.Length; i++)
            {
                if (armyUnits[i].Unit != Entity.Null && em.Exists(armyUnits[i].Unit))
                    count++;
            }

            if (count == 0) return;

            int cols = (int)math.ceil(math.sqrt(count));
            int row = 0, col = 0;

            for (int i = 0; i < armyUnits.Length; i++)
            {
                var unit = armyUnits[i].Unit;
                if (unit == Entity.Null || !em.Exists(unit)) continue;

                float3 offset = new float3(
                    (col - cols / 2f) * spacing,
                    0,
                    (row - cols / 2f) * spacing
                );

                float3 targetPos = destination + offset;
                CommandRouter.IssueMove(em, unit, targetPos, CommandRouter.CommandSource.AI);

                col++;
                if (col >= cols)
                {
                    col = 0;
                    row++;
                }
            }
        }

        // ==================== Combat ====================

        /// <summary>
        /// Issue an attack command from AI.
        /// </summary>
        public static void IssueAttack(EntityManager em, Entity unit, Entity target)
        {
            if (!ShouldAIIssueCommands()) return;
            if (unit == Entity.Null || target == Entity.Null) return;
            if (!em.Exists(unit) || !em.Exists(target)) return;

            CommandRouter.IssueAttack(em, unit, target, CommandRouter.CommandSource.AI);
        }

        /// <summary>
        /// Issue a stop command from AI.
        /// </summary>
        public static void IssueStop(EntityManager em, Entity unit)
        {
            if (!ShouldAIIssueCommands()) return;
            if (unit == Entity.Null || !em.Exists(unit)) return;

            CommandRouter.IssueStop(em, unit, CommandRouter.CommandSource.AI);
        }

        // ==================== Economy ====================

        /// <summary>
        /// Issue a gather command from AI.
        /// </summary>
        public static void IssueGather(EntityManager em, Entity miner, Entity resourceNode, Entity depositLocation)
        {
            if (!ShouldAIIssueCommands()) return;
            if (miner == Entity.Null || resourceNode == Entity.Null) return;
            if (!em.Exists(miner)) return;

            CommandRouter.IssueGather(em, miner, resourceNode, depositLocation, CommandRouter.CommandSource.AI);
        }

        /// <summary>
        /// Issue a build command from AI.
        /// </summary>
        public static void IssueBuild(EntityManager em, Entity builder, Entity targetBuilding,
            string buildingId, float3 position)
        {
            if (!ShouldAIIssueCommands()) return;
            if (builder == Entity.Null || !em.Exists(builder)) return;

            CommandRouter.IssueBuild(em, builder, targetBuilding, buildingId, position, CommandRouter.CommandSource.AI);
        }

        // ==================== Support ====================

        /// <summary>
        /// Issue a heal command from AI.
        /// </summary>
        public static void IssueHeal(EntityManager em, Entity healer, Entity target)
        {
            if (!ShouldAIIssueCommands()) return;
            if (healer == Entity.Null || target == Entity.Null) return;
            if (!em.Exists(healer) || !em.Exists(target)) return;

            CommandRouter.IssueHeal(em, healer, target, CommandRouter.CommandSource.AI);
        }

        /// <summary>
        /// Set rally point for a building from AI.
        /// </summary>
        public static void SetRallyPoint(EntityManager em, Entity building, float3 position)
        {
            if (!ShouldAIIssueCommands()) return;
            if (building == Entity.Null || !em.Exists(building)) return;

            CommandRouter.SetRallyPoint(em, building, position, CommandRouter.CommandSource.AI);
        }

        // ==================== Batch Operations ====================

        /// <summary>
        /// Issue attack commands to all units in an army buffer against prioritized targets.
        /// </summary>
        public static void AttackWithArmy(EntityManager em, DynamicBuffer<ArmyUnit> armyUnits, Entity target)
        {
            if (!ShouldAIIssueCommands()) return;
            if (target == Entity.Null || !em.Exists(target)) return;

            for (int i = 0; i < armyUnits.Length; i++)
            {
                var unit = armyUnits[i].Unit;
                if (unit == Entity.Null || !em.Exists(unit)) continue;

                CommandRouter.IssueAttack(em, unit, target, CommandRouter.CommandSource.AI);
            }
        }

        /// <summary>
        /// Issue stop commands to all units in an army.
        /// </summary>
        public static void StopArmy(EntityManager em, DynamicBuffer<ArmyUnit> armyUnits)
        {
            if (!ShouldAIIssueCommands()) return;

            for (int i = 0; i < armyUnits.Length; i++)
            {
                var unit = armyUnits[i].Unit;
                if (unit == Entity.Null || !em.Exists(unit)) continue;

                CommandRouter.IssueStop(em, unit, CommandRouter.CommandSource.AI);
            }
        }
    }
}