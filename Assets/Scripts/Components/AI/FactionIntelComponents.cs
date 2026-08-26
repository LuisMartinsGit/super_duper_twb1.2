// FactionIntelComponents.cs
// Per-AI-faction intel memory: what the faction has SEEN, where, and when.
// AI plan M1 (docs/AI_Assessment_and_Plan.md) — the last-known-position store
// that scouting feeds and target scoring consumes.
//
// One DynamicBuffer<EnemySightingRecord> lives on each AIBrain entity,
// written by IntelSystem on a fixed cadence from the fog-of-war grids.
// Records persist after the enemy leaves vision (that IS the point — the AI
// remembers); dead entities are pruned. Replaces the old single-position
// AISharedKnowledge aggregate for decision-making.

using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.AI
{
    /// <summary>Broad target category a sighting belongs to. Drives the
    /// TargetScorer type weights and the FOW rule applied on re-validation
    /// (units need CURRENT visibility, statics only need revealed).</summary>
    public enum IntelCategory : byte
    {
        MilitaryUnit     = 0,
        MilitaryBuilding = 1,
        EcoBuilding      = 2,
        Hall             = 3,
        BorderNode        = 4,
        Miner            = 5,
    }

    /// <summary>
    /// One remembered enemy entity. Position/time are from the LAST tick the
    /// entity was inside this faction's current-visibility grid.
    /// </summary>
    public struct EnemySightingRecord : IBufferElementData
    {
        public Entity Enemy;
        public Faction OwnerFaction;
        public float3 Position;
        /// <summary>SystemAPI.Time.ElapsedTime at last sighting.</summary>
        public float LastSeenTime;
        /// <summary>Heuristic strength (see TacticalQuery.UnitStrength).</summary>
        public int EstStrength;
        public IntelCategory Category;
    }
}
