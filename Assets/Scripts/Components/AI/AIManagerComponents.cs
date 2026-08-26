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

    // ═══════════════════════════════════════════════════════════════════════
    // ARMY SYSTEM
    // ═══════════════════════════════════════════════════════════════════════

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

    // ═══════════════════════════════════════════════════════════════════════
    // TACTICAL MANAGER STATE
    // ═══════════════════════════════════════════════════════════════════════

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
        public float LastVaultCheck;
        public float LastSmelterCheck;
    }
    // ═══════════════════════════════════════════════════════════════════════
    // CRYSTAL HUNT STATE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// State tracking for AI veilstone creature hunting.
    /// </summary>
    public struct AIVeilstoneHuntState : IComponentData
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