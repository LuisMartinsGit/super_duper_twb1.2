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