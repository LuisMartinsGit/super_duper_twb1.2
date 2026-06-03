// ResearchComponents.cs
// ECS components for the technology research system
// Place in: Assets/Scripts/Core/Components/

using Unity.Collections;
using Unity.Entities;

// ==================== Research System ====================

/// <summary>
/// Current research state of a building.
/// Mirrors TrainingState pattern for research processing.
/// Attached to buildings that have a "research" array in their TechTreeDB definition.
/// </summary>
public struct ResearchState : IComponentData
{
    /// <summary>0 = idle, 1 = researching</summary>
    public byte Busy;

    /// <summary>Seconds until current technology completes</summary>
    public float Remaining;
}

/// <summary>
/// Queue item for technology research.
/// Buffer attached to buildings that can research.
/// Cost is deducted at queue time (same pattern as TrainQueueItem).
/// </summary>
public struct ResearchQueueItem : IBufferElementData
{
    public FixedString64Bytes TechId;
}
