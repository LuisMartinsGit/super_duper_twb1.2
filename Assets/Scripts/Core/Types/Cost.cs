// Cost.cs
// Fundamental resource cost structure used across all systems

namespace TheWaningBorder.Core
{
    /// <summary>
    /// Represents a resource cost for units, buildings, and technologies.
    /// Used for affordability checks and spending operations.
    /// Three resources since 2026-11-09 (decision 42): veilsteel was folded
    /// into veilstone.
    /// </summary>
    public struct Cost
    {
        public int Supplies;
        public int Iron;
        public int Veilstone;

        /// <summary>
        /// Create a Cost with specified values. Unspecified values default to 0.
        /// </summary>
        public static Cost Of(int supplies = 0, int iron = 0, int veilstone = 0)
        {
            return new Cost
            {
                Supplies = supplies,
                Iron = iron,
                Veilstone = veilstone,
            };
        }

        /// <summary>
        /// Returns true if all resource costs are zero.
        /// </summary>
        public bool IsZero => Supplies == 0 && Iron == 0 && Veilstone == 0;

        /// <summary>
        /// Get total "value" of resources (simple weighted sum for AI evaluation).
        /// </summary>
        public int TotalValue => Supplies + (Iron * 2) + (Veilstone * 3);

        /// <summary>
        /// Add two costs together.
        /// </summary>
        public static Cost operator +(Cost a, Cost b)
        {
            return new Cost
            {
                Supplies = a.Supplies + b.Supplies,
                Iron = a.Iron + b.Iron,
                Veilstone = a.Veilstone + b.Veilstone,
            };
        }

        /// <summary>
        /// Multiply cost by a scalar (for batch costs).
        /// </summary>
        public static Cost operator *(Cost c, int multiplier)
        {
            return new Cost
            {
                Supplies = c.Supplies * multiplier,
                Iron = c.Iron * multiplier,
                Veilstone = c.Veilstone * multiplier,
            };
        }

        /// <summary>
        /// Returns a formatted string of non-zero costs.
        /// </summary>
        public override string ToString()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Supplies > 0) parts.Add($"{Supplies} Supplies");
            if (Iron > 0) parts.Add($"{Iron} Iron");
            if (Veilstone > 0) parts.Add($"{Veilstone} Veilstone");
            return parts.Count > 0 ? string.Join(", ", parts) : "Free";
        }
    }
}
