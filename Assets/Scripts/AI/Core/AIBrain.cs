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
        /// <summary>
        /// Locked-in strategy this AI follows for Age 1 (read by SimpleAISystem).
        /// Set at brain creation; the build order is selected from this.
        /// </summary>
        public AIStrategy Strategy;
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
        TechRush = 2,   // Tech Boom — rush Age 2 with Barracks tech upgrades
        Aggressive = 3, // Balanced — token military + Shrine + age up
        Defensive = 4,  // Standing army, Drills+Armor research, Vault
        Turtle = 5,     // Heavy economy + healers + big stockpile for Alanthor walls
    }

    /// <summary>
    /// Per-AI runtime state for the SimpleAISystem build-order executor.
    /// Tracks which step of the assigned build order the AI is on, plus the
    /// AI think-tick countdown. Lives on the same entity as <see cref="AIBrain"/>.
    /// </summary>
    public struct SimpleAIState : IComponentData
    {
        /// <summary>Which step of the build order the AI is currently trying to issue.</summary>
        public int StepIndex;
        /// <summary>Seconds until the next AI think tick (set by difficulty).</summary>
        public float ThinkTimer;
        /// <summary>Whether the AgeUp step has already been issued (latches to prevent re-trigger).</summary>
        public byte AgeUpIssued;
        /// <summary>
        /// Crystal-miner FLOOR. The runtime allocation is
        /// <c>max(this, totalMiners / 2)</c> whenever cadavers are reachable —
        /// 50/50 is the default, this field only matters if a strategy wants
        /// to front-load more crystal earlier (e.g. TechBoom asking for 2
        /// crystal miners while only 4 total exist). Set by SetCrystalTarget
        /// build-order steps; 0 = use the 50/50 floor only.
        /// </summary>
        public int CrystalMinerTarget;

        // ───── Replace-lost-units bookkeeping ─────
        // Cumulative count of units the build order has queued so far. Each tick
        // SimpleAISystem.ReplaceLostUnits compares these against (alive + queued)
        // and re-queues to make up the difference. Decrement is implicit — when
        // a unit dies it just no longer counts toward "alive" and the deficit
        // appears. So the build order never has to rewind StepIndex.
        /// <summary>How many combat-class units the build order has queued.</summary>
        public int DesiredMilitary;
        /// <summary>How many miners the build order has queued.</summary>
        public int DesiredMiners;
        /// <summary>Most recently queued combat unit type — used as the
        /// replacement template (e.g. "Swordsman" for Rush).</summary>
        public Unity.Collections.FixedString64Bytes LastMilitaryUnit;
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