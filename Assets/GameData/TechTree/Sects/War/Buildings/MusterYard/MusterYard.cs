// Sect of War's building. A stockade of training posts and armourers' racks.
//
// Structure comes from SectBuilding — every sect building is the same shape
// (capped at 5, trains its sect's unit, sells its sect's research) and differs
// only in stats, tag and mesh. See docs/Design/Sects.md section 1.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    public static class MusterYard
    {
        public const int PresentationID = 565;

        /// <summary>Building id — the key BuildingFactory, BuildingCosts and
        /// BuildingSizeConfig all agree on.</summary>
        public const string BuildingId = "Sect_MusterYard";


        /// <summary>
        /// A working yard, not a fortress: middling HP, good sightlines over
        /// the muster ground. Its real output is the per-battalion upgrade
        /// discount, which is faction-wide and does not stack
        /// (MusterYardDiscount).
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => SectBuilding.Create<MusterYardTag>(em, BuildingId, PresentationID, position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => SectBuilding.Create<MusterYardTag>(ecb, BuildingId, PresentationID, position, faction);
    }
}
