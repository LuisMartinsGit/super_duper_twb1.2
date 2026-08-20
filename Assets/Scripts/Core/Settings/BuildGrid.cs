// BuildGrid.cs
// The map-wide 2 m build grid. Canonical spec: docs/Design/Build_Grid.md
// Location: Assets/Scripts/Core/Settings/BuildGrid.cs

using Unity.Mathematics;

/// <summary>
/// The single 2 m square grid every ground-occupying thing sits on —
/// buildings, resource nodes, curse structures, trees and scatter props.
///
/// Anchored at the WORLD ORIGIN, deliberately not at either pathing grid's
/// origin: snapping is then pure integer arithmetic on world coordinates,
/// map-independent, bootstrap-order-independent and bit-identical on every
/// lockstep client. The nav cost field and <see cref="PassabilityGrid"/> stay
/// at 1 m, so one build cell is exactly 2x2 of their cells.
///
/// Note the two units in play. Footprints are AUTHORED in cells
/// (<see cref="BuildingSizeConfig.GetCells"/>) but the <c>BuildingSize</c>
/// component, the placement validator, the nav stamps and the AI clearance
/// checks all speak METRES — which equal 1 m nav cells. Every metre footprint
/// is therefore even, and <see cref="Snap"/> takes metres.
/// </summary>
public static class BuildGrid
{
    /// <summary>Edge length of one build cell, in metres.</summary>
    public const float CellSize = 2f;

    /// <summary>Half a cell. Cell centres sit at odd metres.</summary>
    public const float HalfCell = CellSize * 0.5f;

    // ── Cell <-> world ──────────────────────────────────────────────────

    /// <summary>World XZ -> the build cell containing it.</summary>
    public static int2 WorldToCell(float3 world) => new int2(
        (int)math.floor(world.x / CellSize),
        (int)math.floor(world.z / CellSize));

    /// <summary>Build cell -> the world XZ of its centre.</summary>
    public static float2 CellCentre(int2 cell) => new float2(
        cell.x * CellSize + HalfCell,
        cell.y * CellSize + HalfCell);

    // ── Snapping ────────────────────────────────────────────────────────

    /// <summary>
    /// Snap a position so a footprint of <paramref name="sizeMeters"/> covers
    /// exactly a whole number of build cells.
    ///
    /// Y is passed through untouched — the build grid quantises ground plan
    /// only, and callers that want terrain height apply it themselves. That
    /// keeps this method free of terrain sampling, so it is safe to call from
    /// deterministic (lockstep) code paths.
    /// </summary>
    public static float3 Snap(float3 world, int2 sizeMeters)
    {
        return new float3(
            SnapAxis(world.x, sizeMeters.x),
            world.y,
            SnapAxis(world.z, sizeMeters.y));
    }

    /// <summary>
    /// Snap using the footprint registered for <paramref name="buildingId"/>.
    /// </summary>
    public static float3 Snap(float3 world, string buildingId)
        => Snap(world, BuildingSizeConfig.GetSize(buildingId));

    /// <summary>
    /// Snap to the centre of the containing cell — the single-cell case used
    /// by resource nodes, blight pockets, trees and props.
    /// </summary>
    public static float3 SnapToCellCentre(float3 world)
    {
        float2 c = CellCentre(WorldToCell(world));
        return new float3(c.x, world.y, c.y);
    }

    /// <summary>
    /// One axis of the snap. A footprint spanning <c>cells</c> build cells is
    /// centred on a cell CENTRE when that count is odd and on a cell BOUNDARY
    /// when it is even; either way the span lands on cell edges.
    /// </summary>
    private static float SnapAxis(float coord, int sizeMeters)
    {
        // Metres -> cells. Odd metre footprints cannot tile the grid; round up
        // rather than silently straddling a cell edge.
        int cells = math.max(1, (int)math.ceil(sizeMeters / CellSize));

        if ((cells & 1) == 1)
        {
            // Odd: centre on a cell centre.
            int cell = (int)math.floor(coord / CellSize);
            return cell * CellSize + HalfCell;
        }

        // Even: centre on a cell boundary.
        return math.round(coord / CellSize) * CellSize;
    }

    // ── Footprint helpers ───────────────────────────────────────────────

    /// <summary>
    /// The build cells a footprint covers once snapped, as
    /// (min cell, cell count). Used by outline drawing and by callers that
    /// need to iterate the occupied cells.
    /// </summary>
    public static void FootprintCells(float3 snappedCentre, int2 sizeMeters,
                                      out int2 minCell, out int2 cellCount)
    {
        cellCount = new int2(
            math.max(1, (int)math.ceil(sizeMeters.x / CellSize)),
            math.max(1, (int)math.ceil(sizeMeters.y / CellSize)));

        minCell = new int2(
            (int)math.round((snappedCentre.x - cellCount.x * HalfCell) / CellSize),
            (int)math.round((snappedCentre.z - cellCount.y * HalfCell) / CellSize));
    }

    /// <summary>
    /// True when the position is already grid-aligned for this footprint.
    /// Cheap guard for asserts and for skipping redundant re-snaps.
    /// </summary>
    public static bool IsSnapped(float3 world, int2 sizeMeters, float tolerance = 0.01f)
    {
        float3 s = Snap(world, sizeMeters);
        return math.abs(s.x - world.x) <= tolerance
            && math.abs(s.z - world.z) <= tolerance;
    }
}
