// HonourThyPledge.cs
// Identity constants for King Lexor's level-4 summon.
// Canon: docs/Design/Heroes.md §3.
//
// The card itself is authored data — HonourThyPledge.asset here, mirrored by
// the AbilityCatalog code seed — and PledgeArmy.cs does the conjuring. What
// lives here is only what BOTH of those need to agree on.

namespace TheWaningBorder.Entities
{
    public static class HonourThyPledge
    {
        /// <summary>The catalog name. Must match the asset's abilityName and
        /// the seed card, because AbilityAssignment resolves abilities by
        /// name.</summary>
        public const string AbilityName = "Honour thy Pledge";

        /// <summary>
        /// Hero level the ability unlocks at.
        ///
        /// Also the zero point for every scaling axis in PledgeArmy — soldiers,
        /// rank and duration are all expressed relative to it — so moving the
        /// unlock moves the whole ladder with it and the level-4 row of the
        /// design table stays the floor.
        /// </summary>
        public const int UnlockLevel = 4;
    }
}
