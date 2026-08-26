// UnitCosts.cs
// What a unit costs to train, read from the tech tree.
//
// This lookup used to be EntityActionExtractor.GetUnitCost, in
// UI/Panels/EntityExtractors.Training.cs -- because a panel needed it first.
// It is not a presentation concern: it reads TechCatalog and returns a Cost,
// and its callers include the command router (charging for a train order) and
// the cancel-train refund path, neither of which should have to name the UI
// layer to find out what something costs.
//
// Sits beside BuildCosts, which answers the same question for buildings.

using TheWaningBorder.Core;

namespace TheWaningBorder.Data
{
    /// <summary>Training costs, by unit id.</summary>
    public static class UnitCosts
    {
        /// <summary>
        /// The cost of training <paramref name="unitId"/>, or default when the
        /// tech tree has no entry or no cost block for it. Default (all zero)
        /// rather than throwing: a missing cost must not stop a match, and the
        /// tech-tree validator is what reports the gap.
        /// </summary>
        public static Cost Get(string unitId)
        {
            if (TechCatalog.TryGetUnit(unitId, out var udef) && udef.cost != null)
            {
                return new Cost
                {
                    Supplies = udef.cost.Supplies,
                    Iron = udef.cost.Iron,
                    Veilstone = udef.cost.Veilstone,
                    Veilsteel = udef.cost.Veilsteel,
                };
            }
            return default;
        }
    }
}
