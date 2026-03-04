// AIManagerComponents.cs
// Components for AI management systems (Mission, Military, Tactical)
// Location: Assets/Scripts/AI/Components/AIManagerComponents.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.AI
{
    // ═══════════════════════════════════════════════════════════════════════
    // MISSION SYSTEM
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mission type enumeration.
    /// </summary>
    public enum MissionType : byte
    {
        None = 0,
        Attack = 1,
        Defend = 2,
        Scout = 3,
        Raid = 4,
        Reinforce = 5,
        Expand = 6
    }

    /// <summary>
    /// Mission status enumeration.
    /// </summary>
    public enum MissionStatus : byte
    {
        Pending = 0,
        Active = 1,
        InProgress = 2,
        Completed = 3,
        Failed = 4,
        Cancelled = 5
    }

    /// <summary>
    /// Represents an AI mission (attack, defend, scout, etc.)
    /// </summary>
    public struct AIMission : IComponentData
    {
        /// <summary>Unique identifier for this mission</summary>
        public int MissionId;

        /// <summary>Type of mission</summary>
        public MissionType Type;

        /// <summary>Current status of the mission</summary>
        public MissionStatus Status;

        /// <summary>Faction that owns this mission</summary>
        public Faction OwnerFaction;

        /// <summary>Target faction for attack/raid missions</summary>
        public Faction TargetFaction;

        /// <summary>Target position for the mission</summary>
        public float3 TargetPosition;

        /// <summary>Target entity (if applicable)</summary>
        public Entity TargetEntity;

        /// <summary>Primary army assigned to this mission</summary>
        public Entity AssignedArmy;

        /// <summary>Mission priority (higher = more important)</summary>
        public int Priority;

        /// <summary>Required combat strength to complete mission</summary>
        public int RequiredStrength;

        /// <summary>Currently assigned combat strength</summary>
        public int AssignedStrength;

        /// <summary>Time when mission was created</summary>
        public double CreatedTime;

        /// <summary>Time when mission was last updated</summary>
        public double LastUpdateTime;

        /// <summary>Time when mission was completed (if completed)</summary>
        public float CompletedTime;
    }

    /// <summary>
    /// Buffer element for tracking armies assigned to a mission.
    /// </summary>
    public struct AssignedArmy : IBufferElementData
    {
        /// <summary>The army entity assigned to this mission</summary>
        public Entity ArmyEntity;

        /// <summary>Combat strength contributed by this army</summary>
        public int Strength;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ARMY SYSTEM
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Army status enumeration.
    /// </summary>
    public enum ArmyStatus : byte
    {
        Idle = 0,
        Moving = 1,
        Attacking = 2,
        Defending = 3,
        Retreating = 4,
        Regrouping = 5
    }

    /// <summary>
    /// Represents an AI-controlled army group.
    /// </summary>
    public struct AIArmy : IComponentData
    {
        /// <summary>Unique identifier for this army</summary>
        public int ArmyId;

        /// <summary>Faction that owns this army</summary>
        public Faction Owner;

        /// <summary>Current army status</summary>
        public ArmyStatus Status;

        /// <summary>Current center position of the army</summary>
        public float3 Position;

        /// <summary>Target position the army is moving towards</summary>
        public float3 TargetPosition;

        /// <summary>Currently assigned mission entity</summary>
        public Entity MissionEntity;

        /// <summary>Number of units in the army</summary>
        public int UnitCount;

        /// <summary>Total combat strength of all units</summary>
        public int TotalStrength;

        /// <summary>Last time army state was updated</summary>
        public float LastUpdateTime;

        /// <summary>Whether the army is currently engaging enemies (0 = no, 1 = yes)</summary>
        public byte IsEngaging;

        /// <summary>Whether the army is currently retreating (0 = no, 1 = yes)</summary>
        public byte IsRetreating;
    }

    /// <summary>
    /// Buffer element for tracking units in an army.
    /// </summary>
    public struct ArmyUnit : IBufferElementData
    {
        /// <summary>The unit entity</summary>
        public Entity Unit;

        /// <summary>Combat strength of this unit</summary>
        public int Strength;

        /// <summary>Type/class of the unit</summary>
        public UnitClass UnitType;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MILITARY STATE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// State tracking for AI military management.
    /// </summary>
    public struct AIMilitaryState : IComponentData
    {
        public int TotalSoldiers;
        public int TotalArchers;
        public int TotalSiegeUnits;
        public int ActiveBarracks;
        public int DesiredBarracks;
        public int ArmiesCount;
        public int ScoutsCount;
        public int QueuedSoldiers;
        public int QueuedArchers;
        public int QueuedSiegeUnits;
        public float LastRecruitmentCheck;
        public float RecruitmentCheckInterval;
    }

    /// <summary>
    /// A queued unit recruitment request.
    /// </summary>
    public struct RecruitmentRequest : IBufferElementData
    {
        public UnitClass UnitType;
        public int Quantity;
        public int Priority;
        public Entity RequestingManager;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MISSION MANAGER STATE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// State tracking for AI mission management.
    /// </summary>
    public struct AIMissionState : IComponentData
    {
        public int ActiveMissions;
        public int PendingMissions;
        public float LastMissionUpdate;
        public float MissionUpdateInterval;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // TACTICAL MANAGER STATE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// State tracking for AI tactical decisions.
    /// </summary>
    public struct AITacticalState : IComponentData
    {
        public int ManagedArmies;
        public float LastTacticalUpdate;
        public float TacticalUpdateInterval;
    }
    // ═══════════════════════════════════════════════════════════════════════
    // BUILDING STATE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// State tracking for AI building/construction management.
    /// </summary>
    public struct AIBuildingState : IComponentData
    {
        public int ActiveBuilders;
        public int DesiredBuilders;
        public int QueuedConstructions;
        public float LastBuildCheck;
        public float BuildCheckInterval;
    }

    /// <summary>
    /// A queued building construction request.
    /// </summary>
    public struct BuildRequest : IBufferElementData
    {
        public FixedString64Bytes BuildingType;
        public float3 DesiredPosition;
        public int Priority;
        public byte Assigned;           // 0 = pending, 1 = assigned to builder
        public Entity AssignedBuilder;
    }
    // ═══════════════════════════════════════════════════════════════════════
    // ECONOMY STATE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// State tracking for AI economy management.
    /// </summary>
    public struct AIEconomyState : IComponentData
    {
        public int AssignedMiners;
        public int DesiredMiners;
        public int ActiveGatherersHuts;
        public int DesiredGatherersHuts;
        public float LastMineAssignmentCheck;
        public float MineCheckInterval;
        public byte NeedsMoreSupplyIncome;
        public byte NeedsMoreIronIncome;
    }
    // ═══════════════════════════════════════════════════════════════════════
    // CRYSTAL HUNT STATE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// State tracking for AI crystal creature hunting.
    /// </summary>
    public struct AICrystalHuntState : IComponentData
    {
        public float LastHuntCheck;
        public float HuntCheckInterval;
    }

    // ═══════════════════════════════════════════════════════════════════════
// ECONOMY ASSIGNMENTS
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Tracks a mine assignment for AI economy management.
/// </summary>
public struct MineAssignment : IBufferElementData
{
    /// <summary>The mine entity</summary>
    public Entity Mine;
    
    /// <summary>Position of the mine</summary>
    public float3 Position;
    
    /// <summary>Number of miners assigned to this mine</summary>
    public int AssignedMiners;
    
    /// <summary>Target number of miners for this mine</summary>
    public int DesiredMiners;
}
}