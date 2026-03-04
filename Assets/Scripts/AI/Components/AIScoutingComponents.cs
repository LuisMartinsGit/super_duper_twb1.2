// AIScoutingComponents.cs
// Components for AI scouting and exploration systems
// Location: Assets/Scripts/AI/Components/AIScoutingComponents.cs

using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.AI
{
    // ═══════════════════════════════════════════════════════════════════════
    // EXPLORATION ZONE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents a zone on the map for AI exploration tracking.
    /// Used by AIScoutingBehavior to track explored/unexplored areas.
    /// </summary>
    public struct ExplorationZone : IBufferElementData
    {
        /// <summary>Center position of this exploration zone</summary>
        public float3 CenterPosition;

        /// <summary>Radius of the zone</summary>
        public float Radius;

        /// <summary>Last time this zone was visited by a scout (game time)</summary>
        public float LastVisitedTime;

        /// <summary>Number of times this zone has been visited</summary>
        public int VisitCount;

        /// <summary>Priority for exploration (higher = more important)</summary>
        public int Priority;

        /// <summary>Whether this zone has been explored at least once (0 = no, 1 = yes)</summary>
        public byte IsExplored;

        /// <summary>Whether enemy presence was detected in this zone (0 = no, 1 = yes)</summary>
        public byte HasEnemyPresence;

        /// <summary>Faction that owns this exploration zone data</summary>
        public Faction Owner;
    }

    /// <summary>
    /// Buffer element for storing multiple exploration zones per AI brain.
    /// </summary>
    public struct ExplorationZoneBuffer : IBufferElementData
    {
        public float3 CenterPosition;
        public float Radius;
        public float LastVisitedTime;
        public int VisitCount;
        public int Priority;
        public byte IsExplored;
        public byte HasEnemyPresence;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // COMBAT POWER
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents the combat strength of a unit or army.
    /// Used for AI tactical decisions and army composition.
    /// </summary>
    public struct CombatPower : IComponentData
    {
        /// <summary>Base combat power value</summary>
        public int Value;

        /// <summary>Offensive strength (attack capability)</summary>
        public int OffensivePower;

        /// <summary>Defensive strength (survivability)</summary>
        public int DefensivePower;

        /// <summary>Threat level this unit poses (for targeting priority)</summary>
        public float ThreatLevel;
    }

    /// <summary>
    /// Buffer element for tracking combat power of units in an army.
    /// </summary>
    public struct CombatPowerEntry : IBufferElementData
    {
        public Entity Unit;
        public int Power;
        public UnitClass UnitType;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SCOUTING STATE
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// State tracking for AI scouting behavior.
    /// </summary>
    public struct AIScoutingState : IComponentData
    {
        /// <summary>Number of active scouts</summary>
        public int ActiveScouts;

        /// <summary>Target number of scouts to maintain</summary>
        public int DesiredScouts;

        /// <summary>Last time scout assignments were updated</summary>
        public float LastScoutUpdate;

        /// <summary>Interval between scout updates</summary>
        public float ScoutUpdateInterval;

        /// <summary>Last time scouting priorities were updated</summary>
        public float LastPriorityUpdate;

        /// <summary>Update interval for scouting priorities</summary>
        public float PriorityUpdateInterval;

        /// <summary>Zones that need exploration</summary>
        public int UnexploredZoneCount;

        /// <summary>Percentage of map that has been explored (0-100)</summary>
        public float MapExplorationPercent;
    }

    /// <summary>
    /// Represents an active scout assignment linking a scout unit to a target zone.
    /// </summary>
    public struct ScoutAssignment : IBufferElementData
    {
        /// <summary>The scout unit entity assigned to this patrol</summary>
        public Entity ScoutUnit;

        /// <summary>Target area/zone center to scout</summary>
        public float3 TargetArea;

        /// <summary>Index into the ExplorationZone buffer for the assigned zone</summary>
        public int AssignedZoneIndex;

        /// <summary>Time when this assignment was given</summary>
        public double AssignmentTime;

        /// <summary>Distance from scout to target when assigned</summary>
        public float DistanceToTarget;

        /// <summary>Whether this assignment is currently active (0 = no, 1 = yes)</summary>
        public byte IsActive;

        /// <summary>Priority of this scouting mission</summary>
        public int Priority;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ENEMY SIGHTING
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Buffer element for tracking enemy sightings by AI scouts.
    /// Used to build tactical awareness of enemy positions and strength.
    /// </summary>
    public struct EnemySighting : IBufferElementData
    {
        /// <summary>Which faction was spotted</summary>
        public Faction EnemyFaction;

        /// <summary>Where the enemy was seen</summary>
        public float3 Position;

        /// <summary>Game time when sighting occurred</summary>
        public double TimeStamp;

        /// <summary>Estimated combat strength at this position</summary>
        public int EstimatedStrength;

        /// <summary>1 if this is a base/building, 0 if units</summary>
        public byte IsBase;
    }
}