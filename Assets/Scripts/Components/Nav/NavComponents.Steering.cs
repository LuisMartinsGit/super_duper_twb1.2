// NavComponents.Steering.cs
// Spatial hash + steering (M2): the neighbour grid unit avoidance reads and
// the per-unit steering direction it writes.
// Split out of NavComponents.cs (2026-08-12): that file had grown to 35
// unrelated declarations across seven milestones. Global namespace, matching
// the project's ECS-component convention.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// ==================== M2: Spatial hash + steering ====================

/// <summary>
/// M2 — uniform-grid spatial hash of unit positions, rebuilt every tick by
/// <c>SpatialHashRebuildSystem</c>. The hash maps a packed integer cell key
/// (see <see cref="PackKey"/>) to the entities whose XZ centres lie in that
/// cell. Shared by <c>SteeringSystem</c> (S7) and -- in later milestones --
/// formation movement (S8).
///
/// CCD: <see cref="Map"/> is allocated with <see cref="Allocator.Persistent"/>
/// and disposed in <c>SpatialHashRebuildSystem.OnDestroy</c>. The map is
/// <c>Clear()</c>ed every tick and re-populated via a single-thread
/// <see cref="Unity.Jobs.IJob"/>; multi-thread inserts on a
/// <c>NativeParallelMultiHashMap</c> are not deterministic without
/// per-bucket locks (DR-2), so M2 takes the simpler single-thread route.
///
/// <see cref="CellSize"/> matches the unit-avoidance neighbourhood
/// (<see cref="DefaultCellSize"/> = 2 world units = ~4 swordsman radii).
/// <see cref="BucketCount"/> is the requested capacity at last rebuild.
/// </summary>
public struct NavSpatialHash : IComponentData
{
    /// <summary>Multimap from packed cell key to entity. Keys produced by
    /// <see cref="PackKey"/> against the unit's <c>LocalTransform.Position</c>.</summary>
    public NativeParallelMultiHashMap<int, Entity> Map;
    /// <summary>World units per spatial-hash cell. Matches the steering
    /// neighbour ring radius (3x3 = ~6 world units around each unit).</summary>
    public float CellSize;
    /// <summary>The map's capacity at the most recent rebuild. Used by the
    /// rebuild system to grow capacity when the unit count climbs.</summary>
    public int BucketCount;
    /// <summary>Bumped every tick the hash is rebuilt. Steering reads this
    /// to detect "did the hash get refreshed this tick" sanity-checks.</summary>
    public int Generation;

    /// <summary>Default cell size (m). Tuned for ~0.5 m radius units; the
    /// 3x3 neighbour ring covers a 6 m square which is ~10 unit diameters.</summary>
    public const float DefaultCellSize = 2f;

    /// <summary>
    /// Pack an (x, z) integer cell into a single int hash key. Uses the
    /// well-known "interleaved bit-mix" with prime offsets so adjacent
    /// cells aren't collision-clustered. Deterministic: identical (x, z)
    /// produces identical bits across every machine.
    /// </summary>
    public static int PackKey(int cellX, int cellZ)
    {
        unchecked
        {
            // Multiplicative + additive combine. XOR was the original
            // choice but XOR is symmetric: (cellX*A) ^ (cellZ*B) collides
            // between (a, b) and (-a, -b) when the two products are bit-
            // wise complements (caught by M2 SpatialHashBucketTests on the
            // 3x3 ring around the origin). Addition with unchecked wrap
            // preserves determinism, preserves purity, and the chosen
            // primes guarantee no collisions in any 3x3 ring of cells.
            return cellX * 73856093 + cellZ * 19349663;
        }
    }

    /// <summary>
    /// World-to-cell helper. Floors X/Z by <see cref="CellSize"/>. Matches
    /// the math used by the steering job and the populate job so all three
    /// agree on which bucket a unit lives in.
    /// </summary>
    public static void WorldToCell(in Unity.Mathematics.float3 worldPos, float cellSize,
        out int cellX, out int cellZ)
    {
        cellX = (int)Unity.Mathematics.math.floor(worldPos.x / cellSize);
        cellZ = (int)Unity.Mathematics.math.floor(worldPos.z / cellSize);
    }
}

/// <summary>
/// M2 -- per-unit final desired direction after steering force blending.
/// Written by <c>SteeringSystem</c>; preferred by <c>MovementSystem</c>
/// over <see cref="FlowDesiredDir"/> when present and valid. Falls back
/// to <see cref="FlowDesiredDir"/> -> NavMesh corridor when steering has
/// nothing to say (no neighbours + no flow yet).
///
/// Force-accumulation order is LOCKED at the writer (DR-1):
///   separation -> unit-avoidance -> obstacle-avoidance -> cohesion -> flow blend.
/// Consumers should not re-order or post-process this vector.
/// </summary>
public struct SteeringDesiredDir : IComponentData
{
    /// <summary>XZ-plane unit vector (y == 0). Length == 1 when
    /// <see cref="HasValue"/> != 0; length == 0 when no steering input
    /// applied this tick.</summary>
    public Unity.Mathematics.float3 Value;
    /// <summary>1 when <see cref="Value"/> holds a valid direction this tick.</summary>
    public byte HasValue;
}
