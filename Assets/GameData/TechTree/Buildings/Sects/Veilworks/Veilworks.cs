// File: Assets/GameData/TechTree/Buildings/Sects/Veilworks/Veilworks.cs
// Sect of Reclamation's building. A smelter for cursed matter.
//
// Structure comes from SectBuilding — every sect building is the same shape
// (capped at 5, trains its sect's unit, sells its sect's research) and differs
// only in stats, tag and mesh. See docs/Design/Sects.md section 1.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    public static class Veilworks
    {
        public const int PresentationID = 564;

        /// <summary>Building id — the key BuildingFactory, BuildingCosts and
        /// BuildingSizeConfig all agree on.</summary>
        public const string BuildingId = "Sect_Veilworks";

        private const float DefaultHp = 850f;
        private const float DefaultLos = 15f;

        /// <summary>
        /// The only building that may be raised ON cursed ground, and it takes
    /// no curse damage.
        /// </summary>
        public static Entity Create(EntityManager em, float3 position, Faction faction)
            => SectBuilding.Create<VeilworksTag>(em, BuildingId, PresentationID,
                                             DefaultHp, DefaultLos, position, faction);

        public static Entity Create(EntityCommandBuffer ecb, float3 position, Faction faction)
            => SectBuilding.Create<VeilworksTag>(ecb, BuildingId, PresentationID,
                                             DefaultHp, DefaultLos, position, faction);
    }
}
