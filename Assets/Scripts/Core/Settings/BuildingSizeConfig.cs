// BuildingSizeConfig.cs
// Central lookup table for grid-aligned building sizes.
// Canonical spec: docs/Design/Build_Grid.md

using Unity.Mathematics;

/// <summary>
/// Grid-aligned building sizes. Width = X-axis, Height = Z-axis.
///
/// THE SO OWNS THE FOOTPRINT (2026-10-03, unification item 34). Every
/// building with a BuildingDefSO carries <c>footprintCells</c> — its size in
/// 2 m build cells, the unit docs/Design/Build_Grid.md speaks in — and this
/// class only converts it. The id switch that used to be the authority (one
/// row per building, a hand-kept twin of the asset) is gone; what remains
/// below sizes only the ids that have no BuildingDefSO at all.
///
/// UNITS: <see cref="GetSize"/> returns METRES, which are also 1 m nav /
/// passability cells — that is what the <c>BuildingSize</c> component, the
/// placement validator, the cost-field stamps, the terrain flatten and the AI
/// clearance checks all consume, so it stays the primary accessor.
/// </summary>
public static class BuildingSizeConfig
{
    /// <summary>
    /// Get the footprint (width, height) in METRES for a building by its
    /// string ID: its SO's footprintCells, in metres.
    /// </summary>
    public static int2 GetSize(string buildingId)
    {
        if (TechCatalog.TryGetFootprintCells(buildingId, out var cells))
            return ToMeters(new int2(cells.x, cells.y));
        return CodeSeededSize(buildingId);
    }

    /// <summary>
    /// The ids with no BuildingDefSO: the twelve chapels (Chapel_Sect_*,
    /// docked in the Temple ring) and the curse's well. Everything else is
    /// read from its asset — add a footprint THERE, not here.
    /// </summary>
    private static int2 CodeSeededSize(string buildingId)
    {
        if (buildingId != null && buildingId.StartsWith("Chapel_")) return new int2(2, 2);
        if (buildingId == "BorderMainNode") return new int2(12, 12);
        // An id nobody authored. 4 x 4 cells, the old default, so placement
        // still has a box to test; the SO is the fix.
        return new int2(8, 8);
    }

    /// <summary>
    /// The authored footprint in 2 m BUILD CELLS. This is the number the
    /// design table speaks in — a Hut is 2 x 2 cells (it was 1 x 1 before the
    /// 2026-08-13 doubling; the old figure survived here in prose long after
    /// the table moved).
    /// </summary>
    public static int2 GetCells(string buildingId) => ToCells(GetSize(buildingId));

    /// <summary>Metres -> build cells, rounding up so a footprint never
    /// straddles a cell edge.</summary>
    public static int2 ToCells(int2 sizeMeters) => new int2(
        math.max(1, (int)math.ceil(sizeMeters.x / BuildGrid.CellSize)),
        math.max(1, (int)math.ceil(sizeMeters.y / BuildGrid.CellSize)));

    /// <summary>Build cells -> metres.</summary>
    public static int2 ToMeters(int2 cells) => new int2(
        (int)(cells.x * BuildGrid.CellSize),
        (int)(cells.y * BuildGrid.CellSize));

    /// <summary>
    /// The footprint every single-cell thing uses — resource nodes, blight
    /// pockets, trees and scatter props. One build cell, in metres.
    /// </summary>
    public static int2 SingleCellSize => new int2((int)BuildGrid.CellSize, (int)BuildGrid.CellSize);

    /// <summary>
    /// Compute backward-compatible Radius from grid size.
    /// Returns max(width, height) / 2f to encompass the building footprint.
    /// </summary>
    public static float GetLegacyRadius(int2 size)
    {
        return math.max(size.x, size.y) / 2f;
    }
}
