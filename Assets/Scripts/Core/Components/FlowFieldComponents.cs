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
    /// Given a unit position, return the movement direction from a cached flow field
    /// using a 5×5 Gaussian kernel convolution for smooth cell-boundary transitions.
    /// The snappedDest parameter must come from FlowFieldManager.RequestFlowField()
    /// (via FlowField.DestinationIndex) to ensure the same snap-to-passable and
    /// coarse-grid snapping logic is used for both caching and lookup.
    /// Returns the direct-line direction as fallback.
    /// </summary>
    /// <param name="position">Unit's current world position.</param>
    /// <param name="snappedDest">Snapped destination cell index from FlowField.DestinationIndex, or -1 if no field.</param>
    /// <param name="directDir">Pre-computed normalized direct-line direction.</param>
    /// <param name="distToGoal">Pre-computed distance to goal (horizontal).</param>
    public float3 GetDirection(float3 position, int snappedDest, float3 directDir, float distToGoal)
    {
        if (!IsValid || snappedDest < 0) return directDir;

        // Look up slot for this destination
        if (!DestToSlot.TryGetValue(snappedDest, out int slot))
            return directDir;

        int cx = (int)math.floor((position.x - Origin.x) / CellSize);
        int cz = (int)math.floor((position.z - Origin.z) / CellSize);
        cx = math.clamp(cx, 0, GridWidth - 1);
        cz = math.clamp(cz, 0, GridHeight - 1);

        // ── 5×5 Gaussian kernel convolution ──
        // Samples a 5×5 neighborhood of direction vectors weighted by
        // a Gaussian (σ=1). Skips impassable/unreachable cells (zero direction)
        // so walls don't dilute the flow. Produces a direction that varies
        // continuously as the unit moves across cell boundaries.
        float2 weightedSum = float2.zero;
        float totalWeight = 0f;

        for (int dy = -2; dy <= 2; dy++)
        {
            int nz = cz + dy;
            if (nz < 0 || nz >= GridHeight) continue;

            for (int dx = -2; dx <= 2; dx++)
            {
                int nx = cx + dx;
                if (nx < 0 || nx >= GridWidth) continue;

                int idx = slot * CellsPerField + nz * GridWidth + nx;
                if (idx < 0 || idx >= DirectionData.Length) continue;

                float2 cellDir = DirectionData[idx];
                // Skip unreachable/impassable/destination cells (zero direction)
                if (math.lengthsq(cellDir) < 1e-6f) continue;

                float w = GaussianWeight5x5(dx, dy);
                weightedSum += cellDir * w;
                totalWeight += w;
            }
        }

        // Fallback to direct-line if no valid neighbors in the kernel
        if (totalWeight < 1e-6f || math.lengthsq(weightedSum) < 1e-6f)
            return directDir;

        float2 smoothDir2 = math.normalize(weightedSum);
        float3 flowDir = new float3(smoothDir2.x, 0f, smoothDir2.y);

        // Blend: near destination use direct-line for precise arrival,
        // far from destination use flow field for obstacle avoidance.
        if (distToGoal < BlendRadius)
        {
            float t = distToGoal / BlendRadius;
            float3 blended = math.lerp(directDir, flowDir, t);
            return math.normalizesafe(blended);
        }

        return flowDir;
    }

    /// <summary>
    /// Pre-computed 5×5 Gaussian kernel weight (σ = 1.0).
    /// w = exp(-(dx² + dy²) / 2). All unique d² values for a 5×5 window are 0..8.
    /// </summary>
    private static float GaussianWeight5x5(int dx, int dy)
    {
        int d2 = dx * dx + dy * dy;
        // exp(-d²/2) pre-computed for d² = 0..8
        return d2 switch
        {
            0 => 1.0000f,   // center
            1 => 0.6065f,   // cardinal ±1
            2 => 0.3679f,   // diagonal ±1
            4 => 0.1353f,   // cardinal ±2
            5 => 0.0821f,   // knight-move (±1,±2)
            8 => 0.0183f,   // diagonal ±2
            _ => 0f,
        };
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
