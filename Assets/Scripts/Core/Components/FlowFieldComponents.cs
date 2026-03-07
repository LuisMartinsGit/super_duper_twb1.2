// FlowFieldComponents.cs
// Data types for flow field pathfinding: FlowField (integration + direction fields)
// and FlowFieldRequest (queued generation request).
// Location: Assets/Scripts/Core/Components/FlowFieldComponents.cs

using Unity.Collections;
using Unity.Mathematics;

// ==================== Flow Field Data ====================

/// <summary>
/// A computed flow field containing an integration field (cost-to-goal per cell)
/// and a direction field (unit direction toward lower cost per cell).
/// NOT an IComponentData -- this is managed data owned by FlowFieldManager, not per-entity.
/// Caller must Dispose() when no longer needed to free NativeArrays.
/// </summary>
public struct FlowField
{
    /// <summary>
    /// Cost-to-goal for each cell (flat index = y * Width + x).
    /// ushort.MaxValue (65535) = unreachable / unvisited sentinel.
    /// 0 = destination cell.
    /// </summary>
    public NativeArray<ushort> IntegrationField;

    /// <summary>
    /// Unit direction vector pointing toward lower cost for each cell.
    /// float2.zero = no valid direction (impassable, unreachable, or destination cell itself).
    /// </summary>
    public NativeArray<float2> DirectionField;

    /// <summary>Flat index of the destination cell (y * Width + x).</summary>
    public int DestinationIndex;

    /// <summary>Grid width in cells (copied from PassabilityGrid at creation time).</summary>
    public int Width;

    /// <summary>Grid height in cells (copied from PassabilityGrid at creation time).</summary>
    public int Height;

    /// <summary>True if NativeArrays are allocated and valid.</summary>
    public bool IsCreated => IntegrationField.IsCreated && DirectionField.IsCreated;

    /// <summary>
    /// Disposes both NativeArrays. Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (IntegrationField.IsCreated)
            IntegrationField.Dispose();
        if (DirectionField.IsCreated)
            DirectionField.Dispose();
    }
}

// ==================== Flow Field Request ====================

/// <summary>
/// A request to generate a flow field for a specific destination cell.
/// Queued in FlowFieldManager and processed up to MaxFieldsPerFrame per frame.
/// </summary>
public struct FlowFieldRequest
{
    /// <summary>Flat index into PassabilityGrid.Cells for the destination.</summary>
    public int DestinationCellIndex;

    /// <summary>Original world position of the destination (for convenience).</summary>
    public float3 WorldDestination;
}
