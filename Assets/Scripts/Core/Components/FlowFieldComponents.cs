// FlowFieldComponents.cs
// Data types for flow field pathfinding: FlowField (integration + direction fields),
// FlowFieldRequest (queued generation request), and FlowFieldArrayPool (NativeArray pooling).
// Location: Assets/Scripts/Core/Components/FlowFieldComponents.cs

using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

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

    /// <summary>
    /// Grid version at the time this field was generated.
    /// Used for staleness detection when the passability grid changes.
    /// </summary>
    public int GridVersion;

    /// <summary>True if NativeArrays are allocated and valid.</summary>
    public bool IsCreated => IntegrationField.IsCreated && DirectionField.IsCreated;

    /// <summary>
    /// Returns NativeArrays to the pool instead of deallocating.
    /// Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (IntegrationField.IsCreated)
            FlowFieldArrayPool.Return(IntegrationField);
        if (DirectionField.IsCreated)
            FlowFieldArrayPool.Return(DirectionField);
        IntegrationField = default;
        DirectionField = default;
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

// ==================== NativeArray Pool ====================

/// <summary>
/// Static pool for NativeArrays used by flow fields.
/// Reduces allocation churn by reusing arrays returned from evicted cache entries.
/// Arrays use Allocator.Persistent and are disposed on shutdown via DisposeAll().
/// </summary>
public static class FlowFieldArrayPool
{
    private static readonly Stack<NativeArray<ushort>> _integrationPool = new();
    private static readonly Stack<NativeArray<float2>> _directionPool = new();
    private static int _cellCount;
    private static bool _initialized;

    #if UNITY_EDITOR
    private static bool _warmUp;
    #endif

    /// <summary>
    /// Initialize the pool with the grid cell count. Must be called once
    /// before any Rent calls (typically from FlowFieldManager on first Update).
    /// </summary>
    public static void Init(int cellCount)
    {
        _cellCount = cellCount;
        _initialized = true;
        #if UNITY_EDITOR
        _warmUp = false;
        #endif
    }

    /// <summary>
    /// Rent an integration field array (ushort) from the pool.
    /// Returns a pooled array if available, otherwise allocates a new one.
    /// </summary>
    public static NativeArray<ushort> RentIntegration()
    {
        if (_integrationPool.Count > 0)
            return _integrationPool.Pop();

        #if UNITY_EDITOR
        if (_warmUp)
            Debug.Log("[FlowFieldArrayPool] Allocating new integration NativeArray (pool empty)");
        #endif

        return new NativeArray<ushort>(_cellCount, Allocator.Persistent);
    }

    /// <summary>
    /// Rent a direction field array (float2) from the pool.
    /// Returns a pooled array if available, otherwise allocates a new one.
    /// </summary>
    public static NativeArray<float2> RentDirection()
    {
        if (_directionPool.Count > 0)
            return _directionPool.Pop();

        #if UNITY_EDITOR
        if (_warmUp)
            Debug.Log("[FlowFieldArrayPool] Allocating new direction NativeArray (pool empty)");
        #endif

        return new NativeArray<float2>(_cellCount, Allocator.Persistent);
    }

    /// <summary>Return an integration field array to the pool for reuse.</summary>
    public static void Return(NativeArray<ushort> arr)
    {
        if (arr.IsCreated)
            _integrationPool.Push(arr);
    }

    /// <summary>Return a direction field array to the pool for reuse.</summary>
    public static void Return(NativeArray<float2> arr)
    {
        if (arr.IsCreated)
            _directionPool.Push(arr);
    }

    /// <summary>
    /// Mark the pool as warmed up. After this, new allocations in editor
    /// will log a warning to help detect pool sizing issues.
    /// </summary>
    public static void MarkWarmedUp()
    {
        #if UNITY_EDITOR
        _warmUp = true;
        #endif
    }

    /// <summary>
    /// Dispose all pooled arrays. Call on shutdown (FlowFieldManager.OnDestroy).
    /// </summary>
    public static void DisposeAll()
    {
        while (_integrationPool.Count > 0)
        {
            var arr = _integrationPool.Pop();
            if (arr.IsCreated) arr.Dispose();
        }
        while (_directionPool.Count > 0)
        {
            var arr = _directionPool.Pop();
            if (arr.IsCreated) arr.Dispose();
        }
        _initialized = false;
    }

    /// <summary>Whether the pool has been initialized with a cell count.</summary>
    public static bool IsInitialized => _initialized;
}

// ==================== Flow Field Lookup (Burst-Compatible) ====================

