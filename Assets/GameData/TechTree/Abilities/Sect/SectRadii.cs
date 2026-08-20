// File: Assets/GameData/TechTree/Abilities/Sect/SectRadii.cs
// The four casting radii — docs/Design/Sects.md section 2.
//
// Every targeted sect effect uses one of these four values. No sect power
// may author a bespoke radius: the four-value set is what lets a player
// learn one mental model ("small / medium / large") and apply it to all 36
// actives. Anything outside this file is a design bug.

namespace TheWaningBorder.Economy
{
    /// <summary>Named radius slot. Ordinal — reach grows with the value.</summary>
    public enum SectRadius : byte
    {
        Single = 0,
        Small  = 1,
        Medium = 2,
        Large  = 3,
    }

    public static class SectRadii
    {
        /// <summary>One entity. Cast systems treat this as a pick, not a circle;
        /// the metre value is the pick tolerance, not an area of effect.</summary>
        public const float Single = 1.5f;
        public const float Small  = 8f;
        public const float Medium = 15f;
        public const float Large  = 25f;

        public static float Metres(SectRadius r) => r switch
        {
            SectRadius.Small  => Small,
            SectRadius.Medium => Medium,
            SectRadius.Large  => Large,
            _                 => Single,
        };

        /// <summary>True when the power hits exactly one entity.</summary>
        public static bool IsSingleTarget(SectRadius r) => r == SectRadius.Single;

        public static string Label(SectRadius r) => r switch
        {
            SectRadius.Small  => "Small (8m)",
            SectRadius.Medium => "Medium (15m)",
            SectRadius.Large  => "Large (25m)",
            _                 => "Single Target",
        };

        /// <summary>
        /// Snap an arbitrary metre value to the nearest legal radius. Used only
        /// by the eight sects still on the pre-canon table — new powers must
        /// name their radius, not a number.
        /// </summary>
        public static SectRadius Snap(float metres)
        {
            if (metres <= 3f)  return SectRadius.Single;
            if (metres <= 11f) return SectRadius.Small;
            if (metres <= 20f) return SectRadius.Medium;
            return SectRadius.Large;
        }
    }
}
