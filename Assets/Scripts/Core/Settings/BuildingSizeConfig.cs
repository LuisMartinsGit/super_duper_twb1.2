// BuildingSizeConfig.cs
// Central lookup table for grid-aligned building sizes.
// All values are in 1m grid cells. Max 5 per dimension.
// Location: Assets/Scripts/Core/Settings/BuildingSizeConfig.cs

using Unity.Mathematics;

/// <summary>
/// Central lookup table for grid-aligned building sizes.
/// Returns (width, height) in grid cells for each building ID.
/// Width = X-axis, Height = Z-axis. Max 5 per dimension.
/// </summary>
public static class BuildingSizeConfig
{
    /// <summary>
    /// Get the grid size (width, height) for a building by its string ID.
    /// </summary>
    public static int2 GetSize(string buildingId)
    {
        return buildingId switch
        {
            // Era 1 Core
            "Hall"              => new int2(4, 4),
            "Hut"               => new int2(3, 3),
            "GatherersHut"      => new int2(2, 2),
            "Barracks"          => new int2(3, 4),

            // Era 1 Advanced / Choice
            "ShrineOfRidan"     => new int2(3, 3),
            "TempleOfRidan"     => new int2(4, 4),
            "VaultOfAlmierra"   => new int2(4, 4),
            "FiendstoneKeep"    => new int2(5, 5),

            // Walls - special 1x1
            "Alanthor_Wall"     => new int2(1, 1),

            // Alanthor culture
            "Alanthor_Smelter"  => new int2(3, 3),
            "Alanthor_Tower"    => new int2(2, 2),
            "Alanthor_Garrison" => new int2(3, 4),
            "Alanthor_Stable"   => new int2(4, 3),
            "Alanthor_SiegeYard"=> new int2(3, 3),
            "KingsCourt"        => new int2(4, 4),
            "Alanthor_Crucible" => new int2(3, 3),

            // Runai culture
            "Runai_Outpost"     => new int2(3, 3),
            "Runai_TradeHub"    => new int2(3, 4),
            "ThessarasBazaar"   => new int2(5, 5),
            "Runai_SiegeWorkshop" => new int2(3, 3),
            "Runai_Vault"       => new int2(4, 4),
            "Runai_VeilsteelFoundry" => new int2(3, 3),

            // Feraldis culture
            "Feraldis_HuntingLodge"   => new int2(3, 3),
            "Feraldis_LoggingStation" => new int2(3, 3),
            "Feraldis_Longhouse"      => new int2(4, 3),
            "Feraldis_Tower"          => new int2(2, 2),
            "Feraldis_SiegeYard"      => new int2(3, 3),
            "Feraldis_Foundry"        => new int2(3, 3),

            // Chapels (all sects)
            _ when buildingId != null && buildingId.StartsWith("Chapel_") => new int2(2, 2),

            // Crystal nodes (natural, not player-built)
            "CrystalMainNode"         => new int2(5, 5),
            "CrystalEnforcementNode"  => new int2(2, 2),
            "CrystalResourceNode"     => new int2(2, 2),
            "CrystalRestorationNode"  => new int2(2, 2),
            "CrystalSuppressionNode"  => new int2(2, 2),
            "CrystalTurretNode"       => new int2(2, 2),

            // Default
            _ => new int2(3, 3)
        };
    }

    /// <summary>
    /// Compute backward-compatible Radius from grid size.
    /// Returns max(width, height) / 2f to encompass the building footprint.
    /// </summary>
    public static float GetLegacyRadius(int2 size)
    {
        return math.max(size.x, size.y) / 2f;
    }
}
