// Cost.cs
// Fundamental resource cost structure used across all systems
// Location: Assets/Scripts/Core/Types/Cost.cs

namespace TheWaningBorder.Core
{
    /// <summary>
    /// Represents a resource cost for units, buildings, and technologies.
    /// Used for affordability checks and spending operations.
    /// </summary>
    public struct Cost
    {
        public int Supplies;
        public int Iron;
        public int Crystal;
        public int Veilsteel;
        public int Glow;

        /// <summary>
        /// Create a Cost with specified values. Unspecified values default to 0.
        /// </summary>
        public static Cost Of(int supplies = 0, int iron = 0, int crystal = 0, 
                              int veilsteel = 0, int glow = 0)
        {
            return new Cost
            {
                Supplies = supplies,
                Iron = iron,
                Crystal = crystal,
                Veilsteel = veilsteel,
                Glow = glow
            };
        }

        /// <summary>
        /// Returns true if all resource costs are zero.
        /// </summary>
        public bool IsZero => Supplies == 0 && Iron == 0 && Crystal == 0 && 
                              Veilsteel == 0 && Glow == 0;
        
        /// <summary>
        /// Get total "value" of resources (simple weighted sum for AI evaluation).
        /// </summary>
        public int TotalValue => Supplies + (Iron * 2) + (Crystal * 3) + 
                                 (Veilsteel * 5) + (Glow * 4);
        
        /// <summary>
        /// Add two costs together.
        /// </summary>
        public static Cost operator +(Cost a, Cost b)
        {
            return new Cost
            {
                Supplies = a.Supplies + b.Supplies,
                Iron = a.Iron + b.Iron,
                Crystal = a.Crystal + b.Crystal,
                Veilsteel = a.Veilsteel + b.Veilsteel,
                Glow = a.Glow + b.Glow
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
                Crystal = c.Crystal * multiplier,
                Veilsteel = c.Veilsteel * multiplier,
                Glow = c.Glow * multiplier
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
            if (Crystal > 0) parts.Add($"{Crystal} Crystal");
            if (Veilsteel > 0) parts.Add($"{Veilsteel} Veilsteel");
            if (Glow > 0) parts.Add($"{Glow} Glow");
            return parts.Count > 0 ? string.Join(", ", parts) : "Free";
        }
    }
}