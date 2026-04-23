// AIBrain.cs
// Core AI controller component and initialization
using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;

namespace TheWaningBorder.AI
{
    // ==================== AI Brain Component ====================

    /// <summary>
    /// Main AI controller for a faction. One per AI player.
    /// </summary>
    public struct AIBrain : IComponentData
    {
        public Faction Owner;
        public float UpdateInterval;
        public float NextUpdateTime;
        public byte IsActive;
        public AIPersonality Personality;
        public AIDifficulty Difficulty;
    }

    public enum AIPersonality : byte
    {
        Balanced = 0,
        Aggressive = 1,
        Defensive = 2,
        Economic = 3,
        Rush = 4
    }

    public enum AIDifficulty : byte
    {
        Easy = 0,
        Normal = 1,
        Hard = 2,
        Expert = 3
    }

    // ==================== AI Strategy ====================

    public enum AIStrategy : byte
    {
        Rush = 0,       // Fast barracks, early harassment, minimal economy
        EcoBoom = 1,    // Heavy gatherers, crystal farming, delayed military
        TechRush = 2,   // Rush Age 2, culture buildings, advanced units
        Aggressive = 3, // Continuous pressure, balanced but attack-focused
        Defensive = 4,  // Walls, standing army, counter-attack on opportunity
    }

    /// <summary>
    /// Dynamic strategy state that changes during the game based on conditions.
    /// Attached to the same entity as AIBrain.
    /// </summary>
    public struct AIStrategyState : IComponentData
    {
        public AIStrategy Current;
        public AIStrategy Previous;
        public float LastEvalTime;       // When strategy was last evaluated
        public float EvalInterval;       // How often to re-evaluate (difficulty-dependent)
        public float StrategyStartTime;  // When the current strategy was adopted
        public int ArmiesLostSinceSwitch;   // Armies lost without dealing significant damage
        public int SuccessfulAttacks;        // Attacks that dealt significant damage
        public byte HasAgedUp;               // Whether this AI has completed age-up
    }

    // ==================== Shared Knowledge ====================

    public struct AISharedKnowledge : IComponentData
    {
        public float3 EnemyLastKnownPosition;
        public double EnemyLastSeenTime;
        public int EnemyEstimatedStrength;
        public int KnownEnemyBases;
        public int OwnMilitaryStrength;
        public int OwnEconomicStrength;
        public int EnemyBasesSpotted;
        public int EnemyArmiesSpotted;
    }

    public struct ResourceRequest : IBufferElementData
    {
        public int Supplies;
        public int Iron;
        public int Crystal;
        public int Veilsteel;
        public int Glow;
        public int Priority;
        public Entity Requester;
        public byte Approved;
    }
}