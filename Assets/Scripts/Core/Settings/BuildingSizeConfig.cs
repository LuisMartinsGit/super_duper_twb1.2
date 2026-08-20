// BuildingSizeConfig.cs
// Central lookup table for grid-aligned building sizes.
// Canonical spec: docs/Design/Build_Grid.md
// Location: Assets/Scripts/Core/Settings/BuildingSizeConfig.cs

using Unity.Mathematics;

/// <summary>
/// Central lookup table for grid-aligned building sizes.
/// Width = X-axis, Height = Z-axis.
///
/// UNITS: <see cref="GetSize"/> returns METRES, which are also 1 m nav /
/// passability cells — that is what the <c>BuildingSize</c> component, the
/// placement validator, the cost-field stamps, the terrain flatten and the AI
/// clearance checks all consume, so it stays the primary accessor.
///
/// Footprints are AUTHORED against the 2 m build grid, so every value here is
/// EVEN and <see cref="GetCells"/> gives the authored cell count.
/// See <see cref="BuildGrid"/>.
///
/// DOUBLED 2026-08-13: every footprint here is twice what it was — buildings
/// read far too small against the units and the terrain. The grid itself is
/// unchanged at 2 m, so placement keeps its fine granularity; what changed is
/// that the smallest building is now 2 x 2 cells rather than 1 x 1. The
/// earlier "a Hut is exactly one grid cell" rule is therefore superseded: the
/// Hut is still the smallest building, but it spans four cells.
/// </summary>
public static class BuildingSizeConfig
{
    /// <summary>
    /// Get the footprint (width, height) in METRES for a building by its
    /// string ID. Always even — see the class remarks.
    /// </summary>
    public static int2 GetSize(string buildingId)
    {
        return buildingId switch
        {
            // ── 2 x 2 cells (4 x 4 m) ───────────────────────────────────
            // The smallest class. The Hut is the unit of the SIZE ladder,
            // though no longer of the grid — see the doubling note above.
            "Hut"               => new int2(4, 4),
            "GatherersHut"      => new int2(4, 4),

            // ── 4 x 4 cells (8 x 8 m) ───────────────────────────────────
            "Hall"              => new int2(8, 8),
            "ArcheryRange"      => new int2(8, 8),
            "ShrineOfRidan"     => new int2(8, 8),
            "VaultOfAlmierra"   => new int2(8, 8),

            // ── 5 x 5 cells (10 x 10 m) ─────────────────────────────────
            // One cell wider than the Hall class (2026-08-18): the Barracks
            // read too small for a unit-producing hall. Odd cell count, so
            // it snaps to a cell CENTRE rather than a boundary — that is
            // handled by BuildGrid.SnapAxis and needs nothing here.
            "Barracks"          => new int2(10, 10),

            // 7-sided cathedral. HALVED BACK 2026-08-17: the doubling had
            // given it a 16 x 16 class of its own and in play it dwarfed
            // everything — it now shares the Hall class, and the chapel ring
            // (TempleChapelRing.SlotRadius) and the chapel footprints halve
            // with the wall they dock against. docs/Design/Build_Grid.md
            "TempleOfRidan"     => new int2(8, 8),

            // ── 6 x 6 cells (12 x 12 m) ─────────────────────────────────
            "FiendstoneKeep"    => new int2(12, 12),

            // Walls — hub anchor only. Hubs snap to the grid at 4 x 4 cells;
            // the curtain segments between them stay FREEFORM and stamp their
            // own module footprint in AlanthorWall.CreateInstance. Keep in
            // step with AlanthorWall.HubWidth.
            "Alanthor_Wall"     => new int2(8, 8),

            // Alanthor culture
            "Alanthor_Smelter"  => new int2(8, 8),
            "Alanthor_Tower"    => new int2(4, 4),
            "Alanthor_SiegeYard"=> new int2(8, 8),
            "KingsCourt"        => new int2(8, 8),
            "Alanthor_RoyalStable" => new int2(8, 8),

            // Runai culture
            "Runai_Outpost"     => new int2(8, 8),
            "Runai_TradeHub"    => new int2(8, 8),
            "Runai_TradingPost" => new int2(4, 4),
            "ThessarasBazaar"   => new int2(12, 12),
            "Runai_SiegeWorkshop" => new int2(8, 8),
            "Runai_Vault"       => new int2(8, 8),
            "Runai_VeilsteelFoundry" => new int2(8, 8),

            // Feraldis culture
            "Feraldis_HuntingLodge"   => new int2(8, 8),
            "Feraldis_LoggingStation" => new int2(8, 8),
            "Feraldis_Longhouse"      => new int2(8, 8),
            "Feraldis_Tower"          => new int2(4, 4),
            "Feraldis_SiegeYard"      => new int2(8, 8),
            "Feraldis_Foundry"        => new int2(8, 8),
            "Feraldis_WarTotem"       => new int2(4, 4),
            "Feraldis_Pasture"        => new int2(8, 8),
            "Mine"                    => new int2(8, 8),

            // Sect buildings — one per sect, capped at 5 per faction.
            "Sect_Reliquary"          => new int2(8, 8),
            "Sect_MendingHall"        => new int2(8, 8),
            "Sect_Stonehold"          => new int2(8, 8),
            "Sect_Veilworks"          => new int2(8, 8),
            "Sect_MusterYard"         => new int2(8, 8),

            // Chapels (all sects) — generic Chapel_* prefix wildcard. The
            // temple-ring statues: halved with the Temple (2026-08-17) so
            // they keep their docked proportion against the smaller wall.
            _ when buildingId != null && buildingId.StartsWith("Chapel_") => new int2(2, 2),

            // The curse's well. A structure, not a node, so it gets 6 x 6
            // cells rather than the single cell every resource node takes.
            "BorderMainNode"         => new int2(12, 12),

            // Default
            _ => new int2(8, 8)
        };
    }

    /// <summary>
    /// The authored footprint in 2 m BUILD CELLS. This is the number the
    /// design table and the player-facing outline speak in — a Hut is 1 x 1.
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
