// NavComponents.Scheduler.cs
// Request scheduler (M6) and the M7 determinism replay log.
// Split out of NavComponents.cs (2026-08-12): that file had grown to 35
// unrelated declarations across seven milestones. Global namespace, matching
// the project's ECS-component convention.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// ==================== M6: Request scheduler + extended flow + formations ====================

/// <summary>
/// task-112 M6 -- one entry in the <see cref="NavRequestQueueSingleton"/>
/// pending-request queue. Created by callers (chiefly
/// <c>MoveCommandHelper</c>) when they want a unit to receive a path,
/// drained by <c>NavRequestSchedulerSystem</c> which sorts the queue
/// + coalesces duplicate (goal, profile) pairs + releases up to
/// <see cref="NavRequestQueueSingleton.DefaultMaxRequestsPerTick"/>
/// entries per tick to the pathfinder.
///
/// Sort order is LOCKED at (Priority asc, EnqueueTick asc,
/// Requester.Index asc) -- DR-12 / scheduler contract. Reordering
/// breaks lockstep determinism.
/// </summary>
public struct PendingNavRequest : System.IEquatable<PendingNavRequest>
{
    /// <summary>Entity that wants the path. Used as the deterministic
    /// tie-break key.</summary>
    public Entity Requester;
    /// <summary>Start cell on the cost field. Snapshotted at enqueue
    /// time so the scheduler can dispatch even if the unit moves
    /// before its slot comes up.</summary>
    public int2 StartCell;
    /// <summary>Goal cell on the cost field.</summary>
    public int2 GoalCell;
    /// <summary>Traversal profile hash (matches
    /// <see cref="NavPathRequest.ProfileHash"/>). Used by the
    /// coalescing key.</summary>
    public byte ProfileHash;
    /// <summary>Priority: lower = sooner. Sorted ascending. User-issued
    /// moves use <see cref="PriorityUser"/>; AI / re-routes use
    /// <see cref="PriorityNormal"/>; opportunistic / formation members
    /// use <see cref="PriorityFormation"/>.</summary>
    public byte Priority;
    /// <summary>Sim-tick the request was enqueued (matches
    /// <c>NavRequestQueueSingleton.CurrentTick</c> at enqueue).
    /// Secondary sort key.</summary>
    public uint EnqueueTick;
    /// <summary>Graph generation observed at enqueue. The scheduler
    /// drops requests whose generation is stale (CCD-5).</summary>
    public int Generation;

    /// <summary>Priority for direct user move orders.</summary>
    public const byte PriorityUser = 0;
    /// <summary>Priority for AI / re-issued requests.</summary>
    public const byte PriorityNormal = 1;
    /// <summary>Priority for formation members following a leader.</summary>
    public const byte PriorityFormation = 2;

    public bool Equals(PendingNavRequest other) =>
        Requester == other.Requester
        && StartCell.Equals(other.StartCell)
        && GoalCell.Equals(other.GoalCell)
        && ProfileHash == other.ProfileHash
        && Priority == other.Priority
        && EnqueueTick == other.EnqueueTick
        && Generation == other.Generation;

    public override bool Equals(object obj) => obj is PendingNavRequest r && Equals(r);

    public override int GetHashCode()
    {
        unchecked
        {
            int h = Requester.Index * 397;
            h = (h ^ GoalCell.x) * 397;
            h = (h ^ GoalCell.y) * 397;
            h = (h ^ ProfileHash) * 397;
            return h;
        }
    }
}

/// <summary>
/// task-112 M6 -- coalescing key for the scheduler. Two pending
/// requests with the same key map to the same A* run; the scheduler
/// solves once and broadcasts the result to every requester in the
/// equivalence class.
///
/// Hash is integer-only: packs goal cell + profile into a single int.
/// </summary>
public struct NavRequestCoalesceKey : System.IEquatable<NavRequestCoalesceKey>
{
    public int2 GoalCell;
    public byte ProfileHash;

    public bool Equals(NavRequestCoalesceKey other) =>
        GoalCell.Equals(other.GoalCell) && ProfileHash == other.ProfileHash;

    public override bool Equals(object obj) => obj is NavRequestCoalesceKey k && Equals(k);

    public override int GetHashCode()
    {
        unchecked
        {
            // Pack (gx, gz, profile) into a 32-bit hash. Goal coords
            // fit in 13 bits each at 8192-cell maps; profile in 8 bits.
            return (GoalCell.x * 73856093) ^ (GoalCell.y * 19349663) ^ (ProfileHash * 83492791);
        }
    }
}

/// <summary>
/// task-112 M6 -- singleton holding the per-tick navigation request
/// scheduler state. Owned by <c>NavRequestSchedulerSystem</c>;
/// allocated <see cref="Allocator.Persistent"/> + disposed in the
/// system's <c>OnDestroy</c>.
///
/// Producers (chiefly <c>MoveCommandHelper.Execute</c>) push entries
/// onto <see cref="Pending"/> via the helper on
/// <c>NavRequestQueueSingleton</c>; the scheduler drains the queue in
/// sorted order each tick, coalesces duplicate (goal, profile)
/// entries, and emits up to <see cref="MaxRequestsPerTick"/>
/// <see cref="NavPathRequest"/> components via ECB.
/// </summary>
public struct NavRequestQueueSingleton : IComponentData
{
    /// <summary>Pending request list. Sort order is locked at
    /// (Priority asc, EnqueueTick asc, Requester.Index asc); the
    /// scheduler enforces the order on each tick before dispatch.</summary>
    public NativeList<PendingNavRequest> Pending;
    /// <summary>Per-tick budget. Default
    /// <see cref="DefaultMaxRequestsPerTick"/>.</summary>
    public int MaxRequestsPerTick;
    /// <summary>Number of requests released this tick. Bumped during
    /// dispatch; reset on each <c>OnUpdate</c> entry.</summary>
    public int ReleasedThisTick;
    /// <summary>Monotonic sim-tick counter (incremented at scheduler
    /// <c>OnUpdate</c> entry). Used as the <see cref="PendingNavRequest.EnqueueTick"/>
    /// stamp and as the secondary sort key.</summary>
    public uint CurrentTick;

