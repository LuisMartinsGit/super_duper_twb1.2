// Sect of Prosperity's building: the Treasury (docs/Design/Sects.md §4).
// While it stands, the faction's extractors earn more; it trains the
// Collector, recruits the Guildmaster and sells Charter of Works.
//
// Structure comes from SectBuilding — every sect building is the same shape
// (one per faction, needs a standing Temple to raise, trains its sect's unit
// and hero, sells its sect's research) and differs only in stats, tag and
// mesh. Religion.md §3.2.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    public static class Treasury
    {
        public const int PresentationID = 564;

        /// <summary>Building id — the key BuildingFactory, BuildingCosts and
        /// BuildingSizeConfig all agree on.</summary>
        public const string BuildingId = "Sect_Treasury";

        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => SectBuilding.Create<TreasuryTag>(em, BuildingId, PresentationID, position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => SectBuilding.Create<TreasuryTag>(ecb, BuildingId, PresentationID, position, faction);
    }
}
