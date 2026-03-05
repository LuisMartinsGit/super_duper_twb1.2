// File: Assets/Scripts/Systems/Creatures/CrystalLevelSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Systems.Creatures
{
    /// <summary>
    /// Manages the Crystal faction's leveling system.
    ///
    /// Crystal Main Nodes gain XP passively over time and level up
    /// through 5 tiers. Each level increases:
    /// - Spread radius (+5 per level)
    /// - Creature stats (HP/damage +10% per level above 1)
    /// - Max creatures per node (+1 per level)
    ///
    /// Level thresholds: L1=0, L2=50, L3=150, L4=350, L5=700
    /// Passive XP rate: 1 XP per 30 seconds per node.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CrystalLevelSystem : ISystem
    {
        /// <summary>Seconds between passive XP ticks.</summary>
        private const float XpTickInterval = 30f;

        /// <summary>XP gained per passive tick.</summary>
        private const int XpPerTick = 1;

        /// <summary>Spread radius bonus per level.</summary>
        private const float SpreadRadiusPerLevel = 5f;

        private float _xpTimer;

        /// <summary>XP thresholds for each level. Index = level (1-5).</summary>
        private static readonly int[] LevelThresholds = { 0, 0, 50, 150, 350, 700 };

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CrystalMainNodeTag>();
            _xpTimer = 0f;
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            _xpTimer += dt;

            bool xpTick = false;
            if (_xpTimer >= XpTickInterval)
            {
                _xpTimer -= XpTickInterval;
                xpTick = true;
            }

            foreach (var (levelState, crystalNode, entity) in SystemAPI
                .Query<RefRW<CrystalLevelState>, RefRW<CrystalNode>>()
                .WithAll<CrystalMainNodeTag>()
                .WithEntityAccess())
            {
                ref var level = ref levelState.ValueRW;
                ref var node = ref crystalNode.ValueRW;

                // Passive XP gain
                if (xpTick)
                {
                    level.Xp += XpPerTick;
                }

                // Check for level up (max level 5)
                if (level.Level < 5 && level.Xp >= level.XpToNext)
                {
                    level.Level++;
                    level.XpToNext = GetXpThreshold(level.Level + 1);

                    // Apply level-up bonuses
                    node.SpreadRadius += SpreadRadiusPerLevel;

                    Debug.Log($"[CrystalLevelSystem] Crystal node leveled up to {level.Level}! " +
                              $"SpreadRadius={node.SpreadRadius:F0}, XpToNext={level.XpToNext}");
                }
            }
        }

        /// <summary>
        /// Get XP required for a given level.
        /// </summary>
        private static int GetXpThreshold(int targetLevel)
        {
            if (targetLevel <= 1) return 0;
            if (targetLevel >= LevelThresholds.Length) return int.MaxValue;
            return LevelThresholds[targetLevel];
        }
    }
}