    /// <summary>Default per-tick release budget (DR-12). Sized larger
    /// than the M3 pathfinder budget so the scheduler can saturate the
    /// pathfinder on a mass-move tick.</summary>
    public const int DefaultMaxRequestsPerTick = 16;
}

// ==================== M7: Determinism replay log ====================

/// <summary>
/// task-112 M7 -- one recorded snapshot in the determinism replay log.
/// Uses integer MILLIMETRE coordinates (<see cref="PositionMillimeters"/>)
/// instead of floats so the byte-identical comparison can't be defeated
/// by float ULP drift across machines / Burst versions (DR-15). One
/// snapshot per (sim-tick, entity-index) pair; the log is a flat
/// append-only <see cref="NativeList{T}"/>.
///
/// Sort order: ascending <see cref="Tick"/>, then ascending
/// <see cref="EntityIndex"/> -- mirrors the order the recorder writes
/// them in (chunk-walk visits archetypes in stable order, the recorder
/// sorts within a tick by entity index before append).
/// </summary>
public struct UnitPositionSnapshot : System.IEquatable<UnitPositionSnapshot>
{
    /// <summary>Sim tick this snapshot was taken at (monotonic).</summary>
    public uint Tick;
    /// <summary>Entity index of the unit (Entity.Index). Stable within a
    /// world per the lockstep contract.</summary>
    public int EntityIndex;
    /// <summary>Position in MILLIMETRES (1mm = 0.001 world units). int3
    /// so the byte comparison is exact regardless of float
    /// representation. Range: +-2^31 mm = +- 2.14 million km, well
    /// beyond any plausible map size.</summary>
    public int3 PositionMillimeters;

    /// <summary>Conversion factor: 1 world unit = 1000 millimetres.</summary>
    public const int MillimetersPerUnit = 1000;

    /// <summary>Round-to-nearest float -> int conversion.</summary>
    public static int3 ToMillimeters(float3 worldPos) => new int3(
        (int)math.round(worldPos.x * MillimetersPerUnit),
        (int)math.round(worldPos.y * MillimetersPerUnit),
        (int)math.round(worldPos.z * MillimetersPerUnit));

    /// <summary>Inverse of <see cref="ToMillimeters"/>. Editor diagnostic
    /// only -- the sim never reads from the snapshot directly.</summary>
    public static float3 FromMillimeters(int3 mm) => new float3(
        mm.x / (float)MillimetersPerUnit,
        mm.y / (float)MillimetersPerUnit,
        mm.z / (float)MillimetersPerUnit);

    public bool Equals(UnitPositionSnapshot other) =>
        Tick == other.Tick
        && EntityIndex == other.EntityIndex
        && PositionMillimeters.Equals(other.PositionMillimeters);

    public override bool Equals(object obj) => obj is UnitPositionSnapshot s && Equals(s);

    public override int GetHashCode()
    {
        unchecked
        {
            int h = (int)Tick * 397;
            h = (h ^ EntityIndex) * 397;
            h = (h ^ PositionMillimeters.x) * 397;
            h = (h ^ PositionMillimeters.y) * 397;
            h = (h ^ PositionMillimeters.z) * 397;
            return h;
        }
    }
}

/// <summary>
/// task-112 M7 -- singleton owning the per-tick replay log. One entry
/// per (tick, unit) pair; the log grows monotonically while
/// <see cref="GameSettings.NavReplayMode"/> is <see cref="NavReplayMode.Record"/>
/// or <see cref="NavReplayMode.Replay"/>.
///
/// Allocator.Persistent (DR-17); disposed in
/// <c>DeterminismReplaySystem.OnDestroy</c>. The buffer is intentionally
/// NOT cleared on world tear-down via any other path -- the singleton's
/// OnDestroy is the only legitimate dispose site.
///
/// The recorder writes in (tick asc, entityIndex asc) order; the
/// replayer reads the same range it wrote previously and compares
/// byte-for-byte via <see cref="UnitPositionSnapshot.Equals"/>.
/// </summary>
public struct DeterminismReplayLog : IComponentData
{
    /// <summary>Append-only buffer of position snapshots. Allocator.Persistent.</summary>
    public NativeList<UnitPositionSnapshot> Log;
    /// <summary>Current tick the recorder will write into next (bumped at
    /// the end of each recorded tick). Replayer's cursor for the
    /// next comparison.</summary>
    public uint CurrentTick;
    /// <summary>Index into <see cref="Log"/> the replayer's next
    /// comparison starts at. Lets the comparator linear-scan a single
    /// tick's worth of entries instead of binary-searching the whole
    /// log.</summary>
    public int ReplayCursor;
    /// <summary>1 once at least one tick has been recorded. Lets the
    /// system distinguish "fresh log" from "log used for replay".</summary>
    public byte HasData;
    /// <summary>Bumped every divergence the replayer detects. 0 in the
    /// happy-path; > 0 means the sim has diverged from the recorded log
    /// and the editor should halt.</summary>
    public int DivergenceCount;
}
