// File: Assets/Scripts/Core/Settings/CulturedBuildingNames.cs
// task-071 Phase 1 (partial): culture-keyed display-name lookup for the four
// renamed Age-0 buildings (Hall, Barracks, ArcheryRange, Hut).
//
// Per doc §3.2 line 818-820, cultured renames keep the same base stats — only
// the multiplier ladder applies. So the rename is purely display + train-list
// restriction. This file provides the display half; train-list filtering is
// owned by Phase 2 (deferred).

namespace TheWaningBorder.Core.Settings
{
    /// <summary>
    /// Returns culture-aware display names for the four Age-0 renamed buildings.
    /// Generic Age-0 buildings keep their canonical name when culture is None.
    /// </summary>
    public static class CulturedBuildingNames
    {
        /// <summary>
        /// Look up the display name for a base building id + culture combination.
        /// Returns null if the building id isn't part of the rename layer (caller
        /// falls back to the canonical name).
        /// </summary>
        public static string GetDisplayName(string baseBuildingId, byte culture)
        {
            switch (baseBuildingId)
            {
                case "Hall":
                    return culture switch
                    {
                        Cultures.Alanthor => "Town Hall",
                        Cultures.Runai    => "Trader's Hall",
                        Cultures.Feraldis => "War Hall",
                        _                 => "Hall",
                    };
                case "Barracks":
                    return culture switch
                    {
                        Cultures.Alanthor => "Garrison",
                        Cultures.Runai    => "Route Guard",
                        Cultures.Feraldis => "Longhouse",
                        _                 => "Barracks",
                    };
                case "ArcheryRange":
                    return culture switch
                    {
                        Cultures.Alanthor => "Practice Range",
                        Cultures.Runai    => "Arrowyard",
                        Cultures.Feraldis => "Thrower Camp",
                        _                 => "Archery Range",
                    };
                case "Hut":
                    return culture switch
                    {
                        // Runai houses self-destruct at age-up (RunaiPopOverride
                        // is the substitute). Feraldis houses become raider-spawn
                        // buildings (task-066 Phase 3) but keep the "House" label.
                        Cultures.Alanthor => "House",
                        Cultures.Feraldis => "House",
                        _                 => "Hut",
                    };
                default:
                    return null;
            }
        }
    }
}
