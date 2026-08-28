// Sect of Renewal's building. An open-sided infirmary.
//
// Structure comes from SectBuilding — every sect building is the same shape
// (capped at 5, trains its sect's unit, sells its sect's research) and differs
// only in stats, tag and mesh. See docs/Design/Sects.md section 1.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    public static class MendingHall
    {
        public const int PresentationID = 562;

        /// <summary>Building id — the key BuildingFactory, BuildingCosts and
        /// BuildingSizeConfig all agree on.</summary>
        public const string BuildingId = "Sect_MendingHall";


        /// <summary>
        /// Damaged units that walk inside heal over time, and it is the only
    /// place a Scar Guard is trained.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => SectBuilding.Create<MendingHallTag>(em, BuildingId, PresentationID, position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => SectBuilding.Create<MendingHallTag>(ecb, BuildingId, PresentationID, position, faction);
    }
}
