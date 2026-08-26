// Sect of Fortitude's building. A squat windowless blockhouse.
//
// Structure comes from SectBuilding — every sect building is the same shape
// (capped at 5, trains its sect's unit, sells its sect's research) and differs
// only in stats, tag and mesh. See docs/Design/Sects.md section 1.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    public static class Stonehold
    {
        public const int PresentationID = 563;

        /// <summary>Building id — the key BuildingFactory, BuildingCosts and
        /// BuildingSizeConfig all agree on.</summary>
        public const string BuildingId = "Sect_Stonehold";

        private const float DefaultHp = 1800f;
        private const float DefaultLos = 12f;

        /// <summary>
        /// Built to be shot at: the highest HP of any non-Hall structure, and it
    /// blocks pathing like a wall.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => SectBuilding.Create<StoneholdTag>(em, BuildingId, PresentationID,
                                             DefaultHp, DefaultLos, position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => SectBuilding.Create<StoneholdTag>(ecb, BuildingId, PresentationID,
                                             DefaultHp, DefaultLos, position, faction);
    }
}
