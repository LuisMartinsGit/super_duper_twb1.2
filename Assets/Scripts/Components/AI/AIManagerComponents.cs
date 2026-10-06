// AIManagerComponents.cs
// Components for AI management systems (Mission, Military, Tactical)

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

    // AIMilitaryState, AIBuildingState, AIEconomyState and
    // AIVeilstoneHuntState were removed on 2026-10-05: allocated per brain,
    // read by nothing (their managers were [DisableAutoCreation]).

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

    /// <summary>
    /// A queued building construction request.
    /// </summary>
    public struct BuildRequest : IBufferElementData
    {
        public FixedString64Bytes BuildingType;
        public float3 DesiredPosition;
        public int Priority;
        public byte Assigned;           // 0 = pending, 1 = assigned to worker
        public Entity AssignedWorker;
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
    
    /// <summary>Number of workers assigned to this mine</summary>
    public int AssignedWorkers;
    
    /// <summary>Target number of workers for this mine</summary>
    public int DesiredWorkers;
}
}