/// <summary>
/// Value-type struct for Burst-compatible flow field direction lookup.
/// Holds a flat NativeArray containing all cached direction fields concatenated,
/// plus a NativeHashMap mapping destination cell index to slot index.
/// Used by MovementSystem to avoid managed singleton access in the hot loop.
/// </summary>
public struct FlowFieldLookup
{
    /// <summary>
    /// All cached direction fields concatenated: slot i occupies
    /// [i * CellsPerField .. (i+1) * CellsPerField - 1].
    /// </summary>
    [ReadOnly] public NativeArray<float2> DirectionData;

    /// <summary>Maps destination cell index to slot index in DirectionData.</summary>
    [ReadOnly] public NativeHashMap<int, int> DestToSlot;

    /// <summary>Passability grid cells (0 = passable). Used for line-of-sight checks.</summary>
    [ReadOnly] public NativeArray<byte> PassabilityCells;

    /// <summary>Number of cells per flow field (Width * Height).</summary>
    public int CellsPerField;

    /// <summary>Grid width in cells.</summary>
    public int GridWidth;

    /// <summary>Grid height in cells.</summary>
    public int GridHeight;

    /// <summary>World units per cell.</summary>
    public float CellSize;

    /// <summary>World position of cell (0,0) corner.</summary>
    public float3 Origin;

    /// <summary>Whether this lookup has been populated with valid data.</summary>
    public bool IsValid;

    /// <summary>Blend radius in world units for direct-line blending near destination.</summary>
    private const float BlendRadius = 6f;

    /// <summary>
    /// Given a unit position, return the movement direction from a cached flow field.
    /// Checks if the direct path is clear by sampling cells ahead:
    /// - If all cells along the direct path are passable, uses direct direction for smooth movement.
    /// - If any cell ahead is blocked, follows the flow field direction around obstacles.
    /// Near the destination, always uses direct direction for precise arrival.
    /// </summary>
    /// <param name="position">Unit's current world position.</param>
    /// <param name="snappedDest">Snapped destination cell index from FlowField.DestinationIndex, or -1 if no field.</param>
    /// <param name="directDir">Pre-computed normalized direct-line direction.</param>
    /// <param name="distToGoal">Pre-computed distance to goal (horizontal).</param>
    public float3 GetDirection(float3 position, int snappedDest, float3 directDir, float distToGoal)
    {
        if (!IsValid || snappedDest < 0) return directDir;

        // Near destination: always use direct-line for precise arrival
        if (distToGoal < BlendRadius)
            return directDir;

        // Look up slot for this destination
        if (!DestToSlot.TryGetValue(snappedDest, out int slot))
            return directDir;

        // Per-cell flow field lookup
        int cx = (int)math.floor((position.x - Origin.x) / CellSize);
        int cz = (int)math.floor((position.z - Origin.z) / CellSize);
        cx = math.clamp(cx, 0, GridWidth - 1);
        cz = math.clamp(cz, 0, GridHeight - 1);

        int idx = slot * CellsPerField + cz * GridWidth + cx;
        if (idx < 0 || idx >= DirectionData.Length)
            return directDir;

        float2 flowDir2 = DirectionData[idx];

        // If cell direction is zero (unreachable/destination), use direct
        if (math.lengthsq(flowDir2) < 1e-6f)
            return directDir;

        float3 flowDir = math.normalizesafe(new float3(flowDir2.x, 0f, flowDir2.y));

        // Check if the direct path is clear by sampling cells ahead.
        // Only use direct direction if there are no obstacles in the way.
        // This replaces the old dot-product agreement check which let units
        // walk into forests when the flow field direction was close to direct.
        if (PassabilityCells.IsCreated)
        {
            float lookAhead = math.min(distToGoal, CellSize * 20f);
            bool pathClear = true;

            for (float d = CellSize; d <= lookAhead; d += CellSize)
            {
                float3 checkPos = position + directDir * d;
                int checkX = (int)math.floor((checkPos.x - Origin.x) / CellSize);
                int checkZ = (int)math.floor((checkPos.z - Origin.z) / CellSize);
                checkX = math.clamp(checkX, 0, GridWidth - 1);
                checkZ = math.clamp(checkZ, 0, GridHeight - 1);

                int cellIdx = checkZ * GridWidth + checkX;
                if (cellIdx >= 0 && cellIdx < PassabilityCells.Length && PassabilityCells[cellIdx] != 0)
                {
                    pathClear = false;
                    break;
                }
            }

            if (pathClear)
                return directDir;
        }

        return flowDir;
    }

    /// <summary>
    /// Sample direction data for a single cell, clamping to grid bounds.
    /// Returns float2.zero for out-of-bounds or invalid cells.
    /// </summary>
    private float2 SampleCell(int slot, int cx, int cy)
    {
        cx = math.clamp(cx, 0, GridWidth - 1);
        cy = math.clamp(cy, 0, GridHeight - 1);
        int idx = slot * CellsPerField + cy * GridWidth + cx;
        if (idx < 0 || idx >= DirectionData.Length)
            return float2.zero;
        return DirectionData[idx];
    }
}
